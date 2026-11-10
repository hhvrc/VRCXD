import { beforeEach, describe, expect, test, vi } from 'vitest';

const mocks = vi.hoisted(() => ({
    getMutualGraphSnapshot: vi.fn(),
    saveMutualGraphSnapshot: vi.fn(),
    updateMutualsForFriend: vi.fn(),
    getMutualCountForAllUsers: vi.fn(),
    upsertMutualGraphMeta: vi.fn(),
    bulkUpsertMutualGraphMeta: vi.fn(),
    getMutualGraphMeta: vi.fn()
}));

vi.mock('../../grpcClient.js', () => ({ default: mocks }));

import { mutualGraph } from '../mutualGraph.js';

describe('mutualGraph', () => {
    beforeEach(() => {
        for (const mock of Object.values(mocks)) {
            mock.mockReset();
        }
    });

    test('the snapshot is a Map, friends with no links included', async () => {
        mocks.getMutualGraphSnapshot.mockResolvedValue(
            new Map([
                ['usr_a', ['usr_x', 'usr_y']],
                ['usr_b', []]
            ])
        );

        const snapshot = await mutualGraph.getMutualGraphSnapshot();

        expect(snapshot.get('usr_a')).toEqual(['usr_x', 'usr_y']);
        expect(snapshot.get('usr_b')).toEqual([]);
    });

    test('every read survives an unreachable server as an empty Map', async () => {
        mocks.getMutualGraphSnapshot.mockResolvedValue(null);
        mocks.getMutualCountForAllUsers.mockResolvedValue(null);
        mocks.getMutualGraphMeta.mockResolvedValue(null);

        for (const read of ['getMutualGraphSnapshot', 'getMutualCountForAllUsers', 'getMutualGraphMeta']) {
            const value = await mutualGraph[read]();
            expect(value).toBeInstanceOf(Map);
            expect(value.size).toBe(0);
        }
    });

    // saveMutualGraphSnapshot used to accept anything and quietly treat a
    // non-Map as empty; that guard is worth keeping, since an empty snapshot
    // clears every non-opted-out friend's links server-side.
    test('a non-Map snapshot is sent as an empty Map, not passed through', () => {
        mutualGraph.saveMutualGraphSnapshot(undefined);

        const sent = mocks.saveMutualGraphSnapshot.mock.calls[0][0];
        expect(sent).toBeInstanceOf(Map);
        expect(sent.size).toBe(0);
    });

    test('writes without a friend id never reach the server', async () => {
        await mutualGraph.updateMutualsForFriend('', ['usr_x']);
        await mutualGraph.upsertMutualGraphMeta('', { optedOut: true });
        await mutualGraph.bulkUpsertMutualGraphMeta(new Map());

        expect(mocks.updateMutualsForFriend).not.toHaveBeenCalled();
        expect(mocks.upsertMutualGraphMeta).not.toHaveBeenCalled();
        expect(mocks.bulkUpsertMutualGraphMeta).not.toHaveBeenCalled();
    });

    test('meta writes carry a timestamp even when the caller omits one', () => {
        mutualGraph.upsertMutualGraphMeta('usr_a', { lastFetchedAt: '2026-09-18T10:00:00.000Z', optedOut: 1 });
        expect(mocks.upsertMutualGraphMeta).toHaveBeenCalledWith('usr_a', '2026-09-18T10:00:00.000Z', true);

        mutualGraph.upsertMutualGraphMeta('usr_b', { optedOut: false });
        const [, stamped, optedOut] = mocks.upsertMutualGraphMeta.mock.calls[1];
        expect(optedOut).toBe(false);
        expect(Number.isNaN(Date.parse(stamped))).toBe(false);
    });
});
