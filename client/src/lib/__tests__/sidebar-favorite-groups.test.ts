import { beforeEach, describe, expect, test, vi } from 'vitest';

vi.mock('../api/vrchat-auth.svelte', () => {
	const vrchatAuth = { ownerUserId: '' };
	return { vrchatAuth, ownPath: (rest = '') => `/1/accounts/${encodeURIComponent(vrchatAuth.ownerUserId)}${rest}` };
});

const { sidebarSettings } = await import('../settings/sidebar-settings.svelte');
const { sidebarData } = await import('../people/sidebar-data.svelte');

const allKeys = ['friend:group_1', 'friend:group_2', 'local:MyGroup'];
const group = (key: string) => ({ key, name: key, members: new Set([`usr_${key}`]) });

// The original's sidebarSettingsUtils: an empty pick means every favorite group.
describe('sidebar favorite groups', () => {
	beforeEach(() => {
		sidebarSettings.setFavoriteGroups([], allKeys);
		sidebarData.groups = allKeys.map(group);
	});

	test('picking none or every group stores the empty pick', () => {
		sidebarSettings.setFavoriteGroups([], allKeys);
		expect(sidebarSettings.favoriteGroups).toEqual([]);
		sidebarSettings.setFavoriteGroups([...allKeys], allKeys);
		expect(sidebarSettings.favoriteGroups).toEqual([]);
		sidebarSettings.setFavoriteGroups([...allKeys, 'extra'], allKeys);
		expect(sidebarSettings.favoriteGroups).toEqual([]);
	});

	test('picking some groups stores them', () => {
		sidebarSettings.setFavoriteGroups(['friend:group_1'], allKeys);
		expect(sidebarSettings.favoriteGroups).toEqual(['friend:group_1']);
		sidebarSettings.setFavoriteGroups(['friend:group_1', 'friend:group_2'], allKeys);
		expect(sidebarSettings.favoriteGroups).toEqual(['friend:group_1', 'friend:group_2']);
	});

	test('with no groups at all, any pick counts as every group', () => {
		sidebarSettings.setFavoriteGroups(['friend:group_1'], []);
		expect(sidebarSettings.favoriteGroups).toEqual([]);
	});

	test('the empty pick shows every group, a pick shows only those', () => {
		expect(sidebarData.vipGroups.map((g) => g.key)).toEqual(allKeys);
		sidebarSettings.setFavoriteGroups(['friend:group_1'], allKeys);
		expect(sidebarData.vipGroups.map((g) => g.key)).toEqual(['friend:group_1']);
		expect([...sidebarData.vipIds]).toEqual(['usr_friend:group_1']);
	});
});
