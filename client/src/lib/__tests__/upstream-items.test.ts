import { afterEach, beforeEach, describe, expect, test, vi } from 'vitest';

// Upstream issues and PRs whose behaviour lives in a client module.

vi.mock('#lib/api/client.js', async (importOriginal) => {
	const actual = await importOriginal<typeof import('#lib/api/client.js')>();
	return { ...actual, api: { get: vi.fn(async () => ({})), post: vi.fn(async () => ({})), put: vi.fn(), patch: vi.fn(), delete: vi.fn(), upload: vi.fn() } };
});
vi.mock('#lib/api/vrchat-auth.svelte.js', () => {
	const vrchatAuth = { ownerUserId: 'usr_me' };
	return { vrchatAuth, ownPath: (rest = '') => `/1/accounts/${encodeURIComponent(vrchatAuth.ownerUserId)}${rest}` };
});

const { api } = await import('#lib/api/client.js');
const { vrchatImageAt } = await import('#lib/api/links.js');
const { openInGame } = await import('#lib/instances/instance-launch.svelte.js');
const { formatDifference, replaceBioSymbols } = await import('#lib/helpers/format.js');

describe('upstream issue 1178', () => {
	test('a VRChat file url becomes the image endpoint at the asked size', () => {
		expect(vrchatImageAt('https://api.vrchat.cloud/api/1/file/file_0e8c4e32-7444-44ea-ade4-313c010d4bae/3/file')).toBe(
			'https://api.vrchat.cloud/api/1/image/file_0e8c4e32-7444-44ea-ade4-313c010d4bae/3/128'
		);
		expect(vrchatImageAt('https://api.vrchat.cloud/api/1/image/file_0e8c4e32-7444-44ea-ade4-313c010d4bae/3/1024', 256)).toBe(
			'https://api.vrchat.cloud/api/1/image/file_0e8c4e32-7444-44ea-ade4-313c010d4bae/3/256'
		);
	});

	test('other urls and nothing are left as they are', () => {
		expect(vrchatImageAt('https://example.com/a.png')).toBe('https://example.com/a.png');
		expect(vrchatImageAt(null)).toBe('');
		expect(vrchatImageAt(undefined)).toBe('');
	});
});

describe('upstream issue 1384', () => {
	beforeEach(() => vi.useFakeTimers());
	afterEach(() => vi.useRealTimers());

	test('a second open in VRChat within a second of the first is ignored', async () => {
		const post = vi.mocked(api.post);
		post.mockClear();
		const location = 'wrld_x:1~private(usr_a)';
		await expect(openInGame(location, 'abc')).resolves.toBe('opened');
		await expect(openInGame(location, 'abc')).resolves.toBe('ignored');
		expect(post).toHaveBeenCalledTimes(1);

		vi.advanceTimersByTime(1000);
		await expect(openInGame(location, 'abc')).resolves.toBe('opened');
		expect(post).toHaveBeenCalledTimes(2);
		expect(post.mock.calls[0][1]).toEqual({ url: 'vrchat://launch?ref=vrcx.app&id=wrld_x:1~private(usr_a)&shortName=abc' });
		vi.advanceTimersByTime(1000);
	});
});

describe('upstream commit 42edf0f08b6a', () => {
	test('replaceBioSymbols puts back the real punctuation and trims the end', () => {
		expect(replaceBioSymbols('discord˸ name＃1234 ／ twitter＠name  ')).toBe('discord: name#1234 ／ twitter@name');
		expect(replaceBioSymbols('a⁄b （c） ǃ？')).toBe('a/b (c) !?');
		expect(replaceBioSymbols(null)).toBe('');
	});
});

describe('upstream commit 080d7737f543', () => {
	test('a bio change marks the removed and added words', () => {
		expect(formatDifference('hello old world', 'hello new world', '+{{text}}', '-{{text}}')).toBe('hello -old +new world');
		expect(formatDifference('a<b', 'a<b c', '+{{text}}', '-{{text}}')).toBe('a&lt;b +c');
	});
});
