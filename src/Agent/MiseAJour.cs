using System.Security.Cryptography;
using System.Text;

namespace CryptoCrypt.Agent;

/// <summary>
/// Mise a jour de l agent : uniquement depuis les releases du depot, apres verification de la
/// signature de SHA256SUMS (cle compilee dans l executable) puis de l empreinte du fichier.
/// </summary>
internal static class MiseAJour
{
    public const int CodeRedemarrage = 75;

    public static async Task InstallerAsync(string version, CancellationToken ct)
    {
        if (!Versions.Valide(version))
        {
            throw new AgentException("Version demandee invalide.");
        }
        if (!Produit.MisesAJourActivees)
        {
            throw new AgentException("Mises a jour desactivees : cle de release absente de cette version.");
        }

        var fichier = OperatingSystem.IsWindows() ? $"cc-agent-{Systeme.Rid()}.exe" : $"cc-agent-{Systeme.Rid()}";
        var racine = $"https://github.com/{Produit.Depot}/releases/download/v{version}/";
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"cryptocrypt-agent/{Produit.Version}");

        var sommes = await http.GetByteArrayAsync(racine + "SHA256SUMS", ct).ConfigureAwait(false);
        var signature = await http.GetByteArrayAsync(racine + "SHA256SUMS.sig", ct).ConfigureAwait(false);
        using (var cle = ECDsa.Create())
        {
            cle.ImportFromPem(Produit.ClePubliqueRelease);
            if (!cle.VerifyData(sommes, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence))
            {
                throw new AgentException("Signature de la release invalide : mise a jour refusee.");
            }
        }

        var attendu = Empreinte(Encoding.UTF8.GetString(sommes), fichier)
            ?? throw new AgentException($"Empreinte absente pour {fichier}.");
        var executable = Environment.ProcessPath ?? throw new AgentException("Emplacement de l executable inconnu.");
        var nouveau = executable + ".new";

        await using (var source = await http.GetStreamAsync(racine + fichier, ct).ConfigureAwait(false))
        await using (var cible = File.Create(nouveau))
        {
            await source.CopyToAsync(cible, ct).ConfigureAwait(false);
        }

        string obtenu;
        await using (var lecture = File.OpenRead(nouveau))
        {
            obtenu = Convert.ToHexStringLower(await SHA256.HashDataAsync(lecture, ct).ConfigureAwait(false));
        }
        if (!string.Equals(obtenu, attendu, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(nouveau);
            throw new AgentException("Empreinte du fichier telecharge invalide : mise a jour refusee.");
        }

        var ancien = executable + ".old";
        if (OperatingSystem.IsWindows())
        {
            File.Move(executable, ancien, true);
            File.Move(nouveau, executable);
        }
        else
        {
            File.SetUnixFileMode(nouveau,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            File.Copy(executable, ancien, true);
            File.Move(nouveau, executable, true);
        }
    }

    private static string? Empreinte(string sommes, string fichier)
    {
        foreach (var ligne in sommes.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parties = ligne.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parties.Length == 2 && parties[1].TrimStart('*') == fichier)
            {
                return parties[0];
            }
        }
        return null;
    }
}
