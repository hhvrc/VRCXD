import { accountPath, api, ApiError } from '#lib/api/client.js';
import { avatarHref, groupHref, userHref, worldHref } from '#lib/api/links.js';
import { ownPath, vrchatAuth } from '#lib/api/vrchat-auth.svelte.js';
import type { DetailActions } from '#lib/helpers/detail-actions.svelte.js';
import { toastError } from '#lib/helpers/feedback.js';
import { replaceBioSymbols } from '#lib/helpers/format.js';
import { m } from '#lib/paraglide/messages.js';
import { readSetting, shiftKey } from '#lib/settings/table-settings.svelte.js';
import { translation } from '#lib/settings/translation.svelte.js';
import { toast } from 'svelte-sonner';

export type RepresentedGroup = { groupId: string; name: string; iconUrl?: string; bannerUrl?: string; ownerId?: string; memberCount: number; isRepresenting: boolean; mutualGroup?: boolean | null; memberVisibility?: string | null; privacy?: string | null };
export type TabRow = { id: string; name: string; image?: string | null; iconFrame?: string | null; detail?: string; href?: string; sort?: Record<string, string | number | undefined>; group?: string; groupInfo?: RepresentedGroup };
export type TabState = { rows: TabRow[]; loading: boolean; error?: string };
export type FavoriteWorlds = { name: string; visibility: string; worlds: { id: string; name: string; thumbnailImageUrl?: string; occupants: number }[] };
type AvatarRow = { id: string; name: string; thumbnailImageUrl?: string; imageUrl?: string; releaseStatus?: string; authorId?: string; updatedAt?: string; createdAt?: string; updated_at?: string; created_at?: string };

const HIDDEN_USER = 'usr_00000000-0000-0000-0000-000000000000';
const REMOTE_PROVIDERS = ['https://api.avtrdb.com/v3/avatar/search/vrcx'];

/** The original's isSoleGroupOwner: leaving a group you alone are in deletes it. */
export const soleOwner = (g?: RepresentedGroup) => !!g && g.ownerId === vrchatAuth.ownerUserId && g.memberCount <= 1;

// The user dialog's tab state. It lives as long as the dialog, not the shown user, so the sort and
// group choices carry over to the next user as they did when this sat in user-details.svelte.
export class UserTabs {
	lists = $state<Record<string, TabState>>({});
	tabFilter = $state('');
	represented = $state<RepresentedGroup | undefined>();
	// VRChat's group limits (config constants.GROUPS), fetched once per page; its defaults until then.
	groupLimits = $state({ maxOwned: 5, maxJoined: 100, maxJoinedPlus: 200 });
	#groupLimitsAsked = false;
	ownedGroups = $state(0);
	favorites = $state<{ lists: FavoriteWorlds[]; loading: boolean; error?: string } | undefined>();
	favoriteList = $state('0');
	tabSort = $state<Record<string, string>>({ mutuals: 'alphabetical', groups: 'alphabetical', worlds: 'name', avatars: 'updated' });
	// The original's "Group by:" for your own avatars: all your uploads, public or private ones, or the ones you bought.
	avatarGroup = $state('all');
	worldOrder = $state('descending');

	// The original's bio translation: the button shows the bio in the Target Language, signed with the
	// provider, and shows the original again when pressed a second time. Another user starts over.
	translated = $state<{ userId: string; text: string }>({ userId: '', text: '' });
	translating = $state(false);
	// Bio links whose favicon failed to load show a plain link icon instead (upstream PR 1031).
	brokenFavicons = $state(new Set<string>());

	// The original's groups tab edit mode on your own page (upstream PRs 1241 and 1244): per-group
	// visibility and leave, plus Manage Selected for many at once. Holding Shift leaves without asking
	// (issue 1268). The in-game order arrows need the VRChat registry, which is a desktop action.
	groupEditMode = $state(false);
	groupSelected = $state<string[]>([]);
	groupBulkValue = $state('');

	editGroups = $derived.by(() => {
		const seen = new Set<string>();
		return (this.lists.groups?.rows ?? []).filter((row) => row.groupInfo && !seen.has(row.id) && seen.add(row.id));
	});

	readonly #userId: () => string;
	readonly #friendNumbers: () => Map<string, number>;
	readonly #actions: DetailActions;

	constructor(options: { userId: () => string; activeTab: () => string; friendNumbers: () => Map<string, number>; actions: DetailActions }) {
		this.#userId = options.userId;
		this.#friendNumbers = options.friendNumbers;
		this.#actions = options.actions;
		$effect(() => {
			if (options.activeTab() !== 'groups' || this.#groupLimitsAsked || !vrchatAuth.ownerUserId) return;
			this.#groupLimitsAsked = true;
			api.get<typeof this.groupLimits>(`${ownPath()}/groups/limits`).then(
				(limits) => (this.groupLimits = limits),
				() => {}
			);
		});
	}

	/** Forgets what was loaded for the previous user. */
	reset(): void {
		this.represented = undefined;
		this.lists = {};
		this.favorites = undefined;
		this.tabFilter = '';
	}

	async translateBio(bio: string) {
		if (this.translating) return;
		const text = replaceBioSymbols(bio);
		if (!text) return;
		const userId = this.#userId();
		if (this.translated.userId === userId && this.translated.text) {
			this.translated = { userId, text: '' };
			return;
		}
		this.translating = true;
		const target = userId;
		try {
			const result = await translation.translate(`${text}

Translated by ${translation.providerLabel}`, translation.bioLanguage);
			if (result) this.translated = { userId: target, text: result };
		} finally {
			this.translating = false;
		}
	}

	sortRows(tab: string, rows: TabRow[]): TabRow[] {
		const key = this.tabSort[tab];
		const byName = (a: TabRow, b: TabRow) => a.name.localeCompare(b.name);
		if (key === 'alphabetical' || key === 'name') {
			const sorted = [...rows].sort(byName);
			return tab === 'worlds' && this.worldOrder === 'descending' ? sorted.reverse() : sorted;
		}
		// Largest first, except friend order, which counts up from the first friend.
		const direction = key === 'friendOrder' || (tab === 'worlds' && this.worldOrder === 'ascending') ? 1 : -1;
		return [...rows].sort((a, b) => {
			const x = a.sort?.[key];
			const y = b.sort?.[key];
			if (x === undefined || y === undefined) return x === y ? byName(a, b) : x === undefined ? 1 : -1;
			if (x === y) return byName(a, b);
			return (x < y ? -1 : 1) * direction;
		});
	}

	loadFavorites(reload = false) {
		if (this.favorites && !reload) return;
		const id = this.#userId();
		this.favoriteList = '0';
		this.favorites = { lists: [], loading: true };
		api.get<FavoriteWorlds[]>(`${ownPath()}/users/${encodeURIComponent(id)}/favorite-worlds`)
			.then((lists) => {
				if (id === this.#userId()) this.favorites = { lists, loading: false };
			})
			.catch((err) => {
				if (id === this.#userId()) this.favorites = { lists: [], loading: false, error: ApiError.from(err).message };
			});
	}

	readonly #loaders: Record<string, (base: string) => Promise<TabRow[]>> = {
		mutuals: async (base) =>
			(await api.get<{ id: string; displayName: string; iconUrl?: string; currentAvatarThumbnailImageUrl?: string; iconFrame?: string | null }[]>(`${base}/mutuals`)).map((u) => ({
				id: u.id,
				name: u.displayName,
				image: u.iconUrl || u.currentAvatarThumbnailImageUrl,
				iconFrame: u.iconFrame,
				// VRChat gives friends who hide themselves one shared placeholder id.
				href: u.id === HIDDEN_USER ? undefined : userHref(u.id),
				sort: { friendOrder: this.#friendNumbers().get(u.id) }
			})),
		groups: async (base) => {
			const groups = await api.get<RepresentedGroup[]>(`${base}/groups`);
			this.represented = groups.find((g) => g.isRepresenting);
			this.ownedGroups = groups.filter((g) => g.ownerId === vrchatAuth.ownerUserId).length;
			// The original's getUserGroups split: the user's own groups, the ones shared with you (not on
			// your own page), and the rest. An own group you share shows in both (upstream issue 498).
			const userId = this.#userId();
			const self = userId === vrchatAuth.ownerUserId;
			return groups.flatMap((g) => {
				const row = { id: g.groupId, name: g.name, href: groupHref(g.groupId), image: g.iconUrl, detail: `(${g.memberCount})`, sort: { members: g.memberCount }, groupInfo: g };
				const own = g.ownerId === userId;
				const sections: string[] = [];
				if (own) sections.push('own');
				if (!self && g.mutualGroup) sections.push('mutual');
				if (!own && (self || !g.mutualGroup)) sections.push('other');
				return sections.map((group) => ({ ...row, group }));
			});
		},
		worlds: async (base) =>
			(await api.get<{ id: string; name: string; thumbnailImageUrl?: string; occupants: number; favorites?: number; popularity?: number; updated_at?: string; created_at?: string }[]>(`${base}/worlds`)).map((w) => ({
				id: w.id,
				name: w.name,
				image: w.thumbnailImageUrl,
				detail: `(${w.occupants})`,
				href: worldHref(w.id),
				sort: { updated: w.updated_at, created: w.created_at, favorites: w.favorites, popularity: w.popularity }
			})),
		// The original's Avatars tab: your own uploads and purchases fresh from VRChat; for anyone else
		// the ones seen before plus what the remote avatar databases know of by that author.
		avatars: async (base) => {
			const id = this.#userId();
			const account = ownPath();
			const row = (a: AvatarRow, group?: string): TabRow => ({
				id: a.id,
				name: a.name,
				// Some avatar search providers send only imageUrl (upstream issue 1870).
				image: a.thumbnailImageUrl || a.imageUrl,
				detail: a.releaseStatus,
				href: avatarHref(a.id),
				sort: { updated: a.updatedAt ?? a.updated_at, created: a.createdAt ?? a.created_at },
				group
			});
			if (id === vrchatAuth.ownerUserId) {
				const [own, licensed] = await Promise.all([
					api.get<{ avatar: AvatarRow }[]>(`${account}/avatars`),
					api.get<AvatarRow[]>(`${account}/avatars/licensed`).catch(() => [] as AvatarRow[])
				]);
				return [...own.map((o) => row(o.avatar, o.avatar.releaseStatus)), ...licensed.map((a) => row(a, 'licensed'))];
			}
			const stored = await api.get<AvatarRow[]>(`${base}/avatars`);
			const seen = new Set(stored.map((a) => a.id));
			const rows = stored.map((a) => row(a));
			if (readSetting<boolean>('VRCX_avatarRemoteDatabase', true)) {
				for (const provider of readSetting<string[]>('VRCX_avatarRemoteDatabaseProviderList', REMOTE_PROVIDERS)) {
					// Not every provider can look up by author, so a failure only leaves its avatars out, as in the original.
					const found = await api.get<AvatarRow[]>(`${account}/search/avatars`, { provider, authorId: id }).catch(() => [] as AvatarRow[]);
					for (const a of found) {
						if (!a.id || seen.has(a.id) || a.authorId !== id) continue;
						seen.add(a.id);
						rows.push(row({ releaseStatus: 'public', ...a }));
					}
				}
			}
			return rows;
		}
	};

	loadTab(tab: string, reload = false) {
		const loader = this.#loaders[tab];
		if (!loader || (this.lists[tab] && !reload)) return;
		const id = this.#userId();
		this.lists[tab] = { rows: [], loading: true };
		loader(`${ownPath()}/users/${encodeURIComponent(id)}`)
			.then((rows) => {
				if (id === this.#userId()) this.lists[tab] = { rows, loading: false };
			})
			.catch((err) => {
				if (id === this.#userId()) this.lists[tab] = { rows: [], loading: false, error: ApiError.from(err).message };
			});
	}

	#patchGroup(groupId: string, change: (g: RepresentedGroup) => RepresentedGroup | undefined) {
		const state = this.lists.groups;
		if (!state) return;
		state.rows = state.rows.flatMap((row) => {
			if (row.id !== groupId || !row.groupInfo) return [row];
			const next = change(row.groupInfo);
			return next ? [{ ...row, groupInfo: next }] : [];
		});
	}

	async setGroupVisibility(groupId: string, visibility: string) {
		const account = vrchatAuth.ownerUserId;
		try {
			await api.put(accountPath(account, `/groups/${encodeURIComponent(groupId)}/members/${encodeURIComponent(account)}`), { visibility });
			this.#patchGroup(groupId, (g) => ({ ...g, memberVisibility: visibility }));
			toast.success(m.message_group_visibility_updated());
		} catch (err) {
			toastError(err);
		}
	}

	async leaveGroup(groupId: string) {
		const account = vrchatAuth.ownerUserId;
		const g = this.editGroups.find((row) => row.id === groupId)?.groupInfo;
		const path = accountPath(account, `/groups/${encodeURIComponent(groupId)}`);
		try {
			if (soleOwner(g)) await api.delete(path);
			else await api.post(`${path}/leave`, {});
			this.#patchGroup(groupId, () => undefined);
			this.groupSelected = this.groupSelected.filter((id) => id !== groupId);
		} catch (err) {
			toastError(err);
		}
	}

	leaveGroupPrompt(groupId: string) {
		if (shiftKey.held) return void this.leaveGroup(groupId);
		const g = this.editGroups.find((row) => row.id === groupId)?.groupInfo;
		this.#actions.confirming = {
			get title() { return m.dialog_user_note_memo_confirm(); },
			description: soleOwner(g) ? `Continue? Delete Group ${g?.name}` : 'Are you sure you want to leave this group?',
			destructive: true,
			run: () => this.leaveGroup(groupId)
		};
	}

	async bulkGroupAction(value: string) {
		this.groupBulkValue = value;
		const ids = [...this.groupSelected];
		queueMicrotask(() => (this.groupBulkValue = ''));
		if (!ids.length) return;
		if (value.startsWith('visibility:')) {
			for (const id of ids) await this.setGroupVisibility(id, value.slice('visibility:'.length));
			return;
		}
		if (value !== 'leave') return;
		const run = async () => {
			for (const id of ids) await this.leaveGroup(id);
		};
		if (shiftKey.held) return void run();
		this.#actions.confirming = { get title() { return m.dialog_user_note_memo_confirm(); }, description: `Leave ${ids.length} selected group${ids.length === 1 ? '' : 's'}?`, destructive: true, run };
	}

	selectAllGroups() {
		this.groupSelected = this.groupSelected.length === this.editGroups.length ? [] : this.editGroups.map((row) => row.id);
	}

	exitGroupEdit() {
		this.groupEditMode = false;
		this.groupSelected = [];
	}
}
