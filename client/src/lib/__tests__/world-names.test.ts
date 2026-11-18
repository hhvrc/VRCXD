import { beforeEach, describe, expect, test, vi } from 'vitest';

const get = vi.fn();
vi.mock('#lib/api/client.js', () => ({ api: { get: (...args: unknown[]) => get(...args) } }));
vi.mock('#lib/api/vrchat-auth.svelte.js', () => {
	const vrchatAuth = { ownerUserId: 'usr_me' };
	return { vrchatAuth, ownPath: (rest = '') => `/1/accounts/${encodeURIComponent(vrchatAuth.ownerUserId)}${rest}` };
});

const { worldNames } = await import('../people/names.svelte.js');

// The notifications' world names (a friend's GPS, Online), through the shared cache as the
// original's getWorldName reads its world refs: a friend hopping instances of one world asks once.
describe('world names for notifications', () => {
	beforeEach(() => {
		get.mockReset();
	});

	test('a name is asked for once, however many events want it at the same time', async () => {
		get.mockResolvedValue({ name: 'Club' });
		const names = await Promise.all([worldNames.load('wrld_club'), worldNames.load('wrld_club'), worldNames.load('wrld_club')]);
		expect(names).toEqual(['Club', 'Club', 'Club']);
		expect(await worldNames.load('wrld_club')).toBe('Club');
		expect(get).toHaveBeenCalledTimes(1);
	});

	test('the sync read and the awaited one share the cache', async () => {
		get.mockResolvedValue({ name: 'Lobby' });
		expect(worldNames.get('wrld_lobby')).toBeUndefined();
		expect(await worldNames.load('wrld_lobby')).toBe('Lobby');
		expect(worldNames.get('wrld_lobby')).toBe('Lobby');
		expect(get).toHaveBeenCalledTimes(1);
	});

	test('no world (private, offline) asks nobody, and an unknown one is asked once', async () => {
		expect(await worldNames.load('')).toBeUndefined();
		expect(get).not.toHaveBeenCalled();
		get.mockRejectedValue(new Error('not found'));
		expect(await worldNames.load('wrld_gone')).toBeUndefined();
		expect(await worldNames.load('wrld_gone')).toBeUndefined();
		// Ours, then VRChat's through the server: once each, never again.
		expect(get).toHaveBeenCalledTimes(2);
	});
});
