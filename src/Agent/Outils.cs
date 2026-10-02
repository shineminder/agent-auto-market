using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace CryptoCrypt.Agent;

internal static partial class Versions
{
    [GeneratedRegex(@"^\d{1,3}\.\d{1,3}\.\d{1,4}$")]
    private static partial Regex Motif();

    public static bool Valide(string? v) => v is not null && Motif().IsMatch(v);

    public static bool EstPlusRecente(string? candidate, string actuelle) =>
        Valide(candidate)
        && Version.TryParse(candidate, out var c)
        && Version.TryParse(actuelle, out var a)
        && c > a;
}

internal static class Systeme
{
    public static string Description()
    {
        var d = $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})";
        return d.Length > 120 ? d[..120] : d;
    }

    /// <summary>Cible de l executable publie. Windows ARM execute la version x64.</summary>
    public static string Rid() => OperatingSystem.IsWindows()
        ? "win-x64"
        : RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "linux-arm64" : "linux-x64";
}

internal static class Decimales
{
    public static decimal Lire(string? s) =>
        decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : 0m;

    public static string Ecrire(decimal d) => d.ToString("0.##########", CultureInfo.InvariantCulture);

    public static decimal ArrondirInferieur(decimal valeur, string? increment)
    {
        var pas = Lire(increment);
        return pas > 0 ? Math.Floor(valeur / pas) * pas : Math.Round(valeur, 8, MidpointRounding.ToZero);
    }
}

/// <summary>Plafonds fixes par l utilisateur sur sa machine, appliques avant toute demande au service.</summary>
internal static class PlafondsLocaux
{
    public static string? Refus(ConfigAgent c, EtatLocal e, SignalLocal s, bool simulation)
    {
        if (!string.Equals(s.Devise, "USD", StringComparison.OrdinalIgnoreCase))
        {
            return $"devise {s.Devise} non prise en charge";
        }
        if (s.Action is not ("acheter" or "vendre"))
        {
            return $"action {s.Action} inconnue";
        }
        if (c.Actifs.Count > 0 && !c.Actifs.Contains(s.Symbol, StringComparer.OrdinalIgnoreCase))
        {
            return $"actif {s.Symbol} non autorise sur cette machine";
        }
        if (s.Montant <= 0)
        {
            return "montant invalide";
        }
        if (s.Montant > c.PlafondOrdreUsd)
        {
            return $"montant {Decimales.Ecrire(s.Montant)} USD au-dela du plafond local par ordre ({Decimales.Ecrire(c.PlafondOrdreUsd)} USD)";
        }
        if (!simulation && e.EngageMoisUsd + s.Montant > c.PlafondMoisUsd)
        {
            return $"plafond local mensuel atteint ({Decimales.Ecrire(c.PlafondMoisUsd)} USD)";
        }
        return null;
    }
}
