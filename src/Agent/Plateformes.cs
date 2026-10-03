namespace CryptoCrypt.Agent;

/// <summary>Plateforme d echange : chaque plateforme prise en charge implemente cette interface.</summary>
internal interface IPlateforme : IDisposable
{
    string Nom { get; }
    bool CleDisponible { get; }
    string Paire(string symbole, string devise);
    Task VerifierAsync(CancellationToken ct);
    Task<CoursProduit> CoursAsync(string paire, CancellationToken ct);
    Task<string> PasserOrdreAsync(OrdreMarche ordre, CancellationToken ct);
    Task<ResultatOrdre?> LireOrdreAsync(string id, CancellationToken ct);
}

internal sealed record CoursProduit(decimal Prix, decimal Increment);

internal sealed record OrdreMarche(string ClientOrderId, string Paire, bool Achat, decimal? MontantQuote, decimal? QuantiteBase);

internal sealed record ResultatOrdre(bool Termine, decimal Quantite, decimal Valeur, decimal PrixMoyen, decimal Frais, string Statut);

/// <summary>Cle d acces a une plateforme, stockee dans le coffre local.</summary>
internal sealed class CleApi
{
    public string Identifiant { get; set; } = "";
    public string Secret { get; set; } = "";
}

/// <summary>Une cle par plateforme ; la cle Coinbase des versions 0.1.x est reprise automatiquement.</summary>
internal sealed partial class Secrets
{
    public Dictionary<string, CleApi> Cles { get; set; } = new();

    public CleApi? CleDe(string plateforme) =>
        Cles.TryGetValue(plateforme, out var c)
            ? c
            : plateforme == "coinbase" && !string.IsNullOrEmpty(CoinbaseCle)
                ? new CleApi { Identifiant = CoinbaseNom ?? "", Secret = CoinbaseCle }
                : null;

    public List<string> AvecCle() => Plateformes.Prises.Where(p => CleDe(p) is not null).ToList();

    public bool AuMoinsUneCle() => AvecCle().Count > 0;
}

/// <summary>Registre des plateformes prises en charge par cette version de l agent.</summary>
internal static class Plateformes
{
    /// <summary>Annoncees au service a chaque appel (en-tete X-Agent-Plateformes).</summary>
    public static readonly string[] Prises = ["coinbase"];

    public static string Normaliser(string? nom) =>
        string.IsNullOrWhiteSpace(nom) ? "coinbase" : nom.Trim().ToLowerInvariant();

    public static bool PriseEnCharge(string nom) => Prises.Contains(nom);

    public static IPlateforme Creer(string nom, CleApi? cle) => nom switch
    {
        "coinbase" => new ClientCoinbase(cle?.Identifiant, cle?.Secret),
        _ => throw new AgentException($"Plateforme {nom} non prise en charge par cette version."),
    };

    public static CleApi LireCle(string nom, string contenu) => nom switch
    {
        "coinbase" => ClientCoinbase.LireCle(contenu),
        _ => throw new AgentException($"Plateforme {nom} non prise en charge par cette version."),
    };

    /// <summary>Un client par plateforme prise en charge ; une cle illisible est signalee et ignoree.</summary>
    public static Dictionary<string, IPlateforme> Ouvrir(Secrets secrets, ILogger journal)
    {
        var d = new Dictionary<string, IPlateforme>(StringComparer.OrdinalIgnoreCase);
        foreach (var nom in Prises)
        {
            try
            {
                d[nom] = Creer(nom, secrets.CleDe(nom));
            }
            catch (AgentException ex)
            {
                journal.LogWarning("{Plateforme} : {Message}", nom, ex.Message);
                d[nom] = Creer(nom, null);
            }
        }
        return d;
    }
}