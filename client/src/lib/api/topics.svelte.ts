import { create } from '@bufbuild/protobuf';
import { SubscribeSchema, TopicCloseReason, UnsubscribeSchema, type TopicDelta, type TopicSnapshot } from '#lib/gen/vrcx_live_pb.js';
import { connectLive, liveReady, live, onHello, onTopicMessage, sendLive, type TopicMessage } from '#lib/api/live.svelte.js';

// The client side of the live socket's topics (docs/live-topics.md): server state kept as a copy
// here, sent as one snapshot and then deltas. subscribeTopic(topic, account, codec) hands out a
// shared store per topic and account: the first holder subscribes, later ones share it, and the
// last release unsubscribes. The store keeps the rules of the stream so its users need not:
// - a delta whose rev is not the last one plus one means one was missed: it is dropped and the
//   topic subscribed again, while the store keeps showing what it has until the new snapshot;
// - every ServerHello (the first connection and each reconnect) subscribes again, since the
//   server forgets subscriptions with the connection, and the fresh snapshot replaces the copy;
// - a server whose hello does not list the topic (older than topics) makes the store
//   'unsupported', and its users fall back to what they did before topics (REST and events).

/** How a topic's payloads become a value: the pure half, testable without a socket. */
export type TopicCodec<S> = {
	/** The snapshot's payload as the whole value; undefined when it is not this topic's case. */
	snapshot(payload: TopicSnapshot['payload']): S | undefined;
	/** The value after a delta; undefined when it is not this topic's case. */
	delta(value: S, payload: TopicDelta['payload']): S | undefined;
};

/** connecting: waiting for a snapshot; live: the value is current; unsupported: the server has no such topic; closed: refused (no such account). */
export type TopicStatus = 'connecting' | 'live' | 'unsupported' | 'closed';

export type TopicListener<S> = (value: S | undefined, status: TopicStatus) => void;

/** One holder's share of a topic store. */
export type TopicHandle<S> = {
	readonly value: S | undefined;
	readonly status: TopicStatus;
	/** Called on every change of value or status, and once now; returns the unsubscribe. */
	listen(listener: TopicListener<S>): () => void;
	/** This holder is done; the subscription ends with the last one. */
	release(): void;
};

/** How long after an overflow or error closing the topic is subscribed again. */
export const RESUBSCRIBE_DELAY_MS = 1000;

class TopicEntry<S> {
	value = $state.raw<S | undefined>(undefined);
	status = $state<TopicStatus>('connecting');
	refs = 0;
	subId = 0;
	rev: bigint | undefined;
	retry: ReturnType<typeof setTimeout> | undefined;
	listeners = new Set<TopicListener<S>>();
	disconnect: () => void = () => {};

	constructor(
		readonly key: string,
		readonly topic: string,
		readonly account: string,
		readonly codec: TopicCodec<S>
	) {}

	set(value: S | undefined, status: TopicStatus) {
		this.value = value;
		this.status = status;
		for (const listener of this.listeners) listener(value, status);
	}
}

const entries = new Map<string, TopicEntry<unknown>>();
const bySubId = new Map<number, TopicEntry<unknown>>();
let nextSubId = 1;
let wired = false;

/** Asks for a fresh stream: a new sub id, so the old stream's stragglers are told apart and ignored. */
function subscribe(entry: TopicEntry<unknown>) {
	clearTimeout(entry.retry);
	entry.retry = undefined;
	if (!liveReady()) return; // The next hello subscribes.
	if (live.topics && !live.topics.includes(entry.topic)) {
		entry.set(entry.value, 'unsupported');
		return;
	}
	if (entry.subId) {
		bySubId.delete(entry.subId);
		sendLive({ case: 'unsubscribe', value: create(UnsubscribeSchema, { subId: entry.subId }) });
	}
	entry.subId = nextSubId++;
	entry.rev = undefined;
	bySubId.set(entry.subId, entry);
	if (!sendLive({ case: 'subscribe', value: create(SubscribeSchema, { subId: entry.subId, topic: entry.topic, accountId: entry.account }) })) bySubId.delete(entry.subId);
}

function received(message: TopicMessage) {
	const entry = bySubId.get(message.value.subId);
	if (!entry) return; // An old stream's straggler, or one released.
	switch (message.case) {
		case 'topicSnapshot': {
			const value = entry.codec.snapshot(message.value.payload);
			if (value === undefined) return;
			entry.rev = message.value.rev;
			entry.set(value, 'live');
			break;
		}
		case 'topicDelta': {
			const delta = message.value;
			const next = entry.rev !== undefined && entry.value !== undefined && delta.rev === entry.rev + 1n ? entry.codec.delta(entry.value, delta.payload) : undefined;
			if (next === undefined) {
				// Missed one (or never had the snapshot): the copy can't be trusted to follow, so start over.
				subscribe(entry);
				return;
			}
			entry.rev = delta.rev;
			entry.set(next, 'live');
			break;
		}
		case 'topicClosed': {
			bySubId.delete(entry.subId);
			entry.subId = 0;
			const reason = message.value.reason;
			if (reason === TopicCloseReason.UNKNOWN_TOPIC) entry.set(entry.value, 'unsupported');
			else if (reason === TopicCloseReason.OVERFLOW || reason === TopicCloseReason.ERROR) {
				clearTimeout(entry.retry);
				entry.retry = setTimeout(() => subscribe(entry), RESUBSCRIBE_DELAY_MS);
			} else entry.set(entry.value, 'closed');
			break;
		}
	}
}

function wire() {
	if (wired) return;
	wired = true;
	onTopicMessage(received);
	// A new connection knows none of this page's subscriptions: every one starts over with a snapshot.
	onHello(() => {
		bySubId.clear();
		for (const entry of entries.values()) {
			entry.subId = 0;
			subscribe(entry);
		}
	});
}

/** The shared store for a topic of an account; keep the handle and release it when done. */
export function subscribeTopic<S>(topic: string, account: string, codec: TopicCodec<S>): TopicHandle<S> {
	wire();
	const key = `${topic}\u0000${account}`;
	let entry = entries.get(key) as TopicEntry<S> | undefined;
	if (!entry) {
		entry = new TopicEntry<S>(key, topic, account, codec);
		entries.set(key, entry as TopicEntry<unknown>);
		entry.disconnect = connectLive();
		subscribe(entry as TopicEntry<unknown>);
	}
	entry.refs += 1;
	const held = entry;
	let released = false;
	const mine = new Set<TopicListener<S>>();
	return {
		get value() {
			return held.value;
		},
		get status() {
			return held.status;
		},
		listen(listener) {
			held.listeners.add(listener);
			mine.add(listener);
			listener(held.value, held.status);
			return () => {
				held.listeners.delete(listener);
				mine.delete(listener);
			};
		},
		release() {
			if (released) return;
			released = true;
			for (const listener of mine) held.listeners.delete(listener);
			held.refs -= 1;
			if (held.refs > 0) return;
			clearTimeout(held.retry);
			entries.delete(held.key);
			if (held.subId) {
				bySubId.delete(held.subId);
				sendLive({ case: 'unsubscribe', value: create(UnsubscribeSchema, { subId: held.subId }) });
			}
			held.disconnect();
		}
	};
}
