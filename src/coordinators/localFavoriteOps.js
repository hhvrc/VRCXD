import { toast } from 'vue-sonner';
import { i18n } from '../plugins/i18n';
import { compareByName, replaceReactiveObject } from '../shared/utils';

/**
 * Removes every entry whose id matches. Entries are refs with an `id`.
 *
 * @param {object[]} favoriteGroup
 * @param {string} id
 */
export function removeRefFromGroup(favoriteGroup, id) {
    for (let i = 0; i < favoriteGroup.length; ++i) {
        if (favoriteGroup[i].id === id) {
            favoriteGroup.splice(i, 1);
        }
    }
}

/**
 * Removes the first matching id. Entries are plain ids.
 *
 * @param {string[] | undefined} favoriteGroup
 * @param {string} id
 */
export function removeIdFromGroup(favoriteGroup, id) {
    if (favoriteGroup) {
        const idx = favoriteGroup.indexOf(id);
        if (idx !== -1) {
            favoriteGroup.splice(idx, 1);
        }
    }
}

/**
 * Builds the local (VRCX only) favorite group operations for one favorite
 * type. World and avatar favorites store object refs and keep a local cache
 * table of those objects, friend favorites store plain user ids.
 *
 * @param {object} options
 * @param {() => Record<string, any[]>} options.getFavorites Reactive group name -> entries map
 * @param {() => string[]} options.getGroupNames Sorted group names
 * @param {(entry: any) => string} options.idOf
 * @param {(id: string) => any} options.getEntry Entry to add for an id, undefined to skip
 * @param {(id: string) => any} options.getLoadedEntry Entry for an id loaded from the database
 * @param {string} options.rowIdKey Id property of a database favorite row
 * @param {(favoriteGroup: any[], id: string) => void} options.removeFromGroup
 * @param {object} options.db
 * @param {(id: string, group: string) => void} options.db.add
 * @param {(id: string, group: string) => void} options.db.remove
 * @param {(newName: string, group: string) => void} options.db.rename
 * @param {(group: string) => void} options.db.deleteGroup
 * @param {() => Promise<object[]>} options.db.getAll
 * @param {object} [options.cache] Local object cache, world and avatar only
 * @param {(entry: any) => void} options.cache.add
 * @param {(id: string) => void} options.cache.remove
 * @param {() => Promise<void>} options.cache.load
 * @param {(id: string) => boolean} [options.cache.keep] Keep the cache entry even when no longer favorited
 * @param {() => boolean} [options.shouldSortByName] When set, groups are sorted by name while this returns true
 * @param {(id: string) => void} options.refreshFavoriteDialog
 * @param {(id: string, added: boolean) => void} options.syncDialog
 * @param {() => void} [options.onEntriesChanged] After add, remove and load
 * @param {(newName: string, group: string) => void} [options.onGroupRenamed]
 * @param {(group: string) => void} [options.onGroupDeleted]
 */
export function createLocalFavoriteOps(options) {
    const { getFavorites, getGroupNames, idOf, db, cache } = options;

    function sort() {
        if (!options.shouldSortByName?.()) {
            return;
        }
        const favorites = getFavorites();
        for (const group of getGroupNames()) {
            if (favorites[group]) {
                favorites[group].sort(compareByName);
            }
        }
    }

    /**
     * @param {string} id
     * @param {string} [excludeGroup]
     * @returns {boolean}
     */
    function isInAnyGroup(id, excludeGroup) {
        const favorites = getFavorites();
        for (const groupName of getGroupNames()) {
            if (!favorites[groupName] || groupName === excludeGroup) {
                continue;
            }
            if (favorites[groupName].some((entry) => idOf(entry) === id)) {
                return true;
            }
        }
        return false;
    }

    /**
     * @param {string} id
     */
    function removeFromCacheIfUnused(id) {
        if (!isInAnyGroup(id) && !cache.keep?.(id)) {
            cache.remove(id);
        }
    }

    /**
     * @param {string} id
     * @param {string} group
     * @returns {boolean}
     */
    function has(id, group) {
        const favoriteGroup = getFavorites()[group];
        if (!favoriteGroup) {
            return false;
        }
        return favoriteGroup.some((entry) => idOf(entry) === id);
    }

    /**
     * @param {string} id
     * @param {string} group
     */
    function add(id, group) {
        if (has(id, group)) {
            return;
        }
        const entry = options.getEntry(id);
        if (typeof entry === 'undefined') {
            return;
        }
        const favorites = getFavorites();
        if (!favorites[group]) {
            favorites[group] = [];
        }
        favorites[group].unshift(entry);
        cache?.add(entry);
        db.add(id, group);
        options.refreshFavoriteDialog(id);
        options.syncDialog(id, true);
        sort();
        options.onEntriesChanged?.();
    }

    /**
     * @param {string} id
     * @param {string} group
     */
    function remove(id, group) {
        options.removeFromGroup(getFavorites()[group], id);
        if (cache && !isInAnyGroup(id, group) && !cache.keep?.(id)) {
            cache.remove(id);
        }
        db.remove(id, group);
        options.refreshFavoriteDialog(id);
        options.syncDialog(id, false);
        sort();
        options.onEntriesChanged?.();
    }

    /**
     * @param {string} group
     */
    function deleteGroup(group) {
        const favorites = getFavorites();
        let removedIds = null;
        if (cache) {
            removedIds = new Set(favorites[group].map(idOf));
        }
        delete favorites[group];
        db.deleteGroup(group);
        if (removedIds) {
            removedIds.forEach(removeFromCacheIfUnused);
        }
        options.onGroupDeleted?.(group);
    }

    async function load() {
        const favorites = Object.create(null);
        if (cache) {
            await cache.load();
        }
        const rows = await db.getAll();
        for (const row of rows) {
            if (!favorites[row.groupName]) {
                favorites[row.groupName] = [];
            }
            favorites[row.groupName].unshift(options.getLoadedEntry(row[options.rowIdKey]));
        }
        if (Object.keys(favorites).length === 0) {
            // default group
            favorites.Favorites = [];
        }
        replaceReactiveObject(getFavorites(), favorites);
        sort();
        options.onEntriesChanged?.();
    }

    /**
     * @param {string} newName
     * @param {string} group
     */
    function renameGroup(newName, group) {
        const { t } = i18n.global;
        if (getGroupNames().includes(newName)) {
            toast.error(
                t('prompt.local_favorite_group_rename.message.error', {
                    name: newName
                })
            );
            return;
        }
        const favorites = getFavorites();
        favorites[newName] = favorites[group];
        delete favorites[group];
        db.rename(newName, group);
        sort();
        options.onGroupRenamed?.(newName, group);
    }

    /**
     * @param {string} group
     */
    function newGroup(group) {
        const { t } = i18n.global;
        if (getGroupNames().includes(group)) {
            toast.error(
                t('prompt.new_local_favorite_group.message.error', {
                    name: group
                })
            );
            return;
        }
        const favorites = getFavorites();
        if (!favorites[group]) {
            favorites[group] = [];
        }
        sort();
    }

    return {
        sort,
        has,
        add,
        remove,
        deleteGroup,
        load,
        renameGroup,
        newGroup
    };
}
