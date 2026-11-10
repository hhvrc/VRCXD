import { describe, expect, it } from 'vitest';
import { activeNavKey, defaultNavLayout, readNavConfig, routeNavKeys, sanitizeLayout, type NavDefinition, type NavEntry } from '../nav/nav-layout';

const defs = (...keys: string[]): NavDefinition[] => keys.map((key) => ({ key, icon: 'ri', label: key }));

describe('sanitizeLayout', () => {
	it('drops unknown and repeated keys and empty folders', () => {
		const layout = [
			{ type: 'item', key: 'a' },
			{ type: 'item', key: 'gone' },
			{ type: 'folder', id: 'f', name: 'F', icon: '', items: ['a', 'b'] },
			{ type: 'folder', id: 'empty', name: 'E', icon: 'x', items: ['gone'] }
		];
		expect(sanitizeLayout(layout, [], defs('a', 'b'))).toEqual([
			{ type: 'item', key: 'a' },
			{ type: 'folder', id: 'f', name: 'F', icon: 'ri-folder-line', items: ['b'] }
		]);
	});

	it('appends what is neither placed nor hidden, and keeps Direct Access last', () => {
		const layout = [
			{ type: 'item', key: 'direct-access' },
			{ type: 'item', key: 'a' }
		];
		expect(sanitizeLayout(layout, ['c'], defs('a', 'b', 'c', 'direct-access'))).toEqual([
			{ type: 'item', key: 'a' },
			{ type: 'item', key: 'b' },
			{ type: 'item', key: 'direct-access' }
		]);
	});
});

describe('readNavConfig', () => {
	it('falls back to the defaults for nothing or garbage', () => {
		expect(readNavConfig(null).layout).toEqual(defaultNavLayout());
		expect(readNavConfig('{oops').layout).toEqual(defaultNavLayout());
	});

	it('reads the layout and hidden keys, without tool keys in hidden', () => {
		const config = readNavConfig(JSON.stringify({ layout: [{ type: 'item', key: 'a' }], hiddenKeys: ['b', 'tool-x'] }));
		expect(config).toEqual({ layout: [{ type: 'item', key: 'a' }], hiddenKeys: ['b'] });
	});
});

// The original's activeMenuIndex with the routes' navKeys: tool pages count as their pinned tool, then Tools.
describe('the active nav entry', () => {
	const defs: NavDefinition[] = [
		{ key: 'feed', icon: '', label: 'Feed', href: '/feed' },
		{ key: 'tools', icon: '', label: 'Tools', href: '/tools' },
		{ key: 'dashboard-a', icon: '', label: 'A', href: '/dashboard/a' },
		{ key: 'tool-vrchat-store', icon: '', label: 'Store', href: '/tools?open=vrchat-store' }
	];

	it('a section page is its own entry', () => {
		const layout: NavEntry[] = [{ type: 'item', key: 'feed' }, { type: 'item', key: 'tools' }, { type: 'item', key: 'dashboard-a' }];
		expect(activeNavKey(layout, routeNavKeys('/feed', defs))).toBe('feed');
		expect(activeNavKey(layout, routeNavKeys('/dashboard/a', defs))).toBe('dashboard-a');
		expect(activeNavKey(layout, routeNavKeys('/tools', defs))).toBe('tools');
	});

	it('a tool page is its pinned tool, or Tools when it is not pinned', () => {
		expect(routeNavKeys('/tools/vrchat-store', defs)).toEqual(['tool-vrchat-store', 'tools']);
		expect(routeNavKeys('/tools/gallery/', defs)).toEqual(['tool-gallery', 'tools']);
		const pinned: NavEntry[] = [{ type: 'item', key: 'tool-vrchat-store' }, { type: 'item', key: 'tools' }];
		expect(activeNavKey(pinned, routeNavKeys('/tools/vrchat-store', defs))).toBe('tool-vrchat-store');
		expect(activeNavKey([{ type: 'item', key: 'tools' }], routeNavKeys('/tools/screenshot-metadata', defs))).toBe('tools');
	});

	it('the first match in layout order wins, inside folders too', () => {
		const layout: NavEntry[] = [
			{ type: 'folder', id: 'f', name: 'F', icon: '', items: ['feed', 'tools'] },
			{ type: 'item', key: 'tool-vrchat-store' }
		];
		expect(activeNavKey(layout, ['tool-vrchat-store', 'tools'])).toBe('tools');
	});

	it('a page not in the nav lights nothing', () => {
		expect(activeNavKey([{ type: 'item', key: 'feed' }], routeNavKeys('/settings', defs))).toBe('');
		expect(routeNavKeys('/user', defs)).toEqual([]);
	});
});
