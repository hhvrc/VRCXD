import { m } from '#lib/paraglide/messages.js';
import type { UserBadge } from '#lib/api/types.js';
// A user's VRChat profile theme and background in the user pop-up: the original's
// updateUserDialogProfile (userCoordinator.js), getReadableProfileThemeColor and invertHexColor
// (shared/utils/user.js), the dialog background (MainDialogContainer.vue) and the background
// textures (shared/constants/backgrounds.js).

/** GET /1/accounts/{id}/users/{userId}/public-profile */
export type PublicProfile = {
	id?: string;
	themeIconColor?: string | null;
	themeButtonColor?: string | null;
	themeSubtextColor?: string | null;
	backgroundType?: string | null;
	backgroundGradientTop?: string | null;
	backgroundGradientBottom?: string | null;
	backgroundTextureId?: string | null;
	isEconomyCreator?: boolean | null;
	/** With the Supporter badge the server adds for VRC+ users. */
	badges?: UserBadge[] | null;
};

export type ProfileTheme = { iconColor: string; buttonColor: string; subtextColor: string };

export const DEFAULT_PROFILE_THEME: ProfileTheme = {
	iconColor: 'var(--muted-foreground)',
	buttonColor: 'var(--primary)',
	subtextColor: 'var(--muted-foreground)'
};

export const PROFILE_BACKGROUNDS: { id: string; label: string; url: string }[] = [
	{ id: 'grid', label: 'Grid', url: 'https://assets.vrchat.com/www/profile_decorations/profile_backgrounds/BG_Grid.png' },
	{ id: 'filigree', label: 'Filigree', url: 'https://assets.vrchat.com/www/profile_decorations/profile_backgrounds/BG_Cascade.png' },
	{ id: 'bit-mountain', label: 'Bit Mountain', url: 'https://assets.vrchat.com/www/profile_decorations/profile_backgrounds/BG_GRID_BITS.png' },
	{ id: 'approach', label: 'Approach', url: 'https://assets.vrchat.com/www/profile_decorations/profile_backgrounds/BG_Approach.png' },
	{ id: 'planet-fall', label: 'Planet Fall', url: 'https://assets.vrchat.com/www/profile_decorations/profile_backgrounds/BG_PlanetFall.png' },
	{ id: 'jungle', label: 'Jungle', url: 'https://assets.vrchat.com/www/profile_decorations/profile_backgrounds/BG_Jungle.png' },
	{ id: 'light-streams', label: 'Light Streams', url: 'https://assets.vrchat.com/www/profile_decorations/profile_backgrounds/BG_LightStream.png' },
	{ id: 'koi', label: 'Koi', url: 'https://assets.vrchat.com/www/profile_decorations/profile_backgrounds/Koi_Layer.png' },
	{ id: 'moonlight', label: 'Moonlight', url: 'https://assets.vrchat.com/www/profile_decorations/profile_backgrounds/BG_Cloud.png' },
	{ id: 'machine', label: 'Machine', url: 'https://assets.vrchat.com/www/profile_decorations/profile_backgrounds/TechWallLayer.png' },
	{ id: 'topology', label: 'Topology', url: 'https://assets.vrchat.com/www/profile_decorations/profile_backgrounds/TOPOLOGY.png' },
	{ id: 'circuit', label: 'Circuit', url: 'https://assets.vrchat.com/www/profile_decorations/profile_backgrounds/BG_Circuit.png' },
	{ id: 'kawaii-cupcakes', label: 'Kawaii Cupcakes', url: 'https://assets.vrchat.com/www/profile_decorations/profile_backgrounds/BG_Kawaii_cupcakes.png' },
	{ id: 'bokeh', label: 'Bokeh', url: 'https://assets.vrchat.com/www/profile_decorations/profile_backgrounds/BG_Bokeh.png' },
	{ id: 'hologram', label: 'Hologram', url: 'https://assets.vrchat.com/www/profile_decorations/profile_backgrounds/BG_Shatter.png' },
	{ id: 'flow', label: 'Flow', url: 'https://assets.vrchat.com/www/profile_decorations/profile_backgrounds/BG_Flow_Horizontal.png' },
	{ id: 'jelly', label: 'Jelly', url: 'https://assets.vrchat.com/www/profile_decorations/profile_backgrounds/BG_Jelly.png' },
	{ id: 'cheese', label: 'Cheese', url: 'https://assets.vrchat.com/www/profile_decorations/profile_backgrounds/BG_Cheese.png' },
	{ id: 'alchemy', label: 'Alchemy', url: 'https://assets.vrchat.com/www/profile_decorations/profile_backgrounds/BG_Alchemy.png' },
	{ id: 'sunset-mountain', label: 'Sunset Mountain', url: 'https://assets.vrchat.com/www/profile_decorations/profile_backgrounds/BG_MountainSun.png' },
	{ id: 'magic-rocks', label: 'Magic Rocks', url: 'https://assets.vrchat.com/www/profile_decorations/profile_backgrounds/BG_MagicRocks.png' },
	{ id: 'city-stillness', label: 'City Stillness', url: 'https://assets.vrchat.com/www/profile_decorations/profile_backgrounds/BG_FutureCityStillness.png' },
	{ id: 'music', get label() { return m.dialog_group_event_edit_categories_music(); }, url: 'https://assets.vrchat.com/www/profile_decorations/profile_backgrounds/BG_Music.png' },
	{ id: 'wolf-running', label: 'Wolf Running', url: 'https://assets.vrchat.com/www/profile_decorations/profile_backgrounds/BG_WolfRunning.png' },
	{ id: 'dragon-smoke', label: 'Dragon Smoke', url: 'https://assets.vrchat.com/www/profile_decorations/profile_backgrounds/BG_DragonSmoke.png' },
	{ id: 'night-rain', label: 'Night Rain', url: 'https://assets.vrchat.com/www/profile_decorations/profile_backgrounds/BG_NightRain.png' },
	{ id: 'butterfly-zero', label: 'Butterfly Zero', url: 'https://assets.vrchat.com/www/profile_decorations/profile_backgrounds/BG_ButterFlyZERO.png' },
	{ id: 'i-am-speed', label: 'I Am Speed', url: 'https://assets.vrchat.com/www/profile_decorations/profile_backgrounds/void.png' },
	{ id: 'ronin', label: 'Ronin', url: 'https://assets.vrchat.com/www/profile_decorations/profile_backgrounds/BG_Ronin.png' },
	{ id: 'abduction', label: 'Abduction', url: 'https://assets.vrchat.com/www/profile_decorations/profile_backgrounds/BG_Abduction.png' },
	{ id: 'cat-dream', label: 'Cat Dream', url: 'https://assets.vrchat.com/www/profile_decorations/profile_backgrounds/BG_CatDream.png' },
	{ id: 'virtual-waves', label: 'Virtual Waves', url: 'https://assets.vrchat.com/www/profile_decorations/profile_backgrounds/BG_VirtualWaves.png' },
	{ id: 'bolt-and-snail', label: 'Bolt and Snail', url: 'https://assets.vrchat.com/www/profile_decorations/profile_backgrounds/BG_BoltNSnail.png' },
	{ id: 'escape', label: 'Escape', url: 'https://assets.vrchat.com/www/profile_decorations/profile_backgrounds/BG_Escape.png' },
];

const THEME_COLOR_LIMITS = { darkMinLuminance: 0.06, lightMaxLuminance: 0.74 };

type Rgb = { r: number; g: number; b: number };

function normalizeProfileHex(value: string | null | undefined): string | null {
	const hex = String(value || '')
		.trim()
		.replace(/^#/, '')
		.toLowerCase();
	return /^[0-9a-f]{6}$/.test(hex) ? `#${hex}` : null;
}

function hexToRgb(hex: string): Rgb | null {
	const match = /^#?([0-9a-f]{6})$/i.exec(hex);
	if (!match) return null;
	const value = match[1];
	return { r: parseInt(value.slice(0, 2), 16), g: parseInt(value.slice(2, 4), 16), b: parseInt(value.slice(4, 6), 16) };
}

function rgbToHex(rgb: Rgb): string {
	const toHex = (value: number) => Math.max(0, Math.min(255, Math.round(value))).toString(16).padStart(2, '0');
	return `#${toHex(rgb.r)}${toHex(rgb.g)}${toHex(rgb.b)}`;
}

function getRelativeLuminance(rgb: Rgb): number {
	const channel = (value: number) => {
		const normalized = value / 255;
		return normalized <= 0.03928 ? normalized / 12.92 : ((normalized + 0.055) / 1.055) ** 2.4;
	};
	return 0.2126 * channel(rgb.r) + 0.7152 * channel(rgb.g) + 0.0722 * channel(rgb.b);
}

function mixRgb(from: Rgb, to: Rgb, weight: number): Rgb {
	return { r: from.r + (to.r - from.r) * weight, g: from.g + (to.g - from.g) * weight, b: from.b + (to.b - from.b) * weight };
}

/** A profile colour nudged towards white (dark mode) or black only when it would be unreadable. */
export function getReadableProfileThemeColor(colorValue: string | null | undefined, fallback: string, isDarkMode: boolean): string {
	const normalized = normalizeProfileHex(colorValue);
	if (!normalized) return fallback;
	let rgb = hexToRgb(normalized);
	if (!rgb) return fallback;
	const threshold = isDarkMode ? THEME_COLOR_LIMITS.darkMinLuminance : THEME_COLOR_LIMITS.lightMaxLuminance;
	let luminance = getRelativeLuminance(rgb);
	if (isDarkMode ? luminance >= threshold : luminance <= threshold) return normalized;
	const target = isDarkMode ? { r: 255, g: 255, b: 255 } : { r: 0, g: 0, b: 0 };
	for (let i = 0; i < 14; i++) {
		rgb = mixRgb(rgb, target, 0.18);
		luminance = getRelativeLuminance(rgb);
		if (isDarkMode ? luminance >= threshold : luminance <= threshold) break;
	}
	return rgbToHex(rgb);
}

export function invertHexColor(colorValue: string): string {
	const normalized = normalizeProfileHex(colorValue);
	const rgb = normalized && hexToRgb(normalized);
	return rgb ? rgbToHex({ r: 255 - rgb.r, g: 255 - rgb.g, b: 255 - rgb.b }) : colorValue;
}

export function profileTheme(profile: PublicProfile | undefined, isDarkMode: boolean): ProfileTheme {
	if (!profile) return DEFAULT_PROFILE_THEME;
	return {
		iconColor: getReadableProfileThemeColor(profile.themeIconColor, DEFAULT_PROFILE_THEME.iconColor, isDarkMode),
		buttonColor: getReadableProfileThemeColor(profile.themeButtonColor, DEFAULT_PROFILE_THEME.buttonColor, isDarkMode),
		subtextColor: getReadableProfileThemeColor(profile.themeSubtextColor, DEFAULT_PROFILE_THEME.subtextColor, isDarkMode)
	};
}

/** The pop-up's background: the gradient or texture under a wash of the page colour, as a style string. */
export function profileBackgroundStyle(profile: PublicProfile | undefined, isDarkMode: boolean, opacity: number): string {
	const base = 'overflow: hidden; background-clip: padding-box;';
	if (!profile) return base;
	const wash = 1 - opacity;
	const overlay = isDarkMode ? `rgba(0, 0, 0, ${wash})` : `rgba(255, 255, 255, ${wash})`;
	if (profile.backgroundType === 'gradient') {
		// The original passes the opposite mode here, so light gradients stay light under a light wash.
		const top = getReadableProfileThemeColor(`#${profile.backgroundGradientTop}`, 'var(--background)', !isDarkMode);
		const bottom = getReadableProfileThemeColor(`#${profile.backgroundGradientBottom}`, 'var(--background)', !isDarkMode);
		return `${base} background-image: linear-gradient(${overlay}, ${overlay}), linear-gradient(180deg, ${top}, ${bottom});`;
	}
	if (profile.backgroundType === 'texture') {
		const texture = PROFILE_BACKGROUNDS.find((b) => b.id === profile.backgroundTextureId);
		if (!texture) return base;
		return `${base} background-image: linear-gradient(${overlay}, ${overlay}), url(${texture.url}); background-size: cover; background-position: top center; background-repeat: no-repeat;`;
	}
	return base;
}
