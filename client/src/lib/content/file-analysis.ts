import { accountPath, api } from '#lib/api/client.js';
import { formatFileSize } from '#lib/helpers/format.js';

/** VRChat's analysis of a content bundle, with the original's formatted sizes added. */
export type FileAnalysis = {
	fileSize?: number;
	uncompressedSize?: number;
	avatarStats?: { totalTextureUsage?: number } & Record<string, unknown>;
	_fileSize?: string;
	_uncompressedSize?: string;
	_totalTextureUsage?: string;
} & Record<string, unknown>;

/**
 * The original's getBundleDateSize: per platform, the analysis of the newest bundle the SDK can open,
 * so the platform badges can show the download size. Empty when VRChat can't be asked.
 */
export async function loadFileAnalysis(account: string, kind: 'worlds' | 'avatars', id: string): Promise<Record<string, FileAnalysis>> {
	const found = await api.get<Record<string, FileAnalysis>>(accountPath(account, `/${kind}/${encodeURIComponent(id)}/file-analysis`)).catch(() => ({}) as Record<string, FileAnalysis>);
	for (const json of Object.values(found)) {
		if (typeof json.fileSize === 'number') json._fileSize = formatFileSize(json.fileSize);
		if (typeof json.uncompressedSize === 'number') json._uncompressedSize = formatFileSize(json.uncompressedSize);
		if (typeof json.avatarStats?.totalTextureUsage === 'number') json._totalTextureUsage = formatFileSize(json.avatarStats.totalTextureUsage);
	}
	return found;
}
