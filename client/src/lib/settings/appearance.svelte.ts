import { browser } from '$app/env';
import { toast } from 'svelte-sonner';
import { isLanguageCode, type LanguageCode } from '#lib/settings/languages.js';
import { defineCustomClientStrategy } from '#lib/paraglide/runtime.js';
import { readSetting, storeSetting } from '#lib/settings/settings-storage.js';
import { THEME_COLORS, THEME_CONFIG, type ThemeColor, type ThemeMode } from '#lib/settings/themes.js';
import {
	APP_CJK_FONT_PACK_CONFIG,
	APP_CJK_FONT_PACK_DEFAULT_KEY,
	APP_FONT_CONFIG,
	APP_FONT_DEFAULT_KEY
} from '#lib/settings/fonts.js';

// The original's stores/settings/appearance.js and the parts of shared/utils/base/ui.js that apply
// it to the page: theme mode and colour, font, zoom, status indicators, trust colours, and the
// display options the rest of the client reads. Everything is per browser, under the original's
// storage names, and takes effect as soon as it changes.

class Setting<T> {
	#value: T = $state() as T;
	readonly key: string;
	readonly #normalize: (value: unknown) => T;
	readonly #fallback: T;

	constructor(key: string, fallback: T, normalize: (value: unknown) => T = (value) => value as T) {
		this.key = key;
		this.#normalize = normalize;
		this.#fallback = fallback;
		this.#value = normalize(readSetting<unknown>(key, fallback));
	}

	get value(): T {
		return this.#value;
	}

	set value(next: T) {
		this.#value = this.#normalize(next);
		storeSetting(this.key, this.#value);
	}

	/** Reads the stored value again, for a change made in another tab. */
	reload() {
		this.#value = this.#normalize(readSetting<unknown>(this.key, this.#fallback));
	}
}

const oneOf =
	<T>(allowed: readonly T[], fallback: T) =>
	(value: unknown): T =>
		allowed.includes(value as T) ? (value as T) : fallback;

const bool = (fallback: boolean) => (value: unknown) => (typeof value === 'boolean' ? value : fallback);

const clampInt = (min: number, max: number, fallback: number) => (value: unknown) => {
	const n = Number.parseInt(String(value), 10);
	return Number.isFinite(n) ? Math.min(max, Math.max(min, n)) : fallback;
};

export const MAX_TABLE_PAGE_SIZE = 1000;
export const DEFAULT_TABLE_PAGE_SIZES = [10, 15, 20, 25, 50, 100];
export const TABLE_PAGE_SIZE_SUGGESTIONS = [5, 10, 15, 20, 25, 30, 50, 75, 100, 150, 200, 250, 500, 1000];

export const TABLE_MAX_SIZE_MIN = 100;
export const TABLE_MAX_SIZE_MAX = 10000;
export const SEARCH_LIMIT_MIN = 10000;
export const SEARCH_LIMIT_MAX = 100000;
const DEFAULT_MAX_TABLE_SIZE = 500;
const DEFAULT_SEARCH_LIMIT = 50000;

export function normalizeTablePageSizes(input: unknown): number[] {
	const values = (Array.isArray(input) ? input : DEFAULT_TABLE_PAGE_SIZES)
		.map((v) => Number.parseInt(String(v), 10))
		.filter((v) => v > 0 && v <= MAX_TABLE_PAGE_SIZE);
	const uniqueSorted = Array.from(new Set(values)).sort((a, b) => a - b);
	return uniqueSorted.length ? uniqueSorted : [...DEFAULT_TABLE_PAGE_SIZES];
}

export const TRUST_COLOR_DEFAULTS = {
	untrusted: '#CCCCCC',
	basic: '#1778FF',
	known: '#2BCF5C',
	trusted: '#FF7B42',
	veteran: '#B18FFF',
	vip: '#FF2626',
	troll: '#782F2F'
} as const;

export type TrustColorKey = keyof typeof TRUST_COLOR_DEFAULTS;
export type TrustColors = Record<TrustColorKey, string>;

function normalizeTrustColors(value: unknown): TrustColors {
	const stored = value && typeof value === 'object' ? (value as Record<string, unknown>) : {};
	const colors = { ...TRUST_COLOR_DEFAULTS } as TrustColors;
	for (const key of Object.keys(colors) as TrustColorKey[]) {
		if (typeof stored[key] === 'string' && stored[key]) colors[key] = stored[key] as string;
	}
	return colors;
}

const THEME_MODE_KEYS = Object.keys(THEME_CONFIG) as ThemeMode[];
export const TABLE_DENSITIES = ['standard', 'comfortable', 'compact'] as const;
export type TableDensity = (typeof TABLE_DENSITIES)[number];
const THEME_COLOR_KEYS = THEME_COLORS.map((c) => c.key) as ThemeColor[];
const DARK_THEMES = THEME_MODE_KEYS.filter((mode) => THEME_CONFIG[mode].isDark === true);

class Appearance {
	// Theme (the nav menu's Theme submenu).
	readonly themeMode = new Setting<ThemeMode>('VRCX_ThemeMode', 'system', oneOf(THEME_MODE_KEYS, 'system'));
	readonly themeColor = new Setting<ThemeColor>('VRCX_themeColor', 'default', oneOf(THEME_COLOR_KEYS, 'default'));
	readonly lastDarkTheme = new Setting<ThemeMode>('VRCX_lastDarkTheme', 'dark', oneOf(DARK_THEMES, 'dark'));
	systemIsDark = $state(browser ? window.matchMedia('(prefers-color-scheme: dark)').matches : false);

	// Appearance.
	// The UI language, one of the original's 14 locales (it also orders the CJK fonts by lang).
	readonly appLanguage = new Setting<LanguageCode>('VRCX_appLanguage', 'en', (v) => (isLanguageCode(v) ? v : 'en'));
	/** Whether a language was ever picked, so the login page can offer the system one first. */
	get languagePicked(): boolean {
		return readSetting<unknown>('VRCX_appLanguage', null) !== null;
	}
	readonly fontFamily = new Setting<string>('VRCX_fontFamily', APP_FONT_DEFAULT_KEY, (v) =>
		typeof v === 'string' && APP_FONT_CONFIG[v] ? v : APP_FONT_DEFAULT_KEY
	);
	readonly customFontFamily = new Setting<string>('VRCX_customFontFamily', '', (v) => (typeof v === 'string' ? v : ''));
	readonly cjkFontPack = new Setting<string>('VRCX_cjkFontPack', APP_CJK_FONT_PACK_DEFAULT_KEY, (v) =>
		typeof v === 'string' && APP_CJK_FONT_PACK_CONFIG[v] ? v : APP_CJK_FONT_PACK_DEFAULT_KEY
	);
	/** The original's table density: standard, comfortable (a little tighter) or compact. */
	readonly tableDensity = new Setting<TableDensity>('VRCX_tableDensity', 'standard', oneOf(TABLE_DENSITIES, 'standard'));
	readonly zoomLevel = new Setting<number>('VRCX_zoomLevel', 100, clampInt(30, 500, 100));
	readonly notificationIconDot = new Setting('VRCX_notificationIconDot', true, bool(true));
	/** The original's custom.css (read from its AppData folder), kept in this browser instead. */
	readonly customCss = new Setting<string>('VRCX_customCss', '', (v) => (typeof v === 'string' ? v : ''));

	// User dialog.
	readonly displayVRCProfileThemes = new Setting('VRCX_displayVRCProfileThemes', true, bool(true));
	readonly displayVRCProfileBackgrounds = new Setting('VRCX_displayVRCProfileBackgrounds', false, bool(false));
	readonly profileBackgroundOpacity = new Setting<number>('VRCX_profileBackgroundOpacity', 0.5, (v) => {
		const n = Number(v);
		return Number.isFinite(n) ? Math.min(1, Math.max(0, n)) : 0.5;
	});
	readonly displayVRCProfileCosmetics = new Setting('VRCX_displayVRCProfileCosmetics', false, bool(false));
	readonly hideUserNotes = new Setting('VRCX_hideUserNotes', false, bool(false));
	readonly hideUserMemos = new Setting('VRCX_hideUserMemos', false, bool(false));

	// Display.
	readonly showInstanceIdInLocation = new Setting('VRCX_showInstanceIdInLocation', false, bool(false));
	readonly hideNicknames = new Setting('VRCX_hideNicknames', false, bool(false));
	readonly isAgeGatedInstancesVisible = new Setting('VRCX_isAgeGatedInstancesVisible', true, bool(true));
	readonly isClosedInstancesVisible = new Setting('VRCX_isClosedInstancesVisible', true, bool(true));
	readonly accessibleStatusIndicators = new Setting('VRCX_accessibleStatusIndicators', false, bool(false));

	// Navigation.
	readonly navWidth = new Setting<number>('VRCX_navPanelWidth', 240, clampInt(64, 480, 240));
	readonly isNavCollapsed = new Setting('VRCX_navIsCollapsed', false, bool(false));

	// Lists & tables.
	readonly sortFavorites = new Setting('VRCX_sortFavorites', true, bool(true));
	readonly instanceUsersSortAlphabetical = new Setting('VRCX_instanceUsersSortAlphabetical', false, bool(false));
	readonly tablePageSizes = new Setting<number[]>('VRCX_tablePageSizes', DEFAULT_TABLE_PAGE_SIZES, normalizeTablePageSizes);
	readonly maxTableSize = new Setting<number>('VRCX_maxTableSize_v2', DEFAULT_MAX_TABLE_SIZE, clampInt(TABLE_MAX_SIZE_MIN, TABLE_MAX_SIZE_MAX, DEFAULT_MAX_TABLE_SIZE));
	readonly searchLimit = new Setting<number>('VRCX_searchLimit', DEFAULT_SEARCH_LIMIT, clampInt(SEARCH_LIMIT_MIN, SEARCH_LIMIT_MAX, DEFAULT_SEARCH_LIMIT));

	// Time/date.
	readonly dtHour12 = new Setting('VRCX_dtHour12', false, bool(false));
	readonly dtIsoFormat = new Setting('VRCX_dtIsoFormat', false, bool(false));
	readonly weekStartsOn = new Setting<0 | 1 | 6>('VRCX_weekStartsOn', 1, (v) => oneOf<0 | 1 | 6>([0, 1, 6], 1)(Number(v)));

	// User colours.
	readonly trustColor = new Setting<TrustColors>('VRCX_trustColor', { ...TRUST_COLOR_DEFAULTS }, normalizeTrustColors);

	/** The theme actually shown: System resolves to Light or Dark. */
	get resolvedTheme(): Exclude<ThemeMode, 'system'> {
		const mode = this.themeMode.value;
		if (mode === 'system') return this.systemIsDark ? 'dark' : 'light';
		return mode;
	}

	get isDarkMode(): boolean {
		return THEME_CONFIG[this.resolvedTheme].isDark === true;
	}

	setThemeMode(mode: ThemeMode) {
		this.themeMode.value = mode;
		if (THEME_CONFIG[mode]?.isDark === true) this.lastDarkTheme.value = mode;
	}

	toggleThemeMode() {
		this.setThemeMode(this.isDarkMode ? 'light' : this.lastDarkTheme.value);
	}

	setTrustColor(key: TrustColorKey, color: string) {
		this.trustColor.value = { ...this.trustColor.value, [key]: color };
	}
}

export const appearance = new Appearance();

// Paraglide's locale is this setting: every message function calls getLocale(), which reads the
// $state behind appLanguage, so the UI re-renders in the new language as soon as it changes and
// VRCX_appLanguage stays the stored choice. Paraglide's setLocale() writes it back (it calls this
// once with the current value on its first read, hence the check).
defineCustomClientStrategy('custom-appLanguage', {
	getLocale: () => appearance.appLanguage.value,
	setLocale: (locale) => {
		if (appearance.appLanguage.value !== locale) appearance.appLanguage.value = isLanguageCode(locale) ? locale : 'en';
	}
});

// ---- Applying it to the page (the original's ui.js) ----

const APP_FONT_ATTR = 'data-app-font';
const APP_CJK_FONT_PACK_ATTR = 'data-app-cjk-font-pack';

/** A <style> carrying a font's @import or @font-face rules, one per kind, swapped on change. */
function ensureDynamicFontStyle(attrName: string, styleKey: string, cssImport: string | undefined) {
	document.querySelectorAll(`style[${attrName}]`).forEach((el) => {
		if (el.getAttribute(attrName) !== styleKey) el.remove();
	});
	if (!cssImport || document.querySelector(`style[${attrName}="${styleKey}"]`)) return;
	const style = document.createElement('style');
	style.setAttribute(attrName, styleKey);
	style.textContent = cssImport;
	document.head.appendChild(style);
}

function applyFontFamily(key: string, custom: string) {
	const root = document.documentElement;
	if (key === 'custom') {
		root.style.setProperty('--font-western-primary', custom.trim() || 'system-ui');
		ensureDynamicFontStyle(APP_FONT_ATTR, 'custom', undefined);
		return;
	}
	const config = APP_FONT_CONFIG[key] ?? APP_FONT_CONFIG[APP_FONT_DEFAULT_KEY];
	root.style.setProperty('--font-western-primary', config.cssName);
	ensureDynamicFontStyle(APP_FONT_ATTR, key, config.cssImport);
}

/** The original's refreshCustomCss: one style element after the app's own, replaced on each change. */
export function applyCustomCss(css: string) {
	document.getElementById('app-custom-style')?.remove();
	if (!css.trim()) return;
	const style = document.createElement('style');
	style.id = 'app-custom-style';
	style.textContent = css;
	document.head.appendChild(style);
}

function applyCjkFontPack(key: string) {
	const config = APP_CJK_FONT_PACK_CONFIG[key] ?? APP_CJK_FONT_PACK_CONFIG[APP_CJK_FONT_PACK_DEFAULT_KEY];
	const root = document.documentElement;
	root.style.setProperty('--font-cjk-jp-primary', config.cssName.jp);
	root.style.setProperty('--font-cjk-sc-primary', config.cssName.sc);
	root.style.setProperty('--font-cjk-kr-primary', config.cssName.kr);
	root.style.setProperty('--font-cjk-tc-primary', config.cssName.tc);
	ensureDynamicFontStyle(APP_CJK_FONT_PACK_ATTR, key, config.cssImport);
}

/** The original's updateTrustColorClasses: the x-tag-* rank classes follow the chosen colours. */
function applyTrustColorClasses(colors: TrustColors) {
	let style = document.getElementById('trustColor');
	if (!style) {
		style = document.createElement('style');
		style.id = 'trustColor';
		document.head.appendChild(style);
	}
	style.textContent = Object.entries(colors)
		.map(([rank, color]) => `.x-tag-${rank} { color: ${color} !important; border-color: ${color} !important; }`)
		.join(' ');
}

if (browser) {
	const media = window.matchMedia('(prefers-color-scheme: dark)');
	media.addEventListener('change', () => (appearance.systemIsDark = media.matches));
	// The original's Alt+Shift+R: re-apply the custom CSS (here read again from storage).
	window.addEventListener('keydown', (event) => {
		if (!event.altKey || !event.shiftKey || event.ctrlKey || event.code !== 'KeyR') return;
		event.preventDefault();
		appearance.customCss.reload();
		applyCustomCss(appearance.customCss.value);
		toast.success('Custom CSS refreshed');
	});

	$effect.root(() => {
		$effect(() => {
			const root = document.documentElement;
			const theme = appearance.resolvedTheme;
			root.setAttribute('data-theme', theme);
			root.classList.toggle('dark', THEME_CONFIG[theme].isDark === true);
			root.setAttribute('data-theme-color', appearance.themeColor.value);
		});
		$effect(() => applyFontFamily(appearance.fontFamily.value, appearance.customFontFamily.value));
		$effect(() => applyCjkFontPack(appearance.cjkFontPack.value));
		$effect(() => applyCustomCss(appearance.customCss.value));
		$effect(() => document.documentElement.setAttribute('lang', appearance.appLanguage.value));
		$effect(() => {
			document.documentElement.classList.toggle('accessible-status-indicators', appearance.accessibleStatusIndicators.value);
		});
		$effect(() => applyTrustColorClasses(appearance.trustColor.value));
		$effect(() => {
			// The original's applyTableDensity.
			const classList = document.documentElement.classList;
			classList.toggle('is-compact-table', appearance.tableDensity.value === 'compact');
			classList.toggle('is-comfortable-table', appearance.tableDensity.value === 'comfortable');
		});
		$effect(() => {
			// Page zoom, which the original asked its browser host for. 100 is the browser's own size.
			const zoom = appearance.zoomLevel.value / 100;
			document.documentElement.style.setProperty('zoom', zoom === 1 ? '' : String(zoom));
			document.documentElement.style.setProperty('--app-zoom', String(zoom));
		});
	});
}

type InstanceUser = { displayName?: string; location?: string | null; locationSince?: string | null };

/**
 * The original's instance user order (Sort Users in Instance by): by name, or by time in the
 * instance with the longest there first and anyone still traveling last (compareByLocationAt).
 */
export function sortInstanceUsers<T extends InstanceUser>(users: readonly T[]): T[] {
	const sorted = [...users];
	if (appearance.instanceUsersSortAlphabetical.value) {
		return sorted.sort((a, b) =>
			typeof a.displayName === 'string' && typeof b.displayName === 'string' ? a.displayName.localeCompare(b.displayName) : 0
		);
	}
	const since = (user: T) => (user.locationSince ? Date.parse(user.locationSince) || 0 : 0);
	return sorted.sort((a, b) => {
		const aTraveling = a.location === 'traveling';
		const bTraveling = b.location === 'traveling';
		if (aTraveling || bTraveling) return aTraveling === bTraveling ? 0 : aTraveling ? 1 : -1;
		return since(a) - since(b);
	});
}
