#!/usr/bin/env python3
"""Testes OFFLINE da lógica de gestos (sem câmera, sem celular, sem build).

Roda: .venv/bin/python test_gestures.py
Cria mãos sintéticas (21 landmarks) com curl controlado por dedo e verifica:
  - contagem/pontuação acumulada PIP+DIP (paper MediaPipe Hands)
  - regra do polegar (palma aberta != gesto 4)
  - histerese da calibração passiva (sem flap com score oscilando no limiar)
  - aprendizado dos limiares a partir de uso simulado (punho + palma)
"""
import math
import os
import sys
import tempfile

os.environ.setdefault("CALIB_NO_PERSIST", "1")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import server as S  # noqa: E402

# nunca toca no gesture_calib.json real: usa diretório temporário
S.CALIB_SAVE = os.path.join(tempfile.mkdtemp(), "gesture_calib.json")


def _chain(mcp, curl, seg=0.09, n=3, start_deg=-90.0, step_deg=75.0):
    """Cadeia de um dedo: n segmentos; curl=0 reto, curl=1 dobrado."""
    pts = []
    p = mcp
    ang = start_deg
    for _ in range(n):
        ang += curl * step_deg
        a = math.radians(ang)
        p = (p[0] + seg * math.cos(a), p[1] + seg * math.sin(a), 0.0)
        pts.append(p)
    return pts  # [PIP..TIP] (3 pts) ou [IP, TIP] (2 pts c/ n=2)


def make_hand(curls, thumb_curl=None):
    """21 landmarks (x, y, z). curls = 4 floats (indicador..mindinho)."""
    if thumb_curl is None:
        thumb_curl = 1.0 if all(c > 0.5 for c in curls) else 0.0
    pts = [(0.0, 0.0, 0.0)] * 21
    pts[0] = (0.0, 0.0, 0.0)                       # wrist
    # polegar: 1=CMC 2=MCP 3=IP 4=TIP (cadeia de 2 segmentos a partir do MCP)
    pts[1] = (-0.05, -0.05, 0.0)
    th = _chain((-0.10, -0.10), thumb_curl, seg=0.07, n=2, start_deg=-140.0)
    pts[2], pts[3] = th[0], th[1]                  # MCP? -> 2=MCP da cadeia
    # corrige: cadeia começa no MCP(2); landmarks 3=IP, 4=TIP
    pts[2] = (-0.10, -0.10, 0.0)
    pts[3], pts[4] = th[0], th[1]
    mcps = {5: -0.06, 9: -0.01, 13: 0.04, 17: 0.09}
    for (mcp, curl), mx in zip(zip((5, 9, 13, 17), curls),
                               (mcps[5], mcps[9], mcps[13], mcps[17])):
        pts[mcp] = (mx, -0.18, 0.0)
        pip, dip, tip = _chain(pts[mcp], curl)
        pts[mcp + 1], pts[mcp + 2], pts[mcp + 3] = pip, dip, tip
    return pts


def expect(name, ok):
    print(("  ok  " if ok else "  FAIL") + f"  {name}")
    if not ok:
        sys.exit(1)


print("1) pontuação: dedo reto ~1.0, no punho ~0.2")
open_hand = make_hand([0, 0, 0, 0], thumb_curl=0.0)
fist = make_hand([1, 1, 1, 1], thumb_curl=1.0)
_, s_open = S.finger_scores(open_hand)
_, s_fist = S.finger_scores(fist)
expect(f"retos altos {s_open}", all(s > 0.85 for s in s_open))
expect(f"punho baixo {s_fist}", all(s < 0.35 for s in s_fist))

print("2) contagem por pose (limiar fixo)")
cases = [
    ("punho -> 0", [1, 1, 1, 1], 1.0, 0),
    ("1 dedo -> 1", [0, 1, 1, 1], 1.0, 1),
    ("2 dedos -> 2", [0, 0, 1, 1], 1.0, 2),
    ("3 dedos -> 3", [0, 0, 0, 1], 1.0, 3),
    ("4 + polegar fora = palma (idle)", [0, 0, 0, 0], 0.0, 0),
    ("4 + polegar colado = gesto 4", [0, 0, 0, 0], 1.0, 4),
]
for name, curls, th, want_mode in cases:
    hand = make_hand(curls, thumb_curl=th)
    th_ext, n4, _, _ = S.count_fingers(hand)
    mode = S.mode_from_count(th_ext, n4)
    expect(f"{name} (n4={n4}, mode={mode})", mode == want_mode)

print("3) histerese: score oscilando no limiar NÃO faz flap")
cal = S.FingerCalib("t", 0.6, {})
seq = [0.58, 0.62] * 30  # oscila em torno do limiar
states = {cal.update(s, i * 0.05) for i, s in enumerate(seq)}
expect(f"estado constante durante oscilação ({states})", len(states) == 1)

print("4) aprendizado passivo: punho + palma separa os clusters")
cal2 = S.FingerCalib("i", 0.79, {})
t0 = 1000.0
for i in range(300):                      # 5 s de punho (curvado)
    cal2.update(s_fist[0], t0 + i * 0.05)
for i in range(300):                      # 5 s de palma (estendido)
    cal2.update(s_open[0], t0 + 600 * 0.05 + i * 0.05)
expect(f"limiar aprendido entre os clusters (thr={cal2.thr:.2f})",
       cal2.learned and 0.2 < cal2.thr < 0.9)
expect("punho lê curvado com limiar aprendido",
       cal2.update(s_fist[0], t0 + 999.0) is False)
expect("palma lê estendida com limiar aprendido",
       cal2.update(s_open[0], t0 + 1000.0) is True)

print("5) persistência: thr salva/carrega")
cal2._save()
loaded = S._load_calib_file()
expect(f"'i' salvo = {loaded.get('i')}", abs(loaded.get("i", -1) - cal2.thr) < 0.05)
os.remove(S.CALIB_SAVE)

print("6) modo_from_count regressão (regras do polegar)")
expect("palma = idle", S.mode_from_count(True, 4) == 0)
expect("4 sem polegar = escala", S.mode_from_count(False, 4) == 4)
expect("punho = idle", S.mode_from_count(False, 0) == 0)

print("7) guarda: janela SEM extremos reais NÃO aprende")
cal3 = S.FingerCalib("th", 0.31, {})
for i in range(300):                      # só samples no meio (0.70..0.90)
    cal3.update(0.70 + (i % 21) * 0.01, t0 + i * 0.05)
expect(f"limiar continua padrão ({cal3.thr:.2f})", not cal3.learned and cal3.thr == 0.31)

print("\nTODOS OS TESTES PASSARAM")
