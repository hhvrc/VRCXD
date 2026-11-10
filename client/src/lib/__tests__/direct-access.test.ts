import { beforeEach, describe, expect, test, vi } from 'vitest';

const dialog = vi.hoisted(() => ({ open: vi.fn(), openAvatar: vi.fn(), openGroup: vi.fn(), openWorld: vi.fn() }));
const get = vi.hoisted(() => vi.fn());

vi.mock('../people/user-dialog.svelte', () => ({ userDialog: dialog }));
vi.mock('../api/vrchat-auth.svelte', () => {
	const vrchatAuth = { ownerUserId: 'usr_me' };
	return { vrchatAuth, ownPath: (rest = '') => `/1/accounts/${encodeURIComponent(vrchatAuth.ownerUserId)}${rest}` };
});
vi.mock('svelte-sonner', () => ({ toast: { success: vi.fn(), error: vi.fn() } }));
vi.mock('../api/client', () => ({ api: { get }, accountPath: (account: string, rest = '') => `/1/accounts/${account}${rest}` }));

const { directAccessParse } = await import('../nav/direct-access.svelte');

// Lets the short name and short code lookups finish.
const settle = () => new Promise((resolve) => setTimeout(resolve, 0));

// The original's search store directAccessParse and directAccessWorld.
describe('direct access to users, avatars and groups', () => {
	beforeEach(() => vi.clearAllMocks());

	test('empty or unrecognised input opens nothing', () => {
		expect(directAccessParse('')).toBe(false);
		expect(directAccessParse('hello world')).toBe(false);
		expect(directAccessParse('https://vrchat.com/home')).toBe(false);
	});

	test('ids open their pop-up by prefix, and a 10 character id is a user', () => {
		directAccessParse('usr_abc123');
		directAccessParse('Ab3dEf7h9J');
		directAccessParse('avtr_abc123');
		directAccessParse('b_something');
		directAccessParse('grp_abc123');
		expect(dialog.open.mock.calls).toEqual([['usr_abc123'], ['Ab3dEf7h9J']]);
		expect(dialog.openAvatar.mock.calls).toEqual([['avtr_abc123'], ['b_something']]);
		expect(dialog.openGroup).toHaveBeenCalledWith('grp_abc123');
	});

	test('vrchat.com user, avatar and group links open their pop-up', () => {
		directAccessParse('https://vrchat.com/home/user/usr_abc123');
		directAccessParse('https://vrchat.com/home/avatar/avtr_abc123');
		directAccessParse('https://vrchat.com/home/group/grp_abc123');
		expect(dialog.open).toHaveBeenCalledWith('usr_abc123');
		expect(dialog.openAvatar).toHaveBeenCalledWith('avtr_abc123');
		expect(dialog.openGroup).toHaveBeenCalledWith('grp_abc123');
	});

	test('a vrc.group link or a group short code is looked up by strict search', async () => {
		get.mockResolvedValue([{ id: 'grp_found', shortCode: 'ABCD', discriminator: '1234' }]);
		expect(directAccessParse('https://vrc.group/ABC.1234')).toBe(true);
		expect(directAccessParse('ABCD.1234')).toBe(true);
		await settle();
		expect(get).toHaveBeenCalledWith('/1/accounts/usr_me/search/groups/strict', { query: 'ABC.1234' });
		expect(get).toHaveBeenCalledWith('/1/accounts/usr_me/search/groups/strict', { query: 'ABCD.1234' });
		expect(dialog.openGroup.mock.calls).toEqual([['grp_found']]);
	});
});

describe('direct access to worlds and instances', () => {
	beforeEach(() => vi.clearAllMocks());

	test('world ids open the world pop-up as they are', () => {
		directAccessParse('wrld_abc123:12345~friends');
		directAccessParse('wld_abc');
		directAccessParse('o_test123');
		expect(dialog.openWorld.mock.calls).toEqual([['wrld_abc123:12345~friends'], ['wld_abc'], ['o_test123']]);
	});

	test('a world id with &instanceId= is read as a launch link', () => {
		directAccessParse('wrld_abc&instanceId=123');
		expect(dialog.openWorld).toHaveBeenCalledWith('wrld_abc:123');
	});

	test('an 8 character short name and a vrch.at link resolve through the server', async () => {
		get.mockResolvedValue({ location: 'wrld_abc:123' });
		directAccessParse('AbCdEfGh');
		directAccessParse('https://vrch.at/XyZ12345');
		await settle();
		expect(get).toHaveBeenCalledWith('/1/accounts/usr_me/search/short-name/AbCdEfGh');
		expect(get).toHaveBeenCalledWith('/1/accounts/usr_me/search/short-name/XyZ12345');
		expect(dialog.openWorld.mock.calls).toEqual([['wrld_abc:123'], ['wrld_abc:123']]);
	});

	test('world pages and launch links open the world or the instance', () => {
		directAccessParse('https://vrchat.com/home/world/wrld_abc123');
		directAccessParse('https://vrchat.com/home/launch?worldId=wrld_abc');
		directAccessParse('https://vrchat.com/home/launch?worldId=wrld_abc&instanceId=123');
		directAccessParse('/home/world/wrld_relpath');
		expect(dialog.openWorld.mock.calls).toEqual([['wrld_abc123'], ['wrld_abc'], ['wrld_abc:123'], ['wrld_relpath']]);
	});

	test('a short name the lookup confirms is passed on to the world pop-up', async () => {
		get.mockResolvedValue({ location: 'wrld_abc:123~hidden(usr_x)', shortName: 'AbCdEfGh' });
		directAccessParse('https://vrch.at/AbCdEfGh');
		await settle();
		expect(dialog.openWorld.mock.calls).toEqual([['wrld_abc:123~hidden(usr_x)', 'AbCdEfGh']]);
	});

	test('a failed short name lookup falls back to the launch link location, without the short name', async () => {
		get.mockRejectedValue(new Error('not found'));
		directAccessParse('https://vrchat.com/home/launch?worldId=wrld_abc&instanceId=123&shortName=myShort1');
		await settle();
		expect(dialog.openWorld.mock.calls).toEqual([['wrld_abc:123']]);
	});

	test('a launch link with a short name opens what the short name resolves to', async () => {
		get.mockResolvedValue({ location: 'wrld_abc:123~hidden' });
		directAccessParse('https://vrchat.com/home/launch?worldId=wrld_abc&instanceId=123&shortName=myShort1');
		await settle();
		expect(get).toHaveBeenCalledWith('/1/accounts/usr_me/search/short-name/myShort1');
		expect(dialog.openWorld).toHaveBeenCalledWith('wrld_abc:123~hidden');
	});
});
