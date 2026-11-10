import { compareByDisplayName, compareById, compareByLocationAt } from './compare';

/**
 * Empty room entry as shown in the world and group dialog instance lists.
 *
 * @param {string} id Instance id
 * @param {string} tag Full location tag
 * @param {object} [options]
 * @param {string} [options.shortName]
 * @param {object} [options.ref]
 * @returns {object}
 */
function createInstanceRoom(id, tag, { shortName = '', ref = {} } = {}) {
    return {
        id,
        tag,
        $location: {},
        friendCount: 0,
        users: [],
        shortName,
        ref
    };
}

/**
 * Shared room building pipeline of the world and group dialogs.
 * Adds the friends from the in-game player list, the other friends and the
 * current user to the seeded rooms, then fills in friendCount and sorts the
 * users of each room.
 *
 * @param {object} options
 * @param {object} options.rooms Seeded rooms keyed by keyOf, mutated
 * @param {(location: object) => boolean} options.matches Whether a parsed location belongs to the dialog
 * @param {(location: object) => string} options.keyOf Room key of a parsed location
 * @param {(location: object) => string} options.tagOf Tag of a new room for a parsed location
 * @param {object} [options.currentUser] Cached ref of the current user
 * @param {object} options.lastLocation Game log location (location, playerList, friendList)
 * @param {Iterable<object>} options.friends Friend entries with a ref
 * @param {Map<string, object>} options.cachedUsers
 * @param {boolean} options.sortAlphabetical
 * @returns {object[]} Rooms in insertion order
 */
function buildInstanceRooms({
    rooms,
    matches,
    keyOf,
    tagOf,
    currentUser,
    lastLocation,
    friends,
    cachedUsers,
    sortAlphabetical
}) {
    const lastLocation$ = currentUser?.$location;
    const playersInInstance = lastLocation.playerList;
    if (lastLocation$ && matches(lastLocation$) && playersInInstance.size > 0) {
        // pull instance json from cache
        const friendsInInstance = lastLocation.friendList;
        const room = createInstanceRoom(lastLocation$.instanceId, lastLocation$.tag);
        room.friendCount = friendsInInstance.size;
        rooms[keyOf(lastLocation$)] = room;
        for (const friend of friendsInInstance.values()) {
            // if friend isn't in instance add them
            const addUser = !room.users.some((user) => friend.userId === user.id);
            if (addUser) {
                const ref = cachedUsers.get(friend.userId);
                if (typeof ref !== 'undefined') {
                    room.users.push(ref);
                }
            }
        }
    }
    const addToRoom = (ref) => {
        const key = keyOf(ref.$location);
        let room = rooms[key];
        if (typeof room === 'undefined') {
            room = createInstanceRoom(ref.$location.instanceId, tagOf(ref.$location));
            rooms[key] = room;
        }
        room.users.push(ref);
    };
    for (const friend of friends) {
        const { ref } = friend;
        if (
            typeof ref === 'undefined' ||
            typeof ref.$location === 'undefined' ||
            !matches(ref.$location) ||
            (ref.$location.instanceId === lastLocation$?.instanceId &&
                playersInInstance.size > 0 &&
                ref.location !== 'traveling')
        ) {
            continue;
        }
        if (ref.location === lastLocation.location) {
            // don't add friends to currentUser gameLog instance (except when traveling)
            continue;
        }
        addToRoom(ref);
    }
    if (typeof currentUser !== 'undefined' && matches(currentUser.$location)) {
        addToRoom(currentUser); // add self
    }
    const result = Object.values(rooms);
    for (const room of result) {
        if (room.friendCount === 0) {
            room.friendCount = room.users.length;
        }
        room.users.sort(sortAlphabetical ? compareByDisplayName : compareByLocationAt);
    }
    return result;
}

/**
 * World dialog room order: selected instance, then current instance, then by
 * friend count (by user count when neither has friends), then by id.
 *
 * @param {string} selectedLocation
 * @param {string} [currentLocation]
 * @returns {(a: object, b: object) => number}
 */
function compareWorldDialogRooms(selectedLocation, currentLocation) {
    return function (a, b) {
        // sort selected and current instance to top
        if (b.location === selectedLocation || b.location === currentLocation) {
            // sort selected instance above current instance
            if (a.location === selectedLocation) {
                return -1;
            }
            return 1;
        }
        if (a.location === selectedLocation || a.location === currentLocation) {
            // sort selected instance above current instance
            if (b.location === selectedLocation) {
                return 1;
            }
            return -1;
        }
        // sort by number of users when no friends in instance
        if (a.users.length === 0 && b.users.length === 0 && a.ref?.userCount !== b.ref?.userCount) {
            if (a.ref?.userCount < b.ref?.userCount) {
                return 1;
            }
            return -1;
        }
        // sort by number of friends in instance
        if (a.users.length !== b.users.length) {
            if (a.users.length < b.users.length) {
                return 1;
            }
            return -1;
        }
        // sort by id
        return compareById(a, b);
    };
}

/**
 * Group dialog room order: current instance, then by friend count (by user
 * count when neither has friends).
 *
 * @param {string} [currentLocation]
 * @returns {(a: object, b: object) => number}
 */
function compareGroupDialogRooms(currentLocation) {
    return function (a, b) {
        // sort current instance to top
        if (b.location === currentLocation) {
            return 1;
        }
        if (a.location === currentLocation) {
            return -1;
        }
        // sort by number of users when no friends in instance
        if (a.users.length === 0 && b.users.length === 0) {
            if (a.ref?.userCount < b.ref?.userCount) {
                return 1;
            }
            return -1;
        }
        // sort by number of friends in instance
        if (a.users.length < b.users.length) {
            return 1;
        }
        return -1;
    };
}

export { createInstanceRoom, buildInstanceRooms, compareWorldDialogRooms, compareGroupDialogRooms };
