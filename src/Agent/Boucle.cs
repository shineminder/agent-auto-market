using System.Globalization;
using Microsoft.Extensions.Hosting.WindowsServices;

namespace CryptoCrypt.Agent;

/// <summary>Cycle de l agent : etat du service, mise a jour eventuelle, ordres, renouvellement du jeton.</summary>
internal sealed class Boucle(ILogger<Boucle> journal, IHostApplicationLifetime vie) : BackgroundService
{
    private static readonly TimeSpan SeuilRenouvellement = TimeSpan.FromDays(3);
    private static readonly TimeSpan PauseApresEchecMaj = TimeSpan.FromHours(1);
    private static readonly TimeSpan PauseJetonInvalide = TimeSpan.FromMinutes(10);

    private readonly HashSet<long> _ecartes = [];
    private string? _majEchouee;
    private DateTimeOffset _majProchainEssai;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        journal.LogInformation("{Nom} {Version} demarre ({Systeme}).", Produit.Nom, Produit.Version, Systeme.Description());
        while (!ct.IsCancellationRequested)
        {
            var attente = TimeSpan.FromSeconds(60);
            try
            {
                var config = Stockage.LireConfig();
                attente = TimeSpan.FromSeconds(Math.Clamp(config.IntervalleSecondes, 15, 3600));
                if (await CycleAsync(config, ct).ConfigureAwait(false))
                {
                    return;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (AgentException ex) when (ex.Raison == Raison.JetonInvalide)
            {
                journal.LogError("{Message}", ex.Message);
                attente = PauseJetonInvalide;
            }
            catch (AgentException ex)
            {
                journal.LogWarning("{Message}", ex.Message);
            }
            catch (HttpRequestException ex)
            {
                journal.LogWarning("Reseau indisponible : {Message}", ex.Message);
            }
            catch (TaskCanceledException)
            {
                journal.LogWarning("Delai depasse : nouvel essai au prochain cycle.");
            }
            catch (Exception ex)
            {
                journal.LogError(ex, "Erreur inattendue : nouvel essai au prochain cycle.");
            }

            try
            {
                await Task.Delay(attente, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>Un cycle complet. Renvoie vrai si l agent doit redemarrer (mise a jour installee).</summary>
    private async Task<bool> CycleAsync(ConfigAgent config, CancellationToken ct)
    {
        var secrets = CoffreSecrets.Lire();
        if (string.IsNullOrEmpty(config.Serveur) || string.IsNullOrEmpty(secrets.Jeton))
        {
            journal.LogWarning("Machine non appairee : cc-agent pair --code XXXX-XXXX --server URL.");
            return false;
        }

        using var serveur = new ClientServeur(config, secrets.Jeton);
        var etat = await serveur.EtatAsync(ct).ConfigureAwait(false)
            ?? throw new AgentException("Etat du service illisible.", Raison.Temporaire);

        // Actifs autorises : fixes au premier contact, modifiables ensuite uniquement en local.
        if (config.Actifs.Count == 0)
        {
            var actifs = etat.Actifs
                .Select(a => a.Symbol?.Trim().ToUpperInvariant())
                .Where(Formats.Symbole)
                .OfType<string>()
                .Distinct()
                .ToList();
            if (actifs.Count > 0)
            {
                config.Actifs = actifs;
                Stockage.EcrireConfig(config);
                journal.LogInformation("Actifs autorises sur cette machine (premier contact) : {Actifs}.", string.Join(", ", actifs));
            }
        }

        if (await MettreAJourAsync(config, etat, ct).ConfigureAwait(false))
        {
            return true;
        }

        ClientCoinbase coinbase;
        try
        {
            coinbase = new ClientCoinbase(secrets.CoinbaseNom, secrets.CoinbaseCle);
        }
        catch (AgentException ex)
        {
            journal.LogWarning("{Message}", ex.Message);
            coinbase = new ClientCoinbase(null, null);
        }
        using (coinbase)
        {
            await new Executeur(config, etat, serveur, coinbase, journal, _ecartes).TraiterAsync(ct).ConfigureAwait(false);
        }

        await RenouvelerJetonAsync(serveur, etat, ct).ConfigureAwait(false);
        return false;
    }

    private async Task<bool> MettreAJourAsync(ConfigAgent config, ReponseEtat etat, CancellationToken ct)
    {
        var cible = etat.MiseAJour?.Version;
        if (!config.MisesAJourAuto || !Produit.MisesAJourActivees || !Versions.EstPlusRecente(cible, Produit.Version))
        {
            return false;
        }
        if (cible == _majEchouee && DateTimeOffset.UtcNow < _majProchainEssai)
        {
            return false;
        }

        journal.LogInformation("Mise a jour demandee : {Actuelle} vers {Cible}.", Produit.Version, cible);
        try
        {
            await MiseAJour.InstallerAsync(cible!, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _majEchouee = cible;
            _majProchainEssai = DateTimeOffset.UtcNow + PauseApresEchecMaj;
            journal.LogError("Mise a jour vers {Cible} refusee ou impossible : {Message} Nouvel essai dans une heure.", cible, ex.Message);
            return false;
        }

        journal.LogWarning("Version {Cible} installee et verifiee : redemarrage de l agent.", cible);
        Environment.ExitCode = MiseAJour.CodeRedemarrage;
        if (OperatingSystem.IsWindows() && WindowsServiceHelpers.IsWindowsService())
        {
            // Arret sans signaler l arret au gestionnaire de services : ses actions de recuperation
            // (sc failure, posees par install.ps1) relancent l agent sur le nouvel executable.
            Environment.Exit(MiseAJour.CodeRedemarrage);
        }
        // Linux : arret propre, systemd relance l agent (Restart=always).
        vie.StopApplication();
        return true;
    }

    private async Task RenouvelerJetonAsync(ClientServeur serveur, ReponseEtat etat, CancellationToken ct)
    {
        if (!DateTimeOffset.TryParse(etat.JetonExpireLe, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var expire)
            || expire - DateTimeOffset.UtcNow > SeuilRenouvellement)
        {
            return;
        }
        var r = await serveur.RenouvelerJetonAsync(ct).ConfigureAwait(false);
        if (r?.Jeton is not { Length: > 0 } jeton)
        {
            journal.LogWarning("Renouvellement du jeton refuse : nouvel essai au prochain cycle.");
            return;
        }
        var secrets = CoffreSecrets.Lire();
        secrets.Jeton = jeton;
        CoffreSecrets.Ecrire(secrets);
        journal.LogInformation("Jeton renouvele (valable jusqu au {Expire}).", r.ExpireLe);
    }
}
