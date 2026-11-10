import { describe, expect, test } from 'vitest';

import { createInstanceBody, DEFAULT_INSTANCE_CHOICES, quickInstanceChoices, usableCategories, usableVibes, type InstanceChoices } from '../instances/instance-create.js';

const choices = (over: Partial<InstanceChoices>): InstanceChoices => ({ ...DEFAULT_INSTANCE_CHOICES, ...over });

describe('createInstanceBody', () => {
    test('sends the new instance fields when they are set', () => {
        const body = createInstanceBody('wrld_a', choices({ accessType: 'friends+', region: 'Europe', displayName: 'Room', description: 'Chill', categoryId: 'icat_a', languages: ['eng', 'jpn'], vibeIds: ['ivib_a'] }));
        expect(body).toMatchObject({ worldId: 'wrld_a', type: 'hidden', region: 'eu', displayName: 'Room', description: 'Chill', categoryId: 'icat_a', languages: ['eng', 'jpn'], vibeIds: ['ivib_a'] });
    });

    test('leaves unset fields empty', () => {
        const body = createInstanceBody('wrld_a', choices({}));
        expect(body).toMatchObject({ type: 'public', displayName: null, description: null, categoryId: null, languages: [], vibeIds: [] });
    });

    test('keeps at most three languages and vibes', () => {
        const body = createInstanceBody('wrld_a', choices({ languages: ['a', 'b', 'c', 'd'], vibeIds: ['1', '2', '3', '4'] }));
        expect(body.languages).toEqual(['a', 'b', 'c']);
        expect(body.vibeIds).toEqual(['1', '2', '3']);
    });

    test('sends the minimum avatar performance for any instance, None as empty', () => {
        expect(createInstanceBody('wrld_a', choices({ accessType: 'invite', minimumAvatarPerformance: 'Good' })).minimumAvatarPerformance).toBe('Good');
        expect(createInstanceBody('wrld_a', choices({ minimumAvatarPerformance: 'None' })).minimumAvatarPerformance).toBe('');
    });

    test('a group instance is age gated only with the permission', () => {
        expect(createInstanceBody('wrld_a', choices({ accessType: 'group', ageGate: true })).ageGate).toBe(false);
        expect(createInstanceBody('wrld_a', choices({ accessType: 'group', ageGate: true }), { canAgeGate: true }).ageGate).toBe(true);
    });

    test('any other instance asks for the age gate, which the server checks', () => {
        expect(createInstanceBody('wrld_a', choices({ accessType: 'friends', ageGate: true })).ageGate).toBe(true);
        expect(createInstanceBody('wrld_a', choices({ accessType: 'friends', ageGate: true }), { canAgeGate: false }).ageGate).toBe(false);
    });
});

describe('quickInstanceChoices', () => {
    test('keeps only what the original reads without the dialog', () => {
        const quick = quickInstanceChoices(choices({ accessType: 'group', groupId: 'grp_a', ageGate: true, displayName: 'Room', description: 'd', categoryId: 'c', languages: ['eng'], vibeIds: ['v'], minimumAvatarPerformance: 'Good' }));
        expect(quick).toMatchObject({ accessType: 'group', groupId: 'grp_a', ageGate: true, displayName: '', description: '', categoryId: '', languages: [], vibeIds: [], minimumAvatarPerformance: '' });
    });
});

describe('categories and vibes', () => {
    test('drops deleted ones and orders categories', () => {
        expect(usableCategories([{ id: 'b', name: 'B', order: 2 }, { id: 'x', name: 'X', order: 0, deleted: true }, { id: 'a', name: 'A', order: 1 }]).map((c) => c.id)).toEqual(['a', 'b']);
        expect(usableVibes([{ id: 'a', title: 'A' }, { id: 'b', title: 'B', deleted: true }]).map((v) => v.id)).toEqual(['a']);
        expect(usableCategories(null)).toEqual([]);
    });
});
