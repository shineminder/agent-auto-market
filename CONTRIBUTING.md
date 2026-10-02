# Contribuer

1. Ouvrez un ticket avant toute modification importante.
2. Une branche par sujet ; commits signés recommandés.
3. `dotnet build src/Agent/Agent.csproj -c Release` doit réussir sans avertissement nouveau.
4. Aucune dépendance nouvelle sans justification (surface d'attaque).
5. Ne journalisez jamais de secret (jeton, clé, en-tête d'autorisation).

Les failles de sécurité suivent [SECURITY.md](SECURITY.md), jamais un ticket public.
