using System.Text.Json.Serialization;

namespace CryptoCrypt.Agent;

// ---------- Fichiers locaux ----------

internal sealed class ConfigAgent
{
    public string Serveur { get; set; } = "";
    public bool AutoriserHttp { get; set; }
    public int IntervalleSecondes { get; set; } = 60;
    public decimal PlafondOrdreUsd { get; set; } = 100m;
    public decimal PlafondMoisUsd { get; set; } = 500m;
    public List<string> Actifs { get; set; } = [];
    public bool MisesAJourAuto { get; set; } = true;
}

internal sealed partial class Secrets
{
    public string? Jeton { get; set; }
    public string? CoinbaseNom { get; set; }
    public string? CoinbaseCle { get; set; }
}

internal sealed class EtatLocal
{
    public string Mois { get; set; } = "";
    public decimal EngageMoisUsd { get; set; }
    public List<SignalLocal> EnAttente { get; set; } = [];
}

internal static class Etapes
{
    public const string Nouveau = "nouveau";
    public const string AttenteConfirmation = "attente_confirmation";
    public const string Envoi = "envoi";
}

internal sealed class SignalLocal
{
    public long Id { get; set; }
    public string Action { get; set; } = "";
    public string Symbol { get; set; } = "";
    public string? Plateforme { get; set; }
    public decimal Montant { get; set; }
    public string Devise { get; set; } = "USD";
    public decimal? PrixLimite { get; set; }
    public DateTimeOffset? ExpireLe { get; set; }
    public string? Correlation { get; set; }
    public string Etape { get; set; } = Etapes.Nouveau;
    public long? Autorisation { get; set; }
    public string? ClientOrderId { get; set; }
    public string? OrderId { get; set; }
}

// ---------- API du service ----------

internal sealed class DemandeAppairage
{
    public string Code { get; set; } = "";
    public string Nom { get; set; } = "";
    public string Type { get; set; } = "agent";
    public string Systeme { get; set; } = "";
}

internal sealed class ReponseAppairage
{
    public string? Jeton { get; set; }
    public long AppareilId { get; set; }
    public long AgentId { get; set; }
    public string? ExpireLe { get; set; }
}

internal sealed class AgentInfo
{
    public long Id { get; set; }
    public string? Nom { get; set; }
    public string? Etat { get; set; }
}

internal sealed class ActifInfo
{
    public string? Symbol { get; set; }
    public string? Plateforme { get; set; }
    public string? Paire { get; set; }
}

internal sealed class MiseAJourInfo
{
    public string? Version { get; set; }
}

internal sealed class ReponseEtat
{
    public AgentInfo? Agent { get; set; }
    public bool Simulation { get; set; }
    public List<ActifInfo> Actifs { get; set; } = [];
    public string? JetonExpireLe { get; set; }
    public MiseAJourInfo? MiseAJour { get; set; }
    public string? Adresse { get; set; }
}

internal sealed class SignalApi
{
    public long Id { get; set; }
    public string? Action { get; set; }
    public string? Symbol { get; set; }
    public string? Plateforme { get; set; }
    public decimal Montant { get; set; }
    public string? Devise { get; set; }
    public decimal? PrixLimite { get; set; }
    public DateTimeOffset? ExpireLe { get; set; }
    public string? Correlation { get; set; }
}

internal sealed class ReponseSignaux
{
    public List<SignalApi> Signaux { get; set; } = [];
}

internal sealed class DemandeAutorisation
{
    public long? SignalId { get; set; }
    public string Action { get; set; } = "";
    public string Symbol { get; set; } = "";
    public string? Plateforme { get; set; }
    public decimal Montant { get; set; }
    public string Devise { get; set; } = "USD";
    public decimal? Prix { get; set; }
    public string? Correlation { get; set; }
}

internal sealed class ReponseAutorisation
{
    public bool Autorise { get; set; }
    public bool Confirmation { get; set; }
    public long? Autorisation { get; set; }
    public string? Motif { get; set; }
    public string? Message { get; set; }
}

internal sealed class ReponseEtatAutorisation
{
    public long Autorisation { get; set; }
    public string? Etat { get; set; }
    public bool Utilisable { get; set; }
}

internal sealed class CompteRendu
{
    public string IdempotencyKey { get; set; } = "";
    public long Autorisation { get; set; }
    public string Action { get; set; } = "";
    public string Symbol { get; set; } = "";
    public string? Plateforme { get; set; }
    public decimal Montant { get; set; }
    public string Devise { get; set; } = "USD";
    public decimal? Quantite { get; set; }
    public decimal? Prix { get; set; }
    public decimal? Frais { get; set; }
    public long? SignalId { get; set; }
    public string Etat { get; set; } = "reussie";
    public string? Message { get; set; }
    public string? Correlation { get; set; }
}

internal sealed class ReponseExecution
{
    public bool DejaEnregistre { get; set; }
    public long? Execution { get; set; }
    public bool Refuse { get; set; }
    public string? Motif { get; set; }
    public string? Message { get; set; }
}

// ---------- Coinbase Advanced Trade ----------

internal sealed class MarcheImmediat
{
    public string? QuoteSize { get; set; }
    public string? BaseSize { get; set; }
}

internal sealed class ConfigurationOrdre
{
    public MarcheImmediat? MarketMarketIoc { get; set; }
}

internal sealed class OrdreCoinbase
{
    public string ClientOrderId { get; set; } = "";
    public string ProductId { get; set; } = "";
    public string Side { get; set; } = "";
    public ConfigurationOrdre OrderConfiguration { get; set; } = new();
}

internal sealed class SuccesOrdre
{
    public string? OrderId { get; set; }
}

internal sealed class ErreurOrdre
{
    public string? Error { get; set; }
    public string? Message { get; set; }
    public string? PreviewFailureReason { get; set; }
}

internal sealed class ReponseOrdreCoinbase
{
    public bool Success { get; set; }
    public SuccesOrdre? SuccessResponse { get; set; }
    public ErreurOrdre? ErrorResponse { get; set; }
}

internal sealed class OrdreDetail
{
    public string? Status { get; set; }
    public string? FilledSize { get; set; }
    public string? FilledValue { get; set; }
    public string? AverageFilledPrice { get; set; }
    public string? TotalFees { get; set; }
}

internal sealed class ReponseLectureOrdre
{
    public OrdreDetail? Order { get; set; }
}

internal sealed class ProduitCoinbase
{
    public string? Price { get; set; }
    public string? BaseIncrement { get; set; }
}

// ---------- Serialisation sans reflexion (compilation native) ----------

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    NumberHandling = JsonNumberHandling.AllowReadingFromString)]
[JsonSerializable(typeof(ConfigAgent))]
[JsonSerializable(typeof(Secrets))]
[JsonSerializable(typeof(EtatLocal))]
[JsonSerializable(typeof(DemandeAppairage))]
[JsonSerializable(typeof(ReponseAppairage))]
[JsonSerializable(typeof(ReponseEtat))]
[JsonSerializable(typeof(ReponseSignaux))]
[JsonSerializable(typeof(DemandeAutorisation))]
[JsonSerializable(typeof(ReponseAutorisation))]
[JsonSerializable(typeof(ReponseEtatAutorisation))]
[JsonSerializable(typeof(CompteRendu))]
[JsonSerializable(typeof(ReponseExecution))]
[JsonSerializable(typeof(OrdreCoinbase))]
[JsonSerializable(typeof(ReponseOrdreCoinbase))]
[JsonSerializable(typeof(ReponseLectureOrdre))]
[JsonSerializable(typeof(ProduitCoinbase))]
internal sealed partial class JsonContexte : JsonSerializerContext
{
}
