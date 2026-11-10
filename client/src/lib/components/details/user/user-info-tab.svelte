<script lang="ts" module>
	import type { FriendLogDate } from '#lib/people/user-display.js';

	/** GET .../users/{id}/together */
	export type Together = { joinCount: number; timeTogetherMs: number; lastSeen?: string | null; friendLog: FriendLogDate[] };
</script>

<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import PersonRow from '#lib/components/details/person-row.svelte';
	import CopyIdRow from '#lib/components/details/copy-id-row.svelte';
	import InfoRow from '#lib/components/details/info-row.svelte';
	import LocationWorld from '#lib/components/instance/location-world.svelte';
	import InstanceActions from '#lib/components/instance/instance-actions.svelte';
	import InstanceInfo from '#lib/components/instance/instance-info.svelte';
	import { instanceCreator } from '#lib/instances/instance-launch.svelte.js';
	import { parseLocation } from '#lib/instances/location.js';
	import { sidebarData } from '#lib/people/sidebar-data.svelte.js';
	import { cosmetics } from '#lib/settings/cosmetics.svelte.js';
	import { userNames, worldNames } from '#lib/people/names.svelte.js';
	import { userDialog } from '#lib/people/user-dialog.svelte.js';
	import { translation } from '#lib/settings/translation.svelte.js';
	import { appearance } from '#lib/settings/appearance.svelte.js';
	import { editProfile } from '#lib/content/edit-profile.svelte.js';
	import { credits, refreshCredits } from '#lib/content/credits.svelte.js';
	import { openExternalLink } from '#lib/nav/external-link.svelte.js';
	import { INFO_ROW, NONE, PANEL, PROFILE_SECTION, SECTION_ROW } from '#lib/details.js';
	import type { DetailActions } from '#lib/helpers/detail-actions.svelte.js';
	import { accountPath, api } from '#lib/api/client.js';
	import { imageSrc } from '#lib/api/links.js';
	import { vrchatAuth } from '#lib/api/vrchat-auth.svelte.js';
	import { formatDate, formatDateFilter, getFaviconUrl, replaceBioSymbols, timeAgo, timeToText } from '#lib/helpers/format.js';
	import { clock } from '#lib/helpers/clock.svelte.js';
	import { friendedInfo, userOnlineFor } from '#lib/people/user-display.js';
	import { tableSettings } from '#lib/settings/table-settings.svelte.js';
	import type { Friend, Instance, User } from '#lib/api/types.js';
	import Info from '@lucide/svelte/icons/info';
	import Languages from '@lucide/svelte/icons/languages';
	import LinkIcon from '@lucide/svelte/icons/link';
	import Pencil from '@lucide/svelte/icons/pencil';
	import Trash2 from '@lucide/svelte/icons/trash-2';
	import { Button } from '#lib/components/ui/button/index.js';
	import { Spinner } from '#lib/components/ui/spinner/index.js';
	import type { UserTabs } from './user-tabs.svelte.js';

	// The original's UserDialogInfoTab: the current instance, bio, note and memo, and the VRCX and
	// VRChat info cards, with your own account's options and home location on your own page.
	let {
		user,
		userId,
		isSelf,
		instance,
		worldThumbnail,
		note,
		memo,
		friend,
		together,
		actions,
		iconStyle,
		tabs,
		oneditnote
	}: {
		user: User;
		userId: string;
		isSelf: boolean;
		instance: Instance | undefined;
		worldThumbnail: string | undefined;
		note: string;
		memo: string;
		friend: Friend | undefined;
		together: Together | undefined;
		actions: DetailActions;
		iconStyle: string;
		tabs: UserTabs;
		oneditnote: () => void;
	} = $props();

	const where = $derived(parseLocation(user?.location ?? ''));
	const isRealLocation = $derived(where.isRealInstance);
	const showInstance = $derived(user?.state === 'online' || isSelf);
	// Online For / Offline For: from the friend list for a friend, else from the user's presence, ticking live.
	const onlineFor = $derived(userOnlineFor(user?.state, friend?.onlineFor ?? user?.stateSince));
	const now = $derived(clock(1000).now);
	// Friended or Unfriended: the newest of the friend log's dates, with all of them on hover when more than one.
	const friended = $derived(friendedInfo(together?.friendLog, tableSettings.hideUnfriends));
	const friendedTitle = $derived(
		friended.entries.length === 1 ? formatDateFilter(friended.at, 'long') : friended.entries.length > 1 ? friended.entries.map((e) => `${e.type}: ${formatDateFilter(e.createdAt, 'long')}`).join('\n') : undefined
	);

	// The original's resetHome: clears the account's home world after a confirm.
	function resetHome() {
		const ownerUserId = vrchatAuth.ownerUserId;
		if (!ownerUserId) return;
		actions.confirming = {
			command: 'Reset Home',
			get title() { return m.dialog_user_note_memo_confirm(); },
			run: () =>
				actions.act(async () => {
					await api.delete(accountPath(ownerUserId, '/home'));
					await sidebarData.loadMe();
				}, 'Home location reset')
		};
	}
</script>

{#snippet sectionTitle(label: string)}
	<span class={PROFILE_SECTION}>{label}</span>
{/snippet}

{#if showInstance}
	<div class="mb-2.5 flex flex-col gap-2.5">
		<div class={PANEL}>
			<div class="mb-2 flex items-center justify-between border-b border-muted-foreground/20 pb-1">
				{@render sectionTitle('Current Instance')}
			</div>
			{#if !user.location || where.isOffline}
				<span class="text-sm text-muted-foreground">{m.dialog_user_status_offline()}</span>
			{:else if where.isPrivate}
				<span class="text-sm text-muted-foreground">{m.dialog_user_avatars_private()}</span>
			{:else if where.isTraveling}
				<span class="text-sm text-muted-foreground">{m.location_traveling()}</span>
			{:else if isRealLocation}
				<div class="flex items-start justify-between gap-1.5">
					<div class="flex min-w-0 flex-1 flex-col justify-between gap-1.5">
						<button type="button" class="block cursor-pointer truncate text-left text-foreground" title={instance?.worldName} onclick={() => user?.location && userDialog.openWorld(user.location)}>{instance?.worldName ?? worldNames.get(user.location?.split(':')[0]) ?? user.location}</button>
						{#if instance}
							<div class="flex min-w-0 flex-wrap items-start gap-1.5">
								<LocationWorld class="w-fit max-w-full text-sm" location={user?.location ?? ''} currentUserId={vrchatAuth.ownerUserId} displayName={instance.name} />
								<InstanceActions location={user?.location ?? ''} />
								<InstanceInfo location={user?.location ?? ''} showRefresh friendCount={new Set(instance.present.map((p) => p.userId).filter((id) => sidebarData.friends.some((f) => f.userId === id))).size} />
							</div>
						{/if}
					</div>
					{#if worldThumbnail}
						<img src={imageSrc(worldThumbnail)} alt="" style="width: 80px; height: 60px; border-radius: var(--radius-xl)" loading="lazy" />
					{/if}
				</div>
				{@const creatorId = instanceCreator(user?.location ?? '', where.userId)}
				{#if creatorId}
					{@const creator = sidebarData.friends.find((f) => f.userId === creatorId)}
					<div class="flex flex-wrap items-start">
						<PersonRow userId={creatorId} name={creator?.displayName ?? userNames.get(creatorId) ?? creatorId} image={creator?.thumbnailImageUrl} friend={creator} detail="Instance Creator" />
					</div>
				{/if}
				{#if instance?.present.length}
					<div class="flex flex-wrap items-start" style="flex: 1; max-height: 150px; overflow: auto">
						{#each instance.present as person, index (`${person.userId}:${index}`)}
							{@const known = sidebarData.friends.find((f) => f.userId === person.userId)}
							<PersonRow userId={person.userId} name={person.displayName} image={known?.thumbnailImageUrl} friend={known} since={person.joinedAt} />
						{/each}
					</div>
				{/if}
			{/if}
		</div>
	</div>
{/if}

<div class="@container">
	<div class="grid grid-cols-1 gap-2.5 @[560px]:grid-cols-[minmax(0,1fr)_230px]" style="align-items: start">
		<div class="flex flex-col gap-2.5">
			<div class={PANEL}>
				<div class={SECTION_ROW}>
					{@render sectionTitle('Bio')}
					<div class="flex items-center gap-1">
						{#if translation.enabled && user.bio}
							<Button class="h-5 w-5" size="icon-sm" variant="ghost" aria-label="Translate" onclick={() => tabs.translateBio(user?.bio ?? '')}>
								{#if tabs.translating}<Spinner class="size-3" />{:else}<Languages class="h-3 w-3 text-(--profile-icon)" />{/if}
							</Button>
						{/if}
						{#if isSelf}
							<Button class="h-5 w-5" size="icon-sm" variant="ghost" aria-label={m.dialog_user_actions_edit_profile()} onclick={() => editProfile.show()}><Pencil class="h-3 w-3 text-(--profile-icon)" /></Button>
						{/if}
					</div>
				</div>
				<pre class="font-[inherit] text-xs" style="white-space: pre-wrap; max-height: 210px; overflow-y: auto">{(tabs.translated.userId === userId && tabs.translated.text) || replaceBioSymbols(user.bio) || NONE}</pre>
				{#if user.bioLinks.length > 0}
					<div class="mt-2 flex flex-wrap items-center gap-1.5">
						{#each user.bioLinks as link (link)}
							<a href={link} target="_blank" rel="noreferrer" title={link} onclick={(event) => { event.preventDefault(); event.stopPropagation(); openExternalLink(link); }}>
								{#if getFaviconUrl(link) && !tabs.brokenFavicons.has(link)}
									<img src={getFaviconUrl(link)} alt="" style="width: 16px; height: 16px; vertical-align: middle; cursor: pointer" loading="lazy" onerror={() => (tabs.brokenFavicons = new Set(tabs.brokenFavicons).add(link))} />
								{:else}
									<LinkIcon class="size-4" />
								{/if}
							</a>
						{/each}
					</div>
				{/if}
			</div>
			{#if !appearance.hideUserNotes.value}
				<button type="button" class="cursor-pointer text-left {PANEL}" onclick={oneditnote}>
					<div class={SECTION_ROW}>
						{@render sectionTitle('Note')}
						<span class="inline-flex h-5 w-5 items-center justify-center"><Pencil class="h-3 w-3 text-(--profile-icon)" /></span>
					</div>
					{#if note}
						<pre class="font-[inherit] text-xs" style="white-space: pre-wrap; max-height: 210px; overflow-y: auto">{note}</pre>
					{:else}
						<pre class="font-[inherit] text-xs text-muted-foreground">{NONE}</pre>
					{/if}
				</button>
			{/if}
			{#if !appearance.hideUserMemos.value}
				<button type="button" class="cursor-pointer text-left {PANEL}" onclick={oneditnote}>
					<div class={SECTION_ROW}>
						{@render sectionTitle('Memo')}
						<span class="inline-flex h-5 w-5 items-center justify-center"><Pencil class="h-3 w-3 text-(--profile-icon)" /></span>
					</div>
					{#if memo}
						<pre class="font-[inherit] text-xs" style="white-space: pre-wrap; max-height: 210px; overflow-y: auto">{memo}</pre>
					{:else}
						<pre class="font-[inherit] text-xs text-muted-foreground">{NONE}</pre>
					{/if}
				</button>
			{/if}
		</div>

		<div class="flex flex-col gap-2.5">
			<div class={PANEL}>
				<div class={SECTION_ROW}>
					<span class="{PROFILE_SECTION} inline-flex items-center gap-1">{m.dialog_user_info_vrcx_info()} <span title="Tracked by this server"><Info class="h-3 w-3 shrink-0" /></span></span>
				</div>
				<div class="flex flex-col gap-1.5">
					<InfoRow label={onlineFor.online ? m.dialog_user_info_online_for() : m.dialog_user_info_offline_for()} value={timeAgo(onlineFor.at, now)} title={onlineFor.at ? formatDateFilter(onlineFor.at, 'long') : undefined} />
					{#if !isSelf}
						<InfoRow label={m.dialog_user_info_last_seen()} value={timeAgo(together?.lastSeen)} title={together?.lastSeen ? formatDateFilter(together.lastSeen, 'long') : undefined} />
						<InfoRow label={friended.unfriended ? m.dialog_user_info_unfriended() : m.dialog_user_info_friended()} value={timeAgo(friended.at)} title={friendedTitle} />
						<InfoRow label={m.dialog_user_info_time_together()} value={together?.timeTogetherMs ? timeToText(together.timeTogetherMs) : NONE} title={together?.timeTogetherMs ? timeToText(together.timeTogetherMs, true) : undefined} />
						<InfoRow label={m.dialog_user_info_join_count()} value={together?.joinCount ? String(together.joinCount) : NONE} />
					{/if}
				</div>
			</div>
			<div class={PANEL}>
				<div class="{PROFILE_SECTION} mb-2 border-b border-muted-foreground/20 pb-2">{m.dialog_user_info_header()}</div>
				<div class="flex flex-col gap-1.5">
					<InfoRow label={m.dialog_user_info_last_activity()} value={timeAgo(user.lastActivity)} title={`Last login ${formatDate(user.lastLogin)}`} />
					<InfoRow label={m.dialog_user_info_date_joined()} value={timeAgo(user.dateJoined)} title={user.dateJoined} />
					{#if isSelf}
						<button type="button" class="{INFO_ROW} w-full cursor-pointer text-left hover:text-foreground" title={m.dialog_user_actions_refresh()} onclick={() => refreshCredits(userId)}>
							<span class="shrink-0 text-muted-foreground">{m.view_profile_profile_vrchat_credits()}</span>
							<span class="text-right text-muted-foreground">{credits.get(userId) ?? 'Refresh'}</span>
						</button>
					{/if}
					{#if !isSelf}
						<InfoRow label={m.dialog_user_info_avatar_cloning()} value={user.allowAvatarCopying ? 'Enabled' : 'Disabled'} />
					{:else if sidebarData.me}
						{@const me = sidebarData.me}
						{@const options = [
							{ get label() { return m.dialog_user_info_avatar_cloning(); }, on: !!me.allowAvatarCopying, save: (on: boolean) => sidebarData.saveOptions({ allowAvatarCopying: on }) },
							{ get label() { return m.dialog_user_info_booping(); }, on: me.isBoopingEnabled, save: (on: boolean) => sidebarData.saveOptions({ isBoopingEnabled: on }) },
							{ get label() { return m.dialog_user_info_show_mutual_friends(); }, on: !me.hasSharedConnectionsOptOut, save: (on: boolean) => sidebarData.saveOptions({ hasSharedConnectionsOptOut: !on }) },
							{ get label() { return m.dialog_user_info_show_discord_connections(); }, on: !me.hasDiscordFriendsOptOut, save: (on: boolean) => sidebarData.saveOptions({ hasDiscordFriendsOptOut: !on }) },
							{ get label() { return m.dialog_user_info_reduced_profile_animations(); }, on: cosmetics.reduceAnimations, save: (on: boolean) => cosmetics.setReduceAnimations(userId, on) }
						]}
						<div class="mt-0.5 flex flex-col gap-1.5 border-t border-muted-foreground/20 pt-1.5">
							{#each options as option (option.label)}
								<button
									type="button"
									class="{INFO_ROW} w-full cursor-pointer text-left hover:text-foreground"
									onclick={() => actions.confirm(`${option.on ? 'disable' : 'enable'} ${option.label}`, () => option.save(!option.on), undefined, false)}
								>
									<span class="shrink-0 text-muted-foreground">{option.label}</span>
									<span class="text-right text-muted-foreground">{option.on ? 'Enabled' : 'Disabled'}</span>
								</button>
							{/each}
						</div>
					{/if}
					<CopyIdRow label={m.dialog_user_info_id()} oncopy={() => actions.copy(userId, 'User ID copied to clipboard')} />
				</div>
			</div>
			{#if isSelf && sidebarData.me?.homeLocation}
				{@const home = sidebarData.me.homeLocation}
				<div class={PANEL}>
					<div class="{PROFILE_SECTION} mb-2 border-b border-muted-foreground/20 pb-2">Home Location</div>
					<!-- svelte-ignore a11y_no_static_element_interactions, a11y_click_events_have_key_events -->
					<div class="flex cursor-pointer items-center justify-between gap-2 text-xs" onclick={() => userDialog.openWorld(home)}>
						<span class="truncate">{worldNames.get(home.split(':')[0]) ?? home}</span>
						<Button class="size-5 flex-none rounded-full" size="icon-sm" variant="ghost" aria-label={m.dialog_user_actions_reset_home()} onclick={(e) => { e.stopPropagation(); resetHome(); }}>
							<Trash2 class="size-3" style={iconStyle} />
						</Button>
					</div>
				</div>
			{/if}
		</div>
	</div>
</div>
