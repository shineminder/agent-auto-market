#!/usr/bin/env bash
# CryptoCrypt Agent - installateur guide pour Linux.
# Usage : bash installer.sh      (le site peut fournir ce fichier pre-rempli : SITE et CODE)
set -euo pipefail

SITE=""
CODE=""
DEPOT="shineminder/agent-auto-market"

question() {
  local reponse=""
  if [ -r /dev/tty ]; then read -r -p "$1" reponse </dev/tty || true; else read -r -p "$1" reponse || true; fi
  printf '%s' "$reponse"
}
echec() { printf '\033[31mERREUR : %s\033[0m\n' "$1" >&2; exit 1; }

printf '\n  Installation de CryptoCrypt Agent\n\n'

while :; do
  [ -n "$SITE" ] || SITE="$(question 'Adresse de votre site (ex. https://exemple.com) : ')"
  SITE="${SITE%/}"
  case "$SITE" in https://?*) break ;; *) echo "L adresse doit commencer par https://"; SITE="" ;; esac
done
[ -n "$CODE" ] || CODE="$(question 'Code d appairage, page Mes acces (Entree seule si mise a jour) : ')"

# ---- Fichier de cle Coinbase : recherche automatique ----
TELECHARGEMENTS="$HOME/Downloads"
if command -v xdg-user-dir >/dev/null 2>&1; then TELECHARGEMENTS="$(xdg-user-dir DOWNLOAD 2>/dev/null || echo "$HOME/Downloads")"; fi
ICI="$(cd "$(dirname "$0")" 2>/dev/null && pwd || pwd)"
CLE=""
for f in "$ICI/cdp_api_key.json" "$TELECHARGEMENTS/cdp_api_key.json" "$HOME/Downloads/cdp_api_key.json" "$HOME/cdp_api_key.json"; do
  if [ -f "$f" ]; then CLE="$f"; break; fi
done
if [ -n "$CLE" ]; then
  echo "Cle Coinbase : $CLE"
else
  echo "Fichier cdp_api_key.json introuvable dans le dossier de telechargements."
  CLE="$(question 'Chemin du fichier (Entree seule : l ajouter plus tard) : ')"
  CLE="${CLE%"${CLE##*[![:space:]]}"}"
  CLE="${CLE#\'}"; CLE="${CLE%\'}"; CLE="${CLE#\"}"; CLE="${CLE%\"}"
  CLE="${CLE/#\~/$HOME}"
  [ -z "$CLE" ] || [ -f "$CLE" ] || echec "Fichier introuvable : $CLE"
fi

# ---- Droits administrateur ----
SUDO=""
if [ "$(id -u)" -ne 0 ]; then
  command -v sudo >/dev/null 2>&1 || echec "Relancez en root : su -c 'bash installer.sh'"
  SUDO="sudo"
fi

# ---- Telechargement et lancement de l installateur (qui verifie signature et empreinte) ----
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT
URL="https://github.com/$DEPOT/releases/latest/download/install.sh"
echo "Telechargement de l installateur..."
if command -v curl >/dev/null 2>&1; then
  curl -fsSL "$URL" -o "$TMP/install.sh" || echec "Telechargement impossible : verifiez la connexion Internet."
elif command -v wget >/dev/null 2>&1; then
  wget -qO "$TMP/install.sh" "$URL" || echec "Telechargement impossible : verifiez la connexion Internet."
else
  echec "Installez d abord curl : sudo apt install curl (ou sudo dnf install curl)."
fi

ARGS=(--server "$SITE")
[ -z "$CODE" ] || ARGS+=(--code "$CODE")
[ -z "$CLE" ] || ARGS+=(--key "$CLE")
# Serveur sans ecran : mises a jour de securite automatiques du systeme
if [ -z "${DISPLAY:-}${WAYLAND_DISPLAY:-}" ] && command -v apt-get >/dev/null 2>&1; then ARGS+=(--os-updates); fi

if $SUDO bash "$TMP/install.sh" "${ARGS[@]}"; then
  if [ -n "$CLE" ]; then
    if command -v shred >/dev/null 2>&1; then shred -u "$CLE" 2>/dev/null || rm -f "$CLE"; else rm -f "$CLE"; fi
    echo "Fichier de cle supprime : la cle est conservee par l agent, lisible par lui seul."
  fi
  printf '\n\033[32mInstallation terminee.\033[0m\n'
else
  echec "L installation n a pas abouti : lisez le message ci-dessus."
fi
