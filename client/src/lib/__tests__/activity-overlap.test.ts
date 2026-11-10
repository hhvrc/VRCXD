import { describe, expect, test } from 'vitest';
import {
	buildHeatmapBuckets,
	buildOverlapBuckets,
	buildSessionsFromEvents,
	buildSessionsFromGamelog,
	clipSessionsToRange,
	computeOverlapView,
	findBestOverlapTimeFromBuckets,
	overlapNormalizeConfig,
	type Session
} from '../activity/activity-engine.js';

// The original's overlapCalculator.js tests. That file only wrapped activityEngine.js, which the
// port has as activity-engine.ts; the grid helpers are the same buckets laid out per day.

const ts = (s: string) => new Date(s).getTime();
const at = (s: string) => new Date(s).toISOString();
const HOUR = 3600000;
const sum = (values: number[]) => values.reduce((a, b) => a + b, 0);

describe('buildSessionsFromEvents', () => {
	test('no events, no sessions', () => {
		expect(buildSessionsFromEvents([]).sessions).toEqual([]);
	});

	test('an Online and the Offline after it are one session', () => {
		const { sessions } = buildSessionsFromEvents([
			{ createdAt: at('2025-01-06T10:00:00Z'), type: 'Online' },
			{ createdAt: at('2025-01-06T12:00:00Z'), type: 'Offline' }
		]);
		expect(sessions).toEqual([{ start: ts('2025-01-06T10:00:00Z'), end: ts('2025-01-06T12:00:00Z') }]);
	});

	test('each pair is its own session', () => {
		const { sessions } = buildSessionsFromEvents([
			{ createdAt: at('2025-01-06T10:00:00Z'), type: 'Online' },
			{ createdAt: at('2025-01-06T12:00:00Z'), type: 'Offline' },
			{ createdAt: at('2025-01-06T14:00:00Z'), type: 'Online' },
			{ createdAt: at('2025-01-06T16:00:00Z'), type: 'Offline' }
		]);
		expect(sessions).toEqual([
			{ start: ts('2025-01-06T10:00:00Z'), end: ts('2025-01-06T12:00:00Z') },
			{ start: ts('2025-01-06T14:00:00Z'), end: ts('2025-01-06T16:00:00Z') }
		]);
	});

	test('a second Online closes the session before it', () => {
		const { sessions } = buildSessionsFromEvents([
			{ createdAt: at('2025-01-06T10:00:00Z'), type: 'Online' },
			{ createdAt: at('2025-01-06T12:00:00Z'), type: 'Online' },
			{ createdAt: at('2025-01-06T14:00:00Z'), type: 'Offline' }
		]);
		expect(sessions).toEqual([
			{ start: ts('2025-01-06T10:00:00Z'), end: ts('2025-01-06T12:00:00Z') },
			{ start: ts('2025-01-06T12:00:00Z'), end: ts('2025-01-06T14:00:00Z') }
		]);
	});

	test('an Offline with no Online before it is ignored', () => {
		const { sessions } = buildSessionsFromEvents([
			{ createdAt: at('2025-01-06T10:00:00Z'), type: 'Offline' },
			{ createdAt: at('2025-01-06T12:00:00Z'), type: 'Online' },
			{ createdAt: at('2025-01-06T14:00:00Z'), type: 'Offline' }
		]);
		expect(sessions).toEqual([{ start: ts('2025-01-06T12:00:00Z'), end: ts('2025-01-06T14:00:00Z') }]);
	});

	test('an Online with no Offline yet stays open, not a session', () => {
		const result = buildSessionsFromEvents([{ createdAt: at('2025-01-06T10:00:00Z'), type: 'Online' }]);
		expect(result.sessions).toEqual([]);
		expect(result.pendingSessionStartAt).toBe(ts('2025-01-06T10:00:00Z'));
	});
});

describe('buildSessionsFromGamelog', () => {
	test('no rows, no sessions', () => {
		expect(buildSessionsFromGamelog([])).toEqual([]);
	});

	test('a row without a time lasts until the next row, and touching sessions merge', () => {
		const sessions = buildSessionsFromGamelog([
			{ createdAt: at('2025-01-06T10:00:00Z'), time: 0 },
			{ createdAt: at('2025-01-06T12:00:00Z'), time: HOUR }
		]);
		expect(sessions).toHaveLength(1);
		expect(sessions[0].start).toBe(ts('2025-01-06T10:00:00Z'));
		expect(sessions[0].end).toBe(ts('2025-01-06T13:00:00Z'));
	});

	test('the last row without a time lasts at most a day', () => {
		const now = Date.now();
		const sessions = buildSessionsFromGamelog([{ createdAt: new Date(now - 48 * HOUR).toISOString(), time: 0 }], undefined, now);
		expect(sessions).toHaveLength(1);
		expect(sessions[0].end - sessions[0].start).toBe(24 * HOUR);
	});

	test('sessions within five minutes of each other merge', () => {
		const sessions = buildSessionsFromGamelog([
			{ createdAt: at('2025-01-06T10:00:00Z'), time: HOUR },
			{ createdAt: at('2025-01-06T11:03:00Z'), time: HOUR }
		]);
		expect(sessions).toHaveLength(1);
		expect(sessions[0].start).toBe(ts('2025-01-06T10:00:00Z'));
		expect(sessions[0].end).toBe(ts('2025-01-06T12:03:00Z'));
	});

	test('a longer gap keeps them apart', () => {
		const sessions = buildSessionsFromGamelog([
			{ createdAt: at('2025-01-06T10:00:00Z'), time: HOUR },
			{ createdAt: at('2025-01-06T12:00:00Z'), time: HOUR }
		]);
		expect(sessions).toHaveLength(2);
	});

	test('the merge gap can be set', () => {
		const sessions = buildSessionsFromGamelog(
			[
				{ createdAt: at('2025-01-06T10:00:00Z'), time: HOUR },
				{ createdAt: at('2025-01-06T11:03:00Z'), time: HOUR }
			],
			60000
		);
		expect(sessions).toHaveLength(2);
	});
});

describe('clipSessionsToRange', () => {
	const sessions: Session[] = [
		{ start: ts('2025-01-01T00:00:00Z'), end: ts('2025-01-01T02:00:00Z') },
		{ start: ts('2025-01-10T00:00:00Z'), end: ts('2025-01-10T02:00:00Z') },
		{ start: ts('2025-01-20T00:00:00Z'), end: ts('2025-01-20T02:00:00Z') }
	];

	test('a cutoff before every session keeps them all', () => {
		expect(clipSessionsToRange(sessions, ts('2024-12-01T00:00:00Z'))).toHaveLength(3);
	});

	test('sessions that ended before the cutoff go', () => {
		const result = clipSessionsToRange(sessions, ts('2025-01-05T00:00:00Z'));
		expect(result).toHaveLength(2);
		expect(result[0].start).toBe(ts('2025-01-10T00:00:00Z'));
	});

	test('a session across the cutoff starts at the cutoff', () => {
		const cutoff = ts('2025-01-10T01:00:00Z');
		const result = clipSessionsToRange(sessions, cutoff);
		expect(result).toHaveLength(2);
		expect(result[0].start).toBe(cutoff);
		expect(result[0].end).toBe(ts('2025-01-10T02:00:00Z'));
	});

	test('a cutoff after every session keeps none', () => {
		expect(clipSessionsToRange(sessions, ts('2025-02-01T00:00:00Z'))).toHaveLength(0);
	});
});

describe('overlap', () => {
	const now = ts('2025-01-08T00:00:00Z');
	const view = (self: Session[], target: Session[]) =>
		computeOverlapView({ selfSessions: self, targetSessions: target, dayLabels: ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'], rangeDays: 7, nowMs: now, normalizeConfig: overlapNormalizeConfig(7) });
	const s = (from: string, to: string): Session => ({ start: ts(from), end: ts(to) });

	test('sessions that never meet do not overlap', () => {
		const a = [s('2025-01-06T10:00:00Z', '2025-01-06T12:00:00Z')];
		const b = [s('2025-01-06T14:00:00Z', '2025-01-06T16:00:00Z')];
		const buckets = buildOverlapBuckets(a, b, 0, now);
		expect(sum(buckets)).toBe(0);
		expect(Math.max(...buckets)).toBe(0);
		expect(view(a, b).overlapPercent).toBe(0);
	});

	test('identical sessions overlap fully', () => {
		const a = [s('2025-01-06T10:00:00Z', '2025-01-06T12:00:00Z')];
		expect(sum(buildOverlapBuckets(a, a, 0, now))).toBe(120);
		expect(view(a, a).overlapPercent).toBe(100);
	});

	test('half the time together is fifty percent', () => {
		const a = [s('2025-01-06T10:00:00Z', '2025-01-06T14:00:00Z')];
		const b = [s('2025-01-06T12:00:00Z', '2025-01-06T16:00:00Z')];
		expect(sum(buildOverlapBuckets(a, b, 0, now))).toBe(120);
		expect(view(a, b).overlapPercent).toBe(50);
	});

	test('the buckets are a week of hours, filled where the time falls', () => {
		const a = [s('2025-01-06T10:00:00Z', '2025-01-06T12:00:00Z')];
		const buckets = buildOverlapBuckets(a, a, 0, now);
		expect(buckets).toHaveLength(168);
		const start = new Date(ts('2025-01-06T10:00:00Z'));
		expect(buckets[start.getDay() * 24 + start.getHours()]).toBe(60);
		expect(Math.max(...buckets)).toBeGreaterThan(0);
	});

	test('nothing on either side is no overlap', () => {
		const a = [s('2025-01-06T10:00:00Z', '2025-01-06T12:00:00Z')];
		expect(sum(buildOverlapBuckets([], a, 0, now))).toBe(0);
		expect(view([], a).overlapPercent).toBe(0);
		expect(sum(buildOverlapBuckets(a, [], 0, now))).toBe(0);
		expect(view(a, []).overlapPercent).toBe(0);
	});

	test('the percentage is of the shorter online time', () => {
		const a = [s('2025-01-06T12:00:00Z', '2025-01-06T14:00:00Z')];
		const b = [s('2025-01-06T10:00:00Z', '2025-01-06T14:00:00Z')];
		expect(view(a, b).overlapPercent).toBe(100);
		expect(sum(buildHeatmapBuckets(a, 0, now))).toBe(120);
		expect(sum(buildHeatmapBuckets(b, 0, now))).toBe(240);
	});

	test('no sessions make an empty heatmap', () => {
		const buckets = buildHeatmapBuckets([], 0, now);
		expect(buckets).toHaveLength(168);
		expect(buckets.every((v) => v === 0)).toBe(true);
	});
});

describe('findBestOverlapTimeFromBuckets', () => {
	const days = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
	const empty = () => new Array<number>(168).fill(0);

	test('nothing for an empty week', () => {
		expect(findBestOverlapTimeFromBuckets(empty(), days)).toBe('');
	});

	test('neighbouring strong hours make one range', () => {
		const buckets = empty();
		for (const hour of [20, 21, 22]) buckets[6 * 24 + hour] = 10;
		expect(findBestOverlapTimeFromBuckets(buckets, days)).toBe('Sat, 20:00-23:00');
	});

	test('the day with the most time in those hours', () => {
		const buckets = empty();
		buckets[1 * 24 + 20] = 3;
		buckets[5 * 24 + 20] = 8;
		expect(findBestOverlapTimeFromBuckets(buckets, days)).toContain('Fri');
	});

	test('a tie goes to the first day', () => {
		const buckets = empty();
		for (let day = 0; day < 7; day++) buckets[day * 24 + 15] = 5;
		expect(findBestOverlapTimeFromBuckets(buckets, days)).toBe('Sun, 15:00-16:00');
	});
});
