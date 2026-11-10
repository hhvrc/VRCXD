import { describe, expect, it } from 'vitest';
import { deleteTextMetadata, getScreenshotMetadata, isScreenshotPath, parseLfs, readResolution, screenshotDate, searchMatch } from '../content/screenshot-metadata.js';

const enc = new TextEncoder();
function chunk(type: string, data: Uint8Array): Uint8Array {
	const out = new Uint8Array(12 + data.length);
	new DataView(out.buffer).setUint32(0, data.length);
	out.set(enc.encode(type), 4);
	out.set(data, 8);
	return out; // CRC left as zero; the reader does not check it, like the original.
}
function itxt(keyword: string, text: string) {
	return chunk('iTXt', new Uint8Array([...enc.encode(keyword), 0, 0, 0, 0, 0, ...enc.encode(text)]));
}
function png(...chunks: Uint8Array[]): Uint8Array {
	const ihdr = new Uint8Array(13);
	new DataView(ihdr.buffer).setUint32(0, 1920);
	new DataView(ihdr.buffer).setUint32(4, 1080);
	const parts = [new Uint8Array([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]), chunk('IHDR', ihdr), ...chunks, chunk('IDAT', new Uint8Array(4)), chunk('IEND', new Uint8Array())];
	const out = new Uint8Array(parts.reduce((n, p) => n + p.length, 0));
	let o = 0;
	for (const p of parts) {
		out.set(p, o);
		o += p.length;
	}
	return out;
}

const XMP = `<x:xmpmeta xmlns:x="adobe:ns:meta/"><rdf:RDF><rdf:Description><xmp:CreatorTool>VRChat</xmp:CreatorTool><xmp:Author>Ann</xmp:Author><vrc:WorldID>wrld_1</vrc:WorldID><vrc:WorldDisplayName>Home &amp; Hearth</vrc:WorldDisplayName><vrc:AuthorID>usr_a</vrc:AuthorID><tiff:DateTime>2025-08-01T10:00:00</tiff:DateTime><dc:title><rdf:Alt><rdf:li xml:lang="x-default">Hi</rdf:li></rdf:Alt></dc:title></rdf:Description></rdf:RDF></x:xmpmeta>`;

describe('screenshot metadata', () => {
	it('reads VRChat XMP and the resolution', () => {
		const file = png(itxt('XML:com.adobe.xmp', XMP));
		const m = getScreenshotMetadata(file);
		expect(m.application).toBe('VRChat');
		expect(m.author).toEqual({ id: 'usr_a', displayName: 'Ann' });
		expect(m.world).toEqual({ id: 'wrld_1', instanceId: 'wrld_1', name: 'Home & Hearth' });
		expect(m.note).toBe('Hi');
		expect(readResolution(file)).toBe('1920x1080');
	});

	it('lets VRCX JSON replace the XMP, as the original does', () => {
		const json = JSON.stringify({ application: 'VRCX', version: 1, author: { id: 'usr_a', displayName: 'Ann' }, world: { id: 'wrld_1', name: 'Home', instanceId: 'wrld_1:123' }, players: [{ id: 'usr_b', displayName: 'Bo' }] });
		const m = getScreenshotMetadata(png(itxt('XML:com.adobe.xmp', XMP), itxt('Description', json)));
		expect(m.application).toBe('VRCX');
		expect(m.world.instanceId).toBe('wrld_1:123');
		expect(m.players.map((p) => p.displayName)).toEqual(['Bo']);
		expect(searchMatch(m, 'bo', 'Player Name')).toEqual({ matches: true, match: 'Bo' });
		expect(searchMatch(m, 'usr_b', 'Player ID').matches).toBe(true);
		expect(searchMatch(m, 'hom', 'World Name').matches).toBe(true);
	});

	it('says when there is no metadata, and deletes it', () => {
		expect(getScreenshotMetadata(png()).error).toBe('Image has no valid metadata.');
		const cleaned = deleteTextMetadata(png(itxt('XML:com.adobe.xmp', XMP), itxt('Description', '{"application":"VRCX"}')));
		expect(getScreenshotMetadata(cleaned).error).toBe('Image has no valid metadata.');
		expect(readResolution(cleaned)).toBe('1920x1080');
	});

	it('reads LFS v2', () => {
		const m = parseLfs('lfs|2|author:usr_1,Nat|world:wrld_2,35372,pet park|pos:1,2,3|players:usr_3,0.1,0.2,0.3,Liv;usr_4,1,1,1,Zet');
		expect(m.author).toEqual({ id: 'usr_1', displayName: 'Nat' });
		expect(m.world).toEqual({ id: 'wrld_2', instanceId: 'wrld_2:35372', name: 'pet park' });
		expect(m.players.map((p) => p.displayName)).toEqual(['Liv', 'Zet']);
	});

	it('dates from the file name and skips prints', () => {
		const none = getScreenshotMetadata(png());
		expect(screenshotDate('VRChat_2024-03-05_10-11-12.345_1920x1080.png', none, 0)).toBe(Date.parse('2024-03-05T10:11:12'));
		expect(isScreenshotPath('2024-03/Prints/a.png')).toBe(false);
		expect(isScreenshotPath('2024-03/VRChat_a.png')).toBe(true);
	});
});
