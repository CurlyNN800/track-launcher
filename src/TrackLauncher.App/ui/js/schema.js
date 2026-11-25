// Описание типов шагов для конструктора.
// Поля должны совпадать с C#-классами в TrackLauncher.Core/Models/Steps.cs (camelCase).
// Типы полей: text, number, lines, textarea, checkbox, select, file, folder, script, json, track, browser, layout.

const basename = p => (p || '').replace(/["']/g, '').split(/[\\/]/).filter(Boolean).pop() || '';
const noExt = p => basename(p).replace(/\.[^.]+$/, '');
const hostOf = url => { try { return new URL(url).host || url; } catch { return url; } };

const LAYOUT = { key: 'layout', label: 'Куда поставить окно', type: 'layout', full: true };
const KEEP_OPEN = { key: 'keepOpen', label: 'Не закрывать при завершении трека', type: 'checkbox', default: false };

export const STEP_TYPES = {
  app: {
    title: 'Программа',
    icon: '🖥️',
    hint: 'Запустить exe (Unity, VS Code, Discord…). Можно передать аргументы, например открыть конкретный проект.',
    fields: [
      { key: 'path', label: 'Программа', type: 'file', required: true, full: true, placeholder: 'C:\\Program Files\\Unity\\Hub\\Editor\\6000.0.30f1\\Editor\\Unity.exe' },
      { key: 'args', label: 'Аргументы', type: 'text', full: true, placeholder: '-projectPath "D:\\Projects\\Vesper"' },
      LAYOUT,
      { key: 'blocking', label: 'Дождаться окна перед следующим шагом', type: 'checkbox', default: false },
      KEEP_OPEN,
      { key: 'workingDir', label: 'Рабочая папка', type: 'folder', advanced: true },
      { key: 'windowMatch.process', label: 'Имя процесса окна', type: 'text', advanced: true, hint: 'По умолчанию — имя exe без .exe' },
      { key: 'windowMatch.title', label: 'Заголовок окна содержит', type: 'text', advanced: true, hint: 'Помогает отличить нужное окно от заставки' },
      { key: 'windowTimeoutSec', label: 'Ждать окно, секунд', type: 'number', default: 60, min: 1, advanced: true },
      { key: 'settleMs', label: 'Пауза перед расстановкой, мс', type: 'number', default: 1000, min: 0, advanced: true },
    ],
    describe: s => s.path ? `${noExt(s.path)}${s.args ? ' ' + s.args : ''}` : 'Программа не выбрана',
  },
  url: {
    title: 'Сайты',
    icon: '🌐',
    hint: 'Открыть сайты. Если выбран браузер — в отдельном окне, которое закроется при завершении трека.',
    fields: [
      { key: 'urls', label: 'Адреса (по одному на строку)', type: 'lines', required: true, full: true, placeholder: 'https://ticktick.com\nhttps://docs.unity3d.com' },
      { key: 'browser', label: 'Браузер', type: 'browser' },
      { key: 'profile', label: 'Профиль браузера', type: 'text', placeholder: 'Profile 1' },
      { key: 'newWindow', label: 'В отдельном окне', type: 'checkbox', default: true },
      KEEP_OPEN,
      LAYOUT,
    ],
    describe: s => {
      const urls = (s.urls || []).filter(Boolean);
      if (!urls.length) return 'Адреса не указаны';
      return urls.length > 1 ? `${hostOf(urls[0])} и ещё ${urls.length - 1}` : hostOf(urls[0]);
    },
  },
  folder: {
    title: 'Папка',
    icon: '📁',
    hint: 'Открыть папку в проводнике.',
    fields: [
      { key: 'path', label: 'Папка', type: 'folder', required: true, full: true },
      LAYOUT,
      KEEP_OPEN,
    ],
    describe: s => basename(s.path) || 'Папка не выбрана',
  },
  command: {
    title: 'Команда',
    icon: '⌨️',
    hint: 'Команда в терминале: git pull, npm run dev, свой .bat…',
    fields: [
      { key: 'command', label: 'Команда', type: 'text', required: true, full: true, placeholder: 'git pull' },
      { key: 'workingDir', label: 'Рабочая папка', type: 'folder', full: true },
      { key: 'wait', label: 'Дождаться завершения', type: 'checkbox', default: true, hint: 'Выключи для серверов вроде npm run dev — их остановит «Завершить трек»' },
      { key: 'showWindow', label: 'Показывать окно консоли', type: 'checkbox', default: false },
      { key: 'timeoutSec', label: 'Тайм-аут, секунд', type: 'number', default: 120, min: 1, advanced: true },
    ],
    describe: s => s.command || 'Команда не задана',
  },
  python: {
    title: 'Python',
    icon: '🐍',
    hint: 'Запустить свой скрипт из папки scripts. Параметры придут в stdin как JSON.',
    fields: [
      { key: 'script', label: 'Скрипт', type: 'script', required: true, full: true, placeholder: 'backup_project.py' },
      { key: 'input', label: 'Параметры (JSON)', type: 'json', full: true, placeholder: '{ "source": "D:/Projects/Vesper" }' },
      { key: 'args', label: 'Аргументы командной строки', type: 'text', advanced: true },
      { key: 'timeoutSec', label: 'Тайм-аут, секунд', type: 'number', default: 60, min: 1, advanced: true },
    ],
    describe: s => s.script ? basename(s.script) : 'Скрипт не выбран',
  },
  wait: {
    title: 'Пауза',
    icon: '⏳',
    hint: 'Подождать перед следующим шагом.',
    fields: [{ key: 'seconds', label: 'Секунд', type: 'number', default: 5, min: 0, step: 0.5 }],
    describe: s => `${s.seconds ?? 5} с`,
  },
  notify: {
    title: 'Уведомление',
    icon: '🔔',
    hint: 'Показать уведомление Windows.',
    fields: [
      { key: 'title', label: 'Заголовок', type: 'text', placeholder: 'Название трека' },
      { key: 'message', label: 'Текст', type: 'text', required: true, full: true, placeholder: 'Сегодня дедлайн по SQL' },
    ],
    describe: s => s.message || 'Текст не задан',
  },
  timer: {
    title: 'Таймер',
    icon: '⏱️',
    hint: 'Напомнить через N минут: помодоро, «пора на тренировку».',
    fields: [
      { key: 'minutes', label: 'Минут', type: 'number', default: 50, min: 1 },
      { key: 'message', label: 'Сообщение', type: 'text', full: true, placeholder: 'Время вышло — сделай перерыв' },
    ],
    describe: s => `${s.minutes ?? 50} мин${s.message ? ' — ' + s.message : ''}`,
  },
  track: {
    title: 'Другой трек',
    icon: '🔗',
    hint: 'Выполнить шаги другого трека (например, общий «Утро» внутри «Учёбы»).',
    fields: [{ key: 'trackId', label: 'Трек', type: 'track', required: true, full: true }],
    describe: (s, tracks) => tracks?.find(t => t.id === s.trackId)?.name || s.trackId || 'Трек не выбран',
  },
};

export const COMMON_FIELDS = [
  { key: 'label', label: 'Подпись шага', type: 'text', placeholder: 'Необязательно', advanced: true },
  { key: 'continueOnError', label: 'Если шаг упал — идти дальше', type: 'checkbox', default: true, advanced: true },
];

export const LAYOUT_POSITIONS = [
  ['', 'Не трогать'],
  ['maximized', 'Развернуть'],
  ['left-half', 'Левая половина'],
  ['right-half', 'Правая половина'],
  ['top-half', 'Верхняя половина'],
  ['bottom-half', 'Нижняя половина'],
  ['top-left', 'Четверть: слева сверху'],
  ['top-right', 'Четверть: справа сверху'],
  ['bottom-left', 'Четверть: слева снизу'],
  ['bottom-right', 'Четверть: справа снизу'],
  ['center', 'По центру'],
  ['custom', 'Свои координаты'],
];

export const BROWSER_NAMES = {
  chrome: 'Chrome', edge: 'Edge', firefox: 'Firefox', brave: 'Brave', yandex: 'Яндекс Браузер', opera: 'Opera',
};

export const EMOJIS = ['🚀', '🎮', '🥽', '💻', '📚', '🧮', '🗄️', '🏃', '🎧', '🎬', '✍️', '🌅', '🌙', '🧪', '📈', '🔧', '🎨', '🧠'];
export const COLORS = ['#7c5cff', '#30c4ff', '#2ecc8f', '#ffb020', '#ff5c7a', '#ff7a2e', '#a0e33a', '#e05cff', '#8a8fa3'];

/** Читает значение по пути "windowMatch.process". */
export function getPath(obj, path) {
  return path.split('.').reduce((o, k) => (o == null ? undefined : o[k]), obj);
}

/** Записывает значение по пути; пустые значения удаляют ключ. */
export function setPath(obj, path, value) {
  const keys = path.split('.');
  let target = obj;
  for (const key of keys.slice(0, -1)) {
    if (typeof target[key] !== 'object' || target[key] === null) target[key] = {};
    target = target[key];
  }
  const last = keys.at(-1);
  if (value === '' || value === undefined || value === null) delete target[last];
  else target[last] = value;
}

export function describeStep(step, tracks) {
  const type = STEP_TYPES[step.type];
  if (!type) return step.type;
  return step.label || type.describe(step, tracks);
}

/**
 * Готовит шаг к отправке в C#: "type" первым полем, без служебных _полей,
 * без пустых строк и пустых объектов.
 */
export function cleanStep(step) {
  const out = { type: step.type };
  for (const [key, value] of Object.entries(step)) {
    if (key === 'type' || key.startsWith('_')) continue;
    if (key === 'input') { if (value !== undefined && value !== null) out.input = value; continue; } // JSON пользователя — как есть
    const cleaned = cleanValue(value);
    if (cleaned !== undefined) out[key] = cleaned;
  }
  return out;
}

function cleanValue(value) {
  if (value === null || value === undefined || value === '') return undefined;
  if (Array.isArray(value)) {
    const arr = value.map(cleanValue).filter(v => v !== undefined);
    return arr;
  }
  if (typeof value === 'object') {
    const obj = {};
    for (const [k, v] of Object.entries(value)) {
      const c = cleanValue(v);
      if (c !== undefined) obj[k] = c;
    }
    return Object.keys(obj).length ? obj : undefined;
  }
  return value;
}

export function cleanTrack(track) {
  return {
    id: track.id || '',
    name: (track.name || '').trim(),
    icon: track.icon || undefined,
    color: track.color || undefined,
    description: track.description?.trim() || undefined,
    steps: (track.steps || []).map(cleanStep),
    onEnd: (track.onEnd || []).map(cleanStep),
  };
}
