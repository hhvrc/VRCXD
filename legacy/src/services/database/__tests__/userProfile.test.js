import { beforeEach, describe, expect, test, vi } from 'vitest';

const mocks = vi.hoisted(() => ({
    getUserProfile: vi.fn(),
    getAllUserProfiles: vi.fn(),
    setUserProfile: vi.fn(),
    deleteUserProfile: vi.fn()
}));

vi.mock('../../grpcClient.js', () => ({
    default: {
        getUserProfile: mocks.getUserProfile,
        getAllUserProfiles: mocks.getAllUserProfiles,
        setUserProfile: mocks.setUserProfile,
        deleteUserProfile: mocks.deleteUserProfile
    }
}));

import { parseBioLinks, userProfile } from '../userProfile.js';

describe('userProfile', () => {
    beforeEach(() => {
        mocks.getUserProfile.mockReset();
        mocks.getAllUserProfiles.mockReset();
        mocks.setUserProfile.mockReset();
        mocks.deleteUserProfile.mockReset();
    });

    test('passes a stored profile through as the server sent it', async () => {
        const row = {
            userId: 'usr_a',
            bio: 'hello',
            bioLinks: ['https://a.example'],
            updatedAt: '2026-10-31T10:00:00.000Z'
        };
        mocks.getUserProfile.mockResolvedValue(row);

        await expect(userProfile.getUserProfile('usr_a')).resolves.toEqual(row);
        expect(mocks.getUserProfile).toHaveBeenCalledWith('usr_a');
    });

    // updateUserProfile treats null as "never seen" and stores the profile
    // without logging a feed entry - the right call for an unreachable
    // server too, since there is no previous bio to compare against.
    test('an unknown user, an empty id and an unreachable server all read as null', async () => {
        mocks.getUserProfile.mockResolvedValue(null);
        await expect(userProfile.getUserProfile('usr_unknown')).resolves.toBeNull();

        await expect(userProfile.getUserProfile('')).resolves.toBeNull();
        expect(mocks.getUserProfile).toHaveBeenCalledTimes(1);
    });

    test('an unreachable server reads every profile as an empty list', async () => {
        mocks.getAllUserProfiles.mockResolvedValue(null);
        await expect(userProfile.getAllUserProfiles()).resolves.toEqual([]);

        const rows = [{ userId: 'usr_a', bio: '', bioLinks: [], updatedAt: '' }];
        mocks.getAllUserProfiles.mockResolvedValue(rows);
        await expect(userProfile.getAllUserProfiles()).resolves.toBe(rows);
    });

    test('writes send the user ref fields the local INSERT used, normalised', () => {
        userProfile.setUserProfile({ id: 'usr_a', bio: 'hello', bioLinks: ['https://a.example'] });
        expect(mocks.setUserProfile).toHaveBeenCalledWith('usr_a', 'hello', ['https://a.example']);

        userProfile.setUserProfile({ id: 'usr_b', bio: undefined, bioLinks: undefined });
        expect(mocks.setUserProfile).toHaveBeenCalledWith('usr_b', '', []);
    });

    test('a ref without an id is not written', async () => {
        await expect(userProfile.setUserProfile({ bio: 'x' })).resolves.toBe(false);
        expect(mocks.setUserProfile).not.toHaveBeenCalled();
    });

    test('deletes forward the user id', () => {
        userProfile.deleteUserProfile('usr_a');
        expect(mocks.deleteUserProfile).toHaveBeenCalledWith('usr_a');
    });

    // Still used by feed.js for rows read out of the local feed_bio table.
    test('parseBioLinks tolerates anything that is not a JSON array', () => {
        expect(parseBioLinks('["https://a.example"]')).toEqual(['https://a.example']);
        expect(parseBioLinks('{"a":1}')).toEqual([]);
        expect(parseBioLinks('not json')).toEqual([]);
        expect(parseBioLinks(undefined)).toEqual([]);
    });
});
