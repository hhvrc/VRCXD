import { beforeEach, describe, expect, test, vi } from 'vitest';

const mocks = vi.hoisted(() => ({
    getFriendPresenceSlice: vi.fn(),
    getFriendPresenceAfter: vi.fn(),
    getCurrentUserLocationSlice: vi.fn(),
    getCurrentUserLocationAfter: vi.fn(),
    getActivitySyncState: vi.fn(),
    upsertActivitySyncState: vi.fn(),
    getActivitySessions: vi.fn(),
    replaceActivitySessions: vi.fn(),
    appendActivitySessions: vi.fn(),
    getActivityBucketCache: vi.fn(),
    upsertActivityBucketCache: vi.fn()
}));

vi.mock('../../grpcClient.js', () => ({ default: mocks }));

import { activityV2 } from '../activityV2.js';

describe('activityV2', () => {
    beforeEach(() => {
        for (const mock of Object.values(mocks)) {
            mock.mockReset();
        }
    });

    test('getActivitySourceSliceV2 routes self to the location RPC, others to the presence RPC', async () => {
        mocks.getCurrentUserLocationSlice.mockResolvedValue([{ created_at: '2026-01-01', time: 0 }]);
        mocks.getFriendPresenceSlice.mockResolvedValue([{ created_at: '2026-01-01', type: 'Online' }]);

        await activityV2.getActivitySourceSliceV2({ isSelf: true, fromDays: 7 });
        expect(mocks.getCurrentUserLocationSlice).toHaveBeenCalled();
        expect(mocks.getFriendPresenceSlice).not.toHaveBeenCalled();

        await activityV2.getActivitySourceSliceV2({ userId: 'usr_1', isSelf: false, fromDays: 7 });
        expect(mocks.getFriendPresenceSlice).toHaveBeenCalledWith('usr_1', expect.any(String), '');
    });

    test('an unreachable server reads slices/sessions as empty lists, not an error', async () => {
        mocks.getFriendPresenceSlice.mockResolvedValue(null);
        mocks.getFriendPresenceAfter.mockResolvedValue(null);
        mocks.getCurrentUserLocationSlice.mockResolvedValue(null);
        mocks.getCurrentUserLocationAfter.mockResolvedValue(null);
        mocks.getActivitySessions.mockResolvedValue(null);

        await expect(activityV2.getFriendPresenceSliceV2('usr_1', '2026-01-01')).resolves.toEqual([]);
        await expect(activityV2.getFriendPresenceAfterV2('usr_1', '2026-01-01')).resolves.toEqual([]);
        await expect(activityV2.getCurrentUserLocationSliceV2('2026-01-01')).resolves.toEqual([]);
        await expect(activityV2.getCurrentUserLocationAfterV2('2026-01-01')).resolves.toEqual([]);
        await expect(activityV2.getActivitySessionsV2('usr_1')).resolves.toEqual([]);
    });

    test('sync state and bucket cache reads pass a real answer through untouched', async () => {
        const syncState = { userId: 'usr_1', updatedAt: '2026-01-01T00:00:00.000Z' };
        mocks.getActivitySyncState.mockResolvedValue(syncState);
        await expect(activityV2.getActivitySyncStateV2('usr_1')).resolves.toBe(syncState);

        const bucketCache = { builtFromCursor: 'abc', summary: {} };
        mocks.getActivityBucketCache.mockResolvedValue(bucketCache);
        await expect(
            activityV2.getActivityBucketCacheV2({ ownerUserId: 'usr_1', rangeDays: 30, viewKind: 'activity' })
        ).resolves.toBe(bucketCache);
    });

    test('sync state and bucket cache reads collapse both "no row" and "unreachable" to null', async () => {
        mocks.getActivitySyncState.mockResolvedValue(null);
        await expect(activityV2.getActivitySyncStateV2('usr_1')).resolves.toBeNull();
        mocks.getActivitySyncState.mockResolvedValue(undefined);
        await expect(activityV2.getActivitySyncStateV2('usr_1')).resolves.toBeNull();

        mocks.getActivityBucketCache.mockResolvedValue(null);
        await expect(
            activityV2.getActivityBucketCacheV2({ ownerUserId: 'usr_1', rangeDays: 30, viewKind: 'activity' })
        ).resolves.toBeNull();
        mocks.getActivityBucketCache.mockResolvedValue(undefined);
        await expect(
            activityV2.getActivityBucketCacheV2({ ownerUserId: 'usr_1', rangeDays: 30, viewKind: 'activity' })
        ).resolves.toBeNull();
    });

    test('the writes forward their arguments as-is', () => {
        const syncEntry = { userId: 'usr_1', updatedAt: '2026-01-01T00:00:00.000Z' };
        activityV2.upsertActivitySyncStateV2(syncEntry);
        expect(mocks.upsertActivitySyncState).toHaveBeenCalledWith(syncEntry);

        const sessions = [{ start: 1, end: 2 }];
        activityV2.replaceActivitySessionsV2('usr_1', sessions);
        expect(mocks.replaceActivitySessions).toHaveBeenCalledWith('usr_1', sessions);

        activityV2.appendActivitySessionsV2({ userId: 'usr_1', sessions, replaceFromStartAt: 5 });
        expect(mocks.appendActivitySessions).toHaveBeenCalledWith('usr_1', sessions, 5);

        const bucketEntry = { ownerUserId: 'usr_1', rangeDays: 30, viewKind: 'activity' };
        activityV2.upsertActivityBucketCacheV2(bucketEntry);
        expect(mocks.upsertActivityBucketCache).toHaveBeenCalledWith(bucketEntry);
    });
});
