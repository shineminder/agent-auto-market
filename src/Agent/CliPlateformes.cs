namespace CryptoCrypt.Agent;

/// <summary>Commandes liees aux plateformes : une cle par plateforme, et controle complet.</summary>
internal static partial class Cli
{
    private static async Task<int> KeyAsync(Options o)
    {
        o.Limiter("file", "platform");
        ExigerCompteAgent();
        var nom = Plateformes.Normaliser(o["platform"]);
        if (!Plateformes.PriseEnCharge(nom))
        {
            throw new AgentException($"Plateforme {nom} non prise en charge. Disponibles : {string.Join(", ", Plateformes.Prises)}.");
        }

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

        var cle = Plateformes.LireCle(nom, contenu);
        using (var p = Plateformes.Creer(nom, cle))
        {
            await p.VerifierAsync(CancellationToken.None).ConfigureAwait(false);
        }

        var s = SecretsOuVides();
        s.Cles[nom] = cle;
        if (nom == "coinbase")
        {
            s.CoinbaseNom = null;
            s.CoinbaseCle = null;
        }
        CoffreSecrets.Ecrire(s);
        Console.WriteLine($"Cle {nom} verifiee et enregistree. Supprimez le fichier telecharge.");
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

        var avecCle = s.AvecCle();
        if (avecCle.Count == 0)
        {
            ok &= Resultat("Plateformes", false, "aucune cle : cc-agent key --file cdp_api_key.json");
        }
        foreach (var nom in avecCle)
        {
            try
            {
                using var p = Plateformes.Creer(nom, s.CleDe(nom));
                await p.VerifierAsync(CancellationToken.None).ConfigureAwait(false);
                ok &= Resultat(nom, true, "cle acceptee");
            }
            catch (Exception ex) when (ex is AgentException or HttpRequestException or TaskCanceledException)
            {
                ok &= Resultat(nom, false, ex.Message);
            }
        }

        Resultat("Mises a jour signees", Produit.MisesAJourActivees,
            Produit.MisesAJourActivees ? $"releases de {Produit.Depot}" : "cle de release absente de cette version");
        return ok ? 0 : 1;
    }
}