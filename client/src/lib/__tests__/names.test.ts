import { beforeEach, describe, expect, test, vi } from 'vitest';

const get = vi.fn();
vi.mock('#lib/api/client.js', () => ({ api: { get: (...args: unknown[]) => get(...args) } }));
vi.mock('#lib/api/vrchat-auth.svelte.js', () => {
	const vrchatAuth = { ownerUserId: 'usr_me' };
	return { vrchatAuth, ownPath: (rest = '') => `/1/accounts/${encodeURIComponent(vrchatAuth.ownerUserId)}${rest}` };
});

const { userNames, worldNames } = await import('../people/names.svelte.js');

// The original's resolveRef (and getWorldName): a name for something only known by its id, asked
// for once, the id shown until it comes.
describe('names by id', () => {
	beforeEach(() => get.mockReset());

	test('no id has no name and asks nobody', () => {
		expect(worldNames.get(null)).toBeUndefined();
		expect(worldNames.get(undefined)).toBeUndefined();
		expect(worldNames.get('')).toBeUndefined();
		expect(get).not.toHaveBeenCalled();
	});

	test('an unknown id is fetched and its name comes after', async () => {
		get.mockResolvedValue({ name: 'MyWorld', extra: true });
		expect(worldNames.get('wrld_123')).toBeUndefined();
		expect(get).toHaveBeenCalledWith('/1/worlds/wrld_123');
		await vi.waitFor(() => expect(worldNames.get('wrld_123')).toBe('MyWorld'));
	});

	test('a known name is not fetched again', async () => {
		get.mockResolvedValue({ name: 'Known' });
		worldNames.get('wrld_222');
		await vi.waitFor(() => expect(worldNames.get('wrld_222')).toBe('Known'));
		worldNames.get('wrld_222');
		expect(get).toHaveBeenCalledTimes(1);
	});

	test('a name the server has never seen is fetched through it from VRChat', async () => {
		get.mockRejectedValueOnce(new Error('not found')).mockResolvedValueOnce({ name: 'Fetched' });
		worldNames.get('wrld_789');
		await vi.waitFor(() => expect(worldNames.get('wrld_789')).toBe('Fetched'));
		expect(get).toHaveBeenLastCalledWith('/1/worlds/wrld_789', { refresh: true, account: 'usr_me' });
	});

	test('when nobody knows the id it stays without a name, and is not asked for again', async () => {
		get.mockRejectedValueOnce(new Error('not found')).mockResolvedValueOnce({});
		worldNames.get('wrld_111');
		await vi.waitFor(() => expect(get).toHaveBeenCalledTimes(2));
		await Promise.resolve();
		expect(worldNames.get('wrld_111')).toBeUndefined();
		expect(get).toHaveBeenCalledTimes(2);
	});

	test('user names come from displayName', async () => {
		get.mockResolvedValue({ displayName: 'Alice', status: 'online' });
		userNames.get('usr_1');
		expect(get).toHaveBeenCalledWith('/1/users/usr_1');
		await vi.waitFor(() => expect(userNames.get('usr_1')).toBe('Alice'));
	});
});
