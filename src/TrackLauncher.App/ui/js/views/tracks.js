// Главный экран: панель запущенного трека и карточки треков.

import { call } from '../bridge.js';
import { state, set, subscribe, trackById } from '../state.js';
import { h, mount, toast, confirmBox, openMenu, fmtClock, fmtTime } from '../dom.js';
import { describeStep, STEP_TYPES } from '../schema.js';
import { go, reloadTracks } from '../app.js';

const STATUS_ICON = { pending: '○', running: '◌', done: '✓', failed: '✕', skipped: '–' };

export async function render(root) {
  const activeSlot = h('div');
  const errorsSlot = h('div');
  const gridSlot = h('div');

  mount(root,
    h('div', { class: 'page-head' },
      h('h1', {}, 'Треки'),
      h('div', { class: 'grow' }),
      h('button', { class: 'btn', onClick: importTrack }, 'Импорт'),
      h('button', { class: 'btn primary', onClick: () => go('#new') }, '+ Новый трек')),
    activeSlot,
    errorsSlot,
    gridSlot);

  const draw = () => {
    renderActive(activeSlot);
    renderErrors(errorsSlot);
    renderGrid(gridSlot);
  };
  draw();

  const unsubs = [
    subscribe('status', () => { renderActive(activeSlot); renderGrid(gridSlot); }),
    subscribe('tracks', draw),
  ];

  // Цифры 1–9 запускают треки по порядку.
  const onKey = e => {
    if (/^(INPUT|TEXTAREA|SELECT)$/.test(document.activeElement?.tagName) || e.ctrlKey || e.altKey || e.metaKey) return;
    if (document.querySelector('.modal-backdrop')) return;
    const n = Number(e.key);
    if (n >= 1 && n <= 9 && state.tracks[n - 1]) {
      e.preventDefault();
      startTrack(state.tracks[n - 1]);
    }
  };
  document.addEventListener('keydown', onKey);

  reloadTracks().catch(e => toast(e.message, 'error'));

  return { cleanup: () => { unsubs.forEach(u => u()); document.removeEventListener('keydown', onKey); } };
}

// ───────────── Панель запущенного трека ─────────────

function renderActive(slot) {
  const st = state.status;
  if (!st?.active) { mount(slot); return; }

  const track = trackById(st.trackId);
  const color = track?.color || '';
  const progress = st.progress ?? [];

  let stateBadge;
  if (st.running) stateBadge = h('span', { class: 'badge' }, 'запускается…');
  else if (st.pendingWindows > 0) stateBadge = h('span', { class: 'badge' }, `ждёт окон: ${st.pendingWindows}`);
  else stateBadge = h('span', { class: 'badge live' }, 'идёт');

  mount(slot,
    h('section', { class: 'active-panel', style: color ? { '--track': color } : {} },
      h('div', { class: 'active-head' },
        h('div', { class: 'card-icon' }, track?.icon || '▶'),
        h('div', { class: 'active-title' },
          h('div', { class: 'label' }, 'Сейчас идёт'),
          h('h2', {}, st.trackName)),
        stateBadge,
        h('div', { class: 'elapsed', dataset: { since: st.startedAt } }, fmtClock((Date.now() - new Date(st.startedAt)) / 1000))),

      h('div', { class: 'active-body' },
        progress.length
          ? h('ol', { class: 'progress' }, progress.map(p =>
              h('li', { class: p.status },
                h('span', { class: `st ${p.status}` }, STATUS_ICON[p.status] ?? '•'),
                h('div', {},
                  h('span', { class: 'text' }, p.label),
                  p.message && p.status !== 'done' ? h('div', { class: 'msg' }, p.message) : null))))
          : h('div', { class: 'muted small' }, 'Трек продолжен после перезапуска лаунчера.'),

        h('div', { class: 'side-block' },
          h('h4', {}, 'Таймеры'),
          st.timers?.length
            ? st.timers.map(t => h('div', { class: 'timer-row' },
                h('span', {}, t.label),
                h('b', { dataset: { until: t.dueAt }, title: `в ${fmtTime(t.dueAt)}` }, fmtClock((new Date(t.dueAt) - Date.now()) / 1000))))
            : h('div', { class: 'muted small' }, 'Нет активных таймеров'),
          h('div', { class: 'muted small', style: { marginTop: '12px' } },
            `Окон под контролем трека: ${st.windows}` + (st.processes ? `, фоновых процессов: ${st.processes}` : '')))),

      h('div', { class: 'active-actions' },
        h('button', { class: 'btn danger solid', onClick: endTrack }, '■ Завершить трек'),
        h('button', { class: 'btn', onClick: captureLayout, title: 'Запомнить, где сейчас стоят окна этого трека' }, 'Записать раскладку'),
        track ? h('button', { class: 'btn ghost', onClick: () => go(`#edit/${track.id}`) }, 'Изменить трек') : null)));
}

function renderErrors(slot) {
  if (!state.loadErrors?.length) { mount(slot); return; }
  mount(slot, h('div', { class: 'notice error' },
    h('b', {}, 'Некоторые треки не прочитались:'),
    h('ul', {}, state.loadErrors.map(e => h('li', {}, h('span', { class: 'mono' }, e.file), ` — ${e.error}`))),
    h('div', { class: 'small muted', style: { marginTop: '6px' } }, 'Исправь JSON-файл в папке tracks или удали его.')));
}

// ───────────── Карточки ─────────────

function renderGrid(slot) {
  const tracks = state.tracks;
  if (!tracks.length) {
    mount(slot, h('div', { class: 'empty' },
      h('h2', {}, 'Пока нет ни одного трека'),
      h('p', {}, 'Трек — это набор программ, сайтов и папок, которые открываются одной кнопкой.'),
      h('button', { class: 'btn primary', onClick: () => go('#new') }, 'Создать первый трек')));
    return;
  }

  mount(slot, h('div', { class: 'grid' },
    tracks.map((t, i) => card(t, i)),
    h('div', { class: 'card new-card', onClick: () => go('#new') },
      h('div', { class: 'plus' }, '+'),
      h('div', {}, 'Новый трек'))));
}

function card(track, index) {
  const st = state.status;
  const isActive = st?.active && st.trackId === track.id;
  const steps = (track.steps ?? []).filter(s => s.enabled !== false);
  const shown = steps.slice(0, 5);

  const menuBtn = h('button', {
    class: 'btn ghost icon sm', title: 'Ещё',
    onClick: e => {
      e.stopPropagation();
      openMenu(menuBtn, [
        { label: 'Изменить', onClick: () => go(`#edit/${track.id}`) },
        { label: 'Дублировать', onClick: () => duplicate(track) },
        { label: 'Экспорт в файл…', onClick: () => exportTrack(track) },
        { label: 'Удалить', danger: true, onClick: () => remove(track) },
      ]);
    },
  }, '⋯');

  return h('article', {
    class: `card${isActive ? ' is-active' : ''}`,
    style: track.color ? { '--track': track.color } : {},
    onClick: () => (isActive ? null : startTrack(track)),
    title: isActive ? 'Трек уже идёт' : 'Нажми, чтобы запустить',
  },
    h('div', { class: 'card-top' },
      h('div', { class: 'card-icon' }, track.icon || '🚀'),
      index < 9 ? h('span', { class: 'card-key', title: `Клавиша ${index + 1}` }, String(index + 1)) : null,
      menuBtn),
    h('h3', {}, track.name),
    track.description ? h('p', {}, track.description) : null,
    h('div', { class: 'chips' },
      shown.map(s => h('span', { class: 'chip', title: describeStep(s, state.tracks) },
        `${STEP_TYPES[s.type]?.icon ?? '•'} ${describeStep(s, state.tracks)}`)),
      steps.length > shown.length ? h('span', { class: 'chip' }, `+${steps.length - shown.length}`) : null,
      !steps.length ? h('span', { class: 'chip' }, 'нет шагов') : null),
    h('div', { class: 'card-actions' },
      isActive
        ? h('button', { class: 'btn danger', onClick: e => { e.stopPropagation(); endTrack(); } }, '■ Завершить')
        : h('button', { class: 'btn primary', onClick: e => { e.stopPropagation(); startTrack(track); } }, '▶ Запустить'),
      h('button', { class: 'btn', onClick: e => { e.stopPropagation(); go(`#edit/${track.id}`); } }, 'Изменить')));
}

// ───────────── Действия ─────────────

export async function startTrack(track) {
  const st = state.status;
  if (st?.active && st.trackId === track.id) {
    toast('Этот трек уже идёт');
    return;
  }
  let endCurrent = false;
  if (st?.active) {
    const ok = await confirmBox({
      title: 'Сменить трек?',
      text: `Сейчас идёт «${st.trackName}». Завершить его (окна закроются) и запустить «${track.name}»?`,
      ok: 'Сменить',
    });
    if (!ok) return;
    endCurrent = true;
  }
  try {
    set('status', await call('startTrack', { id: track.id, endCurrent }));
  } catch (e) {
    toast(e.message, 'error');
  }
}

export async function endTrack() {
  const st = state.status;
  if (!st?.active) return;
  if (state.settings.confirmEnd) {
    const ok = await confirmBox({
      title: `Завершить «${st.trackName}»?`,
      text: 'Окна, которые открыл трек, получат команду закрыться — несохранённое программы предложат сохранить.',
      ok: 'Завершить',
      danger: true,
    });
    if (!ok) return;
  }
  try {
    await call('endTrack');
    set('status', await call('getStatus'));
  } catch (e) {
    toast(e.message, 'error');
  }
}

async function captureLayout() {
  try {
    const { updated } = await call('captureLayout');
    toast(`Раскладка записана: ${updated} окон. В следующий раз они встанут так же.`, 'success');
    await reloadTracks();
  } catch (e) {
    toast(e.message, 'error');
  }
}

async function duplicate(track) {
  try {
    const copy = await call('duplicateTrack', { id: track.id });
    await reloadTracks();
    toast(`Создан «${copy.name}»`, 'success');
  } catch (e) {
    toast(e.message, 'error');
  }
}

async function exportTrack(track) {
  try {
    const path = await call('exportTrack', { id: track.id });
    if (path) toast(`Сохранено: ${path}`, 'success');
  } catch (e) {
    toast(e.message, 'error');
  }
}

async function importTrack() {
  try {
    const track = await call('importTrack');
    if (!track) return;
    await reloadTracks();
    toast(`Импортирован «${track.name}». Проверь пути — на другом компе они могут отличаться.`, 'success', 6000);
  } catch (e) {
    toast(e.message, 'error');
  }
}

async function remove(track) {
  const ok = await confirmBox({ title: `Удалить «${track.name}»?`, text: 'Файл трека будет удалён. Статистика останется.', ok: 'Удалить', danger: true });
  if (!ok) return;
  try {
    await call('deleteTrack', { id: track.id });
    await reloadTracks();
  } catch (e) {
    toast(e.message, 'error');
  }
}
