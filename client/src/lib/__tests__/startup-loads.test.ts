import { beforeEach, describe, expect, test, vi } from 'vitest';

const get = vi.hoisted(() => vi.fn());
vi.mock('../api/client.js', async (original) => ({ ...(await original<typeof import('../api/client.js')>()), api: { get, post: vi.fn(async () => ({})) } }));
const auth = vi.hoisted(() => ({ ownerUserId: 'usr_me' }));
vi.mock('../api/vrchat-auth.svelte.js', () => ({ vrchatAuth: auth, ownPath: (rest = '') => `/1/accounts/${encodeURIComponent(auth.ownerUserId)}${rest}` }));

const { desktop, instanceCatalog, loadInstanceCategoriesAndVibes } = await import('../instances/instance-launch.svelte.js');
const { wristHistoryDue } = await import('../notifications/wrist-feed.svelte.js');
const { sidebarData } = await import('../people/sidebar-data.svelte.js');

beforeEach(() => {
	get.mockReset();
});

// What the app reads when it opens, each once and only when something will use it.
describe('startup reads', () => {
	test("the wrist overlay's history waits for an agent that draws the overlay", () => {
		sidebarData.friendsLoaded = true;
		sidebarData.me = { id: 'usr_me', displayName: 'Me', status: 'active', statusDescription: '', statusHistory: [], isBoopingEnabled: false };
		desktop.connected = false;
		desktop.capabilities = ['openvr'];
		expect(wristHistoryDue(false)).toBe(false);
		desktop.connected = true;
		desktop.capabilities = ['launch'];
		expect(wristHistoryDue(false)).toBe(false);
		// The agent comes (back) with the overlay: now it reads, once.
		desktop.capabilities = ['launch', 'openvr'];
		expect(wristHistoryDue(false)).toBe(true);
		expect(wristHistoryDue(true)).toBe(false);
		sidebarData.friendsLoaded = false;
		expect(wristHistoryDue(false)).toBe(false);
	});

	test('the instance categories are read once when the layout and the tooltips ask together', async () => {
		let release: (value: unknown) => void = () => {};
		get.mockImplementation(() => new Promise((resolve) => (release = resolve)));
		const asks = [loadInstanceCategoriesAndVibes(), loadInstanceCategoriesAndVibes(), loadInstanceCategoriesAndVibes()];
		release({ categories: [{ id: 'c', name: 'Chill', order: 1 }], vibes: [{ id: 'v', name: 'Calm' }] });
		await Promise.all(asks);
		expect(get).toHaveBeenCalledTimes(1);
		expect(instanceCatalog.categories).toHaveLength(1);
		await loadInstanceCategoriesAndVibes();
		expect(get).toHaveBeenCalledTimes(1);
	});
});
