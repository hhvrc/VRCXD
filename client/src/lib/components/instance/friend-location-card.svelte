<script lang="ts">
	import IconFrame from '#lib/components/cosmetics/icon-frame.svelte';
	import ProfileEffect from '#lib/components/cosmetics/profile-effect.svelte';
	import Pencil from '@lucide/svelte/icons/pencil';
	import UserIcon from '@lucide/svelte/icons/user';
	import * as Avatar from '#lib/components/ui/avatar/index.js';
	import LocationMenu from '#lib/components/instance/location-menu.svelte';
	import UserContextMenu from '#lib/components/common/user-context-menu.svelte';
	import { iconSrc } from '#lib/api/links.js';
	import type { Friend } from '#lib/api/types.js';
	import { userDialog } from '#lib/people/user-dialog.svelte.js';

	// The original's FriendsLocationsCard: picture, name, status dot, status text and where they are.
	let { friend, scale = 1, spacing = 1, showLocation = true, showCosmetics = true }: { friend: Friend; scale?: number; spacing?: number; showLocation?: boolean; showCosmetics?: boolean } = $props();

	const dot = $derived.by(() => {
		// A friend VRChat reported offline a moment ago shows offline, as the original's userStatusClass.
		if (friend.pendingOffline) return 'offline';
		if (friend.state === 'active') {
			if (friend.status === 'join me') return 'active-joinme';
			if (friend.status === 'ask me') return 'active-askme';
			if (friend.status === 'busy') return 'active-busy';
			return 'active';
		}
		if (friend.state !== 'online' || friend.location === 'offline') return 'offline';
		if (friend.status === 'active') return 'online';
		if (friend.status === 'join me') return 'joinme';
		if (friend.status === 'ask me') return 'askme';
		if (friend.status === 'busy') return 'busy';
		return 'hidden';
	});
</script>

<UserContextMenu userId={friend.userId} name={friend.displayName} state={friend.state} location={friend.location ?? ''}>
	<div
		role="button"
		tabindex="0"
		class="friend-card relative block cursor-pointer rounded-xl border bg-card text-card-foreground shadow-sm transition-[background-color,box-shadow] duration-200 hover:bg-muted"
		style="--card-scale: {scale}; --card-spacing: {spacing}; padding: {8 * scale}px; padding-bottom: {6 * scale}px"
		onclick={() => userDialog.open(friend.userId)}
		onkeydown={(event) => {
			if (event.key === 'Enter' && event.target === event.currentTarget) userDialog.open(friend.userId);
		}}
	>
		<ProfileEffect enabled={showCosmetics} profileEffect={friend.profileEffect} class="rounded-lg object-cover" />
		<div class="friend-card__header mb-1.75 grid items-center">
			<div class="relative mr-2.5 inline-block size-9 flex-none">
				<Avatar.Root class="size-full rounded-full">
					<Avatar.Image src={iconSrc(friend.thumbnailImageUrl)} class="object-cover" />
					<Avatar.Fallback><UserIcon class="text-muted-foreground" size={Math.max(16, 20 * scale)} /></Avatar.Fallback>
				</Avatar.Root>
				<IconFrame enabled={showCosmetics} iconFrame={friend.iconFrame} />
			</div>
			<span class={['friend-card__dot pointer-events-none absolute rounded-full', `friend-card__dot--${dot}`]}></span>
			<div class="friend-card__name relative overflow-hidden font-semibold text-ellipsis whitespace-nowrap" title={friend.displayName}>{friend.displayName}</div>
		</div>
		<div class="friend-card__body grid">
			<div class="friend-card__signature flex items-center overflow-hidden text-ellipsis whitespace-nowrap text-muted-foreground" title={friend.statusDescription}>
				{#if friend.statusDescription}<Pencil class="mr-0.5 h-3.5 w-3.5 shrink-0" style="opacity: 0.7" />{/if}
				<span class="truncate">{friend.statusDescription || ' '}</span>
			</div>
			{#if showLocation}
				<!-- svelte-ignore a11y_click_events_have_key_events, a11y_no_static_element_interactions -->
				<div class="friend-card__world box-border flex max-w-full min-w-0 items-center justify-start overflow-hidden" onclick={(event) => event.stopPropagation()}>
					<LocationMenu class="friend-card__location flex w-full overflow-hidden text-center wrap-break-word" location={friend.location} traveling={friend.travelingToLocation ?? undefined} />
				</div>
			{/if}
		</div>
	</div>
</UserContextMenu>

<style>
	.friend-card {
		max-width: var(--friend-card-target-width, 220px);
		min-width: var(--friend-card-min-width, 220px);
	}
	.friend-card__header {
		grid-template-columns: auto minmax(0, 1fr);
		gap: calc(10px * var(--card-scale) * var(--card-spacing));
		padding-top: calc(3px * var(--card-scale));
		padding-left: calc(4px * var(--card-scale));
	}
	.friend-card__dot {
		top: calc(16px * var(--card-scale));
		right: calc(16px * var(--card-scale));
		inline-size: calc(12px * var(--card-scale));
		block-size: calc(12px * var(--card-scale));
	}
	.friend-card__dot--hidden {
		display: none;
	}
	.friend-card__dot--online {
		background: var(--status-online);
		box-shadow: 0 0 calc(8px * var(--card-scale)) color-mix(in oklch, var(--status-online) 40%, transparent);
	}
	.friend-card__dot--active {
		border: calc(2px * var(--card-scale)) solid var(--status-online);
		box-shadow: 0 0 calc(8px * var(--card-scale)) color-mix(in oklch, var(--status-online) 40%, transparent);
	}
	.friend-card__dot--active-joinme {
		border: calc(2px * var(--card-scale)) solid var(--status-joinme);
		box-shadow: 0 0 calc(8px * var(--card-scale)) color-mix(in oklch, var(--status-joinme) 40%, transparent);
	}
	.friend-card__dot--active-askme {
		border: calc(2px * var(--card-scale)) solid var(--status-askme);
		box-shadow: 0 0 calc(8px * var(--card-scale)) color-mix(in oklch, var(--status-askme) 40%, transparent);
	}
	.friend-card__dot--active-busy {
		border: calc(2px * var(--card-scale)) solid var(--status-busy);
		box-shadow: 0 0 calc(8px * var(--card-scale)) color-mix(in oklch, var(--status-busy) 40%, transparent);
	}
	.friend-card__dot--joinme {
		background: var(--status-joinme);
		box-shadow: 0 0 calc(8px * var(--card-scale)) color-mix(in oklch, var(--status-joinme) 40%, transparent);
	}
	.friend-card__dot--busy {
		background: var(--status-busy);
		box-shadow: 0 0 calc(8px * var(--card-scale)) color-mix(in oklch, var(--status-busy) 40%, transparent);
	}
	.friend-card__dot--askme {
		background: var(--status-askme);
		box-shadow: 0 0 calc(8px * var(--card-scale)) color-mix(in oklch, var(--status-askme) 40%, transparent);
	}
	.friend-card__dot--offline {
		background: var(--status-offline);
	}
	.friend-card__body {
		gap: calc(8px * var(--card-scale) * var(--card-spacing));
	}
	.friend-card__name {
		font-size: calc(13px * var(--card-scale));
	}
	.friend-card__signature {
		font-size: calc(12px * var(--card-scale));
		padding: calc(7px * var(--card-scale)) calc(8px * var(--card-scale));
		line-height: 1.4;
		gap: calc(4px * var(--card-scale));
	}
	.friend-card__world {
		min-height: calc(24px * var(--card-scale));
		padding: calc(7px * var(--card-scale)) calc(8px * var(--card-scale));
		border-radius: calc(var(--radius-lg) * var(--card-scale));
		font-size: calc(12px * var(--card-scale));
		line-height: 1.3;
	}
</style>
