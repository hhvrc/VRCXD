import type { Friend } from '#lib/api/types.js';
import { readSetting } from '#lib/settings/table-settings.svelte.js';
import { sortStatus } from '#lib/people/user-display.js';

// The original's getFriendsSortFunction over the sidebar's sort setting. Friend lists keep this
// order everywhere they show friends.
type Compare = (a: Friend, b: Friend) => number;

const DEFAULT_METHODS = ['Sort Private to Bottom', 'Sort by Time in Instance', 'Sort by Last Active'];

const byName: Compare = (a, b) => a.displayName.localeCompare(b.displayName);

const byPrivate: Compare = (a, b) => {
	if (a.location === 'private' && b.location === 'private') return 0;
	if (a.location === 'private') return 1;
	if (b.location === 'private') return -1;
	return 0;
};

const byStatus: Compare = (a, b) => {
	if (a.status === b.status) return 0;
	if (a.state === 'offline') return 1;
	return sortStatus(a.status, b.status);
};

/** Newest first, and an empty value means in that state for longest. */
function byDate(a: string | null | undefined, b: string | null | undefined): number {
	const left = a ?? '';
	const right = b ?? '';
	if (left < right || (left !== '' && right === '')) return 1;
	if (left > right || (left === '' && right !== '')) return -1;
	return 0;
}

const byLastActive: Compare = (a, b) => {
	if (a.state === 'online' && b.state === 'online') {
		if (a.onlineFor && b.onlineFor && a.onlineFor === b.onlineFor) return byDate(a.lastLogin, b.lastLogin);
		return byDate(a.onlineFor, b.onlineFor);
	}
	return byDate(a.lastActivity, b.lastActivity);
};

const byLastActivity: Compare = (a, b) => byDate(a.lastActivity, b.lastActivity);

const epoch = (value: string | null | undefined) => (value ? Date.parse(value) || 0 : 0);

/** Newest arrival first; friends pending offline go to the bottom. */
const byTimeInInstance: Compare = (a, b) => {
	if (!!a.pendingOffline !== !!b.pendingOffline) return a.pendingOffline ? 1 : -1;
	return epoch(b.locationSince) - epoch(a.locationSince);
};

const byLocation: Compare = (a, b) => {
	if (a.state !== 'online' || b.state !== 'online') return 0;
	return (a.location ?? '').localeCompare(b.location ?? '');
};

const METHODS: Record<string, Compare> = {
	'Sort Alphabetically': byName,
	'Sort Private to Bottom': byPrivate,
	'Sort by Status': byStatus,
	'Sort by Last Active': byLastActive,
	'Sort by Last Seen': byLastActivity,
	'Sort by Time in Instance': byTimeInInstance,
	'Sort by Location': byLocation,
	None: () => 0
};

export function friendComparator(methods?: readonly string[]): Compare {
	const sorts = (methods ?? readSetting<string[]>('VRCX_sidebarSortMethods', DEFAULT_METHODS))
		.map((method) => METHODS[method])
		.filter(Boolean);
	sorts.push(byName);
	return (a, b) => {
		for (const sort of sorts) {
			const result = sort(a, b);
			if (result !== 0) return result;
		}
		return 0;
	};
}
