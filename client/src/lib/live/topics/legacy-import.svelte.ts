import { subscribeTopic, type TopicHandle } from '#lib/api/topics.svelte.js';
import { LEGACY_IMPORT_TOPIC, legacyImportCodec } from '#lib/live/topics/legacy-import.js';
import type { LegacyImportStatus } from '#lib/settings/legacy-import.js';

/** The server's VRCX database upload, its check and its import, as they go: shared, so every holder reads one subscription. Release when done. */
export function legacyImportTopic(): TopicHandle<LegacyImportStatus> {
	return subscribeTopic(LEGACY_IMPORT_TOPIC, '', legacyImportCodec);
}
