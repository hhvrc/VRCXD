import { beforeEach, describe, expect, test, vi } from 'vitest';

const mocks = vi.hoisted(() => ({
    getTableSizes: vi.fn(),
    getMaxFriendNumber: vi.fn()
}));

vi.mock('../../grpcClient.js', () => ({
    default: {
        getTableSizes: mocks.getTableSizes,
        getMaxFriendNumber: mocks.getMaxFriendNumber
    }
}));

import { tableSize } from '../tableSize.js';

describe('tableSize', () => {
    beforeEach(() => {
        mocks.getTableSizes.mockReset();
        mocks.getMaxFriendNumber.mockReset();
    });

    test('passes the counts through under the keys the panel reads', async () => {
        const counts = { gps: 4, status: 3, bio: 0, joinLeave: 120, event: 7 };
        mocks.getTableSizes.mockResolvedValue(counts);

        await expect(tableSize.getTableSizes()).resolves.toEqual(counts);
        // One call for the whole panel, not one per row.
        expect(mocks.getTableSizes).toHaveBeenCalledTimes(1);
    });

    test('an unreachable server reads as no counts, not null', async () => {
        mocks.getTableSizes.mockResolvedValue(null);
        await expect(tableSize.getTableSizes()).resolves.toEqual({});
    });

    // The caller raises its own counter to meet this, so 0 is the floor -
    // exactly what SELECT MAX returned over an empty table.
    test('the friend-number seed falls back to zero', async () => {
        mocks.getMaxFriendNumber.mockResolvedValue(42);
        await expect(tableSize.getMaxFriendLogNumber()).resolves.toBe(42);

        mocks.getMaxFriendNumber.mockResolvedValue(null);
        await expect(tableSize.getMaxFriendLogNumber()).resolves.toBe(0);
    });
});
