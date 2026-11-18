import { beforeEach, describe, expect, test, vi } from 'vitest';
import { create, type MessageInitShape } from '@bufbuild/protobuf';
import { timestampFromDate } from '@bufbuild/protobuf/wkt';
import { NotificationEntrySchema } from '../gen/vrcx_live_pb.js';
import type { Notification } from '../api/types.js';
import { applyNotificationsDelta, applyNotificationsSnapshot, notificationFromEntry } from '../live/topics/notifications.js';

const get = vi.hoisted(() => vi.fn());
const post = vi.hoisted(() => vi.fn(async () => ({})));
vi.mock('#lib/api/client.js', () => ({ api: { get, post }, accountPath: (account: string, rest = '') => `/1/accounts/${account}${rest}` }));
vi.mock('#lib/api/vrchat-auth.svelte.js', () => ({ vrchatAuth: { ownerUserId: 'usr_me' } }));

type Listener = (value: Notification[] | undefined, status: string) => void;
const topic = vi.hoisted(() => ({
	mode: 'connecting',
	listeners: [] as Listener[],
	emit(value: Notification[] | undefined, status: string) {
		for (const listener of this.listeners) listener(value, status);
	}
}));
vi.mock('#lib/live/topics/notifications.svelte.js', () => ({
	notificationsTopic: () => ({
		value: undefined,
		status: topic.mode,
		listen(listener: Listener) {
			topic.listeners.push(listener);
			listener(undefined, topic.mode);
			return () => {};
		},
		release() {}
	})
}));

const at = (minute: number) => timestampFromDate(new Date(Date.UTC(2026, 10, 18, 12, minute)));
const entry = (id: string, minute: number, more: MessageInitShape<typeof NotificationEntrySchema> = {}) =>
	create(NotificationEntrySchema, { id, version: 1, type: 'invite', message: '', createdAt: at(minute), ...more });
const snapshot = (...entries: ReturnType<typeof entry>[]) => applyNotificationsSnapshot({ case: 'notifications', value: { notifications: entries } } as never)!;
const delta = (rows: Notification[], upserts: ReturnType<typeof entry>[], removedIds: string[] = []) =>
	applyNotificationsDelta(rows, { case: 'notifications', value: { upserts, removedIds } } as never);

// The topic's payloads as the rows GET notifications returns.
describe('notifications topic codec', () => {
	test('an entry becomes the REST row: empty strings and unset times are left out, JSON text is parsed', () => {
		const row = notificationFromEntry(
			entry('not_1', 5, {
				version: 2,
				type: 'group.announcement',
				senderUserId: 'usr_bob',
				senderDisplayName: 'Bob',
				title: 'News',
				message: 'hello',
				detailsJson: '{"groupName":"Club"}',
				responsesJson: '[{"type":"delete","data":"","text":"Dismiss"}]',
				seen: true,
				expiresAt: at(30)
			})
		);
		expect(row).toEqual({
			id: 'not_1',
			version: 2,
			type: 'group.announcement',
			senderUserId: 'usr_bob',
			senderDisplayName: 'Bob',
			title: 'News',
			message: 'hello',
			imageUrl: undefined,
			link: undefined,
			linkText: undefined,
			details: { groupName: 'Club' },
			responses: [{ type: 'delete', data: '', text: 'Dismiss' }],
			seen: true,
			hidden: false,
			createdAt: '2026-11-18T12:05:00.000Z',
			expiresAt: '2026-11-18T12:30:00.000Z'
		});
		const bare = notificationFromEntry(entry('not_2', 6, { detailsJson: 'not json' }));
		expect([bare.details, bare.responses, bare.expiresAt, bare.senderUserId]).toEqual([undefined, undefined, undefined, undefined]);
	});

	test('a snapshot is the list as sent, and another topic\'s payload is not ours', () => {
		expect(snapshot(entry('b', 2), entry('a', 1)).map((n) => n.id)).toEqual(['b', 'a']);
		expect(applyNotificationsSnapshot({ case: 'friends', value: { friends: [] } } as never)).toBeUndefined();
		expect(applyNotificationsDelta([], { case: 'friends', value: { upserts: [], removedUserIds: [] } } as never)).toBeUndefined();
	});

	test('a delta replaces, adds and removes rows and keeps the list newest first', () => {
		const rows = snapshot(entry('b', 2), entry('a', 1));
		const next = delta(rows, [entry('c', 3), entry('a', 1, { seen: true })], ['b'])!;
		expect(next.map((n) => [n.id, n.seen])).toEqual([['c', false], ['a', true]]);
		expect(next).not.toBe(rows);
		// Two at the same instant are ordered by id, as the server orders them.
		expect(delta(rows, [entry('a2', 1)])!.map((n) => n.id)).toEqual(['b', 'a', 'a2']);
	});

	test('an empty delta leaves the same array', () => {
		const rows = snapshot(entry('a', 1));
		expect(delta(rows, [])).toBe(rows);
	});
});

// The bell follows the topic, and reads over REST only when there is none.
describe('notification center and the topic', () => {
	const row = (id: string, more: Partial<Notification> = {}): Notification => ({ id, version: 1, type: 'invite', message: '', seen: false, hidden: false, createdAt: '2026-11-18T12:00:00.000Z', ...more });

	beforeEach(() => {
		vi.useFakeTimers();
		vi.resetModules();
		topic.listeners = [];
		topic.mode = 'connecting';
		get.mockReset();
		get.mockImplementation(async () => ({ items: [row('rest')] }));
		post.mockClear();
	});

	async function started() {
		const { notificationCenter, NOTIFICATIONS_TOPIC_WAIT_MS } = await import('../notifications/notification-center.svelte.js');
		notificationCenter.start();
		return { center: notificationCenter, wait: NOTIFICATIONS_TOPIC_WAIT_MS };
	}

	test('the snapshot and deltas are the rows, without the hidden ones, and nothing is read over REST', async () => {
		const { center, wait } = await started();
		topic.emit([row('a'), row('gone', { hidden: true })], 'live');
		expect(center.rows.map((n) => n.id)).toEqual(['a']);
		expect(center.followsTopic).toBe(true);

		topic.emit([row('b'), row('a', { seen: true })], 'live');
		expect(center.rows.map((n) => [n.id, n.seen])).toEqual([['b', false], ['a', true]]);
		await vi.advanceTimersByTimeAsync(wait * 2);
		expect(get).not.toHaveBeenCalled();

		// Marking all seen asks the server; the seen rows come back as a delta, not a read.
		await center.markAllSeen();
		expect(post).toHaveBeenCalledTimes(1);
		expect(get).not.toHaveBeenCalled();
	});

	test('a server without the topic is read over REST once, at once', async () => {
		topic.mode = 'unsupported';
		const { center } = await started();
		await vi.advanceTimersByTimeAsync(0);
		expect(get).toHaveBeenCalledTimes(1);
		expect(center.rows.map((n) => n.id)).toEqual(['rest']);
		expect(center.followsTopic).toBe(false);
	});

	test('no snapshot in time is read over REST, and a snapshot coming later takes over', async () => {
		const { center, wait } = await started();
		await vi.advanceTimersByTimeAsync(wait - 1);
		expect(get).not.toHaveBeenCalled();
		await vi.advanceTimersByTimeAsync(1);
		expect(get).toHaveBeenCalledTimes(1);
		expect(center.rows.map((n) => n.id)).toEqual(['rest']);

		topic.emit([row('live')], 'live');
		expect(center.rows.map((n) => n.id)).toEqual(['live']);
		expect(center.followsTopic).toBe(true);
	});

	test('a read that was under way when the snapshot came does not overwrite it', async () => {
		let release: (page: unknown) => void = () => {};
		get.mockImplementation(() => new Promise((resolve) => (release = resolve)));
		topic.mode = 'unsupported';
		const { center } = await started();
		topic.emit([row('live')], 'live');
		release({ items: [row('stale')] });
		await vi.advanceTimersByTimeAsync(0);
		expect(center.rows.map((n) => n.id)).toEqual(['live']);
	});
});
