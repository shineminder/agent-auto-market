using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CryptoCrypt.Agent;

/// <summary>
/// Acces a Coinbase Advanced Trade avec la cle de l utilisateur, qui ne quitte jamais cet appareil.
/// Chaque requete privee est signee par un jeton JWT ES256 valable deux minutes.
/// </summary>
internal sealed class ClientCoinbase : IDisposable
{
    private const string Hote = "api.coinbase.com";
    private readonly HttpClient _http = new() { BaseAddress = new Uri($"https://{Hote}/"), Timeout = TimeSpan.FromSeconds(30) };
    private readonly string? _nomCle;
    private readonly ECDsa? _cle;

    public ClientCoinbase(string? nomCle, string? clePem)
    {
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"cryptocrypt-agent/{Produit.Version}");
        if (string.IsNullOrEmpty(nomCle) || string.IsNullOrEmpty(clePem))
        {
            return;
        }
        var cle = ECDsa.Create();
        try
        {
            cle.ImportFromPem(clePem);
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            cle.Dispose();
            throw new AgentException("Cle Coinbase illisible : une cle ECDSA (ES256) est attendue. Recreez la cle en choisissant l algorithme ECDSA.");
        }
        if (cle.KeySize != 256)
        {
            cle.Dispose();
            throw new AgentException("Cle Coinbase inattendue : une cle ECDSA P-256 est attendue.");
        }
        _nomCle = nomCle;
        _cle = cle;
    }

    public bool CleDisponible => _cle is not null;

    /// <summary>Verifie que Coinbase accepte la cle (lecture d un compte).</summary>
    public async Task VerifierAsync(CancellationToken ct)
    {
        using var reponse = await EnvoyerSigneAsync(HttpMethod.Get, "/api/v3/brokerage/accounts?limit=1", null, ct).ConfigureAwait(false);
        if (!reponse.IsSuccessStatusCode)
        {
            throw new AgentException($"Coinbase refuse la cle ({(int)reponse.StatusCode}) : verifiez ses droits (Consulter, Negocier).");
        }
    }

    /// <summary>Cours et pas de quantite d un produit (donnee publique).</summary>
    public async Task<ProduitCoinbase> ProduitAsync(string produit, CancellationToken ct)
    {
        using var reponse = await _http.GetAsync("api/v3/brokerage/market/products/" + Uri.EscapeDataString(produit), ct).ConfigureAwait(false);
        if (!reponse.IsSuccessStatusCode)
        {
            throw new AgentException($"Cours indisponible pour {produit} ({(int)reponse.StatusCode}).", Raison.Temporaire);
        }
        var texte = await reponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize(texte, JsonContexte.Default.ProduitCoinbase)
            ?? throw new AgentException($"Cours illisible pour {produit}.", Raison.Temporaire);
    }

    /// <summary>Ordre au marche, execute immediatement ou annule.</summary>
    public async Task<string> PasserOrdreAsync(string clientOrderId, string produit, string cote, string? montantQuote, string? quantiteBase, CancellationToken ct)
    {
        var ordre = new OrdreCoinbase
        {
            ClientOrderId = clientOrderId,
            ProductId = produit,
            Side = cote,
            OrderConfiguration = new ConfigurationOrdre
            {
                MarketMarketIoc = new MarcheImmediat { QuoteSize = montantQuote, BaseSize = quantiteBase },
            },
        };
        var corps = JsonSerializer.Serialize(ordre, JsonContexte.Default.OrdreCoinbase);
        using var reponse = await EnvoyerSigneAsync(HttpMethod.Post, "/api/v3/brokerage/orders", corps, ct).ConfigureAwait(false);
        var texte = await reponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        ReponseOrdreCoinbase? r = null;
        try
        {
            r = JsonSerializer.Deserialize(texte, JsonContexte.Default.ReponseOrdreCoinbase);
        }
        catch (JsonException)
        {
            // reponse non conforme : traitee ci-dessous
        }
        if (!reponse.IsSuccessStatusCode || r is not { Success: true, SuccessResponse.OrderId: { Length: > 0 } id })
        {
            var motif = r?.ErrorResponse?.Message ?? r?.ErrorResponse?.PreviewFailureReason ?? r?.ErrorResponse?.Error ?? $"HTTP {(int)reponse.StatusCode}";
            throw new AgentException("Ordre refuse par Coinbase : " + motif);
        }
        return id;
    }

    public async Task<OrdreDetail?> LireOrdreAsync(string orderId, CancellationToken ct)
    {
        using var reponse = await EnvoyerSigneAsync(HttpMethod.Get, "/api/v3/brokerage/orders/historical/" + Uri.EscapeDataString(orderId), null, ct).ConfigureAwait(false);
        if (!reponse.IsSuccessStatusCode)
        {
            return null;
        }
        var texte = await reponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize(texte, JsonContexte.Default.ReponseLectureOrdre)?.Order;
    }

    private async Task<HttpResponseMessage> EnvoyerSigneAsync(HttpMethod methode, string chemin, string? corps, CancellationToken ct)
    {
        if (_cle is null || _nomCle is null)
        {
            throw new AgentException("Cle Coinbase absente sur cet appareil.");
        }
        using var requete = new HttpRequestMessage(methode, chemin.TrimStart('/'));
        requete.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Jwt(methode.Method, chemin.Split('?', 2)[0]));
        if (corps is not null)
        {
            requete.Content = new StringContent(corps, Encoding.UTF8, "application/json");
        }
        return await _http.SendAsync(requete, ct).ConfigureAwait(false);
    }

    private string Jwt(string methode, string chemin)
    {
        var maintenant = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var nonce = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        var entete = Json(w =>
        {
            w.WriteString("alg", "ES256");
            w.WriteString("kid", _nomCle);
            w.WriteString("nonce", nonce);
            w.WriteString("typ", "JWT");
        });
        var charge = Json(w =>
        {
            w.WriteString("sub", _nomCle);
            w.WriteString("iss", "cdp");
            w.WriteNumber("nbf", maintenant);
            w.WriteNumber("exp", maintenant + 120);
            w.WriteString("uri", $"{methode} {Hote}{chemin}");
        });
        var aSigner = Base64Url(entete) + "." + Base64Url(charge);
        var signature = _cle!.SignData(Encoding.ASCII.GetBytes(aSigner), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return aSigner + "." + Base64Url(signature);
    }

    private static byte[] Json(Action<Utf8JsonWriter> ecrire)
    {
        using var flux = new MemoryStream();
        using (var w = new Utf8JsonWriter(flux))
        {
            w.WriteStartObject();
            ecrire(w);
            w.WriteEndObject();
        }
        return flux.ToArray();
    }

    private static string Base64Url(byte[] octets) =>
        Convert.ToBase64String(octets).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public void Dispose()
    {
        _cle?.Dispose();
        _http.Dispose();
    }
}
