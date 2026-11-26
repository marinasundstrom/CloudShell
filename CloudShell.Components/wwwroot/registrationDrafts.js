// Transient per-document browser memory: no localStorage, sessionStorage, or disk persistence.
// Callers supply explicitly non-secret models. Limit abandoned drafts to twenty.
const drafts = new Map();
export function read(key) { return drafts.get(key) ?? null; }
export function write(key, value) {
    drafts.delete(key);
    drafts.set(key, value);
    while (drafts.size > 20) drafts.delete(drafts.keys().next().value);
}
export function remove(key) { drafts.delete(key); }
