import { beforeEach, describe, expect, test, vi } from 'vitest';

const mocks = vi.hoisted(() => ({
    upsertCachedAvatar: vi.fn(),
    addAvatarToHistory: vi.fn(),
    getAvatarTimeSpent: vi.fn(),
    getAllAvatarTimeSpent: vi.fn(),
    addAvatarTimeSpent: vi.fn(),
    getAvatarHistory: vi.fn(),
    getCachedAvatar: vi.fn(),
    clearAvatarHistory: vi.fn(),
    addAvatarToFavorites: vi.fn(),
    renameAvatarFavoriteGroup: vi.fn(),
    deleteAvatarFavoriteGroup: vi.fn(),
    removeAvatarFromFavorites: vi.fn(),
    queryAvatarFavorites: vi.fn(),
    removeCachedAvatar: vi.fn(),
    listCachedAvatars: vi.fn()
}));

vi.mock('../../grpcClient.js', () => ({ default: mocks }));

import { avatarFavorites } from '../avatarFavorites.js';

describe('avatarFavorites', () => {
    beforeEach(() => {
        for (const mock of Object.values(mocks)) {
            mock.mockReset();
        }
    });

    test('time spent is a map of milliseconds, missing values counted as 0', async () => {
        mocks.getAllAvatarTimeSpent.mockResolvedValue([
            ['avtr_1', 1000],
            ['avtr_2', null]
        ]);

        const map = await avatarFavorites.getAllAvatarTimeSpent();

        expect(map.get('avtr_1')).toBe(1000);
        expect(map.get('avtr_2')).toBe(0);
    });

    test('a single time-spent read falls back to zero for this avatar', async () => {
        mocks.getAvatarTimeSpent.mockResolvedValue(null);

        await expect(avatarFavorites.getAvatarTimeSpent('avtr_1')).resolves.toEqual({
            timeSpent: 0,
            avatarId: 'avtr_1'
        });
    });

    test('history passes the current user and limit through', async () => {
        mocks.getAvatarHistory.mockResolvedValue([{ id: 'avtr_1', name: 'One' }]);

        await expect(avatarFavorites.getAvatarHistory('usr_me')).resolves.toEqual([{ id: 'avtr_1', name: 'One' }]);
        expect(mocks.getAvatarHistory).toHaveBeenCalledWith('usr_me', 100);

        await avatarFavorites.getAvatarHistory('usr_me', 25);
        expect(mocks.getAvatarHistory).toHaveBeenLastCalledWith('usr_me', 25);
    });

    test('lists and the cache lookup survive an unreachable server', async () => {
        mocks.getAvatarHistory.mockResolvedValue(null);
        mocks.queryAvatarFavorites.mockResolvedValue(null);
        mocks.listCachedAvatars.mockResolvedValue(null);
        mocks.getAllAvatarTimeSpent.mockResolvedValue(null);
        mocks.getCachedAvatar.mockResolvedValue(undefined);

        await expect(avatarFavorites.getAvatarHistory('usr_me')).resolves.toEqual([]);
        await expect(avatarFavorites.getAvatarFavorites()).resolves.toEqual([]);
        await expect(avatarFavorites.getAvatarCache()).resolves.toEqual([]);
        await expect(avatarFavorites.getCachedAvatarById('avtr_x')).resolves.toBeNull();
        expect((await avatarFavorites.getAllAvatarTimeSpent()).size).toBe(0);
    });

    test('renaming swaps the argument order for the RPC', () => {
        avatarFavorites.renameAvatarFavoriteGroup('New Name', 'Old Name');
        expect(mocks.renameAvatarFavoriteGroup).toHaveBeenCalledWith('Old Name', 'New Name');
    });

    test('the remaining writes forward their arguments', () => {
        const entry = { id: 'avtr_1', name: 'One' };
        avatarFavorites.addAvatarToCache(entry);
        avatarFavorites.addAvatarToHistory('avtr_1');
        avatarFavorites.addAvatarTimeSpent('avtr_1', 500);
        avatarFavorites.addAvatarToFavorites('avtr_1', 'Favorites');
        avatarFavorites.removeAvatarFromFavorites('avtr_1', 'Favorites');
        avatarFavorites.deleteAvatarFavoriteGroup('Old Name');
        avatarFavorites.removeAvatarFromCache('avtr_1');
        avatarFavorites.clearAvatarHistory();

        expect(mocks.upsertCachedAvatar).toHaveBeenCalledWith(entry);
        expect(mocks.addAvatarToHistory).toHaveBeenCalledWith('avtr_1');
        expect(mocks.addAvatarTimeSpent).toHaveBeenCalledWith('avtr_1', 500);
        expect(mocks.addAvatarToFavorites).toHaveBeenCalledWith('avtr_1', 'Favorites');
        expect(mocks.removeAvatarFromFavorites).toHaveBeenCalledWith('avtr_1', 'Favorites');
        expect(mocks.deleteAvatarFavoriteGroup).toHaveBeenCalledWith('Old Name');
        expect(mocks.removeCachedAvatar).toHaveBeenCalledWith('avtr_1');
        expect(mocks.clearAvatarHistory).toHaveBeenCalled();
    });
});
