import { api, ApiError } from '#lib/api/client.js';

// Importing an original VRCX database (VRCX.sqlite3) from the Settings page. The original has no
// such thing: it is the database. The file goes to the server in parts, since these files are
// often larger than 700 MB and a proxy in front of the server (Cloudflare's takes 100 MB) refuses a
// body that large. Then a dry run reports what the file holds, and the import brings it in. The
// page follows all of it on the "legacy_import" topic (live/topics/legacy-import.ts).

export type LegacyImportState = 'none' | 'uploading' | 'uploaded' | 'checking' | 'checked' | 'importing' | 'imported' | 'failed';

export type LegacyTableReport = {
	source: string;
	target: string;
	read: number;
	candidates: number;
	/** Absent in a dry run, which does not look at the database. */
	inserted?: number;
	updated: number;
	skipped: Record<string, number>;
};

export type LegacyImportReport = { dryRun: boolean; tables: LegacyTableReport[]; notes: string[]; elapsedSeconds: number };

export type LegacyImportStatus = {
	uploadId?: string;
	state: LegacyImportState;
	size: number;
	received: number;
	progress?: { step: number; steps: number; name: string };
	report?: LegacyImportReport;
	error?: string;
	finishedAt?: string;
};

/** Where an upload stands, as the server answers each step of it. */
export type UploadPosition = Pick<LegacyImportStatus, 'uploadId' | 'state' | 'size' | 'received'>;

/** How the upload reaches the server; the page passes `legacyImportApi`, the tests a fake. */
export type LegacyImportTransport = {
	start(size: number): Promise<UploadPosition>;
	put(uploadId: string, offset: number, part: Blob): Promise<UploadPosition>;
	/**
	 * Where the server's copy ends, asked only after a part failed. What the page shows comes from
	 * the topic; this is the upload finding its place again, at the moment it needs it.
	 */
	position(): Promise<UploadPosition>;
};

export const legacyImportApi: LegacyImportTransport & {
	dryRun(uploadId: string): Promise<unknown>;
	import(uploadId: string): Promise<unknown>;
	discard(): Promise<void>;
} = {
	start: (size) => api.post<UploadPosition>('/1/legacy-import', { size }),
	put: (uploadId, offset, part) => api.putBytes<UploadPosition>(`/1/legacy-import/${encodeURIComponent(uploadId)}`, part, { offset }),
	position: () => api.get<UploadPosition>('/1/legacy-import'),
	dryRun: (uploadId) => api.post(`/1/legacy-import/${encodeURIComponent(uploadId)}/dry-run`),
	import: (uploadId) => api.post(`/1/legacy-import/${encodeURIComponent(uploadId)}/import`),
	discard: () => api.delete('/1/legacy-import')
};

/** 16 MiB a part: well under what a proxy takes, and the server takes up to 64 MiB. */
export const PART_SIZE = 16 * 1024 * 1024;

/** How often a part that failed (a dropped connection) is tried again before the upload gives up. */
export const PART_RETRIES = 3;

export type UploadOptions = {
	partSize?: number;
	retries?: number;
	signal?: AbortSignal;
	onProgress?: (received: number, size: number) => void;
	/** Waits before a retry; the tests pass one that does not. */
	wait?: (ms: number) => Promise<void>;
};

/**
 * Sends the file in parts, each starting where the server's copy ends. When a part fails, the
 * server is asked how much it has and the upload goes on from there: a part whose answer was lost
 * is not sent twice, and one cut off half way is sent again whole.
 */
export async function uploadInParts(file: Blob, transport: LegacyImportTransport, options: UploadOptions = {}): Promise<UploadPosition> {
	const partSize = options.partSize ?? PART_SIZE;
	const retries = options.retries ?? PART_RETRIES;
	const wait = options.wait ?? ((ms: number) => new Promise((resolve) => setTimeout(resolve, ms)));
	let status = await transport.start(file.size);
	const uploadId = status.uploadId;
	if (!uploadId) throw new Error('The server did not start an upload');
	let offset = status.received;
	let failures = 0;
	options.onProgress?.(offset, file.size);
	while (offset < file.size) {
		options.signal?.throwIfAborted();
		try {
			status = await transport.put(uploadId, offset, file.slice(offset, Math.min(offset + partSize, file.size)));
			failures = 0;
		} catch (err) {
			const error = ApiError.from(err);
			// The server knows the upload but not this part: anything but a lost or refused part ends it.
			if (error.status !== 0 && error.type !== 'LegacyImport.WrongOffset') throw error;
			if (++failures > retries) throw error;
			if (error.status === 0) await wait(1000 * failures);
			status = await transport.position();
			if (status.uploadId !== uploadId) throw error;
		}
		offset = status.received;
		options.onProgress?.(offset, file.size);
	}
	return status;
}

/** A check or an import is running on the server: the page shows its steps until it is not. */
export const isRunning = (state: LegacyImportState) => state === 'checking' || state === 'importing';

/** The whole file is on the server and has not been imported yet, so it can be checked or imported. */
export const canRun = (status: UploadPosition) =>
	!!status.uploadId && status.size > 0 && status.received === status.size && (status.state === 'uploaded' || status.state === 'checked' || status.state === 'failed');

export type ReportRow = { source: string; target: string; read: number; count: number; already?: number; skipped: number };

/** The report's tables that hold anything, as the page lists them: what was read, what would be or was added, what was already there. */
export function reportRows(report: LegacyImportReport): ReportRow[] {
	return report.tables
		.filter((t) => t.read > 0 || t.candidates > 0)
		.map((t) => ({
			source: t.source,
			target: t.target,
			read: t.read,
			count: report.dryRun ? t.candidates : (t.inserted ?? 0),
			already: report.dryRun || t.inserted === undefined ? undefined : t.candidates - t.inserted,
			skipped: Object.values(t.skipped).reduce((sum, n) => sum + n, 0)
		}));
}

/** Rows a dry run would add, or an import added, across every table. */
export const reportTotal = (report: LegacyImportReport) => reportRows(report).reduce((sum, row) => sum + row.count, 0);

/** How long a run took, as "65 s". */
export const formatElapsed = (seconds: number) => `${Math.round(seconds)} s`;

/** "1.2 GB": sizes as the page shows them. */
export function formatBytes(bytes: number): string {
	const units = ['B', 'KB', 'MB', 'GB', 'TB'];
	let value = bytes;
	let unit = 0;
	while (value >= 1000 && unit < units.length - 1) {
		value /= 1000;
		unit++;
	}
	return `${unit === 0 ? value : value.toFixed(1)} ${units[unit]}`;
}
