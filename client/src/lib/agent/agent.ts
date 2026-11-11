// The PC features the original did itself, done by the account's agent through the server
// (server/API/Controller/Accounts/Agent.cs). Offer each only when agentCan() says the PC does it.
import { api } from '#lib/api/client.js';
import { ownPath } from '#lib/api/vrchat-auth.svelte.js';

/** The folders the agent opens; the server never sends it a path. */
export type AgentFolder = 'photos' | 'screenshots' | 'app_data' | 'ugc_photos' | 'crash_dumps' | 'agent_data' | 'shortcuts' | 'cache';

/** What a native picker on the PC is for. */
export type PickPurpose = 'vrchat_path' | 'ugc_folder' | 'shortcut_folder' | 'folder';

/** Opens the folder on the PC, selecting an item inside it when given (the original's Open*Folder). */
export const openFolder = (folder: AgentFolder, relativePath?: string): Promise<void> =>
	api.post<void>(ownPath('/agent/open-folder'), { folder, relativePath: relativePath ?? null });

/** Shows the picker on the PC; the path chosen, or '' when it was cancelled. */
export async function pickPath(purpose: PickPurpose, start?: string): Promise<string> {
	const answer = await api.post<{ path?: string } | null>(ownPath('/agent/pick-path'), { purpose, start: start ?? null });
	return answer?.path ?? '';
}

/** CropAllPrints over the UGC folder's prints, after the user agreed. */
export const cropAllPrints = (): Promise<{ cropped: number }> => api.post(ownPath('/agent/prints/crop'), { confirm: true });

/** DeleteAllScreenshotMetadata over the PC's photos folder, after the user agreed. How many pictures changed. */
export async function deleteAllScreenshotMetadata(): Promise<number> {
	const answer = await api.post<{ changed?: number } | null>(ownPath('/agent/screenshot-metadata/delete-all'), { confirm: true });
	return answer?.changed ?? 0;
}

/** VRChat's config.json on the PC as text; '' when VRChat has none yet. */
export async function readVrchatConfig(): Promise<string> {
	const answer = await api.get<{ exists: boolean; json: string }>(ownPath('/agent/vrchat-config'));
	return answer.exists ? answer.json : '';
}

export const writeVrchatConfig = (json: string): Promise<void> => api.put<void>(ownPath('/agent/vrchat-config'), { json });

/** One PlayerPrefs value as the original keeps it: 3 binary text, 4 DWORD, 100 a float. */
export type RegistryValue = { data: string | number; type: number };
export type RegistryData = Record<string, RegistryValue>;

export type RegistryBackup = { id: number; name: string; date: string; data: RegistryData };

/** A JSON file is a registry backup when every key holds a type number and data (restoreVrcRegistryFromFile's check). */
export function isRegistryData(value: unknown): value is RegistryData {
	if (!value || typeof value !== 'object' || Array.isArray(value)) return false;
	return Object.values(value).every((v) => !!v && typeof v === 'object' && typeof (v as RegistryValue).type === 'number' && typeof (v as RegistryValue).data !== 'undefined');
}

export const listRegistryBackups = (): Promise<RegistryBackup[]> => api.get<RegistryBackup[]>(ownPath('/registry-backups'));
export const createRegistryBackup = (name: string): Promise<RegistryBackup> => api.post<RegistryBackup>(ownPath('/registry-backups'), { name });
export const restoreRegistryBackup = (id: number): Promise<void> => api.post<void>(ownPath(`/registry-backups/${id}/restore`), { confirm: true });
export const deleteRegistryBackup = (id: number): Promise<void> => api.delete(ownPath(`/registry-backups/${id}`));
export const restoreRegistry = (data: RegistryData): Promise<void> => api.post<void>(ownPath('/agent/vrchat-registry/restore'), { data, confirm: true });
export const deleteVrchatRegistry = (): Promise<void> => api.post<void>(ownPath('/agent/vrchat-registry/delete'), { confirm: true });

/** checkAutoBackupRestoreVrcRegistry: true when VRChat's registry is gone and the backups should be offered. */
export async function checkRegistryBackups(): Promise<boolean> {
	return (await api.post<{ askRestore: boolean }>(ownPath('/registry-backups/check'))).askRestore;
}

/** GetVRChatRegistryKey: the value, or null when VRChat has none. */
export async function readRegistryKey(key: string): Promise<string | number | null> {
	return (await api.get<{ value: string | number | null }>(ownPath(`/agent/vrchat-registry/keys/${encodeURIComponent(key)}`))).value;
}

export const setRegistryKey = (key: string, value: unknown, type: number): Promise<void> =>
	api.put<void>(ownPath(`/agent/vrchat-registry/keys/${encodeURIComponent(key)}`), { value, type });

/** VRChat's local avatar moderations: 4 hides a user's avatar, 5 shows it. */
export const AVATAR_HIDE = 4;
export const AVATAR_SHOW = 5;

/** GetVRChatModerations for the signed-in user; null when VRChat has no list yet. */
export const readLocalModerations = (): Promise<Record<string, number> | null> => api.get<Record<string, number> | null>(ownPath('/agent/local-moderations'));

export const setLocalModeration = (userId: string, type: number): Promise<void> =>
	api.put<void>(ownPath(`/agent/local-moderations/${encodeURIComponent(userId)}`), { type });
