// Shared bits for the Charts pages: theme detection for ECharts/Sigma, the time formats the
// original drove from its hour12 setting, and the tooltip markup both activity charts use.

import { appearance } from '#lib/settings/appearance.svelte.js';
import { timeToText } from '#lib/helpers/format.js';

export function isDarkMode(): boolean {
	if (typeof document === 'undefined') return false;
	// The theme setting, so charts redraw when it changes.
	if (appearance.isDarkMode) return true;
	if (document.documentElement.classList.contains('dark')) return true;
	// The theme may live on :root without a class; judge by the page background.
	const bg = getComputedStyle(document.body).backgroundColor;
	const m = bg.match(/\d+(\.\d+)?/g);
	if (m && m.length >= 3) {
		const [r, g, b] = m.map(Number);
		return 0.2126 * r + 0.7152 * g + 0.0722 * b < 128;
	}
	return window.matchMedia('(prefers-color-scheme: dark)').matches;
}

export function hour12Setting(): boolean {
	return appearance.dtHour12.value;
}

function pad(n: number): string {
	return String(n).padStart(2, '0');
}

/** HH:mm or hh:mm A, like the original's dayjs formats. */
export function clock(date: Date, seconds = false): string {
	const h12 = hour12Setting();
	let h = date.getHours();
	let suffix = '';
	if (h12) {
		suffix = h >= 12 ? ' PM' : ' AM';
		h = h % 12 || 12;
	}
	return `${pad(h)}:${pad(date.getMinutes())}${seconds ? `:${pad(date.getSeconds())}` : ''}${suffix}`;
}

/** A duration measured from midnight, as a clock label (the activity chart's x axis). */
export function clockFromMidnight(ms: number): string {
	const h12 = hour12Setting();
	let h = Math.floor(ms / 3600000) % 24;
	const m = Math.floor((ms % 3600000) / 60000);
	let suffix = '';
	if (h12) {
		suffix = h >= 12 ? ' PM' : ' AM';
		h = h % 12 || 12;
	}
	return `${pad(h)}:${pad(m)}${suffix}`;
}

export function dayKey(date: Date): string {
	return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

export function startOfDay(date: Date): Date {
	return new Date(date.getFullYear(), date.getMonth(), date.getDate(), 0, 0, 0, 0);
}

export function endOfDay(date: Date): Date {
	return new Date(date.getFullYear(), date.getMonth(), date.getDate(), 23, 59, 59, 999);
}

export function isSameDay(a: Date, b: Date): boolean {
	return dayKey(a) === dayKey(b);
}

function escapeHtml(s: string): string {
	return s.replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;');
}

/** The original's InstanceActivityTooltip: a colour bar beside name, time span and duration. */
export function activityTooltip(args: { color: string; displayName: string; icon: string; joinTime: Date; leaveTime: Date; durationMs: number }): string {
	return `<div style="display:flex;align-items:center">
	<div style="width:10px;height:55px;background-color:${args.color};margin-right:6px"></div>
	<div>
		<div>${escapeHtml(args.displayName)} ${escapeHtml(args.icon)}</div>
		<div>${clock(args.joinTime, true)} - ${clock(args.leaveTime, true)}</div>
		<div>${timeToText(args.durationMs, true)}</div>
	</div>
</div>`;
}

export function truncateName(name: string, max = 20): string {
	return name.length > max ? `${name.slice(0, max)}...` : name;
}
