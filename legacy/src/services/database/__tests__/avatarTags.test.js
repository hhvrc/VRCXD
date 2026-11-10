import { beforeEach, describe, expect, test, vi } from 'vitest';

const mocks = vi.hoisted(() => ({
    getAvatarTags: vi.fn(),
    getAllAvatarTags: vi.fn(),
    getAllDistinctTags: vi.fn(),
    addAvatarTag: vi.fn(),
    updateAvatarTagColor: vi.fn(),
    removeAvatarTag: vi.fn(),
    removeAllAvatarTags: vi.fn()
}));

vi.mock('../../grpcClient.js', () => ({ default: mocks }));

import { avatarTags } from '../avatarTags.js';

describe('avatarTags', () => {
    beforeEach(() => {
        for (const mock of Object.values(mocks)) {
            mock.mockReset();
        }
    });

    test('tags come back as { tag, color }, colour null when unset', async () => {
        mocks.getAvatarTags.mockResolvedValue([
            { tag: 'dance', color: 'blue' },
            { tag: 'wip', color: null }
        ]);

        await expect(avatarTags.getAvatarTags('avtr_1')).resolves.toEqual([
            { tag: 'dance', color: 'blue' },
            { tag: 'wip', color: null }
        ]);
        expect(mocks.getAvatarTags).toHaveBeenCalledWith('avtr_1');
    });

    test('the all-tags read is a Map keyed by avatar id', async () => {
        mocks.getAllAvatarTags.mockResolvedValue([
            { avatarId: 'avtr_1', tags: [{ tag: 'wip', color: null }] },
            { avatarId: 'avtr_2', tags: [{ tag: 'dance', color: 'blue' }] }
        ]);

        const map = await avatarTags.getAllAvatarTags();

        expect(map).toBeInstanceOf(Map);
        expect(map.get('avtr_1')).toEqual([{ tag: 'wip', color: null }]);
        expect(map.get('avtr_2')).toEqual([{ tag: 'dance', color: 'blue' }]);
    });

    // MyAvatars.vue does tagsMap.get(id) || [] on the result, so an
    // unreachable server has to be an empty Map, not null.
    test('an unreachable server reads as empty collections', async () => {
        mocks.getAvatarTags.mockResolvedValue(null);
        mocks.getAllAvatarTags.mockResolvedValue(null);
        mocks.getAllDistinctTags.mockResolvedValue(null);

        await expect(avatarTags.getAvatarTags('avtr_1')).resolves.toEqual([]);
        await expect(avatarTags.getAllDistinctTags()).resolves.toEqual([]);
        const map = await avatarTags.getAllAvatarTags();
        expect(map).toBeInstanceOf(Map);
        expect(map.size).toBe(0);
    });

    test('writes forward their arguments, with colour defaulting to null', () => {
        avatarTags.addAvatarTag('avtr_1', 'wip');
        avatarTags.updateAvatarTagColor('avtr_1', 'wip', 'red');
        avatarTags.removeAvatarTag('avtr_1', 'wip');
        avatarTags.removeAllAvatarTags('avtr_1');

        expect(mocks.addAvatarTag).toHaveBeenCalledWith('avtr_1', 'wip', null);
        expect(mocks.updateAvatarTagColor).toHaveBeenCalledWith('avtr_1', 'wip', 'red');
        expect(mocks.removeAvatarTag).toHaveBeenCalledWith('avtr_1', 'wip');
        expect(mocks.removeAllAvatarTags).toHaveBeenCalledWith('avtr_1');
    });
});
