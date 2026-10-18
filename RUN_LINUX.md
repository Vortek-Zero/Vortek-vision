# 🐧 Rodando o AR Metaverse no Linux

Guia prático para rodar este projeto em Linux. O `webserver.exe` original é um
binário Windows (Node.js empacotado com `pkg`) — ele **não roda no Linux**, nem
bem sob Wine (o runtime Node embutido quebra ao herdar um stdout que não seja
um TTY). Por isso existe o substituto nativo incluído aqui:

| Arquivo | O que é |
| :--- | :--- |
| `web app/server.js` | Servidor de sinalização WebRTC + arquivos estáticos, em Node.js puro (**zero dependências**, `npm install` não é necessário). Reimplementa fielmente o WebApp do Unity Render Streaming `3.1.0-exp.9`, que é o pacote usado pelo projeto. |
| `web app/run.sh` | Inicializador que sobe na porta 80 (pedindo sudo) ou numa porta comum. |

---

## 1. Subir o servidor (sinalização + web app)

```bash
cd "web app"
./run.sh          # porta 80, igual ao webserver.exe original (pede sudo)
# ou, sem sudo:
./run.sh 8080
```

Saída esperada:

```
================================================================
  AR Metaverse - servidor de sinalização (nativo, sem wine)
================================================================
  Modo de signaling : public
  Acessível em      : http://192.168.1.102:80
  Local             : http://127.0.0.1:80
  WebSocket         : ws://<IP>:<porta>  (qualquer caminho)
  Página principal  : /highspeedScreenShare/highSpeedModified.html
================================================================
```

> Sempre que o `IP` mostrado mudar (DHCP), basta reiniciar o servidor — ele
> anuncia os IPs atuais no boot.

## 2. Compartilhar a tela no PC

Abra no navegador **do próprio PC** (Chrome/Edge):

```
http://127.0.0.1/highspeedScreenShare/highSpeedModified.html
```

1. O campo de servidor já vem com `127.0.0.1:80` (padrão ajustado para esta
   máquina) — clique em **Connect**.
2. **1. Capture Screen** → escolha a tela ou janela.
3. **2. Stream to Unity**.

> ⚠️ O `getDisplayMedia()` (captura de tela) só funciona em contexto seguro:
> por isso a página tem que ser aberta como `localhost`, e não pelo IP da LAN.
> O celular/app Unity, por outro lado, conecta pelo IP da LAN normalmente.

## 3. Conectar o app Android

1. Instale o Unity Hub e o Editor **6000.4.0f1** (a versão em
   `ProjectSettings/ProjectVersion.txt`) com o módulo **Android Build Support**
   (incluindo OpenJDK + SDK & NDK Tools).
2. Abra a pasta do projeto no Hub.
3. Cenários principais (ver `README.md`):
   - Android (celular): `Assets/Scenes/RemoteRenderingScenes/imax/AndroidAppScene_imax.unity`
   - PC host: `Assets/Scenes/RemoteRenderingScenes/imax/DesktopAppScene_imax.unity`
4. **File > Build Settings > Android > Build And Run** (celular conectado via
   ADB com depuração USB).
5. No app, digite o IP do PC (ex.: `192.168.1.102`) e toque em **Connect**.

## 4. Testar o signaling sem celular nenhum

```bash
node -e '
const u = new WebSocket("ws://127.0.0.1:80");   // "app Unity"
const b = new WebSocket("ws://127.0.0.1:80");   // "browser"
u.onmessage = e => {
  const m = JSON.parse(e.data);
  if (m.type === "connect") u.send(JSON.stringify({type:"connect", connectionId:"windowsStream"}));
  if (m.type === "offer")   u.send(JSON.stringify({type:"answer", from:"windowsStream", data:{sdp:"A", type:"answer", connectionId:"ScreenStream"}}));
};
b.onopen = () => b.send(JSON.stringify({type:"offer", from:"ScreenStream", data:{sdp:"O", type:"offer", polite:true, connectionId:"ScreenStream"}}));
b.onmessage = e => { const m = JSON.parse(e.data); if (m.type === "answer") { console.log("OK:", e.data); process.exit(0); } };
setTimeout(() => process.exit(1), 3000);'
```

---

## 5. Controle por gestos de mão (MediaPipe no PC)

O app AR de pantalla cheia controla a janela **sem tocar na tela**. O celular
só captura os frames da câmera e manda pro PC; o PC roda o MediaPipe
(HandLandmarker) e devolve os gestos.

```bash
cd "web app/handtracking"
./run.sh            # sobe em ws://0.0.0.0:8787
```

Requisitos (já instalados nesta máquina):

- Python 3 + `mediapipe`, `websockets`, `opencv-python-headless` (venv em
  `web app/handtracking/.venv`).
- Modelo `hand_landmarker.task` dentro da pasta `handtracking/`.

Como funciona o fluxo:

1. No PC: `./run.sh` (servidor MediaPipe, porta **8787**).
2. Abra o app AR no celular — assim que a stream de vídeo iniciar, a janela
   aparece **no ponto em que você está olhando** (raycast no centro da tela).
3. Gestos — **contagem de dedos = modo** (qualquer ângulo; o servidor decide):

   | Dedos | Modo | O que faz |
   | :--- | :--- | :--- |
   | **1** | Cursor | **raycast**: o cursor **gruda onde o raio bate na janela** |
   | **2** | Mover | **raycast p/ agarrar**: precisa **apontar p/ a janela**; o centro dela segue o **meio dos 2 dedos** |
   | **3** | Mirar | a janela **cola na pose do celular** — você segura os 3 dedos e mira onde quer a tela |
   | **4** | Escala | **slider vertical**: descer a mão = maior, subir = menor |
   | **0/5** | Ocioso | punho solto ou palma aberta: a janela fica parada onde estava |
   | **punho segurado 0.8s** | Trava | **confirma/congela** a janela (contorno vermelho); outro punho destrava. Vale p/ 3 e 4 dedos |
   | **toque na tela** | Clique | com o cursor sobre a janela, **tocar a tela do celular = clique no PC** no ponto do cursor |

   - No **escuro**, o servidor aplica **glow adaptativo** (clareia o frame
     **só p/ a detecção**; você não vê nada; no claro ele não atua).
     Ver `GLOW_MEAN_THRESH` no `server.py`.

   - Perder a mão (oclusão) = segura a última posição (não teleporta).
   - Sem gesto, a janela **não se mexe**.
4. A janela fica levemente inclinada, tipo monitor (`tiltDegrees`, padrão 10°).
5. **Diagnóstico:** tecla **D** abre um overlay com modo ativo, nº de dedos,
   latência do servidor e o estado vindo do servidor.

> **Toda a lógica de gesto mora NO SERVIDOR** (`server.py`). Pra trocar um
> gesto, ajustar sensibilidade, ganho do potenciômetro (`SCALE_GAIN`,
> `ROLL_PER_TURN_DEG`), limiares de dedo (`STRAIGHT_DEG`) ou limites de escala
> (`SCALE_MIN/MAX`): **edite `server.py` e reinicie** — vale na hora, **sem
> rebuild do APK**.
>
> **Calibração automática dos gestos:** os limiares de dedo se aprendem sozinhos
> durante o uso normal (janela de pontuações + percentis + histerese; método
> "accumulated angles" do paper MediaPipe Hands). Nenhum botão de calibração: você
> só usa o sistema. Os limiares aprendidos persistem em
> `handtracking/gesture_calib.json` — apague o arquivo pra recomeçar do zero.
> Log: linhas `[calib]` mostram quando um limiar é aprendido; os scores por dedo
> aparecem no `[brain]` como `sc=...`.
>
> **Janelas reais do PC (Etapa 3):** `GET /windows` (lista via wmctrl) e
> `POST /window/{focus,minimize,restore,close}?id=0x...` — testados contra uma
> janela real (mousepad): focus/minimize (xdotool)/restore/close (WM_CLOSE) ✓.
> **Limite do Wayland:** só janelas XWayland aparecem na lista; janelas nativas
> Wayland (Chrome moderno etc.) não são enumeráveis. No app: hotbar ancorada na
> câmera (HOME / JANELAS / LAYOUT / CONFIG / PC) + controles X - [] O acima da
> janela; toque na tela = ação de UI (não vira clique no PC).
>
> **Toque na tela (redesign):** 1 toque curto = clique; toque + mover sobre o
> conteúdo = arraste (down/move/up); 2 toques + arrastar vertical = rolagem
> (`POST /scroll {dy}`); 2 toques curtos = botão direito — tudo validado no
> kernel (BTN_LEFT/RIGHT + REL_WHEEL do ydotool).
>
> **Bimanual:** o cérebro detecta as 2 mãos (`num_hands=2`). Com 2 mãos, a
> ESQUERDA dirige os modos (espaço) e a DIREITA é o ponteiro (conteúdo). Com 1
> mão, ela faz os dois papéis. Laterais trocadas? Vire `FLIP_HANDS` no
> `server.py` e reinicie (sem build).
>
> Se o movimento sair **espelhado/invertido**, ajuste pelo servidor (sem
> rebuild): `./run.sh --no-flip-x`, `--flip-y` (padrão `flipX=1 flipY=0`).

Detalhes técnicos (celular = executor fino; cérebro = PC):

- **Transporte** — `HandTrackingControlScript.cs`: captura
  `TryAcquireLatestCpuImage` → JPEG (480px, ~12fps) → `ClientWebSocket`;
  expõe `ImageToScreen()` (displayMatrix + flags `fx/fy`) e emite
  `OnCommand` (o comando pronto do servidor); ainda faz o auto-place.
- **Modelo** — `HandData.cs`: `HandCommand` (bate com o JSON do server) +
  enum `HandMode` (Idle/Cursor/Move/Place/Scale).
- **Executor** — `HandPanelInteractionController.cs`: só executa o modo
  recebido (cursor/mover/mirar/escala); não interpreta gesto.
- **Feedback** — `HandCursorFeedback.cs`: **cursor-raio** da ponta do dedo +
  contorno do painel, cor por modo.
- **Diagnóstico** — `HandTrackingDiagnostics.cs` (overlay, tecla D).
- **PC (cérebro)** — `server.py`: HandLandmarker (VIDEO) + **contagem de
  dedos** (ângulo na junta; polegar desambiguador) + **debounce** de modo +
  potenciômetro de escala; devolve `mode,present,count,u,v,scaleMul,
  minScale,maxScale,fx,fy,state`.
- Se o servidor cair, o toque na tela continua como fallback
  (`PlaceDisplayOnWallScript`).

---

## Notas de implementação

- O protocolo replicado é o do `WebApp/src/class/websockethandler.ts` do Unity
  Render Streaming (modo `public`/broadcast): `connect`, `disconnect`, `offer`,
  `answer`, `candidate`, além de `/config` e ping/pong WebSocket (RFC 6455,
  implementado à mão sem dependências).
- O `connectionId` usado pelo app Android é `windowsStream`
  (`StreamConnectionHandlerAndroidScript.cs`) e o do browser é `ScreenStream`
  (`highSpeedModified.html`).
- O servidor também serve qualquer pasta dentro de `web app/`
  (`public/`, `public1/`, `extra/`, ...), com listagem de diretório automática.
