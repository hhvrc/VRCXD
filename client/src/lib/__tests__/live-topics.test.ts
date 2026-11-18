import { afterEach, beforeEach, describe, expect, test, vi } from 'vitest';
import { create, fromBinary, toBinary, type MessageInitShape } from '@bufbuild/protobuf';
import { timestampFromDate } from '@bufbuild/protobuf/wkt';
import { ClientMessageSchema, FriendEntrySchema, ServerMessageSchema, TopicCloseReason, type ClientMessage, type FriendEntry, type Subscribe } from '../gen/vrcx_live_pb.js';
import type { Friend } from '../api/types.js';

vi.mock('#lib/api/client.js', () => ({ getServerToken: () => 'token' }));

// The browser's WebSocket, driven by the test (as in live-socket.test.ts).
class FakeSocket {
	static all: FakeSocket[] = [];
	binaryType = '';
	sent: ClientMessage[] = [];
	closed = false;
	onopen: (() => void) | null = null;
	onmessage: ((message: { data: ArrayBuffer }) => void) | null = null;
	onerror: (() => void) | null = null;
	onclose: ((event: { code: number }) => void) | null = null;
	constructor(readonly url: URL) {
		FakeSocket.all.push(this);
	}
	send(data: Uint8Array) {
		this.sent.push(fromBinary(ClientMessageSchema, data));
	}
	close() {
		this.closed = true;
	}
	open() {
		this.onopen?.();
	}
	receive(message: Parameters<typeof create<typeof ServerMessageSchema>>[1]) {
		const bytes = toBinary(ServerMessageSchema, create(ServerMessageSchema, message));
		this.onmessage?.({ data: bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength) as ArrayBuffer });
	}
	drop(code = 1006) {
		this.onclose?.({ code });
	}
	hello(topics = ['friends']) {
		this.receive({ message: { case: 'hello', value: { protocolVersion: 1, serverVersion: 'test', topics } } });
	}
	subscribes(): Subscribe[] {
		return this.sent.flatMap((m) => (m.message.case === 'subscribe' ? [m.message.value] : []));
	}
	unsubscribes(): number[] {
		return this.sent.flatMap((m) => (m.message.case === 'unsubscribe' ? [m.message.value.subId] : []));
	}
	lastSubId() {
		const all = this.subscribes();
		return all[all.length - 1].subId;
	}
	snapshot(subId: number, rev: bigint, friends: FriendEntry[]) {
		this.receive({ message: { case: 'topicSnapshot', value: { subId, rev, payload: { case: 'friends', value: { friends } } } } });
	}
	delta(subId: number, rev: bigint, upserts: FriendEntry[], removedUserIds: string[] = []) {
		this.receive({ message: { case: 'topicDelta', value: { subId, rev, payload: { case: 'friends', value: { upserts, removedUserIds } } } } });
	}
}

const latest = () => FakeSocket.all[FakeSocket.all.length - 1];
const entry = (userId: string, friendNumber: number, more: MessageInitShape<typeof FriendEntrySchema> = {}) =>
	create(FriendEntrySchema, { userId, friendNumber, displayName: userId, state: 'offline', status: 'active', trustLevel: 'Trusted User', ...more });
const ids = (friends: Friend[] | undefined) => friends?.map((f) => f.userId);

let live: typeof import('../api/live.svelte.js');
let topics: typeof import('../live/topics/friends.svelte.js');

beforeEach(async () => {
	vi.useFakeTimers();
	vi.resetModules();
	FakeSocket.all = [];
	vi.stubGlobal('WebSocket', FakeSocket);
	vi.stubGlobal('location', { href: 'http://vrcx.test/' });
	live = await import('../api/live.svelte.js');
	topics = await import('../live/topics/friends.svelte.js');
});

afterEach(() => {
	vi.advanceTimersByTime(10_000);
	vi.unstubAllGlobals();
	vi.useRealTimers();
});

describe('live topics', () => {
	test('subscribes after the hello, takes the snapshot and applies deltas in order', () => {
		const handle = topics.friendsTopic('usr_me');
		const ws = latest();
		ws.open();
		expect(ws.subscribes()).toHaveLength(0);
		ws.hello();
		expect(ws.subscribes()).toEqual([expect.objectContaining({ topic: 'friends', accountId: 'usr_me' })]);
		expect(handle.status).toBe('connecting');

		const sub = ws.lastSubId();
		ws.snapshot(sub, 1n, [entry('usr_b', 2), entry('usr_a', 1)]);
		expect(handle.status).toBe('live');
		expect(ids(handle.value)).toEqual(['usr_b', 'usr_a']);

		ws.delta(sub, 2n, [entry('usr_a', 1, { state: 'online', location: 'wrld_x:1', locationSince: timestampFromDate(new Date('2026-11-18T12:00:00Z')) })]);
		expect(handle.value?.find((f) => f.userId === 'usr_a')).toMatchObject({ state: 'online', location: 'wrld_x:1', locationSince: '2026-11-18T12:00:00.000Z' });
		ws.delta(sub, 3n, [entry('usr_c', 3)], ['usr_b']);
		expect(ids(handle.value)).toEqual(['usr_a', 'usr_c']);
		handle.release();
	});

	test('a delta whose rev is not the next one is dropped and the topic subscribed again', () => {
		const handle = topics.friendsTopic('usr_me');
		const ws = latest();
		ws.open();
		ws.hello();
		const first = ws.lastSubId();
		ws.snapshot(first, 1n, [entry('usr_a', 1)]);
		ws.delta(first, 3n, [entry('usr_gap', 2)]);
		// Not applied; a new subscription replaces the old one.
		expect(ids(handle.value)).toEqual(['usr_a']);
		expect(ws.unsubscribes()).toEqual([first]);
		const second = ws.lastSubId();
		expect(second).not.toBe(first);
		// The old stream's stragglers are ignored; the new snapshot replaces the copy.
		ws.delta(first, 4n, [entry('usr_old', 9)]);
		ws.snapshot(second, 1n, [entry('usr_a', 1), entry('usr_gap', 2)]);
		expect(ids(handle.value)).toEqual(['usr_a', 'usr_gap']);
		handle.release();
	});

	test('a reconnect subscribes again and the fresh snapshot replaces the copy', () => {
		const handle = topics.friendsTopic('usr_me');
		let ws = latest();
		ws.open();
		ws.hello();
		ws.snapshot(ws.lastSubId(), 1n, [entry('usr_a', 1), entry('usr_b', 2)]);
		ws.drop();
		vi.advanceTimersByTime(1000);
		ws = latest();
		ws.open();
		ws.hello();
		expect(ws.subscribes()).toHaveLength(1);
		// Kept while the new snapshot is on its way.
		expect(ids(handle.value)).toEqual(['usr_a', 'usr_b']);
		ws.snapshot(ws.lastSubId(), 1n, [entry('usr_b', 2)]);
		expect(ids(handle.value)).toEqual(['usr_b']);
		handle.release();
	});

	test('holders share one subscription; the last release unsubscribes', () => {
		const a = topics.friendsTopic('usr_me');
		const b = topics.friendsTopic('usr_me');
		const other = topics.friendsTopic('usr_alt');
		const ws = latest();
		ws.open();
		ws.hello();
		expect(ws.subscribes().map((s) => s.accountId)).toEqual(['usr_me', 'usr_alt']);
		const mine = ws.subscribes()[0].subId;
		ws.snapshot(mine, 1n, [entry('usr_a', 1)]);
		expect(ids(a.value)).toEqual(['usr_a']);
		expect(ids(b.value)).toEqual(['usr_a']);
		a.release();
		a.release();
		expect(ws.unsubscribes()).toEqual([]);
		b.release();
		expect(ws.unsubscribes()).toEqual([mine]);
		other.release();
		// Nothing holds the socket now: it closes after its grace period.
		vi.advanceTimersByTime(6000);
		expect(ws.closed).toBe(true);
	});

	test('a server whose hello lacks the topic makes it unsupported, and sends no Subscribe', () => {
		const handle = topics.friendsTopic('usr_me');
		const seen: string[] = [];
		handle.listen((_value, status) => seen.push(status));
		const ws = latest();
		ws.open();
		ws.hello([]);
		expect(ws.subscribes()).toHaveLength(0);
		expect(handle.status).toBe('unsupported');
		expect(seen).toEqual(['connecting', 'unsupported']);
		handle.release();
	});

	test('an unknown topic closing is unsupported; an overflow subscribes again after a wait', () => {
		const handle = topics.friendsTopic('usr_me');
		const ws = latest();
		ws.open();
		ws.hello();
		const first = ws.lastSubId();
		ws.snapshot(first, 1n, [entry('usr_a', 1)]);
		ws.receive({ message: { case: 'topicClosed', value: { subId: first, reason: TopicCloseReason.OVERFLOW } } });
		expect(ws.subscribes()).toHaveLength(1);
		vi.advanceTimersByTime(1000);
		expect(ws.subscribes()).toHaveLength(2);
		const second = ws.lastSubId();
		ws.receive({ message: { case: 'topicClosed', value: { subId: second, reason: TopicCloseReason.UNKNOWN_TOPIC } } });
		expect(handle.status).toBe('unsupported');
		vi.advanceTimersByTime(5000);
		expect(ws.subscribes()).toHaveLength(2);
		handle.release();
	});

	test('a row maps to the REST shape: what the server leaves empty is left out', () => {
		const handle = topics.friendsTopic('usr_me');
		const ws = latest();
		ws.open();
		ws.hello();
		ws.snapshot(ws.lastSubId(), 1n, [
			entry('usr_a', 1, { statusDescription: '', location: 'offline', platform: 'android', pendingOffline: true, tags: ['system_trust_known'], nameHue: 7 })
		]);
		expect(handle.value?.[0]).toEqual({
			friendNumber: 1,
			friendsSince: '',
			userId: 'usr_a',
			displayName: 'usr_a',
			trustLevel: 'Trusted User',
			state: 'offline',
			status: 'active',
			statusDescription: undefined,
			location: 'offline',
			travelingToLocation: undefined,
			locationSince: undefined,
			onlineFor: undefined,
			lastActivity: undefined,
			lastLogin: undefined,
			thumbnailImageUrl: undefined,
			tags: ['system_trust_known'],
			developerType: '',
			platform: 'android',
			nickname: undefined,
			pendingOffline: true,
			nameHue: 7,
			iconFrame: undefined,
			profileEffect: undefined
		});
		handle.release();
	});
});

describe('live hello', () => {
	test('records the server topics and announces every hello', () => {
		const heard = vi.fn();
		live.onHello(heard);
		const disconnect = live.connectLive();
		const ws = latest();
		ws.open();
		expect(live.live.topics).toBeNull();
		ws.hello(['friends']);
		expect(live.live.topics).toEqual(['friends']);
		expect(heard).toHaveBeenCalledTimes(1);
		disconnect();
	});
});
