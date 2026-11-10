import { beforeEach, describe, expect, test, vi } from 'vitest';

const mocks = vi.hoisted(() => ({
    toastError: vi.fn()
}));

vi.mock('vue-sonner', () => ({
    toast: { error: (...args) => mocks.toastError(...args) }
}));

vi.mock('../../plugins/i18n', () => ({
    i18n: { global: { t: (key) => key } }
}));

vi.mock('../../shared/utils', async () => {
    const array = await vi.importActual('../../shared/utils/base/array');
    const compare = await vi.importActual('../../shared/utils/compare');
    return {
        compareByName: compare.compareByName,
        replaceReactiveObject: array.replaceReactiveObject
    };
});

import { createLocalFavoriteOps, removeIdFromGroup, removeRefFromGroup } from '../localFavoriteOps';

function createRefOps(overrides = {}) {
    const favorites = {};
    const cachedRefs = new Map([
        ['w1', { id: 'w1', name: 'Charlie' }],
        ['w2', { id: 'w2', name: 'alpha' }],
        ['w3', { id: 'w3', name: 'Bravo' }]
    ]);
    const deps = {
        db: {
            add: vi.fn(),
            remove: vi.fn(),
            rename: vi.fn(),
            deleteGroup: vi.fn(),
            getAll: vi.fn(async () => [])
        },
        cache: {
            add: vi.fn(),
            remove: vi.fn(),
            load: vi.fn(async () => {}),
            keep: vi.fn(() => false)
        },
        sortByName: false,
        refreshFavoriteDialog: vi.fn(),
        syncDialog: vi.fn()
    };
    Object.assign(deps, overrides);
    const ops = createLocalFavoriteOps({
        getFavorites: () => favorites,
        getGroupNames: () => Object.keys(favorites).sort(),
        idOf: (ref) => ref.id,
        getEntry: (id) => cachedRefs.get(id),
        getLoadedEntry: (id) => cachedRefs.get(id) ?? { id },
        rowIdKey: 'worldId',
        removeFromGroup: removeRefFromGroup,
        db: deps.db,
        cache: deps.cache,
        shouldSortByName: () => deps.sortByName,
        refreshFavoriteDialog: deps.refreshFavoriteDialog,
        syncDialog: deps.syncDialog
    });
    return { ops, favorites, deps };
}

function createIdOps() {
    const favorites = {};
    const deps = {
        db: {
            add: vi.fn(),
            remove: vi.fn(),
            rename: vi.fn(),
            deleteGroup: vi.fn(),
            getAll: vi.fn(async () => [])
        },
        refreshFavoriteDialog: vi.fn(),
        syncDialog: vi.fn(),
        onEntriesChanged: vi.fn(),
        onGroupRenamed: vi.fn(),
        onGroupDeleted: vi.fn()
    };
    const ops = createLocalFavoriteOps({
        getFavorites: () => favorites,
        getGroupNames: () => Object.keys(favorites).sort(),
        idOf: (userId) => userId,
        getEntry: (userId) => userId,
        getLoadedEntry: (userId) => userId,
        rowIdKey: 'userId',
        removeFromGroup: removeIdFromGroup,
        db: deps.db,
        refreshFavoriteDialog: deps.refreshFavoriteDialog,
        syncDialog: deps.syncDialog,
        onEntriesChanged: deps.onEntriesChanged,
        onGroupRenamed: deps.onGroupRenamed,
        onGroupDeleted: deps.onGroupDeleted
    });
    return { ops, favorites, deps };
}

describe('createLocalFavoriteOps with object refs', () => {
    beforeEach(() => {
        mocks.toastError.mockClear();
    });

    test('add stores the cached ref, caches it and syncs dialogs', () => {
        const { ops, favorites, deps } = createRefOps();
        ops.add('w1', 'Favorites');
        expect(favorites.Favorites).toEqual([{ id: 'w1', name: 'Charlie' }]);
        expect(deps.cache.add).toHaveBeenCalledWith({ id: 'w1', name: 'Charlie' });
        expect(deps.db.add).toHaveBeenCalledWith('w1', 'Favorites');
        expect(deps.refreshFavoriteDialog).toHaveBeenCalledWith('w1');
        expect(deps.syncDialog).toHaveBeenCalledWith('w1', true);
        expect(ops.has('w1', 'Favorites')).toBe(true);
    });

    test('add ignores duplicates and uncached ids', () => {
        const { ops, favorites, deps } = createRefOps();
        ops.add('w1', 'Favorites');
        ops.add('w1', 'Favorites');
        ops.add('unknown', 'Favorites');
        expect(favorites.Favorites).toHaveLength(1);
        expect(deps.db.add).toHaveBeenCalledTimes(1);
    });

    test('groups are sorted by name only when enabled', () => {
        const { ops, favorites, deps } = createRefOps();
        ops.add('w1', 'Favorites');
        ops.add('w2', 'Favorites');
        expect(favorites.Favorites.map((ref) => ref.id)).toEqual(['w2', 'w1']);
        deps.sortByName = true;
        ops.add('w3', 'Favorites');
        expect(favorites.Favorites.map((ref) => ref.name)).toEqual(['alpha', 'Bravo', 'Charlie']);
    });

    test('remove keeps the cache entry while another group still has it', () => {
        const { ops, deps } = createRefOps();
        ops.add('w1', 'A');
        ops.add('w1', 'B');
        ops.remove('w1', 'A');
        expect(deps.cache.remove).not.toHaveBeenCalled();
        expect(deps.db.remove).toHaveBeenCalledWith('w1', 'A');
        expect(deps.syncDialog).toHaveBeenLastCalledWith('w1', false);
        ops.remove('w1', 'B');
        expect(deps.cache.remove).toHaveBeenCalledWith('w1');
    });

    test('remove keeps the cache entry when cache.keep says so', () => {
        const { ops, deps } = createRefOps();
        deps.cache.keep.mockReturnValue(true);
        ops.add('w1', 'A');
        ops.remove('w1', 'A');
        expect(deps.cache.remove).not.toHaveBeenCalled();
    });

    test('deleteGroup only uncaches refs not favorited in another group', () => {
        const { ops, favorites, deps } = createRefOps();
        ops.add('w1', 'A');
        ops.add('w2', 'A');
        ops.add('w3', 'A');
        ops.add('w1', 'B');
        ops.add('w2', 'B');
        ops.deleteGroup('A');
        expect(favorites.A).toBeUndefined();
        expect(deps.db.deleteGroup).toHaveBeenCalledWith('A');
        expect(deps.cache.remove.mock.calls).toEqual([['w3']]);
    });

    test('load fills groups from the database and falls back to a default group', async () => {
        const { ops, favorites, deps } = createRefOps();
        await ops.load();
        expect(deps.cache.load).toHaveBeenCalled();
        expect(Object.keys(favorites)).toEqual(['Favorites']);

        deps.db.getAll.mockResolvedValue([
            { worldId: 'w1', groupName: 'A' },
            { worldId: 'gone', groupName: 'A' }
        ]);
        await ops.load();
        expect(Object.keys(favorites)).toEqual(['A']);
        expect(favorites.A).toEqual([{ id: 'gone' }, { id: 'w1', name: 'Charlie' }]);
    });

    test('rename and new group reject existing names', () => {
        const { ops, favorites, deps } = createRefOps();
        ops.add('w1', 'A');
        ops.newGroup('B');
        expect(favorites.B).toEqual([]);
        ops.newGroup('B');
        expect(mocks.toastError).toHaveBeenCalledTimes(1);
        ops.renameGroup('B', 'A');
        expect(mocks.toastError).toHaveBeenCalledTimes(2);
        expect(deps.db.rename).not.toHaveBeenCalled();
        ops.renameGroup('C', 'A');
        expect(favorites.A).toBeUndefined();
        expect(favorites.C).toEqual([{ id: 'w1', name: 'Charlie' }]);
        expect(deps.db.rename).toHaveBeenCalledWith('C', 'A');
    });
});

describe('createLocalFavoriteOps with plain ids', () => {
    test('stores user ids and notifies on changes', async () => {
        const { ops, favorites, deps } = createIdOps();
        ops.add('usr_1', 'A');
        expect(favorites.A).toEqual(['usr_1']);
        expect(deps.onEntriesChanged).toHaveBeenCalledTimes(1);
        ops.remove('usr_1', 'A');
        expect(favorites.A).toEqual([]);
        expect(deps.onEntriesChanged).toHaveBeenCalledTimes(2);
        ops.remove('usr_1', 'Missing');
        expect(deps.db.remove).toHaveBeenLastCalledWith('usr_1', 'Missing');

        deps.db.getAll.mockResolvedValue([{ userId: 'usr_2', groupName: 'B' }]);
        await ops.load();
        expect({ ...favorites }).toEqual({ B: ['usr_2'] });
        expect(deps.onEntriesChanged).toHaveBeenCalledTimes(4);
    });

    test('rename and delete call the group hooks', () => {
        const { ops, favorites, deps } = createIdOps();
        ops.add('usr_1', 'A');
        ops.renameGroup('B', 'A');
        expect(deps.onGroupRenamed).toHaveBeenCalledWith('B', 'A');
        ops.deleteGroup('B');
        expect(favorites.B).toBeUndefined();
        expect(deps.db.deleteGroup).toHaveBeenCalledWith('B');
        expect(deps.onGroupDeleted).toHaveBeenCalledWith('B');
    });
});
