import { describe, expect, test } from 'vitest';
import { ApiError } from '../api/client.js';
import { canRun, formatBytes, formatElapsed, reportRows, reportTotal, uploadInParts, type LegacyImportStatus, type LegacyImportTransport, type LegacyTableReport } from '../settings/legacy-import.js';

/** A server that keeps what it was sent, as /1/legacy-import does: a part must start where the copy ends. */
function fakeServer(script: { failPut?: (call: number, offset: number) => 'network' | 'lostAnswer' | 'refuse' | undefined } = {}) {
	let received = new Uint8Array(0);
	let size = 0;
	let calls = 0;
	const uploadId = 'up_1';
	const status = (): LegacyImportStatus => ({ uploadId, state: received.length === size ? 'uploaded' : 'uploading', size, received: received.length });
	const parts: number[] = [];
	const transport: LegacyImportTransport = {
		async start(total) {
			size = total;
			received = new Uint8Array(0);
			return status();
		},
		async put(id, offset, part) {
			const call = ++calls;
			const failure = script.failPut?.(call, offset);
			if (failure === 'network') throw new ApiError(0, undefined, "Couldn't reach the server");
			if (failure === 'refuse') throw new ApiError(507, { type: 'LegacyImport.NoSpace', title: 'No space', status: 507 }, 'No space');
			if (id !== uploadId) throw new ApiError(404, { type: 'LegacyImport.UnknownUpload', title: '', status: 404 }, '');
			if (offset !== received.length) throw new ApiError(409, { type: 'LegacyImport.WrongOffset', title: '', status: 409, detail: `The upload continues at byte ${received.length}` }, '');
			const bytes = new Uint8Array(await part.arrayBuffer());
			const next = new Uint8Array(received.length + bytes.length);
			next.set(received);
			next.set(bytes, received.length);
			received = next;
			parts.push(bytes.length);
			// The server kept the part, but its answer never reached the page.
			if (failure === 'lostAnswer') throw new ApiError(0, undefined, "Couldn't reach the server");
			return status();
		},
		async status() {
			return status();
		}
	};
	return { transport, parts, received: () => received };
}

const file = (length: number) => new Blob([Uint8Array.from({ length }, (_, i) => i % 251)]);
const noWait = async () => {};

describe('uploadInParts', () => {
	test('sends the file in parts of the given size and reports how far it is', async () => {
		const server = fakeServer();
		const progress: number[] = [];
		const done = await uploadInParts(file(1000), server.transport, { partSize: 300, onProgress: (received) => progress.push(received), wait: noWait });

		expect(server.parts).toEqual([300, 300, 300, 100]);
		expect(progress).toEqual([0, 300, 600, 900, 1000]);
		expect(done.state).toBe('uploaded');
		expect(Array.from(server.received())).toEqual(Array.from(new Uint8Array(await file(1000).arrayBuffer())));
	});

	test('a part whose answer was lost is not sent twice, and a dropped one is sent again', async () => {
		const server = fakeServer({ failPut: (call) => (call === 2 ? 'lostAnswer' : call === 3 ? 'network' : undefined) });
		const done = await uploadInParts(file(1000), server.transport, { partSize: 400, wait: noWait });

		expect(server.parts).toEqual([400, 400, 200]);
		expect(done.received).toBe(1000);
		expect(server.received().length).toBe(1000);
	});

	test('gives up after the retries, and at once on a refusal that is not about the offset', async () => {
		const down = fakeServer({ failPut: () => 'network' });
		await expect(uploadInParts(file(100), down.transport, { partSize: 50, retries: 2, wait: noWait })).rejects.toThrow("Couldn't reach the server");

		let puts = 0;
		const full = fakeServer({ failPut: () => (++puts, 'refuse') });
		await expect(uploadInParts(file(100), full.transport, { partSize: 50, wait: noWait })).rejects.toThrow('No space');
		expect(puts).toBe(1);
	});

	test('stops when the page gives up', async () => {
		const server = fakeServer();
		const abort = new AbortController();
		const upload = uploadInParts(file(1000), server.transport, { partSize: 100, wait: noWait, onProgress: (received) => received >= 300 && abort.abort(), signal: abort.signal });
		await expect(upload).rejects.toThrow();
		expect(server.parts.length).toBe(3);
	});
});

describe('the report', () => {
	const tables: LegacyTableReport[] = [
		{ source: 'gamelog_location', target: 'stored_instance_visits', read: 10, candidates: 8, inserted: 5, updated: 0, skipped: { 'not an instance': 2 } },
		{ source: 'memos', target: 'user_memos', read: 3, candidates: 3, inserted: 0, updated: 1, skipped: {} },
		{ source: 'cookies', target: '(not imported)', read: 0, candidates: 0, updated: 0, skipped: {} }
	];

	test('an import lists what was added and what was there, leaving out empty tables', () => {
		const report = { dryRun: false, tables, notes: [], elapsed: '00:00:20' };
		expect(reportRows(report)).toEqual([
			{ source: 'gamelog_location', target: 'stored_instance_visits', read: 10, count: 5, already: 3, skipped: 2 },
			{ source: 'memos', target: 'user_memos', read: 3, count: 0, already: 3, skipped: 0 }
		]);
		expect(reportTotal(report)).toBe(5);
	});

	test('a dry run lists what would be added', () => {
		const report = { dryRun: true, tables: tables.map(({ inserted: _, ...t }) => t), notes: [], elapsed: '00:00:20' };
		expect(reportRows(report).map((row) => [row.count, row.already])).toEqual([
			[8, undefined],
			[3, undefined]
		]);
		expect(reportTotal(report)).toBe(11);
	});
});

test('a file can be checked or imported once it is all there and not yet imported', () => {
	const base = { uploadId: 'up_1', size: 10, received: 10 };
	expect(canRun({ ...base, state: 'uploaded' })).toBe(true);
	expect(canRun({ ...base, state: 'checked' })).toBe(true);
	expect(canRun({ ...base, state: 'failed' })).toBe(true);
	expect(canRun({ ...base, state: 'imported' })).toBe(false);
	expect(canRun({ ...base, state: 'checking' })).toBe(false);
	expect(canRun({ ...base, received: 5, state: 'failed' })).toBe(false);
	expect(canRun({ state: 'none', size: 0, received: 0 })).toBe(false);
});

test("a run's time reads in seconds", () => {
	expect(formatElapsed('00:00:20.4321000')).toBe('20 s');
	expect(formatElapsed('00:01:05')).toBe('65 s');
	expect(formatElapsed('1.00:00:00')).toBe('86400 s');
	expect(formatElapsed('soon')).toBe('soon');
});

test('sizes read as the page shows them', () => {
	expect(formatBytes(512)).toBe('512 B');
	expect(formatBytes(776_000_000)).toBe('776.0 MB');
	expect(formatBytes(1_200_000_000)).toBe('1.2 GB');
});
