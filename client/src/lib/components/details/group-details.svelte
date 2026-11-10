<script lang="ts" module>
	import { openExternalLink } from '#lib/nav/external-link.svelte.js';
	// The original keeps the last tab across groups (groupDialog.lastActiveTab).
	let lastActiveTab = 'info';
</script>

<script lang="ts">
	import { toastError } from '#lib/helpers/feedback.js';
	import { RAIL, PANEL, HEADER_CARD, NONE, SECTION, SECTION_ROW, TAB_CONTENT, TAB_LIST, TAB_LIST_WRAP, TAB_TRIGGER } from '#lib/details.js';
	import { m } from '#lib/paraglide/messages.js';
	import CopyId from '#lib/components/common/copy-id.svelte';
	import { onItemChanged } from '#lib/api/live.svelte.js';
	import PreviousInstancesDialog from '#lib/components/instance/previous-instances-dialog.svelte';
	import { untrack } from 'svelte';
	import { Tabs as TabsPrimitive } from 'bits-ui';
	import { toast } from 'svelte-sonner';
	import Bell from '@lucide/svelte/icons/bell';
	import BellOff from '@lucide/svelte/icons/bell-off';
	import Bookmark from '@lucide/svelte/icons/bookmark';
	import BookmarkCheck from '@lucide/svelte/icons/bookmark-check';
	import Check from '@lucide/svelte/icons/check';
	import CheckCircle from '@lucide/svelte/icons/circle-check';
	import Eye from '@lucide/svelte/icons/eye';
	import ImageIcon from '@lucide/svelte/icons/image';
	import Info from '@lucide/svelte/icons/info';
	import MessageCircle from '@lucide/svelte/icons/message-circle';
	import MessageCircleOff from '@lucide/svelte/icons/message-circle-off';
	import MessageSquare from '@lucide/svelte/icons/message-square';
	import MoreHorizontal from '@lucide/svelte/icons/ellipsis';
	import RefreshCw from '@lucide/svelte/icons/refresh-cw';
	import Trash2 from '@lucide/svelte/icons/trash-2';
	import X from '@lucide/svelte/icons/x';
	import XCircle from '@lucide/svelte/icons/circle-x';
	import LineChart from '@lucide/svelte/icons/chart-line';
	import Settings from '@lucide/svelte/icons/settings';
	import Pencil from '@lucide/svelte/icons/pencil';
	import ArrowRightLeft from '@lucide/svelte/icons/arrow-right-left';
	import Ticket from '@lucide/svelte/icons/ticket';
	import CalendarPlus from '@lucide/svelte/icons/calendar-plus';
	import ShieldCheck from '@lucide/svelte/icons/shield-check';
	import { hasModerationPermission, showGroupMemberModeration } from '#lib/groups/group-moderation.svelte.js';
	import ActionDialogs from '#lib/components/dialogs/action-dialogs.svelte';
	import CopyIdRow from '#lib/components/details/copy-id-row.svelte';
	import InfoRow from '#lib/components/details/info-row.svelte';
	import JsonCard from '#lib/components/details/json-card.svelte';
	import SectionCard from '#lib/components/details/section-card.svelte';
	import ShareMenu from '#lib/components/details/share-menu.svelte';
	import DisplayName from '#lib/components/common/display-name.svelte';
	import GroupMembersTab from '#lib/components/group/group-members-tab.svelte';
	import GroupPostEditDialog from '#lib/components/group/group-post-edit-dialog.svelte';
	import GroupTransferDialog from '#lib/components/group/group-transfer-dialog.svelte';
	import GroupPhotosTab, { type GalleryImage } from '#lib/components/group/group-photos-tab.svelte';
	import GroupStoreTab from '#lib/components/group/group-store-tab.svelte';
	import GroupInfoTab, { type Room } from '#lib/components/group/group-info-tab.svelte';
	import GroupPostsTab from '#lib/components/group/group-posts-tab.svelte';
	import InviteGroupDialog from '#lib/components/invite/invite-group-dialog.svelte';
	import ViewableImage from '#lib/components/common/viewable-image.svelte';
	import { Badge } from '#lib/components/ui/badge/index.js';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
	import { Spinner } from '#lib/components/ui/spinner/index.js';
	import * as Tooltip from '#lib/components/ui/tooltip/index.js';
	import { accountPath, api, ApiError } from '#lib/api/client.js';
	import type { Friend } from '#lib/api/types.js';
	import { DetailActions } from '#lib/helpers/detail-actions.svelte.js';
	import { formatDateFilter, getFaviconUrl, replaceBioSymbols, sanitizeEntity, timeAgo } from '#lib/helpers/format.js';
	import { groupChanges, groupEdit, groupEventEdit, groupEvents, hasGroupPermission, isSoleGroupOwner, languageOptions, type Group, type GroupEvent, type GroupMember, type GroupPost } from '#lib/groups/group-dialog.svelte.js';
	import { languageClass } from '#lib/people/user-display.js';
	import { userDialog } from '#lib/people/user-dialog.svelte.js';
	import { vrchatAuth } from '#lib/api/vrchat-auth.svelte.js';

	// The original's group dialog: GroupDialog.vue, its Info, Posts and Members tabs, and
	// useGroupDialogCommands.
	let { groupId }: { groupId: string } = $props();

	const POSTS_PAGE = 100;

	type Details = { group: Group; instances?: { instances?: Room[] } | null; joinCount: number; lastVisit?: string };

	let group = $state<Group | undefined>();
	let rooms = $state<Room[]>([]);
	let friends = $state<Friend[]>([]);
	let joinCount = $state(0);
	let lastVisit = $state<string | undefined>();
	let loadError = $state('');
	let refreshing = $state(false);
	let previousOpen = $state(false);
	let inviteOpen = $state(false);
	let bannerError = $state(false);
	let iconError = $state(false);
	let activeTab = $state(lastActiveTab);
	let posts = $state<GroupPost[] | undefined>();
	let postsError = $state('');
	let postSearch = $state('');
	let calendar = $state<GroupEvent[]>([]);
	let members = $state<GroupMember[]>([]);
	let memberSort = $state('joinedAt:desc');
	let memberFilter = $state<string | null>(null);
	let languageNames = $state<Record<string, string>>({});
	let transferOpen = $state(false);
	let galleryImages = $state<Record<string, GalleryImage[]>>({});
	let postEdit = $state<{ open: boolean; post?: GroupPost }>({ open: false });

	const actions = new DetailActions(() => load(groupId));
	const owner = $derived(vrchatAuth.ownerUserId);
	const base = $derived(`${accountPath(owner)}/groups/${encodeURIComponent(groupId)}`);
	const inGroup = $derived(group?.membershipStatus === 'member');
	const isOwner = $derived(!!group && group.ownerId === owner);
	const isBlocked = $derived(group?.membershipStatus === 'userblocked');
	const myMember = $derived(group?.myMember ?? undefined);
	const memberRoles = $derived((group?.roles ?? []).filter((r) => myMember?.roleIds?.includes(r.id)));
	const languages = $derived((group?.languages ?? []).filter((key) => languageNames[key] !== undefined).map((key) => ({ key, value: languageNames[key] })));
	const VISIBILITY: Record<string, string> = { visible: 'Everyone', friends: 'Friends', hidden: 'Hidden' };
	const JOIN_STATES: Record<string, string> = { open: 'Open', request: 'Request', invite: 'Invite', closed: 'Closed' };

	async function load(id: string) {
		if (!owner || !id) return;
		loadError = '';
		try {
			const path = `${accountPath(owner)}/groups/${encodeURIComponent(id)}`;
			const [details, nextFriends] = await Promise.all([api.get<Details>(`${path}/details`), api.get<Friend[]>(accountPath(owner, '/friends')).catch(() => [])]);
			if (id !== groupId) return;
			// The original's sanitizeEntityJson on a group it applies, and on each of its roles.
			group = sanitizeEntity(details.group, ['name', 'description', 'rules']);
			if (group.roles) group.roles = group.roles.map((role) => sanitizeEntity(role, ['name', 'description']));
			rooms = details.instances?.instances ?? [];
			friends = nextFriends;
			joinCount = details.joinCount;
			lastVisit = details.lastVisit;
			userDialog.setLabel(id, group.name);
			// The original leaves the Store tab when the group has no store.
			if (activeTab === 'store' && !details.group.storeId) selectTab('info');
			loadPosts(id);
			loadCalendar(id);
		} catch (err) {
			if (id === groupId) loadError = ApiError.from(err).message;
		}
	}

	/** getAllGroupPosts: every page, up to 50, newest first; the first is the announcement. */
	async function loadPosts(id: string) {
		postsError = '';
		try {
			const all: GroupPost[] = [];
			let offset = 0;
			let total = Infinity;
			for (let pages = 0; offset < total && pages < 50; pages++) {
				const page = await api.get<{ posts?: GroupPost[]; total?: number }>(`${accountPath(owner)}/groups/${encodeURIComponent(id)}/posts`, { n: POSTS_PAGE, offset });
				const found = page.posts ?? [];
				total = Number(page.total ?? found.length);
				all.push(...found);
				offset += POSTS_PAGE;
				if (found.length === 0) break;
			}
			if (id === groupId) posts = all.map((p) => ({ ...p, title: replaceBioSymbols(p.title), text: replaceBioSymbols(p.text) }));
		} catch (err) {
			if (id === groupId) {
				posts = [];
				postsError = ApiError.from(err).message;
			}
		}
	}

	async function loadCalendar(id: string) {
		try {
			const found = await api.get<GroupEvent[]>(`${accountPath(owner)}/groups/${encodeURIComponent(id)}/calendar`);
			if (id === groupId) calendar = found.map((e) => ({ ...e, title: replaceBioSymbols(e.title), description: replaceBioSymbols(e.description) }));
		} catch {
			if (id === groupId) calendar = [];
		}
	}

	async function refresh() {
		refreshing = true;
		await load(groupId);
		refreshing = false;
	}

	function selectTab(tab: string) {
		activeTab = tab;
		lastActiveTab = tab;
	}

	$effect(() => {
		const id = groupId;
		if (owner && id) {
			group = undefined;
			posts = undefined;
			calendar = [];
			members = [];
			galleryImages = {};
			memberFilter = null;
			actions.reset();
			bannerError = iconError = false;
			load(id);
		}
	});

	$effect(() => {
		if (owner) languageOptions(owner).then((names) => (languageNames = names));
	});

	// A save in Edit Group (or the roles dialog) refreshes this group.
	$effect(() => {
		if (groupChanges.revision && groupChanges.groupId === groupId) untrack(() => load(groupId));
	});

	// groupEventRevision: an event saved or deleted anywhere refreshes the calendar.
	$effect(() => {
		if (groupEvents.revision) loadCalendar(groupId);
	});

	/** setGroupVisibility, setGroupSubscription and setGroupEventAnnouncements. */
	function setMember(change: Record<string, unknown>, done: string) {
		actions.act(() => api.put(`${base}/members/${encodeURIComponent(owner)}`, change), done);
	}

	/** leaveGroupPrompt: the sole owner deletes the group instead, which closes the pop-up. */
	function leave() {
		if (!group) return;
		const deleting = isSoleGroupOwner(group, owner);
		actions.confirming = {
			get title() { return m.confirm_title(); },
			description: deleting ? `Continue? Delete Group ${group.name}` : 'Are you sure you want to leave this group?',
			destructive: true,
			run: () =>
				deleting
					? actions.act(() => api.delete(base), undefined, false).then((ok) => ok && userDialog.close())
					: actions.act(() => api.post(`${base}/leave`))
		};
	}

	function block() {
		actions.confirming = { get title() { return m.confirm_title(); }, get description() { return m.confirm_block_group(); }, destructive: true, run: () => actions.act(() => api.post(`${base}/block`)) };
	}

	function unblock() {
		actions.confirming = { get title() { return m.confirm_title(); }, get description() { return m.confirm_unblock_group(); }, run: () => actions.act(() => api.delete(`${base}/membership`)) };
	}

	async function followEvent(event: GroupEvent) {
		try {
			const updated = await api.post<GroupEvent>(accountPath(owner, `/calendar/${encodeURIComponent(event.ownerId)}/${encodeURIComponent(event.id)}/follow`), { isFollowing: !event.userInterest?.isFollowing });
			calendar = calendar.map((e) => (e.id === updated.id ? { ...e, ...updated } : e));
		} catch (err) {
			toastError(err);
		}
	}

	const canManagePosts = $derived(hasGroupPermission(group, 'group-announcement-manage'));
	const canManageCalendar = $derived(hasGroupPermission(group, 'group-calendar-manage'));

	/** handleGroupPost: an edited post is replaced, a new one goes first. */
	function postSaved(saved: GroupPost) {
		const next = { ...saved, title: replaceBioSymbols(saved.title), text: replaceBioSymbols(saved.text) };
		const list = posts ?? [];
		posts = list.some((p) => p.id === next.id) ? list.map((p) => (p.id === next.id ? { ...p, ...next } : p)) : [next, ...list];
	}

	function editPost(post: GroupPost) {
		postEdit = { open: true, post };
	}

	/** confirmDeleteGroupPost. */
	function deletePost(post: GroupPost) {
		actions.confirming = {
			get title() { return m.confirm_title(); },
			get description() { return m.confirm_delete_post(); },
			destructive: true,
			run: () =>
				actions
					.act(() => api.delete(`${accountPath(owner)}/groups/${encodeURIComponent(post.groupId)}/posts/${encodeURIComponent(post.id)}`), undefined, false)
					.then((ok) => {
						if (ok && post.groupId === groupId) posts = (posts ?? []).filter((p) => p.id !== post.id);
					})
		};
	}

	/** joinGroup: the toast follows the membership VRChat reports back. */
	function joinGroup() {
		actions.act(async () => {
			const joined = await api.post<{ membershipStatus?: string | null }>(`${base}/join`);
			if (joined?.membershipStatus === 'member') toast.success(m.message_group_joined());
			else if (joined?.membershipStatus === 'requested') toast.success(m.message_group_join_request_sent());
		});
	}

	$effect(() => onItemChanged('group', () => groupId, () => load(groupId)));
</script>

{#if loadError && !group}
	<div class="flex flex-col items-start gap-2 p-1.5">
		<p class="text-sm text-destructive">{loadError}</p>
		<CopyId id={groupId} />
		<Button variant="outline" size="sm" disabled={refreshing} onclick={refresh}>{m.dialog_group_actions_refresh()}</Button>
	</div>
{:else if !group}
	<div class="flex h-40 items-center justify-center"><Spinner /></div>
{:else}
	<div class="flex min-h-0 min-w-0 flex-1 flex-row" data-slot="group-details">
		<div class={RAIL}>
			<div class={HEADER_CARD}>
				<div class="relative aspect-17/6">
					{#if group.bannerUrl && !bannerError}
						<ViewableImage src={group.bannerUrl} class="absolute inset-0" imageClass="block size-full object-cover" onerror={() => (bannerError = true)} />
					{/if}
					<div class="absolute bottom-0 left-3 z-10 size-24 translate-y-1/2 overflow-hidden rounded-lg" style="filter: drop-shadow(0 0 1px rgb(0 0 0 / 0.95)) drop-shadow(0 0 4px rgb(0 0 0 / 0.75)) drop-shadow(0 2px 8px rgb(0 0 0 / 0.55));">
						{#if group.iconUrl && !iconError}
							<ViewableImage src={group.iconUrl} class="size-full" imageClass="size-full object-cover" onerror={() => (iconError = true)} />
						{:else}
							<div class="flex size-full items-center justify-center bg-accent text-muted-foreground"><ImageIcon /></div>
						{/if}
					</div>
				</div>
				<div class="mt-10 flex flex-col p-3">
					<div class="flex w-full max-w-full min-w-0 items-start justify-between gap-2">
						<div class="flex min-w-0 flex-1 flex-col">
							<button type="button" class="block min-w-0 cursor-pointer text-left font-bold" title={group.name || groupId} onclick={() => actions.copy(group?.name ?? '')}>
								<span class="flex min-w-0 items-center gap-1">
									{#if isOwner}<span class="shrink-0">👑</span>{/if}
									<span class="truncate">{group.name || groupId}</span>
								</span>
							</button>
							{#if group.ownerId}<DisplayName userId={group.ownerId} class="block min-w-0 truncate font-mono text-xs text-muted-foreground" />{/if}
						</div>
						<div class="ml-2 flex shrink-0 items-center justify-end gap-2">
							{#if inGroup && myMember}
								{#if myMember.isRepresenting}
									<Button class="rounded-lg" variant="secondary" size="icon" title={m.dialog_group_actions_unrepresent_tooltip()} aria-label={m.dialog_group_actions_unrepresent_tooltip()} disabled={actions.busy} onclick={() => actions.act(() => api.put(`${base}/representation`, { isRepresenting: false }))}><BookmarkCheck /></Button>
								{:else}
									<Button class="rounded-lg" variant="outline" size="icon" title={m.dialog_group_actions_represent_tooltip()} aria-label={m.dialog_group_actions_represent_tooltip()} disabled={actions.busy || group.privacy === 'private'} onclick={() => actions.act(() => api.put(`${base}/representation`, { isRepresenting: true }))}><Bookmark /></Button>
								{/if}
							{:else if myMember?.membershipStatus === 'requested'}
								<Button class="rounded-lg" variant="outline" size="icon" title={m.dialog_group_actions_cancel_join_request_tooltip()} aria-label={m.dialog_group_actions_cancel_join_request_tooltip()} disabled={actions.busy} onclick={() => actions.act(() => api.delete(`${base}/request`))}><X /></Button>
							{:else if myMember?.membershipStatus === 'invited'}
								<Button class="rounded-lg" variant="outline" size="icon" title={m.dialog_group_actions_pending_request_tooltip()} aria-label={m.dialog_group_actions_pending_request_tooltip()} disabled={actions.busy} onclick={joinGroup}><Check /></Button>
							{:else if group.joinState === 'request'}
								<Button class="rounded-lg" variant="outline" size="icon" title={m.dialog_group_actions_request_join_tooltip()} aria-label={m.dialog_group_actions_request_join_tooltip()} disabled={actions.busy} onclick={joinGroup}><MessageSquare /></Button>
							{:else if group.joinState === 'invite'}
								<Button class="rounded-lg" variant="outline" size="icon" title={m.dialog_group_actions_invite_required_tooltip()} aria-label={m.dialog_group_actions_invite_required_tooltip()} disabled><MessageSquare /></Button>
							{:else if group.joinState === 'open'}
								<Button class="rounded-lg" variant="outline" size="icon" title={m.dialog_group_actions_join_group_tooltip()} aria-label={m.dialog_group_actions_join_group_tooltip()} disabled={actions.busy} onclick={joinGroup}><Check /></Button>
							{/if}
							<DropdownMenu.Root>
								<DropdownMenu.Trigger>
									{#snippet child({ props })}
										<Button {...props} class="rounded-lg" variant={isBlocked ? 'destructive' : 'outline'} size="icon" aria-label={m.nav_tooltip_manage()}><MoreHorizontal /></Button>
									{/snippet}
								</DropdownMenu.Trigger>
								<DropdownMenu.Content class="w-auto whitespace-nowrap">
									<DropdownMenu.Item disabled={refreshing} onSelect={refresh}><RefreshCw class="size-4" /> {m.dialog_group_actions_refresh()}</DropdownMenu.Item>
									<ShareMenu kind="group" url={`https://vrc.group/${group.shortCode}.${group.discriminator}`} name={group.name} id={groupId} {actions} />
									{#if inGroup && !hasGroupPermission(group, 'group-roles-manage')}
										<DropdownMenu.Item onSelect={() => userDialog.openGroupRoles(groupId)}><ShieldCheck class="size-4" /> {m.dialog_group_actions_view_roles()}</DropdownMenu.Item>
									{/if}
									{#if hasModerationPermission(myMember?.permissions)}
										<DropdownMenu.Separator />
										{#if hasGroupPermission(group, 'group-data-manage')}
											<DropdownMenu.Item onSelect={() => group && groupEdit.showEdit(group)}><Pencil class="size-4" /> {m.dialog_group_edit_edit_header()}</DropdownMenu.Item>
										{/if}
										<DropdownMenu.Item onSelect={() => showGroupMemberModeration(groupId)}><Settings class="size-4" /> {m.dialog_group_actions_moderation_tools()}</DropdownMenu.Item>
										{#if hasGroupPermission(group, 'group-roles-manage')}
											<DropdownMenu.Item onSelect={() => userDialog.openGroupRoles(groupId)}><ShieldCheck class="size-4" /> {m.dialog_group_actions_manage_roles()}</DropdownMenu.Item>
										{/if}
										{#if hasGroupPermission(group, 'group-invites-manage')}
											<DropdownMenu.Item onSelect={() => (inviteOpen = true)}><MessageSquare class="size-4" /> {m.dialog_group_actions_invite_to_group()}</DropdownMenu.Item>
										{/if}
										{#if canManagePosts}
											<DropdownMenu.Item onSelect={() => (postEdit = { open: true })}><Ticket class="size-4" /> {m.dialog_group_actions_create_post()}</DropdownMenu.Item>
										{/if}
										{#if canManageCalendar}
											<DropdownMenu.Item onSelect={() => group && groupEventEdit.showCreate(group)}><CalendarPlus class="size-4" /> {m.dialog_group_actions_create_event()}</DropdownMenu.Item>
										{/if}
										{#if isOwner}
											<DropdownMenu.Item onSelect={() => (transferOpen = true)}><ArrowRightLeft class="size-4" /> {m.dialog_group_transfer_header()}</DropdownMenu.Item>
										{/if}
									{/if}
									{#if inGroup}
										{#if myMember}
											<DropdownMenu.Separator />
											{#if myMember.isSubscribedToAnnouncements}
												<DropdownMenu.Item onSelect={() => setMember({ isSubscribedToAnnouncements: false }, 'Group subscription updated')}><BellOff class="size-4" /> {m.dialog_group_actions_unsubscribe()}</DropdownMenu.Item>
											{:else}
												<DropdownMenu.Item onSelect={() => setMember({ isSubscribedToAnnouncements: true }, 'Group subscription updated')}><Bell class="size-4" /> {m.dialog_group_actions_subscribe()}</DropdownMenu.Item>
											{/if}
											<!-- A membership without the field counts as subscribed, as in the original. -->
											{#if myMember.isSubscribedToEventAnnouncements ?? true}
												<DropdownMenu.Item onSelect={() => setMember({ isSubscribedToEventAnnouncements: false }, 'Group event announcements updated')}><MessageCircleOff class="size-4" /> {m.dialog_group_actions_unsubscribe_event()}</DropdownMenu.Item>
											{:else}
												<DropdownMenu.Item onSelect={() => setMember({ isSubscribedToEventAnnouncements: true }, 'Group event announcements updated')}><MessageCircle class="size-4" /> {m.dialog_group_actions_subscribe_event()}</DropdownMenu.Item>
											{/if}
											{#if group.privacy === 'default'}
												<DropdownMenu.Separator />
												{#each [['visible', 'Visibility Everyone'], ['friends', 'Visibility Friends'], ['hidden', 'Visibility Hidden']] as [value, label] (value)}
													<DropdownMenu.Item onSelect={() => setMember({ visibility: value }, 'Group visibility updated')}>
														<Eye class="size-4" />
														{#if myMember.visibility === value}<Check class="size-4" />{/if}
														{label}
													</DropdownMenu.Item>
												{/each}
											{/if}
											<DropdownMenu.Separator />
											<DropdownMenu.Item onSelect={() => (previousOpen = true)}><LineChart class="size-4" /> {m.dialog_user_actions_show_previous_instances()}</DropdownMenu.Item>
											<DropdownMenu.Item variant="destructive" onSelect={leave}><Trash2 class="size-4" /> {isSoleGroupOwner(group, owner) ? 'Delete Group' : 'Leave Group'}</DropdownMenu.Item>
										{/if}
									{:else}
										<DropdownMenu.Separator />
										<DropdownMenu.Item onSelect={() => (previousOpen = true)}><LineChart class="size-4" /> {m.dialog_user_actions_show_previous_instances()}</DropdownMenu.Item>
										<DropdownMenu.Separator />
										{#if isBlocked}
											<DropdownMenu.Item variant="destructive" onSelect={unblock}><CheckCircle class="size-4" /> {m.dialog_group_actions_unblock()}</DropdownMenu.Item>
										{:else}
											<DropdownMenu.Item onSelect={block}><XCircle class="size-4" /> {m.dialog_group_actions_block()}</DropdownMenu.Item>
										{/if}
									{/if}
								</DropdownMenu.Content>
							</DropdownMenu.Root>
						</div>
					</div>
					<div class="mt-2 flex flex-col gap-1">
						{#if languages.length}
							<div class="flex flex-wrap gap-1">
								{#each languages as item (item.key)}
									<Badge variant="outline" class="inline-flex h-5 items-center gap-1 px-1.5 text-2xs leading-none">
										<span class={['flags inline-block shrink-0 self-center', languageClass(item.key)]}></span>
										<span class="inline-flex items-center leading-none">{item.value} ({item.key})</span>
									</Badge>
								{/each}
							</div>
						{/if}
						<div class="flex flex-wrap gap-1">
							{#if group.isVerified}<Badge variant="outline">{m.dialog_group_tags_verified()}</Badge>{/if}
							{#if group.privacy === 'private'}<Badge variant="outline">{m.dialog_group_tags_private()}</Badge>{/if}
							{#if group.privacy === 'default'}<Badge variant="outline">{m.dialog_group_tags_public()}</Badge>{/if}
							{#if group.joinState && JOIN_STATES[group.joinState]}<Badge variant="outline">{JOIN_STATES[group.joinState]}</Badge>{/if}
							{#if inGroup}<Badge variant="outline">{m.dialog_group_tags_joined()}</Badge>{/if}
							{#if myMember?.bannedAt}<Badge variant="outline">{m.dialog_group_tags_banned()}</Badge>{/if}
						</div>
					</div>
					{#if actions.error}
						<p class="mt-2 text-xs text-destructive">{actions.error}</p>
					{/if}
				</div>
			</div>

			<div class="mt-2.5 flex flex-col gap-2.5">
				<SectionCard title={m.dialog_group_info_header()}>
					<div class="flex flex-col gap-1.5">
						<InfoRow label={m.dialog_group_info_short_code()} value={`${group.shortCode}.${group.discriminator}`} />
						<InfoRow label={m.dialog_group_info_members()} value={String(group.memberCount ?? '')} />
						<InfoRow label={m.dialog_group_info_online_members()} value={String(group.onlineMemberCount ?? '')} />
						<InfoRow label={m.dialog_group_info_created()} value={timeAgo(group.createdAt)} title={formatDateFilter(group.createdAt, 'long')} />
						{#if group.links?.length}
							<div class="flex items-start justify-between gap-2 text-xs">
								<span class="shrink-0 text-muted-foreground">{m.dialog_group_info_links()}</span>
								<div class="flex gap-1">
									{#each group.links as link, index (`${link}:${index}`)}
										{#if link}
											<a href={link} target="_blank" rel="noopener noreferrer" title={link} onclick={(event) => { event.preventDefault(); event.stopPropagation(); openExternalLink(link); }}><img src={getFaviconUrl(link)} alt="" class="size-4 cursor-pointer align-middle" loading="lazy" /></a>
										{/if}
									{/each}
								</div>
							</div>
						{/if}
						<CopyIdRow label={m.dialog_group_info_id()} oncopy={() => actions.copy(groupId)} />
					</div>
				</SectionCard>
				<div class={PANEL}>
					<div class={SECTION_ROW}>
						<span class="inline-flex items-center gap-1 {SECTION}">{m.dialog_user_info_vrcx_info()} <span title={m.dialog_user_info_vrcx_info_tooltip()}><Info class="size-3 shrink-0" /></span></span>
					</div>
					<div class="flex flex-col gap-1.5">
						<InfoRow label={m.dialog_group_info_last_visited()} value={timeAgo(lastVisit)} title={formatDateFilter(lastVisit, 'long')} />
						<button type="button" class="cursor-pointer text-left hover:text-foreground" title={m.dialog_user_info_open_previous_instance()} onclick={() => (previousOpen = true)}>
							<InfoRow label={m.dialog_user_info_join_count()} value={joinCount ? String(joinCount) : NONE} />
						</button>
					</div>
				</div>
				{#if inGroup && myMember}
					<SectionCard title={m.dialog_group_info_member_info()}>
						<div class="flex flex-col gap-1.5">
							<InfoRow label={m.dialog_group_info_visibility()} value={VISIBILITY[myMember.visibility ?? ''] ?? ''} />
							<button type="button" class="cursor-pointer text-left" title={formatDateFilter(myMember.joinedAt, 'long')} onclick={() => (previousOpen = true)}>
								<InfoRow label={m.dialog_group_info_joined_at()} value={timeAgo(myMember.joinedAt)} />
							</button>
							<div class="flex items-start justify-between gap-2 text-xs">
								<span class="shrink-0 text-muted-foreground">{m.dialog_group_info_roles()}</span>
								{#if memberRoles.length === 0}
									<span class="text-right text-muted-foreground">{NONE}</span>
								{:else}
									<span class="text-right text-muted-foreground">
										{#each memberRoles as role, index (role.id)}
											<Tooltip.Root>
												<Tooltip.Trigger>{role.name}{index < memberRoles.length - 1 ? ', ' : ''}</Tooltip.Trigger>
												<Tooltip.Content side="right" class="text-left">
													<span>Role: {role.name}</span><br />
													<span>Description: {role.description}</span><br />
													{#if role.updatedAt}<span>Updated At: {formatDateFilter(role.updatedAt, 'long')}</span>{:else}<span>Created At: {formatDateFilter(role.createdAt, 'long')}</span>{/if}<br />
													<span>{m.dialog_group_info_role_permissions()}</span><br />
													{#each role.permissions ?? [] as permission (permission)}<span>{permission}</span><br />{/each}
												</Tooltip.Content>
											</Tooltip.Root>
										{/each}
									</span>
								{/if}
							</div>
						</div>
					</SectionCard>
				{/if}
			</div>
		</div>

		<div class="flex min-h-0 min-w-0 flex-1 flex-col pl-4">
			<TabsPrimitive.Root bind:value={() => activeTab, selectTab} class="flex min-h-0 w-full min-w-0 flex-1 flex-col">
				<div class={TAB_LIST_WRAP}>
					<TabsPrimitive.List class={TAB_LIST}>
						<TabsPrimitive.Trigger value="info" class={TAB_TRIGGER}>{m.dialog_group_info_header()}</TabsPrimitive.Trigger>
						<TabsPrimitive.Trigger value="posts" class={TAB_TRIGGER}>{m.dialog_group_posts_header()}</TabsPrimitive.Trigger>
						<TabsPrimitive.Trigger value="members" class={TAB_TRIGGER}>{m.dialog_group_info_members()}</TabsPrimitive.Trigger>
						<TabsPrimitive.Trigger value="photos" class={TAB_TRIGGER}>{m.dialog_group_gallery_header()}</TabsPrimitive.Trigger>
						{#if group.storeId}<TabsPrimitive.Trigger value="store" class={TAB_TRIGGER}>{m.dialog_group_store_header()}</TabsPrimitive.Trigger>{/if}
						<TabsPrimitive.Trigger value="json" class={TAB_TRIGGER}>{m.dialog_group_json_header()}</TabsPrimitive.Trigger>
					</TabsPrimitive.List>
				</div>

				<TabsPrimitive.Content value="info" class={TAB_CONTENT}>
					<GroupInfoTab {group} {groupId} {rooms} {friends} {posts} {calendar} {canManagePosts} {canManageCalendar} onfollow={followEvent} onedit={editPost} ondelete={deletePost} />
				</TabsPrimitive.Content>

				<TabsPrimitive.Content value="posts" class={TAB_CONTENT}>
					<GroupPostsTab {group} {posts} {postsError} bind:search={postSearch} {canManagePosts} onedit={editPost} ondelete={deletePost} />
				</TabsPrimitive.Content>

				<TabsPrimitive.Content value="members" class={TAB_CONTENT}>
					<GroupMembersTab {group} {inGroup} bind:sort={memberSort} bind:filter={memberFilter} bind:members />
				</TabsPrimitive.Content>

				<TabsPrimitive.Content value="photos" class={TAB_CONTENT}>
					<GroupPhotosTab bind:groupRef={() => group!, (next) => (group = next)} bind:images={galleryImages} />
				</TabsPrimitive.Content>

				{#if group.storeId}
					<TabsPrimitive.Content value="store" class={TAB_CONTENT}>
						<GroupStoreTab {groupId} storeId={group.storeId} />
					</TabsPrimitive.Content>
				{/if}

				<TabsPrimitive.Content value="json" class={TAB_CONTENT}>
					<JsonCard value={{ group, posts, instances: rooms, members, galleries: galleryImages, calendar }} fileName={groupId} download={group} onrefresh={refresh} />
				</TabsPrimitive.Content>
			</TabsPrimitive.Root>
		</div>
	</div>

	<PreviousInstancesDialog bind:open={previousOpen} kind="group" id={groupId} name={group?.name} />
	<InviteGroupDialog bind:open={inviteOpen} {groupId} />
	<GroupTransferDialog bind:open={transferOpen} {groupId} groupName={group.name} ownerId={group.ownerId} ontransferred={() => load(groupId)} />
	<GroupPostEditDialog bind:open={postEdit.open} {groupId} roles={group.roles ?? []} post={postEdit.post} onsaved={postSaved} />
	<ActionDialogs bind:confirming={actions.confirming} bind:prompting={actions.prompting} busy={actions.busy} />
{/if}
