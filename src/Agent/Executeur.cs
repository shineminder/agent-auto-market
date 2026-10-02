using System.Globalization;
using System.Text.RegularExpressions;

namespace CryptoCrypt.Agent;

internal static partial class Formats
{
    [GeneratedRegex("^[A-Z0-9]{1,15}$")]
    private static partial Regex MotifSymbole();

    [GeneratedRegex("^[A-Z0-9]{1,15}-[A-Z0-9]{1,15}$")]
    private static partial Regex MotifPaire();

    public static bool Symbole(string? s) => s is not null && MotifSymbole().IsMatch(s);

    public static bool Paire(string? s) => s is not null && MotifPaire().IsMatch(s);
}

/// <summary>
/// Traitement des ordres recus : plafonds locaux, autorisation a usage unique, confirmation eventuelle,
/// execution avec la cle de l utilisateur, compte rendu idempotent. Une issue inconnue n est jamais renvoyee.
/// </summary>
internal sealed class Executeur(
    ConfigAgent config,
    ReponseEtat etat,
    ClientServeur serveur,
    ClientCoinbase coinbase,
    ILogger journal,
    HashSet<long> ecartes)
{
    // Etats de compte rendu. "reussie" vient du contrat ; tout autre etat vaut echec ; le caractere simule est fixe par le serveur.
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
        var proposes = recus.Select(r => r.Id).ToHashSet();

        // Un ordre pas encore autorise que le service ne propose plus est abandonne.
        local.EnAttente.RemoveAll(s => s.Etape == Etapes.Nouveau && !proposes.Contains(s.Id));

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
                if (s.OrderId is { Length: > 0 })
                {
                    return await FinaliserAsync(local, s, ct).ConfigureAwait(false);
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
        var paire = Paire(s);
        if (!Formats.Paire(paire))
        {
            Ecarter(s.Id, $"paire {paire} illisible");
            return true;
        }

        // Les ordres sont passes au marche : un prix limite est attendu localement, jusqu a expiration.
        if (s.PrixLimite is { } limite)
        {
            var cours = await CoursAsync(paire, ct).ConfigureAwait(false);
            var atteint = s.Action == "acheter" ? cours <= limite : cours >= limite;
            if (!atteint)
            {
                return false;
            }
        }

        if (!etat.Simulation && !coinbase.CleDisponible)
        {
            journal.LogWarning("Ordre {Id} en attente : cle Coinbase absente (cc-agent key --file cdp_api_key.json).", s.Id);
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
        return await ExecuterAsync(local, s, ct).ConfigureAwait(false);
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
                journal.LogInformation("Ordre {Id} confirme.", s.Id);
                return await ExecuterAsync(local, s, ct).ConfigureAwait(false);
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

    private async Task<bool> ExecuterAsync(EtatLocal local, SignalLocal s, CancellationToken ct)
    {
        // Les plafonds sont reverifies juste avant l execution (d autres ordres ont pu passer entre-temps).
        if (PlafondsLocaux.Refus(config, local, s, etat.Simulation) is { } refus)
        {
            Ecarter(s.Id, "plafonds locaux : " + refus);
            return true;
        }
        var paire = Paire(s);
        var reference = s.ClientOrderId ??= Guid.NewGuid().ToString("D");

        if (etat.Simulation)
        {
            var cours = await CoursAsync(paire, ct).ConfigureAwait(false);
            journal.LogInformation("Simulation, ordre {Id} : {Action} {Montant} {Devise} de {Symbole} au cours de {Cours} (aucun ordre passe).",
                s.Id, s.Action, Decimales.Ecrire(s.Montant), s.Devise, s.Symbol, Decimales.Ecrire(cours));
            await RendreCompteAsync(s, Reussie, s.Montant, Math.Round(s.Montant / cours, 8), cours, 0m,
                "simulation : aucun ordre passe", ct).ConfigureAwait(false);
            return true;
        }

        string? montantQuote = null;
        string? quantiteBase = null;
        if (s.Action == "acheter")
        {
            montantQuote = Decimales.Ecrire(s.Montant);
        }
        else
        {
            var produit = await coinbase.ProduitAsync(paire, ct).ConfigureAwait(false);
            var cours = Decimales.Lire(produit.Price);
            var quantite = cours > 0 ? Decimales.ArrondirInferieur(s.Montant / cours, produit.BaseIncrement) : 0m;
            if (quantite <= 0)
            {
                Ecarter(s.Id, "quantite a vendre inferieure au minimum de Coinbase");
                await RendreCompteSansEchecAsync(s, Echouee, "quantite inferieure au minimum", ct).ConfigureAwait(false);
                return true;
            }
            quantiteBase = Decimales.Ecrire(quantite);
        }

        // Enregistre AVANT l envoi : si l issue devient inconnue (coupure, delai), l ordre ne sera jamais renvoye.
        s.Etape = Etapes.Envoi;
        Stockage.EcrireEtat(local);

        string orderId;
        try
        {
            orderId = await coinbase.PasserOrdreAsync(reference, paire, s.Action == "acheter" ? "BUY" : "SELL",
                montantQuote, quantiteBase, ct).ConfigureAwait(false);
        }
        catch (AgentException ex)
        {
            // Refus explicite de Coinbase : retire et enregistre avant le compte rendu.
            Ecarter(s.Id, ex.Message);
            local.EnAttente.Remove(s);
            Stockage.EcrireEtat(local);
            await RendreCompteSansEchecAsync(s, Echouee, ex.Message, ct).ConfigureAwait(false);
            return true;
        }

        s.OrderId = orderId;
        local.EngageMoisUsd += s.Montant;
        Stockage.EcrireEtat(local);
        journal.LogInformation("Ordre {Id} transmis a Coinbase ({OrderId}).", s.Id, orderId);
        return await FinaliserAsync(local, s, ct).ConfigureAwait(false);
    }

    private async Task<bool> FinaliserAsync(EtatLocal local, SignalLocal s, CancellationToken ct)
    {
        var d = await coinbase.LireOrdreAsync(s.OrderId!, ct).ConfigureAwait(false);
        var statut = d?.Status?.ToUpperInvariant();
        if (d is null || statut is not ("FILLED" or "CANCELLED" or "EXPIRED" or "FAILED"))
        {
            return false;
        }

        var quantite = Decimales.Lire(d.FilledSize);
        if (quantite > 0)
        {
            var valeur = Decimales.Lire(d.FilledValue);
            var montant = valeur > 0 ? Math.Min(valeur, s.Montant) : s.Montant;
            await RendreCompteAsync(s, Reussie, montant, quantite, Decimales.Lire(d.AverageFilledPrice),
                Decimales.Lire(d.TotalFees), $"ordre Coinbase {s.OrderId}", ct).ConfigureAwait(false);
            return true;
        }

        await RendreCompteAsync(s, Echouee, s.Montant, null, null, null,
            $"ordre Coinbase {s.OrderId} : {statut}, rien d execute", ct).ConfigureAwait(false);
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

    private async Task<decimal> CoursAsync(string paire, CancellationToken ct)
    {
        var cours = Decimales.Lire((await coinbase.ProduitAsync(paire, ct).ConfigureAwait(false)).Price);
        return cours > 0 ? cours : throw new AgentException($"Cours indisponible pour {paire}.", Raison.Temporaire);
    }

    private string Paire(SignalLocal s) =>
        etat.Actifs.FirstOrDefault(a =>
                string.Equals(a.Symbol, s.Symbol, StringComparison.OrdinalIgnoreCase)
                && (s.Plateforme is null || a.Plateforme is null || string.Equals(a.Plateforme, s.Plateforme, StringComparison.OrdinalIgnoreCase)))
            ?.Paire is { Length: > 0 } p
            ? p.Trim().ToUpperInvariant()
            : $"{s.Symbol}-{s.Devise}";

    private void Ecarter(long id, string motif)
    {
        ecartes.Add(id);
        journal.LogInformation("Ordre {Id} ecarte : {Motif}.", id, motif);
    }

    private void SignalerIssueInconnue(SignalLocal s)
    {
        if (ecartes.Add(s.Id))
        {
            journal.LogError("Ordre {Id} : issue inconnue (reference Coinbase {Reference}). Verifiez votre historique Coinbase : "
                + "cet ordre ne sera jamais renvoye automatiquement.", s.Id, s.ClientOrderId);
        }
    }
}
