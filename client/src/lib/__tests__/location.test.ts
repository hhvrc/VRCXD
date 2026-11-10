import { describe, expect, test } from 'vitest';
import { accessTypeLabel, locationText, parseLocation, resolveRegion } from '../instances/location.js';
import { displayLocation } from '../notifications/noty.js';

// The original's locationParser.js and location.js tests, against the port's location.ts. The
// original kept privateId, friendsId and hiddenId apart; the port folds them into userId.

describe('parseLocation', () => {
	test('reads the sentinels and their doubled variants', () => {
		for (const tag of ['offline', 'offline:offline']) expect(parseLocation(tag)).toMatchObject({ isOffline: true, isPrivate: false, isTraveling: false, worldId: '' });
		for (const tag of ['private', 'private:private']) expect(parseLocation(tag)).toMatchObject({ isOffline: false, isPrivate: true, isTraveling: false });
		for (const tag of ['traveling', 'traveling:traveling']) expect(parseLocation(tag)).toMatchObject({ isTraveling: true, isRealInstance: false });
	});

	test('a world id alone is a real location with no instance and no access type', () => {
		const worldId = 'wrld_12345678-1234-1234-1234-123456789012';
		expect(parseLocation(worldId)).toMatchObject({ worldId, instanceId: '', isRealInstance: true, isOffline: false, isPrivate: false, isTraveling: false, accessType: '' });
	});

	test('a bare instance is public', () => {
		expect(parseLocation('wrld_12345:67890')).toMatchObject({ worldId: 'wrld_12345', instanceId: '67890', instanceName: '67890', accessType: 'public', isRealInstance: true });
	});

	test('the region is read and the instance name is the part before the first tilde', () => {
		expect(parseLocation('wrld_12345:67890~region(us)')).toMatchObject({ worldId: 'wrld_12345', instanceId: '67890~region(us)', instanceName: '67890', region: 'us', accessType: 'public' });
	});

	test('private is invite, with canRequestInvite it is invite+', () => {
		expect(parseLocation('wrld_12345:instance~private(usr_12345)')).toMatchObject({ worldId: 'wrld_12345', accessType: 'invite', userId: 'usr_12345', canRequestInvite: false });
		expect(parseLocation('wrld_12345:instance~private(usr_12345)~canRequestInvite')).toMatchObject({ accessType: 'invite+', userId: 'usr_12345', canRequestInvite: true });
	});

	test('friends is friends and hidden is friends+, both with their owner', () => {
		expect(parseLocation('wrld_abc:12345~friends(usr_owner)~region(eu)')).toMatchObject({ accessType: 'friends', userId: 'usr_owner', region: 'eu' });
		expect(parseLocation('wrld_abc:12345~hidden(usr_owner)')).toMatchObject({ accessType: 'friends+', userId: 'usr_owner' });
	});

	test('group instances name their group access type', () => {
		expect(parseLocation('wrld_12345:instance~group(grp_12345)')).toMatchObject({ accessType: 'group', groupId: 'grp_12345', accessTypeName: 'group' });
		expect(parseLocation('wrld_12345:instance~group(grp_12345)~groupAccessType(public)')).toMatchObject({ accessType: 'group', groupId: 'grp_12345', groupAccessType: 'public', accessTypeName: 'groupPublic' });
		expect(parseLocation('wrld_abc:12345~group(grp_xyz)~groupAccessType(plus)').accessTypeName).toBe('groupPlus');
	});

	test('strict and ageGate are flags', () => {
		expect(parseLocation('wrld_12345:instance~strict').strict).toBe(true);
		expect(parseLocation('wrld_12345:instance~ageGate').ageGate).toBe(true);
		expect(parseLocation('wrld_abc:12345~strict~ageGate')).toMatchObject({ strict: true, ageGate: true });
	});

	test('a tag with every part reads them all', () => {
		expect(parseLocation('wrld_12345:67890~region(eu)~private(usr_abc)~canRequestInvite~strict~ageGate')).toMatchObject({
			worldId: 'wrld_12345',
			instanceName: '67890',
			region: 'eu',
			accessType: 'invite+',
			userId: 'usr_abc',
			canRequestInvite: true,
			strict: true,
			ageGate: true
		});
	});

	test('a short name after the tag is split off', () => {
		expect(parseLocation('wrld_12345:67890&shortName=TestInstance')).toMatchObject({ worldId: 'wrld_12345', instanceId: '67890', shortName: 'TestInstance' });
		expect(parseLocation('wrld_abc:12345~friends(usr_a)&shortName=myShort')).toMatchObject({ shortName: 'myShort', accessType: 'friends' });
	});

	test('empty input is nothing at all', () => {
		for (const input of [null, undefined, '']) {
			expect(parseLocation(input)).toMatchObject({ tag: '', worldId: '', isOffline: false, isPrivate: false, isTraveling: false, isRealInstance: false });
		}
	});

	test('local instances are not real', () => {
		expect(parseLocation('local:12345')).toMatchObject({ isRealInstance: false, worldId: '' });
	});

	test('malformed parameters read as empty', () => {
		const L = parseLocation('wrld_12345:instance~private()~region~invalid(test');
		expect(L.worldId).toBe('wrld_12345');
		expect(L.userId).toBe('');
		expect(L.region).toBe('');
	});

	test('the original tag is kept', () => {
		expect(parseLocation('wrld_12345:instance~region(us)').tag).toBe('wrld_12345:instance~region(us)');
	});

	test('every known access type', () => {
		const cases: [string, string][] = [
			['wrld_test:public', 'public'],
			['wrld_test:private~private(usr_123)', 'invite'],
			['wrld_test:private~private(usr_123)~canRequestInvite', 'invite+'],
			['wrld_test:friends~friends(usr_123)', 'friends'],
			['wrld_test:hidden~hidden(usr_123)', 'friends+'],
			['wrld_test:group~group(grp_123)', 'group']
		];
		for (const [tag, accessType] of cases) expect(parseLocation(tag).accessType).toBe(accessType);
	});

	test('the order of the parts does not matter', () => {
		const a = parseLocation('wrld_12345:instance~region(us)~private(usr_123)~strict');
		const b = parseLocation('wrld_12345:instance~strict~private(usr_123)~region(us)');
		expect([a.region, a.userId, a.strict, a.accessType]).toEqual([b.region, b.userId, b.strict, b.accessType]);
	});

	test('very long world ids are kept whole', () => {
		const worldId = 'wrld_' + 'a'.repeat(100);
		expect(parseLocation(worldId)).toMatchObject({ worldId, isRealInstance: true });
	});

	test('corrupted tags do not throw', () => {
		for (const tag of ['wrld_12345:', 'wrld_12345:~', 'wrld_12345:~~~', 'wrld_12345:instance~(', 'wrld_12345:instance~)(', 'wrld_12345:instance~region((nested))']) {
			const L = parseLocation(tag);
			expect(L.worldId).toBe('wrld_12345');
		}
	});
});

describe('displayLocation', () => {
	test('the sentinels read Offline, Private and Traveling', () => {
		for (const tag of ['offline', 'offline:offline']) expect(displayLocation(tag, 'Some World')).toBe('Offline');
		for (const tag of ['private', 'private:private']) expect(displayLocation(tag, 'Some World')).toBe('Private');
		for (const tag of ['traveling', 'traveling:traveling']) expect(displayLocation(tag, 'Some World')).toBe('Traveling');
	});

	test('a world alone is its name', () => {
		expect(displayLocation('wrld_12345', 'Test World')).toBe('Test World');
	});

	test('an instance adds its access type', () => {
		const world = 'Test World';
		expect(displayLocation('wrld_12345:instance', world)).toBe('Test World public');
		expect(displayLocation('wrld_12345:instance~private(usr_123)', world)).toBe('Test World invite');
		expect(displayLocation('wrld_12345:instance~private(usr_123)~canRequestInvite', world)).toBe('Test World invite+');
		expect(displayLocation('wrld_12345:instance~friends(usr_123)', world)).toBe('Test World friends');
		expect(displayLocation('wrld_12345:instance~hidden(usr_123)', world)).toBe('Test World friends+');
		expect(displayLocation('wrld_12345:instance~group(grp_123)', world)).toBe('Test World group');
	});

	test('a group name goes in parentheses after the access type', () => {
		expect(displayLocation('wrld_12345:instance~group(grp_123)', 'Test World', 'My Group')).toBe('Test World group(My Group)');
		expect(displayLocation('wrld_12345:instance~group(grp_123)~groupAccessType(public)', 'Test World', 'Test Group')).toBe('Test World groupPublic(Test Group)');
		expect(displayLocation('wrld_12345:instance~group(grp_123)~groupAccessType(plus)', 'Test World', 'Test Group')).toBe('Test World groupPlus(Test Group)');
		expect(displayLocation('wrld_12345:instance~private(usr_123)', 'Test World', 'Override Group')).toBe('Test World invite(Override Group)');
	});

	test('a missing world name is empty, and an empty group name is left out', () => {
		// The original printed "undefined public" for a missing name; the port leaves it empty.
		expect(displayLocation('wrld_12345:instance', '')).toBe(' public');
		expect(displayLocation('wrld_12345:instance', undefined)).toBe(' public');
		expect(displayLocation('wrld_12345:instance~group(grp_123)', 'Test World', '')).toBe('Test World group');
	});

	test('local and malformed locations are the world name', () => {
		expect(displayLocation('local:12345', 'Local World')).toBe('Local World');
		expect(displayLocation('invalid-location', 'Test World')).toBe('Test World');
		expect(displayLocation('', 'Test World')).toBe('Test World');
		expect(displayLocation(undefined, 'Test World')).toBe('Test World');
	});
});

describe('resolveRegion', () => {
	test('the sentinels have no region', () => {
		for (const tag of ['offline', 'private', 'traveling']) expect(resolveRegion(parseLocation(tag))).toBe('');
	});

	test('the tag region, or us for an instance without one', () => {
		expect(resolveRegion(parseLocation('wrld_12345:67890~region(eu)'))).toBe('eu');
		expect(resolveRegion(parseLocation('wrld_12345:67890~region(jp)'))).toBe('jp');
		expect(resolveRegion(parseLocation('wrld_12345:67890'))).toBe('us');
	});

	test('a world without an instance has no region', () => {
		expect(resolveRegion(parseLocation('wrld_12345'))).toBe('');
	});
});

describe('accessTypeLabel', () => {
	test('names each access type, group kinds with a Group prefix', () => {
		expect(accessTypeLabel('public')).toBe('Public');
		expect(accessTypeLabel('invite')).toBe('Invite');
		expect(accessTypeLabel('friends')).toBe('Friends');
		expect(accessTypeLabel('friends+')).toBe('Friends+');
		expect(accessTypeLabel('groupPublic')).toBe('Group Public');
		expect(accessTypeLabel('groupPlus')).toBe('Group Plus');
	});

	test('an unknown access type is shown as is', () => {
		expect(accessTypeLabel('unknown')).toBe('unknown');
	});
});

describe('locationText', () => {
	test('the sentinels have their labels', () => {
		expect(locationText(parseLocation('offline'))).toBe('Offline');
		expect(locationText(parseLocation('private'))).toBe('Private');
		expect(locationText(parseLocation('traveling'))).toBe('Traveling');
	});

	test('the hint, with the access type when there is an instance', () => {
		expect(locationText(parseLocation('wrld_12345:67890'), 'My World')).toBe('My World · Public');
		expect(locationText(parseLocation('wrld_12345'), 'My World')).toBe('My World');
	});

	test('the world name, with the access type when there is an instance', () => {
		expect(locationText(parseLocation('wrld_12345:67890'), '', 'Cool World')).toBe('Cool World · Public');
		expect(locationText(parseLocation('wrld_12345'), '', 'Cool World')).toBe('Cool World');
	});

	test('the world id when the name is not known', () => {
		expect(locationText(parseLocation('wrld_12345:67890'))).toBe('wrld_12345 · Public');
	});

	test('nothing for an empty location', () => {
		expect(locationText(parseLocation(''))).toBe('');
	});

	test('the hint wins over the world name', () => {
		expect(locationText(parseLocation('wrld_12345:67890'), 'Hint Text', 'World Name')).toBe('Hint Text · Public');
	});
});
