<script lang="ts">
	import { PERSON_NAME, PERSON_SUBTEXT } from '#lib/details.js';
	import PersonAvatar from '#lib/components/common/person-avatar.svelte';
	import { m } from '#lib/paraglide/messages.js';
	import { sidebarSettings } from '#lib/settings/sidebar-settings.svelte.js';
	import Location from '#lib/components/instance/location.svelte';
	import { Spinner } from '#lib/components/ui/spinner/index.js';
	import type { Friend } from '#lib/api/types.js';
	import { iconSrc } from '#lib/api/links.js';
	import { timeToText } from '#lib/helpers/format.js';
	import { userDialog } from '#lib/people/user-dialog.svelte.js';
	import { appearance } from '#lib/settings/appearance.svelte.js';
	import { userColour, userStatusClass } from './sidebar-utils';

	// The original's sidebar FriendItem: picture with status dot, name, then where they are, their
	// time there when grouped by instance, or their status text when not online.
	let { friend, groupByInstance = false, favorite = false, now = Date.now(), style = '' }: { friend: Friend; groupByInstance?: boolean; favorite?: boolean; now?: number; style?: string } = $props();

	const hideNicknames = $derived(appearance.hideNicknames.value);
	const dark = $derived(appearance.isDarkMode);
	const traveling = $derived(friend.location === 'traveling');
	const colour = $derived(userColour(friend, dark));
	const activeOrOffline = $derived(friend.state === 'active' || friend.state === 'offline');
</script>

<div
	class="friend-row relative isolate box-border flex cursor-pointer items-center overflow-hidden rounded-lg p-1.5 text-compact hover:bg-muted/50"
	{style}
	role="button"
	tabindex="0"
	onclick={() => userDialog.open(friend.userId)}
	onkeydown={(e) => e.key === 'Enter' && userDialog.open(friend.userId)}
>
	<PersonAvatar src={iconSrc(friend.thumbnailImageUrl)} statusClass={userStatusClass(friend, friend.pendingOffline)} iconFrame={friend.iconFrame} frameEnabled={sidebarSettings.cosmetics.value} fallbackClass="size-5" />
	<div class="flex h-9 flex-1 flex-col justify-between overflow-hidden">
		{#if !hideNicknames && friend.nickname}
			<span class={PERSON_NAME} style="color: {colour}">{friend.displayName} ({friend.nickname})</span>
		{:else}
			<span class={PERSON_NAME} style="color: {colour}">{friend.displayName}{groupByInstance && favorite ? ' ⭐' : ''}</span>
		{/if}
		{#if activeOrOffline}
			<span class={PERSON_SUBTEXT}>{friend.statusDescription}</span>
		{:else if friend.pendingOffline}
			<div class="extra block truncate text-xs">{m.side_panel_pending_offline()}</div>
		{:else if groupByInstance}
			<div class="flex items-center text-xs">
				{#if traveling}<Spinner class="mr-1" />{/if}
				<span>{friend.locationSince ? timeToText(Math.max(0, now - Date.parse(friend.locationSince))) : ''}</span>
			</div>
		{:else}
			<Location class="extra block truncate text-xs" location={friend.location} traveling={friend.travelingToLocation ?? undefined} link={false} />
		{/if}
	</div>
</div>
