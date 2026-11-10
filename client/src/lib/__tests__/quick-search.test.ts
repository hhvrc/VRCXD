import { beforeEach, describe, expect, test, vi } from 'vitest';

const auth = vi.hoisted(() => ({ ownerUserId: 'usr_me' }));
const get = vi.hoisted(() => vi.fn());

vi.mock('../api/vrchat-auth.svelte', () => {
	const vrchatAuth = auth;
	return { vrchatAuth, ownPath: (rest = '') => `/1/accounts/${encodeURIComponent(vrchatAuth.ownerUserId)}${rest}` };
});
vi.mock('../api/client', () => ({ api: { get }, accountPath: (account: string, rest = '') => `/1/accounts/${account}${rest}` }));

const { quickSearch } = await import('../nav/quick-search.svelte');
type QuickFriend = import('../nav/quick-search.svelte').QuickFriend;
type QuickItem = import('../nav/quick-search.svelte').QuickItem;

const friend = (id: string, name: string, memo = '', note = '') => ({ id, name, memo, note, imageUrl: null });
const item = (id: string, name: string, ownerId: string | null = null) => ({ id, name, ownerId, imageUrl: null });
type Index = { friends: QuickFriend[]; ownAvatars: QuickItem[]; favAvatars: QuickItem[]; ownWorlds: QuickItem[]; favWorlds: QuickItem[]; groups: QuickItem[] };
const emptyIndex: Index = { friends: [], ownAvatars: [], favAvatars: [], ownWorlds: [], favWorlds: [], groups: [] };

async function withIndex(index: Partial<Index>) {
	get.mockResolvedValueOnce({ ...emptyIndex, ...index });
	await quickSearch.load();
}

function search(query: string) {
	quickSearch.query = query;
	return quickSearch.results;
}

// The original's quickSearchUtils and quick search worker, over the server's index.
describe('quick search matching', () => {
	beforeEach(() => {
		auth.ownerUserId = 'usr_me';
		quickSearch.clear();
		get.mockReset();
	});

	test('names match as a substring, any case, with spaces in the query ignored', async () => {
		await withIndex({ friends: [friend('u1', 'HelloWorld'), friend('u2', 'Alice')] });
		expect(search('llo').friends.map((f) => f.id)).toEqual(['u1']);
		expect(search('alice').friends.map((f) => f.id)).toEqual(['u2']);
		expect(search('al ice').friends.map((f) => f.id)).toEqual(['u2']);
		expect(search('bob').friends).toEqual([]);
	});

	test('an empty or blank query matches nothing', async () => {
		await withIndex({ friends: [friend('u1', 'Alice')] });
		expect(search('').friends).toEqual([]);
		expect(search('   ').friends).toEqual([]);
	});

	test('friends match by name first, then by memo or note, and say which', async () => {
		await withIndex({ friends: [friend('u1', 'Alice'), friend('u2', 'Bob', 'coworker'), friend('u3', 'Charlie', '', 'roommate')] });
		expect(search('alice').friends).toMatchObject([{ id: 'u1', matchedField: 'name' }]);
		expect(search('coworker').friends).toMatchObject([{ id: 'u2', matchedField: 'memo' }]);
		expect(search('roommate').friends).toMatchObject([{ id: 'u3', matchedField: 'note', note: 'roommate' }]);
	});

	test('prefix matches come first and each list stops at ten', async () => {
		await withIndex({ friends: [friend('u1', 'XAliceX'), friend('u2', 'Alice'), ...Array.from({ length: 20 }, (_, i) => friend(`t${i}`, `Test${i}`))] });
		expect(search('Alice').friends.map((f) => f.id)).toEqual(['u2', 'u1']);
		expect(search('Test').friends).toHaveLength(10);
	});

	test('own groups are the ones the account owns, the rest are joined groups', async () => {
		await withIndex({ groups: [item('g1', 'My Group', 'usr_me'), item('g2', 'Other Group', 'usr_other'), item('g3', 'Another My Group', 'usr_me')] });
		const results = search('Group');
		expect(results.ownGroups.map((g) => g.id).sort()).toEqual(['g1', 'g3']);
		expect(results.joinedGroups.map((g) => g.id)).toEqual(['g2']);
	});

	test('avatars and worlds match by name in their own and favorite lists', async () => {
		await withIndex({
			ownAvatars: [item('a1', 'Cool Avatar', 'usr_me'), item('a3', 'Cool Suit', 'usr_me')],
			favAvatars: [item('fa1', 'Fav Avatar'), item('fa2', 'Cool Fav')],
			ownWorlds: [item('w1', 'Fun World', 'usr_me')],
			favWorlds: [item('fw1', 'Fav World'), item('fw2', 'Cool Place')]
		});
		const results = search('Cool');
		expect(results.ownAvatars.map((a) => a.id)).toEqual(['a1', 'a3']);
		expect(results.favAvatars.map((a) => a.id)).toEqual(['fa2']);
		expect(results.favWorlds.map((w) => w.id)).toEqual(['fw2']);
		expect(search('Fun').ownWorlds.map((w) => w.id)).toEqual(['w1']);
	});

	test('favorites and joined groups leave out what is already in the own lists', async () => {
		await withIndex({
			ownAvatars: [item('avtr_1', 'Alpha Avatar', 'usr_me')],
			favAvatars: [item('avtr_1', 'Alpha Avatar')],
			ownWorlds: [item('wrld_1', 'Alpha World', 'usr_me')],
			favWorlds: [item('wrld_1', 'Alpha World')],
			groups: [item('grp_1', 'Alpha Group', 'usr_me')]
		});
		const results = search('Alpha');
		expect([results.ownAvatars.length, results.favAvatars.length]).toEqual([1, 0]);
		expect([results.ownWorlds.length, results.favWorlds.length]).toEqual([1, 0]);
		expect([results.ownGroups.length, results.joinedGroups.length]).toEqual([1, 0]);
	});

	test('an index that arrives after the account changed is dropped', async () => {
		let resolve: (value: unknown) => void = () => {};
		get.mockReturnValueOnce(new Promise((r) => (resolve = r)));
		const loading = quickSearch.load();
		auth.ownerUserId = 'usr_other';
		resolve({ ...emptyIndex, friends: [friend('u1', 'Alice')] });
		await loading;
		expect(search('Alice').friends).toEqual([]);
	});

	test('clearing for another account forgets the index and the query', async () => {
		await withIndex({ friends: [friend('u1', 'Alice')] });
		search('Alice');
		quickSearch.clear();
		expect(quickSearch.query).toBe('');
		expect(search('Alice').friends).toEqual([]);
	});
});
