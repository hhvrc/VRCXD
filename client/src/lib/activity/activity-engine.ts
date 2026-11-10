// The original's activityEngine (src/shared/utils/activityEngine.js) and the normalize settings of
// its activity store: online sessions from presence events or the account's own visits, an hourly
// week heatmap from them, the overlap of two people's sessions, peaks and a daily summary.

export const ONLINE_SESSION_MERGE_GAP_MS = 5 * 60 * 1000;
export const DEFAULT_MAX_SESSION_MS = 8 * 60 * 60 * 1000;

export type Session = { start: number; end: number; isOpenTail?: boolean; sourceRevision?: string };
export type PresenceRow = { createdAt: string; type: 'Online' | 'Offline' };
export type VisitRow = { createdAt: string; time: number };
export type NormalizeConfig = {
	floorPercentile?: number;
	capPercentile?: number;
	rankWeight?: number;
	targetCoverage?: number;
	targetVolume?: number;
	rangeDays?: number;
};
export type ExcludeHours = { startHour: number; endHour: number };

export function buildSessionsFromEvents(events: PresenceRow[], initialStart: number | null = null) {
	const sessions: Session[] = [];
	let currentStart = initialStart;
	for (const event of events) {
		const ts = new Date(event.createdAt).getTime();
		if (event.type === 'Online') {
			if (currentStart !== null) sessions.push({ start: currentStart, end: ts });
			currentStart = ts;
		} else if (event.type === 'Offline' && currentStart !== null) {
			sessions.push({ start: currentStart, end: ts });
			currentStart = null;
		}
	}
	return { pendingSessionStartAt: currentStart, sessions };
}

export function buildSessionsFromGamelog(rows: VisitRow[], mergeGapMs = ONLINE_SESSION_MERGE_GAP_MS, nowMs = Date.now()): Session[] {
	if (rows.length === 0) return [];
	const rawSessions: Session[] = [];
	for (let i = 0; i < rows.length; i++) {
		const start = new Date(rows[i].createdAt).getTime();
		let duration = rows[i].time || 0;
		if (duration === 0) {
			duration = i < rows.length - 1 ? new Date(rows[i + 1].createdAt).getTime() - start : nowMs - start;
			duration = Math.min(duration, 24 * 60 * 60 * 1000);
		}
		if (duration > 0) rawSessions.push({ start, end: start + duration });
	}
	rawSessions.sort((a, b) => a.start - b.start);
	return mergeSessions([], rawSessions, mergeGapMs);
}

export function mergeSessions(olderSessions: Session[], newerSessions: Session[], mergeGapMs = ONLINE_SESSION_MERGE_GAP_MS): Session[] {
	const all = [...olderSessions.map(cloneSession), ...newerSessions.map(cloneSession)];
	if (all.length === 0) return [];
	all.sort((a, b) => a.start - b.start);
	const merged = [all[0]];
	for (let i = 1; i < all.length; i++) {
		const last = merged[merged.length - 1];
		const current = all[i];
		if (current.start <= last.end + mergeGapMs) {
			last.end = Math.max(last.end, current.end);
			last.isOpenTail = last.isOpenTail || current.isOpenTail;
			last.sourceRevision = current.sourceRevision || last.sourceRevision || '';
		} else {
			merged.push(current);
		}
	}
	return merged;
}

export function clipSessionsToRange(sessions: Session[], rangeStartMs: number, rangeEndMs = Date.now()): Session[] {
	return sessions
		.filter((session) => session.end > rangeStartMs && session.start < rangeEndMs)
		.map((session) => ({ ...session, start: Math.max(session.start, rangeStartMs), end: Math.min(session.end, rangeEndMs) }))
		.filter((session) => session.end > session.start);
}

export function buildHeatmapBuckets(sessions: Session[], windowStartMs: number, nowMs: number, maxSessionMs = DEFAULT_MAX_SESSION_MS): number[] {
	const buckets = new Float64Array(168);
	for (const session of sessions) {
		const effectiveEnd = Math.min(session.end, session.start + maxSessionMs);
		const start = Math.max(session.start, windowStartMs);
		const end = Math.min(effectiveEnd, nowMs);
		if (end <= start) continue;
		let cursor = start;
		while (cursor < end) {
			const date = new Date(cursor);
			const slot = date.getDay() * 24 + date.getHours();
			date.setHours(date.getHours() + 1, 0, 0, 0);
			const segmentEnd = Math.min(date.getTime(), end);
			buckets[slot] += (segmentEnd - cursor) / 60000;
			cursor = segmentEnd;
		}
	}
	return Array.from(buckets);
}

export function buildOverlapBuckets(selfSessions: Session[], targetSessions: Session[], windowStartMs: number, nowMs: number, maxSessionMs = DEFAULT_MAX_SESSION_MS): number[] {
	const intersections: Session[] = [];
	let leftIndex = 0;
	let rightIndex = 0;
	while (leftIndex < selfSessions.length && rightIndex < targetSessions.length) {
		const left = selfSessions[leftIndex];
		const right = targetSessions[rightIndex];
		const leftEnd = Math.min(left.end, left.start + maxSessionMs);
		const rightEnd = Math.min(right.end, right.start + maxSessionMs);
		const start = Math.max(left.start, right.start);
		const end = Math.min(leftEnd, rightEnd);
		if (start < end) intersections.push({ start, end });
		if (leftEnd < rightEnd) leftIndex++;
		else rightIndex++;
	}
	return buildHeatmapBuckets(intersections, windowStartMs, nowMs, maxSessionMs);
}

export function normalizeBuckets(buckets: number[], config: NormalizeConfig): number[] {
	const { floorPercentile = 15, capPercentile = 85, rankWeight = 0.2, targetCoverage = 0.25, targetVolume = 60, rangeDays = 30 } = config;
	const positiveEntries: { value: number; index: number }[] = [];
	for (let i = 0; i < 168; i++) if (buckets[i] > 0) positiveEntries.push({ value: buckets[i], index: i });
	if (positiveEntries.length === 0) return Array.from({ length: 168 }, () => 0);

	const sortedValues = positiveEntries.map((e) => e.value).sort((a, b) => a - b);
	const floor = percentile(sortedValues, floorPercentile);
	const cap = percentile(sortedValues, capPercentile);
	const logFloor = Math.log1p(floor);
	const logRange = Math.log1p(cap) - logFloor;

	const gated = positiveEntries.filter((e) => e.value >= floor);
	if (gated.length === 0) return Array.from({ length: 168 }, () => 0);

	gated.sort((a, b) => a.value - b.value);
	const count = gated.length;
	const ampWeight = 1 - rankWeight;
	const normalized = new Float64Array(168);
	const tiedRanks = computeTiedRankScores(gated);
	for (let rank = 0; rank < count; rank++) {
		const { value, index } = gated[rank];
		const base = logRange > 1e-9 ? Math.max((Math.log1p(value) - logFloor) / logRange, 0) : 0.5;
		normalized[index] = Math.min(base, 1) * ampWeight + tiedRanks[rank] * rankWeight;
	}

	const coverage = count / 168;
	const volume = gated.reduce((total, e) => total + e.value, 0) / rangeDays;
	const confidence = Math.min(Math.max(Math.min(coverage / targetCoverage, volume / targetVolume), 0), 1);
	for (let i = 0; i < 168; i++) normalized[i] = Math.min(normalized[i] * confidence, 1);
	return Array.from(normalized);
}

export function computePeaksFromBuckets(buckets: number[], dayLabels: string[]) {
	const grid = bucketsToGrid(buckets);
	const daySums = new Array<number>(7).fill(0);
	const hourSums = new Array<number>(24).fill(0);
	for (let day = 0; day < 7; day++) {
		for (let hour = 0; hour < 24; hour++) {
			daySums[day] += grid[day][hour];
			hourSums[hour] += grid[day][hour];
		}
	}
	const maxDaySum = Math.max(...daySums, 0);
	const peakDay = maxDaySum > 0 ? dayLabels[daySums.indexOf(maxDaySum)] : '';
	const maxHourSum = Math.max(...hourSums, 0);
	let peakTime = '';
	if (maxHourSum > 0) {
		const threshold = maxHourSum * 0.7;
		let startHour = hourSums.indexOf(maxHourSum);
		let endHour = startHour;
		while (startHour > 0 && hourSums[startHour - 1] >= threshold) startHour--;
		while (endHour < 23 && hourSums[endHour + 1] >= threshold) endHour++;
		peakTime = startHour === endHour ? `${pad(startHour)}:00` : `${pad(startHour)}:00-${pad(endHour + 1)}:00`;
	}
	return { peakDay, peakTime };
}

export function findBestOverlapTimeFromBuckets(buckets: number[], dayLabels: string[]): string {
	const grid = bucketsToGrid(buckets);
	const hourSums = new Array<number>(24).fill(0);
	for (let hour = 0; hour < 24; hour++) for (let day = 0; day < 7; day++) hourSums[hour] += grid[day][hour];
	const maxHourSum = Math.max(...hourSums, 0);
	if (maxHourSum === 0) return '';
	const threshold = maxHourSum * 0.6;
	let startHour = hourSums.indexOf(maxHourSum);
	let endHour = startHour;
	while (startHour > 0 && hourSums[startHour - 1] >= threshold) startHour--;
	while (endHour < 23 && hourSums[endHour + 1] >= threshold) endHour++;
	const daySums = new Array<number>(7).fill(0);
	for (let day = 0; day < 7; day++) for (let hour = startHour; hour <= endHour; hour++) daySums[day] += grid[day][hour];
	const maxDaySum = Math.max(...daySums, 0);
	if (maxDaySum === 0) return '';
	return `${dayLabels[daySums.indexOf(maxDaySum)]}, ${pad(startHour)}:00-${pad(endHour + 1)}:00`;
}

export function computeActivityView(args: { sessions: Session[]; dayLabels: string[]; rangeDays: number; nowMs?: number; normalizeConfig: NormalizeConfig; maxSessionMs?: number }) {
	const { sessions, dayLabels, rangeDays, nowMs = Date.now(), normalizeConfig, maxSessionMs = DEFAULT_MAX_SESSION_MS } = args;
	const windowStartMs = nowMs - rangeDays * 86400000;
	const clippedSessions = clipSessionsToRange(sessions, windowStartMs, nowMs);
	const rawBuckets = buildHeatmapBuckets(clippedSessions, windowStartMs, nowMs, maxSessionMs);
	const normalizedBuckets = normalizeBuckets(rawBuckets, { ...normalizeConfig, rangeDays });
	const { peakDay, peakTime } = computePeaksFromBuckets(rawBuckets, dayLabels);
	return { rangeDays, rawBuckets, normalizedBuckets, peakDay, peakTime, filteredEventCount: clippedSessions.length };
}

export function computeOverlapView(args: {
	selfSessions: Session[];
	targetSessions: Session[];
	dayLabels: string[];
	rangeDays: number;
	excludeHours?: ExcludeHours | null;
	nowMs?: number;
	normalizeConfig: NormalizeConfig;
	maxSessionMs?: number;
}) {
	const { selfSessions, targetSessions, dayLabels, rangeDays, excludeHours = null, nowMs = Date.now(), normalizeConfig, maxSessionMs = DEFAULT_MAX_SESSION_MS } = args;
	const windowStartMs = nowMs - rangeDays * 86400000;
	const clippedSelf = clipSessionsToRange(selfSessions, windowStartMs, nowMs);
	const clippedTarget = clipSessionsToRange(targetSessions, windowStartMs, nowMs);
	const selfBuckets = buildHeatmapBuckets(clippedSelf, windowStartMs, nowMs, maxSessionMs);
	const targetBuckets = buildHeatmapBuckets(clippedTarget, windowStartMs, nowMs, maxSessionMs);
	const rawBuckets = buildOverlapBuckets(clippedSelf, clippedTarget, windowStartMs, nowMs, maxSessionMs);
	if (excludeHours) applyExcludeHours(rawBuckets, selfBuckets, targetBuckets, excludeHours);

	const overlapMinutes = sum(rawBuckets);
	const denominator = Math.min(sum(selfBuckets), sum(targetBuckets));
	const overlapPercent = denominator > 0 ? Math.round((overlapMinutes / denominator) * 100) : 0;
	const normalizedBuckets = normalizeBuckets(rawBuckets, { ...normalizeConfig, rangeDays });
	const bestOverlapTime = overlapMinutes > 0 ? findBestOverlapTimeFromBuckets(rawBuckets, dayLabels) : '';
	return { rangeDays, rawBuckets, normalizedBuckets, overlapPercent, bestOverlapTime };
}

/** Time online per calendar day, oldest first. */
export function buildDailySummary(sessions: Session[], rangeStartMs: number, rangeEndMs = Date.now()): { date: string; totalMs: number }[] {
	const dayMap = new Map<string, number>();
	let lastDayStart = -Infinity;
	let lastDayKey = '';
	for (const session of clipSessionsToRange(sessions, rangeStartMs, rangeEndMs)) {
		let cursor = session.start;
		while (cursor < session.end) {
			let dayKey: string;
			if (cursor < lastDayStart) {
				dayKey = lastDayKey;
			} else {
				const dayStart = new Date(cursor);
				dayStart.setHours(0, 0, 0, 0);
				dayKey = formatTimestampKey(dayStart);
				dayStart.setDate(dayStart.getDate() + 1);
				lastDayStart = dayStart.getTime();
				lastDayKey = dayKey;
			}
			const segmentEnd = Math.min(session.end, lastDayStart);
			dayMap.set(dayKey, (dayMap.get(dayKey) ?? 0) + (segmentEnd - cursor));
			cursor = lastDayStart;
		}
	}
	return [...dayMap].map(([date, totalMs]) => ({ date, totalMs })).sort((a, b) => a.date.localeCompare(b.date));
}

export function formatTimestampKey(timestamp: Date): string {
	return `${timestamp.getFullYear()}-${pad(timestamp.getMonth() + 1)}-${pad(timestamp.getDate())}`;
}

const DEFAULT_ACTIVITY_CONFIG: NormalizeConfig = { floorPercentile: 15, capPercentile: 85, rankWeight: 0.2, targetCoverage: 0.25, targetVolume: 60 };
const ACTIVITY_CONFIGS: Record<number, NormalizeConfig> = {
	7: { floorPercentile: 10, capPercentile: 80, rankWeight: 0.15, targetCoverage: 0.12, targetVolume: 40 },
	30: { floorPercentile: 15, capPercentile: 85, rankWeight: 0.2, targetCoverage: 0.25, targetVolume: 60 },
	90: { floorPercentile: 15, capPercentile: 85, rankWeight: 0.2, targetCoverage: 0.3, targetVolume: 50 }
};
const OVERLAP_CONFIGS: Record<number, NormalizeConfig> = {
	7: { floorPercentile: 10, capPercentile: 80, rankWeight: 0.15, targetCoverage: 0.08, targetVolume: 15 },
	30: { floorPercentile: 15, capPercentile: 85, rankWeight: 0.2, targetCoverage: 0.15, targetVolume: 25 },
	90: { floorPercentile: 15, capPercentile: 85, rankWeight: 0.2, targetCoverage: 0.18, targetVolume: 20 }
};

/** The original's pickActivityNormalizeConfig; it uses the same values for yourself and friends. */
export function activityNormalizeConfig(rangeDays: number): NormalizeConfig {
	return ACTIVITY_CONFIGS[rangeDays] ?? DEFAULT_ACTIVITY_CONFIG;
}

export function overlapNormalizeConfig(rangeDays: number): NormalizeConfig {
	return OVERLAP_CONFIGS[rangeDays] ?? { floorPercentile: 15, capPercentile: 85, rankWeight: 0.2, targetCoverage: 0.15, targetVolume: 25 };
}

function pad(value: number): string {
	return String(value).padStart(2, '0');
}

function cloneSession(session: Session): Session {
	return { start: session.start, end: session.end, isOpenTail: Boolean(session.isOpenTail), sourceRevision: session.sourceRevision || '' };
}

function percentile(sortedValues: number[], percentileValue: number): number {
	if (sortedValues.length === 0) return 1;
	const index = (percentileValue / 100) * (sortedValues.length - 1);
	const lower = Math.floor(index);
	const upper = Math.ceil(index);
	if (lower === upper) return sortedValues[lower];
	return sortedValues[lower] + (sortedValues[upper] - sortedValues[lower]) * (index - lower);
}

function bucketsToGrid(buckets: number[]): number[][] {
	const grid = Array.from({ length: 7 }, () => new Array<number>(24).fill(0));
	for (let slot = 0; slot < 168; slot++) grid[Math.floor(slot / 24)][slot % 24] = buckets[slot];
	return grid;
}

function applyExcludeHours(rawBuckets: number[], selfBuckets: number[], targetBuckets: number[], { startHour, endHour }: ExcludeHours) {
	for (let day = 0; day < 7; day++) {
		if (startHour <= endHour) {
			zeroHourRange(day, startHour, endHour, rawBuckets, selfBuckets, targetBuckets);
		} else {
			zeroHourRange(day, startHour, 24, rawBuckets, selfBuckets, targetBuckets);
			zeroHourRange(day, 0, endHour, rawBuckets, selfBuckets, targetBuckets);
		}
	}
}

function zeroHourRange(day: number, startHour: number, endHour: number, ...bucketSets: number[][]) {
	for (let hour = startHour; hour < endHour; hour++) for (const buckets of bucketSets) buckets[day * 24 + hour] = 0;
}

function sum(values: number[]): number {
	return values.reduce((total, value) => total + value, 0);
}

function computeTiedRankScores(sortedEntries: { value: number }[]): Float64Array {
	const count = sortedEntries.length;
	const scores = new Float64Array(count);
	let i = 0;
	while (i < count) {
		let j = i;
		while (j < count && sortedEntries[j].value === sortedEntries[i].value) j++;
		const score = (i + 1 + j) / 2 / count;
		for (let k = i; k < j; k++) scores[k] = score;
		i = j;
	}
	return scores;
}
