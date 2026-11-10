import { m } from '#lib/paraglide/messages.js';
import { SvelteMap } from 'svelte/reactivity';
import { toast } from 'svelte-sonner';
import { api } from '#lib/api/client.js';
import { userDialog } from '#lib/people/user-dialog.svelte.js';
import { vrchatAuth, ownPath } from '#lib/api/vrchat-auth.svelte.js';
import { toastError } from '#lib/helpers/feedback.js';

// The original's group member moderation state (groupStore.groupMemberModeration with
// useGroupModerationData, useGroupModerationSelection and useGroupBatchOperations). It lives outside
// the component, as the original's store does, so going to a user from a table and back along the
// crumbs finds the tables and the selection as they were.

export type ModUser = {
	id: string;
	displayName: string;
	thumbnailUrl?: string | null;
	iconUrl?: string | null;
	userIcon?: string | null;
	profilePicOverride?: string | null;
	currentAvatarThumbnailImageUrl?: string | null;
	currentAvatarImageUrl?: string | null;
};

export type ModMember = {
	id?: string;
	groupId?: string;
	userId: string;
	roleIds?: string[];
	mRoleIds?: string[];
	membershipStatus?: string | null;
	visibility?: string | null;
	managerNotes?: string | null;
	joinedAt?: string | null;
	bannedAt?: string | null;
	createdAt?: string | null;
	user?: ModUser | null;
};

export type ModRole = { id: string; name: string; defaultRole?: boolean };

export type ModGroup = {
	id: string;
	name: string;
	memberCount?: number;
	roles?: ModRole[] | null;
	myMember?: { permissions?: string[] } | null;
};

export type AuditLog = {
	id?: string | null;
	created_at?: string | null;
	groupId?: string | null;
	actorId?: string | null;
	actorDisplayName?: string | null;
	targetId?: string | null;
	eventType?: string | null;
	description?: string | null;
	data?: unknown;
};

export type MemberSort = { name: string; value: string };
export type MemberFilter = { name: string; id: string | null };

export const SORTING_OPTIONS: MemberSort[] = [
	{ get name() { return m.dialog_group_members_sorting_joined_at_desc(); }, value: 'joinedAt:desc' },
	{ get name() { return m.dialog_group_members_sorting_joined_at_asc(); }, value: 'joinedAt:asc' }
];
export const FILTER_EVERYONE: MemberFilter = { get name() { return m.dialog_group_tags_everyone(); }, id: null };
export const FILTER_OPTIONS: MemberFilter[] = [FILTER_EVERYONE, { get name() { return m.dialog_group_members_filters_users_with_no_role(); }, id: '' }];

const MODERATION_PERMISSIONS = [
	'group-invites-manage',
	'group-moderates-manage',
	'group-audit-view',
	'group-bans-manage',
	'group-data-manage',
	'group-members-manage',
	'group-members-remove',
	'group-roles-assign',
	'group-roles-manage',
	'group-default-role-manage',
	'group-announcement-manage',
	'group-calendar-manage'
];

/** The original's hasGroupPermission over a permission list (a group's myMember.permissions). */
export function hasPermission(permissions: readonly string[] | null | undefined, permission: string): boolean {
	return !!permissions && (permissions.includes('*') || permissions.includes(permission));
}

/** The original's hasGroupModerationPermission: any of the permissions the tool has a use for. */
export function hasModerationPermission(permissions: readonly string[] | null | undefined): boolean {
	return MODERATION_PERMISSIONS.some((p) => hasPermission(permissions, p));
}

/** 'group.member.ban' to 'Member Ban'. */
export function auditLogTypeName(type: string): string {
	if (!type) return '';
	return type
		.replace('group.', '')
		.replace(/\./g, ' ')
		.replace(/\b\w/g, (l) => l.toUpperCase());
}

export function resolveRoleNames(roleIds: string[] | null | undefined, roles: ModRole[] | null | undefined): string {
	const names: string[] = [];
	for (const id of roleIds ?? []) {
		const role = (roles ?? []).find((r) => r?.id === id);
		if (role?.name) names.push(role.name);
	}
	return names.join(', ');
}

/** The original's userImage: the icon, the thumbnail, then the avatar picture. */
export function userImage(user: ModUser | null | undefined): string {
	if (!user) return '';
	return user.iconUrl || user.userIcon || user.thumbnailUrl || user.currentAvatarThumbnailImageUrl || user.currentAvatarImageUrl || '';
}

/** The original's userImageFull. */
export function userImageFull(user: ModUser | null | undefined): string {
	if (!user) return '';
	return user.iconUrl || user.userIcon || user.profilePicOverride || user.currentAvatarImageUrl || '';
}

/** The original's formatCsvField. */
export function csvField(value: unknown): string {
	if (value === null || value === undefined) return '';
	const text = String(value);
	// eslint-disable-next-line no-control-regex
	return /[\x00-\x1f,"]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text;
}


/** Names that contain the search but VRChat's lagging search index left out, as the original merges them. */
function mergeSearchResults(results: ModMember[], members: ModMember[], search: string): ModMember[] {
	const merged = [...results];
	const seen = new Set(merged.map((item) => item.userId));
	const query = search.toLowerCase();
	for (const member of members) {
		if (!seen.has(member.userId) && (member.user?.displayName ?? '').toLowerCase().includes(query)) {
			merged.push(member);
			seen.add(member.userId);
		}
	}
	return merged;
}

const USER_ID = /usr_[0-9A-Fa-f]{8}-([0-9A-Fa-f]{4}-){3}[0-9A-Fa-f]{12}/g;

const sleep = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

type BatchOptions = {
	action: (user: ModMember, groupId: string) => Promise<unknown>;
	successMessage: string;
	errorMessage: string;
	skipSelf?: boolean;
	onComplete?: () => void;
};

class GroupModeration {
	id = $state('');
	groupRef = $state<ModGroup>({ id: '', name: '' });
	auditLogTypes = $state<string[]>([]);
	openWithUserId = '';
	/** The component is showing; the paging loops stop when it goes. */
	visible = $state(false);
	activeTab = $state('members');
	activeInviteTab = $state('sent');

	selected = new SvelteMap<string, ModMember>();
	members = $state<ModMember[]>([]);
	bans = $state<ModMember[]>([]);
	invites = $state<ModMember[]>([]);
	joinRequests = $state<ModMember[]>([]);
	blocked = $state<ModMember[]>([]);
	logs = $state<AuditLog[]>([]);
	bansSearch = $state('');
	logsSearch = $state('');
	selectedAuditLogTypes = $state<string[]>([]);

	loading = $state(false);
	memberFilter = $state<MemberFilter>(FILTER_EVERYONE);
	memberSortOrder = $state<MemberSort>(SORTING_OPTIONS[0]);
	memberSearch = $state('');
	progressCurrent = $state(0);
	progressTotal = $state(0);

	#loaded: ModMember[] = [];
	#done = false;
	#params = { n: 100, offset: 0, groupId: '', sort: 'joinedAt:desc', roleId: '' as string | undefined };
	#searchRequest = 0;
	#searchTimer: ReturnType<typeof setTimeout> | undefined;

	get permissions(): string[] {
		return this.groupRef.myMember?.permissions ?? [];
	}

	can(permission: string): boolean {
		return hasPermission(this.permissions, permission);
	}

	get selectedUsers(): ModMember[] {
		return [...this.selected.values()];
	}

	#path(rest: string): string {
		return ownPath(`/groups/${encodeURIComponent(this.id)}${rest}`);
	}

	/** The original's clearGroupMemberModerationDialog, plus what a different group must not keep. */
	reset() {
		this.members = [];
		this.bans = [];
		this.invites = [];
		this.joinRequests = [];
		this.blocked = [];
		this.logs = [];
		this.#loaded = [];
		this.#done = false;
		this.memberSearch = '';
	}

	/** Loads the group and, with the audit view permission, its log types. */
	async loadGroup() {
		const groupId = this.id;
		this.groupRef = { id: groupId, name: '' };
		this.auditLogTypes = [];
		try {
			const details = await api.get<{ group: ModGroup }>(`${this.#path('')}/details`);
			if (groupId !== this.id) return;
			this.groupRef = details.group;
			userDialog.setLabel(groupId, details.group.name || groupId);
			if (this.can('group-audit-view')) {
				const types = await api.get<string[]>(this.#path('/audit-log-types'));
				if (groupId === this.id) this.auditLogTypes = types;
			}
		} catch (err) {
			toastError(err);
		}
	}

	// ── Members ──────────────────────────────────────────────────

	async getGroupMembers() {
		this.#loaded = [];
		this.#done = false;
		this.#params = {
			sort: this.memberSortOrder.value || 'joinedAt:desc',
			roleId: this.memberFilter.id !== null ? this.memberFilter.id : '',
			n: 100,
			offset: 0,
			groupId: this.id
		};
		const me = vrchatAuth.ownerUserId;
		const self = await api.get<ModMember | undefined>(this.#path(`/members/${encodeURIComponent(me)}`));
		if (self && this.memberFilter.id === null) this.#loaded.push(self);
		await this.loadMoreGroupMembers();
	}

	async loadMoreGroupMembers(preserveSearch = false) {
		if (this.#done || this.loading) return;
		const params = this.#params;
		if (params.roleId === '') params.roleId = undefined;
		if (!preserveSearch) this.memberSearch = '';
		this.loading = true;
		let page: ModMember[];
		try {
			page = await api.get<ModMember[]>(this.#path('/members'), { n: params.n, offset: params.offset, sort: params.sort, roleId: params.roleId });
		} catch (err) {
			this.#done = true;
			throw err;
		} finally {
			this.loading = false;
		}
		if (params.groupId !== this.id) return;
		const me = vrchatAuth.ownerUserId;
		if (page.some((item) => item.userId === me) && this.#loaded[0]?.userId === me) this.#loaded.splice(0, 1);
		if (page.length < params.n) this.#done = true;
		this.#loaded = [...this.#loaded, ...page];
		if (!preserveSearch) this.members = [...this.#loaded];
		params.offset += params.n;
	}

	async loadAllGroupMembers() {
		if (this.loading) return;
		try {
			await this.getGroupMembers();
			while (this.visible && !this.#done) {
				this.loading = true;
				await sleep(1000);
				this.loading = false;
				await this.loadMoreGroupMembers();
			}
		} catch (err) {
			toastError(err);
		}
	}

	async setSortOrder(sort: MemberSort) {
		if (this.memberSortOrder.value === sort.value) return;
		this.memberSortOrder = sort;
		await this.getGroupMembers().catch(toastError);
	}

	async setFilter(filter: MemberFilter) {
		if (this.memberFilter.id === filter.id && this.memberFilter.name === filter.name) return;
		this.memberFilter = filter;
		await this.getGroupMembers().catch(toastError);
	}

	/** Typing in the members search: under three characters shows nothing, otherwise searches after 200 ms. */
	search() {
		this.#searchRequest += 1;
		clearTimeout(this.#searchTimer);
		if (this.memberSearch.length < 3) {
			this.members = [];
			this.loading = false;
			return;
		}
		this.#searchTimer = setTimeout(() => this.#searchNow(), 200);
	}

	async #searchNow() {
		const groupId = this.id;
		const search = this.memberSearch;
		const requestId = this.#searchRequest;
		this.members = [];
		if (!search || search.length < 3) {
			this.loading = false;
			return;
		}
		this.loading = true;
		try {
			const found = await api.get<{ results: ModMember[] }>(this.#path('/members/search'), { query: search, n: 100, offset: 0 });
			if (requestId !== this.#searchRequest || groupId !== this.id || search !== this.memberSearch) return;
			this.members = mergeSearchResults(found.results, this.#loaded, search);
			await this.#searchLoadRemaining(requestId, search);
		} catch {
			// The original leaves search errors to its request layer.
		} finally {
			if (requestId === this.#searchRequest) this.loading = false;
		}
	}

	// Nothing matched yet: keep loading member pages (up to ten) and match them locally.
	async #searchLoadRemaining(requestId: number, search: string) {
		const groupId = this.id;
		for (let page = 0; page < 10; page++) {
			const stale = () => !this.visible || requestId !== this.#searchRequest || search !== this.memberSearch || this.#done;
			if (this.members.length || this.#params.groupId !== groupId || stale()) return;
			this.loading = true;
			await sleep(1000);
			this.loading = false;
			if (stale()) return;
			try {
				await this.loadMoreGroupMembers(true);
			} catch {
				return;
			}
			if (requestId !== this.#searchRequest || search !== this.memberSearch) return;
			this.members = mergeSearchResults(this.members, this.#loaded, search);
		}
	}

	// ── Bans, invites, join requests, blocked, logs ──────────────

	async #loadPages(groupId: string, path: string, query: Record<string, string | number | boolean>, failure: string): Promise<ModMember[] | undefined> {
		const fetched: ModMember[] = [];
		let offset = 0;
		this.loading = true;
		try {
			// 50 pages of 100, 5000 at most, as the original.
			for (let i = 0; i < 50; i++) {
				const page = await api.get<ModMember[]>(ownPath(`/groups/${encodeURIComponent(groupId)}${path}`), { ...query, n: 100, offset });
				if (groupId !== this.id) return undefined;
				fetched.push(...page);
				offset += 100;
				if (page.length < 100 || !this.visible) break;
			}
			return fetched;
		} catch {
			toast.error(failure);
			return undefined;
		} finally {
			this.loading = false;
		}
	}

	async getAllGroupBans(groupId = this.id) {
		this.bans = [];
		const found = await this.#loadPages(groupId, '/bans', {}, 'Failed to get group bans');
		if (found) this.bans = found;
	}

	async getAllGroupInvitesAndJoinRequests(groupId = this.id) {
		this.invites = [];
		this.joinRequests = [];
		this.blocked = [];
		await Promise.all([
			this.#loadPages(groupId, '/invites', {}, 'Failed to get group invites').then((found) => found && (this.invites = found)),
			this.#loadPages(groupId, '/requests', { blocked: false }, 'Failed to get group join requests').then((found) => found && (this.joinRequests = found)),
			this.#loadPages(groupId, '/requests', { blocked: true }, 'Failed to get group join requests').then((found) => found && (this.blocked = found))
		]);
	}

	async getAllGroupLogs(groupId = this.id, eventTypes: string[] = []) {
		this.logs = [];
		const fetched: AuditLog[] = [];
		let offset = 0;
		this.loading = true;
		try {
			for (let i = 0; i < 50; i++) {
				const page = await api.get<{ results: AuditLog[]; hasNext: boolean }>(ownPath(`/groups/${encodeURIComponent(groupId)}/audit-logs`), {
					n: 100,
					offset,
					eventTypes: eventTypes.length ? eventTypes.join(',') : undefined
				});
				if (groupId !== this.id) return;
				for (const entry of page.results) {
					if (!fetched.some((e) => e.id === entry.id)) fetched.push(entry);
				}
				offset += 100;
				if (!page.hasNext || !this.visible) break;
			}
			this.logs = fetched;
		} catch {
			toast.error('Failed to get group logs');
		} finally {
			this.loading = false;
		}
	}

	// ── Selection ────────────────────────────────────────────────

	toggle(row: ModMember, checked: boolean) {
		if (checked && !this.selected.has(row.userId)) this.selected.set(row.userId, row);
		else if (!checked) this.selected.delete(row.userId);
	}

	selectAll(rows: ModMember[]) {
		for (const row of rows) this.selected.set(row.userId, row);
	}

	clearSelection() {
		this.selected.clear();
	}

	async addToSelection(userId: string) {
		let member = await api.get<ModMember | undefined>(this.#path(`/members/${encodeURIComponent(userId)}`));
		if (member?.user) {
			this.selected.set(member.userId, member);
			return;
		}
		const account = vrchatAuth.ownerUserId;
		const path = `/1/users/${encodeURIComponent(userId)}`;
		const user = await api.get<ModUser>(path, { account }).catch(() => api.get<ModUser>(path, { account, refresh: true }));
		member = { ...(member ?? {}), userId: user.id, user };
		this.selected.set(member.userId, member);
	}

	/** Every usr_ id in the text, or the text itself when there are none. */
	async selectUserIds(input: string) {
		if (!input) return;
		const ids = new Set(input.match(USER_ID) ?? []);
		if (ids.size === 0) ids.add(input);
		await Promise.allSettled([...ids].map((id) => this.addToSelection(id)));
	}

	// ── Bulk actions ─────────────────────────────────────────────

	async #runBatch({ action, successMessage, errorMessage, skipSelf = true, onComplete }: BatchOptions) {
		const users = this.selectedUsers;
		const count = users.length;
		const groupId = this.id;
		const me = vrchatAuth.ownerUserId;
		this.progressTotal = count;
		let allSuccess = true;
		for (let i = 0; i < count; i++) {
			if (!this.progressTotal) break;
			const user = users[i];
			this.progressCurrent = i + 1;
			if (skipSelf && user.userId === me) continue;
			try {
				await action(user, groupId);
			} catch (err) {
				console.error(err);
				toast.error(`${errorMessage}: ${err}`);
				allSuccess = false;
			}
		}
		if (allSuccess) toast.success(successMessage.replace('{count}', String(count)));
		this.progressCurrent = 0;
		this.progressTotal = 0;
		this.selected.clear();
		onComplete?.();
	}

	cancelProgress() {
		this.progressTotal = 0;
	}

	#userPath(groupId: string, userId: string, rest = '') {
		return ownPath(`/groups/${encodeURIComponent(groupId)}${rest}/${encodeURIComponent(userId)}`);
	}

	ban() {
		return this.#runBatch({
			action: (user, groupId) => api.post(this.#userPath(groupId, user.userId, '/bans')),
			successMessage: 'Banned {count} group members',
			errorMessage: 'Failed to ban group member',
			onComplete: () => this.getAllGroupBans()
		});
	}

	unban() {
		return this.#runBatch({
			action: (user, groupId) => api.delete(this.#userPath(groupId, user.userId, '/bans')),
			successMessage: 'Unbanned {count} group members',
			errorMessage: 'Failed to unban group member',
			onComplete: () => this.getAllGroupBans()
		});
	}

	kick() {
		return this.#runBatch({
			action: (user, groupId) => api.delete(this.#userPath(groupId, user.userId, '/members')),
			successMessage: 'Kicked {count} group members',
			errorMessage: 'Failed to kick group member',
			onComplete: () => this.loadAllGroupMembers()
		});
	}

	saveNote(note: string) {
		return this.#runBatch({
			action: async (user, groupId) => {
				if (user.managerNotes === note) return;
				await api.put(this.#userPath(groupId, user.userId, '/members'), { managerNotes: note });
			},
			successMessage: 'Saved notes for {count} group members',
			errorMessage: 'Failed to set group member note for',
			skipSelf: false
		});
	}

	addRoles(roleIds: string[]) {
		const wanted = new Set(roleIds);
		return this.#runBatch({
			action: async (user, groupId) => {
				const current = new Set(user.roleIds ?? []);
				for (const roleId of [...wanted].filter((id) => !current.has(id))) {
					await api.put(`${this.#userPath(groupId, user.userId, '/members')}/roles/${encodeURIComponent(roleId)}`);
				}
			},
			successMessage: 'Added group member roles',
			errorMessage: 'Failed to add group member roles'
		});
	}

	removeRoles(roleIds: string[]) {
		const unwanted = new Set(roleIds);
		return this.#runBatch({
			action: async (user, groupId) => {
				const current = new Set(user.roleIds ?? []);
				for (const roleId of [...unwanted].filter((id) => current.has(id))) {
					await api.delete(`${this.#userPath(groupId, user.userId, '/members')}/roles/${encodeURIComponent(roleId)}`);
				}
			},
			successMessage: 'Roles removed',
			errorMessage: 'Failed to remove group member roles'
		});
	}

	deleteSentInvites() {
		return this.#runBatch({
			action: (user, groupId) => api.delete(this.#userPath(groupId, user.userId, '/invites')),
			successMessage: 'Deleted {count} group invites',
			errorMessage: 'Failed to delete group invites',
			onComplete: () => this.getAllGroupInvitesAndJoinRequests()
		});
	}

	#answer(action: 'accept' | 'reject', block: boolean, successMessage: string, errorMessage: string) {
		return this.#runBatch({
			action: (user, groupId) => api.put(this.#userPath(groupId, user.userId, '/requests'), block ? { action, block: true } : { action }),
			successMessage,
			errorMessage,
			onComplete: () => this.getAllGroupInvitesAndJoinRequests()
		});
	}

	acceptJoinRequests() {
		return this.#answer('accept', false, 'Accepted {count} group join requests', 'Failed to accept group join requests');
	}

	rejectJoinRequests() {
		return this.#answer('reject', false, 'Rejected {count} group join requests', 'Failed to reject group join requests');
	}

	blockJoinRequests() {
		return this.#answer('reject', true, 'Blocked {count} group join requests', 'Failed to block group join requests');
	}

	deleteBlockedRequests() {
		return this.#runBatch({
			action: (user, groupId) => api.delete(this.#userPath(groupId, user.userId, '/members')),
			successMessage: 'Deleted {count} blocked group requests',
			errorMessage: 'Failed to delete blocked group requests',
			onComplete: () => this.getAllGroupInvitesAndJoinRequests()
		});
	}
}

export const groupModeration = new GroupModeration();

/**
 * The original's showGroupMemberModerationDialog: opens the tool in the main pop-up, starting over
 * unless the pop-up already had this group's tool, and selects the user it was opened for.
 */
export function showGroupMemberModeration(groupId: string, userId = '') {
	if (!groupId) return;
	// The original keeps the selection across groups; a selection made for another group is dropped
	// here so a bulk action can't reach users picked for a different group.
	if (groupModeration.id !== groupId) groupModeration.clearSelection();
	if (!userDialog.isOpen || groupModeration.id !== groupId) groupModeration.reset();
	groupModeration.id = groupId;
	groupModeration.openWithUserId = userId;
	userDialog.openGroupModeration(groupId);
}
