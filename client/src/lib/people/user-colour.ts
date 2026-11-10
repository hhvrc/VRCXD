import { tableSettings } from '#lib/settings/table-settings.svelte.js';
import { hueToHex, trustInfo } from '#lib/people/user-display.js';

// The original's $userColour for any user: with "Use Consistent Colors per User" on, a colour from
// the user id (AppApi.GetColourFromUserID: bytes 3 and 4 of the id's MD5), otherwise the rank colour
// with troll and moderator overrides.

const S = [7, 12, 17, 22, 7, 12, 17, 22, 7, 12, 17, 22, 7, 12, 17, 22, 5, 9, 14, 20, 5, 9, 14, 20, 5, 9, 14, 20, 5, 9, 14, 20, 4, 11, 16, 23, 4, 11, 16, 23, 4, 11, 16, 23, 4, 11, 16, 23, 6, 10, 15, 21, 6, 10, 15, 21, 6, 10, 15, 21, 6, 10, 15, 21];
const K = Array.from({ length: 64 }, (_, i) => Math.floor(Math.abs(Math.sin(i + 1)) * 2 ** 32) >>> 0);

/** MD5 of UTF-8 text. Only for colours; nothing here is security sensitive. */
export function md5(text: string): Uint8Array {
	const input = new TextEncoder().encode(text);
	const length = (((input.length + 8) >> 6) + 1) * 64;
	const bytes = new Uint8Array(length);
	bytes.set(input);
	bytes[input.length] = 0x80;
	const view = new DataView(bytes.buffer);
	view.setUint32(length - 8, (input.length * 8) >>> 0, true);
	view.setUint32(length - 4, Math.floor(input.length / 0x20000000), true);
	let [a0, b0, c0, d0] = [0x67452301, 0xefcdab89, 0x98badcfe, 0x10325476];
	for (let offset = 0; offset < length; offset += 64) {
		let [a, b, c, d] = [a0, b0, c0, d0];
		for (let i = 0; i < 64; i++) {
			let f: number;
			let g: number;
			if (i < 16) [f, g] = [(b & c) | (~b & d), i];
			else if (i < 32) [f, g] = [(d & b) | (~d & c), (5 * i + 1) % 16];
			else if (i < 48) [f, g] = [b ^ c ^ d, (3 * i + 5) % 16];
			else [f, g] = [c ^ (b | ~d), (7 * i) % 16];
			const sum = (a + f + K[i] + view.getUint32(offset + g * 4, true)) >>> 0;
			[a, d, c] = [d, c, b];
			b = (b + ((sum << S[i]) | (sum >>> (32 - S[i])))) >>> 0;
		}
		[a0, b0, c0, d0] = [(a0 + a) >>> 0, (b0 + b) >>> 0, (c0 + c) >>> 0, (d0 + d) >>> 0];
	}
	const out = new Uint8Array(16);
	const outView = new DataView(out.buffer);
	[a0, b0, c0, d0].forEach((word, i) => outView.setUint32(i * 4, word, true));
	return out;
}

const hues = new Map<string, number>();

/** The original's GetColourFromUserID. */
export function nameHue(userId: string): number {
	let hue = hues.get(userId);
	if (hue === undefined) {
		const hash = md5(userId);
		hue = (hash[3] << 8) | hash[4];
		hues.set(userId, hue);
	}
	return hue;
}

export type ColourableUser = { id?: string; userId?: string; tags?: string[] | null; developerType?: string | null; nameHue?: number };

/** The colour the original gives this user's name. */
export function nameColour(user: ColourableUser, dark: boolean): string {
	const id = user.id ?? user.userId ?? '';
	if (tableSettings.randomUserColours && id) return hueToHex(user.nameHue ?? nameHue(id), dark);
	return trustInfo(user.tags ?? [], user.developerType ?? undefined).color;
}
