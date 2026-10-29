// Hand-written browser glue: service worker, tab lock, IndexedDB, Web Storage, downloads and dialogs.

// ---- Service worker and updates (spec decision 14): a new version waits, the banner asks, the page never reloads by itself ----

// Called once by the UpdateBanner. A failed registration (no service worker support, private mode, a blocked script)
// only costs offline use and updates, so it is logged, not thrown.
export async function registerWorker(dotnetRef) {
    if (!('serviceWorker' in navigator)) return;
    try {
        const reg = await navigator.serviceWorker.register('service-worker.js', { updateViaCache: 'none' });
        // Only an update: the first install happens with no controller and needs no reload.
        const ready = () => { if (navigator.serviceWorker.controller) dotnetRef.invokeMethodAsync('UpdateReady'); };
        const track = worker => worker?.addEventListener('statechange', () => { if (worker.state === 'installed') ready(); });
        // The update check of this page's navigation started before .NET booted, so a new worker may already be
        // waiting or still installing; updatefound has fired for it by now.
        if (reg.waiting) ready();
        track(reg.installing);
        reg.addEventListener('updatefound', () => track(reg.installing));
        setInterval(() => reg.update().catch(e => console.warn('Checking for a new version failed', e)), 60 * 60 * 1000);
    } catch (e) {
        console.error('Service worker registration failed', e);
    }
}

// Called after .NET has saved and flushed. The waiting worker takes over, then the page reloads onto it.
export async function applyUpdate() {
    const reg = await navigator.serviceWorker.getRegistration();
    if (!reg?.waiting) { location.reload(); return; } // already activated (by another tab): just load it
    navigator.serviceWorker.addEventListener('controllerchange', () => location.reload(), { once: true });
    reg.waiting.postMessage('SKIP_WAITING');
}

export function isStandalone() {
    return matchMedia('(display-mode: standalone)').matches || navigator.standalone === true;
}

// ---- One tab at a time (spec decision 7): Web Lock "hearthsheet", take-over over BroadcastChannel "hearthsheet" ----

let held = false;       // IndexedDB is refused unless this tab holds the lock, so a read-only tab can't write.
let releaseLock = null; // resolves the promise that keeps the lock
let owner = null;       // the .NET TabLock
const channel = new BroadcastChannel('hearthsheet');
channel.onmessage = e => {
    if (e.data === 'release' && held) owner?.invokeMethodAsync('ReleaseRequested');
};

function requireLock() {
    if (!held) throw new Error('Hearthsheet is open in another tab.');
}

// Resolves true once granted, false if not available (ifAvailable). Rejects if aborted before being granted. A lock
// lost after the grant (another tab stole it) makes this tab read-only at once and tells .NET through LockLost.
function request(options) {
    return new Promise((resolve, reject) => {
        let granted = false;
        navigator.locks.request('hearthsheet', options, lock => {
            if (!lock) { resolve(false); return; }
            granted = true;
            held = true;
            resolve(true);
            return new Promise(r => { releaseLock = r; });
        }).catch(e => {
            if (!granted) { reject(e); return; }
            held = false;
            releaseLock = null;
            owner?.invokeMethodAsync('LockLost');
        });
    });
}

// Leaving the page (tab closed, switched away, app minimised): save now, the autosave may not get its turn. The call
// is synchronous so the IndexedDB write has started before a closing page stops running script. pagehide covers a tab
// closed while already hidden (no visibilitychange then).
function pageHidden() {
    if (held) owner?.invokeMethod('PageHidden');
}
document.addEventListener('visibilitychange', () => { if (document.visibilityState === 'hidden') pageHidden(); });
window.addEventListener('pagehide', pageHidden);

// ---- Sync triggers (spec decision 10): back online, and visibility (the 5-minute timer syncs only while visible).
// Hidden also goes through pageHidden above: save, flush, then a pass. A tab without the lock never syncs. ----

let pageWatcher = null; // the .NET Shell
let watcherId = 0;      // which Shell registered (each call gets a new JS wrapper, so refs can't be compared)
window.addEventListener('online', () => { if (held) pageWatcher?.invokeMethodAsync('Online'); });
document.addEventListener('visibilitychange', () => pageWatcher?.invokeMethodAsync('Visible', document.visibilityState === 'visible'));

export function watchPage(dotnetRef, id) {
    pageWatcher = dotnetRef;
    watcherId = id;
    return document.visibilityState === 'visible';
}

// A newer Shell may already be watching; only the one that registered clears it.
export function unwatchPage(id) { if (watcherId === id) pageWatcher = null; }

export function lockAcquire(dotnetRef) {
    owner = dotnetRef;
    return request({ ifAvailable: true });
}

export async function lockTakeOver() {
    channel.postMessage('release');
    try {
        return await request({ signal: AbortSignal.timeout(3000) });
    } catch {
        // The holder didn't let go within 3 s (busy, frozen or in the background): take it anyway.
        return await request({ steal: true });
    }
}

// Called by .NET after it has saved and flushed. Refuses IndexedDB from here on, then lets the lock go.
export function lockRelease() {
    held = false;
    releaseLock?.();
    releaseLock = null;
}

// ---- IndexedDB: one database per name, one object store "records" (key = record key, value = JSON string) ----

const dbs = new Map();   // name -> Promise<IDBDatabase>
const conns = new Map(); // name -> IDBDatabase, once open

function open(name) {
    let db = dbs.get(name);
    if (!db) {
        db = new Promise((resolve, reject) => {
            const req = indexedDB.open(name, 1);
            req.onupgradeneeded = () => req.result.createObjectStore('records');
            req.onsuccess = () => {
                const conn = req.result;
                // Let a delete (or a newer version) proceed, and reopen on next use.
                conn.onversionchange = () => { conn.close(); dbs.delete(name); conns.delete(name); };
                conn.onclose = () => { dbs.delete(name); conns.delete(name); };
                conns.set(name, conn);
                resolve(conn);
            };
            req.onerror = () => { dbs.delete(name); reject(req.error); };
            req.onblocked = () => { dbs.delete(name); reject(new Error(`Opening ${name} is blocked by another tab.`)); };
        });
        dbs.set(name, db);
    }
    return db;
}

export async function idbLoad(dbName) {
    requireLock();
    const db = await open(dbName);
    return new Promise((resolve, reject) => {
        const tx = db.transaction('records', 'readonly');
        const store = tx.objectStore('records');
        const keys = store.getAllKeys();
        const values = store.getAll();
        tx.oncomplete = () => resolve(keys.result.map((key, i) => ({ key, json: values.result[i] })));
        tx.onerror = e => reject(e.target.error ?? tx.error ?? new Error('IndexedDB request failed.'));
        tx.onabort = () => reject(tx.error ?? new Error('Load aborted.'));
    });
}

let failWrites = 0;

export async function idbWrite(dbName, batch) {
    requireLock();
    // An open connection is used at once, so the transaction exists before this call returns: a page closing right
    // after a save (see visibilitychange) still commits it.
    const db = conns.get(dbName) ?? await open(dbName);
    return new Promise((resolve, reject) => {
        const tx = db.transaction('records', 'readwrite');
        const store = tx.objectStore('records');
        for (const { key, json } of batch) {
            if (json === null || json === undefined) store.delete(key);
            else store.put(json, key);
        }
        tx.oncomplete = () => resolve();
        tx.onerror = e => reject(e.target.error ?? tx.error ?? new Error('IndexedDB request failed.'));
        tx.onabort = () => reject(tx.error ?? new Error('Write aborted.'));
        if (failWrites > 0) { failWrites--; tx.abort(); return; }
        tx.commit?.(); // don't wait for the event loop: a closing page may not give it another turn
    });
}

export async function idbDelete(dbName) {
    requireLock();
    const db = dbs.get(dbName);
    dbs.delete(dbName);
    conns.delete(dbName);
    if (db) (await db.catch(() => null))?.close();
    return new Promise((resolve, reject) => {
        const req = indexedDB.deleteDatabase(dbName);
        req.onsuccess = () => resolve();
        req.onerror = () => reject(req.error);
        req.onblocked = () => reject(new Error(`Deleting ${dbName} is blocked by another tab.`));
    });
}

// Test-only: abort the next n idbWrite transactions. Exported for the browser console; app code never calls it.
export function failNextWrites(n) { failWrites = n; }

export async function persist() {
    return navigator.storage?.persist ? await navigator.storage.persist() : false;
}

// ---- Synchronous Web Storage (called through IJSInProcessObjectReference) ----

export function local(key) { return localStorage.getItem(key); }
export function setLocal(key, value) { value === null ? localStorage.removeItem(key) : localStorage.setItem(key, value); }
export function session(key) { return sessionStorage.getItem(key); }
export function setSession(key, value) { value === null ? sessionStorage.removeItem(key) : sessionStorage.setItem(key, value); }

// ---- Downloads ----

export function download(name, mime, bytes) {
    const url = URL.createObjectURL(new Blob([bytes], { type: mime }));
    const a = document.createElement('a');
    a.href = url;
    a.download = name;
    document.body.append(a); // Firefox ignores clicks on a detached link
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 10000);
}

// ---- Dialogs: native modal <dialog> (inert background, Escape fires "close"); focus returns to the trigger ----

let returnFocus = null;

export function dialogShow(el) {
    returnFocus ??= document.activeElement; // the trigger, kept while queued dialogs follow one another
    el.showModal();
}

export function dialogsDone() {
    returnFocus?.focus?.();
    returnFocus = null;
}
