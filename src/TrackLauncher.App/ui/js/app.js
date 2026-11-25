// Точка входа интерфейса: загрузка данных, маршруты (#tracks, #edit/id, #stats, #settings), события ядра.

import { call, on, isMock } from './bridge.js';
import { state, set, subscribe, trackById } from './state.js';
import { h, mount, toast, fmtClock, fmtMinutes } from './dom.js';
import * as tracksView from './views/tracks.js';
import * as editorView from './views/editor.js';
import * as statsView from './views/stats.js';
import * as settingsView from './views/settings.js';

const viewEl = document.getElementById('view');
let current = null;          // { cleanup, canLeave }
let currentHash = '';
let ignoreHashChange = false;

const routes = {
  tracks: () => tracksView.render(viewEl),
  edit: id => editorView.render(viewEl, id),
  new: () => editorView.render(viewEl, null),
  stats: () => statsView.render(viewEl),
  settings: () => settingsView.render(viewEl),
};

async function route() {
  if (ignoreHashChange) { ignoreHashChange = false; return; }

  const hash = location.hash.replace(/^#\/?/, '') || 'tracks';
  if (current?.canLeave && !(await current.canLeave())) {
    // Остаёмся на месте (например, в редакторе с несохранёнными изменениями).
    ignoreHashChange = true;
    location.hash = currentHash;
    return;
  }

  const [name, ...rest] = hash.split('/');
  const handler = routes[name] ?? routes.tracks;
  current?.cleanup?.();
  current = null;
  currentHash = hash;

  document.querySelectorAll('.nav-link').forEach(a =>
    a.classList.toggle('active', a.dataset.route === (name === 'edit' || name === 'new' ? 'tracks' : name)));

  viewEl.scrollTop = 0;
  try {
    current = (await handler(decodeURIComponent(rest.join('/')))) ?? null;
  } catch (e) {
    console.error(e);
    mount(viewEl, h('div', { class: 'notice error' }, `Не удалось открыть экран: ${e.message}`));
  }
}

export function go(hash) {
  location.hash = hash;
}

export async function reloadTracks() {
  const data = await call('getTracks');
  set('loadErrors', data.loadErrors);
  set('tracks', data.tracks);
}

// ───────────── Мини-статус в боковой панели ─────────────

const mini = document.getElementById('mini-status');
mini.addEventListener('click', () => go('#tracks'));

function renderMini() {
  const st = state.status;
  if (!st?.active) { mini.hidden = true; return; }
  const track = trackById(st.trackId);
  mini.hidden = false;
  mini.style.setProperty('--track', track?.color || '');
  mount(mini,
    h('b', {}, `${track?.icon ?? '▶'} ${st.trackName}`),
    h('span', { dataset: { since: st.startedAt } }, fmtClock((Date.now() - new Date(st.startedAt)) / 1000)));
}
subscribe('status', renderMini);
subscribe('tracks', renderMini);

// Живые часы: все элементы с data-since обновляются раз в секунду.
setInterval(() => {
  document.querySelectorAll('[data-since]').forEach(el => {
    el.textContent = fmtClock((Date.now() - new Date(el.dataset.since)) / 1000);
  });
  document.querySelectorAll('[data-until]').forEach(el => {
    el.textContent = fmtClock((new Date(el.dataset.until) - Date.now()) / 1000);
  });
}, 1000);

// ───────────── События от ядра ─────────────

on('status', status => set('status', status));

on('stepProgress', p => {
  const st = state.status;
  if (!st?.active || st.trackId !== p.trackId) return;
  const progress = [...(st.progress ?? [])];
  const i = progress.findIndex(x => x.index === p.index);
  const item = { index: p.index, label: p.label, status: p.status, message: p.message };
  if (i >= 0) progress[i] = item; else progress.push(item);
  set('status', { ...st, progress });
});

on('trackEnded', s => {
  toast(`«${s.trackName}» завершён: ${fmtMinutes(s.minutes)}. Закрыто окон: ${s.closed}`, 'success', 5000);
});

on('timerFired', t => toast(`⏱ ${t.message || t.label}`, 'warning', 8000));

on('windowShown', async () => {
  try {
    set('status', await call('getStatus'));
    if (currentHash === 'tracks' || currentHash === '') await reloadTracks();
  } catch { /* не критично */ }
});

// ───────────── Клавиатура ─────────────

document.addEventListener('keydown', e => {
  if (e.defaultPrevented) return;
  const typing = /^(INPUT|TEXTAREA|SELECT)$/.test(document.activeElement?.tagName);
  if (e.key === 'Escape' && !typing && !document.querySelector('.modal-backdrop, .menu')) {
    if (currentHash.startsWith('tracks') || currentHash === '') call('hideWindow');
    else go('#tracks');
  }
});

// ───────────── Старт ─────────────

async function init() {
  try {
    const data = await call('getInit');
    state.version = data.version;
    state.settings = data.settings;
    state.monitors = data.monitors;
    state.browsers = data.browsers;
    state.dataFolder = data.dataFolder;
    state.loadErrors = data.loadErrors;
    state.tracks = data.tracks;
    set('status', data.status);
    document.getElementById('version').textContent = `v${data.version}${isMock ? ' · демо в браузере' : ''}`;
  } catch (e) {
    mount(viewEl, h('div', { class: 'notice error' }, `Не удалось связаться с лаунчером: ${e.message}`));
    return;
  }
  window.addEventListener('hashchange', route);
  await route();
}

init();
