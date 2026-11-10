import { beforeEach, describe, expect, test, vi } from 'vitest';

const mocks = vi.hoisted(() => ({
    getModeration: vi.fn(),
    setModeration: vi.fn(),
    deleteModeration: vi.fn()
}));

vi.mock('../../grpcClient.js', () => ({
    default: {
        getModeration: mocks.getModeration,
        setModeration: mocks.setModeration,
        deleteModeration: mocks.deleteModeration
    }
}));

import { moderation } from '../moderation.js';

describe('moderation', () => {
    beforeEach(() => {
        mocks.getModeration.mockReset();
        mocks.setModeration.mockReset();
        mocks.deleteModeration.mockReset();
    });

    test('passes a stored row through as the server sent it', async () => {
        const row = {
            userId: 'usr_a',
            updatedAt: '2026-09-18T10:00:00.000Z',
            displayName: 'Alice',
            block: true,
            mute: false
        };
        mocks.getModeration.mockResolvedValue(row);

        await expect(moderation.getModeration('usr_a')).resolves.toEqual(row);
        expect(mocks.getModeration).toHaveBeenCalledWith('usr_a');
    });

    // photonModerationUpdate reads row.userId to tell "nothing recorded"
    // from a real row, so both cases have to be an object, never null.
    test('an unknown user and an unreachable server both read as no row', async () => {
        mocks.getModeration.mockResolvedValue({});
        await expect(moderation.getModeration('usr_unknown')).resolves.toEqual({});

        mocks.getModeration.mockResolvedValue(null);
        await expect(moderation.getModeration('usr_a')).resolves.toEqual({});
    });

    test('writes go to the server with block/mute forced to booleans', () => {
        moderation.setModeration({
            userId: 'usr_a',
            updatedAt: '2026-09-18T10:00:00.000Z',
            displayName: 'Alice',
            block: 1,
            mute: undefined
        });

        expect(mocks.setModeration).toHaveBeenCalledWith('usr_a', '2026-09-18T10:00:00.000Z', 'Alice', true, false);
    });

    test('deleting forwards the user id', () => {
        moderation.deleteModeration('usr_a');
        expect(mocks.deleteModeration).toHaveBeenCalledWith('usr_a');
    });
});
