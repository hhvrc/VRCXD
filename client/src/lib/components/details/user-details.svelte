<script lang="ts">
	import { toastError } from '#lib/helpers/feedback.js';
	import { SMALL_DIALOG } from '#lib/styles.js';
	import { m } from '#lib/paraglide/messages.js';
	import { untrack } from 'svelte';
	import ShieldCheck from '@lucide/svelte/icons/shield-check';
	import ShieldAlert from '@lucide/svelte/icons/shield-alert';
	import ChevronDown from '@lucide/svelte/icons/chevron-down';
	import * as Tooltip from '#lib/components/ui/tooltip/index.js';
	import { trustInfo } from '#lib/people/user-display.js';
	import IconFrame from '#lib/components/cosmetics/icon-frame.svelte';
	import NameplateEffect from '#lib/components/cosmetics/nameplate-effect.svelte';
	import ProfileEffect from '#lib/components/cosmetics/profile-effect.svelte';
	import CopyId from '#lib/components/common/copy-id.svelte';
	import { onItemChanged } from '#lib/api/live.svelte.js';
	import { noteInstance } from '#lib/instances/instance-launch.svelte.js';
	import { sidebarData } from '#lib/people/sidebar-data.svelte.js';
	import { recentActions } from '#lib/api/recent-actions.svelte.js';
	import Clock from '@lucide/svelte/icons/clock';
	import PreviousInstancesDialog from '#lib/components/instance/previous-instances-dialog.svelte';
	import FavoriteButton from '#lib/components/favorites/favorite-button.svelte';
	import ActionDialogs from '#lib/components/dialogs/action-dialogs.svelte';
	import { DetailActions } from '#lib/helpers/detail-actions.svelte.js';
	import AvatarInfo from '#lib/components/details/avatar-info.svelte';
	import ViewableImage from '#lib/components/common/viewable-image.svelte';
	import { NONE, TAB_CONTENT, TAB_LIST, TAB_LIST_WRAP, TAB_TRIGGER, PANEL, PROFILE_SECTION, TITLE_BUTTON } from '#lib/details.js';
	import { Tabs as TabsPrimitive } from 'bits-ui';
	import Apple from '@lucide/svelte/icons/apple';
	import Check from '@lucide/svelte/icons/check';
	import Hand from '@lucide/svelte/icons/hand';
	import History from '@lucide/svelte/icons/history';
	import IdCard from '@lucide/svelte/icons/id-card';
	import ImageIcon from '@lucide/svelte/icons/image';
	import MessageCircle from '@lucide/svelte/icons/message-circle';
	import MessageSquare from '@lucide/svelte/icons/message-square';
	import Monitor from '@lucide/svelte/icons/monitor';
	import MoreHorizontal from '@lucide/svelte/icons/ellipsis';
	import Pencil from '@lucide/svelte/icons/pencil';
	import RefreshCw from '@lucide/svelte/icons/refresh-cw';
	import Send from '@lucide/svelte/icons/send';
	import Share2 from '@lucide/svelte/icons/share-2';
	import Shield from '@lucide/svelte/icons/shield';
	import Smartphone from '@lucide/svelte/icons/smartphone';
	import UserIcon from '@lucide/svelte/icons/user';
	import UserMinus from '@lucide/svelte/icons/user-minus';
	import UserPlus from '@lucide/svelte/icons/user-plus';
	import Users from '@lucide/svelte/icons/users';
	import Network from '@lucide/svelte/icons/network';
	import Flag from '@lucide/svelte/icons/flag';
	import InviteGroupDialog from '#lib/components/invite/invite-group-dialog.svelte';
	import UserInfoTab from '#lib/components/details/user/user-info-tab.svelte';
	import UserListTab from '#lib/components/details/user/user-list-tab.svelte';
	import UserFavoriteWorldsTab from '#lib/components/details/user/user-favorite-worlds-tab.svelte';
	import UserHistoryTab from '#lib/components/details/user/user-history-tab.svelte';
	import UserJsonTab from '#lib/components/details/user/user-json-tab.svelte';
	import { UserTabs } from '#lib/components/details/user/user-tabs.svelte.js';
	import ModerateGroupDialog from '#lib/components/dialogs/moderate-group-dialog.svelte';
	import Settings from '@lucide/svelte/icons/settings';
	import ModerationReportDialog, { type ReportTarget } from '#lib/components/dialogs/moderation-report-dialog.svelte';
	import VolumeX from '@lucide/svelte/icons/volume-x';
	import XCircle from '@lucide/svelte/icons/circle-x';
	import * as Avatar from '#lib/components/ui/avatar/index.js';
	import { Badge } from '#lib/components/ui/badge/index.js';
	import { Checkbox } from '#lib/components/ui/checkbox/index.js';
	import * as Popover from '#lib/components/ui/popover/index.js';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as Dialog from '#lib/components/ui/dialog/index.js';
	import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
	import { Spinner } from '#lib/components/ui/spinner/index.js';
	import { accountPath, api, ApiError } from '#lib/api/client.js';
	import { imageSrc, groupHref, vrchatUrl } from '#lib/api/links.js';
	import { formatDateFilter, replaceBioSymbols, statusClass } from '#lib/helpers/format.js';
	import type { Friend, Instance, Memo, PlayerModeration, User, UserBadge, UserHistory } from '#lib/api/types.js';
	import type { Together } from './user/user-info-tab.svelte';
	import { openAvatarFromImage } from '#lib/content/avatar-lookup.js';
	import BadgeCheck from '@lucide/svelte/icons/badge-check';
	import { toast } from 'svelte-sonner';
	import { credits, refreshCredits } from '#lib/content/credits.svelte.js';
	import { parseLocation } from '#lib/instances/location.js';
	import BoopDialog from '#lib/components/invite/boop-dialog.svelte';
	import InviteMessageDialog from '#lib/components/invite/invite-message-dialog.svelte';
	import { userDialog } from '#lib/people/user-dialog.svelte.js';
	import { DEFAULT_PROFILE_THEME, profileTheme, type PublicProfile } from '#lib/settings/profile-theme.js';
	import { editProfile } from '#lib/content/edit-profile.svelte.js';
	import { vrchatAuth, ownPath } from '#lib/api/vrchat-auth.svelte.js';
	import { appearance } from '#lib/settings/appearance.svelte.js';
	import { announceUserDialog, customTag } from '#lib/people/custom-tags.svelte.js';
	import { tagStyle } from '#lib/people/custom-tags.js';
	import { agentCan } from '#lib/instances/instance-launch.svelte.js';
	import { AVATAR_HIDE, AVATAR_SHOW, readLocalModerations, setLocalModeration } from '#lib/agent/agent.js';

	// The original's user dialog (UserDialog.vue and its tabs), class for class: a summary column
	// with the image on top, and underlined tabs beside it. Used by the pop-up and by /user.
	let { userId }: { userId: string } = $props();

	// The original's getUserStatusText: anything else reads as Offline.
	const STATUSES: Record<string, string> = { active: 'Active', 'join me': 'Join Me', 'ask me': 'Ask Me', busy: 'Do Not Disturb' };
	const statusText = (status: string) => STATUSES[status] ?? 'Offline';
	const PADDED_TAB = `${TAB_CONTENT} pt-2`;

	let user = $state<User | undefined>();
	let history = $state<UserHistory | undefined>();
	let memo = $state('');
	let draftMemo = $state('');
	let friend = $state<Friend | undefined>();
	let moderations = $state<PlayerModeration[]>([]);
	// VRChat's own show/hide avatar list on the PC (LocalPlayerModerations): 4 hidden, 5 shown, 0 neither.
	let localAvatar = $state(0);
	let instance = $state<Instance | undefined>();
	let worldThumbnail = $state<string | undefined>();
	let loadError = $state('');
	let saving = $state('');
	let refreshing = $state(false);
	let activeTab = $state('info');
	let bannerError = $state(false);
	let iconError = $state(false);
	let editingMemo = $state(false);
	let relationship = $state<{ isFriend: boolean; incomingRequest: boolean; outgoingRequest: boolean; note: string } | undefined>();
	let ownLocation = $state('');
	let draftNote = $state('');
	const actions = new DetailActions(async () => {});
	let boopOpen = $state(false);
	let reporting = $state<ReportTarget | undefined>();
	let invitingToGroup = $state<string | undefined>();
	let moderatingInGroup = $state<string | undefined>();
	let inviteMessageOpen = $state(false);
	let requestMessageOpen = $state(false);
	let previousOpen = $state(false);

	let mutualCounts = $state<{ friends: number; groups: number } | undefined>();
	// The original's getUserStats and friend log history: the game log's stays with this user, when
	// it last saw them, and the Friend and Unfriend dates.
	let together = $state<Together | undefined>();
	let friendNumbers = $state(new Map<string, number>());
	// The list, favorite worlds and bio state the tabs share. It outlives the shown user.
	const tabs = new UserTabs({ userId: () => userId, activeTab: () => activeTab, friendNumbers: () => friendNumbers, actions });
	const isSelf = $derived(userId === vrchatAuth.ownerUserId);
	// The original shows the public profile's badges: the live one once loaded, the stored copy until then.
	const badges = $derived(userDialog.profileOf(userId)?.badges ?? user?.badges ?? []);

	async function showAvatarAuthor() {
		const image = user?.currentAvatarImageUrl || user?.currentAvatarThumbnailImageUrl;
		if (!image) return;
		try {
			const warning = await openAvatarFromImage(image, userId);
			if (warning) toast.warning(warning);
		} catch (err) {
			toastError(err);
		}
	}
	const bannerUrl = $derived(user?.profilePicOverride || user?.currentAvatarImageUrl || user?.currentAvatarThumbnailImageUrl);
	const iconUrl = $derived(user?.iconUrl || user?.userIcon || user?.profilePicOverride || user?.currentAvatarThumbnailImageUrl || user?.currentAvatarImageUrl);
	const avatarUrl = $derived(user?.currentAvatarThumbnailImageUrl || user?.currentAvatarImageUrl);
	const platform = $derived(user?.platform || user?.lastPlatform);
	// The original's $customTag: what a program on the PC pipe tagged this user with.
	const tag = $derived(vrchatAuth.ownerUserId ? customTag(vrchatAuth.ownerUserId, userId) : undefined);
	// The original's getUserStateText: online shows the status, active and offline add it in brackets.
	const stateText = $derived.by(() => {
		if (!user) return '';
		if (user.state !== 'active' && user.state !== 'offline') return statusText(user.status);
		const state = user.state === 'active' ? 'Active' : 'Offline';
		return user.status && user.status !== 'active' ? `${state} (${statusText(user.status)})` : state;
	});
	// The original's $isModerator: a developer type, or the admin_moderator tag.
	const isModerator = $derived(!!user && ((!!user.developerType && user.developerType !== 'none') || user.tags.includes('admin_moderator')));
	const dotClass = $derived(!user ? '' : user.state === 'offline' ? '' : user.state === 'active' ? (user.status === 'active' ? 'active' : `active-${statusClass(user.status)}`) : statusClass(user.status));
	const hasModeration = (type: string) => moderations.some((item) => item.type === type);
	const isFriend = $derived(relationship?.isFriend ?? !!friend);
	const note = $derived(relationship?.note ?? '');
	const canInvite = $derived(!!ownLocation && parseLocation(ownLocation).isRealInstance);
	// Turning one on is destructive and not offered against moderators, as in the original.
	const MODERATIONS = [
		{ type: 'block', on: 'Block', off: 'Unblock' },
		{ type: 'mute', on: 'Mute', off: 'Unmute' },
		{ type: 'muteChat', on: 'Mute Chatbox', off: 'Unmute Chatbox' },
		{ type: 'interactOff', on: 'Disable Avatar Interaction', off: 'Enable Avatar Interaction' }
	];
	// The original's setPlayerModeration: Hide and Show Avatar toggle, through the PC's agent.
	async function setLocalAvatar(type: number) {
		const id = userId;
		const next = localAvatar === type ? 0 : type;
		try {
			await setLocalModeration(id, next);
			if (id === userId) localAvatar = next;
		} catch {
			toast.error(m.message_avatar_change_moderation_failed());
		}
	}

	async function loadLocalAvatar(id: string) {
		if (!agentCan('local_moderations')) return;
		const list = await readLocalModerations().catch(() => null);
		if (id === userId) localAvatar = list?.[id] ?? 0;
	}

	function moderate(type: string, label: string, on: boolean) {
		actions.confirming = { command: label, get title() { return m.dialog_user_note_memo_confirm(); }, destructive: !on, run: () => actions.act(() => setModeration(type, !on), 'User moderated') };
	}

	async function load(id: string, refresh = false) {
		const ownerUserId = vrchatAuth.ownerUserId;
		if (!ownerUserId || !id) return;
		loadError = '';
		try {
			const [u, h, friends, mods] = await Promise.all([
				api.get<User>(`/1/users/${encodeURIComponent(id)}`, { refresh, account: ownerUserId }),
				api.get<UserHistory>(`/1/users/${encodeURIComponent(id)}/history`).catch(() => undefined),
				api.get<Friend[]>(accountPath(ownerUserId, '/friends')),
				api.get<PlayerModeration[]>(accountPath(ownerUserId, '/moderations'))
			]);
			const [nextRelationship, own] = await Promise.all([
				api.get<NonNullable<typeof relationship>>(`${accountPath(ownerUserId)}/users/${encodeURIComponent(id)}/relationship`).catch(() => undefined),
				api.get<User>(`/1/users/${encodeURIComponent(ownerUserId)}`, { account: ownerUserId }).catch(() => undefined)
			]);
			const nextMemo = await api
				.get<Memo>(`/1/users/${encodeURIComponent(id)}/memo`)
				.then((item) => item.memo)
				.catch(() => '');
			// Another user was opened while this one loaded.
			if (id !== userId) return;
			user = u;
			history = h;
			friend = friends.find((f) => f.userId === id);
			friendNumbers = new Map(friends.map((f) => [f.userId, f.friendNumber]));
			moderations = mods.filter((item) => item.targetUserId === id);
			memo = nextMemo;
			relationship = nextRelationship;
			ownLocation = own?.location ?? '';
			userDialog.setLabel(id, u.displayName);
			loadInstance(id, u);
			loadMutualCounts(id);
			loadTogether(id);
			loadProfile(id);
			tabs.loadTab('groups');
		} catch (err) {
			if (id !== userId) return;
			const error = ApiError.from(err);
			loadError = error.type === 'User.NotFound' ? 'The server has never seen this user. Refresh to fetch them from VRChat.' : error.message;
			user = undefined;
		}
	}

	// The original's updateUserDialogProfile: the profile theme and background, live from VRChat.
	async function loadProfile(id: string) {
		const ownerUserId = vrchatAuth.ownerUserId;
		if (!ownerUserId) return;
		const profile = await api.get<PublicProfile | null>(`${accountPath(ownerUserId)}/users/${encodeURIComponent(id)}/public-profile`).catch(() => null);
		userDialog.setProfile(id, profile ?? {});
	}

	// VRChat Profile Themes: the user's icon, button and subtext colours, made readable on this theme.
	const theme = $derived(appearance.displayVRCProfileThemes.value ? profileTheme(userDialog.profileOf(userId), appearance.isDarkMode) : DEFAULT_PROFILE_THEME);
	const iconStyle = $derived(`color: ${theme.iconColor === DEFAULT_PROFILE_THEME.iconColor ? 'var(--foreground)' : theme.iconColor}`);

	async function loadTogether(id: string) {
		together = undefined;
		const ownerUserId = vrchatAuth.ownerUserId;
		if (!ownerUserId || id === ownerUserId) return;
		const totals = await api.get<NonNullable<typeof together>>(`${accountPath(ownerUserId)}/users/${encodeURIComponent(id)}/together`).catch(() => undefined);
		if (id === userId) together = totals;
	}

	// The original asks for the shared friend and group counts of anyone but yourself, unless your
	// account hides its shared connections.
	async function loadMutualCounts(id: string) {
		mutualCounts = undefined;
		const ownerUserId = vrchatAuth.ownerUserId;
		if (!ownerUserId || id === ownerUserId) return;
		if (!sidebarData.me) await sidebarData.loadMe();
		if (sidebarData.me?.hasSharedConnectionsOptOut) return;
		const counts = await api.get<{ friends: number; groups: number }>(`${accountPath(ownerUserId)}/users/${encodeURIComponent(id)}/mutual-counts`).catch(() => undefined);
		if (id === userId) mutualCounts = counts;
	}

	// The original's toggleBadgeVisibility and toggleBadgeShowcased: hiding a badge takes it out of the
	// showcase and showcasing one shows it.
	async function updateBadge(badge: UserBadge, change: { hidden?: boolean; showcased?: boolean }) {
		let hidden = change.hidden ?? !!badge.hidden;
		let showcased = change.showcased ?? badge.showcased;
		if (change.hidden) showcased = false;
		if (change.showcased) hidden = false;
		try {
			const updated = await api.put<UserBadge>(ownPath(`/badges/${encodeURIComponent(badge.badgeId)}`), { hidden, showcased });
			const apply = (list: UserBadge[]) => list.map((b) => (b.badgeId === badge.badgeId ? { ...b, hidden: updated.hidden, showcased: updated.showcased } : b));
			if (user?.badges) user.badges = apply(user.badges);
			const profile = userDialog.profileOf(userId);
			if (profile?.badges) userDialog.setProfile(userId, { ...profile, badges: apply(profile.badges) });
			toast.success(m.message_badge_updated());
		} catch (err) {
			toastError(err);
		}
	}

	async function loadInstance(id: string, u: User) {
		instance = undefined;
		worldThumbnail = undefined;
		const location = u.location;
		if (u.state !== 'online' || !location || !parseLocation(location).isRealInstance) return;
		const [i, w] = await Promise.all([
			api.get<Instance>(`/1/instances/${encodeURIComponent(location)}`).then(noteInstance, () => undefined),
			api.get<{ thumbnailImageUrl?: string }>(`/1/worlds/${encodeURIComponent(location.split(':')[0])}`).catch(() => undefined)
		]);
		if (id !== userId) return;
		instance = i;
		worldThumbnail = w?.thumbnailImageUrl;
	}

	async function refresh() {
		refreshing = true;
		actions.error = '';
		try {
			await load(userId, true);
		} finally {
			refreshing = false;
		}
	}

	$effect(() => {
		const id = userId;
		if (vrchatAuth.ownerUserId && id) {
			user = undefined;
			history = undefined;
			friend = undefined;
			moderations = [];
			localAvatar = 0;
			untrack(() => loadLocalAvatar(id));
			instance = undefined;
			relationship = undefined;
			actions.reset();
			activeTab = 'info';
			tabs.reset();
			bannerError = false;
			iconError = false;
			load(id);
			untrack(() => announceUserDialog(vrchatAuth.ownerUserId, id));
		}
	});

	function editNoteAndMemo() {
		draftNote = note;
		draftMemo = memo;
		editingMemo = true;
	}

	async function saveNoteAndMemo() {
		saving = 'memo';
		actions.error = '';
		try {
			if (draftMemo !== memo) {
				if (draftMemo.trim()) await api.put(`/1/users/${encodeURIComponent(userId)}/memo`, { memo: draftMemo });
				else await api.delete(`/1/users/${encodeURIComponent(userId)}/memo`);
				memo = draftMemo;
			}
			if (draftNote !== note) {
				await api.put(ownPath(`/user-notes/${encodeURIComponent(userId)}`), { note: draftNote });
				// The original's handleNoteChange shows the saved note through replaceBioSymbols, as the server stores it.
				if (relationship) relationship.note = replaceBioSymbols(draftNote);
			}
			editingMemo = false;
		} catch (err) {
			actions.error = ApiError.from(err).message;
		} finally {
			saving = '';
		}
	}

	const userPath = $derived(ownPath());
	const encodedId = $derived(encodeURIComponent(userId));

	async function setModeration(type: string, on: boolean) {
		const path = `${userPath}/moderations/${encodedId}/${type}`;
		if (on) await api.post(path);
		else await api.delete(path);
		const mods = await api.get<PlayerModeration[]>(ownPath('/moderations'));
		moderations = mods.filter((item) => item.targetUserId === userId);
	}

	async function sendInvite(messageSlot?: number, photo?: Blob | null) {
		const world = await api.get<Instance>(`/1/instances/${encodeURIComponent(ownLocation)}`).catch(() => undefined);
		const worldName = world?.worldName ?? '';
		if (photo) {
			const form = new FormData();
			form.append('image', photo, 'image.png');
			form.append('location', ownLocation);
			form.append('worldName', worldName);
			if (messageSlot !== undefined) form.append('messageSlot', String(messageSlot));
			await api.upload(`${userPath}/invites/${encodedId}/photo`, form);
		} else await api.post(`${userPath}/invites/${encodedId}`, { location: ownLocation, worldName, messageSlot: messageSlot ?? null });
	}

	async function sendRequestInvite(requestSlot: number, photo: Blob | null) {
		if (photo) {
			const form = new FormData();
			form.append('image', photo, 'image.png');
			form.append('requestSlot', String(requestSlot));
			await api.upload(`${userPath}/invite-requests/${encodedId}/photo`, form);
		} else await api.post(`${userPath}/invite-requests/${encodedId}`, { requestSlot });
	}

	async function friendAction(action: 'send' | 'cancel' | 'unfriend' | 'accept' | 'decline') {
		const base = `${userPath}/friend-requests/${encodedId}`;
		if (action === 'send') await api.post(base);
		else if (action === 'cancel') await api.delete(base);
		else if (action === 'accept') await api.post(`${base}/accept`);
		else if (action === 'decline') await api.post(`${base}/decline`);
		else await api.delete(`${userPath}/friends/${encodedId}`);
		await load(userId, true);
	}

	// The original's onTabActivated: the own Info tab reads the balance once.
	$effect(() => {
		if (isSelf && activeTab === 'info' && !credits.has(userId)) untrack(() => refreshCredits(userId));
	});
	$effect(() => onItemChanged('user', () => userId, () => load(userId)));
	// The original's updateUserDialogProfile after a profile save.
	$effect(() =>
		editProfile.onSaved(() => {
			if (isSelf) load(userId, true);
		})
	);
</script>

{#if !vrchatAuth.ownerUserId}
	<p class="p-1.5 text-sm text-muted-foreground">Log in to VRChat to look up a user.</p>
{:else if !userId}
	<p class="p-1.5 text-sm text-muted-foreground">No user selected: open one from the friend list, player list or search.</p>
{:else if loadError && !user}
	<div class="flex flex-col items-start gap-2 p-1.5">
		<p class="text-sm text-destructive">{loadError}</p>
		<CopyId id={userId} />
		<Button variant="outline" size="sm" disabled={refreshing} onclick={refresh}>{refreshing ? 'Fetching…' : 'Refresh'}</Button>
	</div>
{:else if !user}
	<div class="flex h-40 items-center justify-center"><Spinner /></div>
{:else}
	<div class="flex min-h-0 min-w-0 flex-1 flex-row" data-slot="user-details" style="--profile-subtext: {theme.subtextColor}; --profile-icon: {theme.iconColor}">
		<div class="w-77 flex-none overflow-y-auto">
			<div class="relative flex flex-col overflow-hidden rounded-t-[15px] rounded-b-lg bg-(--profile-card)">
				<ProfileEffect profileEffect={user.profileEffect} class="z-1" />
				<div class="relative aspect-17/6">
					{#if bannerUrl && !bannerError}
						<ViewableImage src={bannerUrl} class="absolute inset-0" imageClass="block size-full object-cover" onerror={() => (bannerError = true)} />
					{:else}
						<div class="absolute inset-0 bg-muted"></div>
					{/if}
					<div class="absolute right-2.5 bottom-0 z-30 flex translate-y-1/2 gap-1.5 pt-10">
						{#if hasModeration('block')}<span title="Blocked"><XCircle class="h-6 w-6 text-red-400" /></span>{/if}
						{#if hasModeration('mute')}<span title="Muted"><VolumeX class="h-6 w-6 text-red-400" /></span>{/if}
						{#if hasModeration('muteChat')}<span title="Chatbox disabled"><MessageCircle class="h-6 w-6 text-red-400" /></span>{/if}
						{#if hasModeration('hideAvatar') || localAvatar === AVATAR_HIDE}<span title="Avatar hidden"><UserIcon class="h-6 w-6 text-red-400" /></span>{/if}
						{#if hasModeration('interactOff')}<span title="Avatar interaction disabled"><Hand class="h-6 w-6 text-red-400" /></span>{/if}
					</div>
					<div class="absolute bottom-0 left-3 z-20 size-24 translate-y-1/2">
						<div class="size-full overflow-hidden rounded-full" style="filter: drop-shadow(0 0 1px rgb(0 0 0 / 0.95)) drop-shadow(0 0 4px rgb(0 0 0 / 0.75)) drop-shadow(0 2px 8px rgb(0 0 0 / 0.55));">
							{#if iconUrl && !iconError}
								<ViewableImage src={iconUrl} class="size-full" imageClass="size-full object-cover" onerror={() => (iconError = true)} />
							{:else}
								<div class="flex size-full items-center justify-center bg-accent text-muted-foreground"><ImageIcon /></div>
							{/if}
						</div>
						<IconFrame iconFrame={user.iconFrame} class="z-2" />
					</div>
				</div>

				<NameplateEffect nameplateEffect={user.nameplateEffect} />

				<div class="relative isolate z-40 flex flex-col gap-2 px-3 pt-15 pb-3">
					<div class="flex items-center gap-1.5">
						<div class="min-w-0 flex-1">
							<div class="flex flex-wrap items-center gap-x-1 leading-snug">
								{#if history?.displayNames.length}
									<Tooltip.Root>
										<Tooltip.Trigger aria-label="Previous Display Names"><ChevronDown class="inline-block size-4" /></Tooltip.Trigger>
										<Tooltip.Content side="bottom">
											<span>Previous Display Names</span>
											{#each history.displayNames as change, index (`${change.changedAt}:${index}`)}
												<div><span>{change.previousDisplayName}</span> &horbar; {formatDateFilter(change.changedAt, 'long')}</div>
											{/each}
										</Tooltip.Content>
									</Tooltip.Root>
								{/if}
								<button type="button" class={TITLE_BUTTON} title={user.displayName} onclick={() => actions.copy(user?.displayName ?? '', 'User DisplayName copied to clipboard')}>
									{user.displayName || userId}
								</button>
								{#if userDialog.profileOf(userId)?.isEconomyCreator}
									<span title={m.dialog_user_info_economy_creator()}><BadgeCheck class="size-3.5 text-[#3b82f6]" /></span>
								{/if}
								{#if user.pronouns}
									<span class="h-0 basis-full"></span>
									<span class="font-mono text-xs text-muted-foreground">{replaceBioSymbols(user.pronouns)}</span>
								{/if}
							</div>
						</div>
						<FavoriteButton type="friend" targetId={userId} onlyIfFavorite={isSelf || !isFriend} class="flex-none border-muted-foreground/30!" {theme} />
						<DropdownMenu.Root>
							<DropdownMenu.Trigger>
								{#snippet child({ props })}
									<Button {...props} variant="outline" size="icon" class="ml-2 flex-none rounded-lg border-muted-foreground/30!" style={iconStyle} aria-label={m.nav_tooltip_manage()}>
										<MoreHorizontal />
									</Button>
								{/snippet}
							</DropdownMenu.Trigger>
							<DropdownMenu.Content class="w-auto whitespace-nowrap">
								<DropdownMenu.Item disabled={refreshing} onSelect={refresh}><RefreshCw class="size-4" /> {m.dialog_user_actions_refresh()}</DropdownMenu.Item>
								<DropdownMenu.Sub>
									<DropdownMenu.SubTrigger><Share2 class="size-4" /> {m.dialog_user_actions_share()}</DropdownMenu.SubTrigger>
									<DropdownMenu.SubContent class="w-auto whitespace-nowrap">
										<DropdownMenu.Item onSelect={() => actions.copy(vrchatUrl('user', userId), 'User URL copied to clipboard')}>{m.dialog_user_info_copy_url()}</DropdownMenu.Item>
										<DropdownMenu.Item onSelect={() => actions.copy(user?.displayName ?? '', 'User DisplayName copied to clipboard')}>{m.dialog_user_info_copy_display_name()}</DropdownMenu.Item>
										<DropdownMenu.Item onSelect={() => actions.copy(userId, 'User ID copied to clipboard')}>{m.dialog_user_info_copy_id()}</DropdownMenu.Item>
									</DropdownMenu.SubContent>
								</DropdownMenu.Sub>
								{#if isSelf}
									<DropdownMenu.Item onSelect={() => editProfile.show()}><Pencil class="size-4" /> {m.dialog_user_actions_edit_profile()}</DropdownMenu.Item>
									{#if sidebarData.me?.fallbackAvatar}
										<DropdownMenu.Item onSelect={() => sidebarData.me?.fallbackAvatar && userDialog.openAvatar(sidebarData.me.fallbackAvatar)}><UserIcon class="size-4" /> {m.dialog_user_actions_show_fallback_avatar()}</DropdownMenu.Item>
									{/if}
								{:else}
									<DropdownMenu.Separator />
									{#if isFriend}
										<DropdownMenu.Item onSelect={() => actions.act(() => api.post(`${userPath}/invite-requests/${encodedId}`), 'Request invite sent')}><Send class="size-4" /> Request Invite{#if recentActions.isRecent(userId, 'Request Invite')}<DropdownMenu.Shortcut><Clock class="size-3.5 text-muted-foreground" /></DropdownMenu.Shortcut>{/if}</DropdownMenu.Item>
										<DropdownMenu.Item onSelect={() => (requestMessageOpen = true)}><Send class="size-4" /> Request Invite With Message{#if recentActions.isRecent(userId, 'Request Invite Message', 'Request Invite Photo')}<DropdownMenu.Shortcut><Clock class="size-3.5 text-muted-foreground" /></DropdownMenu.Shortcut>{/if}</DropdownMenu.Item>
										{#if canInvite}
											<DropdownMenu.Item onSelect={() => actions.act(() => sendInvite(), 'Invite sent')}><MessageSquare class="size-4" /> Invite{#if recentActions.isRecent(userId, 'Invite')}<DropdownMenu.Shortcut><Clock class="size-3.5 text-muted-foreground" /></DropdownMenu.Shortcut>{/if}</DropdownMenu.Item>
											<DropdownMenu.Item onSelect={() => (inviteMessageOpen = true)}><MessageSquare class="size-4" /> Invite With Message{#if recentActions.isRecent(userId, 'Invite Message', 'Invite Photo')}<DropdownMenu.Shortcut><Clock class="size-3.5 text-muted-foreground" /></DropdownMenu.Shortcut>{/if}</DropdownMenu.Item>
										{/if}
										<DropdownMenu.Item disabled={!sidebarData.me?.isBoopingEnabled} onSelect={() => (boopOpen = true)}><Hand class="size-4" /> {m.dialog_user_actions_send_boop()}</DropdownMenu.Item>
									{:else if relationship?.incomingRequest}
										<DropdownMenu.Item onSelect={() => actions.confirm(m.dialog_user_actions_accept_friend_request(), () => friendAction('accept'))}><Check class="size-4" /> {m.dialog_user_actions_accept_friend_request()}</DropdownMenu.Item>
										<DropdownMenu.Item onSelect={() => actions.confirm(m.dialog_user_actions_decline_friend_request(), () => friendAction('decline'))}><XCircle class="size-4" /> {m.dialog_user_actions_decline_friend_request()}</DropdownMenu.Item>
									{:else if relationship?.outgoingRequest}
										<DropdownMenu.Item onSelect={() => actions.confirm(m.dialog_user_actions_cancel_friend_request(), () => friendAction('cancel'))}><XCircle class="size-4" /> {m.dialog_user_actions_cancel_friend_request()}</DropdownMenu.Item>
									{:else}
										<DropdownMenu.Item onSelect={() => actions.confirm(m.dialog_user_actions_send_friend_request(), () => friendAction('send'))}><UserPlus class="size-4" /> Send Friend Request{#if recentActions.isRecent(userId, 'Send Friend Request')}<DropdownMenu.Shortcut><Clock class="size-3.5 text-muted-foreground" /></DropdownMenu.Shortcut>{/if}</DropdownMenu.Item>
									{/if}
									<DropdownMenu.Item onSelect={editNoteAndMemo}><Pencil class="size-4" /> {m.dialog_user_actions_edit_note_memo()}</DropdownMenu.Item>
									<DropdownMenu.Separator />
									<DropdownMenu.Sub>
										<DropdownMenu.SubTrigger><Shield class="size-4" /> {m.nav_tooltip_moderation()}</DropdownMenu.SubTrigger>
										<DropdownMenu.SubContent class="w-auto whitespace-nowrap">
											<DropdownMenu.Item disabled={isModerator} onSelect={() => (reporting = { type: 'user', contentId: userId, contentName: user?.displayName })}><Flag class="size-4" /> {m.dialog_user_actions_report_user()}</DropdownMenu.Item>
											<DropdownMenu.Separator />
											{#each MODERATIONS as moderation (moderation.type)}
												{@const on = hasModeration(moderation.type)}
												{@const label = on ? moderation.off : moderation.on}
												<DropdownMenu.Item disabled={!on && isModerator} onSelect={() => moderate(moderation.type, label, on)}>{label}</DropdownMenu.Item>
											{/each}
											{#if agentCan('local_moderations')}
												<DropdownMenu.Item onSelect={() => setLocalAvatar(AVATAR_SHOW)}><UserIcon class="size-4" />{#if localAvatar === AVATAR_SHOW}<Check class="size-4" />{/if} {m.dialog_user_actions_moderation_show_avatar()}</DropdownMenu.Item>
												<DropdownMenu.Item onSelect={() => setLocalAvatar(AVATAR_HIDE)}><UserIcon class="size-4" />{#if localAvatar === AVATAR_HIDE}<Check class="size-4" />{/if} {m.dialog_user_actions_moderation_hide_avatar()}</DropdownMenu.Item>
											{/if}
										</DropdownMenu.SubContent>
									</DropdownMenu.Sub>
									<DropdownMenu.Separator />
									<DropdownMenu.Item onSelect={() => (invitingToGroup = userId)}><MessageSquare class="size-4" /> {m.dialog_user_actions_invite_to_group()}</DropdownMenu.Item>
									<DropdownMenu.Item onSelect={() => (moderatingInGroup = userId)}><Settings class="size-4" /> {m.dialog_user_actions_group_moderation()}</DropdownMenu.Item>
									<DropdownMenu.Separator />
									{#if user.currentAvatarThumbnailImageUrl}
										<!-- The original's Show Avatar Author (upstream issues 15 and 109): the avatar, or failing that its author. -->
										<DropdownMenu.Item onSelect={showAvatarAuthor}><UserIcon class="size-4" /> {m.dialog_user_actions_show_avatar_author()}</DropdownMenu.Item>
									{/if}
									<DropdownMenu.Item onSelect={() => (previousOpen = true)}><History class="size-4" /> {m.dialog_user_actions_show_previous_instances()}</DropdownMenu.Item>
									{#if isFriend}
										<DropdownMenu.Separator />
										<DropdownMenu.Item variant="destructive" onSelect={() => (actions.confirming = { command: 'Unfriend', get title() { return m.dialog_user_note_memo_confirm(); }, destructive: true, run: () => actions.act(() => friendAction('unfriend'), 'Unfriended') })}><UserMinus class="size-4" /> {m.dialog_user_actions_unfriend()}</DropdownMenu.Item>
									{/if}
								{/if}
							</DropdownMenu.Content>
						</DropdownMenu.Root>
					</div>

					<!-- svelte-ignore a11y_no_static_element_interactions, a11y_click_events_have_key_events -->
					<div class={['flex items-center gap-1.5 text-xs text-muted-foreground', isSelf && 'cursor-pointer hover:text-foreground']} onclick={() => isSelf && editProfile.show()}>
						<i class="x-user-status mt-0.5 flex-none {dotClass}" title={stateText}></i>
						<div class="min-w-0">
							{#if user.statusDescription}
								<span class="block wrap-anywhere">{user.statusDescription}</span>
							{:else}
								<span class="block wrap-anywhere">{stateText}</span>
							{/if}
						</div>
					</div>

					<div class="flex flex-wrap gap-1 text-2xs">
						<Badge variant="outline" class={['name h-5 px-1.5 text-2xs leading-none', trustInfo(user.tags, user.developerType).className]} title={m.dialog_user_tags_trust_level()}><Shield class="h-2.5 w-2.5" /> {user.trustLevel}</Badge>
						{#if isSelf && sidebarData.me?.accountStanding}
							{@const good = sidebarData.me.accountStanding === 'good'}
							<Badge variant="outline" class={['h-5 px-1.5 text-2xs leading-none capitalize', good ? 'border-green-500! text-green-500' : 'border-red-500! text-red-500']} title={m.dialog_user_tags_account_standing()}>
								{#if good}<ShieldCheck class="h-2.5 w-2.5" />{:else}<ShieldAlert class="h-2.5 w-2.5" />{/if}
								{sidebarData.me.accountStanding}
							</Badge>
						{/if}
						{#if user.ageVerified && user.ageVerificationStatus}
							<Badge variant="outline" class="h-5 border-[#3b82f6]! px-1.5 text-2xs leading-none text-[#3b82f6]" title={m.dialog_user_tags_age_verified()}><IdCard class="h-2.5 w-2.5" />{#if user.ageVerificationStatus === '18+'} 18+{/if}</Badge>
						{/if}
						{#if friend}
							<Badge variant="outline" class="h-5 border-amber-400! px-1.5 text-2xs leading-none text-amber-400"><UserPlus class="h-2.5 w-2.5" /> {friend.friendNumber || ''}</Badge>
						{/if}
						{#if mutualCounts?.friends}
							<Badge variant="outline" class="h-5 border-zinc-500/50! px-1.5 text-2xs leading-none" title={m.dialog_user_tags_mutual_friends()}><Users class="h-2.5 w-2.5" /> {mutualCounts.friends}</Badge>
						{/if}
						{#if mutualCounts?.groups}
							<Badge variant="outline" class="h-5 border-zinc-500/50! px-1.5 text-2xs leading-none" title={m.dialog_user_tags_mutual_groups()}><Network class="h-2.5 w-2.5" /> {mutualCounts.groups}</Badge>
						{/if}
						{#if user.discordId}
							<a href={`discord://-/users/${encodeURIComponent(user.discordId)}`} title={m.dialog_user_tags_open_in_discord()}>
								<Badge variant="outline" class="h-5 cursor-pointer border-[#7289da]! px-1.5 text-2xs leading-none text-[#7289da]">
									<i class="ri-discord-line inline-flex h-2.5 w-2.5 items-center justify-center text-3xs leading-none before:block before:leading-none"></i> {m.dialog_user_tags_discord()}
								</Badge>
							</a>
						{/if}
						{#if user.tags.includes('system_troll')}
							<Badge variant="outline" class="x-tag-troll h-5 px-1.5 text-2xs leading-none">{m.view_settings_appearance_user_colors_trust_levels_nuisance()}</Badge>
						{:else if user.tags.includes('system_probable_troll')}
							<Badge variant="outline" class="x-tag-troll h-5 px-1.5 text-2xs leading-none">{m.view_favorite_avatars_almost_nuisance()}</Badge>
						{/if}
						{#if isModerator}
							<Badge variant="outline" class="x-tag-vip h-5 px-1.5 text-2xs leading-none">{m.dialog_user_tags_vrchat_team()}</Badge>
						{/if}
						{#if platform === 'standalonewindows'}
							<Badge variant="outline" class="text-platform-pc border-platform-pc! h-5 px-1.5 text-2xs"><Monitor class="text-platform-pc h-3 w-3" /> PC</Badge>
						{:else if platform === 'android'}
							<Badge variant="outline" class="text-platform-quest border-platform-quest! h-5 px-1.5 text-2xs"><Smartphone class="text-platform-quest h-3 w-3" /> {m.dialog_group_event_edit_platform_values_android()}</Badge>
						{:else if platform === 'ios'}
							<Badge variant="outline" class="text-platform-ios border-platform-ios! h-5 px-1.5 text-2xs"><Apple class="text-platform-ios h-3 w-3" /> {m.dialog_group_event_edit_platform_values_ios()}</Badge>
						{/if}
						{#if tag}
							<Badge variant="outline" class="name h-5 px-1.5 text-2xs leading-none" style={tagStyle(tag)}>{tag.tag}</Badge>
						{/if}
					</div>
					{#if badges.length}
						<div class="flex flex-wrap gap-1.5">
							{#each badges as badge (badge.badgeId)}
								<Popover.Root>
									<Popover.Trigger title={badge.hidden ? `${badge.badgeName} (Hidden)` : badge.badgeName}>
										<img src={imageSrc(badge.badgeImageUrl)} alt={badge.badgeName} loading="lazy" class={['size-7.5 cursor-pointer rounded-sm object-cover hover:grayscale-0', badge.hidden && 'grayscale']} />
									</Popover.Trigger>
									<Popover.Content side="right" class="w-75 text-sm">
										<ViewableImage src={badge.badgeImageUrl} imageClass="max-h-full max-w-full" />
										<div class="mt-1 flex flex-col wrap-normal">
											<span>{badge.badgeName}</span>
											<span class="text-xs text-muted-foreground">{badge.badgeDescription}</span>
											{#if badge.assignedAt}
												<span class="font-mono text-xs text-muted-foreground">{m.dialog_user_badges_assigned()}: {formatDateFilter(badge.assignedAt, 'long')}</span>
											{/if}
											{#if isSelf}
												<label class="mt-1.5 inline-flex items-center gap-2">
													<Checkbox checked={!!badge.hidden} onCheckedChange={(hidden) => updateBadge(badge, { hidden })} />
													<span>{m.dialog_user_badges_hidden()}</span>
												</label>
												<label class="mt-1 inline-flex items-center gap-2">
													<Checkbox checked={badge.showcased} onCheckedChange={(showcased) => updateBadge(badge, { showcased })} />
													<span>{m.dialog_user_badges_showcased()}</span>
												</label>
											{/if}
										</div>
									</Popover.Content>
								</Popover.Root>
							{/each}
						</div>
					{/if}
					{#if actions.error}
						<p class="text-xs text-destructive">{actions.error}</p>
					{/if}
				</div>
			</div>

			<div class="mt-2 flex flex-col {PANEL}">
				<div class="{PROFILE_SECTION} mb-2 border-b border-muted-foreground/20 pb-2">{m.dialog_user_info_avatar_info()}</div>
				<div class="flex justify-between gap-2 text-xs">
					{#if avatarUrl}
						<AvatarInfo imageUrl={user.currentAvatarImageUrl || avatarUrl} {userId} resolve />
						<ViewableImage src={avatarUrl} full={user.currentAvatarImageUrl} class="flex-none" imageClass="h-12 w-16 rounded-lg object-cover" />
					{:else}
						<div class="text-xs text-muted-foreground">{NONE}</div>
					{/if}
				</div>
			</div>

			<div class="mt-2 flex flex-col {PANEL}">
				<div class="{PROFILE_SECTION} mb-2 border-b border-muted-foreground/20 pb-2">{m.dialog_user_info_represented_group()}</div>
				{#if tabs.represented}
					<div class="flex items-center gap-2.5">
						<div class="min-w-0 flex-1">
							<div class="truncate text-xs font-medium">
								{#if tabs.represented.ownerId === userId}<span class="mr-1">👑</span>{/if}<a class="hover:underline" href={groupHref(tabs.represented.groupId)}>{tabs.represented.name}</a>
							</div>
							<div class="text-xs text-muted-foreground">({tabs.represented.memberCount})</div>
						</div>
						<Avatar.Root class="size-10! flex-none rounded-lg!">
							<Avatar.Image src={imageSrc(tabs.represented.iconUrl) ?? undefined} />
							<Avatar.Fallback class="rounded-lg!"><ImageIcon class="size-4 text-muted-foreground" /></Avatar.Fallback>
						</Avatar.Root>
					</div>
					{#if tabs.represented.bannerUrl}
						<div class="mt-2">
							<img class="aspect-6/1 h-[80px] w-full rounded-lg object-cover" src={imageSrc(tabs.represented.bannerUrl)} alt="" loading="lazy" />
						</div>
					{/if}
				{:else}
					<div class="text-xs text-muted-foreground">{NONE}</div>
				{/if}
			</div>
		</div>

		<div class="flex min-h-0 min-w-0 flex-1 flex-col pl-2">
			<TabsPrimitive.Root bind:value={activeTab} onValueChange={(tab) => { tabs.tabFilter = ''; tabs.loadTab(tab); if (tab === 'favorite-worlds') tabs.loadFavorites(); }} class="flex min-h-0 w-full min-w-0 flex-1 flex-col">
				<div class={TAB_LIST_WRAP}>
					<TabsPrimitive.List class={TAB_LIST}>
						<TabsPrimitive.Trigger value="info" class={TAB_TRIGGER}>{m.dialog_user_info_header()}</TabsPrimitive.Trigger>
						{#if !isSelf}
							<TabsPrimitive.Trigger value="mutuals" class={TAB_TRIGGER}>{m.dialog_user_tags_mutual_friends()}</TabsPrimitive.Trigger>
						{/if}
						<TabsPrimitive.Trigger value="groups" class={TAB_TRIGGER}>{m.dialog_user_groups_header()}</TabsPrimitive.Trigger>
						<TabsPrimitive.Trigger value="worlds" class={TAB_TRIGGER}>{m.dialog_user_worlds_header()}</TabsPrimitive.Trigger>
						<TabsPrimitive.Trigger value="favorite-worlds" class={TAB_TRIGGER}>{m.dialog_user_favorite_worlds_header()}</TabsPrimitive.Trigger>
						<TabsPrimitive.Trigger value="avatars" class={TAB_TRIGGER}>{m.dialog_user_avatars_header()}</TabsPrimitive.Trigger>
						<TabsPrimitive.Trigger value="activity" class={TAB_TRIGGER}>{m.dialog_user_activity_header()}</TabsPrimitive.Trigger>
						<TabsPrimitive.Trigger value="json" class={TAB_TRIGGER}>{m.dialog_user_json_header()}</TabsPrimitive.Trigger>
					</TabsPrimitive.List>
				</div>

				<TabsPrimitive.Content value="info" class={PADDED_TAB}>
					<UserInfoTab {user} {userId} {isSelf} {instance} {worldThumbnail} {note} {memo} {friend} {together} {actions} {iconStyle} {tabs} oneditnote={editNoteAndMemo} />
				</TabsPrimitive.Content>

				{#each ['mutuals', 'groups', 'worlds', 'avatars'] as tab (tab)}
					<TabsPrimitive.Content value={tab} class={PADDED_TAB}>
						<UserListTab {tab} {tabs} {isSelf} />
					</TabsPrimitive.Content>
				{/each}

				<TabsPrimitive.Content value="favorite-worlds" class={PADDED_TAB}>
					<UserFavoriteWorldsTab {tabs} />
				</TabsPrimitive.Content>

				<TabsPrimitive.Content value="activity" class={PADDED_TAB}>
					<UserHistoryTab {userId} active={activeTab === 'activity'} {history} />
				</TabsPrimitive.Content>

				<TabsPrimitive.Content value="json" class={PADDED_TAB}>
					<UserJsonTab {user} {userId} {isSelf} onrefresh={() => load(userId)} />
				</TabsPrimitive.Content>
			</TabsPrimitive.Root>
		</div>
	</div>

	<Dialog.Root bind:open={editingMemo}>
		<Dialog.Content class={SMALL_DIALOG} showCloseButton={false}>
			<Dialog.Header><Dialog.Title>{m.dialog_user_note_memo_header()}</Dialog.Title></Dialog.Header>
			<!-- Each field follows its User Dialog setting, but with both hidden the dialog shows both. -->
			{@const bothHidden = appearance.hideUserNotes.value && appearance.hideUserMemos.value}
			{#if !appearance.hideUserNotes.value || bothHidden}
				<span class="text-sm">{m.dialog_user_info_note()}</span>
				<textarea
					rows="6"
					maxlength="256"
					bind:value={draftNote}
					placeholder={m.dialog_user_info_note_placeholder()}
					class="w-full resize-none rounded-md border border-input bg-transparent p-2 text-xs outline-none focus-visible:ring-1 focus-visible:ring-ring"
				></textarea>
				<span class="-mt-3 text-right text-xs text-muted-foreground">{draftNote.length}/256</span>
			{/if}
			{#if !appearance.hideUserMemos.value || bothHidden}
				<span class="text-sm">{m.dialog_user_info_memo()}</span>
				<textarea
					rows="6"
					bind:value={draftMemo}
					placeholder={m.dialog_user_info_memo_placeholder()}
					class="w-full resize-none rounded-md border border-input bg-transparent p-2 text-xs outline-none focus-visible:ring-1 focus-visible:ring-ring"
				></textarea>
			{/if}
			{#if actions.error}<p class="text-xs text-destructive">{actions.error}</p>{/if}
			<Dialog.Footer>
				<Button variant="secondary" class="mr-2" onclick={() => (editingMemo = false)}>{m.dialog_user_note_memo_cancel()}</Button>
				<Button disabled={saving === 'memo'} onclick={saveNoteAndMemo}>{m.dialog_user_note_memo_confirm()}</Button>
			</Dialog.Footer>
		</Dialog.Content>
	</Dialog.Root>

	<PreviousInstancesDialog bind:open={previousOpen} kind="user" id={userId} name={user?.displayName} />
	<ActionDialogs bind:confirming={actions.confirming} bind:prompting={actions.prompting} busy={actions.busy} />

	<InviteMessageDialog bind:open={inviteMessageOpen} type="message" send={(slot, photo) => sendInvite(slot, photo)} />
	<InviteMessageDialog bind:open={requestMessageOpen} type="request" send={sendRequestInvite} />
	<ModerationReportDialog bind:target={reporting} />
	<InviteGroupDialog bind:userId={invitingToGroup} userName={user?.displayName} />
	<ModerateGroupDialog bind:userId={moderatingInGroup} userName={user?.displayName} />
	<BoopDialog bind:open={boopOpen} name={user.displayName} onsend={(emoji) => actions.act(() => api.post(`${userPath}/users/${encodedId}/boop`, { emojiId: emoji || null }), 'Boop sent')} />

{/if}
