using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace CryptoCrypt.Agent;

/// <summary>Ligne de commande : configuration locale et diagnostic. La commande run lance le service.</summary>
internal static class Cli
{
    private const string AccesRefuse =
        "Acces refuse. Windows : ouvrez PowerShell en administrateur. Linux : sudo -u ccagent cc-agent <commande>.";

    public static async Task<int> ExecuterAsync(string[] args)
    {
        var commande = args.Length > 0 ? args[0].Trim().ToLowerInvariant() : "help";
        try
        {
            if (commande == "run")
            {
                return await Hote.ExecuterAsync(args[1..]).ConfigureAwait(false);
            }
            var o = Options.Lire(args.Length > 1 ? args[1..] : []);
            return commande switch
            {
                "status" => await StatusAsync(o).ConfigureAwait(false),
                "verify" => await VerifyAsync(o).ConfigureAwait(false),
                "limits" => Limits(o),
                "update" => await UpdateAsync(o).ConfigureAwait(false),
                "pair" => await PairAsync(o).ConfigureAwait(false),
                "key" => await KeyAsync(o).ConfigureAwait(false),
                "version" or "--version" or "-v" => AfficherVersion(),
                "help" or "--help" or "-h" or "/?" => Aide(Console.Out, 0),
                _ => Inconnue(commande),
            };
        }
        catch (AgentException ex)
        {
            return Erreur(ex.Message);
        }
        catch (UnauthorizedAccessException)
        {
            return Erreur(AccesRefuse);
        }
        catch (CryptographicException ex)
        {
            return Erreur("Erreur cryptographique : " + ex.Message + " Si les secrets sont illisibles : refaites cc-agent pair puis cc-agent key.");
        }
        catch (HttpRequestException ex)
        {
            return Erreur("Connexion impossible : " + ex.Message + " Verifiez la connexion Internet et le pare-feu.");
        }
        catch (TaskCanceledException)
        {
            return Erreur("Delai depasse : le serveur ne repond pas. Reessayez dans quelques minutes.");
        }
        catch (IOException ex)
        {
            return Erreur("Fichier inaccessible : " + ex.Message);
        }
    }

    // ---------- Commandes ----------

    private static async Task<int> StatusAsync(Options o)
    {
        o.Limiter();
        var c = Stockage.LireConfig();
        var s = CoffreSecrets.Lire();
        var e = Stockage.LireEtat();
        var engage = e.Mois == MoisCourant() ? e.EngageMoisUsd : 0m;

        Console.WriteLine($"{Produit.Nom} {Produit.Version} - {Systeme.Description()}");
        Ligne("Serveur", c.Serveur is { Length: > 0 } ? c.Serveur : "aucun");
        Ligne("Appairage", string.IsNullOrEmpty(s.Jeton) ? "non (cc-agent pair --code XXXX-XXXX --server URL)" : "oui");
        Ligne("Cle Coinbase", string.IsNullOrEmpty(s.CoinbaseCle) ? "absente (cc-agent key --file cdp_api_key.json)" : "presente");
        AfficherPlafonds(c, engage);
        Ligne("Ordres en cours", e.EnAttente.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var x in e.EnAttente)
        {
            Console.WriteLine($"    #{x.Id} {x.Action} {x.Symbol} {D(x.Montant)} {x.Devise} : {Libelle(x)}");
        }

        if (!string.IsNullOrEmpty(c.Serveur) && !string.IsNullOrEmpty(s.Jeton))
        {
            try
            {
                using var client = new ClientServeur(c, s.Jeton);
                var r = await client.EtatAsync(CancellationToken.None).ConfigureAwait(false);
                Ligne("Agent", r?.Agent is { } a ? $"{a.Nom} ({a.Etat})" : "?");
                Ligne("Mode", r?.Simulation == true ? "simulation (aucun ordre passe)" : "reel");
                Ligne("Version cible", r?.MiseAJour?.Version ?? "aucune");
                Ligne("Jeton valable jusqu au", r?.JetonExpireLe ?? "?");
            }
            catch (Exception ex) when (ex is AgentException or HttpRequestException or TaskCanceledException)
            {
                Ligne("Service", "injoignable : " + ex.Message);
            }
        }
        return 0;
    }

    private static async Task<int> VerifyAsync(Options o)
    {
        o.Limiter();
        var c = Stockage.LireConfig();
        var s = CoffreSecrets.Lire();
        var ok = true;
        Console.WriteLine($"{Produit.Nom} {Produit.Version} - {Systeme.Description()}");

        if (string.IsNullOrEmpty(c.Serveur) || string.IsNullOrEmpty(s.Jeton))
        {
            ok &= Resultat("Service", false, "machine non appairee : cc-agent pair --code XXXX-XXXX --server URL");
        }
        else
        {
            try
            {
                using var client = new ClientServeur(c, s.Jeton);
                var e = await client.EtatAsync(CancellationToken.None).ConfigureAwait(false);
                ok &= Resultat("Service", true, $"joint, agent {e?.Agent?.Nom} ({e?.Agent?.Etat}){(e?.Simulation == true ? ", mode simulation" : "")}");
            }
            catch (Exception ex) when (ex is AgentException or HttpRequestException or TaskCanceledException)
            {
                ok &= Resultat("Service", false, ex.Message);
            }
        }

        if (string.IsNullOrEmpty(s.CoinbaseCle))
        {
            ok &= Resultat("Coinbase", false, "cle absente : cc-agent key --file cdp_api_key.json");
        }
        else
        {
            try
            {
                using var cb = new ClientCoinbase(s.CoinbaseNom, s.CoinbaseCle);
                await cb.VerifierAsync(CancellationToken.None).ConfigureAwait(false);
                ok &= Resultat("Coinbase", true, "cle acceptee");
            }
            catch (Exception ex) when (ex is AgentException or HttpRequestException or TaskCanceledException)
            {
                ok &= Resultat("Coinbase", false, ex.Message);
            }
        }

        Resultat("Mises a jour signees", Produit.MisesAJourActivees,
            Produit.MisesAJourActivees ? $"releases de {Produit.Depot}" : "cle de release absente de cette version");
        return ok ? 0 : 1;
    }

    private static int Limits(Options o)
    {
        o.Limiter("order", "month", "assets", "auto-update");
        var c = Stockage.LireConfig();
        if (o.Nombre > 0)
        {
            ExigerCompteAgent();
            if (o["order"] is { } ordre) c.PlafondOrdreUsd = Montant(ordre, "--order");
            if (o["month"] is { } mois) c.PlafondMoisUsd = Montant(mois, "--month");
            if (o["assets"] is { } actifs) c.Actifs = Actifs(actifs);
            if (o["auto-update"] is { } auto) c.MisesAJourAuto = Booleen(auto, "--auto-update");
            if (c.PlafondOrdreUsd > c.PlafondMoisUsd)
            {
                throw new AgentException("Le plafond par ordre ne peut pas depasser le plafond mensuel.");
            }
            Stockage.EcrireConfig(c);
            Console.WriteLine("Reglages enregistres (pris en compte au prochain cycle).");
        }
        var e = Stockage.LireEtat();
        AfficherPlafonds(c, e.Mois == MoisCourant() ? e.EngageMoisUsd : 0m);
        return 0;
    }

    private static async Task<int> UpdateAsync(Options o)
    {
        o.Limiter("version", "allow-downgrade");
        ExigerCompteAgent();
        var retour = o.Present("allow-downgrade");
        var cible = o["version"]?.Trim().TrimStart('v');
        var depuisService = false;

        if (string.IsNullOrEmpty(cible))
        {
            if (retour) throw new AgentException("--allow-downgrade exige --version X.Y.Z.");
            var c = Stockage.LireConfig();
            var s = CoffreSecrets.Lire();
            if (string.IsNullOrEmpty(c.Serveur) || string.IsNullOrEmpty(s.Jeton))
            {
                throw new AgentException("Machine non appairee : precisez la version, cc-agent update --version X.Y.Z.");
            }
            using var client = new ClientServeur(c, s.Jeton);
            cible = (await client.EtatAsync(CancellationToken.None).ConfigureAwait(false))?.MiseAJour?.Version;
            if (string.IsNullOrEmpty(cible))
            {
                Console.WriteLine($"Aucune mise a jour demandee par le service (version installee : {Produit.Version}).");
                return 0;
            }
            depuisService = true;
        }

        if (!Versions.Valide(cible)) throw new AgentException("Version attendue au format X.Y.Z.");
        if (cible == Produit.Version)
        {
            Console.WriteLine($"Deja a jour ({cible}).");
            return 0;
        }
        if (!Versions.EstPlusRecente(cible, Produit.Version))
        {
            if (depuisService)
            {
                Console.WriteLine($"La version demandee par le service ({cible}) n est pas plus recente que {Produit.Version} : rien a faire.");
                return 0;
            }
            if (!retour)
            {
                throw new AgentException($"La version {cible} est plus ancienne que la version installee ({Produit.Version}). "
                    + $"Pour confirmer ce retour en arriere : cc-agent update --version {cible} --allow-downgrade");
            }
        }

        Console.WriteLine($"Telechargement et verification de la version {cible}...");
        await MiseAJour.InstallerAsync(cible, CancellationToken.None).ConfigureAwait(false);
        Console.WriteLine($"Version {cible} installee. Redemarrez le service pour l utiliser :");
        Console.WriteLine(OperatingSystem.IsWindows()
            ? "  Restart-Service CryptoCryptAgent      (PowerShell administrateur)"
            : "  sudo systemctl restart cryptocrypt-agent");
        return 0;
    }

    private static async Task<int> PairAsync(Options o)
    {
        o.Limiter("code", "server");
        ExigerCompteAgent();
        var code = o["code"]?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(code) || code.Length > 32)
        {
            throw new AgentException("Code d appairage attendu : cc-agent pair --code XXXX-XXXX --server URL (code genere sur le site, page Mes acces).");
        }

        var c = Stockage.LireConfig();
        var serveur = (o["server"] is { Length: > 0 } sv ? sv : c.Serveur).Trim().TrimEnd('/');
        if (serveur.Length == 0) throw new AgentException("Adresse du service attendue : --server https://...");
        c.Serveur = serveur;

        using var client = new ClientServeur(c, null);
        var r = await client.AppairerAsync(new DemandeAppairage
        {
            Code = code,
            Nom = Environment.MachineName,
            Systeme = Systeme.Description(),
        }, CancellationToken.None).ConfigureAwait(false);
        if (r?.Jeton is not { Length: > 0 } jeton)
        {
            throw new AgentException("Code d appairage refuse ou expire : generez un nouveau code sur le site (page Mes acces), valable 10 minutes.");
        }

        var s = SecretsOuVides();
        s.Jeton = jeton;
        CoffreSecrets.Ecrire(s);
        Stockage.EcrireConfig(c);
        Console.WriteLine($"Machine appairee (appareil #{r.AppareilId}, agent #{r.AgentId}). Jeton valable jusqu au {r.ExpireLe}.");
        return 0;
    }

    private static async Task<int> KeyAsync(Options o)
    {
        o.Limiter("file");
        ExigerCompteAgent();
        string contenu;
        if (o["file"] is { Length: > 0 } fichier)
        {
            if (!File.Exists(fichier)) throw new AgentException($"Fichier de cle introuvable : {fichier}");
            contenu = await File.ReadAllTextAsync(fichier).ConfigureAwait(false);
        }
        else if (Console.IsInputRedirected)
        {
            contenu = await Console.In.ReadToEndAsync().ConfigureAwait(false);
        }
        else
        {
            throw new AgentException("Fichier attendu : cc-agent key --file cdp_api_key.json");
        }

        var (nom, pem) = LireFichierCle(contenu);
        using (var cb = new ClientCoinbase(nom, pem))
        {
            await cb.VerifierAsync(CancellationToken.None).ConfigureAwait(false);
        }
        var s = SecretsOuVides();
        s.CoinbaseNom = nom;
        s.CoinbaseCle = pem;
        CoffreSecrets.Ecrire(s);
        Console.WriteLine("Cle Coinbase verifiee et enregistree. Supprimez le fichier telecharge.");
        return 0;
    }

    // ---------- Outils ----------

    private static (string Nom, string Cle) LireFichierCle(string contenu)
    {
        try
        {
            using var doc = JsonDocument.Parse(contenu);
            var racine = doc.RootElement;
            string? Champ(string n) =>
                racine.ValueKind == JsonValueKind.Object && racine.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String
                    ? v.GetString()
                    : null;
            var nom = Champ("name") ?? Champ("id");
            var cle = Champ("privateKey");
            if (!string.IsNullOrWhiteSpace(nom) && cle is not null && cle.Contains("PRIVATE KEY", StringComparison.Ordinal))
            {
                return (nom.Trim(), cle);
            }
        }
        catch (JsonException)
        {
            // traite ci-dessous
        }
        throw new AgentException("Fichier de cle non reconnu : le fichier cdp_api_key.json telecharge chez Coinbase est attendu (algorithme ECDSA).");
    }

    private static Secrets SecretsOuVides()
    {
        try
        {
            return CoffreSecrets.Lire();
        }
        catch (CryptographicException)
        {
            return new Secrets();
        }
    }

    /// <summary>Sous Linux, les fichiers doivent appartenir au compte du service : jamais root.</summary>
    private static void ExigerCompteAgent()
    {
        if (OperatingSystem.IsWindows() || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CCAGENT_HOME")))
        {
            return;
        }
        if (Environment.UserName == "root")
        {
            throw new AgentException("Ne lancez pas cette commande en root (les fichiers deviendraient illisibles pour le service). Utilisez : sudo -u ccagent cc-agent ...");
        }
    }

    private static decimal Montant(string texte, string option)
    {
        if (!decimal.TryParse(texte.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var m)
            || m <= 0 || m > 1_000_000m)
        {
            throw new AgentException($"{option} : montant en USD attendu (ex. 100).");
        }
        return m;
    }

    private static List<string> Actifs(string texte)
    {
        var liste = texte.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(a => a.ToUpperInvariant())
            .Distinct()
            .ToList();
        if (liste.Count == 0 || liste.Exists(a => !Formats.Symbole(a)))
        {
            throw new AgentException("--assets : liste de symboles attendue, ex. BTC,ETH.");
        }
        return liste;
    }

    private static bool Booleen(string texte, string option) => texte.Trim().ToLowerInvariant() switch
    {
        "true" or "oui" or "1" or "on" => true,
        "false" or "non" or "0" or "off" => false,
        _ => throw new AgentException($"{option} : true ou false attendu."),
    };

    private static string MoisCourant() => DateTime.UtcNow.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    private static string D(decimal d) => Decimales.Ecrire(d);

    private static string Libelle(SignalLocal s) => s.Etape switch
    {
        Etapes.Nouveau => "a traiter",
        Etapes.AttenteConfirmation => "en attente de votre confirmation sur le site",
        Etapes.Envoi when s.OrderId is { Length: > 0 } => "transmis a Coinbase, compte rendu en cours",
        Etapes.Envoi => $"ISSUE INCONNUE, verifiez sur Coinbase (reference {s.ClientOrderId}) ; jamais renvoye",
        _ => s.Etape,
    };

    private static void AfficherPlafonds(ConfigAgent c, decimal engage)
    {
        Ligne("Plafond par ordre", $"{D(c.PlafondOrdreUsd)} USD");
        Ligne("Plafond mensuel", $"{D(c.PlafondMoisUsd)} USD (engage ce mois : {D(engage)} USD)");
        Ligne("Actifs autorises", c.Actifs.Count > 0 ? string.Join(", ", c.Actifs) : "fixes au premier contact avec le service");
        Ligne("Mises a jour auto", c.MisesAJourAuto ? "oui" : "non");
    }

    private static void Ligne(string nom, string valeur) => Console.WriteLine($"  {nom,-24}: {valeur}");

    private static bool Resultat(string nom, bool reussi, string detail)
    {
        Console.WriteLine($"  [{(reussi ? "OK" : "ECHEC")}] {nom} : {detail}");
        return reussi;
    }

    private static int AfficherVersion()
    {
        Console.WriteLine(Produit.Version);
        return 0;
    }

    private static int Erreur(string message)
    {
        Console.Error.WriteLine("ERREUR : " + message);
        return 1;
    }

    private static int Inconnue(string commande)
    {
        Console.Error.WriteLine($"Commande inconnue : {commande}");
        return Aide(Console.Error, 2);
    }

    private static int Aide(TextWriter w, int code)
    {
        w.WriteLine($"{Produit.Nom} {Produit.Version}");
        w.WriteLine("""

            Usage : cc-agent <commande> [options]

              status                               Etat : appairage, cle, plafonds, ordres en cours, version cible
              verify                               Controle complet : service joint, cle acceptee par Coinbase
              limits [--order USD] [--month USD]   Affiche ou modifie les plafonds locaux
                     [--assets BTC,ETH] [--auto-update true|false]
              update [--version X.Y.Z] [--allow-downgrade]
                                                   Installe la version demandee (si plus recente et signee)
              pair --code XXXX-XXXX [--server URL] Appaire cette machine
              key --file cdp_api_key.json          Remplace la cle Coinbase (verifiee avant enregistrement)
              run                                  Execute l agent (utilise par le service)
              version | help

            Windows : PowerShell administrateur. Linux : sudo -u ccagent cc-agent <commande>
            """);
        return code;
    }

    private sealed class Options
    {
        private readonly Dictionary<string, string> _valeurs = new(StringComparer.OrdinalIgnoreCase);

        public static Options Lire(string[] args)
        {
            var o = new Options();
            for (var i = 0; i < args.Length; i++)
            {
                var a = args[i];
                if (!a.StartsWith("--", StringComparison.Ordinal) || a.Length < 3)
                {
                    throw new AgentException($"Argument inattendu : {a}. Aide : cc-agent help");
                }
                var nom = a[2..];
                var valeur = "";
                var egal = nom.IndexOf('=');
                if (egal > 0)
                {
                    valeur = nom[(egal + 1)..];
                    nom = nom[..egal];
                }
                else if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    valeur = args[++i];
                }
                o._valeurs[nom] = valeur;
            }
            return o;
        }

        public int Nombre => _valeurs.Count;

        public string? this[string nom] => _valeurs.TryGetValue(nom, out var v) ? v : null;

        public bool Present(string nom) => _valeurs.ContainsKey(nom);

        public void Limiter(params string[] autorisees)
        {
            foreach (var nom in _valeurs.Keys)
            {
                if (!autorisees.Contains(nom, StringComparer.OrdinalIgnoreCase))
                {
                    throw new AgentException($"Option inconnue : --{nom}. Aide : cc-agent help");
                }
            }
        }
    }
}
