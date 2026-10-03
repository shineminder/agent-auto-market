using System.Globalization;
using System.Text.RegularExpressions;

namespace CryptoCrypt.Agent;

internal static partial class Formats
{
    [GeneratedRegex("^[A-Z0-9]{1,15}$")]
    private static partial Regex MotifSymbole();

    [GeneratedRegex("^[A-Z0-9][A-Z0-9_/-]{1,30}$")]
    private static partial Regex MotifPaire();

    public static bool Symbole(string? s) => s is not null && MotifSymbole().IsMatch(s);

    public static bool Paire(string? s) => s is not null && MotifPaire().IsMatch(s);
}

/// <summary>
/// Traitement des ordres recus : plafonds locaux, autorisation a usage unique liee au signal, confirmation eventuelle,
/// execution sur la plateforme du signal avec la cle de l utilisateur, compte rendu idempotent.
/// Une issue inconnue n est jamais renvoyee.
/// </summary>
internal sealed class Executeur(
    ConfigAgent config,
    ReponseEtat etat,
    ClientServeur serveur,
    IReadOnlyDictionary<string, IPlateforme> plateformes,
    ILogger journal,
    HashSet<long> ecartes)
{
    // Etats de compte rendu : "reussie", tout autre etat vaut echec ; le caractere simule est fixe par le serveur.
    private const string Reussie = "reussie";
    private const string Echouee = "echouee";

    public async Task TraiterAsync(CancellationToken ct)
    {
        var local = Stockage.LireEtat();
        var mois = DateTime.UtcNow.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        if (local.Mois != mois)
        {
            local.Mois = mois;
            local.EngageMoisUsd = 0m;
        }

        var recus = (await serveur.SignauxAsync(ct).ConfigureAwait(false))?.Signaux
            ?? throw new AgentException("Liste des ordres illisible.", Raison.Temporaire);

        // Le service ne sert chaque signal qu une fois (etat pris) : l ordre reste en file locale jusqu a son expiration.
        foreach (var r in recus)
        {
            if (r.Id <= 0 || ecartes.Contains(r.Id) || local.EnAttente.Exists(s => s.Id == r.Id))
            {
                continue;
            }
            var symbole = r.Symbol?.Trim().ToUpperInvariant();
            if (!Formats.Symbole(symbole))
            {
                Ecarter(r.Id, "actif illisible");
                continue;
            }
            local.EnAttente.Add(new SignalLocal
            {
                Id = r.Id,
                Action = (r.Action ?? "").Trim().ToLowerInvariant(),
                Symbol = symbole!,
                Plateforme = r.Plateforme,
                Montant = r.Montant,
                Devise = string.IsNullOrWhiteSpace(r.Devise) ? "USD" : r.Devise.Trim().ToUpperInvariant(),
                PrixLimite = r.PrixLimite,
                ExpireLe = r.ExpireLe,
                Correlation = r.Correlation,
            });
        }
        Stockage.EcrireEtat(local);

        foreach (var s in local.EnAttente.ToList())
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (await TraiterUnAsync(local, s, ct).ConfigureAwait(false))
                {
                    local.EnAttente.Remove(s);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or AgentException { Raison: not Raison.JetonInvalide }
                                       && !ct.IsCancellationRequested)
            {
                if (s.Etape == Etapes.Envoi && s.OrderId is null)
                {
                    SignalerIssueInconnue(s);
                }
                else
                {
                    journal.LogWarning("Ordre {Id} : {Message} Nouvel essai au prochain cycle.", s.Id, ex.Message);
                }
            }
            Stockage.EcrireEtat(local);
        }
    }

    /// <summary>Fait avancer un ordre. Renvoie vrai quand il peut etre retire de la file locale.</summary>
    private async Task<bool> TraiterUnAsync(EtatLocal local, SignalLocal s, CancellationToken ct)
    {
        var expire = s.ExpireLe is { } e && e <= DateTimeOffset.UtcNow;
        switch (s.Etape)
        {
            case Etapes.Nouveau:
                if (expire)
                {
                    Ecarter(s.Id, "expire avant traitement");
                    return true;
                }
                return await DemanderAsync(local, s, ct).ConfigureAwait(false);

            case Etapes.AttenteConfirmation:
                return await AttendreConfirmationAsync(local, s, expire, ct).ConfigureAwait(false);

            case Etapes.Envoi:
                if (s.OrderId is { Length: > 0 } && Plateforme(s) is { } p)
                {
                    return await FinaliserAsync(local, s, p, ct).ConfigureAwait(false);
                }
                SignalerIssueInconnue(s);
                return expire || s.ExpireLe is null;

            default:
                Ecarter(s.Id, $"etape locale inconnue ({s.Etape})");
                return true;
        }
    }

    private async Task<bool> DemanderAsync(EtatLocal local, SignalLocal s, CancellationToken ct)
    {
        if (PlafondsLocaux.Refus(config, local, s, etat.Simulation) is { } refus)
        {
            Ecarter(s.Id, "plafonds locaux : " + refus);
            return true;
        }
        if (Plateforme(s) is not { } p)
        {
            Ecarter(s.Id, $"plateforme {Plateformes.Normaliser(s.Plateforme)} non prise en charge par cette version de l agent");
            return true;
        }
        var paire = Paire(s, p);
        if (!Formats.Paire(paire))
        {
            Ecarter(s.Id, $"paire {paire} illisible");
            return true;
        }

        // Les ordres sont passes au marche : un prix limite est attendu localement, jusqu a expiration.
        if (s.PrixLimite is { } limite)
        {
            var cours = await CoursAsync(p, paire, ct).ConfigureAwait(false);
            var atteint = s.Action == "acheter" ? cours <= limite : cours >= limite;
            if (!atteint)
            {
                return false;
            }
        }

        if (!etat.Simulation && !p.CleDisponible)
        {
            journal.LogWarning("Ordre {Id} en attente : cle {Plateforme} absente (cc-agent key --platform {Plateforme} --file ...).", s.Id, p.Nom, p.Nom);
            return false;
        }

        var r = await serveur.AutoriserAsync(new DemandeAutorisation
        {
            Action = s.Action,
            Symbol = s.Symbol,
            Plateforme = s.Plateforme,
            Montant = s.Montant,
            Devise = s.Devise,
            Prix = s.PrixLimite,
            SignalId = s.Id,
            Correlation = s.Correlation,
        }, ct).ConfigureAwait(false) ?? throw new AgentException("Reponse d autorisation illisible.", Raison.Temporaire);

        if (!r.Autorise || r.Autorisation is not { } autorisation)
        {
            Ecarter(s.Id, $"refuse par le service ({r.Motif ?? "motif non precise"}) {r.Message}".TrimEnd());
            return true;
        }
        s.Autorisation = autorisation;

        if (r.Confirmation)
        {
            s.Etape = Etapes.AttenteConfirmation;
            journal.LogInformation("Ordre {Id} : en attente de votre confirmation sur le site.", s.Id);
            return false;
        }
        return await ExecuterAsync(local, s, p, ct).ConfigureAwait(false);
    }

    private async Task<bool> AttendreConfirmationAsync(EtatLocal local, SignalLocal s, bool expire, CancellationToken ct)
    {
        if (s.Autorisation is not { } id)
        {
            Ecarter(s.Id, "autorisation absente");
            return true;
        }
        var r = await serveur.EtatAutorisationAsync(id, ct).ConfigureAwait(false)
            ?? throw new AgentException("Etat de l autorisation illisible.", Raison.Temporaire);

        switch (r.Etat)
        {
            case "demande":
                if (Plateforme(s) is not { } p)
                {
                    Ecarter(s.Id, "plateforme non prise en charge");
                    return true;
                }
                journal.LogInformation("Ordre {Id} confirme.", s.Id);
                return await ExecuterAsync(local, s, p, ct).ConfigureAwait(false);
            case "a_confirmer":
                if (!expire)
                {
                    return false;
                }
                Ecarter(s.Id, "non confirme avant expiration");
                return true;
            default:
                Ecarter(s.Id, $"autorisation {r.Etat ?? "inconnue"}");
                return true;
        }
    }

    private async Task<bool> ExecuterAsync(EtatLocal local, SignalLocal s, IPlateforme p, CancellationToken ct)
    {
        // Les plafonds sont reverifies juste avant l execution (d autres ordres ont pu passer entre-temps).
        if (PlafondsLocaux.Refus(config, local, s, etat.Simulation) is { } refus)
        {
            Ecarter(s.Id, "plafonds locaux : " + refus);
            return true;
        }
        var paire = Paire(s, p);
        var reference = s.ClientOrderId ??= Guid.NewGuid().ToString("D");
        var achat = s.Action == "acheter";

        if (etat.Simulation)
        {
            var cours = await CoursAsync(p, paire, ct).ConfigureAwait(false);
            journal.LogInformation("Simulation, ordre {Id} : {Action} {Montant} {Devise} de {Symbole} sur {Plateforme} au cours de {Cours} (aucun ordre passe).",
                s.Id, s.Action, Decimales.Ecrire(s.Montant), s.Devise, s.Symbol, p.Nom, Decimales.Ecrire(cours));
            await RendreCompteAsync(s, Reussie, s.Montant, Math.Round(s.Montant / cours, 8), cours, 0m,
                "simulation : aucun ordre passe", ct).ConfigureAwait(false);
            return true;
        }

        decimal? montantQuote = null;
        decimal? quantiteBase = null;
        if (achat)
        {
            montantQuote = s.Montant;
        }
        else
        {
            var produit = await p.CoursAsync(paire, ct).ConfigureAwait(false);
            var quantite = produit.Prix > 0 ? Arrondir(s.Montant / produit.Prix, produit.Increment) : 0m;
            if (quantite <= 0)
            {
                Ecarter(s.Id, "quantite a vendre inferieure au minimum de la plateforme");
                await RendreCompteSansEchecAsync(s, Echouee, "quantite inferieure au minimum", ct).ConfigureAwait(false);
                return true;
            }
            quantiteBase = quantite;
        }

        // Enregistre AVANT l envoi : si l issue devient inconnue (coupure, delai), l ordre ne sera jamais renvoye.
        s.Etape = Etapes.Envoi;
        Stockage.EcrireEtat(local);

        string orderId;
        try
        {
            orderId = await p.PasserOrdreAsync(new OrdreMarche(reference, paire, achat, montantQuote, quantiteBase), ct).ConfigureAwait(false);
        }
        catch (AgentException ex)
        {
            // Refus explicite de la plateforme : retire et enregistre avant le compte rendu.
            Ecarter(s.Id, ex.Message);
            local.EnAttente.Remove(s);
            Stockage.EcrireEtat(local);
            await RendreCompteSansEchecAsync(s, Echouee, ex.Message, ct).ConfigureAwait(false);
            return true;
        }

        s.OrderId = orderId;
        local.EngageMoisUsd += s.Montant;
        Stockage.EcrireEtat(local);
        journal.LogInformation("Ordre {Id} transmis a {Plateforme} ({OrderId}).", s.Id, p.Nom, orderId);
        return await FinaliserAsync(local, s, p, ct).ConfigureAwait(false);
    }

    private async Task<bool> FinaliserAsync(EtatLocal local, SignalLocal s, IPlateforme p, CancellationToken ct)
    {
        var r = await p.LireOrdreAsync(s.OrderId!, ct).ConfigureAwait(false);
        if (r is null || !r.Termine)
        {
            return false;
        }

        if (r.Quantite > 0)
        {
            var montant = r.Valeur > 0 ? Math.Min(r.Valeur, s.Montant) : s.Montant;
            await RendreCompteAsync(s, Reussie, montant, r.Quantite, r.PrixMoyen, r.Frais,
                $"ordre {p.Nom} {s.OrderId}", ct).ConfigureAwait(false);
            return true;
        }

        await RendreCompteAsync(s, Echouee, s.Montant, null, null, null,
            $"ordre {p.Nom} {s.OrderId} : {r.Statut}, rien d execute", ct).ConfigureAwait(false);
        local.EngageMoisUsd = Math.Max(0m, local.EngageMoisUsd - s.Montant);
        return true;
    }

    private async Task RendreCompteAsync(SignalLocal s, string etatExecution, decimal montant,
        decimal? quantite, decimal? prix, decimal? frais, string message, CancellationToken ct)
    {
        var r = await serveur.CompteRenduAsync(new CompteRendu
        {
            IdempotencyKey = s.ClientOrderId ?? throw new AgentException("Reference d ordre absente."),
            Autorisation = s.Autorisation ?? throw new AgentException("Autorisation absente."),
            Action = s.Action,
            Symbol = s.Symbol,
            Plateforme = s.Plateforme,
            Montant = montant,
            Devise = s.Devise,
            Quantite = quantite,
            Prix = prix,
            Frais = frais,
            SignalId = s.Id,
            Etat = etatExecution,
            Message = message.Length > 250 ? message[..250] : message,
            Correlation = s.Correlation,
        }, ct).ConfigureAwait(false) ?? throw new AgentException("Reponse au compte rendu illisible.", Raison.Temporaire);

        if (r.DejaEnregistre || r.Execution is not null)
        {
            journal.LogInformation("Ordre {Id} : compte rendu enregistre ({Etat}).", s.Id, etatExecution);
        }
        else
        {
            journal.LogWarning("Ordre {Id} : compte rendu refuse par le service ({Motif}) {Message}",
                s.Id, r.Motif ?? "motif non precise", r.Message ?? "");
        }
    }

    private async Task RendreCompteSansEchecAsync(SignalLocal s, string etatExecution, string message, CancellationToken ct)
    {
        try
        {
            await RendreCompteAsync(s, etatExecution, s.Montant, null, null, null, message, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            journal.LogWarning("Ordre {Id} : compte rendu non transmis ({Message}).", s.Id, ex.Message);
        }
    }

    private static async Task<decimal> CoursAsync(IPlateforme p, string paire, CancellationToken ct)
    {
        var c = await p.CoursAsync(paire, ct).ConfigureAwait(false);
        return c.Prix > 0 ? c.Prix : throw new AgentException($"Cours indisponible pour {paire}.", Raison.Temporaire);
    }

    private static decimal Arrondir(decimal valeur, decimal pas) =>
        pas > 0 ? Math.Floor(valeur / pas) * pas : Math.Round(valeur, 8, MidpointRounding.ToZero);

    private IPlateforme? Plateforme(SignalLocal s)
    {
        var nom = Plateformes.Normaliser(s.Plateforme);
        return Plateformes.PriseEnCharge(nom) && plateformes.TryGetValue(nom, out var p) ? p : null;
    }

    private string Paire(SignalLocal s, IPlateforme p) =>
        etat.Actifs.FirstOrDefault(a =>
                string.Equals(a.Symbol, s.Symbol, StringComparison.OrdinalIgnoreCase)
                && string.Equals(Plateformes.Normaliser(a.Plateforme), p.Nom, StringComparison.OrdinalIgnoreCase))
            ?.Paire is { Length: > 0 } paire
            ? paire.Trim().ToUpperInvariant()
            : p.Paire(s.Symbol, s.Devise);

    private void Ecarter(long id, string motif)
    {
        ecartes.Add(id);
        journal.LogInformation("Ordre {Id} ecarte : {Motif}.", id, motif);
    }

    private void SignalerIssueInconnue(SignalLocal s)
    {
        if (ecartes.Add(s.Id))
        {
            journal.LogError("Ordre {Id} : issue inconnue (reference {Reference}). Verifiez votre historique sur la plateforme : "
                + "cet ordre ne sera jamais renvoye automatiquement.", s.Id, s.ClientOrderId);
        }
    }
}