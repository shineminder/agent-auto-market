namespace CryptoCrypt.Agent;

internal enum Raison
{
    Generale,
    Temporaire,
    JetonInvalide,
}

/// <summary>Erreur attendue, au message lisible par l utilisateur.</summary>
internal sealed class AgentException(string message, Raison raison = Raison.Generale) : Exception(message)
{
    public Raison Raison { get; } = raison;
}
