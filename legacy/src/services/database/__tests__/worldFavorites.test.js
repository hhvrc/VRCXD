import { beforeEach, describe, expect, test, vi } from 'vitest';

const mocks = vi.hoisted(() => ({
    queryWorldFavorites: vi.fn(),
    addWorldToFavorites: vi.fn(),
    removeWorldFromFavorites: vi.fn(),
    renameWorldFavoriteGroup: vi.fn(),
    deleteWorldFavoriteGroup: vi.fn(),
    upsertCachedWorld: vi.fn(),
    removeCachedWorld: vi.fn(),
    listCachedWorlds: vi.fn(),
    getCachedWorld: vi.fn()
}));

vi.mock('../../grpcClient.js', () => ({ default: mocks }));

import { worldFavorites } from '../worldFavorites.js';

const cachedWorld = {
    id: 'wrld_1',
    authorId: 'usr_author',
    authorName: 'Author',
    created_at: '2026-01-01T00:00:00.000Z',
    description: 'a world',
    imageUrl: 'https://img/1',
    name: 'World One',
    releaseStatus: 'public',
    thumbnailImageUrl: 'https://img/1t',
    updated_at: '2026-02-01T00:00:00.000Z',
    version: 3
};

describe('worldFavorites', () => {
    beforeEach(() => {
        for (const mock of Object.values(mocks)) {
            mock.mockReset();
        }
    });

    test('favorites come back as { created_at, worldId, groupName }', async () => {
        const rows = [{ created_at: '2026-09-18T10:00:00.000Z', worldId: 'wrld_1', groupName: 'Favorites' }];
        mocks.queryWorldFavorites.mockResolvedValue(rows);

        await expect(worldFavorites.getWorldFavorites()).resolves.toEqual(rows);
    });

    test('the world cache keeps every field the local row had', async () => {
        mocks.listCachedWorlds.mockResolvedValue([cachedWorld]);
        await expect(worldFavorites.getWorldCache()).resolves.toEqual([cachedWorld]);

        mocks.getCachedWorld.mockResolvedValue(cachedWorld);
        await expect(worldFavorites.getCachedWorldById('wrld_1')).resolves.toEqual(cachedWorld);
    });

    // The RPC says null for "not cached" and undefined for "couldn't ask";
    // callers only test the row for falsiness, so both read as null.
    test('a missing row and an unreachable server both read as null', async () => {
        mocks.getCachedWorld.mockResolvedValue(null);
        await expect(worldFavorites.getCachedWorldById('wrld_x')).resolves.toBeNull();

        mocks.getCachedWorld.mockResolvedValue(undefined);
        await expect(worldFavorites.getCachedWorldById('wrld_x')).resolves.toBeNull();
    });

    test('lists survive an unreachable server as empty', async () => {
        mocks.queryWorldFavorites.mockResolvedValue(null);
        mocks.listCachedWorlds.mockResolvedValue(null);

        await expect(worldFavorites.getWorldFavorites()).resolves.toEqual([]);
        await expect(worldFavorites.getWorldCache()).resolves.toEqual([]);
    });

    test('renaming swaps the argument order for the RPC', () => {
        worldFavorites.renameWorldFavoriteGroup('New Name', 'Old Name');
        expect(mocks.renameWorldFavoriteGroup).toHaveBeenCalledWith('Old Name', 'New Name');
    });

    test('the other writes forward their arguments', () => {
        worldFavorites.addWorldToCache(cachedWorld);
        worldFavorites.addWorldToFavorites('wrld_1', 'Favorites');
        worldFavorites.removeWorldFromFavorites('wrld_1', 'Favorites');
        worldFavorites.deleteWorldFavoriteGroup('Old Name');
        worldFavorites.removeWorldFromCache('wrld_1');

        expect(mocks.upsertCachedWorld).toHaveBeenCalledWith(cachedWorld);
        expect(mocks.addWorldToFavorites).toHaveBeenCalledWith('wrld_1', 'Favorites');
        expect(mocks.removeWorldFromFavorites).toHaveBeenCalledWith('wrld_1', 'Favorites');
        expect(mocks.deleteWorldFavoriteGroup).toHaveBeenCalledWith('Old Name');
        expect(mocks.removeCachedWorld).toHaveBeenCalledWith('wrld_1');
    });
});
