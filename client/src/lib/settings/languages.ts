import { m } from '#lib/paraglide/messages.js';
import type { Locale } from '#lib/paraglide/runtime.js';

// The UI languages: one inlang message file per locale under ./localization, compiled by Paraglide
// (project.inlang) into the m.* functions in #lib/paraglide. The active locale is the
// appearance.appLanguage setting, see the custom strategy in appearance.svelte.ts.
export const languageCodes = ['cs', 'en', 'es', 'fr', 'hu', 'ja', 'ko', 'pl', 'pt', 'ru', 'th', 'vi', 'zh-CN', 'zh-TW'] as const satisfies readonly Locale[];

export type LanguageCode = (typeof languageCodes)[number];

/** Narrows a stored value to one of the supported codes. */
export function isLanguageCode(value: unknown): value is LanguageCode {
	return typeof value === 'string' && (languageCodes as readonly string[]).includes(value);
}

/** "English (en)" and the like: each locale's own `language` message. */
export function languageName(code: string): string {
	return isLanguageCode(code) ? m.language({}, { locale: code }) : code;
}

/** The original's resolveSystemLanguage: a BCP-47 tag to one of the supported codes, or null. */
export function resolveSystemLanguage(systemLanguage: string | undefined | null, codes: readonly string[] = languageCodes): string | null {
	if (!systemLanguage) return null;
	if (codes.includes(systemLanguage)) return systemLanguage;
	const [lang, ...parts] = systemLanguage.split('-');
	if (lang === 'zh') {
		if (parts.includes('Hant') || parts.some((p) => ['TW', 'HK', 'MO'].includes(p))) return codes.includes('zh-TW') ? 'zh-TW' : null;
		return codes.includes('zh-CN') ? 'zh-CN' : null;
	}
	return codes.find((code) => code.split('-')[0] === lang) ?? null;
}
