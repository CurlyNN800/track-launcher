// Настройки лаунчера.

import { call } from '../bridge.js';
import { state } from '../state.js';
import { h, mount, toast, field, switchEl } from '../dom.js';

export async function render(root) {
  let settings;
  try {
    settings = await call('getSettings');
  } catch (e) {
    mount(root, h('div', { class: 'notice error' }, e.message));
    return null;
  }

  const draft = { ...settings };
  const save = async (patch, message) => {
    Object.assign(draft, patch);
    try {
      const result = await call('saveSettings', draft);
      state.settings = result.settings;
      if (result.warning) toast(result.warning, 'warning', 6000);
      else if (message) toast(message, 'success');
    } catch (e) {
      toast(e.message, 'error');
    }
  };

  const row = (title, description, control) => h('div', { class: 'setting-row' },
    h('div', { class: 'text' }, h('b', {}, title), h('span', {}, description)), control);

  // ── Python ──
  const pythonInput = h('input', { class: 'input', value: draft.pythonPath ?? 'python', spellcheck: false });
  const pythonInfo = h('div', { class: 'hint' }, 'Команда (python, py) или полный путь к python.exe');
  pythonInput.addEventListener('change', () => save({ pythonPath: pythonInput.value.trim() || 'python' }, 'Путь к Python сохранён'));

  const checkPython = async () => {
    pythonInfo.textContent = 'Проверяю…';
    try {
      pythonInfo.textContent = `✓ ${await call('checkPython', { path: pythonInput.value.trim() })}`;
    } catch (e) {
      pythonInfo.textContent = `✕ ${e.message}`;
    }
  };

  const pickPython = async () => {
    const path = await call('pickFile', { kind: 'python' });
    if (path) { pythonInput.value = path; save({ pythonPath: path }); checkPython(); }
  };

  // ── Горячая клавиша ──
  const hotkeyInput = h('input', {
    class: 'input hotkey-input', readOnly: true, value: draft.hotkey || 'выключена',
    title: 'Нажми и затем нужное сочетание клавиш',
  });
  hotkeyInput.addEventListener('focus', () => { hotkeyInput.classList.add('listening'); hotkeyInput.value = 'Нажми сочетание…'; });
  hotkeyInput.addEventListener('blur', () => { hotkeyInput.classList.remove('listening'); hotkeyInput.value = draft.hotkey || 'выключена'; });
  hotkeyInput.addEventListener('keydown', e => {
    e.preventDefault();
    e.stopPropagation();
    if (e.key === 'Escape') { hotkeyInput.blur(); return; }
    if (e.key === 'Backspace' || e.key === 'Delete') {
      save({ hotkey: '' }, 'Горячая клавиша выключена');
      hotkeyInput.blur();
      return;
    }
    const combo = comboFromEvent(e);
    if (!combo) return;
    save({ hotkey: combo }, `Горячая клавиша: ${combo}`);
    hotkeyInput.blur();
  });

  const logEl = h('pre', { class: 'log' }, '…');
  const loadLog = async () => {
    const lines = await call('getLog');
    logEl.textContent = lines.length ? lines.join('\n') : 'Пока пусто';
    logEl.scrollTop = logEl.scrollHeight;
  };

  mount(root, h('div', { class: 'settings' },
    h('div', { class: 'page-head' }, h('h1', {}, 'Настройки')),

    h('div', { class: 'panel' },
      h('h3', {}, 'Запуск'),
      row('Запускать вместе с Windows', 'Лаунчер стартует при входе в систему',
        switchEl(draft.autostart, v => save({ autostart: v }))),
      row('Показывать треки при входе', 'Сразу открыть окно с карточками. Если выключено — лаунчер ждёт в трее',
        switchEl(draft.showOnLogin, v => save({ showOnLogin: v }))),
      row('Крестик сворачивает в трей', 'Чтобы совсем выйти — «Выход» в меню значка в трее',
        switchEl(draft.closeToTray, v => save({ closeToTray: v }))),
      row('Прятать окно после запуска трека', 'Лаунчер уходит в трей и не мешает',
        switchEl(draft.hideOnStart, v => save({ hideOnStart: v }))),
      row('Спрашивать перед «Завершить трек»', 'Защита от случайного нажатия',
        switchEl(draft.confirmEnd, v => save({ confirmEnd: v }))),
      row('Горячая клавиша', 'Показать лаунчер из любого места. Backspace — выключить',
        h('div', { style: { width: '220px' } }, hotkeyInput))),

    h('div', { class: 'panel' },
      h('h3', {}, 'Python для шагов-скриптов'),
      field('Python', h('div', { class: 'input-row' },
        pythonInput,
        h('button', { class: 'btn', onClick: pickPython }, 'Обзор…'),
        h('button', { class: 'btn', onClick: checkPython }, 'Проверить')), null),
      pythonInfo),

    h('div', { class: 'panel' },
      h('h3', {}, 'Данные'),
      h('div', { class: 'muted small mono' }, state.dataFolder),
      h('div', { class: 'input-row', style: { flexWrap: 'wrap' } },
        h('button', { class: 'btn', onClick: () => call('openFolder', { kind: 'data' }) }, 'Открыть папку данных'),
        h('button', { class: 'btn', onClick: () => call('openFolder', { kind: 'tracks' }) }, 'Треки (JSON)'),
        h('button', { class: 'btn', onClick: () => call('openFolder', { kind: 'scripts' }) }, 'Скрипты'),
        h('button', { class: 'btn', onClick: () => call('openFolder', { kind: 'logs' }) }, 'Логи'))),

    h('div', { class: 'panel' },
      h('div', { class: 'setting-row' }, h('h3', { class: 'text', style: { margin: 0 } }, 'Журнал'),
        h('button', { class: 'btn sm', onClick: loadLog }, 'Обновить')),
      logEl),

    h('div', { class: 'panel' },
      h('h3', {}, 'О программе'),
      h('div', { class: 'muted' }, `Лаунчер треков v${state.version}. C# + WebView2 + Python.`),
      h('div', {}, h('button', { class: 'btn danger', onClick: () => call('quit') }, 'Выйти из лаунчера')))));

  loadLog().catch(() => {});
  return null;
}

/** Ctrl+Alt+K → "Ctrl+Alt+K". Возвращает null, пока нажаты только модификаторы. */
function comboFromEvent(e) {
  const mods = [];
  if (e.ctrlKey) mods.push('Ctrl');
  if (e.altKey) mods.push('Alt');
  if (e.shiftKey) mods.push('Shift');
  if (e.metaKey) mods.push('Win');

  let key = null;
  if (/^Key[A-Z]$/.test(e.code)) key = e.code.slice(3);
  else if (/^Digit\d$/.test(e.code)) key = e.code.slice(5);
  else if (/^F\d{1,2}$/.test(e.code)) key = e.code;
  else if (e.code === 'Space') key = 'Space';
  else if (e.code === 'Backquote') key = 'OemTilde';
  else if (['Home', 'End', 'Insert', 'PageUp', 'PageDown'].includes(e.code)) key = e.code === 'PageUp' ? 'Prior' : e.code === 'PageDown' ? 'Next' : e.code;

  if (!key || !mods.length) return null;
  return [...mods, key].join('+');
}
