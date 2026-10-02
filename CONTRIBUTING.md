# Contributing

1. Open an issue before any significant change.
2. One branch per topic; signed commits recommended.
3. `dotnet build src/Agent/Agent.csproj -c Release` must succeed with no new warnings.
4. No new dependency without a reason (attack surface).
5. Never log a secret (token, key, authorization header).

Security issues follow [SECURITY.md](SECURITY.md), never a public issue.
