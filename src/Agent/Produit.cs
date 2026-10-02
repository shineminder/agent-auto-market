namespace CryptoCrypt.Agent;

/// <summary>Identite du produit et elements de confiance compiles dans l executable.</summary>
internal static class Produit
{
    public const string Nom = "CryptoCrypt Agent";
    public const string NomService = "CryptoCryptAgent";
    public const string NomUnite = "cryptocrypt-agent";

    /// <summary>Depot GitHub des releases : la seule source de mise a jour acceptee.</summary>
    public const string Depot = "shineminder/agent-auto-market";

    /// <summary>Cle publique qui signe SHA256SUMS a chaque release (ECDSA P-256).</summary>
    public const string ClePubliqueRelease = """
-----BEGIN PUBLIC KEY-----
MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEVkGn/aLeGIhfx8XLyj9rGP/r8+iZ
M2DMM6lT/JOSqVYuJa9TYpSXMZv6R1bGBdqiD+/kN9HwJeETbhsO1Q2szw==
-----END PUBLIC KEY-----
""";

    public static string Version { get; } = typeof(Produit).Assembly.GetName().Version is { } v
        ? $"{v.Major}.{v.Minor}.{v.Build}"
        : "0.0.0";

    public static bool MisesAJourActivees =>
        ClePubliqueRelease.Contains("BEGIN PUBLIC KEY", StringComparison.Ordinal)
        && !Depot.StartsWith("__", StringComparison.Ordinal);
}
