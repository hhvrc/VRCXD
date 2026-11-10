import { appearance } from '#lib/settings/appearance.svelte.js';

// Formatting helpers ported from the original's dateCoordinator.js, format.js and string.js, so
// the tables read the same way they did.

function padZero(n: number): string {
	return String(n).padStart(2, '0');
}

/**
 * The original's formatDateFilter: "short" is MM/DD HH:mm, "long" adds the year and seconds,
 * both in the browser's locale unless the ISO setting is on. Hour12 and ISO are the same
 * settings keys the original persisted (VRCX_dtHour12, VRCX_dtIsoFormat).
 */
export function formatDateFilter(dateStr: string | number | undefined | null, format: 'long' | 'short' | 'time' | 'date'): string {
	if (!dateStr) return '-';
	const dt = new Date(dateStr);
	if (isNaN(dt.getTime())) return '-';
	const isoFormat = appearance.dtIsoFormat.value;
	const hour12 = appearance.dtHour12.value;
	let dateFormat = 'en-gb';
	if (!isoFormat && typeof navigator !== 'undefined' && navigator.language) dateFormat = navigator.language;
	if (dateFormat.length > 4 && dateFormat[4] === '_') dateFormat = dateFormat.slice(0, 4);
	const hourCycle = hour12 ? 'h12' : 'h23';

	if (isoFormat && format === 'long') {
		return `${dt.getFullYear()}-${padZero(dt.getMonth() + 1)}-${padZero(dt.getDate())} ${padZero(dt.getHours())}:${padZero(dt.getMinutes())}:${padZero(dt.getSeconds())}`;
	}
	if (format === 'long') {
		return dt.toLocaleDateString(dateFormat, { month: '2-digit', day: '2-digit', year: 'numeric', hour: 'numeric', minute: 'numeric', second: 'numeric', hourCycle });
	}
	if (format === 'short') {
		return dt
			.toLocaleDateString(dateFormat, { month: '2-digit', day: '2-digit', hour: 'numeric', minute: 'numeric', hourCycle })
			.replace(' AM', 'am')
			.replace(' PM', 'pm')
			.replace(',', '');
	}
	if (format === 'time') return dt.toLocaleTimeString(dateFormat, { hour: 'numeric', minute: 'numeric', hourCycle });
	return dt.toLocaleDateString(dateFormat, { month: '2-digit', day: '2-digit', year: 'numeric' });
}

/** The original's timeToText: milliseconds to "1d 2h 3m" (seconds only when nothing bigger, rounded to 5). */
/** The original's formatSeconds: mm:ss, or hh:mm:ss from an hour. */
export function formatSeconds(duration: number): string {
	const time = Math.max(0, Math.floor(Number(duration) || 0));
	const pad = (n: number) => String(n).padStart(2, '0');
	const hours = Math.floor(time / 3600);
	const minutes = Math.floor(time / 60) % 60;
	return `${hours > 0 ? `${pad(hours)}:` : ''}${pad(minutes)}:${pad(time % 60)}`;
}

export function timeToText(ms: number | string | undefined | null, needSeconds = false): string {
	let n = Number(ms);
	if (isNaN(n)) return String(ms ?? '');
	n = Math.floor(n / 1000);
	if (n < 0) n = -n;
	const arr: string[] = [];
	if (n >= 31536000) {
		arr.push(`${Math.floor(n / 31536000)}y`);
		n %= 31536000;
	}
	if (n >= 86400) {
		arr.push(`${Math.floor(n / 86400)}d`);
		n %= 86400;
	}
	if (n >= 3600) {
		arr.push(`${Math.floor(n / 3600)}h`);
		n %= 3600;
	}
	if (n >= 60) {
		arr.push(`${Math.floor(n / 60)}m`);
		n %= 60;
	}
	if (needSeconds || (arr.length === 0 && n < 60)) {
		n = Math.floor((n + 2.5) / 5) * 5;
		arr.push(`${n}s`);
	}
	return arr.join(' ');
}

const bioSymbols: Record<string, string> = {
	'@': '＠', '#': '＃', $: '＄', '%': '％', '&': '＆', '=': '＝', '+': '＋', '/': '⁄', '\\': '＼', ';': ';', ':': '˸',
	',': '‚', '?': '？', '!': 'ǃ', '"': '＂', '<': '≺', '>': '≻', '.': '․', '^': '＾', '{': '｛', '}': '｝', '[': '［',
	']': '］', '(': '（', ')': '）', '|': '｜', '*': '∗'
};

/** VRChat makes users type full-width look-alikes for punctuation; put the real characters back. */
export function replaceBioSymbols(text: unknown): string {
	if (typeof text !== 'string') return '';
	let out = text;
	for (const [real, fake] of Object.entries(bioSymbols)) out = out.split(fake).join(real);
	return out.replace(/ {1,}/g, ' ').trimEnd();
}

/** The original's sanitizeEntityJson: a copy with each set field passed through replaceBioSymbols. */
export function sanitizeEntity<T extends object>(json: T, fields: (keyof T)[]): T {
	const out = { ...json };
	for (const field of fields) if (out[field]) out[field] = replaceBioSymbols(out[field]) as T[keyof T];
	return out;
}

export function getFaviconUrl(resource: string): string {
	if (!resource) return '';
	try {
		const url = new URL(resource);
		return `https://icons.duckduckgo.com/ip2/${url.host}.ico`;
	} catch {
		return '';
	}
}

function escapeHtml(s: string): string {
	return s.replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;').replaceAll('"', '&quot;').replaceAll("'", '&#039;');
}

/**
 * The original's word diff for bios: an HTML string with removed words in .x-text-removed and
 * added words in .x-text-added, newlines as <br>.
 */
export function formatDifference(
	oldString: unknown,
	newString: unknown,
	markerAddition = '<span class="x-text-added">{{text}}</span>',
	markerDeletion = '<span class="x-text-removed">{{text}}</span>'
): string {
	const [oldText, newText] = [oldString, newString].map((s) => escapeHtml(String(s ?? '')).replaceAll('\n', '<br>'));
	const oldWords = oldText.split(/\s+/).flatMap((w) => w.split(/(<br>)/));
	const newWords = newText.split(/\s+/).flatMap((w) => w.split(/(<br>)/));

	function findLongestMatch(oldStart: number, oldEnd: number, newStart: number, newEnd: number) {
		let bestOldStart = oldStart;
		let bestNewStart = newStart;
		let bestSize = 0;
		const lookup = new Map<string, number[]>();
		for (let i = oldStart; i < oldEnd; i++) {
			const word = oldWords[i];
			if (!lookup.has(word)) lookup.set(word, []);
			lookup.get(word)!.push(i);
		}
		for (let j = newStart; j < newEnd; j++) {
			const positions = lookup.get(newWords[j]);
			if (!positions) continue;
			for (const i of positions) {
				let size = 0;
				while (i + size < oldEnd && j + size < newEnd && oldWords[i + size] === newWords[j + size]) size++;
				if (size > bestSize) {
					bestOldStart = i;
					bestNewStart = j;
					bestSize = size;
				}
			}
		}
		return { oldStart: bestOldStart, newStart: bestNewStart, size: bestSize };
	}

	function build(words: string[], start: number, end: number, pattern: string): string[] {
		const r: string[] = [];
		const ts = words.slice(start, end).filter((w) => w.length > 0).join(' ').split('<br>');
		for (let i = 0; i < ts.length; i++) {
			if (i > 0) r.push('<br>');
			if (ts[i].length < 1) continue;
			r.push(pattern.replace('{{text}}', ts[i]));
		}
		return r;
	}

	function buildDiff(oldStart: number, oldEnd: number, newStart: number, newEnd: number): string[] {
		const result: string[] = [];
		const match = findLongestMatch(oldStart, oldEnd, newStart, newEnd);
		if (match.size > 0) {
			if (oldStart < match.oldStart || newStart < match.newStart) result.push(...buildDiff(oldStart, match.oldStart, newStart, match.newStart));
			result.push(oldWords.slice(match.oldStart, match.oldStart + match.size).join(' '));
			if (match.oldStart + match.size < oldEnd || match.newStart + match.size < newEnd) {
				result.push(...buildDiff(match.oldStart + match.size, oldEnd, match.newStart + match.size, newEnd));
			}
		} else {
			if (oldStart < oldEnd) result.push(...build(oldWords, oldStart, oldEnd, markerDeletion));
			if (newStart < newEnd) result.push(...build(newWords, newStart, newEnd, markerAddition));
		}
		return result;
	}

	return buildDiff(0, oldWords.length, 0, newWords.length)
		.join(' ')
		.replace(/<br>[ ]+<br>/g, '<br><br>')
		.replace(/<br> /g, '<br>');
}

/** The original's statusClass: the dot colour for a VRChat status ("active" is the plain online green). */
export function statusClass(status: string | undefined | null): string {
	switch (status) {
		case 'active':
			return 'online';
		case 'join me':
			return 'joinme';
		case 'ask me':
			return 'askme';
		case 'busy':
			return 'busy';
		default:
			return '';
	}
}

/** How long ago, as the original's dialogs show it: the two largest units, like "2 years, 3 months". */
export function timeAgo(datetime: string | undefined | null, now = Date.now()): string {
	if (!datetime) return '-';
	const unit = (value: number, name: string) => `${value} ${name}${value === 1 ? '' : 's'}`;
	const pair = (big: number, bigName: string, small: number, smallName: string) =>
		small > 0 ? `${unit(big, bigName)}, ${unit(small, smallName)}` : unit(big, bigName);
	const parsed = Date.parse(datetime);
	if (isNaN(parsed)) return datetime;
	const n = Math.abs(Math.floor((now - parsed) / 1000));
	if (n === 0) return '-';
	if (n >= 31536000) return pair(Math.floor(n / 31536000), 'year', Math.floor((n % 31536000) / 2592000), 'month');
	if (n >= 2592000) return pair(Math.floor(n / 2592000), 'month', Math.floor((n % 2592000) / 86400), 'day');
	if (n >= 86400) return pair(Math.floor(n / 86400), 'day', Math.floor((n % 86400) / 3600), 'hour');
	if (n >= 3600) return pair(Math.floor(n / 3600), 'hour', Math.floor((n % 3600) / 60), 'min');
	if (n >= 60) return unit(Math.floor(n / 60), 'min');
	return unit(Math.floor((n + 2.5) / 5) * 5, 'sec');
}

/**
 * dayjs's fromNow, as the original's relative times: "in 2 hours", "3 days ago". `withoutSuffix`
 * is dayjs's fromNow(true), which the notification center shows: "5 minutes".
 */
export function fromNow(datetime: string | undefined | null, now = Date.now(), withoutSuffix = false): string {
	const at = datetime ? Date.parse(datetime) : NaN;
	if (isNaN(at)) return '';
	const diff = at - now;
	const s = Math.abs(diff) / 1000;
	const m = s / 60;
	const h = m / 60;
	const d = h / 24;
	const text =
		s < 45 ? 'a few seconds'
		: s < 90 ? 'a minute'
		: m < 45 ? `${Math.round(m)} minutes`
		: m < 90 ? 'an hour'
		: h < 22 ? `${Math.round(h)} hours`
		: h < 36 ? 'a day'
		: d < 26 ? `${Math.round(d)} days`
		: d < 45 ? 'a month'
		: d < 320 ? `${Math.round(d / 30.4)} months`
		: d < 548 ? 'a year'
		: `${Math.round(d / 365)} years`;
	if (withoutSuffix) return text;
	return diff > 0 ? `in ${text}` : `${text} ago`;
}

/** The original's formatFileSize: bytes as KB, MB or GB with two decimals. */
export function formatFileSize(bytes: number): string {
	const units = ['KB', 'MB', 'GB'];
	let value = bytes / 1024;
	let unit = 0;
	while (value >= 1024 && unit < units.length - 1) {
		value /= 1024;
		unit++;
	}
	return `${value.toFixed(2)} ${units[unit]}`;
}

export const NONE = '–';

export function formatDate(iso: string | undefined | null): string {
	return iso ? new Date(iso).toLocaleString() : '';
}

export function formatSince(iso: string | undefined | null): string {
	if (!iso) return NONE;
	const minutes = Math.floor((Date.now() - new Date(iso).getTime()) / 60000);
	if (minutes < 1) return 'just now';
	if (minutes < 60) return `${minutes}m`;
	const hours = Math.floor(minutes / 60);
	if (hours < 48) return `${hours}h ${minutes % 60}m`;
	return `${Math.floor(hours / 24)}d`;
}

export function formatDuration(from: string, to: string | undefined | null): string {
	const end = to ? new Date(to).getTime() : Date.now();
	const minutes = Math.floor((end - new Date(from).getTime()) / 60000);
	if (minutes < 1) return '<1m';
	const hours = Math.floor(minutes / 60);
	return hours === 0 ? `${minutes}m` : `${hours}h ${minutes % 60}m`;
}
