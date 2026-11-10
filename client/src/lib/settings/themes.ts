import { m } from '#lib/paraglide/messages.js';
// The original's shared/constants/themes.js: the theme modes (System follows the OS) and the
// colour themes on top of them. The colour stylesheets are in src/styles/themes/, scoped by
// data-theme-color so they can all be bundled.

export type ThemeMode = 'system' | 'light' | 'dark' | 'midnight';

export const THEME_CONFIG: Record<ThemeMode, { isDark: boolean | 'system'; name: string }> = {
	system: { isDark: 'system', get name() { return m.view_settings_category_system(); } },
	light: { isDark: false, get name() { return m.view_settings_appearance_appearance_theme_mode_light(); } },
	dark: { isDark: true, get name() { return m.view_settings_appearance_appearance_theme_mode_dark(); } },
	midnight: { isDark: true, get name() { return m.view_settings_appearance_appearance_theme_mode_midnight(); } }
};

export const THEME_MODES = Object.keys(THEME_CONFIG) as ThemeMode[];

export const THEME_COLORS = [
	{ key: 'default', get label() { return m.view_settings_appearance_theme_color_default(); }, swatch: 'oklch(0.27 0 0)' },
	{ key: 'blue', get label() { return m.view_settings_appearance_theme_color_blue(); }, swatch: 'oklch(0.488 0.243 264.376)' },
	{ key: 'green', get label() { return m.view_settings_appearance_theme_color_green(); }, swatch: 'oklch(0.648 0.2 131.684)' },
	{ key: 'orange', get label() { return m.view_settings_appearance_theme_color_orange(); }, swatch: 'oklch(0.646 0.222 41.116)' },
	{ key: 'red', get label() { return m.view_settings_appearance_theme_color_red(); }, swatch: 'oklch(0.577 0.245 27.325)' },
	{ key: 'rose', get label() { return m.view_settings_appearance_theme_color_rose(); }, swatch: 'oklch(0.586 0.253 17.585)' },
	{ key: 'violet', get label() { return m.view_settings_appearance_theme_color_violet(); }, swatch: 'oklch(0.541 0.281 293.009)' },
	{ key: 'yellow', get label() { return m.view_settings_appearance_theme_color_yellow(); }, swatch: 'oklch(0.852 0.199 91.936)' }
] as const;

export type ThemeColor = (typeof THEME_COLORS)[number]['key'];
