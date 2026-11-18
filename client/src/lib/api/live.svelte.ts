import { create, fromBinary, toBinary } from '@bufbuild/protobuf';
import { timestampDate } from '@bufbuild/protobuf/wkt';
import {
	ClientMessageSchema,
	ServerMessageSchema,
	type ClientMessage,
	type InstanceVisitChanged,
	type PresenceChanged,
	type ServerEvent,
	type ServerHello,
	type ServerMessage
} from '#lib/gen/vrcx_live_pb.js';
import { mediaCookie } from '#lib/api/client.js';
import { startKeepalive, type Keepalive } from '#lib/api/keepalive.js';
import { debounce } from '#lib/helpers/debounce.js';

// One websocket (/1/ws, binary protobuf frames) for the whole UI, shared by every page that
// wants live state. The stream is live-only: pages seed their state from REST and apply the
// ServerEvents here on top. Reconnects with backoff. Browsers can't set headers on an upgrade, so
// the socket is let in by the server's HttpOnly cookie (mediaCookie), not the token in its URL,
// which a proxy would log. Events sent while the socket was down are gone, so
// every reconnect is announced (onReconnect, live.reconnects) for the stores to read again.
// Topics (topics.svelte.ts, docs/live-topics.md) ride the same socket: they subscribe after each
// ServerHello (onHello) and read their snapshots and deltas through onTopicMessage.

export type LiveState = {
	connected: boolean;
	error: string;
	serverVersion: string;
	/** Latest presence per user id, from PresenceChanged events. */
	presence: Map<string, PresenceChanged>;
	/** Open visits per instance location, from InstanceVisitChanged events. */
	present: Map<string, Map<string, InstanceVisitChanged>>;
	/** Every event, newest first, capped - for the feed. */
	events: { at: Date; event: ServerEvent }[];
	/** Bumped on every event, so pages can refetch cheaply when anything changed. */
	version: number;
	/** Bumped each time the socket opens again after the first time: whatever came meanwhile was missed. */
	reconnects: number;
};

export const live = $state<LiveState>({
	connected: false,
	error: '',
	serverVersion: '',
	presence: new Map(),
	present: new Map(),
	events: [],
	version: 0,
	reconnects: 0
});

const MAX_EVENTS = 500;

type Listener = (event: ServerEvent) => void;
const listeners = new Set<Listener>();

/** Called for every ServerEvent while subscribed; returns the unsubscribe function. */
export function onLiveEvent(listener: Listener): () => void {
	listeners.add(listener);
	return () => listeners.delete(listener);
}

const reconnectListeners = new Set<() => void>();

/**
 * Called each time the socket is back after having been open before (a dropped connection, the
 * server restarting, a dead socket found by the heartbeat, or the last page closing it), since the
 * events in between were never seen. Not called for the first connection. Returns the unsubscribe.
 */
export function onReconnect(listener: () => void): () => void {
	reconnectListeners.add(listener);
	return () => reconnectListeners.delete(listener);
}

const helloListeners = new Set<(hello: ServerHello) => void>();

/**
 * Called for every ServerHello, the first and each one after a reconnect: the moment a topic
 * subscribes (again), since the server forgets subscriptions with the connection. Returns the unsubscribe.
 */
export function onHello(listener: (hello: ServerHello) => void): () => void {
	helloListeners.add(listener);
	return () => helloListeners.delete(listener);
}

/** A topic's snapshot, delta or closing. */
export type TopicMessage = Extract<ServerMessage['message'], { case: 'topicSnapshot' | 'topicDelta' | 'topicClosed' }>;
const topicListeners = new Set<(message: TopicMessage) => void>();

/** Called for every topic message on the socket; returns the unsubscribe. */
export function onTopicMessage(listener: (message: TopicMessage) => void): () => void {
	topicListeners.add(listener);
	return () => topicListeners.delete(listener);
}

/** The socket has said hello and the server answered: Subscribe now reaches a server that knows the topics. */
let helloSeen = false;

/** Whether the current connection has had its ServerHello. */
export const liveReady = () => helloSeen;

/** Sends on the socket when it is open and greeted; false when it is not (the next hello is the time to try again). */
export function sendLive(message: ClientMessage['message']): boolean {
	if (!socket || !helloSeen) return false;
	try {
		socket.send(toBinary(ClientMessageSchema, create(ClientMessageSchema, { message })));
		return true;
	} catch {
		return false;
	}
}

let socket: WebSocket | undefined;
let wanted = 0;
let retryDelay = 1000;
let retryTimer: ReturnType<typeof setTimeout> | undefined;
let everOpened = false;
let keepalive: Keepalive | undefined;

function apply(event: ServerEvent) {
	switch (event.event.case) {
		case 'presence': {
			const presence = event.event.value;
			live.presence = new Map(live.presence).set(presence.userId, presence);
			break;
		}
		case 'visit': {
			const visit = event.event.value;
			const present = new Map(live.present);
			const inInstance = new Map(present.get(visit.instanceLocation) ?? []);
			if (visit.joined) inInstance.set(visit.userId, visit);
			else inInstance.delete(visit.userId);
			present.set(visit.instanceLocation, inInstance);
			live.present = present;
			break;
		}
	}
	const at = event.at ? timestampDate(event.at) : new Date();
	live.events = [{ at, event }, ...live.events].slice(0, MAX_EVENTS);
	live.version += 1;
	for (const listener of listeners) listener(event);
}

let messageCount = 0;

/** Messages received on the live socket so far: the status bar's per-minute rate, as the original's wsState.messageCount. */
export const liveMessageCount = () => messageCount;

let opening = false;

function open() {
	if (socket || opening || wanted <= 0) return;
	if (mediaCookie.isReady()) return connect();
	opening = true;
	void mediaCookie.ensure().then(() => {
		opening = false;
		connect();
	});
}

function connect() {
	if (socket || wanted <= 0) return;
	const url = new URL('/1/ws', location.href);
	url.protocol = url.protocol === 'https:' ? 'wss:' : 'ws:';
	const ws = new WebSocket(url);
	let opened = false;
	ws.binaryType = 'arraybuffer';
	socket = ws;

	ws.onopen = () => {
		opened = true;
		live.connected = true;
		live.error = '';
		retryDelay = 1000;
		const hello = create(ClientMessageSchema, {
			message: {
				case: 'hello',
				value: { clientId: '', clientName: 'vrcx-web', muteEvents: false, protocolVersion: 1 }
			}
		});
		ws.send(toBinary(ClientMessageSchema, hello));
		keepalive?.stop();
		keepalive = startKeepalive(
			() => ws.send(PING),
			() => drop(ws)
		);
		const again = everOpened;
		everOpened = true;
		if (again) {
			live.reconnects += 1;
			for (const listener of reconnectListeners) listener();
		}
	};
	ws.onmessage = (message: MessageEvent<ArrayBuffer>) => {
		messageCount += 1;
		if (socket === ws) keepalive?.received();
		const decoded = fromBinary(ServerMessageSchema, new Uint8Array(message.data));
		switch (decoded.message.case) {
			case 'hello': {
				const hello = decoded.message.value;
				live.serverVersion = hello.serverVersion;
				helloSeen = socket === ws;
				for (const listener of helloListeners) listener(hello);
				break;
			}
			case 'event':
				apply(decoded.message.value);
				break;
			case 'topicSnapshot':
			case 'topicDelta':
			case 'topicClosed': {
				const message = decoded.message;
				for (const listener of topicListeners) listener(message);
				break;
			}
		}
	};
	ws.onerror = () => {
		live.error = 'Live connection failed';
	};
	ws.onclose = (event) => {
		if (socket !== ws) return;
		if (event.code === 1008 || event.code === 4401) live.error = 'The server rejected the token';
		// Refused before it opened: the cookie may be gone (cleared, or set for an older token), so ask again.
		if (!opened) mediaCookie.forget();
		lost();
	};
}

const PING = toBinary(ClientMessageSchema, create(ClientMessageSchema, { message: { case: 'ping', value: {} } }));

/** The socket is gone: stop its heartbeat and, while a page still wants it, try again after the backoff. */
function lost() {
	socket = undefined;
	helloSeen = false;
	keepalive?.stop();
	keepalive = undefined;
	live.connected = false;
	if (wanted <= 0) return;
	clearTimeout(retryTimer);
	retryTimer = setTimeout(open, retryDelay);
	retryDelay = Math.min(retryDelay * 2, 30000);
}

/**
 * The heartbeat found the socket silent: give it up now rather than waiting for the browser's close
 * handshake, which on a dead connection can take minutes.
 */
function drop(ws: WebSocket) {
	if (socket !== ws) return;
	ws.onopen = ws.onmessage = ws.onerror = ws.onclose = null;
	try {
		ws.close(4000, 'no answer');
	} catch {
		// Already closing.
	}
	lost();
}

function close() {
	if (retryTimer) clearTimeout(retryTimer);
	retryTimer = undefined;
	keepalive?.stop();
	keepalive = undefined;
	const ws = socket;
	socket = undefined;
	helloSeen = false;
	ws?.close(1000, 'bye');
	live.connected = false;
}

// Call from a page's $effect and return the result as its cleanup: the socket opens for the
// first page that wants it and closes a few seconds after the last one goes away, so moving
// between pages (cleanup runs before the next page's effect) doesn't reconnect.
const CLOSE_GRACE_MS = 5000;
let closeTimer: ReturnType<typeof setTimeout> | undefined;

export function connectLive(): () => void {
	wanted += 1;
	if (closeTimer) {
		clearTimeout(closeTimer);
		closeTimer = undefined;
	}
	open();
	return () => {
		wanted -= 1;
		if (wanted <= 0) {
			wanted = 0;
			closeTimer = setTimeout(() => {
				closeTimer = undefined;
				if (wanted <= 0) close();
			}, CLOSE_GRACE_MS);
		}
	};
}

/**
 * Reloads a page's item when the server says it changed, from any path: the pipeline, a sync, a
 * refresh on another page, or the scheduled refreshes. At most once every 30 seconds, because a
 * reload can itself fetch from VRChat and move live counts again.
 */
export function onItemChanged(kind: 'user' | 'world' | 'group' | 'avatar', id: () => string | undefined, reload: () => unknown): () => void {
	// The page has just loaded the item, so the first reload waits out the interval from now too.
	const run = debounce(reload, { wait: 1000, maxWait: 30_000, minInterval: 30_000, lastRun: Date.now() });
	const unsubscribe = onLiveEvent((event) => {
		const target = id();
		if (!target) return;
		const matches =
			kind === 'user'
				? event.event.case === 'user' && event.event.value.userId === target
				: event.event.case === 'content' && event.event.value.kind === kind && event.event.value.id === target;
		if (matches) run();
	});
	return () => {
		run.cancel();
		unsubscribe();
	};
}

export type LiveEventKind = NonNullable<ServerEvent['event']['case']>;

/**
 * The usual page wiring: keeps the socket open and calls `run`, debounced by `delay` ms (and at
 * least every `maxWait` ms while matching events keep coming), for each event that `matches` (a
 * list of event kinds, or a test), and once after a reconnect, since the events in between were
 * missed. Returns the cleanup, so an $effect can return it as it is.
 */
export function watchLive(matches: readonly LiveEventKind[] | ((event: ServerEvent) => boolean), run: () => unknown, delay: number, maxWait?: number): () => void {
	const test = typeof matches === 'function' ? matches : (event: ServerEvent) => !!event.event.case && matches.includes(event.event.case);
	const disconnect = connectLive();
	const refresh = debounce(run, { wait: delay, maxWait });
	const unsubscribe = onLiveEvent((event) => {
		if (test(event)) refresh();
	});
	const unsubscribeReconnect = onReconnect(refresh);
	return () => {
		refresh.cancel();
		unsubscribe();
		unsubscribeReconnect();
		disconnect();
	};
}
