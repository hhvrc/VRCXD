<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import ViewableImage from '#lib/components/common/viewable-image.svelte';
	import CalendarEventDetails from '#lib/components/tools/calendar-event-details.svelte';
	import { groupHref, vrchatUrl } from '#lib/api/links.js';
	import Pencil from '@lucide/svelte/icons/pencil';
	import ImageIcon from '@lucide/svelte/icons/image';
	import Repeat from '@lucide/svelte/icons/repeat';
	import Share2 from '@lucide/svelte/icons/share-2';
	import Star from '@lucide/svelte/icons/star';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as Card from '#lib/components/ui/card/index.js';
	import * as HoverCard from '#lib/components/ui/hover-card/index.js';
	import { formatDateFilter } from '#lib/helpers/format.js';
	import type { CalendarEvent, CalendarGroup } from '#lib/nav/tools.js';

	// The original's GroupCalendarEventCard and GroupCalendarEventDetails: banner, title, time and
	// access, share and follow buttons, and the details on hover.

	let {
		event,
		group,
		mode,
		isFollowing = false,
		onfollow,
		onmessage,
		onedit
	}: {
		event: CalendarEvent;
		group?: CalendarGroup;
		mode: 'timeline' | 'grid';
		isFollowing?: boolean;
		onfollow: (event: CalendarEvent) => void;
		onmessage: (message: string) => void;
		/** Shown only with group-calendar-manage in the event's group, as the original's canManageEvent. */
		onedit?: (event: CalendarEvent) => void;
	} = $props();

	let bannerError = $state(false);
	const bannerUrl = $derived(event.imageUrl || group?.bannerUrl || '');
	const capitalize = (text: string | null) => (text ? text.charAt(0).toUpperCase() + text.slice(1) : '');
	const time = $derived(
		mode === 'timeline'
			? `${formatDateFilter(event.startsAt, 'time')} - ${formatDateFilter(event.endsAt, 'time')}`
			: `${formatDateFilter(event.startsAt, 'short')} - ${formatDateFilter(event.endsAt, 'short')}`
	);

	async function copyLink() {
		await navigator.clipboard.writeText(`${vrchatUrl('group', event.ownerId)}/calendar/${event.id}`);
		onmessage('Calendar event URL copied to clipboard');
	}
</script>

<HoverCard.Root openDelay={200} closeDelay={100}>
	<HoverCard.Trigger>
		{#snippet child({ props })}
			<div {...props}>
				<Card.Root class="group relative w-56 gap-0 overflow-hidden p-0 hover:bg-accent hover:shadow-sm">
					{#if bannerUrl && !bannerError}
						<ViewableImage src={bannerUrl} imageClass="h-24 w-full object-cover" onerror={() => (bannerError = true)} />
					{:else}
						<div class="flex h-24 items-center justify-center bg-muted"><ImageIcon class="size-6 text-muted-foreground" /></div>
					{/if}
					<div class="flex flex-col gap-1 p-2 text-xs">
						{#if mode === 'timeline'}<a class="block truncate text-muted-foreground hover:underline" href={groupHref(event.ownerId)}>{group?.name}</a>{/if}
						<div class="line-clamp-2 text-sm font-medium">
							{#if event.seriesId}<span title={m.dialog_group_calendar_event_card_repeating_event_tooltip()}><Repeat class="inline size-4" /></span>{/if}
							{event.title}
						</div>
						<div class="flex items-center justify-between gap-2 text-muted-foreground">
							<span class={mode === 'grid' ? 'text-2xs' : ''}>{time}</span>
							<span>{capitalize(event.accessType)}</span>
						</div>
					</div>
					<div class="absolute top-1 right-1 flex gap-1">
						{#if onedit}
							<Button size="icon-xs" variant="secondary" class="rounded-full" aria-label={m.dialog_group_event_edit_edit_header()} onclick={() => onedit(event)}><Pencil class="size-3" /></Button>
						{/if}
						<Button size="icon-xs" variant="secondary" class="rounded-full" aria-label="Copy event link" onclick={copyLink}><Share2 class="size-3" /></Button>
						<Button size="icon-xs" variant={isFollowing ? 'default' : 'secondary'} class="rounded-full" aria-label={isFollowing ? 'Stop following' : 'Follow'} onclick={() => onfollow(event)}>
							<Star class="size-3" fill={isFollowing ? 'currentColor' : 'none'} />
						</Button>
					</div>
				</Card.Root>
			</div>
		{/snippet}
	</HoverCard.Trigger>
	<HoverCard.Content side="right" align="start" class="w-125 p-3">
		<CalendarEventDetails {event} {onmessage} />
	</HoverCard.Content>
</HoverCard.Root>
