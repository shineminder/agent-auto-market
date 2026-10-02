#!/usr/bin/env bash
# Installe ou met a jour CryptoCrypt Agent sous Linux (PC ou serveur). A lancer en root.
# Exemples :
#   sudo bash install.sh --code ABCD-EFGH --key ~/cdp_api_key.json
#   sudo bash install.sh --key ~/cdp_api_key.json      (ajouter la cle plus tard)
#   sudo bash install.sh                               (mise a jour)
set -euo pipefail

DEPOT="shineminder/agent-auto-market"
SERVEUR=""
VERSION="latest"
CODE=""
CLE=""
PLAFOND_ORDRE=""
PLAFOND_MOIS=""
MAJ_OS="non"
DONNEES="/var/lib/cryptocrypt-agent"
PROGRAMME="/opt/cryptocrypt-agent"
UNITE="cryptocrypt-agent"
COMPTE="ccagent"

usage() {
  echo "Usage : sudo bash install.sh [--code XXXX-XXXX] [--key fichier.json] [--order USD] [--month USD] [--os-updates] [--version X.Y.Z] [--server URL]"
}

while [ $# -gt 0 ]; do
  case "$1" in
    --code) CODE="$2"; shift 2 ;;
    --key) CLE="$2"; shift 2 ;;
    --server) SERVEUR="$2"; shift 2 ;;
    --version) VERSION="$2"; shift 2 ;;
    --order) PLAFOND_ORDRE="$2"; shift 2 ;;
    --month) PLAFOND_MOIS="$2"; shift 2 ;;
    --os-updates) MAJ_OS="oui"; shift ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Option inconnue : $1" >&2; usage; exit 1 ;;
  esac
done

etape() { printf '\033[36m==> %s\033[0m\n' "$1"; }
echec() { printf '\033[31mERREUR : %s\033[0m\n' "$1" >&2; exit 1; }

[ "$(id -u)" -eq 0 ] || echec "A lancer en root (sudo)."
case "$DEPOT" in *__*) echec "Script non configure (depot)." ;; esac
[ -z "$CODE" ] || [ -n "$SERVEUR" ] || echec "Indiquez l adresse de votre site : --server https://... (commande affichee sur la page Mes acces)."

case "$(uname -m)" in
  x86_64|amd64) RID="linux-x64" ;;
  aarch64|arm64) RID="linux-arm64" ;;
  *) echec "Architecture non prise en charge : $(uname -m)" ;;
esac

paquets() {
  if command -v apt-get >/dev/null 2>&1; then
    DEBIAN_FRONTEND=noninteractive apt-get update -qq
    DEBIAN_FRONTEND=noninteractive apt-get install -y -qq "$@"
  elif command -v dnf >/dev/null 2>&1; then
    dnf install -y -q "$@"
  else
    echec "Installez manuellement : $*"
  fi
}

etape "Dependances"
command -v curl >/dev/null 2>&1 || paquets curl
command -v openssl >/dev/null 2>&1 || paquets openssl
[ -d /etc/ssl/certs ] || paquets ca-certificates
if ! ldconfig -p 2>/dev/null | grep -q 'libssl\.so'; then
  if command -v apt-get >/dev/null 2>&1; then paquets libssl3 || paquets libssl1.1; else paquets openssl-libs; fi
fi

etape "Recherche de la version"
if [ "$VERSION" = "latest" ]; then
  VERSION="$(curl -fsSL "https://api.github.com/repos/$DEPOT/releases/latest" | grep -m1 '"tag_name"' | sed -E 's/.*"v?([^"]+)".*/\1/')"
fi
BASE="https://github.com/$DEPOT/releases/download/v$VERSION"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

etape "Telechargement de la version $VERSION"
curl -fsSL "$BASE/SHA256SUMS" -o "$TMP/SHA256SUMS"
curl -fsSL "$BASE/SHA256SUMS.sig" -o "$TMP/SHA256SUMS.sig"
curl -fsSL "$BASE/cc-agent-$RID" -o "$TMP/cc-agent-$RID"

etape "Verification de la signature et de l empreinte"
cat > "$TMP/cle.pem" <<'CLE'
-----BEGIN PUBLIC KEY-----
MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEVkGn/aLeGIhfx8XLyj9rGP/r8+iZ
M2DMM6lT/JOSqVYuJa9TYpSXMZv6R1bGBdqiD+/kN9HwJeETbhsO1Q2szw==
-----END PUBLIC KEY-----
CLE
openssl dgst -sha256 -verify "$TMP/cle.pem" -signature "$TMP/SHA256SUMS.sig" "$TMP/SHA256SUMS" >/dev/null \
  || echec "Signature de la release invalide : installation annulee."
( cd "$TMP" && grep " cc-agent-$RID\$" SHA256SUMS | sha256sum -c --status ) \
  || echec "Empreinte invalide : installation annulee."

etape "Installation (utilisateur systeme dedie, service durci)"
id "$COMPTE" >/dev/null 2>&1 || useradd --system --home-dir "$DONNEES" --shell "$(command -v nologin || echo /usr/sbin/nologin)" "$COMPTE"
install -d -m 0755 -o "$COMPTE" -g "$COMPTE" "$PROGRAMME"
install -d -m 0700 -o "$COMPTE" -g "$COMPTE" "$DONNEES"
systemctl stop "$UNITE" 2>/dev/null || true
install -m 0755 -o "$COMPTE" -g "$COMPTE" "$TMP/cc-agent-$RID" "$PROGRAMME/cc-agent"
ln -sf "$PROGRAMME/cc-agent" /usr/local/bin/cc-agent

cat > "/etc/systemd/system/$UNITE.service" <<UNIT
[Unit]
Description=CryptoCrypt Agent
After=network-online.target
Wants=network-online.target

[Service]
Type=notify
User=$COMPTE
Group=$COMPTE
ExecStart=$PROGRAMME/cc-agent run
Restart=always
RestartSec=30
UMask=0077
NoNewPrivileges=true
ProtectSystem=strict
ProtectHome=true
PrivateTmp=true
PrivateDevices=true
ProtectKernelTunables=true
ProtectKernelModules=true
ProtectKernelLogs=true
ProtectControlGroups=true
ProtectClock=true
ProtectHostname=true
RestrictSUIDSGID=true
RestrictRealtime=true
RestrictNamespaces=true
LockPersonality=true
RestrictAddressFamilies=AF_INET AF_INET6 AF_UNIX
CapabilityBoundingSet=
ReadWritePaths=$DONNEES $PROGRAMME

[Install]
WantedBy=multi-user.target
UNIT
systemctl daemon-reload

agent() { runuser -u "$COMPTE" -- "$PROGRAMME/cc-agent" "$@"; }

etape "Configuration"
if [ -n "$CODE" ]; then
  agent pair --code "$CODE" --server "$SERVEUR" || echec "Appairage refuse : generez un nouveau code sur le site (page Mes acces)."
fi
if [ -n "$CLE" ]; then
  [ -r "$CLE" ] || echec "Fichier de cle introuvable : $CLE"
  agent key < "$CLE" || echec "Cle Coinbase refusee (droits Consulter + Negocier, algorithme ECDSA)."
fi
if [ -n "$PLAFOND_ORDRE" ] || [ -n "$PLAFOND_MOIS" ] || [ ! -f "$DONNEES/config.json" ]; then
  agent limits --order "${PLAFOND_ORDRE:-100}" --month "${PLAFOND_MOIS:-500}" >/dev/null
fi

if [ "$MAJ_OS" = "oui" ] && command -v apt-get >/dev/null 2>&1; then
  etape "Mises a jour de securite automatiques du systeme"
  paquets unattended-upgrades
  printf 'APT::Periodic::Update-Package-Lists "1";\nAPT::Periodic::Unattended-Upgrade "1";\n' > /etc/apt/apt.conf.d/20auto-upgrades
fi

etape "Demarrage"
systemctl enable --now "$UNITE" >/dev/null
agent verify || true
printf '\n\033[32mInstallation terminee.\033[0m Etat : sudo -u %s cc-agent status - Journal : journalctl -u %s -f\n' "$COMPTE" "$UNITE"
