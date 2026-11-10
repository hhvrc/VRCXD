import { beforeEach, describe, expect, test, vi } from 'vitest';

vi.mock('$app/env', () => ({ browser: true }));

const store = new Map<string, string>();
vi.stubGlobal('localStorage', {
	getItem: (key: string) => store.get(key) ?? null,
	setItem: (key: string, value: string) => void store.set(key, value),
	removeItem: (key: string) => void store.delete(key)
});

const { readSetting, storeSetting } = await import('../settings/settings-storage');

// The original's table persistence reads its saved JSON with safeJsonParse; the port's settings
// and table state go through readSetting.
describe('per-browser settings', () => {
	beforeEach(() => store.clear());

	test('a saved value is read back as JSON', () => {
		storeSetting('vrcx:table:feed', { sorting: [{ id: 'created', desc: true }] });
		expect(store.get('vrcx:table:feed')).toBe('{"sorting":[{"id":"created","desc":true}]}');
		expect(readSetting('vrcx:table:feed', {})).toEqual({ sorting: [{ id: 'created', desc: true }] });
	});

	test('broken, empty or missing values give the fallback', () => {
		store.set('broken', '{oops');
		store.set('empty', '');
		expect(readSetting('broken', 'fallback')).toBe('fallback');
		expect(readSetting('empty', 'fallback')).toBe('fallback');
		expect(readSetting('missing', 'fallback')).toBe('fallback');
	});
});
