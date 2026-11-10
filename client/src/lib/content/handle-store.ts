// File System Access handles kept between visits (a handle can't go in localStorage, but IndexedDB
// stores it). The browser still asks again before the page may read through it.

const DB = 'vrcx-handles';
const STORE = 'handles';

function open(): Promise<IDBDatabase> {
	return new Promise((resolve, reject) => {
		const request = indexedDB.open(DB, 1);
		request.onupgradeneeded = () => request.result.createObjectStore(STORE);
		request.onsuccess = () => resolve(request.result);
		request.onerror = () => reject(request.error);
	});
}

export async function loadHandle<T>(key: string): Promise<T | undefined> {
	try {
		const db = await open();
		return await new Promise<T | undefined>((resolve, reject) => {
			const request = db.transaction(STORE).objectStore(STORE).get(key);
			request.onsuccess = () => resolve(request.result as T | undefined);
			request.onerror = () => reject(request.error);
		});
	} catch {
		return undefined;
	}
}

export async function saveHandle(key: string, handle: unknown): Promise<void> {
	try {
		const db = await open();
		await new Promise<void>((resolve, reject) => {
			const tx = db.transaction(STORE, 'readwrite');
			tx.objectStore(STORE).put(handle, key);
			tx.oncomplete = () => resolve();
			tx.onerror = () => reject(tx.error);
		});
	} catch {
		// Remembering the folder is a convenience; the page works without it.
	}
}
