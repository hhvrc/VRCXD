import { beforeEach, describe, expect, test, vi } from 'vitest';

const get = vi.hoisted(() => vi.fn());
vi.mock('../api/client.js', async (original) => ({ ...(await original<typeof import('../api/client.js')>()), api: { get, post: vi.fn() } }));

import { disabledContent } from '../components/instance/instance-info.svelte';
import { buildLegacyInstanceTag, canInvite, canInviteSelf, closedInstances, gameLaunchLink } from '../instances/instance-launch.svelte.js';

beforeEach(() => {
	get.mockReset();
	closedInstances.clear();
});

describe('buildLegacyInstanceTag', () => {
	const base = { instanceName: '12345', userId: 'usr_test', accessType: 'public', region: 'US West' };

	test('public instances carry only the region', () => {
		expect(buildLegacyInstanceTag(base)).toBe('12345~region(us)');
		expect(buildLegacyInstanceTag({ ...base, region: 'US East' })).toBe('12345~region(use)');
		expect(buildLegacyInstanceTag({ ...base, region: 'Europe' })).toBe('12345~region(eu)');
		expect(buildLegacyInstanceTag({ ...base, region: 'Japan' })).toBe('12345~region(jp)');
	});

	test('friends+ is hidden and friends is friends', () => {
		expect(buildLegacyInstanceTag({ ...base, accessType: 'friends+' })).toBe('12345~hidden(usr_test)~region(us)');
		expect(buildLegacyInstanceTag({ ...base, accessType: 'friends' })).toBe('12345~friends(usr_test)~region(us)');
	});

	test('invite is private, and invite+ adds canRequestInvite', () => {
		expect(buildLegacyInstanceTag({ ...base, accessType: 'invite+' })).toBe('12345~private(usr_test)~canRequestInvite~region(us)');
		expect(buildLegacyInstanceTag({ ...base, accessType: 'invite' })).toBe('12345~private(usr_test)~region(us)');
	});

	test('group instances carry the group and its access type, and ageGate only there', () => {
		expect(buildLegacyInstanceTag({ ...base, accessType: 'group', groupId: 'grp_abc', groupAccessType: 'plus' })).toBe('12345~group(grp_abc)~groupAccessType(plus)~region(us)');
		expect(buildLegacyInstanceTag({ ...base, accessType: 'group', groupId: 'grp_abc', groupAccessType: 'members', ageGate: true })).toBe('12345~group(grp_abc)~groupAccessType(members)~ageGate~region(us)');
		expect(buildLegacyInstanceTag({ ...base, ageGate: true })).toBe('12345~region(us)');
	});

	test('strict is added for invite and friends only', () => {
		expect(buildLegacyInstanceTag({ ...base, accessType: 'invite', strict: true })).toBe('12345~private(usr_test)~region(us)~strict');
		expect(buildLegacyInstanceTag({ ...base, accessType: 'friends', strict: true })).toBe('12345~friends(usr_test)~region(us)~strict');
		expect(buildLegacyInstanceTag({ ...base, strict: true })).toBe('12345~region(us)');
		expect(buildLegacyInstanceTag({ ...base, accessType: 'friends+', strict: true })).toBe('12345~hidden(usr_test)~region(us)');
	});

	test('an empty name or an unknown region adds nothing', () => {
		expect(buildLegacyInstanceTag({ ...base, instanceName: '' })).toBe('~region(us)');
		expect(buildLegacyInstanceTag({ ...base, region: 'Mars' })).toBe('12345');
	});
});

describe('canInvite', () => {
	const me = 'usr_me';
	const here = 'wrld_last:12345';

	test('nothing to invite to without a location', () => {
		expect(canInvite('', me, here)).toBe(false);
		expect(canInvite(null as unknown as string, me, here)).toBe(false);
	});

	test('public, group and own instances can be invited to', () => {
		expect(canInvite('wrld_123:instance', me, here)).toBe(true);
		expect(canInvite('wrld_123:instance~group(grp_123)~groupAccessType(public)', me, here)).toBe(true);
		expect(canInvite('wrld_123:instance~private(usr_me)', me, here)).toBe(true);
	});

	test('invite or friends instances of someone else cannot', () => {
		expect(canInvite('wrld_123:instance~private(usr_other)', me, here)).toBe(false);
		expect(canInvite('wrld_123:instance~friends(usr_other)', me, here)).toBe(false);
	});

	test('a friends+ instance only when you are in it', () => {
		const location = 'wrld_123:instance~hidden(usr_other)';
		expect(canInvite(location, me, location)).toBe(true);
		expect(canInvite(location, me, here)).toBe(false);
	});

	test('a closed instance cannot', () => {
		closedInstances.set('wrld_123:instance', '2024-01-01');
		expect(canInvite('wrld_123:instance', me, here)).toBe(false);
	});
});

describe('canInviteSelf', () => {
	const me = 'usr_me';

	test('nothing to join without a location', () => {
		expect(canInviteSelf('', me, new Set())).toBe(false);
		expect(canInviteSelf(null as unknown as string, me, new Set())).toBe(false);
	});

	test('your own and public instances', () => {
		expect(canInviteSelf('wrld_123:instance~private(usr_me)', me, new Set())).toBe(true);
		expect(canInviteSelf('wrld_123:instance', me, new Set())).toBe(true);
	});

	test('a friends instance only when its owner is a friend', () => {
		expect(canInviteSelf('wrld_123:instance~friends(usr_owner)', me, new Set(['usr_owner']))).toBe(true);
		expect(canInviteSelf('wrld_123:instance~friends(usr_other)', me, new Set())).toBe(false);
	});

	test('a closed instance cannot be joined', () => {
		closedInstances.set('wrld_123:instance', '2024-01-01');
		expect(canInviteSelf('wrld_123:instance', me, new Set())).toBe(false);
	});

	test('an open invite instance of someone else can', () => {
		expect(canInviteSelf('wrld_123:instance~private(usr_other)', me, new Set())).toBe(true);
	});
});

describe('gameLaunchLink', () => {
	const friends = 'wrld_123:456~friends(usr_abc)';

	test('a given short name is used for a non-public instance without asking', async () => {
		expect(await gameLaunchLink(friends, 'myShort')).toBe('vrchat://launch?ref=vrcx.app&id=wrld_123:456~friends(usr_abc)&shortName=myShort');
		expect(get).not.toHaveBeenCalled();
	});

	test('otherwise the short name is fetched', async () => {
		get.mockResolvedValue({ shortName: 'fetched123' });
		expect(await gameLaunchLink(friends, '')).toContain('shortName=fetched123');
		expect(get).toHaveBeenCalled();
	});

	test('the secure name stands in for an empty short name', async () => {
		get.mockResolvedValue({ shortName: '', secureName: 'secure456' });
		expect(await gameLaunchLink(friends, '')).toContain('shortName=secure456');
	});

	test('no short name when VRChat has none', async () => {
		get.mockResolvedValue({});
		expect(await gameLaunchLink(friends, '')).toBe('vrchat://launch?ref=vrcx.app&id=wrld_123:456~friends(usr_abc)');
		get.mockRejectedValue(new Error('not found'));
		expect(await gameLaunchLink(friends, '')).toBe('vrchat://launch?ref=vrcx.app&id=wrld_123:456~friends(usr_abc)');
	});

	test('public instances are asked about too', async () => {
		get.mockResolvedValue({});
		await gameLaunchLink('wrld_123:456', '');
		expect(get).toHaveBeenCalled();
	});
});

describe('disabledContent', () => {
	test('nothing for missing or empty settings', () => {
		expect(disabledContent(undefined)).toEqual([]);
		expect(disabledContent({})).toEqual([]);
	});

	test('the settings turned off, in the original order', () => {
		expect(disabledContent({ stickers: false, emoji: false, props: true })).toEqual(['emoji', 'stickers']);
	});

	test('settings the instance does not mention are not disabled', () => {
		expect(disabledContent({ emoji: true })).toEqual([]);
	});
});
