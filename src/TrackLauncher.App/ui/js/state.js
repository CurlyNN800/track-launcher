// Общее состояние интерфейса + простая подписка на изменения.

export const state = {
  version: '',
  tracks: [],
  loadErrors: [],
  status: { active: false },
  settings: {},
  monitors: [],
  browsers: [],
  dataFolder: '',
};

const subscribers = new Map();

/** subscribe('status', fn) → функция отписки. */
export function subscribe(key, fn) {
  if (!subscribers.has(key)) subscribers.set(key, new Set());
  subscribers.get(key).add(fn);
  return () => subscribers.get(key)?.delete(fn);
}

export function set(key, value) {
  state[key] = value;
  for (const fn of subscribers.get(key) ?? []) {
    try { fn(value); } catch (e) { console.error(e); }
  }
}

export function trackById(id) {
  return state.tracks.find(t => t.id === id);
}
