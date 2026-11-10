import { beforeEach, describe, expect, test, vi } from 'vitest';

const mocks = vi.hoisted(() => ({
    queryFriendFavorites: vi.fn(),
    addFriendToFavorites: vi.fn(),
    removeFriendFromFavorites: vi.fn(),
    renameFriendFavoriteGroup: vi.fn(),
    deleteFriendFavoriteGroup: vi.fn()
}));

vi.mock('../../grpcClient.js', () => ({
    default: {
        queryFriendFavorites: mocks.queryFriendFavorites,
        addFriendToFavorites: mocks.addFriendToFavorites,
        removeFriendFromFavorites: mocks.removeFriendFromFavorites,
        renameFriendFavoriteGroup: mocks.renameFriendFavoriteGroup,
        deleteFriendFavoriteGroup: mocks.deleteFriendFavoriteGroup
    }
}));

import { friendFavorites } from '../friendFavorites.js';

describe('friendFavorites', () => {
    beforeEach(() => {
        for (const mock of Object.values(mocks)) {
            mock.mockReset();
        }
    });

    test('returns rows as { created_at, userId, groupName }', async () => {
        const rows = [
            { created_at: '2026-09-18T10:00:00.000Z', userId: 'usr_a', groupName: 'Favorites' },
            { created_at: '2026-09-18T11:00:00.000Z', userId: 'usr_b', groupName: 'Favorites' }
        ];
        mocks.queryFriendFavorites.mockResolvedValue(rows);

        await expect(friendFavorites.getFriendFavorites()).resolves.toEqual(rows);
    });

    test('an unreachable server reads as no favorites', async () => {
        mocks.queryFriendFavorites.mockResolvedValue(null);
        await expect(friendFavorites.getFriendFavorites()).resolves.toEqual([]);
    });

    test('add and remove forward the user and group', () => {
        friendFavorites.addFriendToLocalFavorites('usr_a', 'Favorites');
        friendFavorites.removeFriendFromLocalFavorites('usr_b', 'Group 2');

        expect(mocks.addFriendToFavorites).toHaveBeenCalledWith('usr_a', 'Favorites');
        expect(mocks.removeFriendFromFavorites).toHaveBeenCalledWith('usr_b', 'Group 2');
    });

    // This function takes (new, old) while the RPC takes (old, new) - a swap
    // that would silently rename the wrong group if it were ever dropped.
    test('renaming swaps the argument order for the RPC', () => {
        friendFavorites.renameFriendFavoriteGroup('New Name', 'Old Name');

        expect(mocks.renameFriendFavoriteGroup).toHaveBeenCalledWith('Old Name', 'New Name');
    });

    test('deleting a group forwards its name', () => {
        friendFavorites.deleteFriendFavoriteGroup('Old Name');
        expect(mocks.deleteFriendFavoriteGroup).toHaveBeenCalledWith('Old Name');
    });
});
