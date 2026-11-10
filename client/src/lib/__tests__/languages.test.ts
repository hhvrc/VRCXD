import { describe, expect, test } from 'vitest';

import { appearance } from '../settings/appearance.svelte.js';
import { languageCodes, languageName, resolveSystemLanguage } from '../settings/languages.js';
import { m } from '../paraglide/messages.js';
import { getLocale, locales, setLocale } from '../paraglide/runtime.js';

describe('resolveSystemLanguage', () => {
    describe('returns null for invalid input', () => {
        test('empty string', () => {
            expect(resolveSystemLanguage('', languageCodes)).toBeNull();
        });

        test('null', () => {
            expect(resolveSystemLanguage(null, languageCodes)).toBeNull();
        });

        test('undefined', () => {
            expect(resolveSystemLanguage(undefined, languageCodes)).toBeNull();
        });

        test('unsupported language', () => {
            expect(resolveSystemLanguage('de-DE', languageCodes)).toBeNull();
        });
    });

    describe('exact match', () => {
        test('zh-CN matches zh-CN', () => {
            expect(resolveSystemLanguage('zh-CN', languageCodes)).toBe('zh-CN');
        });

        test('zh-TW matches zh-TW', () => {
            expect(resolveSystemLanguage('zh-TW', languageCodes)).toBe('zh-TW');
        });

        test('en matches en', () => {
            expect(resolveSystemLanguage('en', languageCodes)).toBe('en');
        });

        test('hu matches hu', () => {
            expect(resolveSystemLanguage('hu', languageCodes)).toBe('hu');
        });

        test.each(languageCodes)('%s resolves to itself', (code) => {
            expect(resolveSystemLanguage(code, languageCodes)).toBe(code);
        });
    });

    describe('prefix match', () => {
        test('ja-JP matches ja', () => {
            expect(resolveSystemLanguage('ja-JP', languageCodes)).toBe('ja');
        });

        test('ko-KR matches ko', () => {
            expect(resolveSystemLanguage('ko-KR', languageCodes)).toBe('ko');
        });

        test('fr-FR matches fr', () => {
            expect(resolveSystemLanguage('fr-FR', languageCodes)).toBe('fr');
        });

        test('en-US matches en', () => {
            expect(resolveSystemLanguage('en-US', languageCodes)).toBe('en');
        });

        test('es-MX matches es', () => {
            expect(resolveSystemLanguage('es-MX', languageCodes)).toBe('es');
        });

        test('pt-BR matches pt', () => {
            expect(resolveSystemLanguage('pt-BR', languageCodes)).toBe('pt');
        });

        test('ru-RU matches ru', () => {
            expect(resolveSystemLanguage('ru-RU', languageCodes)).toBe('ru');
        });

        test('hu-HU matches hu', () => {
            expect(resolveSystemLanguage('hu-HU', languageCodes)).toBe('hu');
        });

        test('hu-Latn-HU matches hu (script + region)', () => {
            expect(resolveSystemLanguage('hu-Latn-HU', languageCodes)).toBe('hu');
        });
    });

    describe('Chinese region-aware mapping', () => {
        test('zh-HK maps to zh-TW (traditional)', () => {
            expect(resolveSystemLanguage('zh-HK', languageCodes)).toBe('zh-TW');
        });

        test('zh-MO maps to zh-TW (traditional)', () => {
            expect(resolveSystemLanguage('zh-MO', languageCodes)).toBe('zh-TW');
        });

        test('zh-SG maps to zh-CN (simplified)', () => {
            expect(resolveSystemLanguage('zh-SG', languageCodes)).toBe('zh-CN');
        });

        test('bare zh maps to zh-CN', () => {
            expect(resolveSystemLanguage('zh', languageCodes)).toBe('zh-CN');
        });

        test('zh-Hant maps to zh-TW (traditional script tag)', () => {
            expect(resolveSystemLanguage('zh-Hant', languageCodes)).toBe('zh-TW');
        });

        test('zh-Hans maps to zh-CN (simplified script tag)', () => {
            expect(resolveSystemLanguage('zh-Hans', languageCodes)).toBe('zh-CN');
        });

        test('zh-Hant-HK maps to zh-TW (script + region)', () => {
            expect(resolveSystemLanguage('zh-Hant-HK', languageCodes)).toBe('zh-TW');
        });

        test('zh-Hans-CN maps to zh-CN (script + region)', () => {
            expect(resolveSystemLanguage('zh-Hans-CN', languageCodes)).toBe('zh-CN');
        });

        test('zh-Hant-MO maps to zh-TW (script + traditional region)', () => {
            expect(resolveSystemLanguage('zh-Hant-MO', languageCodes)).toBe('zh-TW');
        });
    });
});

describe('messages', () => {
	test('every language code is a compiled locale', () => {
		expect([...locales].sort()).toEqual([...languageCodes].sort());
		expect(languageName('en')).toBe('English (en)');
		expect(languageName('xx')).toBe('xx');
	});

	test('reads English and fills params', () => {
		expect(m.dialog_user_info_booping({}, { locale: 'en' })).toBe('Booping');
		expect(m.notifications_avatar_change({ avatar: 'Fox' }, { locale: 'en' })).toBe('changed into avatar Fox');
	});

	test('a locale missing a message falls back to English', () => {
		// cs.json has no common.no_data.
		expect(m.common_no_data({}, { locale: 'cs' })).toBe('No data');
	});

	test('the locale is the appLanguage setting, switched without a reload', () => {
		appearance.appLanguage.value = 'en';
		expect(getLocale()).toBe('en');
		expect(m.dialog_user_info_booping()).toBe('Booping');
		setLocale('hu', { reload: false });
		expect(appearance.appLanguage.value).toBe('hu');
		expect(m.dialog_user_info_booping()).toBe('Boopolás');
		appearance.appLanguage.value = 'ja';
		expect(getLocale()).toBe('ja');
		appearance.appLanguage.value = 'en';
	});
});
