<script lang="ts" module>
	export type Room = { instanceId?: string; location: string; memberCount?: number; world?: { name?: string } };
</script>

<script lang="ts">
	import { CARD, NONE, PANEL, SECTION } from '#lib/details.js';
	import { m } from '#lib/paraglide/messages.js';
	import { sidebarData } from '#lib/people/sidebar-data.svelte.js';
	import { toast } from 'svelte-sonner';
	import ImageIcon from '@lucide/svelte/icons/image';
	import Pencil from '@lucide/svelte/icons/pencil';
	import Trash2 from '@lucide/svelte/icons/trash-2';
	import PersonRow from '#lib/components/details/person-row.svelte';
	import SectionCard from '#lib/components/details/section-card.svelte';
	import GroupPostMeta from '#lib/components/group/group-post-meta.svelte';
	import InstanceActions from '#lib/components/instance/instance-actions.svelte';
	import InstanceInfo from '#lib/components/instance/instance-info.svelte';
	import Location from '#lib/components/instance/location.svelte';
	import CalendarEventCard from '#lib/components/tools/calendar-event-card.svelte';
	import ViewableImage from '#lib/components/common/viewable-image.svelte';
	import { Button } from '#lib/components/ui/button/index.js';
	import type { Friend } from '#lib/api/types.js';
	import { sortInstanceUsers } from '#lib/settings/appearance.svelte.js';
	import { groupEventEdit, splitCalendar, type Group, type GroupEvent, type GroupPost } from '#lib/groups/group-dialog.svelte.js';

	// The original's GroupDialogInfoTab: the group's instances, description, announcement, events and rules.
	let {
		group,
		groupId,
		rooms,
		friends,
		posts,
		calendar,
		canManagePosts,
		canManageCalendar,
		onfollow,
		onedit,
		ondelete
	}: {
		group: Group;
		groupId: string;
		rooms: Room[];
		friends: Friend[];
		posts: GroupPost[] | undefined;
		calendar: GroupEvent[];
		canManagePosts: boolean;
		canManageCalendar: boolean;
		onfollow: (event: GroupEvent) => void;
		onedit: (post: GroupPost) => void;
		ondelete: (post: GroupPost) => void;
	} = $props();

	let announcementPhotoError = $state(false);

	const announcement = $derived(posts?.[0]);
	const events = $derived(splitCalendar(calendar));

	const friendsIn = (location: string) => sortInstanceUsers(friends.filter((f) => f.location === location));

	// The original's applyGroupDialogInstances: besides the rooms VRChat lists, any of this group's
	// instances a friend is in, and the account's own, which lists the account too.
	const ownLocation = $derived(sidebarData.me?.location ?? '');
	const shownRooms = $derived.by(() => {
		const tag = `~group(${groupId})`;
		const listed = new Set(rooms.map((r) => r.location));
		const extra = [...friends.map((f) => f.location), ownLocation].filter((l): l is string => !!l && l.includes(tag) && !listed.has(l));
		return [...rooms, ...[...new Set(extra)].map((location) => ({ location }) as Room)];
	});
</script>

{#snippet eventCards(list: GroupEvent[], height: string)}
	<div class="flex flex-wrap gap-4 overflow-x-hidden overflow-y-auto py-2.5 {height}">
		{#each list as event (event.id)}
			<CalendarEventCard {event} group={group ? { name: group.name, bannerUrl: group.bannerUrl } : undefined} mode="grid" isFollowing={!!event.userInterest?.isFollowing} onfollow={(e) => onfollow(e as GroupEvent)} onmessage={(item) => toast.success(item)} onedit={canManageCalendar ? () => groupEventEdit.showEdit(event, group) : undefined} />
		{/each}
	</div>
{/snippet}

{#if shownRooms.length}
	<SectionCard title={m.dialog_group_info_instances()} class="{CARD} mb-2.5">
		{#each shownRooms as room (room.location)}
			<div class="mb-4 flex flex-col gap-2">
				<div class="flex flex-wrap items-center gap-2 overflow-hidden whitespace-nowrap">
					<Location location={room.location} hint={room.world?.name ?? ''} excludeGroupName class="rounded-full border px-2 py-0.5 text-sm text-muted-foreground" />
					<InstanceActions location={room.location} friendIds={new Set(friendsIn(room.location).map((f) => f.userId))} class="text-sm" />
					<InstanceInfo location={room.location} friendCount={friendsIn(room.location).length} showRefresh />
					{#if room.memberCount}<span class="text-sm text-muted-foreground">({room.memberCount})</span>{/if}
				</div>
				{#if friendsIn(room.location).length || (sidebarData.me && room.location === ownLocation)}
					<div class="flex flex-wrap items-start overflow-auto" style="max-height: 150px">
						{#if sidebarData.me && room.location === ownLocation}
							<PersonRow userId={sidebarData.me.id} name={sidebarData.me.displayName} image={sidebarData.me.imageUrl} />
						{/if}
						{#each friendsIn(room.location) as friend (friend.userId)}
							<PersonRow userId={friend.userId} name={friend.displayName} image={friend.thumbnailImageUrl} {friend} timed />
						{/each}
					</div>
				{/if}
			</div>
		{/each}
	</SectionCard>
{/if}
<div class="flex flex-col gap-2.5">
	<SectionCard title={m.dialog_group_info_header()}>
		<pre class="max-h-52 overflow-y-auto font-[inherit] text-xs whitespace-pre-wrap">{group.description || NONE}</pre>
	</SectionCard>
	<div class={PANEL}>
		<div class="mb-2 flex items-center justify-between border-b border-border pb-2">
			<span class={SECTION}>{m.dialog_group_info_announcement()}</span>
			{#if announcement && canManagePosts}
				{@const current = announcement}
				<div class="flex gap-1">
					<Button class="h-5 w-5 text-muted-foreground" size="icon-sm" variant="ghost" aria-label={m.dialog_group_posts_edit_tooltip()} onclick={() => onedit(current)}><Pencil class="h-3 w-3" /></Button>
					<Button class="h-5 w-5 text-muted-foreground" size="icon-sm" variant="ghost" aria-label={m.dialog_group_posts_delete_tooltip()} onclick={() => ondelete(current)}><Trash2 class="h-3 w-3" /></Button>
				</div>
			{/if}
		</div>
		{#if announcement}
			{#if announcement.title}<div class="mb-1 text-xs font-medium">{announcement.title}</div>{/if}
			{#if announcement.imageUrl}
				<div class="mb-2">
					{#if !announcementPhotoError}
						<ViewableImage src={announcement.imageUrl} class="size-15 overflow-hidden rounded-md" imageClass="size-15 object-cover" onerror={() => (announcementPhotoError = true)} />
					{:else}
						<div class="flex size-15 items-center justify-center rounded-md bg-muted"><ImageIcon class="size-5 text-muted-foreground" /></div>
					{/if}
				</div>
			{/if}
			<pre class="font-[inherit] text-xs whitespace-pre-wrap">{announcement.text}</pre>
			<div class="mt-2 flex flex-row items-center justify-end gap-2 text-xs text-muted-foreground"><GroupPostMeta entry={announcement} {group} /></div>
		{:else}
			<pre class="font-[inherit] text-xs text-muted-foreground">{NONE}</pre>
		{/if}
	</div>
	{#if events.upcoming.length}
		<SectionCard title={m.dialog_group_info_upcoming_events()}>{@render eventCards(events.upcoming, 'max-h-[360px]')}</SectionCard>
	{/if}
	{#if events.past.length}
		<SectionCard title={m.dialog_group_info_past_events()}>{@render eventCards(events.past, 'max-h-[230px]')}</SectionCard>
	{/if}
	<SectionCard title={m.dialog_group_info_rules()}>
		<pre class="max-h-52 overflow-y-auto font-[inherit] text-xs whitespace-pre-wrap">{group.rules || NONE}</pre>
	</SectionCard>
</div>
