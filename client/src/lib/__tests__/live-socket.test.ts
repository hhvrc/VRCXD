import { afterEach, beforeEach, describe, expect, test, vi } from 'vitest';
import { create, fromBinary, toBinary } from '@bufbuild/protobuf';
import { ClientMessageSchema, ServerMessageSchema, type ClientMessage } from '../gen/vrcx_live_pb.js';

const cookie = vi.hoisted(() => ({ ready: true, asked: 0, resolve: () => {} }));
vi.mock('#lib/api/client.js', () => ({
	mediaCookie: {
		isReady: () => cookie.ready,
		ensure: () => {
			cookie.asked += 1;
			return new Promise<void>((resolve) => (cookie.resolve = () => ((cookie.ready = true), resolve())));
		},
		forget: () => (cookie.ready = false)
	}
}));

// A stand-in for the browser's WebSocket: the test opens, feeds and drops it.
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
	pings() {
		return this.sent.filter((m) => m.message.case === 'ping').length;
	}
}

const latest = () => FakeSocket.all[FakeSocket.all.length - 1];
const presence = (userId: string) => ({ message: { case: 'event' as const, value: { event: { case: 'presence' as const, value: { userId, state: 'online' } } } } });

let live: typeof import('../api/live.svelte.js');
let disconnect: () => void;

beforeEach(async () => {
	vi.useFakeTimers();
	vi.resetModules();
	FakeSocket.all = [];
	cookie.ready = true;
	cookie.asked = 0;
	vi.stubGlobal('WebSocket', FakeSocket);
	vi.stubGlobal('location', { href: 'http://vrcx.test/feed' });
	live = await import('../api/live.svelte.js');
	disconnect = live.connectLive();
	latest().open();
});

afterEach(() => {
	disconnect();
	vi.advanceTimersByTime(10_000);
	vi.unstubAllGlobals();
	vi.useRealTimers();
});

describe('live socket cookie', () => {
	test('a socket refused before it opened waits for the cookie to be set again, then connects', async () => {
		latest().drop();
		vi.advanceTimersByTime(60_000);
		expect(FakeSocket.all).toHaveLength(2);
		expect(cookie.asked).toBe(0);
		latest().drop(); // refused: it never opened
		const refused = FakeSocket.all.length;
		vi.advanceTimersByTime(60_000);
		expect(cookie.asked).toBe(1);
		expect(FakeSocket.all).toHaveLength(refused);

		cookie.resolve();
		await vi.advanceTimersByTimeAsync(0);
		expect(FakeSocket.all).toHaveLength(refused + 1);
	});
});

describe('live socket heartbeat', () => {
	test('says hello, then pings every 30 seconds as the agent does', () => {
		const ws = latest();
		expect(ws.sent[0].message.case).toBe('hello');
		// The cookie lets it in; a token in the URL would be in the proxy's logs.
		expect(ws.url.search).toBe('');
		vi.advanceTimersByTime(29_999);
		expect(ws.pings()).toBe(0);
		vi.advanceTimersByTime(1);
		expect(ws.pings()).toBe(1);
		ws.receive({ message: { case: 'pong', value: {} } });
		vi.advanceTimersByTime(30_000);
		expect(ws.pings()).toBe(2);
	});

	test('an answering server keeps the socket', () => {
		const ws = latest();
		for (let i = 0; i < 10; i++) {
			vi.advanceTimersByTime(30_000);
			ws.receive({ message: { case: 'pong', value: {} } });
		}
		expect(ws.closed).toBe(false);
		expect(FakeSocket.all).toHaveLength(1);
		expect(live.live.connected).toBe(true);
	});

	test('90 seconds of silence is a dead socket: it is given up and opened again', () => {
		const ws = latest();
		vi.advanceTimersByTime(89_999);
		expect(ws.closed).toBe(false);
		vi.advanceTimersByTime(1);
		expect(ws.closed).toBe(true);
		expect(live.live.connected).toBe(false);
		// The backoff, then a new socket.
		vi.advanceTimersByTime(1000);
		expect(FakeSocket.all).toHaveLength(2);
		expect(latest()).not.toBe(ws);
	});
});

describe('live socket reconnects', () => {
	test('the first connection is not a reconnect; each one after is, once', () => {
		const heard = vi.fn();
		live.onReconnect(heard);
		expect(heard).not.toHaveBeenCalled();
		expect(live.live.reconnects).toBe(0);
		latest().drop();
		vi.advanceTimersByTime(1000);
		latest().open();
		expect(heard).toHaveBeenCalledTimes(1);
		expect(live.live.reconnects).toBe(1);
		latest().drop();
		vi.advanceTimersByTime(2000);
		latest().open();
		expect(heard).toHaveBeenCalledTimes(2);
	});

	test('watchLive reads again once after a reconnect, as well as for its events', () => {
		const run = vi.fn();
		const stop = live.watchLive(['presence'], run, 750);
		latest().receive(presence('usr_a'));
		vi.advanceTimersByTime(750);
		expect(run).toHaveBeenCalledTimes(1);
		latest().drop();
		vi.advanceTimersByTime(1000);
		latest().open();
		vi.advanceTimersByTime(750);
		expect(run).toHaveBeenCalledTimes(2);
		stop();
	});

	test('watchLive runs under a steady stream of events, at least every maxWait', () => {
		const run = vi.fn();
		const stop = live.watchLive(['presence'], run, 750, 3000);
		for (let i = 0; i < 20; i++) {
			latest().receive(presence('usr_a'));
			vi.advanceTimersByTime(500);
		}
		// Ten seconds of events half a second apart: a reset-on-every-event debounce never ran.
		expect(run).toHaveBeenCalledTimes(3);
		stop();
		vi.advanceTimersByTime(5000);
		expect(run).toHaveBeenCalledTimes(3);
	});
});
