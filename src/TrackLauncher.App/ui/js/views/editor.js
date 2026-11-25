// Конструктор трека: название, иконка, цвет и шаги (перетаскиваются мышкой).

import { call } from '../bridge.js';
import { state } from '../state.js';
import { h, mount, toast, confirmBox, field, switchEl } from '../dom.js';
import {
  STEP_TYPES, COMMON_FIELDS, LAYOUT_POSITIONS, BROWSER_NAMES, EMOJIS, COLORS,
  getPath, setPath, describeStep, cleanTrack, cleanStep,
} from '../schema.js';
import { go, reloadTracks } from '../app.js';

let uidCounter = 1;
const uid = () => `s${uidCounter++}`;

export async function render(root, id) {
  let original;
  if (id) {
    try {
      original = await call('getTrack', { id });
    } catch (e) {
      toast(e.message, 'error');
      go('#tracks');
      return null;
    }
  } else {
    original = { id: '', name: '', icon: '🚀', color: COLORS[0], description: '', steps: [], onEnd: [] };
  }

  const draft = structuredClone(original);
  draft.steps = (draft.steps ?? []).map(s => ({ ...s, _uid: uid() }));
  draft.onEnd = (draft.onEnd ?? []).map(s => ({ ...s, _uid: uid() }));
  const baseline = JSON.stringify(cleanTrack(draft));

  const ui = {
    tab: 'steps',
    expanded: new Set(),
    advanced: new Set(),
    issues: [],
    saving: false,
    saved: false,
  };

  const isDirty = () => JSON.stringify(cleanTrack(draft)) !== baseline;

  // ── разметка ──
  const preview = h('div', { class: 'icon-preview' });
  const titleEl = h('h1', {});
  const dirtyEl = h('span', { class: 'muted small' });
  const tabsEl = h('div', { class: 'tabs' });
  const tabHint = h('div', { class: 'tab-hint' });
  const listEl = h('ul', { class: 'steps' });
  const paletteEl = h('div', { class: 'palette' });
  const issuesEl = h('div');
  const saveBtn = h('button', { class: 'btn primary', onClick: save }, 'Сохранить');

  const nameInput = h('input', {
    class: 'input', value: draft.name, placeholder: 'Например: Курсовая Vesper',
    onInput: e => { draft.name = e.target.value; refreshHeader(); },
  });
  const iconInput = h('input', {
    class: 'input', value: draft.icon ?? '', maxlength: 8, style: { flex: '0 0 64px', textAlign: 'center', fontSize: '18px' },
    onInput: e => { draft.icon = e.target.value; refreshHeader(); refreshEmojiRow(); },
  });
  const emojiRow = h('div', { class: 'emoji-row' });
  const swatches = h('div', { class: 'swatches' });
  const colorInput = h('input', {
    type: 'color', value: draft.color || COLORS[0], style: { width: '40px', height: '30px', border: '0', background: 'none', cursor: 'pointer' },
    onInput: e => { draft.color = e.target.value; refreshHeader(); refreshSwatches(); },
  });
  const descInput = h('input', {
    class: 'input', value: draft.description ?? '', placeholder: 'Пара слов, зачем этот трек',
    onInput: e => { draft.description = e.target.value; refreshHeader(); },
  });

  mount(root, h('div', { class: 'editor' },
    h('div', { class: 'page-head' },
      h('button', { class: 'btn ghost', onClick: () => go('#tracks') }, '← Назад'),
      preview,
      titleEl,
      h('div', { class: 'grow' }),
      id ? h('button', { class: 'btn ghost', onClick: duplicateSelf, title: 'Сохранить копию под новым именем' }, 'Дублировать') : null),

    h('div', { class: 'editor-meta' },
      field('Название', nameInput),
      field('Описание', descInput),
      h('div', { class: 'field' },
        h('label', {}, 'Иконка'),
        h('div', { class: 'input-row', style: { alignItems: 'center' } }, iconInput, emojiRow)),
      h('div', { class: 'field' },
        h('label', {}, 'Цвет'),
        h('div', { class: 'input-row', style: { alignItems: 'center' } }, swatches, colorInput))),

    tabsEl,
    tabHint,
    listEl,
    paletteEl,
    issuesEl,

    h('div', { class: 'savebar' },
      dirtyEl,
      h('div', { class: 'grow' }),
      h('button', { class: 'btn ghost', onClick: () => go('#tracks') }, 'Отмена'),
      saveBtn)));

  refreshHeader();
  refreshEmojiRow();
  refreshSwatches();
  renderTabs();
  renderList();
  renderPalette();
  if (!id) nameInput.focus();

  const onKey = e => {
    if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 's') {
      e.preventDefault();
      save();
    }
  };
  document.addEventListener('keydown', onKey);

  return {
    cleanup: () => document.removeEventListener('keydown', onKey),
    canLeave: async () => {
      if (ui.saved || !isDirty()) return true;
      return confirmBox({ title: 'Выйти без сохранения?', text: 'Изменения в треке пропадут.', ok: 'Выйти', danger: true });
    },
  };

  // ───────────── шапка ─────────────

  function refreshHeader() {
    preview.textContent = draft.icon || '🚀';
    preview.style.setProperty('--track', draft.color || COLORS[0]);
    titleEl.textContent = draft.name?.trim() || (id ? 'Без названия' : 'Новый трек');
    dirtyEl.replaceChildren(...(isDirty() ? [h('span', { class: 'dirty-dot' }), 'Есть несохранённые изменения · Ctrl+S'] : ['Ctrl+S — сохранить']));
  }

  function refreshEmojiRow() {
    mount(emojiRow, EMOJIS.slice(0, 10).map(e => h('button', {
      type: 'button', class: draft.icon === e ? 'on' : '',
      onClick: () => { draft.icon = e; iconInput.value = e; refreshHeader(); refreshEmojiRow(); },
    }, e)));
  }

  function refreshSwatches() {
    mount(swatches, COLORS.map(c => h('button', {
      type: 'button', class: draft.color === c ? 'on' : '', style: { background: c }, title: c,
      onClick: () => { draft.color = c; colorInput.value = c; refreshHeader(); refreshSwatches(); },
    })));
  }

  // ───────────── вкладки ─────────────

  function renderTabs() {
    const tab = (key, label) => h('button', {
      class: `tab${ui.tab === key ? ' on' : ''}`,
      onClick: () => { ui.tab = key; renderTabs(); renderList(); },
    }, label, h('span', { class: 'count' }, String(draft[key].length)));
    mount(tabsEl, tab('steps', 'При запуске'), tab('onEnd', 'При завершении'));
    tabHint.textContent = ui.tab === 'steps'
      ? 'Шаги выполняются сверху вниз. Перетаскивай за ⠿, чтобы поменять порядок.'
      : 'Эти шаги выполнятся при «Завершить трек», до того как закроются окна. Например: напомнить про коммит или сделать бэкап.';
  }

  // ───────────── список шагов ─────────────

  function currentList() {
    return draft[ui.tab];
  }

  function renderList() {
    const list = currentList();
    if (!list.length) {
      mount(listEl, h('li', { class: 'muted small', style: { padding: '14px 4px' } },
        ui.tab === 'steps' ? 'Пока пусто — добавь первый шаг ниже.' : 'Ничего не делать при завершении — тоже вариант.'));
      return;
    }
    mount(listEl, list.map((step, index) => stepItem(step, index)));
  }

  function stepItem(step, index) {
    const type = STEP_TYPES[step.type] ?? { title: step.type, icon: '?', fields: [] };
    const expanded = ui.expanded.has(step._uid);
    const issues = ui.issues.filter(i => i.phase === ui.tab && i.stepIndex === index);
    const level = issues.some(i => i.level === 'error') ? 'has-error' : issues.length ? 'has-warning' : '';

    const titleB = h('b', {}, describeStep(step, state.tracks));
    const li = h('li', {
      class: `step ${step.enabled === false ? 'disabled' : ''} ${level}`,
      dataset: { uid: step._uid },
    });

    const head = h('div', { class: 'step-head', onClick: () => toggle(step) },
      h('span', { class: 'handle', title: 'Перетащи, чтобы поменять порядок',
        onMousedown: () => { li.draggable = true; },
        onMouseup: () => { li.draggable = false; },
        onClick: e => e.stopPropagation() }, '⠿'),
      h('span', { class: 'step-num' }, String(index + 1)),
      h('span', { class: 'type-icon' }, type.icon),
      h('div', { class: 'step-title' }, titleB, h('span', {}, type.title)),
      h('div', { class: 'step-tools', onClick: e => e.stopPropagation() },
        switchEl(step.enabled !== false, v => { step.enabled = v; li.classList.toggle('disabled', !v); refreshHeader(); }),
        h('button', { class: 'btn ghost icon sm', title: 'Проверить этот шаг', onClick: () => testStep(step) }, '▶'),
        h('button', { class: 'btn ghost icon sm', title: 'Дублировать', onClick: () => duplicateStep(index) }, '⧉'),
        h('button', { class: 'btn ghost icon sm', title: 'Удалить', onClick: () => removeStep(index) }, '✕'),
        h('button', { class: 'btn ghost icon sm', title: expanded ? 'Свернуть' : 'Настроить', onClick: () => toggle(step) }, expanded ? '▴' : '▾')));

    li.append(head);
    if (expanded) li.append(stepBody(step, type, issues, () => { titleB.textContent = describeStep(step, state.tracks); refreshHeader(); }));
    else if (issues.length) li.append(h('div', { class: 'step-body' }, issuesBlock(issues)));

    setupDrag(li, step);
    return li;
  }

  function issuesBlock(issues) {
    return h('div', { class: 'step-issues' }, issues.map(i => h('div', { class: i.level }, `${i.level === 'error' ? '✕' : '!'} ${i.message}`)));
  }

  function toggle(step) {
    if (ui.expanded.has(step._uid)) ui.expanded.delete(step._uid);
    else ui.expanded.add(step._uid);
    renderList();
  }

  function stepBody(step, type, issues, onChange) {
    const showAdvanced = ui.advanced.has(step._uid);
    const all = [...type.fields, ...COMMON_FIELDS];
    const basic = all.filter(f => !f.advanced);
    const advanced = all.filter(f => f.advanced);

    const rerender = () => renderList();
    return h('div', { class: 'step-body' },
      type.hint ? h('div', { class: 'full muted small' }, type.hint) : null,
      basic.map(f => fieldControl(step, f, onChange, rerender)),
      advanced.length
        ? h('button', {
            class: 'btn ghost sm advanced-toggle',
            onClick: () => { showAdvanced ? ui.advanced.delete(step._uid) : ui.advanced.add(step._uid); rerender(); },
          }, showAdvanced ? '▴ Скрыть дополнительное' : `▾ Дополнительно (${advanced.length})`)
        : null,
      showAdvanced ? advanced.map(f => fieldControl(step, f, onChange, rerender)) : null,
      issues.length ? issuesBlock(issues) : null);
  }

  // ───────────── поля ─────────────

  function fieldControl(step, f, onChange, rerender) {
    const value = getPath(step, f.key);
    const set = v => { setPath(step, f.key, v); onChange(); };
    const cls = f.full ? 'full' : '';

    switch (f.type) {
      case 'checkbox':
        return h('div', { class: `field ${cls}`, style: { justifyContent: 'flex-end' } },
          switchEl(value ?? f.default, v => set(v), f.label),
          f.hint ? h('div', { class: 'hint' }, f.hint) : null);

      case 'number':
        return field(f.label, h('input', {
          class: 'input', type: 'number', value: value ?? '', placeholder: f.default !== undefined ? String(f.default) : '',
          min: f.min, step: f.step ?? 1,
          onInput: e => set(e.target.value === '' ? undefined : Number(e.target.value)),
        }), f.hint, cls);

      case 'lines':
        return field(f.label, h('textarea', {
          class: 'input', rows: 3, value: (value ?? []).join('\n'), placeholder: f.placeholder, spellcheck: false,
          onInput: e => set(e.target.value.split('\n').map(s => s.trim()).filter(Boolean)),
        }), f.hint, cls);

      case 'json': {
        const area = h('textarea', {
          class: 'input', rows: 4, spellcheck: false, placeholder: f.placeholder,
          value: value === undefined ? '' : JSON.stringify(value, null, 2),
        });
        const hint = h('div', { class: 'hint' }, f.hint ?? 'Скрипт получит это в stdin как поле "input"');
        area.addEventListener('input', () => {
          const text = area.value.trim();
          if (!text) { area.classList.remove('invalid'); hint.textContent = 'Пусто'; set(undefined); return; }
          try {
            set(JSON.parse(text));
            area.classList.remove('invalid');
            hint.textContent = 'JSON в порядке';
          } catch (err) {
            area.classList.add('invalid');
            hint.textContent = `Ошибка JSON: ${err.message}`;
          }
        });
        return h('div', { class: `field ${cls}` }, h('label', {}, f.label), area, hint);
      }

      case 'file':
      case 'folder':
      case 'script': {
        const input = h('input', { class: 'input', value: value ?? '', placeholder: f.placeholder ?? '', spellcheck: false, onInput: e => set(e.target.value) });
        const browse = h('button', {
          class: 'btn', type: 'button',
          onClick: async () => {
            try {
              const path = f.type === 'folder'
                ? await call('pickFolder')
                : await call('pickFile', { kind: f.type === 'script' ? 'script' : 'exe' });
              if (path) { input.value = path; set(path); }
            } catch (e) { toast(e.message, 'error'); }
          },
        }, 'Обзор…');
        return field(f.label, h('div', { class: 'input-row' }, input, browse), f.hint, cls);
      }

      case 'browser': {
        const known = Object.keys(BROWSER_NAMES);
        const installed = new Set(state.browsers ?? []);
        const isCustom = value && !known.includes(value);
        const select = h('select', {
          class: 'input',
          onChange: e => {
            if (e.target.value === '__custom') { set(''); custom.hidden = false; custom.focus(); return; }
            custom.hidden = true;
            set(e.target.value);
          },
        },
          h('option', { value: '' }, 'По умолчанию (не закроется)'),
          known.map(k => h('option', { value: k, selected: value === k }, `${BROWSER_NAMES[k]}${installed.has(k) ? '' : ' — не найден'}`)),
          h('option', { value: '__custom', selected: !!isCustom }, 'Другой — путь к exe…'));
        const custom = h('input', {
          class: 'input', value: isCustom ? value : '', placeholder: 'C:\\...\\browser.exe', hidden: !isCustom,
          onInput: e => set(e.target.value),
        });
        return h('div', { class: `field ${cls}` }, h('label', {}, f.label), select, custom);
      }

      case 'track': {
        const options = state.tracks.filter(t => t.id !== draft.id);
        return field(f.label, h('select', { class: 'input', onChange: e => set(e.target.value) },
          h('option', { value: '' }, '— выбери трек —'),
          options.map(t => h('option', { value: t.id, selected: value === t.id }, `${t.icon ?? ''} ${t.name}`))), f.hint, cls);
      }

      case 'layout':
        return layoutControl(step, f, onChange, rerender);

      default:
        return field(f.label, h('input', {
          class: 'input', value: value ?? '', placeholder: f.placeholder ?? '', spellcheck: false,
          onInput: e => set(e.target.value),
        }), f.hint, cls);
    }
  }

  function layoutControl(step, f, onChange, rerender) {
    const layout = step.layout ?? null;
    const position = layout?.position ?? '';
    const monitors = state.monitors?.length ? state.monitors : [{ index: 0, label: '1 — основной' }];

    const update = patch => {
      if (patch === null) delete step.layout;
      else step.layout = { monitor: 0, ...(step.layout ?? {}), ...patch };
      onChange();
    };

    const positionSelect = h('select', {
      class: 'input',
      onChange: e => {
        const v = e.target.value;
        if (!v) update(null);
        else if (v === 'custom') update({ position: v, x: layout?.x ?? 0, y: layout?.y ?? 0, width: layout?.width ?? 1200, height: layout?.height ?? 800 });
        else update({ position: v, x: undefined, y: undefined, width: undefined, height: undefined });
        rerender();
      },
    }, LAYOUT_POSITIONS.map(([v, label]) => h('option', { value: v, selected: v === position }, label)));

    const monitorSelect = h('select', {
      class: 'input', disabled: !position,
      onChange: e => update({ monitor: Number(e.target.value) }),
    }, monitors.map(m => h('option', { value: m.index, selected: (layout?.monitor ?? 0) === m.index }, `Монитор ${m.label}`)));

    const num = (key, label) => h('div', { class: 'field' }, h('label', {}, label), h('input', {
      class: 'input', type: 'number', value: layout?.[key] ?? '',
      onInput: e => update({ [key]: e.target.value === '' ? undefined : Number(e.target.value) }),
    }));

    return h('div', { class: 'field full' },
      h('label', {}, f.label),
      h('div', { class: 'layout-box' },
        positionSelect,
        monitorSelect,
        position === 'custom'
          ? h('div', { class: 'layout-custom' }, num('x', 'X'), num('y', 'Y'), num('width', 'Ширина'), num('height', 'Высота'))
          : null),
      h('div', { class: 'hint' }, 'Проще всего: запусти трек, расставь окна руками и нажми «Записать раскладку».'));
  }

  // ───────────── перетаскивание ─────────────

  function setupDrag(li, step) {
    li.addEventListener('dragstart', e => {
      e.dataTransfer.effectAllowed = 'move';
      e.dataTransfer.setData('text/plain', step._uid);
      li.classList.add('dragging');
    });
    li.addEventListener('dragend', () => {
      li.draggable = false;
      li.classList.remove('dragging');
      listEl.querySelectorAll('.step').forEach(el => el.classList.remove('drag-over-top', 'drag-over-bottom'));
    });
    li.addEventListener('dragover', e => {
      e.preventDefault();
      const rect = li.getBoundingClientRect();
      const top = e.clientY < rect.top + rect.height / 2;
      li.classList.toggle('drag-over-top', top);
      li.classList.toggle('drag-over-bottom', !top);
    });
    li.addEventListener('dragleave', () => li.classList.remove('drag-over-top', 'drag-over-bottom'));
    li.addEventListener('drop', e => {
      e.preventDefault();
      const fromUid = e.dataTransfer.getData('text/plain');
      const list = currentList();
      const from = list.findIndex(s => s._uid === fromUid);
      let to = list.findIndex(s => s._uid === step._uid);
      if (from < 0 || to < 0 || from === to) { renderList(); return; }
      const rect = li.getBoundingClientRect();
      if (e.clientY >= rect.top + rect.height / 2) to += 1;
      const [moved] = list.splice(from, 1);
      if (from < to) to -= 1;
      list.splice(to, 0, moved);
      ui.issues = [];
      renderList();
      refreshHeader();
    });
  }

  // ───────────── палитра шагов ─────────────

  function renderPalette() {
    mount(paletteEl,
      h('div', { class: 'palette-title' }, 'Добавить шаг'),
      Object.entries(STEP_TYPES).map(([key, t]) => h('button', {
        class: 'btn sm', title: t.hint, onClick: () => addStep(key),
      }, `${t.icon} ${t.title}`)));
  }

  function addStep(type) {
    const step = { type, _uid: uid() };
    if (type === 'url') step.urls = [];
    currentList().push(step);
    ui.expanded.add(step._uid);
    renderTabs();
    renderList();
    refreshHeader();
    listEl.lastElementChild?.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
  }

  function duplicateStep(index) {
    const list = currentList();
    const copy = { ...structuredClone(list[index]), _uid: uid() };
    list.splice(index + 1, 0, copy);
    ui.issues = [];
    renderTabs();
    renderList();
    refreshHeader();
  }

  function removeStep(index) {
    currentList().splice(index, 1);
    ui.issues = [];
    renderTabs();
    renderList();
    refreshHeader();
  }

  async function testStep(step) {
    toast(`Проверяю: ${describeStep(step, state.tracks)}…`);
    try {
      const result = await call('testStep', { step: cleanStep(step), trackId: draft.id || null });
      toast(`${result.ok ? '✓' : '✕'} ${result.message || (result.ok ? 'Шаг выполнен' : 'Ошибка')}`, result.ok ? 'success' : 'error', 6000);
    } catch (e) {
      toast(e.message, 'error');
    }
  }

  // ───────────── сохранение ─────────────

  async function save() {
    if (ui.saving) return;
    ui.saving = true;
    saveBtn.disabled = true;
    try {
      const result = await call('saveTrack', { track: cleanTrack(draft) });
      ui.issues = result.issues ?? [];
      if (!result.saved) {
        showIssues();
        const firstError = ui.issues.find(i => i.level === 'error' && i.stepIndex != null);
        if (firstError) {
          ui.tab = firstError.phase;
          const step = draft[firstError.phase][firstError.stepIndex];
          if (step) ui.expanded.add(step._uid);
          renderTabs();
        }
        renderList();
        toast('Трек не сохранён — исправь ошибки', 'error');
        return;
      }
      await reloadTracks();
      const warnings = ui.issues.filter(i => i.level === 'warning').length;
      toast(warnings ? `Сохранено. Предупреждений: ${warnings} — проверь пути.` : 'Сохранено', warnings ? 'warning' : 'success');
      // Без вопроса «выйти без сохранения?» — всё уже сохранено.
      ui.saved = true;
      go('#tracks');
    } catch (e) {
      toast(e.message, 'error');
    } finally {
      ui.saving = false;
      saveBtn.disabled = false;
    }
  }

  function showIssues() {
    const general = ui.issues.filter(i => i.stepIndex == null);
    mount(issuesEl, general.length
      ? h('div', { class: 'notice error', style: { marginTop: '16px' } }, h('ul', {}, general.map(i => h('li', {}, i.message))))
      : null);
  }

  async function duplicateSelf() {
    try {
      const copy = await call('duplicateTrack', { id });
      await reloadTracks();
      toast(`Создана копия «${copy.name}»`, 'success');
      go(`#edit/${copy.id}`);
    } catch (e) {
      toast(e.message, 'error');
    }
  }
}
