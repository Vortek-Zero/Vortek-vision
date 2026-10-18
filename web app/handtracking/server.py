#!/usr/bin/env python3
"""
CÉREBRO do hand tracking — roda NO PC (porta 8787).

O celular só manda frames JPEG da câmera; AQUI acontece toda a lógica:
  1. MediaPipe HandLandmarker detecta os 21 landmarks da mão.
  2. Conta os dedos estendidos (ângulo nas juntas — método oficial do paper
     do MediaPipe Hands) -> isso define o MODO, independente de ângulo.
  3. Máquina de estados com debounce decide o modo ativo.
  4. Devolve um COMANDO pronto pro celular executar.

  contagem de dedos -> modo:
     1 dedo  = MODO 1  move o cursor (raio na ponta do dedo)
     2 dedos = MODO 2  move a janela (segue o cursor)
     3 dedos = MODO 3  janela cola na pose do celular (mirar pra posicionar)
     4 dedos = MODO 4  escala tipo potenciômetro (gira a mão)
     0 ou 5  = MODO 0  ocioso (punho / palma aberta)

TROCAR UM GESTO / CALIBRAR = editar isto e reiniciar (segundos, SEM build):
     .venv/bin/python server.py [--port 8787] [--flip-x|--no-flip-x] [--flip-y]
Protocolo: entrada = binário (JPEG); saída = texto (JSON).
"""

import asyncio
import argparse
import json
import math
import os
import time
from collections import deque

import cv2
import numpy as np
import mediapipe as mp
from mediapipe.tasks.python import vision
from mediapipe.tasks.python.core.base_options import BaseOptions
import websockets

MODEL_PATH = "hand_landmarker.task"

# --- landmarks (21) ---
WRIST = 0
THUMB_IP = 3
THUMB_TIP = 4
INDEX_MCP, INDEX_PIP, INDEX_TIP = 5, 6, 8
MIDDLE_MCP, MIDDLE_PIP, MIDDLE_TIP = 9, 10, 12
RING_MCP, RING_PIP, RING_TIP = 13, 14, 16
PINKY_MCP, PINKY_PIP, PINKY_TIP = 17, 18, 20

# Dedos: (MCP, PIP, TIP) — o polegar usa (MCP, IP, TIP).
FINGERS = [
    (2, THUMB_IP, THUMB_TIP),          # polegar
    (INDEX_MCP, INDEX_PIP, INDEX_TIP),  # indicador
    (MIDDLE_MCP, MIDDLE_PIP, MIDDLE_TIP),
    (RING_MCP, RING_PIP, RING_TIP),
    (PINKY_MCP, PINKY_PIP, PINKY_TIP),
]

# --- calibração (mexe aqui e reinicia; sem build) ---
# Limiar por dedo: anelar/mindinho mais exigentes p/ não contar curvado
# (principal causa do flap 3<->4: mindinho curvado lendo como estendido).
# Estes são só o PONTO DE PARTIDA: a calibração passiva abaixo aprende o
# limiar real do usuário durante o uso normal (ver CALIB_*).
STRAIGHT_DEG = (148.0, 150.0, 155.0, 162.0)  # indicador, médio, anelar, mindinho
THUMB_STRAIGHT_DEG = 145.0  # polegar tem articulação mais solta
DEBOUNCE_S = 0.25        # tempo estável antes de trocar de modo (evita flap 2<->3<->4)

# --- CALIBRAÇÃO PASSIVA (sem botão, sem vídeo de calibração) ---
# Base: paper MediaPipe Hands usa "accumulated angles of joints" (PIP+DIP);
# a literatura de limiar adaptativo separa os 2 clusters (curvado x estendido)
# por percentis numa janela do uso normal + histerese (Schmitt trigger).
# O usuário NÃO faz nada: ele só usa o sistema e os limiares se ajustam.
CALIB_WIN = 480         # amostras por dedo na janela (~24 s a 20 fps)
CALIB_P_LOW = 20.0      # percentil do cluster "curvado"
CALIB_P_HIGH = 80.0     # percentil do cluster "estendido"
CALIB_MIN_SEP = 0.15    # separação mínima entre clusters p/ confiar no limiar
CALIB_HYST = 0.05       # banda morta em volta do limiar (mata flap na raiz)
CALIB_THR_MIN = 0.35    # limites de segurança do limiar aprendido
CALIB_THR_MAX = 0.90
CALIB_RECAL_EVERY = 60  # recalcula a cada N amostras novas
CALIB_SAVE = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                          "gesture_calib.json")  # persiste p/ não relearn no restart
LOST_S = 0.40            # sem mão por isso -> modo 0 (maior = janela não congela ao girar rápido)
MODE4_CONFIRM_S = 0.5    # modo 4 (escala) precisa estar estável MAIS tempo p/ entrar
                         # (sem isso, flap 3<->4 durante movimento vertical escala a janela)
EMIT_S = 0.0             # limite p/ emitir (0 = todo frame)
SCALE_GAIN = 1.0         # ganho do potenciômetro (1 = 90° -> 2x)
SCALE_MIN = 0.4
SCALE_MAX = 3.0
ROLL_PER_TURN_DEG = 90.0 # graus de giro pra dobrar/metade o tamanho

# --- escala (MODO 4 = 4 dedos; subir/descer a mão = maior/menor, tipo slider) ---
VERT_GAIN = 3.0          # quanto a escala muda por unidade de movimento vertical
SCALE_MIN = 0.4
SCALE_MAX = 3.0
FX = 1                   # flip eixo X (imagem->tela); ajuste com --no-flip-x
FY = 0

# --- GLOW adaptativo p/ escuro (só pré-processamento p/ a detecção) ---
# Se o frame está escuro, clareia com gama ANTES do MediaPipe. O usuário NÃO
# vê nada: o JPEG do celular nunca é exibido, só os landmarks importam.
# Claro (>= limiar) = sem glow (gama 1.0, sem custo).
GLOW_MEAN_THRESH = 70.0  # brilho médio abaixo disso => aplica glow
GLOW_MIN_GAMMA = 0.35    # força máxima (quanto menor, mais clareia)


def _v(pts, a, b):
    return (pts[a][0] - pts[b][0], pts[a][1] - pts[b][1], pts[a][2] - pts[b][2])


def _angle_at(pts, a, b, c):
    """Ângulo (graus) no ponto b formado por a-b-c."""
    v1 = _v(pts, b, a)
    v2 = _v(pts, b, c)
    dot = v1[0] * v2[0] + v1[1] * v2[1] + v1[2] * v2[2]
    n1 = math.sqrt(v1[0] ** 2 + v1[1] ** 2 + v1[2] ** 2)
    n2 = math.sqrt(v2[0] ** 2 + v2[1] ** 2 + v2[2] ** 2)
    if n1 < 1e-9 or n2 < 1e-9:
        return 180.0
    return math.degrees(math.acos(max(-1.0, min(1.0, dot / (n1 * n2)))))


def _clamp01(x):
    return 0.0 if x < 0.0 else (1.0 if x > 1.0 else x)


def finger_scores(pts):
    """(score_polegar, [4 scores]) em 0..1 — quão estendido está cada dedo.

    Ponto de partida do paper MediaPipe Hands ("accumulated angles of joints"):
    soma o ângulo do PIP com o do DIP (landmarks 7/11/15/19, que o código
    antigo IGNORAVA) e normaliza em 0..1:
      dedo reto  -> PIP~180 + DIP~180 = 360 -> score 1.0
      punho      -> ~210                    -> score ~0.17
    O limiar exato fica por conta da calibração passiva (FingerCalib).
    """
    scores = []
    for mcp, pip, tip in FINGERS[1:]:
        dip = pip + 1  # 7, 11, 15, 19
        acc = _angle_at(pts, mcp, pip, dip) + _angle_at(pts, pip, dip, tip)
        scores.append(_clamp01((acc - 180.0) / 180.0))
    thumb_acc = _angle_at(pts, 2, THUMB_IP, THUMB_TIP)  # polegar: IP único
    thumb = _clamp01((thumb_acc - 90.0) / 90.0)
    return thumb, scores


def _default_score_thr():
    """Converte os limiares em graus pro domínio do score (0..1)."""
    # graus (PIP+DIP) -> score: PIP=STRAIGHT_DEG + DIP~172 => (acc-180)/180
    return tuple((d + 172.0 - 180.0) / 180.0 for d in STRAIGHT_DEG)


def _load_calib_file():
    try:
        with open(CALIB_SAVE) as f:
            data = json.load(f)
        return data if isinstance(data, dict) else {}
    except Exception:
        return {}


class FingerCalib:
    """Limiar passivo por dedo: janela de scores -> percentis -> limiar com
    histerese. Aprende durante o uso normal; persiste em gesture_calib.json."""

    def __init__(self, name, default_thr, saved=None):
        self.name = name
        self.default = default_thr
        thr = (saved or {}).get(name)
        self.thr = (thr if isinstance(thr, (int, float))
                    and CALIB_THR_MIN <= thr <= CALIB_THR_MAX else default_thr)
        self.learned = isinstance(thr, (int, float)) and self.thr != default_thr
        self.win = deque(maxlen=CALIB_WIN)
        self.ext = False          # estado com histerese
        self._last_log = 0.0
        self._last_save = 0.0

    def _recalc(self, now):
        if len(self.win) < 120:
            return
        arr = np.fromiter(self.win, dtype=np.float64, count=len(self.win))
        lo = float(np.percentile(arr, CALIB_P_LOW))
        hi = float(np.percentile(arr, CALIB_P_HIGH))
        if hi - lo < CALIB_MIN_SEP:
            return  # uso ainda não mostrou os 2 estados -> mantém limiar atual
        if hi < 0.70 or lo > 0.55:
            # janela sem os EXTREMOS reais (punho ~0.15 / estendido ~0.95):
            # média aqui viraria limiar no meio do nada e quebraria o gesto.
            return
        new = (lo + hi) * 0.5
        new = max(CALIB_THR_MIN, min(CALIB_THR_MAX, new))
        if abs(new - self.thr) > 0.02:
            self.thr = new
            self.learned = True
            if now - self._last_log >= 8.0:
                self._last_log = now
                print(f"[calib] {self.name}: limiar aprendido = {self.thr:.2f} "
                      f"(faixa {lo:.2f}..{hi:.2f}, n={len(self.win)})", flush=True)
        if now - self._last_save >= 10.0:
            self._last_save = now
            self._save()

    def _save(self):
        try:
            data = _load_calib_file()
            data[self.name] = round(self.thr, 3)
            with open(CALIB_SAVE, "w") as f:
                json.dump(data, f)
        except Exception:
            pass

    def update(self, score, now):
        self.win.append(score)
        if len(self.win) % CALIB_RECAL_EVERY == 0:
            self._recalc(now)
        # histerese: só troca passando MARGEM além do limiar
        if not self.ext and score > self.thr + CALIB_HYST:
            self.ext = True
        elif self.ext and score < self.thr - CALIB_HYST:
            self.ext = False
        return self.ext


def count_fingers(pts, calib=None, now=0.0):
    """(polegar_estendido, nº dos 4 dedos estendidos: indicador..mindinho).

    Dedo reto = score alto (ângulos acumulados PIP+DIP ~360). Contamos SÓ os 4
    dedos (não o polegar) e separamos o polegar, porque ele serve de
    desambiguador:
      - 4 dedos + polegar RECOLHIDO = gesto "4" (escala)
      - 4 dedos + polegar DE FORA   = palma aberta (ocioso)

    `calib` = lista de FingerCalib (calibração passiva). Sem calib, usa limiar
    fixo (útil p/ teste offline).
    """
    thumb_score, scores = finger_scores(pts)
    if calib is None:
        thr_list = _default_score_thr()
        thumb_ext = thumb_score > ((THUMB_STRAIGHT_DEG + 90.0) / 2 - 90.0) / 90.0
        n = sum(s > t for s, t in zip(scores, thr_list))
        return thumb_ext, n, thumb_score, scores
    thumb_ext = calib[0].update(thumb_score, now)
    n = sum(calib[i + 1].update(scores[i], now) for i in range(4))
    return thumb_ext, n, thumb_score, scores


def mode_from_count(thumb_ext, count4):
    """Contagem de dedos -> modo (4 dedos = escala; polegar desambigua)."""
    if count4 == 4 and thumb_ext:
        return 0  # palma aberta (4 + polegar de fora) = ocioso, NÃO escala
    if count4 in (1, 2, 3, 4):
        return count4
    return 0  # punho (0) ou mão fora = ocioso


# --- BIMANUAL (2 mãos com papéis distintos) ---
# Com 2 mãos detectadas:
#   MÃO ESQUERDA -> MODES (contagem de dedos dirige mover/posicionar/escala)
#   MÃO DIREITA  -> PONTEIRO (u,v do indicador; alvo de clique/arraste)
# Com 1 mão, ela faz os dois papéis (compatível com o app antigo).
# FLIP_HANDS: troca Left/Right se a câmera devolver a lateral espelhada
# (sem build: edite e reinicie).
FLIP_HANDS = False


def _hand_label(handedness, i):
    try:
        cat = handedness[i].categories[0].categoryName  # "Left" | "Right"
    except Exception:
        return "?"
    if FLIP_HANDS:
        cat = "Right" if cat == "Left" else "Left"
    return cat


def pinch_distance(pts):
    """Distância polegar-índice normalizada pelo tamanho da mão (0 = colado)."""
    hand = math.dist(pts[WRIST][:2], pts[MIDDLE_MCP][:2])
    if hand < 1e-6:
        return 1.0
    return math.dist(pts[THUMB_TIP][:2], pts[INDEX_TIP][:2]) / hand


def _wrap180(a):
    return (a + 180.0) % 360.0 - 180.0


class HandBrain:
    def __init__(self):
        opts = vision.HandLandmarkerOptions(
            base_options=BaseOptions(model_asset_path=MODEL_PATH),
            running_mode=vision.RunningMode.VIDEO,
            num_hands=2,  # 2 mãos: esquerda=modos, direita=pointer (bimanual)
            min_hand_detection_confidence=0.5,
            min_hand_presence_confidence=0.5,
            min_tracking_confidence=0.5,
        )
        self.landmarker = vision.HandLandmarker.create_from_options(opts)
        self.ts_ms = 0

        # calibração passiva por dedo: [polegar, indicador, médio, anelar, mindinho]
        saved = _load_calib_file()
        defaults = ( ((THUMB_STRAIGHT_DEG + 90.0) / 2 - 90.0) / 90.0, ) + _default_score_thr()
        names = ("thumb", "index", "middle", "ring", "pinky")
        self.calib = [FingerCalib(n, d, saved) for n, d in zip(names, defaults)]
        learned = [f"{c.name}={c.thr:.2f}" for c in self.calib if c.learned]
        if learned:
            print(f"[calib] limiares carregados de gesture_calib.json: "
                  f"{', '.join(learned)}", flush=True)
        else:
            print("[calib] primeiro uso: limiares aprendem sozinhos em ~segundos "
                  "de uso normal (persistem em gesture_calib.json)", flush=True)

        # estado do modo
        self.cur_mode = 0
        self.cand_mode = 0
        self.cand_since = 0.0
        self.last_seen = 0.0
        self.base_v = None    # altura da mão na entrada do MODO 4 (slider de escala)

    def _roll(self, pts):
        # Orientação da mão (pulso -> base do dedo médio) no plano da imagem.
        dx = pts[MIDDLE_MCP][0] - pts[WRIST][0]
        dy = pts[MIDDLE_MCP][1] - pts[WRIST][1]
        return math.degrees(math.atan2(dy, dx))

    def process(self, jpeg_bytes, now):
        img = cv2.imdecode(np.frombuffer(jpeg_bytes, np.uint8), cv2.IMREAD_COLOR)
        if img is None:
            return {"ok": 0, "mode": 0, "present": 0}

        # --- GLOW adaptativo p/ escuro (invisível p/ o usuário) ---
        gray = cv2.cvtColor(img, cv2.COLOR_BGR2GRAY)
        mean = float(gray.mean())
        glow = 1.0
        if mean < GLOW_MEAN_THRESH:
            glow = max(GLOW_MIN_GAMMA, min(1.0, mean / GLOW_MEAN_THRESH))
            if glow < 0.999:
                lut = np.array([((i / 255.0) ** glow) * 255.0
                                for i in range(256)]).astype(np.uint8)
                img = cv2.LUT(img, lut)

        rgb = cv2.cvtColor(img, cv2.COLOR_BGR2RGB)
        mpi = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb)
        self.ts_ms += 33
        result = self.landmarker.detect_for_video(mpi, self.ts_ms)

        landmarks = result.hand_landmarks or []
        if not landmarks:
            # Sem mão: segura por LOST_S e depois zera.
            if now - self.last_seen >= LOST_S:
                self.cur_mode = 0
                self.cand_mode = 0
                self.base_v = None
            return {"ok": 1, "mode": self.cur_mode, "present": 0,
                    "count": 0, "u": 0.5, "v": 0.5,
                    "scaleMul": 1.0, "minScale": SCALE_MIN, "maxScale": SCALE_MAX,
                    "fx": FX, "fy": FY, "state": "nohand", "hands": 0}

        handed = result.handedness or []
        hands = []
        for i, lm in enumerate(landmarks):
            pts = [(p.x, p.y, p.z) for p in lm]
            hands.append((_hand_label(handed, i), pts))
        self.last_seen = now

        # papéis: esquerda = modos; direita = ponteiro (1 mão = os dois)
        modeLbl, modePts = next((h for h in hands if h[0] == "Left"), hands[0])
        ptrLbl, ptrPts = next((h for h in hands if h[0] == "Right"), hands[-1])

        thumb_ext, count4, thumb_score, scores = count_fingers(modePts, self.calib, now)
        count = count4 + (1 if thumb_ext else 0)  # p/ diagnóstico

        # ponteiro = ponta do indicador da MÃO PONTEIRO
        u = ptrPts[INDEX_TIP][0]
        v_ptr = ptrPts[INDEX_TIP][1]
        v = modePts[INDEX_TIP][1]  # altura da mão dos MODOS (slider de escala)

        # modo = contagem de dedos (1/2/3/4)
        raw_mode = mode_from_count(thumb_ext, count4)

        # --- debounce do modo ---
        if raw_mode != self.cand_mode:
            self.cand_mode = raw_mode
            self.cand_since = now
        need = MODE4_CONFIRM_S if self.cand_mode == 4 else DEBOUNCE_S
        if (now - self.cand_since) >= need and self.cur_mode != self.cand_mode:
            self.cur_mode = self.cand_mode
            if self.cur_mode == 4:
                self.base_v = v  # congela a altura de referência (slider)

        # ponteiro do MODO 2 = MEIO entre indicador e médio da MÃO PONTEIRO
        # ("janela no meio dos 2 dedos"); demais modos usam a ponta do indicador
        if self.cur_mode == 2:
            u = (ptrPts[INDEX_TIP][0] + ptrPts[MIDDLE_TIP][0]) * 0.5
            v_ptr = (ptrPts[INDEX_TIP][1] + ptrPts[MIDDLE_TIP][1]) * 0.5

        # log de troca de modo (dispara a cada transição)
        if self.cur_mode != getattr(self, "_last_mode", -1):
            self._last_mode = self.cur_mode
            print(f"[brain] >>> MODO {self.cur_mode}  (count4={count4} v={v:.2f} "
                  f"mãos={len(hands)} modos={modeLbl}/ponteiro={ptrLbl})", flush=True)

        # --- escala por SLIDER VERTICAL (modo 4): subir a mão = maior ---
        scale_mul = 1.0
        if self.cur_mode == 4 and self.base_v is not None:
            raw_mul = 1.0 + (v - self.base_v) * VERT_GAIN  # inverter: descer = maior
            scale_mul = max(SCALE_MIN, min(SCALE_MAX, raw_mul))

        # --- log de diagnóstico (throttle ~5/s) p/ calibrar com a mão real ---
        if now - getattr(self, "_last_log", 0.0) >= 0.2:
            self._last_log = now
            print(f"[brain] count4={count4} th={int(thumb_ext)} v={v:.2f} base_v={self.base_v if self.base_v is not None else -1:.2f} "
                  f"mode={self.cur_mode} scaleMul={scale_mul:.2f} glow={glow:.2f} "
                  f"sc={thumb_score:.2f}/" + ",".join(f"{s:.2f}" for s in scores), flush=True)

        return {
            "ok": 1,
            "mode": self.cur_mode,
            "present": 1,
            "count": count,
            "u": round(u, 4),
            "v": round(v_ptr, 4),
            "scaleMul": round(scale_mul, 4),
            "minScale": SCALE_MIN,
            "maxScale": SCALE_MAX,
            "fx": FX,
            "fy": FY,
            "ms": 0.0,
            "hands": len(hands),
            "handMode": modeLbl[:1],
            "handPtr": ptrLbl[:1],
            "state": f"mode{self.cur_mode}/f{count}" + (f"/glow{glow:.2f}" if glow < 0.999 else ""),
        }


brain = None
clients = set()
FLIP_KNOWN = True


async def handler(ws):
    global brain
    clients.add(ws)
    addr = ws.remote_address
    print(f"[brain] celular conectado: {addr} (total: {len(clients)})", flush=True)
    try:
        async for message in ws:
            if isinstance(message, bytes):
                t0 = time.time()
                reply = brain.process(message, time.time())
                reply["ms"] = round((time.time() - t0) * 1000, 1)
                await ws.send(json.dumps(reply))
    except websockets.ConnectionClosed:
        pass
    finally:
        clients.discard(ws)
        print(f"[brain] celular desconectado: {addr} (total: {len(clients)})", flush=True)


async def main(port):
    global brain
    brain = HandBrain()
    async with websockets.serve(handler, "0.0.0.0", port, max_size=8 * 1024 * 1024):
        print("=" * 64, flush=True)
        print("  CÉREBRO de hand tracking (MediaPipe) — lógica AQUI, não no app", flush=True)
        print(f"  WebSocket : ws://0.0.0.0:{port}", flush=True)
        print(f"  Modos     : 1=cursor 2=mover 3=posicionar 4=escala | 0=ocioso", flush=True)
        print(f"  flipX={FX} flipY={FY} (ajuste: --flip-x/--no-flip-x/--flip-y)", flush=True)
        print("=" * 64, flush=True)
        await asyncio.Future()


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--port", type=int, default=8787)
    ap.add_argument("--flip-x", dest="flip_x", action="store_true")
    ap.add_argument("--no-flip-x", dest="flip_x", action="store_false")
    ap.set_defaults(flip_x=True)
    ap.add_argument("--flip-y", dest="flip_y", action="store_true")
    ap.set_defaults(flip_y=False)
    args = ap.parse_args()
    FX = 1 if args.flip_x else 0
    FY = 1 if args.flip_y else 0
    try:
        asyncio.run(main(args.port))
    except KeyboardInterrupt:
        print("\n[brain] servidor encerrado", flush=True)
