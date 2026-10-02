using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace CryptoCrypt.Agent;

/// <summary>Client de l API du service. Toutes les connexions sont sortantes.</summary>
internal sealed class ClientServeur : IDisposable
{
    private readonly HttpClient _http;

    public ClientServeur(ConfigAgent config, string? jeton)
    {
        if (!Uri.TryCreate(config.Serveur.TrimEnd('/') + "/api/agent/v1/", UriKind.Absolute, out var adresse))
        {
            throw new AgentException("Adresse du serveur invalide.");
        }
        if (adresse.Scheme != Uri.UriSchemeHttps && !config.AutoriserHttp)
        {
            throw new AgentException("Le serveur doit etre joint en HTTPS.");
        }
        _http = new HttpClient { BaseAddress = adresse, Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"cryptocrypt-agent/{Produit.Version}");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("X-Agent-Version", Produit.Version);
        _http.DefaultRequestHeaders.TryAddWithoutValidation("X-Agent-Os", Systeme.Description());
        if (!string.IsNullOrEmpty(jeton))
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jeton);
        }
    }

    public Task<ReponseAppairage?> AppairerAsync(DemandeAppairage d, CancellationToken ct) =>
        EnvoyerAsync(HttpMethod.Post, "appairer", Corps(d, JsonContexte.Default.DemandeAppairage), JsonContexte.Default.ReponseAppairage, ct);

    public Task<ReponseEtat?> EtatAsync(CancellationToken ct) =>
        EnvoyerAsync(HttpMethod.Get, "etat", null, JsonContexte.Default.ReponseEtat, ct);

    public Task<ReponseSignaux?> SignauxAsync(CancellationToken ct) =>
        EnvoyerAsync(HttpMethod.Get, "signaux", null, JsonContexte.Default.ReponseSignaux, ct);

    public Task<ReponseAutorisation?> AutoriserAsync(DemandeAutorisation d, CancellationToken ct) =>
        EnvoyerAsync(HttpMethod.Post, "autoriser", Corps(d, JsonContexte.Default.DemandeAutorisation), JsonContexte.Default.ReponseAutorisation, ct);

    public Task<ReponseEtatAutorisation?> EtatAutorisationAsync(long id, CancellationToken ct) =>
        EnvoyerAsync(HttpMethod.Get, $"autorisation/{id}", null, JsonContexte.Default.ReponseEtatAutorisation, ct);

    public Task<ReponseExecution?> CompteRenduAsync(CompteRendu c, CancellationToken ct) =>
        EnvoyerAsync(HttpMethod.Post, "executions", Corps(c, JsonContexte.Default.CompteRendu), JsonContexte.Default.ReponseExecution, ct);

    public Task<ReponseAppairage?> RenouvelerJetonAsync(CancellationToken ct) =>
        EnvoyerAsync(HttpMethod.Post, "jeton/renouveler", new StringContent("{}", Encoding.UTF8, "application/json"), JsonContexte.Default.ReponseAppairage, ct);

    private static StringContent Corps<T>(T valeur, JsonTypeInfo<T> info) =>
        new(JsonSerializer.Serialize(valeur, info), Encoding.UTF8, "application/json");

    private async Task<TR?> EnvoyerAsync<TR>(HttpMethod methode, string chemin, HttpContent? corps, JsonTypeInfo<TR> info, CancellationToken ct)
        where TR : class
    {
        using var requete = new HttpRequestMessage(methode, chemin) { Content = corps };
        using var reponse = await _http.SendAsync(requete, ct).ConfigureAwait(false);
        var code = (int)reponse.StatusCode;
        var texte = await reponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (code == 401)
        {
            throw new AgentException("Jeton refuse par le service : appareil revoque ou jeton expire. Refaites l appairage.", Raison.JetonInvalide);
        }
        if (code == 429)
        {
            throw new AgentException("Trop de requetes : nouvel essai au prochain cycle.", Raison.Temporaire);
        }
        if (code >= 500)
        {
            throw new AgentException($"Service indisponible ({code}).", Raison.Temporaire);
        }
        if (code == 403 && texte.Contains("\"erreur\"", StringComparison.Ordinal))
        {
            throw new AgentException("Acces refuse par le service : " + (texte.Length > 200 ? texte[..200] : texte));
        }
        if (code is 200 or 201 or 403 or 404 or 422)
        {
            try
            {
                return JsonSerializer.Deserialize(texte, info);
            }
            catch (JsonException)
            {
                // reponse non conforme : traitee ci-dessous
            }
        }
        throw new AgentException($"Reponse inattendue du service ({code}).", Raison.Temporaire);
    }

    public void Dispose() => _http.Dispose();
}
