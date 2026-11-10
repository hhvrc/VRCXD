import { m } from '#lib/paraglide/messages.js';
import { toast } from 'svelte-sonner';
import { accountPath, api } from '#lib/api/client.js';
import type { CalendarEvent } from '#lib/nav/tools.js';

// What the group pop-up and its dialogs share: the original's group shapes (VRChat's, as the server
// passes them on), hasGroupPermission and friends from shared/utils/group.js, and a revision the
// calendars watch, as the original's groupEventRevision.

export type GroupRole = {
	id: string;
	groupId?: string;
	name: string;
	description?: string;
	isSelfAssignable?: boolean;
	isManagementRole?: boolean;
	isAddedOnJoin?: boolean;
	defaultRole?: boolean;
	requiresTwoFactor?: boolean;
	requiresPurchase?: boolean;
	permissions?: string[];
	order: number;
	createdAt?: string;
	updatedAt?: string;
};

export type GroupMyMember = {
	id?: string;
	userId?: string;
	permissions?: string[];
	roleIds?: string[];
	visibility?: string;
	isRepresenting?: boolean;
	isSubscribedToAnnouncements?: boolean;
	isSubscribedToEventAnnouncements?: boolean | null;
	joinedAt?: string;
	bannedAt?: string;
	membershipStatus?: string;
};

export type GroupGallery = {
	id: string;
	name: string;
	description?: string;
	membersOnly?: boolean;
	roleIdsToView?: string[] | null;
	roleIdsToSubmit?: string[] | null;
	roleIdsToAutoApprove?: string[] | null;
	roleIdsToManage?: string[] | null;
};

export type Group = {
	id: string;
	name: string;
	shortCode?: string;
	discriminator?: string;
	description?: string;
	rules?: string;
	iconUrl?: string;
	iconId?: string;
	bannerUrl?: string;
	bannerId?: string;
	ownerId?: string;
	privacy?: string;
	joinState?: string;
	links?: string[];
	languages?: string[];
	memberCount?: number;
	onlineMemberCount?: number;
	isVerified?: boolean;
	membershipStatus?: string;
	myMember?: GroupMyMember | null;
	roles?: GroupRole[] | null;
	galleries?: GroupGallery[] | null;
	storeId?: string | null;
	allowGroupJoinPrompt?: boolean | null;
	createdAt?: string;
};

export type GroupPost = {
	id: string;
	groupId: string;
	title: string;
	text: string;
	imageId?: string | null;
	imageUrl?: string | null;
	authorId?: string;
	editorId?: string | null;
	visibility?: string;
	roleIds?: string[];
	createdAt?: string;
	updatedAt?: string;
};

export type GroupMemberUser = {
	id: string;
	displayName: string;
	thumbnailUrl?: string | null;
	iconUrl?: string | null;
	profilePicOverride?: string | null;
	currentAvatarThumbnailImageUrl?: string | null;
	currentAvatarImageUrl?: string | null;
	imageUrl?: string | null;
	iconFrame?: string | null;
};

export type GroupMember = {
	id: string;
	groupId?: string;
	userId: string;
	roleIds?: string[];
	visibility?: string | null;
	isRepresenting?: boolean;
	isSubscribedToAnnouncements?: boolean;
	managerNotes?: string | null;
	joinedAt?: string;
	user?: GroupMemberUser | null;
};

/** A calendar event with the fields the event editor reads. */
export type GroupEvent = CalendarEvent & {
	tags?: string[];
	imageId?: string | null;
	roleIds?: string[] | null;
	platforms?: string[];
	languages?: string[];
	hostEarlyJoinMinutes?: number;
	guestEarlyJoinMinutes?: number;
	recurrence?: Recurrence | null;
};

export type Recurrence = {
	frequency?: string;
	interval: number;
	daysOfWeek?: string[];
	end?: { type: string; count?: number; date?: string } | null;
};

/** The original's hasGroupPermission: the account holds the permission in the group, or all of them. */
export function hasGroupPermission(group: { myMember?: { permissions?: string[] } | null } | null | undefined, permission: string): boolean {
	const held = group?.myMember?.permissions;
	return !!held && (held.includes('*') || held.includes(permission));
}

export function isSoleGroupOwner(group: Group | undefined, userId: string): boolean {
	return group?.ownerId === userId && typeof group.memberCount === 'number' && group.memberCount <= 1;
}

/**
 * The original's mergeGroupMemberSearchResults: VRChat's search index lags behind recent joins, so
 * loaded members whose name matches are added to its results.
 */
export function mergeGroupMemberSearchResults(results: GroupMember[], members: GroupMember[], search: string): GroupMember[] {
	const merged = [...results];
	const seen = new Set(merged.map((item) => item.userId));
	const query = search.toLowerCase();
	for (const member of members) {
		if (seen.has(member.userId)) continue;
		if ((member.user?.displayName ?? '').toLowerCase().includes(query)) {
			merged.push(member);
			seen.add(member.userId);
		}
	}
	return merged;
}

/** The original's useGroupCalendarEvents: one entry per series, upcoming ones and past ones, newest first. */
export function splitCalendar<T extends { startsAt: string; endsAt: string; seriesId?: string | null }>(calendar: T[], now = Date.now()): { upcoming: T[]; past: T[] } {
	const sorted = [...calendar].sort((a, b) => a.startsAt.localeCompare(b.startsAt));
	const pick = (wanted: (end: number) => boolean) => {
		const series = new Set<string>();
		return sorted
			.filter((event) => {
				const end = new Date(event.endsAt).getTime();
				if (event.seriesId) {
					if (series.has(event.seriesId)) return false;
					if (wanted(end)) series.add(event.seriesId);
				}
				return wanted(end);
			})
			.reverse();
	};
	return { upcoming: pick((end) => end >= now), past: pick((end) => end < now) };
}

/** The original's downloadAndSaveJson. */
export function downloadJson(fileName: string, data: unknown): void {
	if (!fileName || !data) return;
	try {
		const link = document.createElement('a');
		link.href = `data:application/json;charset=utf-8,${encodeURIComponent(JSON.stringify(data, null, 2))}`;
		link.download = `${fileName}.json`;
		document.body.appendChild(link);
		link.click();
		link.remove();
	} catch {
		toast.error('Failed to download JSON.');
	}
}

let languages: Promise<Record<string, string>> | undefined;
let languagesFor = '';

/** VRChat's spoken language options (the original's subsetOfLanguages), fetched once per account. */
export function languageOptions(ownerUserId: string): Promise<Record<string, string>> {
	if (!languages || languagesFor !== ownerUserId) {
		languagesFor = ownerUserId;
		languages = api.get<Record<string, string>>(accountPath(ownerUserId, '/languages')).catch(() => {
			languages = undefined;
			return {};
		});
	}
	return languages;
}

/** Bumped after an event is created, edited or deleted, so open calendars fetch again. */
export const groupEvents = $state({ revision: 0 });

/** Bumped after a group is saved somewhere else (Edit Group, roles), so its open pop-up reloads. */
export const groupChanges = $state({ groupId: '', revision: 0 });

export function notifyGroupChanged(groupId: string): void {
	groupChanges.groupId = groupId;
	groupChanges.revision++;
}

export type GroupEditForm = {
	mode: 'create' | 'edit';
	groupId: string;
	name: string;
	shortCode: string;
	description: string;
	joinState: string;
	privacy: string;
	roleTemplate: string;
	languages: string[];
	rules: string;
	links: string[];
	bannerId: string;
	bannerUrl: string;
	iconId: string;
	iconUrl: string;
	allowGroupJoinPrompt: boolean;
};

function blankGroupEdit(): GroupEditForm {
	return {
		mode: 'create',
		groupId: '',
		name: '',
		shortCode: '',
		description: '',
		joinState: 'open',
		privacy: 'default',
		roleTemplate: 'default',
		languages: [],
		rules: '',
		links: [],
		bannerId: '',
		bannerUrl: '',
		iconId: '',
		iconUrl: '',
		allowGroupJoinPrompt: false
	};
}

// The original's groupEditDialog (stores/group.js): one Create/Edit Group dialog for the whole app.
class GroupEditState {
	open = $state(false);
	form = $state<GroupEditForm>(blankGroupEdit());

	/** showCreateGroupDialog. */
	showCreate(): void {
		this.form = blankGroupEdit();
		this.open = true;
	}

	/** showEditGroupDialog. */
	showEdit(group: Group): void {
		this.form = {
			...blankGroupEdit(),
			mode: 'edit',
			groupId: group.id,
			name: group.name,
			shortCode: group.shortCode ?? '',
			description: group.description ?? '',
			joinState: group.joinState ?? 'open',
			privacy: group.privacy ?? 'default',
			languages: [...(group.languages ?? [])],
			rules: group.rules ?? '',
			links: [...(group.links ?? [])],
			bannerId: group.bannerId ?? '',
			bannerUrl: group.bannerUrl ?? '',
			iconId: group.iconId ?? '',
			iconUrl: group.iconUrl ?? '',
			allowGroupJoinPrompt: !!group.allowGroupJoinPrompt
		};
		this.open = true;
	}
}

export const groupEdit = new GroupEditState();

export type GroupEventForm = {
	mode: 'create' | 'edit';
	groupId: string;
	eventId: string;
	group: Group | undefined;
	startsAt: string;
	endsAt: string;
	title: string;
	accessType: string;
	description: string;
	category: string;
	tags: string[];
	imageId: string;
	imageUrl: string;
	roleIds: string[];
	platforms: string[];
	languages: string[];
	sendCreationNotification: boolean;
	hostEarlyJoinMinutes: number;
	guestEarlyJoinMinutes: number;
	closeInstanceAfterEndMinutes: number;
	seriesId: string | null;
	recurrence: Recurrence | null;
};

/** resetGroupEventEditDialog: starts at the next hour, two hours long. */
function blankGroupEvent(): GroupEventForm {
	const startsAt = new Date();
	startsAt.setMinutes(0, 0, 0);
	startsAt.setHours(startsAt.getHours() + 1);
	const endsAt = new Date(startsAt);
	endsAt.setHours(endsAt.getHours() + 2);
	return {
		mode: 'create',
		groupId: '',
		eventId: '',
		group: undefined,
		startsAt: startsAt.toISOString(),
		endsAt: endsAt.toISOString(),
		title: '',
		accessType: 'group',
		description: '',
		category: 'other',
		tags: [],
		imageId: '',
		imageUrl: '',
		roleIds: [],
		platforms: [],
		languages: [],
		sendCreationNotification: false,
		hostEarlyJoinMinutes: 60,
		guestEarlyJoinMinutes: 5,
		closeInstanceAfterEndMinutes: 5,
		seriesId: null,
		recurrence: null
	};
}

// The original's groupEventEditDialog: one Create/Edit Group Event dialog for the whole app, opened
// from the group pop-up and from the Tools calendar.
class GroupEventEditState {
	open = $state(false);
	form = $state<GroupEventForm>(blankGroupEvent());

	/** showCreateGroupEventDialog. */
	showCreate(group: Group): void {
		this.form = { ...blankGroupEvent(), groupId: group.id, group };
		this.open = true;
	}

	/** showEditGroupEventDialog. */
	showEdit(event: GroupEvent, group: Group | undefined): void {
		this.form = {
			...blankGroupEvent(),
			mode: 'edit',
			groupId: event.ownerId,
			eventId: event.id,
			group,
			startsAt: event.startsAt,
			endsAt: event.endsAt,
			title: event.title ?? '',
			accessType: event.accessType ?? 'group',
			description: event.description ?? '',
			category: event.category ?? 'other',
			tags: [...(event.tags ?? [])],
			imageId: event.imageId ?? '',
			imageUrl: event.imageUrl ?? '',
			roleIds: [...(event.roleIds ?? [])],
			platforms: [...(event.platforms ?? [])],
			languages: [...(event.languages ?? [])],
			hostEarlyJoinMinutes: event.hostEarlyJoinMinutes ?? 60,
			guestEarlyJoinMinutes: event.guestEarlyJoinMinutes ?? 5,
			closeInstanceAfterEndMinutes: event.closeInstanceAfterEndMinutes ?? 5,
			seriesId: event.seriesId,
			recurrence: event.recurrence ? structuredClone($state.snapshot(event.recurrence)) : null
		};
		this.open = true;
	}

	/**
	 * GroupCalendarEventCard's editEvent and Edit Parent Event: fetches the group when not given, and the
	 * event (the calendar's copy can lack the fields the form needs).
	 */
	async editById(ownerUserId: string, groupId: string, eventId: string, group?: Group): Promise<void> {
		try {
			const path = accountPath(ownerUserId, `/groups/${encodeURIComponent(groupId)}`);
			const [event, owner] = await Promise.all([api.get<GroupEvent>(`${path}/calendar/${encodeURIComponent(eventId)}`), group ? Promise.resolve(group) : api.get<{ group: Group }>(`${path}/details`).then((d) => d.group)]);
			this.showEdit(event, owner);
		} catch {
			toast.error(m.dialog_group_event_edit_load_error());
		}
	}
}

export const groupEventEdit = new GroupEventEditState();
