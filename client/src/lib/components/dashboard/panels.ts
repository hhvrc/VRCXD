import { m } from '#lib/paraglide/messages.js';
import type { Component } from 'svelte';
import { allNavItems } from '#lib/nav/nav-items.js';

// The original's panelRegistry and PanelSelector: whole pages a panel can show, and the widgets.
// Friend List, Moderation, the charts and Tools are blocked in dashboards, as there.
type Loader = () => Promise<{ default: Component<any> }>;

export const PAGE_PANELS: Record<string, Loader> = {
	feed: () => import('../../../routes/feed/+page.svelte'),
	'friends-locations': () => import('../../../routes/friends-locations/+page.svelte'),
	'game-log': () => import('../../../routes/game-log/+page.svelte'),
	'player-list': () => import('../../../routes/player-list/+page.svelte'),
	search: () => import('../../../routes/search/+page.svelte'),
	'favorite-friends': () => import('../../../routes/favorite-friends/+page.svelte'),
	'favorite-worlds': () => import('../../../routes/favorite-worlds/+page.svelte'),
	'favorite-avatars': () => import('../../../routes/favorite-avatars/+page.svelte'),
	'friend-log': () => import('../../../routes/friend-log/+page.svelte'),
	notification: () => import('../../../routes/notification/+page.svelte')
};

/**
 * The original's DASHBOARD_BLOCKED_PANEL_KEYS: not offered in the panel picker, but a saved
 * dashboard that already has one still shows the page, as the original's registry does.
 */
const BLOCKED_PANELS: Record<string, Loader> = {
	'friend-list': () => import('../../../routes/friend-list/+page.svelte'),
	moderation: () => import('../../../routes/moderation/+page.svelte'),
	'charts-instance': () => import('../../../routes/charts-instance/+page.svelte'),
	'charts-mutual': () => import('../../../routes/charts-mutual/+page.svelte'),
	'charts-hot-worlds': () => import('../../../routes/charts-hot-worlds/+page.svelte'),
	tools: () => import('../../../routes/tools/+page.svelte')
};

export type WidgetKey = 'widget:feed' | 'widget:game-log' | 'widget:instance';

export const WIDGETS: { key: WidgetKey; icon: string; label: string; route: string; defaultConfig: Record<string, unknown>; load: Loader }[] = [
	{ key: 'widget:feed', icon: 'ri-rss-line', get label() { return m.dashboard_widget_feed(); }, route: '/feed', defaultConfig: { filters: [] }, load: () => import('./feed-widget.svelte') },
	{ key: 'widget:game-log', icon: 'ri-history-line', get label() { return m.dashboard_widget_game_log(); }, route: '/game-log', defaultConfig: { filters: [] }, load: () => import('./game-log-widget.svelte') },
	{
		key: 'widget:instance',
		icon: 'ri-group-3-line',
		get label() { return m.dashboard_widget_instance(); },
		route: '/player-list',
		defaultConfig: { columns: ['icon', 'displayName', 'rank', 'timer'] },
		load: () => import('./instance-widget.svelte')
	}
];

export const pageOptions = allNavItems.filter((item) => item.key in PAGE_PANELS);

export function panelInfo(key: string | null): { icon: string; label: string } | null {
	if (!key) return null;
	const widget = WIDGETS.find((w) => w.key === key);
	if (widget) return widget;
	return allNavItems.find((item) => item.key === key) ?? null;
}

export function panelLoader(key: string | null): Loader | null {
	if (!key) return null;
	return WIDGETS.find((w) => w.key === key)?.load ?? PAGE_PANELS[key] ?? BLOCKED_PANELS[key] ?? null;
}
