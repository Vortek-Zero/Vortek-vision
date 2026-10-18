#!/usr/bin/env bash
# Inicia o servidor de hand tracking (MediaPipe) que roda NO PC.
# O celular (app AR) conecta em ws://<IP-do-PC>:8787 e manda os frames da câmera.
set -e
cd "$(dirname "$0")"

PY=".venv/bin/python"
if [ ! -x "$PY" ]; then
    echo "[handtracking] criando venv em $(pwd)/.venv ..."
    python3 -m venv .venv
    "$PY" -m pip install -q mediapipe websockets opencv-python-headless
    echo "[handtracking] modelo hand_landmarker.task precisa estar nesta pasta!"
fi

exec "$PY" server.py "$@"
