// Статистика: сколько времени ушло на каждый трек.

import { call } from '../bridge.js';
import { state } from '../state.js';
import { h, mount, toast, fmtMinutes, fmtDate, fmtTime } from '../dom.js';

const PERIODS = [[7, 'Неделя'], [30, 'Месяц'], [90, '3 месяца']];
const FALLBACK_COLORS = ['#7c5cff', '#30c4ff', '#2ecc8f', '#ffb020', '#ff5c7a', '#ff7a2e', '#a0e33a', '#e05cff'];
const WEEKDAYS = ['вс', 'пн', 'вт', 'ср', 'чт', 'пт', 'сб'];

let days = 7;

export async function render(root) {
  const body = h('div', {}, h('div', { class: 'muted' }, 'Считаю…'));
  const periodEl = h('div', { class: 'segmented' });

  mount(root,
    h('div', { class: 'page-head' },
      h('h1', {}, 'Статистика'),
      h('div', { class: 'grow' }),
      periodEl,
      h('button', { class: 'btn', onClick: exportCsv }, 'Экспорт CSV')),
    body);

  const drawPeriods = () => mount(periodEl, PERIODS.map(([d, label]) =>
    h('button', { class: d === days ? 'on' : '', onClick: () => { days = d; drawPeriods(); load(); } }, label)));

  async function load() {
    try {
      const report = await call('getStats', { days });
      draw(body, report);
    } catch (e) {
      mount(body, h('div', { class: 'notice error' }, e.message));
    }
  }

  drawPeriods();
  await load();
  return null;
}

function colorFor(trackId, color, index) {
  return color || state.tracks.find(t => t.id === trackId)?.color || FALLBACK_COLORS[index % FALLBACK_COLORS.length];
}

function draw(root, r) {
  if (!r.recent.length) {
    mount(root, h('div', { class: 'empty' },
      h('h2', {}, 'Статистики пока нет'),
      h('p', {}, 'Запусти любой трек — время начнёт считаться само. Не забывай нажимать «Завершить трек».')));
    return;
  }

  const colors = Object.fromEntries(r.perTrack.map((t, i) => [t.trackId, colorFor(t.trackId, t.color, i)]));
  const maxTrack = Math.max(1, ...r.perTrack.map(t => t.minutes));
  const maxDay = Math.max(1, ...r.perDay.map(d => d.total));
  const labelEvery = r.days <= 7 ? 1 : r.days <= 31 ? 5 : 14;

  mount(root,
    h('div', { class: 'tiles' },
      tile('Всего за период', fmtMinutes(r.totalMinutes)),
      tile('Запусков', String(r.sessions)),
      tile('Самая длинная сессия', fmtMinutes(r.longestMinutes)),
      tile('Дней подряд', `${r.streakDays} 🔥`)),

    h('div', { class: 'panel' },
      h('h3', {}, 'По дням'),
      h('div', { class: 'columns' },
        r.perDay.map(d => {
          const order = Object.entries(d.tracks).sort((a, b) => b[1] - a[1]);
          const tip = d.total
            ? `${fmtDate(d.date)}: ${fmtMinutes(d.total)}\n` + order.map(([id, m]) => `${nameOf(r, id)} — ${fmtMinutes(m)}`).join('\n')
            : `${fmtDate(d.date)}: ничего`;
          return h('div', { class: `col${d.total ? '' : ' empty-col'}`, title: tip },
            order.map(([id, m]) => h('div', { class: 'seg', style: { height: `${(m / maxDay) * 100}%`, background: colors[id] ?? '#555' } })));
        })),
      h('div', { class: 'col-labels' },
        r.perDay.map((d, i) => {
          const date = new Date(d.date + 'T12:00:00');
          const show = r.days <= 7 || i % labelEvery === 0 || i === r.perDay.length - 1;
          return h('span', {}, show ? (r.days <= 7 ? WEEKDAYS[date.getDay()] : fmtDate(date)) : '');
        })),
      h('div', { class: 'legend' }, r.perTrack.map(t => h('span', {}, h('i', { style: { background: colors[t.trackId] } }), t.name)))),

    h('div', { class: 'two-col' },
      h('div', { class: 'panel' },
        h('h3', {}, 'По трекам'),
        r.perTrack.length
          ? r.perTrack.map(t => h('div', { class: 'hbar' },
              h('span', { class: 'name', title: t.name }, t.name),
              h('div', { class: 'track-bar' }, h('div', { class: 'fill', style: { width: `${(t.minutes / maxTrack) * 100}%`, background: colors[t.trackId] } })),
              h('span', { class: 'val' }, fmtMinutes(t.minutes))))
          : h('div', { class: 'muted' }, 'За этот период ничего не запускалось')),

      h('div', { class: 'panel' },
        h('h3', {}, 'Последние запуски'),
        h('table', { class: 'table' },
          h('thead', {}, h('tr', {}, h('th', {}, 'Трек'), h('th', {}, 'Когда'), h('th', { class: 'num' }, 'Длилось'))),
          h('tbody', {}, r.recent.map((s, i) => h('tr', {},
            h('td', {}, h('span', { class: 'dot', style: { background: colorFor(s.trackId, s.color, i) } }), s.name),
            h('td', { class: 'muted' }, `${fmtDate(s.started)}, ${fmtTime(s.started)}`),
            h('td', { class: 'num' }, s.open ? 'идёт' : fmtMinutes(s.minutes)))))))));
}

function tile(label, value) {
  return h('div', { class: 'tile' }, h('div', { class: 'k' }, label), h('div', { class: 'v' }, value));
}

function nameOf(report, id) {
  return report.perTrack.find(t => t.trackId === id)?.name ?? state.tracks.find(t => t.id === id)?.name ?? id;
}

async function exportCsv() {
  try {
    const path = await call('exportStatsCsv');
    if (path) toast(`Сохранено: ${path}`, 'success');
  } catch (e) {
    toast(e.message, 'error');
  }
}
