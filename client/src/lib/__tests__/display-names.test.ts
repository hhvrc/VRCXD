import { describe, expect, test, vi } from 'vitest';

const get = vi.hoisted(() => vi.fn());
vi.mock('#lib/api/client.js', () => ({ api: { get } }));

const { userNames } = await import('../people/names.svelte.js');

// The original's DisplayName: a user known only by id gets their name looked up once.
describe('user names', () => {
	test('an id without a name is fetched once and then shows the name', async () => {
		get.mockResolvedValueOnce({ displayName: 'Fetched User' });
		expect(userNames.get('usr_1')).toBeUndefined();
		expect(userNames.get('usr_1')).toBeUndefined();
		await vi.waitFor(() => expect(userNames.get('usr_1')).toBe('Fetched User'));
		expect(get).toHaveBeenCalledTimes(1);
		expect(get).toHaveBeenCalledWith('/1/users/usr_1');
	});

	test('a user the server has not seen is asked of VRChat through it', async () => {
		get.mockRejectedValueOnce(new Error('404')).mockResolvedValueOnce({ displayName: 'From VRChat' });
		userNames.get('usr_2');
		await vi.waitFor(() => expect(userNames.get('usr_2')).toBe('From VRChat'));
		expect(get).toHaveBeenLastCalledWith('/1/users/usr_2', expect.objectContaining({ refresh: true }));
	});

	test('no id asks nothing', () => {
		get.mockClear();
		expect(userNames.get(undefined)).toBeUndefined();
		expect(get).not.toHaveBeenCalled();
	});
});
