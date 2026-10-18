# Diagnóstico — Hand Tracking + Manipulação de Painéis (Vortek Vision / AR-Metaverse)

> Fase 1 do plano de reconstrução da interação por mãos no estilo Meta Quest.
> **Fatos** = verificados no código/cena. **Hipóteses** = exigem confirmação em runtime.

---

## 1. Framework e mapa de módulos

- **Framework 3D:** Unity 6000.4.0f1 (app Android AR), cena `Assets/Scenes/ARMetaverseScene.unity`.
- **Painel:** GameObject `DisplayScreenQuad` (fileID `315225803`).
- **Câmera:** `ARCameraManager` (fileID `331264286`), câmera principal AR.
- **Hand tracking (detecção):** MediaPipe HandLandmarker roda **no PC** (`web app/handtracking/server.py`);
  o celular captura frames JPEG da câmera CPU e envia por WebSocket (porta 8787).
- **Transporte/coordenadas no celular:** `Assets/Scripts/HandTrackingControlScript.cs` (monolítico, 539 linhas).
- **Fallback por toque:** `Assets/Scripts/PlaceDisplayOnWallScript.cs` (arrasto/pinça/tap-place).

### Onde cada responsabilidade vive hoje (TUDO num script só)

| Responsabilidade | Arquivo / função |
| :--- | :--- |
| Detecção + landmarks | `server.py` (PC) |
| Captura de frame + WS | `HandTrackingControlScript.CaptureAndSendFrame` / `StartWebSocket` |
| Conversão coord. imagem→tela | `HandTrackingControlScript.HandToScreen` |
| Gesto (pinça) | `HandTrackingControlScript.ApplyHands` + limiar do `server.py` |
| Seleção/agarrio | `HandTrackingControlScript.ApplyHands` (inline) |
| Mover/girar/escalar | `HandTrackingControlScript.ApplyHands` (inline) |
| Colocação/ancoragem | `HandTrackingControlScript.PlaceAtGaze` (ARAnchor) |
| Feedback visual | **inexistente** |
| Diagnóstico | `verboseLogs` (0/1) |

---

## 2. Causas CONFIRMADAS (fatos)

**C1 — Dois controladores escrevem no mesmo Quad, simultaneamente.**
O GameObject `1133066359` tem `PlaceDisplayOnWallScript` (1133066366) **e**
`HandTrackingControlScript` (1133066368) ambos `m_Enabled: 1`, ambos com
`targetQuad: {fileID: 315225803}`. Escrevem `targetQuad.transform` por caminhos
diferentes (toque vs gesto) e ambos chamam `HidePlaneVisuals`/ ancoragem.
Conflito potencial quando um toque acidental acompanha um gesto.

**C2 — "Agarra em qualquer lugar" (teleporta).**
`ApplyHands`: quando `!prevPinching`, agarra **sem checar proximidade** com a
janela. Qualquer pinça em qualquer ponto da tela toma controle → a janela
"pula" pra mão. Não há estado de HOVER nem gate de seleção.

**C3 — Detecção de pinça frouxa (punho conta como pinça).**
`server.py` (original): `PINCH_CLOSE=0.45` só compara distância polegar–índice.
Punho fechado / mão parcialmente fechada também dá distância pequena → falsa
pinça → grab acidental. Não verificava extensão dos outros dedos.
*(parcialmente corrigido numa iteração anterior: agora exige ≥2 dedos abertos.)*

**C4 — Mão única acopla mover+girar+escalar sem dead zone.**
Branch single-hand aplica `roll` (rotação) **e** razão de tamanho (escala)
junto com a posição, todo frame, sem zona morta. Mover a mão muda
levemente o ângulo e o tamanho aparente → a janela gira/redimensiona sem
comando. Origem do "escalar/girar sai de controle".

**C5 — Sem feedback visual.**
Nenhum cursor, hover ou destaque. Impossível saber se a mão foi detectada,
qual painel está sob o cursor, ou quando o gesto foi reconhecido.

**C6 — Sem máquina de estados explícita.**
Estado espalhado em booleans soltos: `prevPinching`, `twoHandGrab`,
`hasPlaced`, `streamStarted`. Sem PINCH_CANDIDATE (confirmação temporal),
sem TRACKING_LOST distinto de "soltou", sem RELEASING.

**C7 — Transformação de coordenadas fixa e não verificável em runtime.**
`HandToScreen` usa `displayMatrix` + flip `(1-v)` com defaults fixos.
Sintoma relatado: movimento espelhado/invertido. Ajuste exige rebuild.

---

## 3. Hipóteses (exigem confirmação em runtime)

**H1 — Eixo espelhado exato** (X, Y ou ambos). Confirmar com cursor em runtime
+ flags de flip enviadas pelo servidor (já adicionadas: `fx`, `fy`).

**H2 — Troca de slot de mão** na histerese por índice (0/1) causa flicker de
estado quando MediaPipe inverte ordem das mãos entre frames.

**H3 — Perda de rastreamento == soltar**: `if (!anyPinch) → release`. Oclusão
momentânea libera o painel e o re-adquirir pode pular. Precisa distinguir
TRACKING_LOST de RELEASING intencional.

**H4 — Offset inicial**: o `grabOffset` é calculado no start, mas como o
grab acontece em qualquer lugar (C2), na prática o painel salta para a mão
em vez de preservar o deslocamento (viola a regra "não salte ao agarrar").

---

## 4. O que NÃO é problema (testado/lido)

- **Thread safety:** WS roda em thread pool → `ConcurrentQueue` → `Update()`
  desserializa e aplica. Nenhum acesso a `transform` fora da main thread.
  Não há bug de concorrência de thread nas transforms. ✔
- **Head tracking / auto-place:** `PlaceAtGaze` + `ARAnchor` funcionam
  (relato do usuário). Preservar. ✔
- **`ARResolutionControllerScript`** no mesmo GameObject: controle de resolução,
  não interfere em interação. ✔
- **Landmarks do MediaPipe são suficientes?** SIM. 21 pontos + handedness
  bastam para geometria de pinça, extensão de dedos, rotação (pulso→dedo médio)
  e escala relativa (pulso→MCP). **Não precisa treinar modelo novo.**

---

## 5. Plano por fase (resumo)

- **Fase 2 (rastreamento confiável):** separar HandTracking / GestureRecognition
  / InteractionStateMachine / SpatialManipulation / VisualFeedback / Diagnostics
  em módulos próprios; manter transporte+detecção existentes.
- **Fase 3 (uma mão):** hover por proximidade, pinça com histerese+confirmação,
  agarrio com offset preservado, mover, soltar.
- **Fase 4 (duas mãos):** escala com referência inicial estável; translação;
  rotação só se confiável.
- **Fase 5 (polimento):** feedback, suavização, recuperação de tracking,
  diagnóstico e testes (Unity Test Runner EditMode para lógica pura).
