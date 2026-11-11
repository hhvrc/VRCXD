<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import CalendarIcon from '@lucide/svelte/icons/calendar';
	import Download from '@lucide/svelte/icons/download';
	import { toast } from 'svelte-sonner';
	import { Button } from '#lib/components/ui/button/index.js';
	import { api, ApiError, getServerToken } from '#lib/api/client.js';
	import { agentCan } from '#lib/instances/instance-launch.svelte.js';
	import { formatDateFilter, fromNow } from '#lib/helpers/format.js';
	import type { CalendarEvent } from '#lib/nav/tools.js';
	import { ownPath } from '#lib/api/vrchat-auth.svelte.js';

	// The original's GroupCalendarEventDetails: title, time (relative in the instance bar), Export to
	// Calendar (the PC's calendar app, through its agent), .ics download,
	// category, interest, close time, created date and description.
	let { event, relativeTime = false, onmessage }: { event: CalendarEvent; relativeTime?: boolean; onmessage?: (message: string) => void } = $props();

	const capitalize = (text: string | null) => (text ? text.charAt(0).toUpperCase() + text.slice(1) : '');
	const range = $derived(
		relativeTime
			? `${fromNow(event.startsAt)} - ${fromNow(event.endsAt)}`
			: `${formatDateFilter(event.startsAt, 'time')} - ${formatDateFilter(event.endsAt, 'time')}`
	);

	// openCalendarEvent: the server fetches the .ics and the PC's agent opens it.
	async function openOnPc() {
		try {
			await api.post<void>(ownPath(`/calendar/${encodeURIComponent(event.ownerId)}/${encodeURIComponent(event.id)}/open-on-pc`));
		} catch (err) {
			const message = `Failed to download .ics file, ${ApiError.from(err).message}`;
			if (onmessage) onmessage(message);
			else toast.error(message);
		}
	}

	async function downloadIcs() {
		try {
			const path = ownPath(`/calendar/${encodeURIComponent(event.ownerId)}/${encodeURIComponent(event.id)}/ics`);
			const response = await fetch(path, { headers: { VrcxToken: getServerToken() } });
			if (!response.ok) throw new Error(`${response.status} ${response.statusText}`);
			const href = URL.createObjectURL(await response.blob());
			const link = document.createElement('a');
			link.href = href;
			link.download = `${event.id}.ics`;
			document.body.appendChild(link);
			link.click();
			link.remove();
			URL.revokeObjectURL(href);
		} catch (err) {
			const message = `Failed to download .ics file, ${ApiError.from(err).message}`;
			if (onmessage) onmessage(message);
			else toast.error(message);
		}
	}
</script>

<div class="flex items-baseline justify-between gap-3 text-xs">
	<div class="text-compact font-semibold">{event.title}</div>
	<div class="whitespace-nowrap">{range}</div>
</div>
<div class="mt-2 grid grid-cols-2 gap-x-3 gap-y-2 text-xs">
	{#if agentCan('calendar')}
		<div class="flex min-w-0 flex-col gap-1">
			<Button variant="outline" size="sm" onclick={openOnPc}><CalendarIcon class="size-4" />{m.dialog_group_calendar_event_card_export_to_calendar()}</Button>
		</div>
		<div class="flex min-w-0 flex-col gap-1">
			<Button variant="outline" size="sm" onclick={downloadIcs}><Download class="size-4" />{m.dialog_group_calendar_event_card_download_ics()}</Button>
		</div>
	{:else}
		<div class="col-span-2">
			<Button variant="outline" size="sm" class="w-full" onclick={downloadIcs}><Download class="size-4" /><CalendarIcon class="size-4" />{m.dialog_group_calendar_event_card_download_ics()}</Button>
		</div>
	{/if}
	<div class="flex min-w-0 flex-col gap-1">
		<div>{m.dialog_group_event_edit_category()}</div>
		<div class="font-medium">{capitalize(event.category)}</div>
	</div>
	<div class="flex min-w-0 flex-col gap-1">
		<div>{m.dialog_group_calendar_event_card_interested_user()}</div>
		<div class="font-medium">{event.interestedUserCount}</div>
	</div>
	<div class="flex min-w-0 flex-col gap-1">
		<div>{m.dialog_group_calendar_event_card_close_time()}</div>
		<div class="font-medium">{event.closeInstanceAfterEndMinutes} min</div>
	</div>
	<div class="flex min-w-0 flex-col gap-1">
		<div>{m.dialog_group_calendar_event_card_created()}</div>
		<div class="font-medium">{formatDateFilter(event.createdAt, 'long')}</div>
	</div>
	<div class="col-span-2 flex min-w-0 flex-col gap-1">
		<div>{m.dialog_world_info_description()}</div>
		<div class="leading-snug font-normal wrap-break-word whitespace-pre-wrap">{event.description}</div>
	</div>
</div>
