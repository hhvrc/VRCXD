// The original's user transforms: rank, its colour and sort number from a user's tags, name colour
// from a hue, and languages from tags.
import { appearance, type TrustColorKey } from '#lib/settings/appearance.svelte.js';

/** The default rank colours; the ones in use are the User Colors setting (appearance.trustColor). */
export function trustInfo(tags: readonly string[], developerType?: string | null): { className: string; colorKey: TrustColorKey; color: string; sortNum: number; label: string } {
	let className = 'x-tag-untrusted';
	let colorKey: TrustColorKey = 'untrusted';
	let sortNum = 1;
	let label = 'Visitor';
	if (tags.includes('system_trust_veteran')) {
		[className, colorKey, sortNum] = ['x-tag-veteran', 'veteran', 5];
		label = 'Trusted User';
	} else if (tags.includes('system_trust_trusted')) {
		[className, colorKey, sortNum] = ['x-tag-trusted', 'trusted', 4];
		label = 'Known User';
	} else if (tags.includes('system_trust_known')) {
		[className, colorKey, sortNum] = ['x-tag-known', 'known', 3];
		label = 'User';
	} else if (tags.includes('system_trust_basic')) {
		[className, colorKey, sortNum] = ['x-tag-basic', 'basic', 2];
		label = 'New User';
	}
	const isTroll = tags.includes('system_troll') || tags.includes('system_probable_troll');
	const isModerator = (!!developerType && developerType !== 'none') || tags.includes('admin_moderator');
	if (isTroll) {
		colorKey = 'troll';
		sortNum += 0.1;
	}
	if (isModerator) {
		colorKey = 'vip';
		sortNum += 0.3;
	}
	return { className, colorKey, color: appearance.trustColor.value[colorKey], sortNum, label };
}

function hsvToHex(h: number, s: number, v: number): string {
	const i = Math.floor(h * 6);
	const f = h * 6 - i;
	const p = v * (1 - s);
	const q = v * (1 - f * s);
	const t = v * (1 - (1 - f) * s);
	const [r, g, b] = [
		[v, t, p],
		[q, v, p],
		[p, v, t],
		[p, q, v],
		[t, p, v],
		[v, p, q]
	][i % 6];
	const part = (value: number) => Math.round(value * 255).toString(16).padStart(2, '0');
	return `#${part(r)}${part(g)}${part(b)}`;
}

export function hueToHex(hue: number, dark: boolean): string {
	return dark ? hsvToHex(hue / 65535, 0.6, 1) : hsvToHex(hue / 65535, 1, 0.7);
}

const languageNames = new Intl.DisplayNames(['en'], { type: 'language', fallback: 'code' });

export function languagesOf(tags: readonly string[]): { key: string; value: string }[] {
	return tags
		.filter((tag) => tag.startsWith('language_'))
		.map((tag) => {
			const key = tag.slice('language_'.length);
			let value = key;
			try {
				value = languageNames.of(key) ?? key;
			} catch {
				// Not a code the browser knows: the code it is.
			}
			return { key, value };
		});
}

const languageMappings: Record<string, string> = {
    eng: 'us',
    kor: 'kr',
    rus: 'ru',
    spa: 'es',
    por: 'pt',
    zho: 'cn',
    deu: 'de',
    jpn: 'jp',
    fra: 'fr',
    swe: 'se',
    nld: 'nl',
    pol: 'pl',
    dan: 'dk',
    nor: 'no',
    ita: 'it',
    tha: 'th',
    fin: 'fi',
    hun: 'hu',
    ces: 'cz',
    tur: 'tr',
    ara: 'ae',
    ron: 'ro',
    vie: 'vn',
    ukr: 'ua',
    ase: 'us',
    bfi: 'gb',
    dse: 'nl',
    fsl: 'fr',
    jsl: 'jp',
    kvk: 'kr',

    mlt: 'mt',
    ind: 'id',
    hrv: 'hr',
    heb: 'he',
    afr: 'af',
    ben: 'be',
    bul: 'bg',
    cmn: 'cn',
    cym: 'cy',
    ell: 'el',
    est: 'et',
    fil: 'ph',
    gla: 'gd',
    gle: 'ga',
    hin: 'hi',
    hmn: 'cn',
    hye: 'hy',
    isl: 'is',
    lav: 'lv',
    lit: 'lt',
    ltz: 'lb',
    mar: 'hi',
    mkd: 'mk',
    msa: 'my',
    sco: 'gd',
    slk: 'sk',
    slv: 'sl',
    tel: 'hi',
    mri: 'nz',
    wuu: 'cn',
    yue: 'cn',
    tws: 'cn',
    asf: 'au',
    nzs: 'nz',
    gsg: 'de',
    epo: 'eo',
    tok: 'tok'
};


/** The flag class for a language code. */
export function languageClass(key: string): string {
	return languageMappings[key] ?? 'unknown';
}

const STATUS_ORDER = ['join me', 'active', 'ask me', 'busy', 'offline'];

/** The original's sortStatus: join me, active, ask me, busy, offline. */
export function sortStatus(a: string, b: string): number {
	const left = STATUS_ORDER.indexOf(a);
	const right = STATUS_ORDER.indexOf(b);
	if (left < 0 || right < 0 || left === right) return 0;
	return left < right ? -1 : 1;
}

/**
 * The original's userOnlineForTimestamp and the label beside it: "Online For" only while online
 * with a time, "Offline For" otherwise (active included), and no time when none is known.
 */
export function userOnlineFor(state: string | undefined | null, since: string | undefined | null): { online: boolean; at: string | null } {
	const at = since || null;
	return { online: state === 'online' && !!at, at };
}

/** A Friend or Unfriend row of the account's friend log, as GET .../users/{id}/together sends them, newest first. */
export type FriendLogDate = { type: string; createdAt: string };

/**
 * The original's dateFriendedInfo: the user's Friend rows, and Unfriend rows unless they are hidden,
 * newest first. The newest says whether the row reads Friended or Unfriended and from when.
 */
export function friendedInfo(log: FriendLogDate[] | undefined, hideUnfriends: boolean): { unfriended: boolean; at: string | null; entries: FriendLogDate[] } {
	const entries = (log ?? []).filter((e) => e.type === 'Friend' || (e.type === 'Unfriend' && !hideUnfriends));
	const latest = entries[0];
	return { unfriended: latest?.type === 'Unfriend', at: latest?.createdAt ?? null, entries };
}
