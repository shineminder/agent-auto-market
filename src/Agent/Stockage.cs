using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace CryptoCrypt.Agent;

internal static class Chemins
{
    public static string Donnees { get; } = Environment.GetEnvironmentVariable("CCAGENT_HOME") is { Length: > 0 } d
        ? d
        : OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CryptoCryptAgent")
            : "/var/lib/cryptocrypt-agent";

    public static string Config => Path.Combine(Donnees, "config.json");
    public static string Etat => Path.Combine(Donnees, "state.json");
    public static string Secrets => Path.Combine(Donnees, OperatingSystem.IsWindows() ? "secrets.bin" : "secrets.json");
}

internal static class Stockage
{
    public static ConfigAgent LireConfig() => Lire(Chemins.Config, JsonContexte.Default.ConfigAgent) ?? new ConfigAgent();

    public static void EcrireConfig(ConfigAgent c) =>
        Ecrire(Chemins.Config, JsonSerializer.SerializeToUtf8Bytes(c, JsonContexte.Default.ConfigAgent));

    public static EtatLocal LireEtat() => Lire(Chemins.Etat, JsonContexte.Default.EtatLocal) ?? new EtatLocal();

    public static void EcrireEtat(EtatLocal e) =>
        Ecrire(Chemins.Etat, JsonSerializer.SerializeToUtf8Bytes(e, JsonContexte.Default.EtatLocal));

    private static T? Lire<T>(string chemin, JsonTypeInfo<T> info) where T : class =>
        File.Exists(chemin) ? JsonSerializer.Deserialize(File.ReadAllBytes(chemin), info) : null;

    /// <summary>Ecriture atomique ; sous Linux, fichier lisible par le seul compte de l agent.</summary>
    internal static void Ecrire(string chemin, byte[] octets)
    {
        Directory.CreateDirectory(Chemins.Donnees);
        var temporaire = chemin + ".tmp";
        File.WriteAllBytes(temporaire, octets);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(temporaire, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        File.Move(temporaire, chemin, true);
    }
}

/// <summary>
/// Secrets de l appareil : jeton du service et cle Coinbase.
/// Windows : chiffres par la protection des donnees Windows, dossier reserve au compte du service.
/// Linux : fichier 0600 de l utilisateur systeme de l agent.
/// </summary>
internal static class CoffreSecrets
{
    private static readonly byte[] Entropie = "CryptoCryptAgent/secrets/v1"u8.ToArray();

    public static Secrets Lire()
    {
        if (!File.Exists(Chemins.Secrets))
        {
            return new Secrets();
        }
        var octets = File.ReadAllBytes(Chemins.Secrets);
        if (OperatingSystem.IsWindows())
        {
            octets = ProtectedData.Unprotect(octets, Entropie, DataProtectionScope.LocalMachine);
        }
        try
        {
            return JsonSerializer.Deserialize(octets, JsonContexte.Default.Secrets) ?? new Secrets();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(octets);
        }
    }

    public static void Ecrire(Secrets s)
    {
        var clair = JsonSerializer.SerializeToUtf8Bytes(s, JsonContexte.Default.Secrets);
        var aEcrire = clair;
        if (OperatingSystem.IsWindows())
        {
            aEcrire = ProtectedData.Protect(clair, Entropie, DataProtectionScope.LocalMachine);
            CryptographicOperations.ZeroMemory(clair);
        }
        Stockage.Ecrire(Chemins.Secrets, aEcrire);
        CryptographicOperations.ZeroMemory(aEcrire);
    }
}
