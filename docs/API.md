# Contrat de l'API (côté agent)

Base : `https://gaplab.fr/api/agent/v1` — HTTPS obligatoire. Authentification : `Authorization: Bearer <jeton>` (sauf appairage).
En-têtes envoyés à chaque appel : `X-Agent-Version` (ex. `1.2.3`), `X-Agent-Os` (description du système, 120 caractères max).
Limites : 60 appels par minute ; appairage : 10 appels par 10 minutes.

| Méthode | Route | Corps | Réponses |
|---|---|---|---|
| POST | `/appairer` | `code`, `nom`, `type`, `systeme` | 201 `jeton`, `appareil_id`, `agent_id`, `expire_le` · 422 code invalide |
| GET | `/etat` | — | 200 `agent`, `simulation`, `actifs[]`, `jeton_expire_le`, `mise_a_jour.version` |
| GET | `/signaux` | — | 200 `signaux[]` : `id`, `action`, `symbol`, `plateforme`, `montant`, `devise`, `prix_limite`, `expire_le`, `correlation` |
| POST | `/autoriser` | `action`, `symbol`, `plateforme`, `montant`, `devise`, `prix?`, `correlation` | 200 `autorise`, `confirmation`, `autorisation`, `expire_le` · 403 `motif`, `message` |
| GET | `/autorisation/{id}` | — | 200 `etat` : `a_confirmer`, `demande`, `annulee`, `expire`... |
| POST | `/executions` | `idempotency_key`, `autorisation`, `action`, `symbol`, `plateforme`, `montant`, `devise`, `quantite`, `prix`, `frais`, `signal_id?`, `etat`, `message`, `correlation` | 201 `execution`, `deja_enregistre` · 403 `motif` |
| POST | `/jeton/renouveler` | — | 201 `jeton`, `expire_le` |

Règles côté agent :

- vérifier les plafonds locaux **avant** `/autoriser` ;
- n'exécuter que si `autorise` est vrai, et seulement après `etat = demande` si une confirmation est requise ;
- une exécution cite toujours son `autorisation` et ne dépasse jamais son montant ;
- une issue inconnue n'est jamais renvoyée automatiquement.
