import { beforeEach, describe, expect, test, vi } from 'vitest';

const mocks = vi.hoisted(() => ({
    getFriendLogCurrent: vi.fn(),
    setFriendLogCurrent: vi.fn(),
    deleteFriendLogCurrent: vi.fn()
}));

vi.mock('../../grpcClient.js', () => ({
    default: {
        getFriendLogCurrent: mocks.getFriendLogCurrent,
        setFriendLogCurrent: mocks.setFriendLogCurrent,
        deleteFriendLogCurrent: mocks.deleteFriendLogCurrent
    }
}));

import { friendLogCurrent } from '../friendLogCurrent.js';

describe('friendLogCurrent', () => {
    beforeEach(() => {
        mocks.getFriendLogCurrent.mockReset();
        mocks.setFriendLogCurrent.mockReset();
        mocks.deleteFriendLogCurrent.mockReset();
    });

    test('returns the rows the server holds, friend numbers included', async () => {
        const rows = [
            { userId: 'usr_a', displayName: 'Alice', trustLevel: 'Known User', friendNumber: 7 },
            { userId: 'usr_b', displayName: 'Bob', trustLevel: 'User', friendNumber: 8 }
        ];
        mocks.getFriendLogCurrent.mockResolvedValue(rows);

        await expect(friendLogCurrent.getFriendLogCurrent()).resolves.toEqual(rows);
    });

    // getFriendLog() iterates the result immediately, so an unreachable
    // server has to be an empty list rather than null.
    test('an unreachable server reads as an empty list', async () => {
        mocks.getFriendLogCurrent.mockResolvedValue(null);
        await expect(friendLogCurrent.getFriendLogCurrent()).resolves.toEqual([]);
    });

    test('a single row is sent as a one-entry list', () => {
        const entry = { userId: 'usr_a', displayName: 'Alice', trustLevel: 'User', friendNumber: 1 };
        friendLogCurrent.setFriendLogCurrent(entry);

        expect(mocks.setFriendLogCurrent).toHaveBeenCalledWith([entry]);
    });

    test('a whole list is one call, and an empty one is no call at all', async () => {
        const rows = [
            { userId: 'usr_a', displayName: 'Alice', trustLevel: 'User', friendNumber: 1 },
            { userId: 'usr_b', displayName: 'Bob', trustLevel: 'User', friendNumber: 2 }
        ];
        friendLogCurrent.setFriendLogCurrentArray(rows);
        expect(mocks.setFriendLogCurrent).toHaveBeenCalledWith(rows);

        mocks.setFriendLogCurrent.mockClear();
        await friendLogCurrent.setFriendLogCurrentArray([]);
        expect(mocks.setFriendLogCurrent).not.toHaveBeenCalled();
    });

    test('deleting forwards the user id', () => {
        friendLogCurrent.deleteFriendLogCurrent('usr_a');
        expect(mocks.deleteFriendLogCurrent).toHaveBeenCalledWith('usr_a');
    });
});
