import { describe, expect, test } from 'vitest';
import { normalizeHiddenKeys, sanitizeLayout, type NavDefinition } from '../nav/nav-layout';

// The original's navMenuUtils cases, over the port's nav-layout.
const defs: NavDefinition[] = ['feed', 'search', 'tools', 'notification', 'direct-access'].map((key) => ({ key, icon: 'ri', label: key }));
const known = new Set(defs.map((d) => d.key));

describe('normalizeHiddenKeys', () => {
	test('anything but an array hides nothing', () => {
		expect(normalizeHiddenKeys(null, known)).toEqual([]);
		expect(normalizeHiddenKeys(undefined, known)).toEqual([]);
		expect(normalizeHiddenKeys('string', known)).toEqual([]);
		expect(normalizeHiddenKeys(42, known)).toEqual([]);
		expect(normalizeHiddenKeys([], known)).toEqual([]);
	});

	test('unknown, empty and repeated keys are dropped, the order stays', () => {
		expect(normalizeHiddenKeys(['feed', 'nonexistent', 'search'], known)).toEqual(['feed', 'search']);
		expect(normalizeHiddenKeys(['feed', 'feed', 'search'], known)).toEqual(['feed', 'search']);
		expect(normalizeHiddenKeys([null, '', undefined, 'feed'], known)).toEqual(['feed']);
		expect(normalizeHiddenKeys(['tools', 'feed', 'search'], known)).toEqual(['tools', 'feed', 'search']);
	});
});

describe('sanitizeLayout cases from the original', () => {
	test('no saved layout gives every entry', () => {
		const result = sanitizeLayout(null, [], defs);
		expect(result.map((e) => e.type === 'item' && e.key)).toEqual(['feed', 'search', 'tools', 'notification', 'direct-access']);
	});

	test('a valid item keeps its place and a repeated one appears once', () => {
		const result = sanitizeLayout(
			[
				{ type: 'item', key: 'tools' },
				{ type: 'item', key: 'tools' },
				{ type: 'item', key: 'nonexistent' }
			],
			[],
			defs
		);
		expect(result[0]).toEqual({ type: 'item', key: 'tools' });
		expect(result.filter((e) => e.type === 'item' && e.key === 'tools')).toHaveLength(1);
		expect(result.some((e) => e.type === 'item' && e.key === 'nonexistent')).toBe(false);
	});

	test('a folder keeps its name and valid items, and gets an id when it has none', () => {
		const result = sanitizeLayout(
			[
				{ type: 'folder', id: 'my-folder', name: 'My Folder', icon: 'ri-star-line', items: ['feed', 'search'] },
				{ type: 'folder', name: 'No ID Folder', items: ['tools'] }
			],
			[],
			defs
		);
		const folders = result.filter((e) => e.type === 'folder');
		expect(folders[0]).toEqual({ type: 'folder', id: 'my-folder', name: 'My Folder', icon: 'ri-star-line', items: ['feed', 'search'] });
		expect(folders[1].type === 'folder' && folders[1].id).toMatch(/^nav-folder-/);
	});

	test('missing entries are appended unless hidden', () => {
		const result = sanitizeLayout([{ type: 'item', key: 'feed' }], ['search', 'tools'], defs);
		const keys = result.map((e) => e.type === 'item' && e.key);
		expect(keys).toEqual(['feed', 'notification', 'direct-access']);
	});
});
