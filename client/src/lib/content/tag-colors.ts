import { m } from '#lib/paraglide/messages.js';
// Port of src/shared/constants/tags.js - the same palette and the same djb2
// hash, so a tag added here and the same tag in the Electron client get the
// same colour rather than two apps disagreeing about what "wip" looks like.

export type TagColor = {
	name: string;
	label: string;
	bg: string;
	text: string;
};

export const TAG_COLORS: readonly TagColor[] = [
	{ name: 'default', label: 'Default', bg: 'oklch(0.4 0 0 / 0.2)', text: 'oklch(0.7 0 0)' },
	{ name: 'blue', get label() { return m.view_settings_appearance_theme_color_blue(); }, bg: 'oklch(0.488 0.243 264 / 0.2)', text: 'oklch(0.65 0.2 264)' },
	{ name: 'green', get label() { return m.view_settings_appearance_theme_color_green(); }, bg: 'oklch(0.648 0.2 132 / 0.2)', text: 'oklch(0.7 0.18 132)' },
	{ name: 'orange', get label() { return m.view_settings_appearance_theme_color_orange(); }, bg: 'oklch(0.646 0.222 41 / 0.2)', text: 'oklch(0.72 0.19 41)' },
	{ name: 'red', get label() { return m.view_settings_appearance_theme_color_red(); }, bg: 'oklch(0.577 0.245 27 / 0.2)', text: 'oklch(0.68 0.2 27)' },
	{ name: 'rose', get label() { return m.view_settings_appearance_theme_color_rose(); }, bg: 'oklch(0.586 0.253 18 / 0.2)', text: 'oklch(0.7 0.2 18)' },
	{ name: 'violet', get label() { return m.view_settings_appearance_theme_color_violet(); }, bg: 'oklch(0.541 0.281 293 / 0.2)', text: 'oklch(0.68 0.22 293)' },
	{ name: 'yellow', get label() { return m.view_settings_appearance_theme_color_yellow(); }, bg: 'oklch(0.852 0.199 92 / 0.2)', text: 'oklch(0.82 0.17 92)' }
];

// getTagColor: djb2 over the tag name, skipping index 0 (the default grey)
// so a named tag always lands on a real colour.
export function colorForTagName(tagName: string): TagColor {
	let hash = 5381;
	for (let i = 0; i < tagName.length; i++) {
		hash = ((hash << 5) + hash + tagName.charCodeAt(i)) >>> 0;
	}
	return TAG_COLORS[(hash % (TAG_COLORS.length - 1)) + 1];
}

// A stored tag may carry an explicit palette name; anything else (including
// the empty string a tag added without a colour gets) falls back to the hash,
// which is what ManageTagsDialog.vue#resolveColor does.
export function resolveTagColor(tagName: string, storedColor: string): TagColor {
	return TAG_COLORS.find((color) => color.name === storedColor) ?? colorForTagName(tagName);
}
