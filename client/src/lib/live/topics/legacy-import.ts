import { timestampDate } from '@bufbuild/protobuf/wkt';
import type { LegacyImportUpdate, TopicDelta, TopicSnapshot } from '#lib/gen/vrcx_live_pb.js';
import type { TopicCodec } from '#lib/api/topics.svelte.js';
import type { LegacyImportState, LegacyImportStatus } from '#lib/settings/legacy-import.js';

// The "legacy_import" topic's payloads as the status the Settings page shows. The server's state is
// small, so the snapshot and every delta are the whole status: each one replaces the last. Not an
// account's topic: it is subscribed with no account. Pure: legacy-import.svelte.ts is the store.

export const LEGACY_IMPORT_TOPIC = 'legacy_import';

const STATES: readonly LegacyImportState[] = ['none', 'uploading', 'uploaded', 'checking', 'checked', 'importing', 'imported', 'failed'];

/** The status as the page uses it: what the server leaves empty is left out. */
export function statusFromUpdate(u: LegacyImportUpdate): LegacyImportStatus {
	return {
		uploadId: u.uploadId || undefined,
		state: STATES.includes(u.state as LegacyImportState) ? (u.state as LegacyImportState) : 'none',
		size: Number(u.size),
		received: Number(u.received),
		progress: u.steps > 0 ? { step: u.step, steps: u.steps, name: u.stepName } : undefined,
		report: u.report && {
			dryRun: u.report.dryRun,
			elapsedSeconds: u.report.elapsedSeconds,
			notes: [...u.report.notes],
			tables: u.report.tables.map((t) => ({
				source: t.source,
				target: t.target,
				read: Number(t.read),
				candidates: Number(t.candidates),
				inserted: t.inserted === undefined ? undefined : Number(t.inserted),
				updated: Number(t.updated),
				skipped: Object.fromEntries(Object.entries(t.skipped).map(([reason, count]) => [reason, Number(count)]))
			}))
		},
		error: u.error || undefined,
		finishedAt: u.finishedAt ? timestampDate(u.finishedAt).toISOString() : undefined
	};
}

export const legacyImportCodec: TopicCodec<LegacyImportStatus> = {
	snapshot: (payload: TopicSnapshot['payload']) => (payload.case === 'legacyImport' ? statusFromUpdate(payload.value) : undefined),
	delta: (_value, payload: TopicDelta['payload']) => (payload.case === 'legacyImport' ? statusFromUpdate(payload.value) : undefined)
};
