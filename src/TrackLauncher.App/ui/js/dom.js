// Маленькие помощники для работы с DOM без фреймворка.

const PROPS = new Set(['value', 'checked', 'disabled', 'selected', 'title', 'placeholder', 'type', 'min', 'max',
  'step', 'rows', 'draggable', 'tabIndex', 'hidden', 'htmlFor', 'spellcheck', 'readOnly']);

/**
 * Создаёт элемент: h('button', { class: 'btn', onClick: fn }, 'Текст', h('span', {}, '…'))
 * Строки становятся текстом (не HTML), null/false пропускаются, массивы разворачиваются.
 */
export function h(tag, props = {}, ...children) {
  const el = document.createElement(tag);
  for (const [key, value] of Object.entries(props ?? {})) {
    if (value === null || value === undefined || value === false) continue;
    if (key === 'class') el.className = value;
    else if (key === 'style' && typeof value === 'object') {
      for (const [k, v] of Object.entries(value)) {
        if (k.startsWith('--')) el.style.setProperty(k, v);
        else el.style[k] = v;
      }
    } else if (key === 'dataset') Object.assign(el.dataset, value);
    else if (key.startsWith('on') && typeof value === 'function') el.addEventListener(key.slice(2).toLowerCase(), value);
    else if (PROPS.has(key)) el[key] = value;
    else el.setAttribute(key, value === true ? '' : String(value));
  }
  append(el, children);
  return el;
}

export function append(parent, children) {
  for (const child of children.flat(Infinity)) {
    if (child === null || child === undefined || child === false) continue;
    parent.append(child instanceof Node ? child : document.createTextNode(String(child)));
  }
  return parent;
}

export function clear(el) {
  while (el.firstChild) el.firstChild.remove();
  return el;
}

/** Заменяет содержимое элемента. */
export function mount(el, ...children) {
  clear(el);
  return append(el, children);
}

// ───────────── Тосты ─────────────

export function toast(message, kind = 'info', ms = 3800) {
  const root = document.getElementById('toasts');
  const el = h('div', { class: `toast ${kind}` }, message);
  root.append(el);
  setTimeout(() => {
    el.style.transition = 'opacity .2s';
    el.style.opacity = '0';
    setTimeout(() => el.remove(), 220);
  }, ms);
}

// ───────────── Модальное подтверждение ─────────────

export function confirmBox({ title, text = '', ok = 'Да', cancel = 'Отмена', danger = false }) {
  return new Promise(resolve => {
    const root = document.getElementById('modal-root');
    const close = result => {
      document.removeEventListener('keydown', onKey, true);
      clear(root);
      resolve(result);
    };
    const onKey = e => {
      if (e.key === 'Escape') { e.stopPropagation(); close(false); }
      if (e.key === 'Enter') { e.stopPropagation(); close(true); }
    };
    document.addEventListener('keydown', onKey, true);

    const okBtn = h('button', { class: `btn ${danger ? 'danger solid' : 'primary'}`, onClick: () => close(true) }, ok);
    mount(root,
      h('div', { class: 'modal-backdrop', onMousedown: e => { if (e.target === e.currentTarget) close(false); } },
        h('div', { class: 'modal', role: 'dialog' },
          h('h3', {}, title),
          text ? h('p', {}, text) : null,
          h('div', { class: 'modal-actions' },
            h('button', { class: 'btn ghost', onClick: () => close(false) }, cancel),
            okBtn))));
    okBtn.focus();
  });
}

// ───────────── Выпадающее меню ─────────────

export function openMenu(anchor, items) {
  closeMenus();
  const rect = anchor.getBoundingClientRect();
  const menu = h('div', { class: 'menu' },
    items.filter(Boolean).map(item => h('button', {
      class: item.danger ? 'danger' : '',
      onClick: e => { e.stopPropagation(); closeMenus(); item.onClick(); },
    }, item.label)));
  document.body.append(menu);
  const width = menu.offsetWidth;
  menu.style.left = `${Math.max(8, Math.min(rect.right - width, window.innerWidth - width - 8))}px`;
  menu.style.top = `${Math.min(rect.bottom + 6, window.innerHeight - menu.offsetHeight - 8)}px`;
  setTimeout(() => document.addEventListener('mousedown', outside, { once: true }), 0);
  function outside(e) { if (!menu.contains(e.target)) closeMenus(); else document.addEventListener('mousedown', outside, { once: true }); }
}

export function closeMenus() {
  document.querySelectorAll('.menu').forEach(m => m.remove());
}

// ───────────── Форматирование ─────────────

/** 135 → «2 ч 15 мин» */
export function fmtMinutes(minutes) {
  const m = Math.round(minutes || 0);
  if (m < 60) return `${m} мин`;
  const hours = Math.floor(m / 60);
  const rest = m % 60;
  return rest ? `${hours} ч ${rest} мин` : `${hours} ч`;
}

/** 3723 → «1:02:03» */
export function fmtClock(totalSeconds) {
  const s = Math.max(0, Math.floor(totalSeconds));
  const hours = Math.floor(s / 3600);
  const minutes = Math.floor((s % 3600) / 60);
  const seconds = s % 60;
  const mm = String(minutes).padStart(2, '0');
  const ss = String(seconds).padStart(2, '0');
  return hours > 0 ? `${hours}:${mm}:${ss}` : `${minutes}:${ss}`;
}

export function fmtTime(date) {
  return new Date(date).toLocaleTimeString('ru-RU', { hour: '2-digit', minute: '2-digit' });
}

export function fmtDate(date) {
  return new Date(date).toLocaleDateString('ru-RU', { day: 'numeric', month: 'short' });
}

/** Поле с подписью. */
export function field(label, control, hint, cls = '') {
  return h('div', { class: `field ${cls}` }, h('label', {}, label), control, hint ? h('div', { class: 'hint' }, hint) : null);
}

/** Переключатель-«тумблер». */
export function switchEl(checked, onChange, text) {
  const input = h('input', { type: 'checkbox', checked: !!checked, onChange: e => onChange(e.target.checked) });
  return h('label', { class: 'switch' }, input, h('span', { class: 'knob' }), text ? h('span', {}, text) : null);
}
