// The original's buildGameLogSessions: game log rows grouped under the stay they happened in, with
// bursts of joins and leaves folded into one row and repeated videos counted.
export type GameLogRow = {
	key: string;
	id: number;
	type: string;
	createdAt: string;
	location?: string | null;
	worldId?: string | null;
	worldName?: string | null;
	groupName?: string | null;
	userId?: string | null;
	displayName?: string | null;
	timeMs?: number | null;
	videoUrl?: string | null;
	videoName?: string | null;
	videoId?: string | null;
	videoPos?: number | null;
	videoLength?: number | null;
	resourceUrl?: string | null;
	data?: string | null;
	message?: string | null;
	instanceId?: string | null;
	isFriend: boolean;
	isFavorite: boolean;
};

export type GroupMember = Pick<GameLogRow, 'displayName' | 'userId' | 'createdAt' | 'isFriend' | 'isFavorite'>;
export type SessionEvent = GameLogRow & { playCount?: number };
export type SessionGroup = { type: 'JoinGroup' | 'LeftGroup'; key: string; createdAt: string; count: number; members: GroupMember[] };
export type SessionItem = SessionEvent | SessionGroup;
export type SessionSegment = {
	id: number;
	createdAt: string;
	location: string;
	worldId?: string | null;
	worldName?: string | null;
	groupName?: string | null;
	duration: number | null;
	events: SessionItem[];
};

export const isGroup = (item: SessionItem): item is SessionGroup => item.type === 'JoinGroup' || item.type === 'LeftGroup';

const TOLERANCE_MS = 1000;
const AGGREGATE_THRESHOLD = 5;
const AGGREGATE_WINDOW_MS = 5000;

function toEpoch(date: string | null | undefined): number {
	if (!date) return 0;
	const time = Date.parse(date);
	return Number.isNaN(time) ? 0 : time;
}

function fold(events: SessionItem[], indices: number[], groupType: SessionGroup['type']) {
	if (indices.length < AGGREGATE_THRESHOLD) return;
	const batch = indices.map((i) => events[i] as SessionEvent);
	const group: SessionGroup = {
		type: groupType,
		key: `${groupType}:${batch[0].key}`,
		createdAt: batch[0].createdAt,
		count: batch.length,
		members: batch.map(({ displayName, userId, createdAt, isFriend, isFavorite }) => ({ displayName, userId, createdAt, isFriend, isFavorite }))
	};
	for (let i = indices.length - 1; i >= 0; i--) events.splice(indices[i], 1);
	events.splice(indices[0], 0, group);
}

function aggregateTail(events: SessionItem[], matchType: string, groupType: SessionGroup['type']) {
	const last = events.findLastIndex((event) => event.type === matchType);
	if (last === -1) return;
	const windowStart = toEpoch(events[last].createdAt) - AGGREGATE_WINDOW_MS;
	const indices: number[] = [];
	for (let i = last; i >= 0; i--) {
		if (toEpoch(events[i].createdAt) < windowStart) break;
		if (events[i].type === matchType) indices.unshift(i);
	}
	fold(events, indices, groupType);
}

function aggregateHead(events: SessionItem[], matchType: string, groupType: SessionGroup['type']) {
	const first = events.findIndex((event) => event.type === matchType);
	if (first === -1) return;
	const windowEnd = toEpoch(events[first].createdAt) + AGGREGATE_WINDOW_MS;
	const indices: number[] = [];
	for (let i = first; i < events.length; i++) {
		if (toEpoch(events[i].createdAt) > windowEnd) break;
		if (events[i].type === matchType) indices.push(i);
	}
	fold(events, indices, groupType);
}

function deduplicateVideoPlay(events: SessionItem[]) {
	for (let i = events.length - 1; i > 0; i--) {
		const current = events[i] as SessionEvent;
		const previous = events[i - 1] as SessionEvent;
		if (
			current.type === 'VideoPlay' &&
			previous.type === 'VideoPlay' &&
			(current.videoUrl || current.videoName) === (previous.videoUrl || previous.videoName)
		) {
			previous.playCount = (previous.playCount || 1) + (current.playCount || 1);
			events.splice(i, 1);
		}
	}
	for (const event of events) {
		if (event.type === 'VideoPlay' && !(event as SessionEvent).playCount) (event as SessionEvent).playCount = 1;
	}
}

export function buildGameLogSessions(locations: GameLogRow[], flatEvents: GameLogRow[]): SessionSegment[] {
	if (locations.length === 0) return [];

	const segments = locations
		.map((location) => ({
			id: location.id,
			createdAt: location.createdAt,
			epoch: toEpoch(location.createdAt),
			location: location.location ?? '',
			worldId: location.worldId,
			worldName: location.worldName,
			groupName: location.groupName,
			duration: location.timeMs || null,
			events: [] as SessionItem[]
		}))
		.sort((a, b) => a.epoch - b.epoch);

	const seen = new Set<string>();
	const unique = flatEvents.filter((event) => {
		const key = [event.type, event.createdAt, event.userId ?? '', event.location ?? '', event.videoUrl ?? ''].join('\0');
		if (seen.has(key)) return false;
		seen.add(key);
		return true;
	});

	const byLocation = new Map<string, number[]>();
	segments.forEach((segment, index) => {
		const list = byLocation.get(segment.location);
		if (list) list.push(index);
		else byLocation.set(segment.location, [index]);
	});

	for (const event of unique) {
		const epoch = toEpoch(event.createdAt);
		const candidates = event.location ? byLocation.get(event.location) : undefined;
		let index = 0;
		if (candidates?.length) {
			index = candidates[0];
			for (let j = candidates.length - 1; j >= 0; j--) {
				if (segments[candidates[j]].epoch <= epoch + TOLERANCE_MS) {
					index = candidates[j];
					break;
				}
			}
		} else {
			for (let i = segments.length - 1; i >= 0; i--) {
				if (segments[i].epoch <= epoch + TOLERANCE_MS) {
					index = i;
					break;
				}
			}
		}
		segments[index].events.push({ ...event });
	}

	for (const segment of segments) {
		const cutoff = segment.epoch - TOLERANCE_MS;
		segment.events = segment.events.filter((event) => toEpoch(event.createdAt) >= cutoff).sort((a, b) => toEpoch(a.createdAt) - toEpoch(b.createdAt));

		// Whoever joined as we arrived and "left" in the same moment never really left.
		const windowEnd = segment.epoch + AGGREGATE_WINDOW_MS;
		const joined = new Set<string>();
		for (const event of segment.events as SessionEvent[]) {
			if (toEpoch(event.createdAt) > windowEnd) break;
			if (event.type === 'OnPlayerJoined' && event.userId) joined.add(event.userId);
		}
		if (joined.size > 0) {
			segment.events = (segment.events as SessionEvent[]).filter(
				(event) => !(toEpoch(event.createdAt) <= windowEnd && event.type === 'OnPlayerLeft' && event.userId && joined.has(event.userId))
			);
		}

		aggregateTail(segment.events, 'OnPlayerLeft', 'LeftGroup');
		aggregateTail(segment.events, 'OnPlayerJoined', 'JoinGroup');
		aggregateHead(segment.events, 'OnPlayerJoined', 'JoinGroup');
		deduplicateVideoPlay(segment.events);
		segment.events.reverse();
	}

	return segments.reverse().map(({ epoch: _epoch, ...rest }) => rest);
}

function memberMatches(member: GroupMember, term: string): boolean {
	return (member.displayName ?? '').toUpperCase().includes(term);
}

function eventMatches(event: SessionEvent, term: string): boolean {
	return [event.displayName, event.videoName, event.videoUrl, event.worldName, event.data, event.message, event.resourceUrl].some((value) => (value ?? '').toUpperCase().includes(term));
}

/**
 * The original's applySessionsSearchFilter: a world name match keeps the whole stay, otherwise only the rows that match.
 * No search keeps every stay, empty ones too (upstream 267cb691).
 */
export function searchSessions(segments: SessionSegment[], search: string, limit: number): SessionSegment[] {
	const term = search.trim().toUpperCase();
	// No search keeps every stay, past the search limit too (upstream PR 1948).
	if (!term) return segments;
	const found: SessionSegment[] = [];
	for (const segment of segments) {
		if ((segment.worldName ?? '').toUpperCase().includes(term)) {
			found.push(segment);
			continue;
		}
		const events = segment.events.flatMap((event): SessionItem[] => {
			if (!isGroup(event)) return eventMatches(event, term) ? [event] : [];
			const single = event.type === 'JoinGroup' ? 'OnPlayerJoined' : 'OnPlayerLeft';
			return event.members
				.filter((member) => memberMatches(member, term))
				.map((member, index) => ({ ...member, key: `${event.key}:${index}`, id: 0, type: single, location: segment.location }));
		});
		if (events.length > 0) found.push({ ...segment, events });
	}
	return found.slice(0, limit);
}
