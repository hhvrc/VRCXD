// The PC's Photon lobby (the original's photon store), kept by the server from the Photon logger's
// stream on the PC's pipe: GET /photon, fetched again on each PhotonUpdated event.
import { api } from '#lib/api/client.js';
import { watchLive } from '#lib/api/live.svelte.js';
import { ownPath } from '#lib/api/vrchat-auth.svelte.js';

export type PhotonJoin = {
	joinTime?: string | null;
	hasInstantiated: boolean;
	inVrMode?: boolean | null;
	avatarEyeHeight?: number | null;
	canModerateInstance?: boolean | null;
	groupOnNameplate?: string | null;
	showGroupBadgeToOthers?: boolean | null;
	showSocialRank?: boolean | null;
	useImpostorAsFallback?: boolean | null;
	platform?: string | null;
};

export type PhotonPlayer = { photonId: number; userId?: string | null; displayName?: string | null; isMaster: boolean; join?: PhotonJoin | null };

export type PhotonAvatar = {
	id?: string | null;
	name?: string | null;
	description?: string | null;
	authorId?: string | null;
	authorName?: string | null;
	releaseStatus?: string | null;
	imageUrl?: string | null;
	thumbnailImageUrl?: string | null;
};

export type PhotonEvent = {
	createdAt: string;
	type: string;
	text?: string | null;
	color?: string | null;
	photonId?: number | null;
	displayName: string;
	userId: string;
	isMaster: boolean;
	isModerator?: boolean | null;
	avatar?: PhotonAvatar | null;
	platform?: string | null;
	status?: string | null;
	previousStatus?: string | null;
	statusDescription?: string | null;
	previousStatusDescription?: string | null;
	groupId?: string | null;
	groupName?: string | null;
	previousGroupId?: string | null;
	previousGroupName?: string | null;
	shortName?: string | null;
	location?: string | null;
	worldName?: string | null;
	imageUrl?: string | null;
	fileId?: string | null;
};

export type PhotonTimeout = { userId: string; displayName: string; time: number; rawTime: number };

export type PhotonSnapshot = {
	enabled: boolean;
	location?: string | null;
	master: number;
	currentUser: number;
	players: PhotonPlayer[];
	timeouts: PhotonTimeout[];
	events: PhotonEvent[];
	previousEvents: PhotonEvent[];
};

/** The original's photonEventTableTypeFilterList. */
export const PHOTON_EVENT_TYPES = [
	'Event',
	'OnPlayerJoined',
	'OnPlayerLeft',
	'ChangeAvatar',
	'ChangeStatus',
	'ChangeGroup',
	'PortalSpawn',
	'DeletedPortal',
	'ChatBoxMessage',
	'Moderation',
	'Camera',
	'SpawnEmoji',
	'MasterMigrate'
] as const;

const EMPTY: PhotonSnapshot = { enabled: false, master: 0, currentUser: 0, players: [], timeouts: [], events: [], previousEvents: [] };

/** What updatePlayerListDebounce read off the lobby for one player: matched by id, else by name. */
export type PhotonMarks = { photonId: number; isMaster: boolean; isModerator: boolean; inVrMode: boolean | null; groupOnNameplate: string; timeoutTime: number };

export function photonMarks(lobby: PhotonSnapshot, userId: string, displayName: string): PhotonMarks {
	let photonId = -1;
	for (const p of lobby.players) {
		if ((p.userId && p.userId === userId) || (p.displayName && p.displayName === displayName)) photonId = p.photonId;
	}
	const player = lobby.players.find((p) => p.photonId === photonId);
	const join = player?.join;
	const timeout = lobby.timeouts.find((t) => t.displayName === displayName);
	return {
		photonId,
		isMaster: lobby.master !== 0 && photonId === lobby.master,
		isModerator: !!join?.canModerateInstance,
		inVrMode: join?.inVrMode ?? null,
		groupOnNameplate: join?.groupOnNameplate ?? '',
		timeoutTime: timeout?.time ?? 0
	};
}

/** The table's search and type filter (PhotonEventTable filterRows). */
export function filterPhotonEvents(rows: PhotonEvent[], query: string, types: readonly string[]): PhotonEvent[] {
	const q = query.trim().toLowerCase();
	const set = new Set(types);
	return rows.filter((row) => {
		if (set.size && !set.has(row.type)) return false;
		if (!q) return true;
		return (row.displayName ?? '').toLowerCase().includes(q) || (row.text ?? '').toLowerCase().includes(q);
	});
}

export class PhotonLobby {
	snapshot = $state.raw<PhotonSnapshot>(EMPTY);

	async load() {
		try {
			this.snapshot = await api.get<PhotonSnapshot>(ownPath('/photon'));
		} catch {
			this.snapshot = EMPTY;
		}
	}

	/** Loads now and on each change for this account; returns the stop. */
	watch(account: string): () => void {
		void this.load();
		return watchLive((event) => event.event.case === 'photon' && event.event.value.accountId === account, () => this.load(), 150);
	}
}
