import { m } from '#lib/paraglide/messages.js';
import { readSetting, storeSetting } from '#lib/settings/table-settings.svelte.js';

// The original appearance store's sidebar options, under the same storage names.
export const SORT_OPTIONS = [
	{ value: 'Sort Alphabetically', get label() { return m.view_settings_appearance_side_panel_sorting_alphabetical(); } },
	{ value: 'Sort by Status', get label() { return m.view_settings_appearance_side_panel_sorting_status(); } },
	{ value: 'Sort Private to Bottom', get label() { return m.view_settings_appearance_side_panel_sorting_private_to_bottom(); } },
	{ value: 'Sort by Last Active', get label() { return m.view_settings_appearance_side_panel_sorting_last_active(); } },
	{ value: 'Sort by Last Seen', get label() { return m.view_settings_appearance_side_panel_sorting_last_seen(); } },
	{ value: 'Sort by Time in Instance', get label() { return m.view_settings_appearance_side_panel_sorting_time_in_instance(); } },
	{ value: 'Sort by Location', get label() { return m.view_settings_appearance_side_panel_sorting_location(); } }
] as const;

const DEFAULT_SORT = ['Sort Private to Bottom', 'Sort by Time in Instance', 'Sort by Last Active'];

function flag(key: string, fallback: boolean) {
	let value = $state(readSetting<boolean>(key, fallback));
	return {
		get value() {
			return value;
		},
		set value(next: boolean) {
			value = next;
			storeSetting(key, next);
		}
	};
}

/** Earlier port builds stored a VRChat group by its bare name; the original keys it friend:group_N. */
function withGroupKeys(keys: string[]): string[] {
	return keys.map((key) => (key.includes(':') ? key : `friend:${key}`));
}

class SidebarSettings {
	groupByInstance = flag('VRCX_sidebarGroupByInstance', true);
	hideSameInstance = flag('VRCX_hideFriendsInSameInstance', false);
	sameInstanceAboveFavorites = flag('VRCX_sameInstanceAboveFavorites', false);
	divideByFriendGroup = flag('VRCX_sidebarDivideByFriendGroup', true);
	cosmetics = flag('VRCX_sidebarCosmetics', false);
	sortMethods = $state<string[]>(readSetting<string[]>('VRCX_sidebarSortMethods', DEFAULT_SORT));
	favoriteGroups = $state<string[]>(withGroupKeys(readSetting<string[]>('VRCX_sidebarFavoriteGroups', [])));
	favoriteGroupOrder = $state<string[]>(withGroupKeys(readSetting<string[]>('VRCX_sidebarFavoriteGroupOrder', [])));

	/** The original's handleSaveSidebarSortOrder: no repeats, and no gaps before a later choice. */
	setSort(index: 0 | 1 | 2, method: string) {
		const order = [...this.sortMethods, '', '', ''].slice(0, 3);
		order[index] = method;
		if (order[0] === order[1]) order[1] = '';
		if (order[0] === order[2]) order[2] = '';
		if (order[1] === order[2]) order[2] = '';
		if (!order[0]) order[1] = '';
		if (!order[1]) order[2] = '';
		this.sortMethods = order;
		storeSetting('VRCX_sidebarSortMethods', order);
	}

	/** An empty list means every group, as in the original's sidebarSettingsUtils. */
	setFavoriteGroups(value: string[], allKeys: string[]) {
		const next = !value.length || (value.length >= allKeys.length && allKeys.every((k) => value.includes(k))) ? [] : value;
		this.favoriteGroups = next;
		storeSetting('VRCX_sidebarFavoriteGroups', next);
	}

	setFavoriteGroupOrder(value: string[]) {
		this.favoriteGroupOrder = value;
		storeSetting('VRCX_sidebarFavoriteGroupOrder', value);
	}
}

export const sidebarSettings = new SidebarSettings();
