import { beforeEach, describe, expect, test, vi } from 'vitest';

const mocks = vi.hoisted(() => ({
    getFriendLogHistory: vi.fn(),
    addFriendLogHistory: vi.fn(),
    deleteFriendLogHistory: vi.fn()
}));

vi.mock('../../grpcClient.js', () => ({
    default: {
        getFriendLogHistory: mocks.getFriendLogHistory,
        addFriendLogHistory: mocks.addFriendLogHistory,
        deleteFriendLogHistory: mocks.deleteFriendLogHistory
    }
}));

import { friendLogHistory } from '../friendLogHistory.js';

const oldest = { rowId: 1, created_at: '2026-01-01T00:00:00.000Z', type: 'Friend', userId: 'usr_a' };
const newest = { rowId: 2, created_at: '2026-02-01T00:00:00.000Z', type: 'Unfriend', userId: 'usr_a' };

describe('friendLogHistory', () => {
    beforeEach(() => {
        for (const mock of Object.values(mocks)) {
            mock.mockReset();
        }
    });

    // The two reads want opposite orders, and one RPC serves both: it returns
    // insertion order, so only the unfiltered read reverses.
    test('the unfiltered read is newest-first', async () => {
        mocks.getFriendLogHistory.mockResolvedValue([oldest, newest]);

        await expect(friendLogHistory.getFriendLogHistory()).resolves.toEqual([newest, oldest]);
        expect(mocks.getFriendLogHistory).toHaveBeenCalledWith('', []);
    });

    test('the per-user read keeps insertion order and passes its filters', async () => {
        mocks.getFriendLogHistory.mockResolvedValue([oldest, newest]);

        await expect(friendLogHistory.getFriendLogHistoryForUserId('usr_a', ['Friend', 'Unfriend'])).resolves.toEqual([
            oldest,
            newest
        ]);
        expect(mocks.getFriendLogHistory).toHaveBeenCalledWith('usr_a', ['Friend', 'Unfriend']);
    });

    test('missing types become an empty filter, not undefined', async () => {
        mocks.getFriendLogHistory.mockResolvedValue([]);
        await friendLogHistory.getFriendLogHistoryForUserId('usr_a');
        expect(mocks.getFriendLogHistory).toHaveBeenCalledWith('usr_a', []);
    });

    test('both reads survive an unreachable server as empty lists', async () => {
        mocks.getFriendLogHistory.mockResolvedValue(null);

        await expect(friendLogHistory.getFriendLogHistory()).resolves.toEqual([]);
        await expect(friendLogHistory.getFriendLogHistoryForUserId('usr_a', [])).resolves.toEqual([]);
    });

    test('one entry is sent as a one-entry list, an empty array not at all', async () => {
        friendLogHistory.addFriendLogHistory(newest);
        expect(mocks.addFriendLogHistory).toHaveBeenCalledWith([newest]);

        mocks.addFriendLogHistory.mockClear();
        await friendLogHistory.addFriendLogHistoryArray([]);
        expect(mocks.addFriendLogHistory).not.toHaveBeenCalled();
    });

    test('deleting forwards the whole entry, rowId or not', () => {
        friendLogHistory.deleteFriendLogHistory(newest);
        expect(mocks.deleteFriendLogHistory).toHaveBeenCalledWith(newest);
    });
});
