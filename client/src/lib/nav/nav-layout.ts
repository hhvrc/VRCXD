// The original's custom nav layout (nav-menu/navMenuUtils.js, navLayoutDefaults.js): the nav is one
// ordered list of items and folders of items. Anything not placed and not hidden is appended, and
// Direct Access always stays last.

export const NAV_CONFIG_KEY = 'VRCX_customNavMenuLayoutList';
export const DASHBOARD_NAV_KEY_PREFIX = 'dashboard-';
export const TOOL_NAV_KEY_PREFIX = 'tool-';
export const DEFAULT_FOLDER_ICON = 'ri-folder-line';

export type NavItemEntry = { type: 'item'; key: string };
export type NavFolderEntry = { type: 'folder'; id: string; name: string; icon: string; items: string[] };
export type NavEntry = NavItemEntry | NavFolderEntry;
export type NavDefinition = { key: string; icon: string; label: string; href?: string; action?: 'direct-access' };
export type NavConfig = { layout: NavEntry[]; hiddenKeys: string[] };

export const isToolNavKey = (key: string) => key.startsWith(TOOL_NAV_KEY_PREFIX);
export const isDashboardNavKey = (key: string) => key.startsWith(DASHBOARD_NAV_KEY_PREFIX);

/** The port's sections as the original's default layout: loose items, then folders. */
export function defaultNavLayout(): NavEntry[] {
	return [
		{ type: 'item', key: 'feed' },
		{ type: 'item', key: 'friends-locations' },
		{ type: 'item', key: 'game-log' },
		{ type: 'item', key: 'player-list' },
		{ type: 'item', key: 'search' },
		{ type: 'folder', id: 'default-folder-favorites', name: 'Favorites', icon: 'ri-star-line', items: ['favorite-friends', 'favorite-worlds', 'favorite-avatars'] },
		{ type: 'folder', id: 'default-folder-social', name: 'Social', icon: 'ri-group-line', items: ['friend-log', 'friend-list', 'moderation'] },
		{ type: 'item', key: 'notification' },
		{ type: 'item', key: 'my-avatars' },
		{ type: 'folder', id: 'default-folder-charts', name: 'Charts', icon: 'ri-pie-chart-line', items: ['charts-instance', 'charts-mutual', 'charts-hot-worlds'] },
		{ type: 'item', key: 'tools' },
		{ type: 'item', key: 'direct-access' }
	];
}

export function generateNavFolderId(): string {
	return typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function' ? `nav-folder-${crypto.randomUUID()}` : `nav-folder-${Date.now()}-${Math.random().toString(16).slice(2)}`;
}

/** normalizeHiddenKeys: known keys, once each. */
export function normalizeHiddenKeys(hiddenKeys: unknown, known: ReadonlySet<string>): string[] {
	if (!Array.isArray(hiddenKeys)) return [];
	return [...new Set(hiddenKeys.filter((k): k is string => typeof k === 'string' && known.has(k)))];
}

/** sanitizeLayout: drop unknown and repeated keys and empty folders, append the rest, Direct Access last. */
export function sanitizeLayout(layout: unknown, hiddenKeys: readonly string[], definitions: readonly NavDefinition[]): NavEntry[] {
	const known = new Set(definitions.map((d) => d.key));
	const hidden = new Set(hiddenKeys);
	const used = new Set<string>();
	const out: NavEntry[] = [];
	const take = (key: unknown): key is string => typeof key === 'string' && known.has(key) && !used.has(key) && (used.add(key), true);

	for (const entry of Array.isArray(layout) ? layout : []) {
		if (entry?.type === 'item') {
			if (take(entry.key)) out.push({ type: 'item', key: entry.key });
		} else if (entry?.type === 'folder') {
			const items = (Array.isArray(entry.items) ? entry.items : []).filter(take);
			if (items.length) out.push({ type: 'folder', id: typeof entry.id === 'string' && entry.id ? entry.id : generateNavFolderId(), name: typeof entry.name === 'string' ? entry.name : '', icon: typeof entry.icon === 'string' && entry.icon ? entry.icon : DEFAULT_FOLDER_ICON, items });
		}
	}
	for (const definition of definitions) {
		if (!used.has(definition.key) && !hidden.has(definition.key)) {
			used.add(definition.key);
			out.push({ type: 'item', key: definition.key });
		}
	}
	const directAccess = out.findIndex((e) => e.type === 'item' && e.key === 'direct-access');
	if (directAccess !== -1 && directAccess !== out.length - 1) out.push(...out.splice(directAccess, 1));
	return out;
}

/**
 * The original's route navKeys: the entries a page counts as, most specific first. A tool page
 * (/tools/vrchat-store) counts as its pinned tool, then as Tools; any other page as the entries it is.
 */
export function routeNavKeys(pathname: string, definitions: readonly NavDefinition[]): string[] {
	const tool = /^\/tools\/([^/]+)\/?$/.exec(pathname);
	if (tool) return [`${TOOL_NAV_KEY_PREFIX}${tool[1]}`, 'tools'];
	return definitions.filter((d) => !!d.href && !d.href.includes('?') && d.href === pathname).map((d) => d.key);
}

/** activeMenuIndex: the first entry, in layout order, the page counts as; none when it is not in the nav. */
export function activeNavKey(layout: readonly NavEntry[], navKeys: readonly string[]): string {
	for (const entry of layout) {
		if (entry.type === 'item' && navKeys.includes(entry.key)) return entry.key;
		if (entry.type === 'folder') {
			const key = navKeys.find((k) => entry.items.includes(k));
			if (key) return key;
		}
	}
	return '';
}

/** loadStoredNavConfig: the saved layout and hidden keys, or the defaults. */
export function readNavConfig(raw: string | null): NavConfig {
	try {
		const parsed = raw ? JSON.parse(raw) : null;
		const layout = Array.isArray(parsed?.layout) ? parsed.layout : Array.isArray(parsed) ? parsed : null;
		const hiddenKeys = Array.isArray(parsed?.hiddenKeys) ? parsed.hiddenKeys.filter((k: unknown): k is string => typeof k === 'string' && !isToolNavKey(k)) : [];
		if (layout?.length) return { layout, hiddenKeys };
	} catch {
		// A broken value falls back to the defaults.
	}
	return { layout: defaultNavLayout(), hiddenKeys: [] };
}
