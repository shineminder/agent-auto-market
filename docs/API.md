# API contract (agent side)

Base: `https://<your-site>/api/agent/v1`. HTTPS required. The site address is set at install time and can be changed with `cc-agent server --url`.
Authentication: `Authorization: Bearer <token>` (except pairing).
Headers sent on every call: `X-Agent-Version` (e.g. `1.2.3`), `X-Agent-Os` (system description, 120 characters max).
Rate limits: 60 calls per minute; pairing: 10 calls per 10 minutes.

| Method | Route | Body | Responses |
|---|---|---|---|
| POST | `/appairer` | `code`, `nom`, `type`, `systeme` | 201 `jeton`, `appareil_id`, `agent_id`, `expire_le` / 422 invalid code |
| GET | `/etat` | - | 200 `agent`, `simulation`, `actifs[]`, `jeton_expire_le`, `mise_a_jour.version` |
| GET | `/signaux` | - | 200 `signaux[]`: `id`, `action`, `symbol`, `plateforme`, `montant`, `devise`, `prix_limite`, `expire_le`, `correlation` |
| POST | `/autoriser` | `action`, `symbol`, `plateforme`, `montant`, `devise`, `prix?`, `correlation` | 200 `autorise`, `confirmation`, `autorisation`, `expire_le`, `simulation`, `correlation` / refusal: `autorise=false`, `motif`, `message` |
| GET | `/autorisation/{id}` | - | 200 `etat` (`a_confirmer`, `demande`, `annulee`, `expire`...), `utilisable`, `expire_le` |
| POST | `/executions` | `idempotency_key`, `autorisation`, `action`, `symbol`, `plateforme`, `montant`, `devise`, `quantite`, `prix`, `frais`, `signal_id?`, `etat`, `message`, `correlation` | 201 `execution`, `deja_enregistre` / refusal: `refuse`, `motif`, `message` |
| POST | `/jeton/renouveler` | - | 201 `jeton`, `expire_le` |

Agent-side rules:

- check local limits **before** `/autoriser`;
- execute only if `autorise` is true, and only once `etat = demande` when a confirmation is required;
- an execution always cites its `autorisation` and never exceeds its amount;
- `etat` is `reussie` for a success; any other value counts as a failure. Simulation is decided by the server, never by the device;
- an unknown outcome is never sent again automatically.
