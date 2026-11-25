// Заглушка вместо C#, когда ui/index.html открыт в обычном браузере.
// Нужна только для вёрстки интерфейса: программы она, конечно, не запускает.
// Запуск: из папки ui выполнить `python -m http.server 8080` и открыть http://localhost:8080

import { STEP_TYPES } from './schema.js';

const delay = ms => new Promise(r => setTimeout(r, ms));
const clone = v => structuredClone(v);

const tracks = [
  {
    id: 'vesper', name: 'Курсовая Vesper', icon: '🥽', color: '#7c5cff', description: 'Unity + VS Code + документация, таймер 50 минут',
    steps: [
      { type: 'command', enabled: true, continueOnError: true, command: 'git pull', workingDir: 'D:\\Projects\\Vesper' },
      { type: 'app', enabled: true, continueOnError: true, path: 'C:\\Program Files\\Unity\\Hub\\Editor\\6000.0.30f1\\Editor\\Unity.exe', args: '-projectPath "D:\\Projects\\Vesper"', layout: { monitor: 0, position: 'left-half' } },
      { type: 'app', enabled: true, continueOnError: true, path: '%LocalAppData%\\Programs\\Microsoft VS Code\\Code.exe', args: '"D:\\Projects\\Vesper\\Assets\\Scripts"', layout: { monitor: 0, position: 'right-half' } },
      { type: 'url', enabled: true, continueOnError: true, urls: ['https://docs.unity3d.com', 'https://ticktick.com'], browser: 'chrome', newWindow: true },
      { type: 'timer', enabled: true, continueOnError: true, minutes: 50, message: 'Перерыв! Встань и разомнись' },
    ],
    onEnd: [{ type: 'notify', enabled: true, continueOnError: true, title: 'Vesper', message: 'Не забудь закоммитить изменения' }],
  },
  {
    id: 'sql-practice', name: 'SQL практика', icon: '🗄️', color: '#30c4ff', description: 'SSMS, задания и AdventureWorks',
    steps: [
      { type: 'python', enabled: true, continueOnError: true, script: 'new_practice_folder.py', input: { base: 'D:/Study/SQL', prefix: 'Практика' } },
      { type: 'url', enabled: true, continueOnError: true, urls: ['https://learn.microsoft.com/sql/'], browser: 'edge', newWindow: true },
    ],
    onEnd: [],
  },
  {
    id: 'morning', name: 'Утро', icon: '🌅', color: '#ffb020', description: 'План на день',
    steps: [{ type: 'url', enabled: true, continueOnError: true, urls: ['https://ticktick.com'], newWindow: true }],
    onEnd: [],
  },
];

let settings = { pythonPath: 'python', autostart: false, showOnLogin: true, closeToTray: true, hideOnStart: true, confirmEnd: true, hotkey: 'Ctrl+Alt+Space' };
let status = { active: false };
const log = ['12:00:00 [INFO] Демо-режим: интерфейс открыт в браузере'];

function makeStatus(track) {
  return {
    active: true, trackId: track.id, trackName: track.name, startedAt: new Date().toISOString(),
    running: true, pendingWindows: 0, windows: 0, processes: 0, timers: [],
    progress: track.steps.map((s, i) => ({ index: i, label: s.label || STEP_TYPES[s.type].describe(s), status: 'pending', message: null })),
  };
}

async function runFake(track, emit) {
  for (let i = 0; i < track.steps.length; i++) {
    if (!status.active) return;
    const label = status.progress[i].label;
    status.progress[i] = { index: i, label, status: 'running', message: null };
    emit('stepProgress', { trackId: track.id, ...status.progress[i] });
    await delay(500);
    if (!status.active) return;
    status.progress[i] = { index: i, label, status: 'done', message: 'демо' };
    emit('stepProgress', { trackId: track.id, ...status.progress[i] });
    if (track.steps[i].type === 'timer') {
      status.timers.push({ id: 't' + i, label: 'Таймер', message: track.steps[i].message, dueAt: new Date(Date.now() + track.steps[i].minutes * 60000).toISOString() });
    }
    if (['app', 'url', 'folder'].includes(track.steps[i].type)) status.windows++;
  }
  status.running = false;
  emit('status', clone(status));
}

function demoStats(days) {
  const perDay = Array.from({ length: days }, (_, i) => {
    const d = new Date(); d.setDate(d.getDate() - (days - 1 - i));
    const v = Math.round(Math.random() * 150), s = Math.round(Math.random() * 60);
    return { date: d.toISOString().slice(0, 10), total: v + s, tracks: { vesper: v, 'sql-practice': s } };
  });
  const sum = id => perDay.reduce((a, d) => a + d.tracks[id], 0);
  return {
    days, totalMinutes: sum('vesper') + sum('sql-practice'), sessions: days * 2, longestMinutes: 148, streakDays: 4,
    perTrack: [
      { trackId: 'vesper', name: 'Курсовая Vesper', color: '#7c5cff', minutes: sum('vesper'), sessions: days },
      { trackId: 'sql-practice', name: 'SQL практика', color: '#30c4ff', minutes: sum('sql-practice'), sessions: days },
    ],
    perDay,
    recent: [{ trackId: 'vesper', name: 'Курсовая Vesper', color: '#7c5cff', started: new Date().toISOString(), minutes: 84, open: false }],
  };
}

export async function handle(action, p, emit) {
  await delay(60);
  switch (action) {
    case 'getInit':
      return clone({
        version: '1.0.0', tracks, loadErrors: [], status, settings, browsers: ['chrome', 'edge'],
        monitors: [{ index: 0, label: '1 — основной (2560×1440)' }, { index: 1, label: '2 (1920×1080)' }],
        dataFolder: 'C:\\Users\\you\\AppData\\Roaming\\TrackLauncher',
      });
    case 'getTracks': return clone({ tracks, loadErrors: [] });
    case 'getTrack': return clone(tracks.find(t => t.id === p.id));
    case 'saveTrack': {
      const t = p.track;
      if (!t.name) return { saved: false, issues: [{ level: 'error', message: 'У трека нет названия' }] };
      if (!t.id) t.id = 'track-' + Date.now();
      const i = tracks.findIndex(x => x.id === t.id);
      if (i >= 0) tracks[i] = t; else tracks.push(t);
      return { saved: true, track: clone(t), issues: [] };
    }
    case 'deleteTrack': tracks.splice(tracks.findIndex(t => t.id === p.id), 1); return null;
    case 'duplicateTrack': {
      const copy = clone(tracks.find(t => t.id === p.id));
      copy.id += '-2'; copy.name += ' (копия)'; tracks.push(copy); return copy;
    }
    case 'startTrack': {
      const track = tracks.find(t => t.id === p.id);
      status = makeStatus(track);
      runFake(track, emit);
      return clone(status);
    }
    case 'endTrack': {
      const minutes = (Date.now() - new Date(status.startedAt)) / 60000;
      const summary = { trackId: status.trackId, trackName: status.trackName, minutes, closed: status.windows, killed: 0 };
      status = { active: false };
      emit('trackEnded', summary);
      return summary;
    }
    case 'getStatus': return clone(status);
    case 'captureLayout': return { updated: status.windows };
    case 'testStep': await delay(400); return { ok: true, message: 'Демо: шаг «выполнен»' };
    case 'pickFile': return p.kind === 'script' ? 'backup_project.py' : 'C:\\Program Files\\Demo\\demo.exe';
    case 'pickFolder': return 'D:\\Projects\\Demo';
    case 'getStats': return demoStats(p.days ?? 7);
    case 'getSettings': return clone(settings);
    case 'saveSettings': settings = { ...p }; return { settings: clone(settings), warning: null };
    case 'checkPython': return 'Python 3.13.0 (демо)';
    case 'getLog': return log;
    case 'getMonitors': return [];
    case 'exportTrack': case 'importTrack': case 'exportStatsCsv': case 'openFolder': case 'hideWindow': case 'quit':
      return null;
    default:
      throw new Error(`Демо не знает команду ${action}`);
  }
}
