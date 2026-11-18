import { readdirSync, readFileSync } from 'node:fs';
import { join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, test } from 'vitest';

// The ratchet for the target architecture (AGENTS.md): a feature that has a live store is read
// from the store, and components and pages do not ask the REST client for it themselves. What
// still does is listed below; the list may only shrink, and nothing new may join it.

const src = fileURLToPath(new URL('../../', import.meta.url));

function svelteFiles(dir: string): string[] {
	return readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
		const path = join(dir, entry.name);
		if (entry.isDirectory()) return svelteFiles(path);
		return entry.name.endsWith('.svelte') ? [path] : [];
	});
}

/**
 * The friends feature's REST paths (server/API/Controller/Friends), as `friends`, `friend-log` or
 * `friend-requests` at the start of a path segment. Other features' routes that merely end in
 * /friends (the hot world's friends, the tools export) are not the feature.
 */
const friendsPath = /\/(friends|friend-log|friend-requests)(?=[/'"`?])/g;
const otherFeature = /(\/tools|hot-worlds\/[^'"`]*)$/;

function friendsRequests(source: string): string[] {
	const found: string[] = [];
	for (const line of source.split('\n')) {
		// A path is often built on one line and requested on another, so every line counts but comments.
		if (/^\s*(\/\/|\*|\/\*|<!--)/.test(line)) continue;
		for (const match of line.matchAll(friendsPath)) {
			if (otherFeature.test(line.slice(0, match.index))) continue;
			found.push(match[1]);
		}
	}
	return found;
}

/**
 * Violations that predate the rule, as "file: path segment". There are none left: the friends
 * requests live in lib/people/friends-api.ts. Never add a line to make a change pass.
 */
const notYetMoved: string[] = [];

function violations(): string[] {
	const found = new Set<string>();
	for (const dir of ['lib/components', 'routes']) {
		for (const file of svelteFiles(join(src, dir))) {
			const name = relative(src, file).replaceAll('\\', '/');
			for (const segment of friendsRequests(readFileSync(file, 'utf8'))) found.add(`${name}: ${segment}`);
		}
	}
	return [...found].sort();
}

describe('architecture ratchet', () => {
	test('the scan sees a friends request and leaves other features alone', () => {
		expect(friendsRequests("const a = await api.get<Friend[]>(accountPath(owner, '/friends'));")).toEqual(['friends']);
		expect(friendsRequests('await api.delete(`${userPath}/friend-requests/${id}`);')).toEqual(['friend-requests']);
		expect(friendsRequests("await api.get(ownPath('/friend-log'), { limit: 50 });")).toEqual(['friend-log']);
		expect(friendsRequests('const base = `${userPath}/friend-requests/${encodedId}`;')).toEqual(['friend-requests']);
		expect(friendsRequests('// The original over GET /1/accounts/{id}/friends/details.')).toEqual([]);
		expect(friendsRequests("await api.get(ownPath('/tools/friends'));")).toEqual([]);
		expect(friendsRequests('await api.get(ownPath(`/hot-worlds/${id}/friends`));')).toEqual([]);
		expect(friendsRequests("'friends-locations': () => import('../routes/friends-locations/+page.svelte'),")).toEqual([]);
	});

	test('no component or page newly asks the REST client for friends', () => {
		expect(violations().filter((v) => !notYetMoved.includes(v))).toEqual([]);
	});

	test('the allow-list holds only what still violates', () => {
		const now = violations();
		expect(notYetMoved.filter((v) => !now.includes(v))).toEqual([]);
	});
});
