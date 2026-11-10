import { create, fromBinary, toBinary } from '@bufbuild/protobuf';
import { timestampDate } from '@bufbuild/protobuf/wkt';
import {
	ClientMessageSchema,
	ServerMessageSchema,
	type InstanceVisitChanged,
	type PresenceChanged,
	type ServerEvent
} from '#lib/gen/vrcx_live_pb.js';
import { getServerToken } from '#lib/api/client.js';

// One websocket (/1/ws, binary protobuf frames) for the whole UI, shared by every page that
// wants live state. The stream is live-only: pages seed their state from REST and apply the
// ServerEvents here on top. Reconnects with backoff; the token rides along as ?token= since
// browsers can't set headers on an upgrade.

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
};

export const live = $state<LiveState>({
	connected: false,
	error: '',
	serverVersion: '',
	presence: new Map(),
	present: new Map(),
	events: [],
	version: 0
});

const MAX_EVENTS = 500;

type Listener = (event: ServerEvent) => void;
const listeners = new Set<Listener>();

/** Called for every ServerEvent while subscribed; returns the unsubscribe function. */
export function onLiveEvent(listener: Listener): () => void {
	listeners.add(listener);
	return () => listeners.delete(listener);
}

let socket: WebSocket | undefined;
let wanted = 0;
let retryDelay = 1000;
let retryTimer: ReturnType<typeof setTimeout> | undefined;

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

function open() {
	if (socket || wanted <= 0) return;
	const url = new URL('/1/ws', location.href);
	url.protocol = url.protocol === 'https:' ? 'wss:' : 'ws:';
	url.searchParams.set('token', getServerToken());
	const ws = new WebSocket(url);
	ws.binaryType = 'arraybuffer';
	socket = ws;

	ws.onopen = () => {
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
	};
	ws.onmessage = (message: MessageEvent<ArrayBuffer>) => {
		messageCount += 1;
		const decoded = fromBinary(ServerMessageSchema, new Uint8Array(message.data));
		switch (decoded.message.case) {
			case 'hello':
				live.serverVersion = decoded.message.value.serverVersion;
				break;
			case 'event':
				apply(decoded.message.value);
				break;
		}
	};
	ws.onerror = () => {
		live.error = 'Live connection failed';
	};
	ws.onclose = (event) => {
		if (socket === ws) socket = undefined;
		live.connected = false;
		if (wanted <= 0) return;
		if (event.code === 1008 || event.code === 4401) live.error = 'The server rejected the token';
		retryTimer = setTimeout(open, retryDelay);
		retryDelay = Math.min(retryDelay * 2, 30000);
	};
}

function close() {
	if (retryTimer) clearTimeout(retryTimer);
	retryTimer = undefined;
	const ws = socket;
	socket = undefined;
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
	let timer: ReturnType<typeof setTimeout> | undefined;
	let last = Date.now();
	const unsubscribe = onLiveEvent((event) => {
		const target = id();
		if (!target) return;
		const matches =
			kind === 'user'
				? event.event.case === 'user' && event.event.value.userId === target
				: event.event.case === 'content' && event.event.value.kind === kind && event.event.value.id === target;
		if (!matches) return;
		clearTimeout(timer);
		timer = setTimeout(() => {
			last = Date.now();
			reload();
		}, Math.max(1000, 30_000 - (Date.now() - last)));
	});
	return () => {
		clearTimeout(timer);
		unsubscribe();
	};
}

export type LiveEventKind = NonNullable<ServerEvent['event']['case']>;

/**
 * The usual page wiring: keeps the socket open and calls `run`, debounced by `delay` ms, for each
 * event that `matches` (a list of event kinds, or a test). Returns the cleanup, so an $effect can
 * return it as it is.
 */
export function watchLive(matches: readonly LiveEventKind[] | ((event: ServerEvent) => boolean), run: () => unknown, delay: number): () => void {
	const test = typeof matches === 'function' ? matches : (event: ServerEvent) => !!event.event.case && matches.includes(event.event.case);
	const disconnect = connectLive();
	let timer: ReturnType<typeof setTimeout> | undefined;
	const unsubscribe = onLiveEvent((event) => {
		if (!test(event)) return;
		clearTimeout(timer);
		timer = setTimeout(run, delay);
	});
	return () => {
		clearTimeout(timer);
		unsubscribe();
		disconnect();
	};
}
