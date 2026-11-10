import { afterEach, beforeEach, describe, expect, test, vi } from 'vitest';

const get = vi.hoisted(() => vi.fn());
const post = vi.hoisted(() => vi.fn());
vi.mock('#lib/api/client.js', async (actual) => ({ ...(await actual<typeof import('#lib/api/client.js')>()), api: { get, post, put: vi.fn(), delete: vi.fn() } }));
vi.mock('#lib/api/vrchat-auth.svelte.js', () => {
	const vrchatAuth = { ownerUserId: 'usr_me' };
	return { vrchatAuth, ownPath: (rest = '') => `/1/accounts/${encodeURIComponent(vrchatAuth.ownerUserId)}${rest}` };
});
vi.mock('#lib/people/user-dialog.svelte.js', () => ({ userDialog: { setLabel: vi.fn(), isOpen: false, openGroupModeration: vi.fn() } }));
const toast = vi.hoisted(() => ({ success: vi.fn(), error: vi.fn() }));
vi.mock('svelte-sonner', () => ({ toast }));

import { auditLogTypeName, FILTER_EVERYONE, groupModeration as M, hasPermission, resolveRoleNames, SORTING_OPTIONS, type ModMember } from '../groups/group-moderation.svelte.js';

const base = '/1/accounts/usr_me/groups/grp_1';
const member = (userId: string, displayName = userId): ModMember => ({ userId, user: { id: userId, displayName } });
const page = (prefix: string, count: number) => Array.from({ length: count }, (_, i) => member(`usr_${prefix}${i}`, `${prefix} ${i}`));
const ALICE = 'usr_11111111-2222-3333-4444-555555555555';
const BOB = 'usr_aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee';

/** Answers api.get by path with a function of the query. */
function answer(routes: Record<string, (query: Record<string, unknown>) => unknown>) {
	get.mockImplementation(async (path: string, query: Record<string, unknown> = {}) => {
		const route = routes[path];
		if (!route) throw new Error(`unexpected GET ${path}`);
		return route(query);
	});
}

beforeEach(() => {
	get.mockReset();
	post.mockReset();
	toast.error.mockReset();
	M.id = 'grp_1';
	M.visible = true;
	M.loading = false;
	M.reset();
	M.clearSelection();
	M.memberSortOrder = SORTING_OPTIONS[0];
	M.memberFilter = FILTER_EVERYONE;
});

afterEach(() => {
	vi.useRealTimers();
});

describe('auditLogTypeName', () => {
	test('converts a dotted audit log type to title case', () => {
		expect(auditLogTypeName('group.member.ban')).toBe('Member Ban');
	});

	test('a single segment after the group prefix is one word', () => {
		expect(auditLogTypeName('group.update')).toBe('Update');
	});

	test('several segments become several words', () => {
		expect(auditLogTypeName('group.role.member.add')).toBe('Role Member Add');
	});

	test('an empty or missing type is an empty name', () => {
		expect(auditLogTypeName('')).toBe('');
		expect(auditLogTypeName(null as unknown as string)).toBe('');
		expect(auditLogTypeName(undefined as unknown as string)).toBe('');
	});
});

describe('resolveRoleNames', () => {
	const roles = [
		{ id: 'role_1', name: 'Admin' },
		{ id: 'role_2', name: 'Moderator' },
		{ id: 'role_3', name: 'Member' }
	];

	test('role ids become comma separated names', () => {
		expect(resolveRoleNames(['role_1', 'role_3'], roles)).toBe('Admin, Member');
	});

	test('no role ids is an empty string', () => {
		expect(resolveRoleNames([], roles)).toBe('');
	});

	test('unknown role ids are skipped', () => {
		expect(resolveRoleNames(['role_1', 'role_unknown'], roles)).toBe('Admin');
	});

	test('missing role ids are an empty string', () => {
		expect(resolveRoleNames(null, roles)).toBe('');
		expect(resolveRoleNames(undefined, roles)).toBe('');
	});

	test('missing roles are an empty string', () => {
		expect(resolveRoleNames(['role_1'], null)).toBe('');
	});

	test('one role is its name without a comma', () => {
		expect(resolveRoleNames(['role_2'], roles)).toBe('Moderator');
	});
});

describe('group moderation tables', () => {
	test('bans fill the table', async () => {
		answer({ [`${base}/bans`]: () => [member('usr_a'), member('usr_b')] });
		await M.getAllGroupBans();
		expect(M.bans.map((b) => b.userId)).toEqual(['usr_a', 'usr_b']);
		expect(M.loading).toBe(false);
	});

	test('bans page through 100 at a time until a short page', async () => {
		answer({ [`${base}/bans`]: (q) => (q.offset === 0 ? page('a', 100) : page('b', 3)) });
		await M.getAllGroupBans();
		expect(M.bans).toHaveLength(103);
		expect(get.mock.calls.map((c) => c[1].offset)).toEqual([0, 100]);
	});

	test('bans of a group the tool has since left are dropped', async () => {
		answer({
			[`${base}/bans`]: () => {
				M.id = 'grp_2';
				return [member('usr_a')];
			}
		});
		await M.getAllGroupBans('grp_1');
		expect(M.bans).toEqual([]);
	});

	test('a failed bans read leaves the table empty and says so', async () => {
		get.mockRejectedValue(new Error('down'));
		await M.getAllGroupBans();
		expect(M.bans).toEqual([]);
		expect(toast.error).toHaveBeenCalledWith('Failed to get group bans');
		expect(M.loading).toBe(false);
	});

	test('bans stop paging when the tool is closed', async () => {
		answer({
			[`${base}/bans`]: () => {
				M.visible = false;
				return page('a', 100);
			}
		});
		await M.getAllGroupBans();
		expect(get).toHaveBeenCalledTimes(1);
		expect(M.bans).toHaveLength(100);
	});

	test('logs fill the table once per entry', async () => {
		answer({
			[`${base}/audit-logs`]: (q) =>
				q.offset === 0 ? { results: [{ id: 'log_1' }, { id: 'log_2' }], hasNext: true } : { results: [{ id: 'log_2' }, { id: 'log_3' }], hasNext: false }
		});
		await M.getAllGroupLogs();
		expect(M.logs.map((l) => l.id)).toEqual(['log_1', 'log_2', 'log_3']);
	});

	test('logs ask for the chosen event types', async () => {
		answer({ [`${base}/audit-logs`]: () => ({ results: [], hasNext: false }) });
		await M.getAllGroupLogs('grp_1', ['group.member.ban', 'group.member.kick']);
		expect(get.mock.calls[0][1].eventTypes).toBe('group.member.ban,group.member.kick');
		await M.getAllGroupLogs('grp_1');
		expect(get.mock.calls[1][1].eventTypes).toBeUndefined();
	});

	test('invites, join requests and blocked requests load side by side', async () => {
		let pending = 0;
		let most = 0;
		const slow = (rows: ModMember[]) => async () => {
			pending++;
			most = Math.max(most, pending);
			await Promise.resolve();
			pending--;
			return rows;
		};
		answer({
			[`${base}/invites`]: slow([member('usr_invited')]),
			[`${base}/requests`]: (q) => slow([member(q.blocked ? 'usr_blocked' : 'usr_asking')])()
		});
		await M.getAllGroupInvitesAndJoinRequests();
		expect(most).toBe(3);
		expect(M.invites.map((r) => r.userId)).toEqual(['usr_invited']);
		expect(M.joinRequests.map((r) => r.userId)).toEqual(['usr_asking']);
		expect(M.blocked.map((r) => r.userId)).toEqual(['usr_blocked']);
	});
});

describe('group moderation selection', () => {
	test('every usr_ id in the input is selected', async () => {
		answer({ [`${base}/members/${ALICE}`]: () => member(ALICE, 'Alice'), [`${base}/members/${BOB}`]: () => member(BOB, 'Bob') });
		await M.selectUserIds(`${ALICE}, and ${BOB}`);
		expect([...M.selected.keys()].sort()).toEqual([ALICE, BOB].sort());
	});

	test('input without a usr_ id is looked up as it is', async () => {
		answer({ [`${base}/members/someone`]: () => member('usr_someone', 'Someone') });
		await M.selectUserIds('someone');
		expect([...M.selected.keys()]).toEqual(['usr_someone']);
	});

	test('empty input selects nobody', async () => {
		await M.selectUserIds('');
		expect(get).not.toHaveBeenCalled();
		expect(M.selected.size).toBe(0);
	});

	test('a member with a user object is selected as the group returned it', async () => {
		answer({ [`${base}/members/${ALICE}`]: () => ({ ...member(ALICE, 'Alice'), roleIds: ['role_1'] }) });
		await M.addToSelection(ALICE);
		expect(M.selected.get(ALICE)?.roleIds).toEqual(['role_1']);
		expect(get).toHaveBeenCalledTimes(1);
	});

	test('a member without a user object is completed from the user lookup', async () => {
		answer({
			[`${base}/members/${ALICE}`]: () => ({ userId: ALICE, roleIds: ['role_1'] }),
			[`/1/users/${ALICE}`]: () => ({ id: ALICE, displayName: 'Alice' })
		});
		await M.addToSelection(ALICE);
		expect(M.selected.get(ALICE)).toMatchObject({ userId: ALICE, roleIds: ['role_1'], user: { displayName: 'Alice' } });
	});

	test('reset clears every table and the member search', () => {
		M.members = [member('usr_a')];
		M.bans = [member('usr_b')];
		M.invites = [member('usr_c')];
		M.joinRequests = [member('usr_d')];
		M.blocked = [member('usr_e')];
		M.logs = [{ id: 'log_1' }];
		M.memberSearch = 'abc';
		M.reset();
		expect([M.members, M.bans, M.invites, M.joinRequests, M.blocked, M.logs]).toEqual([[], [], [], [], [], []]);
		expect(M.memberSearch).toBe('');
	});
});

describe('group moderation member search', () => {
	test('a search under three characters clears the results without asking', () => {
		vi.useFakeTimers();
		M.members = [member('usr_a')];
		M.memberSearch = 'ab';
		M.search();
		vi.advanceTimersByTime(1000);
		expect(M.members).toEqual([]);
		expect(get).not.toHaveBeenCalled();
	});

	test('rapid typing asks once with the latest query', async () => {
		vi.useFakeTimers();
		answer({ [`${base}/members/search`]: (q) => ({ results: [member('usr_found', String(q.query))] }) });
		for (const text of ['ali', 'alic', 'alice']) {
			M.memberSearch = text;
			M.search();
			await vi.advanceTimersByTimeAsync(50);
		}
		await vi.advanceTimersByTimeAsync(200);
		expect(get).toHaveBeenCalledTimes(1);
		expect(get.mock.calls[0][1].query).toBe('alice');
		expect(M.members.map((r) => r.user?.displayName)).toEqual(['alice']);
	});

	test('a response for an older query is ignored', async () => {
		vi.useFakeTimers();
		const replies: Array<(value: unknown) => void> = [];
		get.mockImplementation(() => new Promise((resolve) => replies.push(resolve)));
		M.memberSearch = 'old';
		M.search();
		await vi.advanceTimersByTimeAsync(200);
		M.memberSearch = 'new';
		M.search();
		await vi.advanceTimersByTimeAsync(200);
		replies[1]({ results: [member('usr_new', 'new')] });
		await vi.advanceTimersByTimeAsync(0);
		replies[0]({ results: [member('usr_old', 'old')] });
		await vi.advanceTimersByTimeAsync(0);
		expect(M.members.map((r) => r.userId)).toEqual(['usr_new']);
	});

	test('with no matches the remaining member pages are loaded and matched', async () => {
		vi.useFakeTimers();
		answer({
			[`${base}/members/usr_me`]: () => undefined,
			[`${base}/members`]: (q) => (q.offset === 0 ? page('early', 100) : [member('usr_zed', 'Zed Late')]),
			[`${base}/members/search`]: () => ({ results: [] })
		});
		await M.getGroupMembers();
		M.memberSearch = 'zed';
		M.search();
		await vi.advanceTimersByTimeAsync(200);
		expect(M.members).toEqual([]);
		await vi.advanceTimersByTimeAsync(1000);
		expect(M.members.map((r) => r.userId)).toEqual(['usr_zed']);
	});

	test('choosing the same sort again reloads nothing', async () => {
		await M.setSortOrder({ ...SORTING_OPTIONS[0] });
		expect(get).not.toHaveBeenCalled();
	});

	test('choosing the same filter again reloads nothing', async () => {
		await M.setFilter(FILTER_EVERYONE);
		expect(get).not.toHaveBeenCalled();
	});

	test('loading all members while already loading does nothing', async () => {
		M.loading = true;
		await M.loadAllGroupMembers();
		expect(get).not.toHaveBeenCalled();
	});
});

describe('group moderation bulk actions', () => {
	test('a bulk action counts its progress and a cancel stops it before the next user', async () => {
		answer({ [`${base}/bans`]: () => [] });
		for (const id of ['usr_a', 'usr_b', 'usr_c']) M.toggle(member(id), true);
		const seen: Array<[number, number]> = [];
		post.mockImplementation(async () => {
			seen.push([M.progressCurrent, M.progressTotal]);
			M.cancelProgress();
		});
		await M.ban();
		expect(seen).toEqual([[1, 3]]);
		expect(post).toHaveBeenCalledTimes(1);
		expect([M.progressCurrent, M.progressTotal]).toEqual([0, 0]);
		expect(M.selected.size).toBe(0);
	});

	test('kick and ban each need their own group permission, and the star grants every one', () => {
		M.groupRef = { id: 'grp_1', name: 'Group', myMember: { permissions: ['group-members-remove'] } };
		expect(M.can('group-members-remove')).toBe(true);
		expect(M.can('group-bans-manage')).toBe(false);
		expect(hasPermission(['*'], 'group-bans-manage')).toBe(true);
		expect(hasPermission(null, 'group-members-remove')).toBe(false);
	});
});
