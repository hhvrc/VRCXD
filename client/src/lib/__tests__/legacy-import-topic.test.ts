import { describe, expect, test } from 'vitest';
import { create } from '@bufbuild/protobuf';
import { timestampFromDate } from '@bufbuild/protobuf/wkt';
import { FriendsSnapshotSchema, LegacyImportUpdateSchema, TopicDeltaSchema, TopicSnapshotSchema } from '../gen/vrcx_live_pb.js';
import { legacyImportCodec, statusFromUpdate } from '../live/topics/legacy-import.js';

// The "legacy_import" topic as the Settings page reads it: the snapshot and every delta are the
// whole status, so each one replaces what the page had.

const update = (init: Parameters<typeof create<typeof LegacyImportUpdateSchema>>[1]) => create(LegacyImportUpdateSchema, init);

describe('legacy import topic', () => {
	test('a status with nothing uploaded leaves out what the server left empty', () => {
		expect(statusFromUpdate(update({ state: 'none' }))).toEqual({ state: 'none', size: 0, received: 0, uploadId: undefined, progress: undefined, report: undefined, error: undefined, finishedAt: undefined });
	});

	test('a finished import maps its step, its report and its time', () => {
		const at = new Date('2026-11-18T12:00:00Z');
		const status = statusFromUpdate(
			update({
				uploadId: 'up_1',
				state: 'imported',
				size: 776_000_000n,
				received: 776_000_000n,
				step: 9,
				steps: 9,
				stepName: 'settings',
				finishedAt: timestampFromDate(at),
				report: {
					dryRun: false,
					elapsedSeconds: 61.5,
					notes: ['x is not known'],
					tables: [
						{ source: 'memos', target: 'user_memos', read: 3n, candidates: 3n, inserted: 1n, updated: 0n, skipped: { empty: 2n } },
						{ source: 'cookies', target: '(not imported)', read: 1n, candidates: 0n, updated: 0n, skipped: {} }
					]
				}
			})
		);

		expect(status.size).toBe(776_000_000);
		expect(status.progress).toEqual({ step: 9, steps: 9, name: 'settings' });
		expect(status.finishedAt).toBe(at.toISOString());
		expect(status.report?.elapsedSeconds).toBe(61.5);
		expect(status.report?.tables).toEqual([
			{ source: 'memos', target: 'user_memos', read: 3, candidates: 3, inserted: 1, updated: 0, skipped: { empty: 2 } },
			{ source: 'cookies', target: '(not imported)', read: 1, candidates: 0, inserted: undefined, updated: 0, skipped: {} }
		]);
	});

	test('an unknown state reads as nothing uploaded rather than as a run', () => {
		expect(statusFromUpdate(update({ state: 'paused' })).state).toBe('none');
	});

	test('a delta replaces the status, and another topic is not this one', () => {
		const before = legacyImportCodec.snapshot(create(TopicSnapshotSchema, { payload: { case: 'legacyImport', value: update({ uploadId: 'up_1', state: 'uploading', size: 10n, received: 4n }) } }).payload)!;
		expect(before.received).toBe(4);

		const after = legacyImportCodec.delta(before, create(TopicDeltaSchema, { payload: { case: 'legacyImport', value: update({ uploadId: 'up_1', state: 'checking', size: 10n, received: 10n, step: 1, steps: 9, stepName: 'names' }) } }).payload);
		expect(after?.state).toBe('checking');
		expect(after?.progress?.name).toBe('names');

		expect(legacyImportCodec.snapshot(create(TopicSnapshotSchema, { payload: { case: 'friends', value: create(FriendsSnapshotSchema) } }).payload)).toBeUndefined();
	});
});
