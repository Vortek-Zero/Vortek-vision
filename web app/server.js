#!/usr/bin/env node
/**
 * AR Metaverse - Signaling & Static File Server (Linux/nativa)
 * ---------------------------------------------------------------------------
 * Substituto nativo do "webserver.exe" (Windows) incluído no projeto.
 *
 * Ele reimplementa, com Node.js puro e sem dependências, o servidor WebApp do
 * Unity Render Streaming 3.1.0:
 *   - Static file server apontando para a pasta "web app/" (public, public1,
 *     highspeedScreenShare, extra, ...)
 *   - Signaling WebRTC por WebSocket (modo "public"/broadcast), compatível com
 *     o SignalingManager do app Android e com o highSpeedModified.html.
 *
 * Uso:
 *   node server.js                 # porta 80 (precisa de sudo/root)
 *   node server.js --port 8080     # porta comum, sem sudo
 *   PORT=8080 node server.js       # idem, via variável de ambiente
 */

'use strict';

const http = require('http');
const fs = require('fs');
const path = require('path');
const os = require('os');
const crypto = require('crypto');

// ---------------------------------------------------------------------------
// Configuração
// ---------------------------------------------------------------------------
const argv = process.argv.slice(2);
function argValue(flag, fallback) {
  const i = argv.indexOf(flag);
  if (i !== -1 && argv[i + 1] !== undefined) return argv[i + 1];
  return fallback;
}

const ROOT = __dirname;
const PORT = parseInt(argValue('--port', process.env.PORT || '80'), 10);
const HOST = argValue('--host', process.env.HOST || '0.0.0.0');
const MODE = argValue('--mode', process.env.MODE || 'public'); // public | private

// --- Clique remoto (celular -> mouse do PC) ---
// O app AR manda a UV do ponto na janela (vídeo = tela cheia compartilhada).
// Wayland+GNOME: ydotool relativo (absoluto é ignorado); com accel 'flat' o
// movimento relativo é 1:1 exato. O perfil do mouse é salvo/restaurado a cada clique.
const CLICK_SCREEN_W = 1920;   // xdotool getdisplaygeometry (tela compartilhada)
const CLICK_SCREEN_H = 1080;
const CLICK_MIRROR_X = false;  // se o clique sair espelhado na horizontal, vire true
const CLICK_FLIP_Y = true;     // UV v=0 embaixo; pixel y=0 em cima
const YDOTOOL_SOCKET_PATH = process.env.YDOTOOL_SOCKET || '/run/user/1000/.ydotool_socket';

// Botões ydotool (validados no kernel: 0x40=down, 0x80=up, 0xC0=click p/ LEFT)
const YDOTOOL_BTN = { left: 0x00, right: 0x01, middle: 0x02 };

function runCmd(cmd, args, envExtra, timeoutMs) {
  const { execFile } = require('child_process');
  return new Promise((resolve) => {
    execFile(cmd, args, { env: { ...process.env, ...envExtra }, timeout: timeoutMs || 8000 },
      (err, stdout, stderr) => resolve({ ok: !err, out: String(stdout || ''), err: String((err && err.message) || stderr || '') }));
  });
}

// Perfil do mouse: cacheado no1º uso; restaurado após click/up (durante
// arraste o perfil fica flat — many gsettings por frame é innaceitável).
let savedMouse = null;
async function setMouse(key, val) {
  return runCmd('gsettings', ['set', 'org.gnome.desktop.peripherals.mouse', key, val], null);
}
async function getMouse(key) {
  return (await runCmd('gsettings', ['get', 'org.gnome.desktop.peripherals.mouse', key], null)).out.trim();
}

async function clickAtPixel(x, y, button, action) {
  const env = { YDOTOOL_SOCKET: YDOTOOL_SOCKET_PATH };
  const btn = YDOTOOL_BTN[button] !== undefined ? YDOTOOL_BTN[button] : 0;
  if (!savedMouse) {
    savedMouse = { prof: getClean(await getMouse('accel-profile')),
                   spd: getClean(await getMouse('speed')) };
  }
  await setMouse('accel-profile', 'flat');
  await setMouse('speed', '0.0');
  try {
    if (action !== 'none') {          // 'none' = só botão, sem reposicionar
      const m = await runCmd('xdotool', ['getmouselocation', '--shell'], null);
      const mm = m.out.match(/X=(\d+)\s+Y=(\d+)/);
      const cx = mm ? parseInt(mm[1], 10) : x;
      const cy = mm ? parseInt(mm[2], 10) : y;
      const dx = Math.round(x - cx), dy = Math.round(y - cy);
      if (dx !== 0 && dy !== 0)
        await runCmd('ydotool', ['mousemove', '-x', String(dx), '-y', String(dy)], env);
      else if (dx !== 0)
        await runCmd('ydotool', ['mousemove', '-x', String(dx), '-y', '0'], env);
      else if (dy !== 0)
        await runCmd('ydotool', ['mousemove', '-x', '0', '-y', String(dy)], env);
    }
    if (action === 'move') return { ok: 1, x, y, action };
    let code;
    if (action === 'down') code = 0x40 | btn;
    else if (action === 'up') code = 0x80 | btn;
    else code = 0xC0 | btn;                       // click = down+up
    const c = await runCmd('ydotool', ['click', '0x' + code.toString(16)], env);
    if (!c.ok) return { ok: 0, error: 'ydotool click: ' + c.err };
    if (action === 'click' || action === 'up') {  // fim do gesto: restaura perfil
      if (savedMouse.prof) await setMouse('accel-profile', savedMouse.prof);
      if (savedMouse.spd) await setMouse('speed', savedMouse.spd);
      savedMouse = null;
    }
    return { ok: 1, x, y, action };
  } catch (e) {
    return { ok: 0, error: String((e && e.message) || e) };
  }
}

// Rola o mouse (validado no kernel: REL_WHEEL via `mousemove -w -- 0 <dy>`)
async function scrollBy(dyRaw) {
  const dy = Math.max(-12, Math.min(12, Math.round(Number(dyRaw) || 0)));
  if (dy === 0) return { ok: 1, dy: 0 };
  const r = await runCmd('ydotool', ['mousemove', '-w', '--', '0', String(dy)],
                         { YDOTOOL_SOCKET: YDOTOOL_SOCKET_PATH });
  return r.ok ? { ok: 1, dy } : { ok: 0, error: 'ydotool scroll: ' + r.err };
}

function getClean(s) {
  return String(s || '').trim().replace(/^'|'$/g, ''); // gsettings devolve 'flat' com aspas
}

// --- Gerenciamento de janelas do PC (wmctrl) ---
// Limite real do Wayland/GNOME: só janelas XWayland aparecem no wmctrl.
// Janelas nativas Wayland (Chrome moderno, apps GTK4) NÃO são enumeráveis
// por aqui — é a restrição da plataforma, não do código.
const WIN_ID_RE = /^0x[0-9a-f]+$/i;

async function listWindows() {
  const r = await runCmd('wmctrl', ['-l', '-x'], null);
  if (!r.ok) return { ok: 0, error: 'wmctrl: ' + r.err, windows: [] };
  const windows = [];
  for (const line of r.out.split('\n')) {
    const m = line.match(/^(0x[0-9a-f]+)\s+(-?\d+)\s+(\S+)\s+(\S+)\s*(.*)$/i);
    if (!m) continue;
    windows.push({ id: m[1], desktop: parseInt(m[2], 10), wmClass: m[4], title: m[5] || m[4] });
  }
  return { ok: 1, windows };
}

async function windowOp(op, id) {
  if (!WIN_ID_RE.test(String(id || ''))) return { ok: 0, error: 'id de janela inválido' };
  let r;
  if (op === 'focus') {
    r = await runCmd('wmctrl', ['-i', '-a', id], null);
  } else if (op === 'minimize') {
    // Testado: wmctrl '-b add,hidden' é IGNORADO pelo Mutter; xdotool funciona.
    r = await runCmd('xdotool', ['windowminimize', id], { DISPLAY: ':0' });
  } else if (op === 'restore') {
    // Testado: focar a janela restaura do minimize (Mutter).
    r = await runCmd('wmctrl', ['-i', '-a', id], null);
  } else if (op === 'close') {
    r = await runCmd('wmctrl', ['-i', '-c', id], null); // WM_CLOSE (graceful, não mata processo)
  } else {
    return { ok: 0, error: 'operação desconhecida: ' + op };
  }
  return r.ok ? { ok: 1, op, id } : { ok: 0, error: op + ': ' + r.err };
}

function jsonRes(res, code, obj) {
  res.writeHead(code, { 'Content-Type': 'application/json', 'Access-Control-Allow-Origin': '*' });
  res.end(JSON.stringify(obj));
}

function clickParams(bodyObj, url) {
  const p = { ...(bodyObj || {}) };
  const q = String((url || '').split('?')[1] || '').split('&');
  for (const kv of q) {
    const i = kv.indexOf('=');
    if (i > 0) {
      try { p[decodeURIComponent(kv.slice(0, i))] = decodeURIComponent(kv.slice(i + 1)); }
      catch (_e) { /* ignora */ }
    }
  }
  return p;
}

async function handleClick(p, res) {
  const done = (code, obj) => {
    res.writeHead(code, { 'Content-Type': 'application/json', 'Access-Control-Allow-Origin': '*' });
    res.end(JSON.stringify(obj));
  };
  try {
    const u = parseFloat(p.u), v = parseFloat(p.v);
    if (!(u >= 0 && u <= 1 && v >= 0 && v <= 1)) {
      done(400, { ok: 0, error: 'u/v precisam estar em 0..1' });
      return;
    }
    const uu = CLICK_MIRROR_X ? 1 - u : u;
    const vv = CLICK_FLIP_Y ? 1 - v : v;
    const x = Math.round(uu * (CLICK_SCREEN_W - 1));
    const y = Math.round(vv * (CLICK_SCREEN_H - 1));
    if (p.dry === true || p.dry === '1' || p.dry === 1) {
      done(200, { ok: 1, dry: 1, x, y });
      return;
    }
    const button = ['left', 'right', 'middle'].includes(p.button) ? p.button : 'left';
    const action = ['click', 'down', 'up', 'move'].includes(p.action) ? p.action : 'click';
    const r = await clickAtPixel(x, y, button, action);
    done(r.ok ? 200 : 500, r);
  } catch (e) {
    done(500, { ok: 0, error: String((e && e.message) || e) });
  }
}

const MIME = {
  '.html': 'text/html; charset=utf-8',
  '.htm': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.mjs': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.png': 'image/png',
  '.jpg': 'image/jpeg',
  '.jpeg': 'image/jpeg',
  '.gif': 'image/gif',
  '.svg': 'image/svg+xml',
  '.ico': 'image/x-icon',
  '.webm': 'video/webm',
  '.mp4': 'video/mp4',
  '.txt': 'text/plain; charset=utf-8',
  '.wasm': 'application/wasm',
};

// ---------------------------------------------------------------------------
// Sinalização (espelho fiel de WebApp/src/class/websockethandler.ts)
// ---------------------------------------------------------------------------
let isPrivate = MODE === 'private';

/** ws -> Set<connectionId> */
const clients = new Map();
/** connectionId -> [wsOfferente, wsAnswerador] */
const connectionPair = new Map();

function getOrCreateConnectionIds(ws) {
  if (!clients.has(ws)) clients.set(ws, new Set());
  return clients.get(ws);
}

function add(ws) {
  clients.set(ws, new Set());
}

function remove(ws) {
  const connectionIds = clients.get(ws) || new Set();
  connectionIds.forEach((connectionId) => {
    const pair = connectionPair.get(connectionId);
    if (pair) {
      const other = pair[0] === ws ? pair[1] : pair[0];
      if (other) other.sendJson({ type: 'disconnect', connectionId });
    }
    connectionPair.delete(connectionId);
  });
  clients.delete(ws);
  log(`[signal] cliente removido (${clients.size} ativo${clients.size === 1 ? '' : 's'})`);
}

function onConnect(ws, connectionId) {
  let polite = true;
  if (isPrivate) {
    if (connectionPair.has(connectionId)) {
      const pair = connectionPair.get(connectionId);
      if (pair[0] != null && pair[1] != null) {
        ws.sendJson({ type: 'error', message: `${connectionId}: This connection id is already used.` });
        return;
      } else if (pair[0] != null) {
        connectionPair.set(connectionId, [pair[0], ws]);
      }
    } else {
      connectionPair.set(connectionId, [ws, null]);
      polite = false;
    }
  }
  getOrCreateConnectionIds(ws).add(connectionId);
  ws.sendJson({ type: 'connect', connectionId, polite });
  log(`[signal] connect '${connectionId}'`);
}

function onDisconnect(ws, connectionId) {
  const connectionIds = clients.get(ws);
  if (connectionIds) connectionIds.delete(connectionId);

  if (connectionPair.has(connectionId)) {
    const pair = connectionPair.get(connectionId);
    const other = pair[0] === ws ? pair[1] : pair[0];
    if (other) other.sendJson({ type: 'disconnect', connectionId });
  }
  connectionPair.delete(connectionId);
  ws.sendJson({ type: 'disconnect', connectionId });
}

function broadcastExcept(ws, payload) {
  const msg = JSON.stringify(payload);
  clients.forEach((_v, k) => {
    if (k === ws) return;
    k.sendText(msg);
  });
}

function onOffer(ws, message) {
  const connectionId = message.connectionId;
  const newOffer = { sdp: message.sdp, timestamp: Date.now(), polite: false };

  if (isPrivate) {
    if (connectionPair.has(connectionId)) {
      const pair = connectionPair.get(connectionId);
      const other = pair[0] === ws ? pair[1] : pair[0];
      if (other) {
        newOffer.polite = true;
        other.sendJson({ from: connectionId, to: '', type: 'offer', data: newOffer });
      }
    }
    return;
  }

  connectionPair.set(connectionId, [ws, null]);
  broadcastExcept(ws, { from: connectionId, to: '', type: 'offer', data: newOffer });
}

function onAnswer(ws, message) {
  const connectionId = message.connectionId;
  getOrCreateConnectionIds(ws).add(connectionId);
  const newAnswer = { sdp: message.sdp, timestamp: Date.now() };

  if (!connectionPair.has(connectionId)) return;

  const pair = connectionPair.get(connectionId);
  const other = pair[0] === ws ? pair[1] : pair[0];
  if (!isPrivate) connectionPair.set(connectionId, [other, ws]);
  if (other) other.sendJson({ from: connectionId, to: '', type: 'answer', data: newAnswer });
}

function onCandidate(ws, message) {
  const connectionId = message.connectionId;
  const candidate = {
    candidate: message.candidate,
    sdpMLineIndex: message.sdpMLineIndex,
    sdpMid: message.sdpMid,
    timestamp: Date.now(),
  };

  if (isPrivate) {
    if (connectionPair.has(connectionId)) {
      const pair = connectionPair.get(connectionId);
      const other = pair[0] === ws ? pair[1] : pair[0];
      if (other) other.sendJson({ from: connectionId, to: '', type: 'candidate', data: candidate });
    }
    return;
  }

  broadcastExcept(ws, { from: connectionId, to: '', type: 'candidate', data: candidate });
}

// ---------------------------------------------------------------------------
// WebSocket (RFC 6455) - implementação mínima, sem dependências
// ---------------------------------------------------------------------------
const WS_GUID = '258EAFA5-E914-47DA-95CA-C5AB0DC85B11';

function acceptKey(key) {
  return crypto.createHash('sha1').update(key + WS_GUID).digest('base64');
}

function encodeFrame(opcode, payload) {
  const len = payload.length;
  let header;
  if (len < 126) {
    header = Buffer.alloc(2);
    header[1] = len;
  } else if (len < 65536) {
    header = Buffer.alloc(4);
    header[1] = 126;
    header.writeUInt16BE(len, 2);
  } else {
    header = Buffer.alloc(10);
    header[1] = 127;
    header.writeUInt32BE(0, 2);
    header.writeUInt32BE(len, 6);
  }
  header[0] = 0x80 | opcode; // FIN + opcode
  return Buffer.concat([header, payload]);
}

class WSConnection {
  constructor(socket) {
    this.socket = socket;
    this.buffer = Buffer.alloc(0);
    this.alive = true;
    this.isAlive = () => this.alive && !this.socket.destroyed;

    socket.on('data', (chunk) => this.onData(chunk));
    socket.on('error', () => this.destroy());
    socket.on('close', () => this.destroy());
  }

  sendText(text) {
    if (!this.isAlive()) return;
    try {
      this.socket.write(encodeFrame(0x1, Buffer.from(text, 'utf8')));
    } catch (_e) {
      this.destroy();
    }
  }

  sendJson(obj) {
    this.sendText(JSON.stringify(obj));
  }

  ping() {
    if (!this.isAlive()) return;
    try {
      this.socket.write(encodeFrame(0x9, Buffer.alloc(0)));
    } catch (_e) {
      this.destroy();
    }
  }

  close(code = 1000) {
    if (!this.isAlive()) return;
    const payload = Buffer.alloc(2);
    payload.writeUInt16BE(code, 0);
    try {
      this.socket.write(encodeFrame(0x8, payload));
    } catch (_e) { /* ignore */ }
    this.destroy();
  }

  destroy() {
    if (!this.alive) return;
    this.alive = false;
    try {
      this.socket.destroy();
    } catch (_e) { /* ignore */ }
    remove(this);
  }

  onData(chunk) {
    this.buffer = Buffer.concat([this.buffer, chunk]);

    // Limita o buffer para evitar consumo excessivo de memória.
    if (this.buffer.length > 64 * 1024 * 1024) {
      this.close(1009);
      return;
    }

    while (this.alive) {
      const buf = this.buffer;
      if (buf.length < 2) break;

      const fin = (buf[0] & 0x80) !== 0;
      const opcode = buf[0] & 0x0f;
      const masked = (buf[1] & 0x80) !== 0;
      let payloadLen = buf[1] & 0x7f;
      let offset = 2;

      if (payloadLen === 126) {
        if (buf.length < offset + 2) break;
        payloadLen = buf.readUInt16BE(offset);
        offset += 2;
      } else if (payloadLen === 127) {
        if (buf.length < offset + 8) break;
        const hi = buf.readUInt32BE(offset);
        const lo = buf.readUInt32BE(offset + 4);
        payloadLen = hi * 4294967296 + lo;
        offset += 8;
      }

      let maskKey = null;
      if (masked) {
        if (buf.length < offset + 4) break;
        maskKey = buf.subarray(offset, offset + 4);
        offset += 4;
      }

      if (buf.length < offset + payloadLen) break;

      let payload = buf.subarray(offset, offset + payloadLen);
      if (maskKey) {
        const unmasked = Buffer.allocUnsafe(payloadLen);
        for (let i = 0; i < payloadLen; i++) {
          unmasked[i] = payload[i] ^ maskKey[i & 3];
        }
        payload = unmasked;
      }

      this.buffer = buf.subarray(offset + payloadLen);

      switch (opcode) {
        case 0x0: // continuação (mensagens grandes fatiadas)
        case 0x1: // texto
        case 0x2: // binário
          if (!fin) {
            // Mensagem fragmentada: acumula até chegar o frame FIN.
            this.fragBuffer = Buffer.concat([this.fragBuffer || Buffer.alloc(0), payload]);
            continue;
          }
          if (this.fragBuffer) {
            payload = Buffer.concat([this.fragBuffer, payload]);
            this.fragBuffer = null;
          }
          this.handleMessage(payload);
          break;
        case 0x8: // close
          this.close(1000);
          return;
        case 0x9: // ping -> pong
          try {
            this.socket.write(encodeFrame(0xa, payload));
          } catch (_e) {
            this.destroy();
          }
          break;
        case 0xa: // pong
          break;
        default:
          this.close(1002);
          return;
      }
    }
  }

  handleMessage(payload) {
    let text;
    try {
      text = payload.toString('utf8');
    } catch (_e) {
      return;
    }

    let msg;
    try {
      msg = JSON.parse(text);
    } catch (_e) {
      log('[signal] mensagem JSON inválida ignorada');
      return;
    }
    if (!msg || typeof msg.type !== 'string') return;

    switch (msg.type) {
      case 'connect':
        onConnect(this, msg.connectionId);
        break;
      case 'disconnect':
        onDisconnect(this, msg.connectionId);
        break;
      case 'offer':
        onOffer(this, msg.data || {});
        break;
      case 'answer':
        onAnswer(this, msg.data || {});
        break;
      case 'candidate':
        onCandidate(this, msg.data || {});
        break;
      case 'ping':
        this.sendJson({ type: 'pong' });
        break;
      default:
        break;
    }
  }
}

// ---------------------------------------------------------------------------
// Static file server + HTTP
// ---------------------------------------------------------------------------
function sendFile(res, filePath) {
  fs.stat(filePath, (err, stat) => {
    if (err || !stat.isFile()) {
      res.writeHead(404, { 'Content-Type': 'text/plain; charset=utf-8' });
      res.end('404 Not Found');
      return;
    }
    const ext = path.extname(filePath).toLowerCase();
    res.writeHead(200, {
      'Content-Type': MIME[ext] || 'application/octet-stream',
      'Content-Length': stat.size,
      'Cache-Control': 'no-cache',
      'Access-Control-Allow-Origin': '*',
    });
    fs.createReadStream(filePath).pipe(res);
  });
}

const LANDING_PAGE = `<!DOCTYPE html>
<html lang="pt-BR"><head><meta charset="utf-8"><title>AR Metaverse - Servidor</title>
<style>body{font-family:system-ui,sans-serif;background:#0f172a;color:#f8fafc;display:flex;justify-content:center;align-items:center;min-height:100vh;margin:0}
div{background:#1e293b;border:1px solid #334155;border-radius:16px;padding:32px;max-width:560px}
a{color:#38bdf8;display:block;margin:10px 0}code{color:#94a3b8}</style></head>
<body><div><h1>&#128640; Servidor AR Metaverse no ar</h1>
<p>Signaling WebRTC (WebSocket) ativo na porta <strong>${PORT}</strong>.</p>
<h2>P&aacute;ginas</h2>
<a href="/highspeedScreenShare/highSpeedModified.html">/highspeedScreenShare/highSpeedModified.html</a>
<a href="/public1/screenshare1.html">/public1/screenshare1.html</a>
<a href="/public1/screenshare.html">/public1/screenshare.html</a>
<a href="/public/screenshare.html">/public/screenshare.html</a>
<h2>Como usar</h2>
<p>No PC: abra a p&aacute;ginas acima, informe <code>127.0.0.1:${PORT}</code> e compartilhe a tela.</p>
<p>No celular: no app Unity, informe <code>&lt;IP deste PC&gt;:${PORT}</code>.</p>
</div></body></html>`;

const server = http.createServer((req, res) => {
  let urlPath;
  try {
    urlPath = decodeURIComponent((req.url || '/').split('?')[0]);
  } catch (_e) {
    urlPath = '/';
  }

  // --- Janelas do PC: lista + operações reais (wmctrl) ---
  if (urlPath === '/windows') {
    listWindows().then((r) => jsonRes(res, r.ok ? 200 : 500, r));
    return;
  }
  if (urlPath.startsWith('/window/')) {
    const op = urlPath.slice('/window/'.length);
    const id = clickParams({}, req.url || '').id;
    windowOp(op, id).then((r) => jsonRes(res, r.ok ? 200 : 400, r));
    return;
  }

  // Endpoint de configuração, igual ao WebApp oficial do Unity Render Streaming.
  if (urlPath === '/config') {
    res.writeHead(200, { 'Content-Type': 'application/json', 'Access-Control-Allow-Origin': '*' });
    res.end(JSON.stringify({ useWebSocket: true, startupMode: MODE, logging: 'dev' }));
    return;
  }

  // Rolagem remota do celular (POST /scroll {dy} | GET /scroll?dy=).
  if (urlPath === '/scroll') {
    const handle = (p) => scrollBy(p.dy).then((r) => jsonRes(res, r.ok ? 200 : 500, r));
    if (req.method === 'POST') {
      let body = '';
      req.on('data', (c) => { body += c; if (body.length > 8192) { try { req.destroy(); } catch (_e) {} } });
      req.on('end', () => {
        let p = {};
        try { p = JSON.parse(body || '{}'); } catch (_e) { p = {}; }
        handle(clickParams(p, req.url || ''));
      });
    } else {
      handle(clickParams({}, req.url || ''));
    }
    return;
  }

  // Clique remoto do celular na janela AR (POST /click {u,v[,button,action]} | GET /click?u=&v=[&dry=1]).
  if (urlPath === '/click') {
    if (req.method === 'POST') {
      let body = '';
      req.on('data', (c) => { body += c; if (body.length > 8192) { try { req.destroy(); } catch (_e) {} } });
      req.on('end', () => {
        let p = {};
        try { p = JSON.parse(body || '{}'); } catch (_e) { p = {}; }
        handleClick(clickParams(p, req.url || ''), res);
      });
    } else if (req.method === 'GET') {
      handleClick(clickParams({}, req.url || ''), res);
    } else {
      res.writeHead(405, { 'Content-Type': 'application/json' });
      res.end(JSON.stringify({ ok: 0, error: 'use POST ou GET' }));
    }
    return;
  }

  if (urlPath === '/' || urlPath === '/index.html') {
    res.writeHead(200, { 'Content-Type': MIME['.html'] });
    res.end(LANDING_PAGE);
    return;
  }

  // Normaliza e previne path traversal.
  const safePath = path.normalize(urlPath).replace(/^(\.\.[/\\])+/, '');
  let filePath = path.join(ROOT, safePath);

  if (!filePath.startsWith(ROOT)) {
    res.writeHead(403, { 'Content-Type': 'text/plain; charset=utf-8' });
    res.end('403 Forbidden');
    return;
  }

  if (urlPath.endsWith('/')) {
    filePath = path.join(filePath, 'index.html');
  }

  fs.stat(filePath, (err, stat) => {
    if (!err && stat.isDirectory()) {
      const indexPath = path.join(filePath, 'index.html');
      if (fs.existsSync(indexPath)) {
        sendFile(res, indexPath);
        return;
      }
      // Lista simples do diretório.
      const entries = fs.readdirSync(filePath, { withFileTypes: true })
        .filter((e) => !e.name.endsWith('.meta') && !e.name.startsWith('.'))
        .map((e) => `<a href="${urlPath}${e.name}${e.isDirectory() ? '/' : ''}">${e.name}${e.isDirectory() ? '/' : ''}</a>`)
        .join('');
      res.writeHead(200, { 'Content-Type': MIME['.html'] });
      res.end(`<!DOCTYPE html><html><head><meta charset="utf-8"><title>${urlPath}</title>
<style>body{font-family:system-ui;background:#0f172a;color:#f8fafc;padding:32px}a{color:#38bdf8;display:block;margin:6px 0}</style></head>
<body><h1>${urlPath}</h1>${entries}</body></html>`);
      return;
    }
    sendFile(res, filePath);
  });
});

server.on('upgrade', (req, socket) => {
  const key = req.headers['sec-websocket-key'];
  const version = req.headers['sec-websocket-version'];
  if (!key || version !== '13') {
    socket.write('HTTP/1.1 400 Bad Request\r\n\r\n');
    socket.destroy();
    return;
  }

  socket.write(
    'HTTP/1.1 101 Switching Protocols\r\n' +
    'Upgrade: websocket\r\n' +
    'Connection: Upgrade\r\n' +
    `Sec-WebSocket-Accept: ${acceptKey(key)}\r\n` +
    '\r\n'
  );
  socket.setNoDelay(true);

  const ws = new WSConnection(socket);
  add(ws);
  log(`[signal] novo cliente WebSocket conectado (total: ${clients.size})`);
});

// Keepalive: fecha conexões mortas e detecta clientes zumbis.
setInterval(() => {
  clients.forEach((_, ws) => ws.ping());
}, 15000);

// ---------------------------------------------------------------------------
// Boot
// ---------------------------------------------------------------------------
function log(msg) {
  console.log(`[${new Date().toISOString()}] ${msg}`);
}

function lanAddresses() {
  const out = [];
  const ifs = os.networkInterfaces();
  for (const name of Object.keys(ifs)) {
    for (const iface of ifs[name] || []) {
      if (iface.family === 'IPv4' && !iface.internal) out.push(`${iface.address}:${PORT}`);
    }
  }
  return out;
}

server.listen(PORT, HOST, () => {
  console.log('================================================================');
  console.log('  AR Metaverse - servidor de sinalização (nativo, sem wine)');
  console.log('================================================================');
  console.log(`  Modo de signaling : ${MODE}`);
  lanAddresses().forEach((a) => console.log(`  Acessível em      : http://${a}`));
  console.log(`  Local             : http://127.0.0.1:${PORT}`);
  console.log('  WebSocket         : ws://<IP>:<porta>  (qualquer caminho)');
  console.log('  Página principal  : /highspeedScreenShare/highSpeedModified.html');
  console.log('================================================================');
});

process.on('SIGINT', () => {
  console.log('\nEncerrando...');
  clients.forEach((_, ws) => ws.close());
  server.close(() => process.exit(0));
  setTimeout(() => process.exit(0), 500).unref();
});
