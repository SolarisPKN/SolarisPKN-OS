#!/usr/bin/env bash
# SolarisPKN-Paste · instalación sencilla en Debian/Ubuntu.
# https://github.com/SolarisPKN/SolarisPKN-OS/blob/main/instalar-solarispkn-paste.sh
# Sin curl|bash, apt-key ni trusted=yes.
set -Eeuo pipefail
umask 077

readonly PACKAGE="solarispkn-paste"
readonly BOOTSTRAP_FILE="solarispkn-repository_1.0-1_all.deb"
readonly BOOTSTRAP_URL="https://raw.githubusercontent.com/SolarisPKN/SolarisPKN-OS/main/apt/bootstrap/solarispkn-repository_1.0-1_all.deb"
# SHA-256 del paquete oficial verificado. Un cambio inesperado bloquea la instalación.
readonly BOOTSTRAP_SHA256="eb20a79a0407e1178f4ad21eb1dff4c2f00ff1a09b77b517707d00384162905d"

say() { printf '\n\033[1;36m%s\033[0m\n' "$*"; }
fail() { printf '\nError: %s\n' "$*" >&2; exit 1; }
as_root() {
    if (( EUID == 0 )); then
        "$@"
    else
        sudo "$@"
    fi
}

[[ -f /etc/os-release ]] || fail "No puedo identificar esta distribución."
# shellcheck disable=SC1091
. /etc/os-release
case " $ID ${ID_LIKE:-} " in
    *" debian "*|*" ubuntu "*) ;;
    *) fail "Este instalador es para Debian/Ubuntu y derivados compatibles con APT." ;;
esac
command -v apt-get >/dev/null || fail "Falta apt-get."
command -v dpkg-query >/dev/null || fail "Falta dpkg-query."
command -v sha256sum >/dev/null || fail "Falta sha256sum."
case "$(dpkg --print-architecture)" in
    amd64|arm64) ;;
    *) fail "Por ahora el repositorio SolarisPKN admite amd64 y arm64." ;;
esac
if (( EUID != 0 )); then
    command -v sudo >/dev/null || fail "No tenés sudo instalado. Ejecutá: su -c 'bash instalar-solarispkn-paste.sh'"
    say "El sistema puede solicitar tu contraseña para instalar los paquetes."
    sudo -v || fail "Se necesita permiso de administrador para instalar paquetes."
fi

say "SolarisPKN-Paste · instalación en Debian/Ubuntu"
printf 'Se instalará %s y se configurará el repositorio firmado SolarisPKN.\n' "$PACKAGE"
printf 'No se actualizan otros programas, no se borran archivos y no se desactivan las firmas de APT.\n'

new_source="/etc/apt/sources.list.d/solarispkn.sources"
new_key="/usr/share/keyrings/solarispkn-archive.asc"
legacy_source="/etc/apt/sources.list.d/solarispkn-paste.sources"

if [[ -f "$new_source" && -f "$legacy_source" ]]; then
    fail "Hay dos fuentes APT de SolarisPKN (antigua y nueva). Revisá y migrá la duplicada antes de instalar."
fi

if dpkg-query -W -f='${db:Status-Abbrev}' solarispkn-repository 2>/dev/null | grep -q '^ii '; then
    [[ -s "$new_key" && -s "$new_source" ]] || fail "El repositorio está instalado pero incompleto. Reparalo antes de seguir."
    say "Repositorio SolarisPKN ya configurado; no se duplicarán claves."
elif [[ -f "$legacy_source" ]]; then
    say "Detecté el repositorio SolarisPKN antiguo. Lo reutilizaré para evitar entradas Signed-By duplicadas."
else
    if grep -Rilqs 'raw.githubusercontent.com/SolarisPKN/SolarisPKN-OS/main/apt' \
        /etc/apt/sources.list.d /etc/apt/sources.list 2>/dev/null; then
        fail "Existe una configuración SolarisPKN no reconocida. Revisá tus fuentes APT antes de continuar."
    fi
    if command -v curl >/dev/null 2>&1; then
        fetch() { curl --proto '=https' --tlsv1.2 --fail --location --silent --show-error \
            --retry 3 --connect-timeout 15 --max-time 120 --output "$2" "$1"; }
    elif command -v wget >/dev/null 2>&1; then
        fetch() { wget --https-only --tries=3 --timeout=40 -q -O "$2" "$1"; }
    else
        fail "Para la primera descarga hace falta curl o wget."
    fi

    tmp_dir="$(mktemp -d)"
    trap 'rm -rf -- "$tmp_dir"' EXIT
    say "Descargando la configuración firmada del repositorio..."
    fetch "$BOOTSTRAP_URL" "$tmp_dir/$BOOTSTRAP_FILE" ||
        fail "No pude descargar el paquete del repositorio."
    printf '%s  %s\n' "$BOOTSTRAP_SHA256" "$tmp_dir/$BOOTSTRAP_FILE" |
        sha256sum --check --status ||
        fail "SHA-256 incorrecto. No se instalará nada de este archivo."

    say "Preparando APT e instalando la clave pública delimitada al repositorio..."
    as_root apt-get update
    as_root apt-get install -y --no-install-recommends "$tmp_dir/$BOOTSTRAP_FILE"
fi

say "Verificando la firma de APT e instalando SolarisPKN-Paste..."
as_root apt-get update
as_root apt-get install -y "$PACKAGE"

say "¡SolarisPKN-Paste instalado!"
command -v solarispkn-paste >/dev/null ||
    fail "APT no devolvió error, pero falta el ejecutable solarispkn-paste."
solarispkn-paste --version
printf '\nPara empezar: solarispkn-paste --help\n'
printf 'Las futuras versiones publicadas en este repositorio llegan con apt update/upgrade.\n'
