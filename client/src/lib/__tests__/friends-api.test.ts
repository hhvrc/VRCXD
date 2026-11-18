import { beforeEach, describe, expect, test, vi } from 'vitest';

const calls = vi.hoisted(() => [] as string[]);
vi.mock('../api/client.js', async (original) => ({
	...(await original<typeof import('../api/client.js')>()),
	api: {
		get: vi.fn(async (path: string) => (calls.push(`GET ${path}`), { friends: [] })),
		post: vi.fn(async (path: string) => (calls.push(`POST ${path}`), {})),
		delete: vi.fn(async (path: string) => (calls.push(`DELETE ${path}`), {}))
	}
}));
vi.mock('../api/vrchat-auth.svelte.js', () => ({ ownPath: (rest = '') => `/1/accounts/usr_me${rest}` }));

const friends = await import('../people/friends-api.js');

beforeEach(() => {
	calls.length = 0;
});

// The routes are the server's contract (FriendsController and the accept and decline in UserActions).
describe('friends requests', () => {
	test('each action asks the route the server serves it on', async () => {
		await friends.loadFriendDetails('usr_other');
		await friends.loadFriendProfile('usr_a');
		await friends.unfriend('usr_a');
		await friends.sendFriendRequest('usr_a');
		await friends.cancelFriendRequest('usr_a');
		await friends.acceptFriendRequest('usr_a');
		await friends.declineFriendRequest('usr_a');
		await friends.hideFriendLogEntry('Unfriend:12');

		expect(calls).toEqual([
			'GET /1/accounts/usr_other/friends/details',
			'POST /1/accounts/usr_me/friends/usr_a/load',
			'DELETE /1/accounts/usr_me/friends/usr_a',
			'POST /1/accounts/usr_me/friend-requests/usr_a',
			'DELETE /1/accounts/usr_me/friend-requests/usr_a',
			'POST /1/accounts/usr_me/friend-requests/usr_a/accept',
			'POST /1/accounts/usr_me/friend-requests/usr_a/decline',
			'DELETE /1/accounts/usr_me/friend-log/Unfriend%3A12'
		]);
	});

	test('ids are escaped, so one with a slash cannot reach another route', async () => {
		await friends.unfriend('usr_a/load');
		expect(calls).toEqual(['DELETE /1/accounts/usr_me/friends/usr_a%2Fload']);
	});
});
