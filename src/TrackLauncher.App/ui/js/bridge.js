// Мост между интерфейсом и C#.
// call('startTrack', { id }) → Promise с ответом C#. on('status', fn) — подписка на события ядра.
// Если страница открыта в обычном браузере (для вёрстки), вместо C# работает mock.js.

const webview = window.chrome?.webview;
const pending = new Map();
const listeners = new Map();
let nextId = 1;

const mock = webview ? null : await import('./mock.js');
export const isMock = !webview;

if (webview) {
  webview.addEventListener('message', event => {
    const msg = event.data;
    if (msg && msg.event) {
      emit(msg.event, msg.data);
      return;
    }
    const request = pending.get(msg?.id);
    if (!request) return;
    pending.delete(msg.id);
    if (msg.ok) request.resolve(msg.data);
    else request.reject(new Error(msg.error || 'Неизвестная ошибка'));
  });
}

export function call(action, payload = {}) {
  if (mock) return mock.handle(action, payload, emit);
  const id = nextId++;
  return new Promise((resolve, reject) => {
    pending.set(id, { resolve, reject });
    webview.postMessage({ id, action, payload });
  });
}

export function on(name, fn) {
  if (!listeners.has(name)) listeners.set(name, new Set());
  listeners.get(name).add(fn);
  return () => listeners.get(name)?.delete(fn);
}

export function emit(name, data) {
  for (const fn of listeners.get(name) ?? []) {
    try { fn(data); } catch (e) { console.error(e); }
  }
}
