// Hand-written browser glue: service worker, tab lock, IndexedDB and Web Storage.
if ('serviceWorker' in navigator) {
    navigator.serviceWorker.register('service-worker.js', { updateViaCache: 'none' });
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

const dbs = new Map(); // name -> Promise<IDBDatabase>

function open(name) {
    let db = dbs.get(name);
    if (!db) {
        db = new Promise((resolve, reject) => {
            const req = indexedDB.open(name, 1);
            req.onupgradeneeded = () => req.result.createObjectStore('records');
            req.onsuccess = () => {
                const conn = req.result;
                // Let a delete (or a newer version) proceed, and reopen on next use.
                conn.onversionchange = () => { conn.close(); dbs.delete(name); };
                conn.onclose = () => dbs.delete(name);
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
    const db = await open(dbName);
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
        if (failWrites > 0) { failWrites--; tx.abort(); }
    });
}

export async function idbDelete(dbName) {
    requireLock();
    const db = dbs.get(dbName);
    dbs.delete(dbName);
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
    a.click();
    setTimeout(() => URL.revokeObjectURL(url), 10000);
}
