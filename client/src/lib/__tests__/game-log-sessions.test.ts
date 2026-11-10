import { describe, expect, test } from 'vitest';
import { buildGameLogSessions, isGroup, searchSessions, type GameLogRow, type SessionEvent } from '../activity/game-log-sessions.js';

// The original's GameLogSessions behaviour: events under the stay they happened in, bursts of joins
// and leaves folded, repeated videos counted, and the sessions search.
const T0 = Date.parse('2026-11-01T12:00:00Z');
const at = (seconds: number) => new Date(T0 + seconds * 1000).toISOString();
let nextId = 1;

function row(type: string, seconds: number, extra: Partial<GameLogRow> = {}): GameLogRow {
	const id = nextId++;
	return { key: `${type}:${id}`, id, type, createdAt: at(seconds), isFriend: false, isFavorite: false, ...extra };
}
const stay = (seconds: number, location: string, worldName = 'World') => row('Location', seconds, { location, worldName, timeMs: 60_000 });
const join = (seconds: number, userId: string, location: string) => row('OnPlayerJoined', seconds, { userId, displayName: userId, location });
const left = (seconds: number, userId: string, location: string) => row('OnPlayerLeft', seconds, { userId, displayName: userId, location });
const video = (seconds: number, videoUrl: string, location: string) => row('VideoPlay', seconds, { videoUrl, location });
const userIds = (events: unknown[]) => (events as SessionEvent[]).map((e) => e.userId);

const A = 'wrld_a:1';
const B = 'wrld_b:2';

describe('buildGameLogSessions', () => {
	test('no stays gives no sessions', () => {
		expect(buildGameLogSessions([], [join(1, 'usr_x', A)])).toEqual([]);
	});

	test('events land in the stay they happened in, newest stay and event first', () => {
		const sessions = buildGameLogSessions([stay(0, A, 'Alpha'), stay(100, B, 'Beta')], [join(10, 'usr_1', A), join(20, 'usr_2', A), join(110, 'usr_3', B)]);
		expect(sessions.map((s) => s.worldName)).toEqual(['Beta', 'Alpha']);
		expect(userIds(sessions[1].events)).toEqual(['usr_2', 'usr_1']);
		expect(sessions[0].events).toHaveLength(1);
	});

	test('an event without a location goes to the stay that was current', () => {
		const sessions = buildGameLogSessions([stay(0, A), stay(100, B)], [row('Event', 50, { data: 'hello' })]);
		expect(sessions[1].events).toHaveLength(1);
		expect(sessions[0].events).toHaveLength(0);
	});

	test('duplicate rows are counted once', () => {
		const event = join(10, 'usr_1', A);
		const sessions = buildGameLogSessions([stay(0, A)], [event, { ...event, key: 'copy', id: 999 }]);
		expect(sessions[0].events).toHaveLength(1);
	});

	test('five or more joins on arrival fold into one group', () => {
		const joins = ['a', 'b', 'c', 'd', 'e'].map((u, i) => join(1 + i * 0.5, `usr_${u}`, A));
		const sessions = buildGameLogSessions([stay(0, A)], [...joins, join(600, 'usr_late', A)]);
		const groups = sessions[0].events.filter(isGroup);
		expect(groups).toHaveLength(1);
		expect(groups[0].type).toBe('JoinGroup');
		expect(groups[0].count).toBe(5);
		expect(sessions[0].events).toHaveLength(2);
	});

	test('four joins stay as single rows', () => {
		const joins = ['a', 'b', 'c', 'd'].map((u, i) => join(1 + i, `usr_${u}`, A));
		const sessions = buildGameLogSessions([stay(0, A)], joins);
		expect(sessions[0].events.some(isGroup)).toBe(false);
		expect(sessions[0].events).toHaveLength(4);
	});

	test('a burst of leaves at the end folds into one group', () => {
		const leaves = ['a', 'b', 'c', 'd', 'e', 'f'].map((u, i) => left(300 + i, `usr_${u}`, A));
		const sessions = buildGameLogSessions([stay(0, A)], leaves);
		const groups = sessions[0].events.filter(isGroup);
		expect(groups).toHaveLength(1);
		expect(groups[0].type).toBe('LeftGroup');
		expect(groups[0].members.map((m) => m.userId)).toContain('usr_f');
	});

	test('someone who joined as we arrived and left in the same moment is not shown leaving', () => {
		const sessions = buildGameLogSessions([stay(0, A)], [join(1, 'usr_1', A), left(2, 'usr_1', A), left(200, 'usr_2', A)]);
		expect(sessions[0].events.map((e) => e.type)).toEqual(['OnPlayerLeft', 'OnPlayerJoined']);
		expect(userIds(sessions[0].events)[0]).toBe('usr_2');
	});

	test('the same video played back to back is one row with a count', () => {
		const sessions = buildGameLogSessions([stay(0, A)], [video(10, 'https://v/1', A), video(20, 'https://v/1', A), video(30, 'https://v/2', A)]);
		const plays = sessions[0].events as SessionEvent[];
		expect(plays).toHaveLength(2);
		expect(plays.find((p) => p.videoUrl === 'https://v/1')?.playCount).toBe(2);
		expect(plays.find((p) => p.videoUrl === 'https://v/2')?.playCount).toBe(1);
	});

	test('events from before the stay began are dropped', () => {
		const sessions = buildGameLogSessions([stay(100, A)], [join(10, 'usr_early', A), join(150, 'usr_1', A)]);
		expect(userIds(sessions[0].events)).toEqual(['usr_1']);
	});
});

describe('searchSessions', () => {
	const sessions = () =>
		buildGameLogSessions(
			[stay(0, A, 'Alpha Club'), stay(1000, B, 'Beta')],
			[...['ann', 'bob', 'cat', 'dan', 'eve'].map((u, i) => join(1 + i * 0.2, u, A)), join(1100, 'zed', B), video(1200, 'https://video/song', B)]
		);

	test('no search keeps every stay, empty ones too', () => {
		const some = buildGameLogSessions([stay(0, A), stay(100, B)], [join(110, 'x', B)]);
		expect(searchSessions(some, '', 10)).toHaveLength(2);
	});

	test('no search is not cut at the search limit', () => {
		const many = buildGameLogSessions([stay(0, A), stay(100, B), stay(200, A)], []);
		expect(searchSessions(many, '', 1)).toHaveLength(3);
	});

	test('a world name match keeps the whole stay', () => {
		const found = searchSessions(sessions(), 'alpha', 10);
		expect(found).toHaveLength(1);
		expect(found[0].events.some(isGroup)).toBe(true);
	});

	test('a member of a folded group is found on its own row', () => {
		const found = searchSessions(sessions(), 'CAT', 10);
		expect(found).toHaveLength(1);
		expect(found[0].events).toHaveLength(1);
		expect(found[0].events[0].type).toBe('OnPlayerJoined');
		expect((found[0].events[0] as SessionEvent).displayName).toBe('cat');
	});

	test('video urls are searched and the limit applies', () => {
		expect(searchSessions(sessions(), 'video/song', 10)[0].events).toHaveLength(1);
		expect(searchSessions(sessions(), 'a', 1)).toHaveLength(1);
	});
});
