import { beforeEach, describe, expect, test, vi } from 'vitest';

const mocks = vi.hoisted(() => ({
    queryMemos: vi.fn(),
    setUserMemo: vi.fn(),
    deleteUserMemo: vi.fn(),
    setWorldMemo: vi.fn(),
    deleteWorldMemo: vi.fn(),
    setAvatarMemo: vi.fn(),
    deleteAvatarMemo: vi.fn(),
    addUserNote: vi.fn(),
    getAllUserNotes: vi.fn(),
    deleteUserNote: vi.fn()
}));

vi.mock('../../grpcClient.js', () => ({ default: mocks }));

import { memos } from '../memos.js';

const allMemos = {
    userMemos: [{ userId: 'usr_a', editedAt: '2026-09-18T10:00:00.000Z', memo: 'about alice' }],
    worldMemos: [{ worldId: 'wrld_1', editedAt: '2026-09-18T10:00:00.000Z', memo: 'nice world' }],
    avatarMemos: [{ avatarId: 'avtr_1', editedAt: '2026-09-18T10:00:00.000Z', memo: 'my avatar' }]
};

describe('memos', () => {
    beforeEach(() => {
        for (const mock of Object.values(mocks)) {
            mock.mockReset();
        }
    });

    test('each single read picks its row out of the full memo set', async () => {
        mocks.queryMemos.mockResolvedValue(allMemos);

        await expect(memos.getUserMemo('usr_a')).resolves.toEqual(allMemos.userMemos[0]);
        await expect(memos.getWorldMemo('wrld_1')).resolves.toEqual(allMemos.worldMemos[0]);
        await expect(memos.getAvatarMemoDB('avtr_1')).resolves.toEqual(allMemos.avatarMemos[0]);
    });

    // Callers read .memo straight off the result, so a miss has to be an
    // empty row rather than undefined.
    test('a missing memo and an unreachable server both read as an empty row', async () => {
        mocks.queryMemos.mockResolvedValue(allMemos);
        await expect(memos.getUserMemo('usr_unknown')).resolves.toEqual({ userId: '', editedAt: '', memo: '' });

        mocks.queryMemos.mockResolvedValue(null);
        await expect(memos.getUserMemo('usr_a')).resolves.toEqual({ userId: '', editedAt: '', memo: '' });
        await expect(memos.getWorldMemo('wrld_1')).resolves.toEqual({ worldId: '', editedAt: '', memo: '' });
        await expect(memos.getAvatarMemoDB('avtr_1')).resolves.toEqual({ avatarId: '', editedAt: '', memo: '' });
    });

    test('the memo list reduces to { userId, memo } and survives no server', async () => {
        mocks.queryMemos.mockResolvedValue(allMemos);
        await expect(memos.getAllUserMemos()).resolves.toEqual([{ userId: 'usr_a', memo: 'about alice' }]);

        mocks.queryMemos.mockResolvedValue(null);
        await expect(memos.getAllUserMemos()).resolves.toEqual([]);
    });

    test('memo writes forward id, timestamp and text', () => {
        memos.setUserMemo({ userId: 'usr_a', editedAt: 't', memo: 'm' });
        memos.setWorldMemo({ worldId: 'wrld_1', editedAt: 't', memo: 'm' });
        memos.setAvatarMemo({ avatarId: 'avtr_1', editedAt: 't', memo: 'm' });
        memos.deleteUserMemo('usr_a');
        memos.deleteWorldMemo('wrld_1');
        memos.deleteAvatarMemo('avtr_1');

        expect(mocks.setUserMemo).toHaveBeenCalledWith('usr_a', 't', 'm');
        expect(mocks.setWorldMemo).toHaveBeenCalledWith('wrld_1', 't', 'm');
        expect(mocks.setAvatarMemo).toHaveBeenCalledWith('avtr_1', 't', 'm');
        expect(mocks.deleteUserMemo).toHaveBeenCalledWith('usr_a');
        expect(mocks.deleteWorldMemo).toHaveBeenCalledWith('wrld_1');
        expect(mocks.deleteAvatarMemo).toHaveBeenCalledWith('avtr_1');
    });

    test('notes forward their four fields and read back as a list', async () => {
        memos.addUserNote({ userId: 'usr_a', displayName: 'Alice', note: 'n', createdAt: 't' });
        expect(mocks.addUserNote).toHaveBeenCalledWith('usr_a', 'Alice', 'n', 't');

        mocks.getAllUserNotes.mockResolvedValue(null);
        await expect(memos.getAllUserNotes()).resolves.toEqual([]);

        memos.deleteUserNote('usr_a');
        expect(mocks.deleteUserNote).toHaveBeenCalledWith('usr_a');
    });
});
