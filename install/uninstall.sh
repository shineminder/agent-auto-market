#!/usr/bin/env bash
# Desinstalle CryptoCrypt Agent. --keep-data garde la configuration et la cle.
set -euo pipefail
[ "$(id -u)" -eq 0 ] || { echo "A lancer en root (sudo)." >&2; exit 1; }
systemctl disable --now cryptocrypt-agent 2>/dev/null || true
rm -f /etc/systemd/system/cryptocrypt-agent.service /usr/local/bin/cc-agent
systemctl daemon-reload
rm -rf /opt/cryptocrypt-agent
if [ "${1:-}" != "--keep-data" ]; then rm -rf /var/lib/cryptocrypt-agent; userdel ccagent 2>/dev/null || true; fi
echo "Agent desinstalle. Revoquez l appareil sur le site (Mes acces) et supprimez la cle chez Coinbase."
