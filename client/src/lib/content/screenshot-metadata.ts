// The original's screenshot metadata reader (Dotnet/ScreenshotMetadata: ScreenshotHelper, PNGHelper,
// PNGFile): the text chunks of a PNG, read as VRChat's XMP, VRCX's JSON "Description", or the older
// LFS and ScreenshotManager mod formats; and the same chunks removed for Delete Metadata.

export type Vec3 = { x: number; y: number; z: number };
export type ScreenshotPlayer = { id: string; displayName: string; pos?: Vec3 | null };
export type ScreenshotMetadata = {
	application?: string | null;
	version?: number;
	author: { id: string; displayName?: string | null };
	world: { id: string; name?: string | null; instanceId: string };
	players: ScreenshotPlayer[];
	pos?: Vec3 | null;
	timestamp?: string | null;
	note?: string | null;
	error?: string;
};

const SIGNATURE = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];

export type PngChunk = { type: string; start: number; end: number; data: Uint8Array };

export function isPng(bytes: Uint8Array): boolean {
	return bytes.length >= 33 && SIGNATURE.every((b, i) => bytes[i] === b);
}

/** Every chunk in order, including any an old mod appended after IEND, until the data stops making sense. */
export function readChunks(bytes: Uint8Array): PngChunk[] {
	const chunks: PngChunk[] = [];
	const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
	let offset = 8;
	while (offset + 12 <= bytes.length) {
		const length = view.getUint32(offset);
		const type = String.fromCharCode(...bytes.subarray(offset + 4, offset + 8));
		if (!/^[A-Za-z]{4}$/.test(type) || offset + 12 + length > bytes.length) break;
		chunks.push({ type, start: offset, end: offset + 12 + length, data: bytes.subarray(offset + 8, offset + 8 + length) });
		offset += 12 + length;
	}
	return chunks;
}

const utf8 = new TextDecoder();

/** An iTXt chunk's keyword and text (uncompressed, as VRChat and VRCX write them). */
export function readITXt(data: Uint8Array): { keyword: string; text: string; compressed: boolean } {
	const keywordEnd = data.indexOf(0);
	const keyword = utf8.decode(data.subarray(0, keywordEnd));
	const compressed = data[keywordEnd + 1] === 1;
	const languageEnd = data.indexOf(0, keywordEnd + 3);
	const translatedEnd = data.indexOf(0, languageEnd + 1);
	return { keyword, text: utf8.decode(data.subarray(translatedEnd + 1)), compressed };
}

/** The IHDR size as "WxH". */
export function readResolution(bytes: Uint8Array): string {
	const ihdr = readChunks(bytes).find((c) => c.type === 'IHDR');
	if (!ihdr || ihdr.data.length < 8) return '0x0';
	const view = new DataView(ihdr.data.buffer, ihdr.data.byteOffset, 8);
	return `${view.getUint32(0)}x${view.getUint32(4)}`;
}

/** ReadTextMetadata: the XMP first, then VRCX's Description, else an old mod's chunk at the end of the file. */
export function readTextMetadata(bytes: Uint8Array): string[] {
	const chunks = readChunks(bytes);
	const iend = chunks.findIndex((c) => c.type === 'IEND');
	const inSpec = iend < 0 ? chunks : chunks.slice(0, iend + 1);
	const texts = inSpec.filter((c) => c.type === 'iTXt').map((c) => readITXt(c.data));
	const result: string[] = [];
	const xmp = texts.find((t) => t.keyword === 'XML:com.adobe.xmp')?.text;
	const description = texts.find((t) => t.keyword === 'Description')?.text;
	if (xmp) result.push(xmp);
	if (description) result.push(description);
	if (result.length === 0 && chunks.some((c) => c.type === 'sRGB')) {
		const last = [...chunks].reverse().find((c) => c.type === 'iTXt');
		if (last) {
			const legacy = readITXt(last.data);
			if (legacy.keyword === 'Description' && legacy.text) result.push(legacy.text);
		}
	}
	return result;
}

const empty = (): ScreenshotMetadata => ({ author: { id: '' }, world: { id: '', instanceId: '' }, players: [] });

function xmpValue(xml: string, tag: string): string | undefined {
	const element = new RegExp(`<${tag}(?:\\s[^>]*)?>([\\s\\S]*?)</${tag}>`).exec(xml);
	if (element) return decodeXml(element[1].trim());
	const attribute = new RegExp(`\\s${tag}="([^"]*)"`).exec(xml);
	return attribute ? decodeXml(attribute[1]) : undefined;
}

const decodeXml = (s: string) => s.replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&quot;/g, '"').replace(/&apos;/g, "'").replace(/&amp;/g, '&');

/** ParseVRCImage: VRChat's own XMP. Older files have the author id in Author and the world in vrc:World. */
export function parseVrcXmp(source: string): ScreenshotMetadata {
	const xml = source.substring(source.indexOf('<x:xmpmeta'));
	let authorName = xmpValue(xml, 'xmp:Author');
	let authorId = xmpValue(xml, 'vrc:AuthorID');
	const title = /<dc:title[^>]*>[\s\S]*?<rdf:li[^>]*>([\s\S]*?)<\/rdf:li>/.exec(xml)?.[1];
	const worldId = xmpValue(xml, 'vrc:WorldID') || xmpValue(xml, 'vrc:World') || '';
	if (!authorId) {
		authorId = authorName;
		authorName = undefined;
	}
	const dateTime = xmpValue(xml, 'tiff:DateTime');
	return {
		application: xmpValue(xml, 'xmp:CreatorTool') ?? null,
		version: 1,
		author: { id: authorId ?? '', displayName: authorName ?? null },
		world: { id: worldId, instanceId: worldId, name: xmpValue(xml, 'vrc:WorldDisplayName') ?? null },
		players: [],
		timestamp: dateTime && !Number.isNaN(Date.parse(dateTime)) ? dateTime : null,
		note: title !== undefined ? decodeXml(title.trim()) : null
	};
}

const num = (s: string | undefined) => {
	const n = Number.parseFloat(s ?? '');
	return Number.isFinite(n) ? n : 0;
};

/** ParseLfsPicture: the LFS v1/v2, LFS CVR and ScreenshotManager formats. */
export function parseLfs(source: string): ScreenshotMetadata {
	const metadata = empty();
	let parts = source.split('|');
	if (parts[1] === 'cvr') parts = parts.slice(1);
	const application = parts[0];
	const version = Number.parseInt(parts[1], 10);
	metadata.application = application;
	metadata.version = version;
	const isCvr = application === 'cvr';
	if (application === 'screenshotmanager') {
		const author = parts[2].split(',');
		metadata.author = { id: author[0], displayName: author[1] };
		const world = parts[3].split(',');
		metadata.world = { id: world[0], name: world[2], instanceId: `${world[0]}:${world[1]}` };
		return metadata;
	}
	for (let i = 2; i < parts.length; i++) {
		const [key, value] = parts[i].split(':');
		if (!value) continue;
		const values = value.split(',');
		switch (key) {
			case 'author':
				metadata.author = { id: isCvr ? '' : values[0], displayName: isCvr ? `${values[1]} (${values[0]})` : values[1] };
				break;
			case 'world':
				metadata.world = {
					id: isCvr || version === 1 ? '' : values[0],
					instanceId: isCvr || version === 1 ? '' : `${values[0]}:${values[1]}`,
					name: isCvr ? `${values[2]} (${values[0]})` : version === 1 ? value : values[2]
				};
				break;
			case 'pos':
				metadata.pos = { x: num(values[0]), y: num(values[1]), z: num(values[2]) };
				break;
			case 'players':
				for (const player of value.split(';')) {
					const p = player.split(',');
					metadata.players.push({
						id: isCvr ? '' : p[0],
						displayName: isCvr ? `${p[4]} (${p[0]})` : p[4],
						pos: { x: num(p[1]), y: num(p[2]), z: num(p[3]) }
					});
				}
				break;
		}
	}
	return metadata;
}

/** A value from a JSON object whatever the key's case, as the original's deserializer reads it. */
function pick(obj: unknown, key: string): unknown {
	if (!obj || typeof obj !== 'object') return undefined;
	const found = Object.keys(obj).find((k) => k.toLowerCase() === key.toLowerCase());
	return found === undefined ? undefined : (obj as Record<string, unknown>)[found];
}

const str = (v: unknown) => (typeof v === 'string' ? v : v == null ? '' : String(v));
const vec = (v: unknown): Vec3 | null => (v && typeof v === 'object' ? { x: num(str(pick(v, 'x'))), y: num(str(pick(v, 'y'))), z: num(str(pick(v, 'z'))) } : null);

/** VRCX's own JSON, written into the Description chunk. */
export function parseVrcxJson(source: string): ScreenshotMetadata {
	const json = JSON.parse(source) as unknown;
	const author = pick(json, 'author');
	const world = pick(json, 'world');
	const players = pick(json, 'players');
	return {
		application: str(pick(json, 'application')) || null,
		version: Number(pick(json, 'version')) || 0,
		author: { id: str(pick(author, 'id')), displayName: str(pick(author, 'displayName')) || null },
		world: { id: str(pick(world, 'id')), name: str(pick(world, 'name')) || null, instanceId: str(pick(world, 'instanceId')) },
		players: Array.isArray(players) ? players.map((p) => ({ id: str(pick(p, 'id')), displayName: str(pick(p, 'displayName')), pos: vec(pick(p, 'pos')) })) : [],
		pos: vec(pick(json, 'pos')),
		timestamp: str(pick(json, 'timestamp')) || null,
		note: str(pick(json, 'note')) || null
	};
}

/** GetScreenshotMetadata: the metadata, or an error saying there is none. */
export function getScreenshotMetadata(bytes: Uint8Array): ScreenshotMetadata {
	if (!isPng(bytes)) return { ...empty(), error: 'Invalid file selected. Please select a valid VRChat screenshot.' };
	const texts = readTextMetadata(bytes);
	let result = empty();
	try {
		for (const text of texts) {
			let gotVrchat = false;
			if (text.startsWith('<x:xmpmeta')) {
				result = parseVrcXmp(text);
				gotVrchat = true;
			}
			if (text.startsWith('{') && text.endsWith('}')) {
				const vrcx = parseVrcxJson(text);
				if (gotVrchat) {
					result.players = vrcx.players;
					result.world.instanceId = vrcx.world.instanceId;
				} else result = vrcx;
			}
			if (text.startsWith('lfs') || text.startsWith('screenshotmanager')) result = parseLfs(text);
		}
	} catch {
		return { ...empty(), error: 'Failed to parse metadata.' };
	}
	if (!result.application || texts.length === 0) return { ...empty(), error: 'Image has no valid metadata.' };
	return result;
}

/** DeleteTextMetadata with VRChat's too: the file without its XMP and Description chunks. */
export function deleteTextMetadata(bytes: Uint8Array): Uint8Array {
	const drop = readChunks(bytes).filter((c) => {
		if (c.type !== 'iTXt') return false;
		const keyword = readITXt(c.data).keyword;
		return keyword === 'Description' || keyword === 'XML:com.adobe.xmp';
	});
	if (!drop.length) return bytes;
	const parts: Uint8Array[] = [];
	let at = 0;
	for (const c of drop) {
		parts.push(bytes.subarray(at, c.start));
		at = c.end;
	}
	parts.push(bytes.subarray(at));
	const out = new Uint8Array(parts.reduce((n, p) => n + p.length, 0));
	let offset = 0;
	for (const p of parts) {
		out.set(p, offset);
		offset += p.length;
	}
	return out;
}

const FILE_NAME_DATE =
	/VRChat_((\d{3,})x(\d{3,})_(\d{4})-(\d{2})-(\d{2})_(\d{2})-(\d{2})-(\d{2})\.(\d{1,})|(\d{4})-(\d{2})-(\d{2})_(\d{2})-(\d{2})-(\d{2})\.(\d{3})_(\d{3,})x(\d{3,}))/;

/** When the picture was taken: its metadata timestamp, else VRChat's file name, else the file's own date. */
export function screenshotDate(fileName: string, metadata: ScreenshotMetadata, fileDate: number): number {
	if (metadata.timestamp) {
		const t = Date.parse(metadata.timestamp);
		if (t) return t;
	}
	const m = FILE_NAME_DATE.exec(fileName);
	if (m) {
		if (m[2] !== undefined && m[4].length === 4) return Date.parse(`${m[4]}-${m[5]}-${m[6]}T${m[7]}:${m[8]}:${m[9]}`);
		if (m[11] !== undefined && m[11].length === 4) return Date.parse(`${m[11]}-${m[12]}-${m[13]}T${m[14]}:${m[15]}:${m[16]}`);
	}
	return fileDate;
}

export const SEARCH_TYPES = ['Player Name', 'Player ID', 'World Name', 'World ID'] as const;
export type SearchType = (typeof SEARCH_TYPES)[number];

/** FindScreenshots' match rule for one picture, and the matched players for the Match column. */
export function searchMatch(metadata: ScreenshotMetadata, query: string, type: SearchType): { matches: boolean; match: string } {
	const q = query.toLowerCase();
	switch (type) {
		case 'Player Name': {
			const found = metadata.players.filter((p) => p.displayName?.toLowerCase().includes(q));
			return { matches: found.length > 0, match: found.map((p) => p.displayName).join(', ') };
		}
		case 'Player ID': {
			const found = metadata.players.find((p) => p.id === query);
			return { matches: !!found, match: found?.displayName ?? '' };
		}
		case 'World Name':
			return { matches: !!metadata.world.name && metadata.world.name.toLowerCase().includes(q), match: '' };
		case 'World ID':
			return { matches: metadata.world.id === query, match: '' };
	}
}

/** GetLastScreenshot's rule: prints, stickers and emoji folders don't count. */
export const isScreenshotPath = (path: string) => !/(^|[\\/])(Prints|Stickers|Emoji)[\\/]/i.test(path);
