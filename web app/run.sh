#!/usr/bin/env bash
# ---------------------------------------------------------------------------
# AR Metaverse - servidor nativo para Linux (substitui o webserver.exe)
#
#   ./run.sh            -> sobe na porta 80 (pede sudo, igual ao .exe original)
#   ./run.sh 8080       -> sobe numa porta comum, sem sudo
# ---------------------------------------------------------------------------
set -e
cd "$(dirname "$0")"

PORT="${1:-80}"

if ! command -v node >/dev/null 2>&1; then
  echo "ERRO: Node.js não encontrado. Instale com: sudo pacman -S nodejs"
  exit 1
fi

if [ "$PORT" -lt 1024 ] && [ "$(id -u)" -ne 0 ]; then
  echo "Porta $PORT requer root. Pedindo sudo..."
  exec sudo -p "Senha do sudo: " node "$PWD/server.js" --port "$PORT"
fi

exec node server.js --port "$PORT"
