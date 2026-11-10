import { useFavoriteStore } from '../stores/favorite';
import { useAppearanceSettingsStore } from '../stores/settings/appearance';
import { useAvatarStore } from '../stores/avatar';
import { applyAvatar } from './avatarCoordinator';
import { useFriendStore } from '../stores/friend';
import { useGeneralSettingsStore } from '../stores/settings/general';
import { useUserStore } from '../stores/user';
import { useWorldStore } from '../stores/world';
import { rebuildFavoriteSearchIndex } from './searchIndexCoordinator';
import { createLocalFavoriteOps, removeIdFromGroup, removeRefFromGroup } from './localFavoriteOps';
import { applyWorld } from './worldCoordinator';
import { runUpdateFriendFlow } from './friendPresenceCoordinator';
import { avatarRequest, favoriteRequest, queryRequest } from '../api';
import { database } from '../services/database';
import { processBulk } from '../services/request';
import { watchState } from '../services/watchState';
import { createDefaultFavoriteCachedRef, removeFromArray } from '../shared/utils';

// --- handleFavorite / handleFavoriteAtDelete / handleFavoriteAdd ---

/**
 * @param {object} args
 * @returns {void}
 */
export function handleFavorite(args) {
    const userStore = useUserStore();
    const worldStore = useWorldStore();
    const avatarStore = useAvatarStore();

    args.ref = applyFavoriteCached(args.json);
    applyFavorite(args.ref.type, args.ref.favoriteId);
    runUpdateFriendFlow(args.ref.favoriteId);
    const { ref } = args;
    const userDialog = userStore.userDialog;
    if (userDialog.visible && ref.favoriteId === userDialog.id) {
        userStore.setUserDialogIsFavorite(true);
    }
    const worldDialog = worldStore.worldDialog;
    if (worldDialog.visible && ref.favoriteId === worldDialog.id) {
        worldStore.setWorldDialogIsFavorite(true);
    }
    const avatarDialog = avatarStore.avatarDialog;
    if (avatarDialog.visible && ref.favoriteId === avatarDialog.id) {
        avatarStore.setAvatarDialogIsFavorite(true);
    }
}

/**
 * @param {object} args
 * @returns {void}
 */
export function handleFavoriteAdd(args) {
    const favoriteStore = useFavoriteStore();
    const friendStore = useFriendStore();
    const avatarStore = useAvatarStore();
    const generalSettingsStore = useGeneralSettingsStore();

    handleFavorite({
        json: args.json,
        params: {
            favoriteId: args.json.id
        }
    });
    if (!favoriteStore.favoritesSortOrder.includes(args.params.favoriteId)) {
        favoriteStore.favoritesSortOrder.unshift(args.params.favoriteId);
    }

    if (args.params.type === 'avatar' && !avatarStore.cachedAvatars.has(args.params.favoriteId)) {
        refreshFavoriteAvatars(args.params.tags);
    }

    if (
        args.params.type === 'friend' &&
        (!generalSettingsStore.localFavoriteFriendsGroups.some((key) => !key.startsWith('local:')) ||
            generalSettingsStore.localFavoriteFriendsGroups.includes('friend:' + args.params.tags))
    ) {
        friendStore.updateLocalFavoriteFriends();
    }
    favoriteStore.updateFavoriteDialog(args.params.objectId);
}

/**
 * @param {object} ref
 * @returns {void}
 */
export function handleFavoriteAtDelete(ref) {
    const favoriteStore = useFavoriteStore();
    const friendStore = useFriendStore();
    const userStore = useUserStore();
    const worldStore = useWorldStore();
    const avatarStore = useAvatarStore();

    const favorite = favoriteStore.state.favoriteObjects.get(ref.favoriteId);
    removeFromArray(favoriteStore.state.favoriteFriends_, favorite);
    removeFromArray(favoriteStore.state.favoriteWorlds_, favorite);
    removeFromArray(favoriteStore.state.favoriteAvatars_, favorite);
    favoriteStore.cachedFavorites.delete(ref.id);
    favoriteStore.cachedFavoritesByObjectId.delete(ref.favoriteId);
    favoriteStore.state.favoriteObjects.delete(ref.favoriteId);
    friendStore.localFavoriteFriends.delete(ref.favoriteId);
    favoriteStore.setFavoritesSortOrder(favoriteStore.favoritesSortOrder.filter((id) => id !== ref.favoriteId));

    runUpdateFriendFlow(ref.favoriteId);
    friendStore.updateSidebarFavorites();
    const userDialog = userStore.userDialog;
    if (userDialog.visible && userDialog.id === ref.favoriteId) {
        userStore.setUserDialogIsFavorite(false);
    }
    const worldDialog = worldStore.worldDialog;
    if (worldDialog.visible && worldDialog.id === ref.favoriteId) {
        worldStore.setWorldDialogIsFavorite(favoriteStore.localWorldFavoritesList.includes(worldDialog.id));
    }
    const avatarDialog = avatarStore.avatarDialog;
    if (avatarDialog.visible && avatarDialog.id === ref.favoriteId) {
        avatarStore.setAvatarDialogIsFavorite(false);
    }
    favoriteStore.countFavoriteGroups();
    rebuildFavoriteSearchIndex();
}

/**
 * @param {string} objectId
 * @returns {void}
 */
export function handleFavoriteDelete(objectId) {
    const favoriteStore = useFavoriteStore();
    const ref = favoriteStore.getCachedFavoritesByObjectId(objectId);
    if (typeof ref === 'undefined') {
        return;
    }
    handleFavoriteAtDelete(ref);
}

/**
 * @param {object} args
 * @returns {void}
 */
export function handleFavoriteGroupClear(args) {
    const favoriteStore = useFavoriteStore();
    const key = `${args.params.type}:${args.params.group}`;
    for (const ref of favoriteStore.cachedFavorites.values()) {
        if (ref.$groupKey !== key) {
            continue;
        }
        handleFavoriteAtDelete(ref);
    }
}

// --- List handlers ---

/**
 * @param {object} args
 * @returns {void}
 */
export function handleFavoriteWorldList(args) {
    for (const json of args.json) {
        if (json.id === '???') {
            continue;
        }
        applyWorld(json);
    }
}

/**
 * @param {object} args
 */
export function handleFavoriteAvatarList(args) {
    for (const json of args.json) {
        if (json.releaseStatus === 'hidden') {
            continue;
        }
        applyAvatar(json);
    }
}

// --- applyFavoriteCached / applyFavorite ---

/**
 * @param {object} json
 * @returns {object}
 */
export function applyFavoriteCached(json) {
    const favoriteStore = useFavoriteStore();
    const friendStore = useFriendStore();
    const generalSettingsStore = useGeneralSettingsStore();
    const groups = generalSettingsStore.localFavoriteFriendsGroups;
    let ref = favoriteStore.cachedFavorites.get(json.id);
    if (typeof ref === 'undefined') {
        ref = createDefaultFavoriteCachedRef(json);
        const oldEntry = favoriteStore.cachedFavoritesByObjectId.get(ref.favoriteId);
        if (oldEntry && oldEntry.id !== ref.id) {
            favoriteStore.cachedFavorites.delete(oldEntry.id);
        }
        favoriteStore.cachedFavorites.set(ref.id, ref);
        favoriteStore.cachedFavoritesByObjectId.set(ref.favoriteId, ref);
        if (ref.type === 'friend' && (groups.length === 0 || groups.includes(ref.$groupKey))) {
            friendStore.localFavoriteFriends.add(ref.favoriteId);
            friendStore.updateSidebarFavorites();
        }
        if (!favoriteStore.isFavoriteLoading) {
            favoriteStore.countFavoriteGroups();
        }
    } else {
        if (ref.favoriteId !== json.favoriteId) {
            favoriteStore.cachedFavoritesByObjectId.delete(ref.favoriteId);
        }
        Object.assign(ref, json);
        ref.$groupKey = `${ref.type}:${String(ref.tags[0])}`;
        favoriteStore.cachedFavoritesByObjectId.set(ref.favoriteId, ref);
    }

    return ref;
}

/**
 * @param {'friend' | 'world' | 'vrcPlusWorld' | 'avatar'} type
 * @param {string} objectId
 * @returns {Promise<void>}
 */
export async function applyFavorite(type, objectId) {
    const favoriteStore = useFavoriteStore();
    const userStore = useUserStore();
    const friendStore = useFriendStore();
    const worldStore = useWorldStore();
    const avatarStore = useAvatarStore();

    let ref;
    const favorite = favoriteStore.getCachedFavoritesByObjectId(objectId);
    let ctx = favoriteStore.state.favoriteObjects.get(objectId);
    if (typeof favorite !== 'undefined') {
        let isTypeChanged = false;
        if (typeof ctx === 'undefined') {
            ctx = {
                id: objectId,
                type,
                groupKey: favorite.$groupKey,
                ref: null,
                name: ''
            };
            if (type === 'friend') {
                ref = userStore.cachedUsers.get(objectId);
                if (typeof ref === 'undefined') {
                    ref = friendStore.friendLog.get(objectId);
                    if (typeof ref !== 'undefined' && ref.displayName) {
                        ctx.name = ref.displayName;
                    }
                } else {
                    ctx.ref = ref;
                    ctx.name = ref.displayName;
                }
            } else if (type === 'world' || type === 'vrcPlusWorld') {
                ref = worldStore.cachedWorlds.get(objectId);
                if (typeof ref !== 'undefined') {
                    ctx.ref = ref;
                    ctx.name = ref.name;
                }
            } else if (type === 'avatar') {
                ref = avatarStore.cachedAvatars.get(objectId);
                if (typeof ref !== 'undefined') {
                    ctx.ref = ref;
                    ctx.name = ref.name;
                }
            }
            favoriteStore.state.favoriteObjects.set(objectId, ctx);
            isTypeChanged = true;
        } else {
            if (ctx.type !== type) {
                // WTF???
                isTypeChanged = true;
                if (type === 'friend') {
                    removeFromArray(favoriteStore.state.favoriteFriends_, ctx);
                } else if (type === 'world' || type === 'vrcPlusWorld') {
                    removeFromArray(favoriteStore.state.favoriteWorlds_, ctx);
                } else if (type === 'avatar') {
                    removeFromArray(favoriteStore.state.favoriteAvatars_, ctx);
                }
            }
            ctx.groupKey = favorite.$groupKey;
            if (type === 'friend') {
                ref = userStore.cachedUsers.get(objectId);
                if (typeof ref !== 'undefined') {
                    if (ctx.ref !== ref) {
                        ctx.ref = ref;
                    }
                    if (ctx.name !== ref.displayName) {
                        ctx.name = ref.displayName;
                    }
                }
                // else too bad
            } else if (type === 'world' || type === 'vrcPlusWorld') {
                ref = worldStore.cachedWorlds.get(objectId);
                if (typeof ref !== 'undefined') {
                    if (ctx.ref !== ref) {
                        ctx.ref = ref;
                    }
                    if (ctx.name !== ref.name) {
                        ctx.name = ref.name;
                    }
                } else {
                    // try fetch from local world favorites
                    const world = await database.getCachedWorldById(objectId);
                    if (world) {
                        ctx.ref = world;
                        ctx.name = world.name;
                        ctx.deleted = true;
                    }
                    if (!world) {
                        // try fetch from local world history
                        const worldName = await database.getGameLogWorldNameByWorldId(objectId);
                        if (worldName) {
                            ctx.name = worldName;
                            ctx.deleted = true;
                        }
                    }
                }
            } else if (type === 'avatar') {
                ref = avatarStore.cachedAvatars.get(objectId);
                if (typeof ref !== 'undefined') {
                    if (ctx.ref !== ref) {
                        ctx.ref = ref;
                    }
                    if (ctx.name !== ref.name) {
                        ctx.name = ref.name;
                    }
                } else {
                    // try fetch from local avatar history
                    const avatar = await database.getCachedAvatarById(objectId);
                    if (avatar) {
                        ctx.ref = avatar;
                        ctx.name = avatar.name;
                        ctx.deleted = true;
                    }
                }
            }
        }
        if (isTypeChanged) {
            if (type === 'friend') {
                favoriteStore.state.favoriteFriends_.push(ctx);
            } else if (type === 'world' || type === 'vrcPlusWorld') {
                favoriteStore.state.favoriteWorlds_.push(ctx);
            } else if (type === 'avatar') {
                favoriteStore.state.favoriteAvatars_.push(ctx);
            }
        }
    }
    rebuildFavoriteSearchIndex();
}

// --- Refresh flows ---

/**
 * @returns {void}
 */
export function refreshFavorites() {
    const favoriteStore = useFavoriteStore();
    const friendStore = useFriendStore();

    if (favoriteStore.isFavoriteLoading) {
        return;
    }
    favoriteStore.setIsFavoriteLoading(true);
    queryRequest
        .fetch('favoriteLimits')
        .then((args) => {
            favoriteStore.setFavoriteLimits({
                ...favoriteStore.favoriteLimits,
                ...args.json
            });
        })
        .catch((err) => {
            console.error(err);
        });
    let newFavoriteSortOrder = [];
    processBulk({
        fn: (params) => favoriteRequest.getFavorites(params),
        N: -1,
        params: {
            n: 300,
            offset: 0
        },
        handle(args) {
            for (const json of args.json) {
                newFavoriteSortOrder.push(json.favoriteId);
                handleFavorite({
                    json,
                    params: {
                        favoriteId: json.id
                    }
                });
            }
        },
        done(ok) {
            if (ok) {
                for (const id of favoriteStore.favoritesSortOrder) {
                    if (!newFavoriteSortOrder.includes(id)) {
                        const fav = favoriteStore.cachedFavoritesByObjectId.get(id);
                        if (fav) {
                            handleFavoriteAtDelete(fav);
                        }
                    }
                }
                favoriteStore.setFavoritesSortOrder(newFavoriteSortOrder);
            }
            refreshFavoriteItems();
            favoriteStore.refreshFavoriteGroups();
            friendStore.updateLocalFavoriteFriends();
            favoriteStore.setIsFavoriteLoading(false);
            watchState.isFavoritesLoaded = true;
            favoriteStore.countFavoriteGroups();
        }
    });
}

/**
 * @param {string} tag
 * @returns {Promise<void>}
 */
export async function refreshFavoriteAvatars(tag) {
    const params = {
        n: 300,
        offset: 0,
        tag
    };
    const args = await favoriteRequest.getFavoriteAvatars(params);
    handleFavoriteAvatarList(args);
}

/**
 * @returns {void}
 */
export function refreshFavoriteItems() {
    const favoriteStore = useFavoriteStore();
    const types = {
        world: [0, (params) => favoriteRequest.getFavoriteWorlds(params)],
        avatar: [0, (params) => favoriteRequest.getFavoriteAvatars(params)]
    };
    const tags = [];
    for (const ref of favoriteStore.cachedFavorites.values()) {
        const type = types[ref.type];
        if (typeof type === 'undefined') {
            continue;
        }
        if (ref.type === 'avatar' && !tags.includes(ref.tags[0])) {
            tags.push(ref.tags[0]);
        }
        ++type[0];
    }
    for (const type in types) {
        const [N, fn] = types[type];
        if (N > 0) {
            if (type === 'avatar') {
                for (const tag of tags) {
                    processBulk({
                        fn,
                        N,
                        handle: (args) => handleFavoriteAvatarList(args),
                        params: {
                            n: 300,
                            offset: 0,
                            tag
                        }
                    });
                }
            } else {
                processBulk({
                    fn,
                    N,
                    handle: (args) => handleFavoriteWorldList(args),
                    params: {
                        n: 300,
                        offset: 0
                    }
                });
            }
        }
    }
}

// --- Local favorites ---

/**
 * @param {string} objectId
 */
function refreshFavoriteDialog(objectId) {
    const favoriteStore = useFavoriteStore();
    if (favoriteStore.favoriteDialog.visible && favoriteStore.favoriteDialog.objectId === objectId) {
        favoriteStore.updateFavoriteDialog(objectId);
    }
}

/**
 * @returns {boolean}
 */
function shouldSortByName() {
    return !useAppearanceSettingsStore().sortFavorites;
}

const localWorldFavoriteOps = createLocalFavoriteOps({
    getFavorites: () => useFavoriteStore().localWorldFavorites,
    getGroupNames: () => useFavoriteStore().localWorldFavoriteGroups,
    idOf: (ref) => ref.id,
    getEntry: (worldId) => useWorldStore().cachedWorlds.get(worldId),
    getLoadedEntry: (worldId) => useWorldStore().cachedWorlds.get(worldId) ?? { id: worldId },
    rowIdKey: 'worldId',
    removeFromGroup: removeRefFromGroup,
    db: {
        add: (worldId, group) => database.addWorldToFavorites(worldId, group),
        remove: (worldId, group) => database.removeWorldFromFavorites(worldId, group),
        rename: (newName, group) => database.renameWorldFavoriteGroup(newName, group),
        deleteGroup: (group) => database.deleteWorldFavoriteGroup(group),
        getAll: () => database.getWorldFavorites()
    },
    cache: {
        add: (ref) => database.addWorldToCache(ref),
        remove: (worldId) => database.removeWorldFromCache(worldId),
        async load() {
            const worldStore = useWorldStore();
            const worldCache = await database.getWorldCache();
            for (const ref of worldCache) {
                if (!worldStore.cachedWorlds.has(ref.id)) {
                    applyWorld(ref);
                }
            }
        }
    },
    shouldSortByName,
    refreshFavoriteDialog,
    syncDialog(worldId, added) {
        const worldStore = useWorldStore();
        if (worldStore.worldDialog.visible && worldStore.worldDialog.id === worldId) {
            worldStore.setWorldDialogIsFavorite(added || useFavoriteStore().getCachedFavoritesByObjectId(worldId));
        }
    }
});

const localAvatarFavoriteOps = createLocalFavoriteOps({
    getFavorites: () => useFavoriteStore().localAvatarFavorites,
    getGroupNames: () => useFavoriteStore().localAvatarFavoriteGroups,
    idOf: (ref) => ref.id,
    getEntry: (avatarId) => useAvatarStore().cachedAvatars.get(avatarId),
    getLoadedEntry: (avatarId) => useAvatarStore().cachedAvatars.get(avatarId) ?? { id: avatarId },
    rowIdKey: 'avatarId',
    removeFromGroup: removeRefFromGroup,
    db: {
        add: (avatarId, group) => database.addAvatarToFavorites(avatarId, group),
        remove: (avatarId, group) => database.removeAvatarFromFavorites(avatarId, group),
        rename: (newName, group) => database.renameAvatarFavoriteGroup(newName, group),
        deleteGroup: (group) => database.deleteAvatarFavoriteGroup(group),
        getAll: () => database.getAvatarFavorites()
    },
    cache: {
        add: (ref) => database.addAvatarToCache(ref),
        remove: (avatarId) => database.removeAvatarFromCache(avatarId),
        // avatars in the local avatar history keep their cache entry
        keep: (avatarId) => useAvatarStore().avatarHistory.includes(avatarId),
        async load() {
            const avatarStore = useAvatarStore();
            const avatarCache = await database.getAvatarCache();
            for (const ref of avatarCache) {
                if (!avatarStore.cachedAvatars.has(ref.id)) {
                    applyAvatar(ref);
                }
            }
        }
    },
    shouldSortByName,
    refreshFavoriteDialog,
    syncDialog(avatarId, added) {
        const avatarStore = useAvatarStore();
        if (avatarStore.avatarDialog.visible && avatarStore.avatarDialog.id === avatarId) {
            avatarStore.setAvatarDialogIsFavorite(added || useFavoriteStore().getCachedFavoritesByObjectId(avatarId));
        }
    }
});

const localFriendFavoriteOps = createLocalFavoriteOps({
    getFavorites: () => useFavoriteStore().localFriendFavorites,
    getGroupNames: () => useFavoriteStore().localFriendFavoriteGroups,
    idOf: (userId) => userId,
    getEntry: (userId) => userId,
    getLoadedEntry: (userId) => userId,
    rowIdKey: 'userId',
    removeFromGroup: removeIdFromGroup,
    db: {
        add: (userId, group) => database.addFriendToLocalFavorites(userId, group),
        remove: (userId, group) => database.removeFriendFromLocalFavorites(userId, group),
        rename: (newName, group) => database.renameFriendFavoriteGroup(newName, group),
        deleteGroup: (group) => database.deleteFriendFavoriteGroup(group),
        getAll: () => database.getFriendFavorites()
    },
    refreshFavoriteDialog,
    syncDialog(userId, added) {
        const favoriteStore = useFavoriteStore();
        const userStore = useUserStore();
        const userDialog = userStore.userDialog;
        if (userDialog.visible && userDialog.id === userId) {
            userStore.setUserDialogIsFavorite(
                added ||
                    favoriteStore.getCachedFavoritesByObjectId(userId) ||
                    favoriteStore.isInAnyLocalFriendGroup(userId)
            );
        }
    },
    onEntriesChanged: () => useFriendStore().updateLocalFavoriteFriends(),
    onGroupRenamed(newName, group) {
        const generalSettingsStore = useGeneralSettingsStore();
        const oldKey = `local:${group}`;
        const idx = generalSettingsStore.localFavoriteFriendsGroups.indexOf(oldKey);
        if (idx !== -1) {
            const updated = [...generalSettingsStore.localFavoriteFriendsGroups];
            updated[idx] = `local:${newName}`;
            generalSettingsStore.setLocalFavoriteFriendsGroups(updated);
        }
    },
    onGroupDeleted(group) {
        const generalSettingsStore = useGeneralSettingsStore();
        const key = `local:${group}`;
        if (generalSettingsStore.localFavoriteFriendsGroups.includes(key)) {
            generalSettingsStore.setLocalFavoriteFriendsGroups(
                generalSettingsStore.localFavoriteFriendsGroups.filter((k) => k !== key)
            );
        }
        useFriendStore().updateLocalFavoriteFriends();
    }
});

export const sortLocalWorldFavorites = localWorldFavoriteOps.sort;
export const addLocalWorldFavorite = localWorldFavoriteOps.add;
export const removeLocalWorldFavorite = localWorldFavoriteOps.remove;
export const deleteLocalWorldFavoriteGroup = localWorldFavoriteOps.deleteGroup;
export const getLocalWorldFavorites = localWorldFavoriteOps.load;
export const renameLocalWorldFavoriteGroup = localWorldFavoriteOps.renameGroup;
export const newLocalWorldFavoriteGroup = localWorldFavoriteOps.newGroup;

export const sortLocalAvatarFavorites = localAvatarFavoriteOps.sort;
export const addLocalAvatarFavorite = localAvatarFavoriteOps.add;
export const removeLocalAvatarFavorite = localAvatarFavoriteOps.remove;
export const deleteLocalAvatarFavoriteGroup = localAvatarFavoriteOps.deleteGroup;
export const getLocalAvatarFavorites = localAvatarFavoriteOps.load;
export const renameLocalAvatarFavoriteGroup = localAvatarFavoriteOps.renameGroup;
export const newLocalAvatarFavoriteGroup = localAvatarFavoriteOps.newGroup;

export const addLocalFriendFavorite = localFriendFavoriteOps.add;
export const removeLocalFriendFavorite = localFriendFavoriteOps.remove;
export const deleteLocalFriendFavoriteGroup = localFriendFavoriteOps.deleteGroup;
export const getLocalFriendFavorites = localFriendFavoriteOps.load;
export const renameLocalFriendFavoriteGroup = localFriendFavoriteOps.renameGroup;
export const newLocalFriendFavoriteGroup = localFriendFavoriteOps.newGroup;

/**
 * Check invalid local avatar favorites
 *
 * @param {string | null} targetGroup - Target group to check, null for all groups
 * @param {Function | null} onProgress - Progress callback function, receives (current, total) parameters
 * @returns {Promise<{ total: number; invalid: number; invalidIds: string[] }>}
 */
export async function checkInvalidLocalAvatars(targetGroup = null, onProgress = null) {
    const favoriteStore = useFavoriteStore();
    const result = {
        total: 0,
        invalid: 0,
        invalidIds: []
    };

    const groupsToCheck = targetGroup ? [targetGroup] : favoriteStore.localAvatarFavoriteGroups;

    for (const group of groupsToCheck) {
        const favoriteGroup = favoriteStore.localAvatarFavorites[group];
        if (favoriteGroup && favoriteGroup.length > 0) {
            result.total += favoriteGroup.length;
        }
    }

    let currentIndex = 0;

    for (const group of groupsToCheck) {
        const favoriteGroup = favoriteStore.localAvatarFavorites[group];
        if (!favoriteGroup || favoriteGroup.length === 0) {
            continue;
        }

        for (const favorite of favoriteGroup) {
            currentIndex++;

            if (typeof onProgress === 'function') {
                onProgress(currentIndex, result.total);
            }

            try {
                await avatarRequest.getAvatar({
                    avatarId: favorite.id
                });
                await new Promise((resolve) => setTimeout(resolve, 500));
            } catch (err) {
                console.error(`Failed to fetch avatar ${favorite.id}:`, err);
                result.invalid++;
                result.invalidIds.push(favorite.id);
            }
        }
    }

    return result;
}

/**
 * Remove invalid avatars from local favorites
 *
 * @param {string[]} avatarIds - Array of avatar IDs to remove
 * @param {string | null} targetGroup - Target group, null for all groups
 * @returns {Promise<{ removed: number; removedIds: string[] }>}
 */
export async function removeInvalidLocalAvatars(avatarIds, targetGroup = null) {
    const favoriteStore = useFavoriteStore();
    const result = {
        removed: 0,
        removedIds: []
    };

    const groupsToCheck = targetGroup ? [targetGroup] : favoriteStore.localAvatarFavoriteGroups;

    for (const group of groupsToCheck) {
        const favoriteGroup = favoriteStore.localAvatarFavorites[group];
        if (!favoriteGroup) {
            continue;
        }

        for (const avatarId of avatarIds) {
            const index = favoriteGroup.findIndex((fav) => fav.id === avatarId);
            if (index !== -1) {
                removeLocalAvatarFavorite(avatarId, group);
                result.removed++;
                if (!result.removedIds.includes(avatarId)) {
                    result.removedIds.push(avatarId);
                }
            }
        }
    }

    return result;
}

// --- Sort / Init ---

export async function saveSortFavoritesOption() {
    const appearanceSettingsStore = useAppearanceSettingsStore();
    getLocalWorldFavorites();
    getLocalFriendFavorites();
    appearanceSettingsStore.setSortFavorites();
}

export async function initFavorites() {
    refreshFavorites();
    getLocalWorldFavorites();
    getLocalAvatarFavorites();
    getLocalFriendFavorites();
}

/**
 * Called by the favorite store watch when login state changes.
 * Handles cross-store cleanup that can't stay in the store.
 *
 * @param {boolean} isLoggedIn
 */
export function onLoginStateChanged(isLoggedIn) {
    const friendStore = useFriendStore();
    friendStore.localFavoriteFriends.clear();
    if (isLoggedIn) {
        initFavorites();
    } else {
        rebuildFavoriteSearchIndex();
    }
}
