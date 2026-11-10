import { describe, expect, test } from 'vitest';

import { instanceDescription, instanceLanguageList } from '../instances/instance-details.js';

const names: Record<string, string> = { eng: 'English', jpn: 'Japanese', kor: 'Korean' };
const nameOf = (key: string, code: string) => names[key] ?? code;

describe('instanceLanguageList', () => {
    test('lists spoken languages by share and stars the creator set ones', () => {
        const list = instanceLanguageList({ languages: ['ja', 'en-US'], languagesIso639: ['jpn', 'eng'], languageRatio: { ja: 0.25, 'en-US': 0.75 }, creationLanguages: ['eng'] }, nameOf);
        expect(list).toEqual([
            { code: 'en-US', key: 'eng', name: 'English (US)', percent: 75, isCreationLanguage: true },
            { code: 'ja', key: 'jpn', name: 'Japanese', percent: 25, isCreationLanguage: false }
        ]);
    });

    test('adds creator languages nobody speaks yet, without a share, last', () => {
        const list = instanceLanguageList({ languages: ['en'], languagesIso639: ['eng'], languageRatio: { en: 1 }, creationLanguages: ['kor', 'eng'] }, nameOf);
        expect(list.map((l) => [l.key, l.percent, l.isCreationLanguage])).toEqual([
            ['eng', 100, true],
            ['kor', null, true]
        ]);
    });

    test('creator languages alone are listed when VRChat reports none spoken', () => {
        expect(instanceLanguageList({ creationLanguages: ['jpn'] }, nameOf)).toEqual([{ code: 'jpn', key: 'jpn', name: 'Japanese', percent: null, isCreationLanguage: true }]);
        expect(instanceLanguageList(undefined, nameOf)).toEqual([]);
    });
});

describe('instanceDescription', () => {
    test('shows the creator description', () => {
        expect(instanceDescription({ description: '  Chill hangout ', world: { description: 'A world' } })).toBe('Chill hangout');
    });

    test('hides one that only repeats the world description', () => {
        expect(instanceDescription({ description: 'A world', world: { description: 'A world to explore' } })).toBe('');
    });

    test('is empty without one', () => {
        expect(instanceDescription({ description: null })).toBe('');
        expect(instanceDescription(undefined)).toBe('');
    });
});
