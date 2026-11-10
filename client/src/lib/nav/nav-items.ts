import { m } from '#lib/paraglide/messages.js';
// Mirrors src/shared/constants/ui.js's navDefinitions in the existing Vue
// app - same keys, icons (Remix Icon, matching the original exactly), and
// grouping, so this skeleton's nav is a faithful structural port, not a
// placeholder invented from scratch.

export type NavItem = {
	key: string;
	icon: string;
	label: string;
};

export type NavGroup = {
	label: string;
	items: NavItem[];
};

export const navTop: NavItem[] = [
	{ key: 'feed', icon: 'ri-rss-line', get label() { return m.nav_tooltip_feed(); } },
	{ key: 'friends-locations', icon: 'ri-user-location-line', label: 'Friends Map' },
	{ key: 'game-log', icon: 'ri-history-line', get label() { return m.nav_tooltip_game_log(); } },
	{ key: 'player-list', icon: 'ri-group-3-line', get label() { return m.nav_tooltip_player_list(); } },
	{ key: 'search', icon: 'ri-search-line', get label() { return m.nav_tooltip_search(); } }
];

export const navGroups: NavGroup[] = [
	{
		get label() { return m.nav_tooltip_favorites(); },
		items: [
			{ key: 'favorite-friends', icon: 'ri-user-heart-line', get label() { return m.side_panel_friends(); } },
			{ key: 'favorite-worlds', icon: 'ri-earth-line', get label() { return m.side_panel_search_worlds(); } },
			{ key: 'favorite-avatars', icon: 'ri-empathize-line', get label() { return m.side_panel_search_avatars(); } }
		]
	},
	{
		get label() { return m.nav_tooltip_social(); },
		items: [
			{ key: 'friend-log', icon: 'ri-contacts-line', get label() { return m.nav_tooltip_friend_log(); } },
			{ key: 'friend-list', icon: 'ri-booklet-line', get label() { return m.nav_tooltip_friend_list(); } },
			{ key: 'moderation', icon: 'ri-shield-user-line', get label() { return m.nav_tooltip_moderation(); } }
		]
	}
];

export const navMiddle: NavItem[] = [
	{ key: 'notification', icon: 'ri-notification-2-line', get label() { return m.side_panel_notifications(); } },
	{ key: 'my-avatars', icon: 'ri-contacts-book-3-line', get label() { return m.nav_tooltip_my_avatars(); } }
];

export const navCharts: NavGroup = {
	get label() { return m.nav_tooltip_charts(); },
	items: [
		{ key: 'charts-instance', icon: 'ri-bar-chart-horizontal-line', get label() { return m.view_charts_instance_activity_header(); } },
		{ key: 'charts-mutual', icon: 'ri-group-2-line', get label() { return m.table_friendList_mutualFriends(); } },
		{ key: 'charts-hot-worlds', icon: 'ri-fire-line', get label() { return m.view_charts_hot_worlds_header(); } }
	]
};

export const navBottom: NavItem[] = [{ key: 'tools', icon: 'ri-tools-line', get label() { return m.nav_tooltip_tools(); } }];

export const allNavItems: NavItem[] = [
	...navTop,
	...navGroups.flatMap((g) => g.items),
	...navMiddle,
	...navCharts.items,
	...navBottom
];
