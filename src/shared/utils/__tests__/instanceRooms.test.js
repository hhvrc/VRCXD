import {
    buildInstanceRooms,
    compareGroupDialogRooms,
    compareWorldDialogRooms,
    createInstanceRoom
} from '../instanceRooms';
import { parseLocation } from '../locationParser';

function user(id, location, extra = {}) {
    return {
        id,
        displayName: id,
        location,
        $location: parseLocation(location),
        $location_at: 0,
        ...extra
    };
}

function worldOptions(overrides = {}) {
    return {
        rooms: {},
        matches: (L) => L.worldId === 'wrld_a',
        keyOf: (L) => L.instanceId,
        tagOf: (L) => `wrld_a:${L.instanceId}`,
        currentUser: undefined,
        lastLocation: { location: '', playerList: new Map(), friendList: new Map() },
        friends: [],
        cachedUsers: new Map(),
        sortAlphabetical: true,
        ...overrides
    };
}

describe('createInstanceRoom', () => {
    test('creates an empty room', () => {
        expect(createInstanceRoom('1', 'wrld_a:1')).toEqual({
            id: '1',
            tag: 'wrld_a:1',
            $location: {},
            friendCount: 0,
            users: [],
            shortName: '',
            ref: {}
        });
    });

    test('accepts shortName and ref', () => {
        const ref = { userCount: 3 };
        const room = createInstanceRoom('1', 'wrld_a:1', { shortName: 'abc', ref });
        expect(room.shortName).toBe('abc');
        expect(room.ref).toBe(ref);
    });
});

describe('buildInstanceRooms', () => {
    test('groups matching friends into rooms and skips others', () => {
        const a = user('usr_a', 'wrld_a:1');
        const b = user('usr_b', 'wrld_a:1');
        const c = user('usr_c', 'wrld_a:2');
        const other = user('usr_d', 'wrld_b:1');
        const rooms = buildInstanceRooms(
            worldOptions({
                friends: [{ ref: b }, { ref: a }, { ref: c }, { ref: other }, { ref: undefined }]
            })
        );
        expect(rooms.map((r) => r.tag)).toEqual(['wrld_a:1', 'wrld_a:2']);
        expect(rooms[0].users.map((u) => u.id)).toEqual(['usr_a', 'usr_b']);
        expect(rooms[0].friendCount).toBe(2);
        expect(rooms[1].friendCount).toBe(1);
    });

    test('keeps seeded rooms and adds users to them', () => {
        const seeded = createInstanceRoom('1', 'wrld_a:1', { ref: { userCount: 5 } });
        const rooms = buildInstanceRooms(
            worldOptions({
                rooms: { 1: seeded, 3: createInstanceRoom('3', 'wrld_a:3') },
                friends: [{ ref: user('usr_a', 'wrld_a:1') }]
            })
        );
        expect(rooms[0]).toBe(seeded);
        expect(seeded.users.map((u) => u.id)).toEqual(['usr_a']);
        expect(rooms[1].users).toEqual([]);
        expect(rooms[1].friendCount).toBe(0);
    });

    test('adds the current user', () => {
        const me = user('usr_me', 'wrld_a:1');
        const rooms = buildInstanceRooms(worldOptions({ currentUser: me }));
        expect(rooms).toHaveLength(1);
        expect(rooms[0].users).toEqual([me]);
    });

    test('fills the in-game room from the game log friend list', () => {
        const me = user('usr_me', 'wrld_a:1~private(usr_me)');
        const inGame = user('usr_a', 'wrld_a:1~private(usr_me)');
        const sameInstance = user('usr_b', 'wrld_a:1~private(usr_me)');
        const traveling = user('usr_c', 'traveling', { $location: parseLocation('wrld_a:1~private(usr_me)') });
        const cachedUsers = new Map([[inGame.id, inGame]]);
        const rooms = buildInstanceRooms(
            worldOptions({
                currentUser: me,
                cachedUsers,
                lastLocation: {
                    location: me.location,
                    playerList: new Map([['x', {}]]),
                    friendList: new Map([
                        ['usr_a', { userId: 'usr_a' }],
                        ['usr_missing', { userId: 'usr_missing' }]
                    ])
                },
                friends: [{ ref: sameInstance }, { ref: traveling }]
            })
        );
        expect(rooms).toHaveLength(1);
        const room = rooms[0];
        expect(room.tag).toBe(me.location);
        // friendCount comes from the game log friend list, not the users found
        expect(room.friendCount).toBe(2);
        // traveling friends are still added, self is added last before sorting
        expect(room.users.map((u) => u.id).sort()).toEqual(['usr_a', 'usr_c', 'usr_me']);
    });

    test('skips friends in the game log location', () => {
        const friend = user('usr_a', 'wrld_a:2');
        const rooms = buildInstanceRooms(
            worldOptions({
                lastLocation: { location: 'wrld_a:2', playerList: new Map(), friendList: new Map() },
                friends: [{ ref: friend }]
            })
        );
        expect(rooms).toEqual([]);
    });

    test('keys group rooms by tag', () => {
        const a = user('usr_a', 'wrld_a:1~group(grp_a)');
        const b = user('usr_b', 'wrld_b:1~group(grp_a)');
        const rooms = buildInstanceRooms(
            worldOptions({
                matches: (L) => L.groupId === 'grp_a',
                keyOf: (L) => L.tag,
                tagOf: (L) => L.tag,
                friends: [{ ref: a }, { ref: b }]
            })
        );
        expect(rooms.map((r) => [r.id, r.tag])).toEqual([
            ['1~group(grp_a)', 'wrld_a:1~group(grp_a)'],
            ['1~group(grp_a)', 'wrld_b:1~group(grp_a)']
        ]);
    });

    test('sorts users by location time unless alphabetical', () => {
        const a = user('usr_b', 'wrld_a:1', { $location_at: 1 });
        const b = user('usr_a', 'wrld_a:1', { $location_at: 2 });
        const friends = [{ ref: b }, { ref: a }];
        const byTime = buildInstanceRooms(worldOptions({ friends, sortAlphabetical: false }));
        expect(byTime[0].users.map((u) => u.id)).toEqual(['usr_b', 'usr_a']);
        const byName = buildInstanceRooms(worldOptions({ friends, sortAlphabetical: true }));
        expect(byName[0].users.map((u) => u.id)).toEqual(['usr_a', 'usr_b']);
    });
});

function room(location, users = 0, userCount) {
    return {
        id: location.split(':')[1],
        location,
        users: Array.from({ length: users }, (_, i) => ({ id: `usr_${i}` })),
        ref: { userCount }
    };
}

describe('compareWorldDialogRooms', () => {
    test('orders selected, current, friends, users, id', () => {
        const rooms = [
            room('wrld_a:5', 0, 1),
            room('wrld_a:4', 0, 9),
            room('wrld_a:3', 2),
            room('wrld_a:2', 1),
            room('wrld_a:1', 1),
            room('wrld_a:cur'),
            room('wrld_a:sel')
        ];
        rooms.sort(compareWorldDialogRooms('wrld_a:sel', 'wrld_a:cur'));
        expect(rooms.map((r) => r.id)).toEqual(['sel', 'cur', '3', '1', '2', '4', '5']);
    });
});

describe('compareGroupDialogRooms', () => {
    test('orders current, then friends, then users', () => {
        const rooms = [room('wrld_a:1', 0, 1), room('wrld_a:2', 0, 9), room('wrld_a:3', 2), room('wrld_a:cur')];
        rooms.sort(compareGroupDialogRooms('wrld_a:cur'));
        expect(rooms.map((r) => r.id)).toEqual(['cur', '3', '2', '1']);
    });

    test('is consistent for ties', () => {
        const compare = compareGroupDialogRooms('wrld_a:cur');
        const a = room('wrld_a:1', 1);
        const b = room('wrld_a:2', 1);
        const c = room('wrld_a:3', 0, 4);
        const d = room('wrld_a:4', 0, 4);
        expect(compare(a, b)).toBe(-compare(b, a));
        expect(compare(c, d)).toBe(-compare(d, c));
        expect(compare(a, a)).toBe(0);
        const rooms = [d, b, c, a];
        rooms.sort(compare);
        expect(rooms.map((r) => r.id)).toEqual(['1', '2', '3', '4']);
    });
});
