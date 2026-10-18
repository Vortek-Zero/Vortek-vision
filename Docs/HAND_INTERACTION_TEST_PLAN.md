# Plano de testes — Hand Tracking + Manipulação (Vortek Vision)

> Fase 5. Mapeia os 12 critérios de aceitação e classifica cada verificação como
> **AUTOMATIZADA** (Unity Test Runner / EditMode, lógica pura) ou **MANUAL**
> (no celular com MediaPipe + PC). Nada é declarado como "passou" sem executar.

---

## A. Testes automatizados (lógica pura — EditMode)

Requerem colocar a lógica pura (`HandData`, `GestureRecognizer`,
`InteractionStateMachine`, `SpatialManipulator`) num asmdef próprio
(`Vortek.HandTracking.Core`) para o asmdef de testes poder referenciá-lo.
O controlador/cursor ficam em Assembly-CSharp (podem referenciar o Core).

| # | Critério | Teste | Arquivo sugerido |
| :-- | :--- | :--- | :--- |
| 1 | Mão aberta não agarra acidentalmente | `pinchDist` alto mantém `Idle` por 100 frames | `GestureRecognizerTests` |
| 2 | Pinça válida seleciona o painel | `Idle→Hovering→PinchCandidate→GrabbedOneHand` só com `hoverTarget=true` | `InteractionStateMachineTests` |
| 5 | Abrir os dedos solta confiavelmente | `Pinching` + `pinchDist>pinchOpen` → `Releasing` (uma vez) | `GestureRecognizerTests` |
| 6 | Perda de tracking não teletransporta | `present=0` por >`lostMs` durante grab → `TrackingLost` (não `Releasing`) e manipulador **não** é atualizado | `InteractionStateMachineTests` |
| 9 | Uma mão não move duas janelas | máquina só tem 1 sessão/sessão-única; `GrabbedOneHand` não emite 2 `GrabBegin` | `InteractionStateMachineTests` |
| 11 | Consistente com diferentes FPS | `SmoothApply` usa `1-exp(-k*dt)`; teste: mesmo deslocamento para dt=1/30 e dt=1/90 dentro de tolerância | `SpatialManipulatorTests` |

### Casos geométricos adicionais (Core)
- **Offset preservado:** `BeginSingle` com painel em P e mão em M →
  `TargetPositionSingle(M)` == P (sem salto).
- **Escala por razão:** `BeginTwo` com distância D0 → `ComputeTwo` com 2·D0
  produz escala ≈ 2× (limitada por `maxScaleMultiplier`), e a referência
  **não** muda entre frames.
- **Histerese:** `pinchDist` oscilando em torno de `pinchClose` NÃO alterna
  `Pinching/Idle` rapidamente.

---

## B. Testes manuais (no dispositivo — não automatizáveis aqui)

| # | Critério | Como verificar |
| :-- | :--- | :--- |
| 3 | Painel não salta ao agarrar | aproximar a mão do painel, pinçar OVER ele, confirmar que ele acompanha sem pular |
| 4 | Sem tremulação excessiva | mover a mão devagar; cursor/painel não devem vibrar |
| 7 | 2ª mão redimensiona sem saltos | segurar com 1 mão, pinçar com a 2ª, afastar → cresce suave |
| 8 | Soltar uma mão não deixa preso | em modo 2 mãos, abrir a 2ª → volta a 1 mão e continua seguindo a 1ª; abrir a 1ª → solta |
| 10 | UI responsiva durante tracking | FPS estável, sem travar ao mover a mão rápido |
| 12 | Câmera/head tracking seguem iguais | janela continua ancorada no mundo; auto-place no início da stream funciona |

### Variações manuais obrigatórias
- Mudanças rápidas de posição da mão.
- Oclusão (mão sai de campo / coberta).
- Mãos parcialmente visíveis (só pontas).
- Nenhuma mão detectada por vários segundos.
- Toque na tela (fallback `PlaceDisplayOnWallScript`) — não deve brigar com o gesto.

---

## C. Ainda NÃO verificado (pendente de execução)

- Compilação dos scripts C# novos (em andamento: checagem batch do Unity).
- Todos os testes automatizados acima (asmdef de Core + testes a criar).
- Todos os testes manuais (exigem celular + PC + MediaPipe rodando).

**Nenhum teste é dado como passado até ser executado.**
