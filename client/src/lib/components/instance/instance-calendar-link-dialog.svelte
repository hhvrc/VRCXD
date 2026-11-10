<script lang="ts">
	import { toastError } from '#lib/helpers/feedback.js';
	import { m } from '#lib/paraglide/messages.js';
	import Loader2 from '@lucide/svelte/icons/loader-2';
	import { toast } from 'svelte-sonner';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as Dialog from '#lib/components/ui/dialog/index.js';
	import * as Select from '#lib/components/ui/select/index.js';
	import { api } from '#lib/api/client.js';
	import { parseLocation } from '#lib/instances/location.js';
	import { ownPath } from '#lib/api/vrchat-auth.svelte.js';

	// The original's InstanceCalendarLinkDialog: link one of the group's events that starts within the
	// next 6 hours or ended within the last 6, or remove the link.
	type CalendarEvent = { id: string; title: string; description?: string; startsAt: string; endsAt: string };
	const LINK_WINDOW_MS = 6 * 60 * 60 * 1000;

	let { open = $bindable(false), location, currentEventId = '', onchanged }: { open?: boolean; location: string; currentEventId?: string; onchanged?: () => void } = $props();

	let events = $state<CalendarEvent[]>([]);
	let selected = $state('');
	let fetching = $state(false);
	let loading = $state(false);

	const linkable = $derived.by(() => {
		const now = Date.now();
		return events
			.filter((e) => new Date(e.startsAt).getTime() <= now + LINK_WINDOW_MS && new Date(e.endsAt).getTime() >= now - LINK_WINDOW_MS)
			.sort((a, b) => a.startsAt.localeCompare(b.startsAt));
	});
	const selectedEvent = $derived(linkable.find((e) => e.id === selected));
	const canAssign = $derived(!loading && !!selectedEvent && selected !== currentEventId);

	$effect(() => {
		if (open) {
			selected = currentEventId;
			void fetchEvents();
		}
	});

	function setOpen(value: boolean) {
		if (!loading) open = value;
	}

	async function fetchEvents() {
		const groupId = parseLocation(location).groupId;
		if (!groupId) return;
		fetching = true;
		try {
			events = await api.get<CalendarEvent[]>(ownPath(`/groups/${encodeURIComponent(groupId)}/calendar/recent`));
			if (!selected && linkable.length) selected = linkable[0].id;
		} catch (err) {
			toastError(err);
		} finally {
			fetching = false;
		}
	}

	function timeRange(e: CalendarEvent) {
		const start = new Date(e.startsAt).toLocaleString(undefined, { dateStyle: 'short', timeStyle: 'short' });
		const end = new Date(e.endsAt).toLocaleTimeString(undefined, { timeStyle: 'short' });
		return `${start} - ${end}`;
	}

	async function updateLink(calendarEntryId: string | null) {
		loading = true;
		try {
			await api.put(ownPath('/instances/calendar-event'), { location, calendarEntryId });
			toast.success(calendarEntryId ? 'Group event assigned' : 'Group event link removed');
			open = false;
			onchanged?.();
		} catch (err) {
			toastError(err);
		} finally {
			loading = false;
		}
	}
</script>

<Dialog.Root bind:open={() => open, setOpen}>
	<Dialog.Content class="sm:max-w-[650px]">
		<Dialog.Header><Dialog.Title>{m.dialog_instance_calendar_link_header()}</Dialog.Title></Dialog.Header>
		{#if fetching}
			<div class="flex justify-center py-4"><Loader2 class="size-4 animate-spin" /></div>
		{:else if linkable.length === 0}
			<p class="text-sm text-muted-foreground">{m.dialog_instance_calendar_link_no_events()}</p>
		{:else}
			<div class="flex flex-col gap-4">
				<div class="flex flex-col gap-1.5 text-sm">
					<span class="font-medium">{m.dialog_instance_calendar_link_event()}</span>
					<Select.Root type="single" bind:value={selected} disabled={loading}>
						<Select.Trigger size="sm" class="w-full">{selectedEvent ? `${selectedEvent.title} (${timeRange(selectedEvent)})` : ''}</Select.Trigger>
						<Select.Content>
							{#each linkable as event (event.id)}
								<Select.Item value={event.id} label={`${event.title} (${timeRange(event)})`} />
							{/each}
						</Select.Content>
					</Select.Root>
				</div>
				{#if selectedEvent}
					<div class="flex flex-col gap-1 rounded-md border p-3 text-sm">
						<span class="font-medium">{selectedEvent.title}</span>
						<span class="text-muted-foreground">{timeRange(selectedEvent)}</span>
						{#if selectedEvent.description}<p class="whitespace-pre-wrap">{selectedEvent.description}</p>{/if}
					</div>
				{/if}
			</div>
		{/if}
		<Dialog.Footer>
			<Button variant="secondary" disabled={loading} onclick={() => (open = false)}>{m.dialog_alertdialog_cancel()}</Button>
			{#if currentEventId}<Button variant="destructive" disabled={loading} onclick={() => updateLink(null)}>{m.dialog_instance_calendar_link_remove()}</Button>{/if}
			<Button disabled={!canAssign} onclick={() => updateLink(selected)}>{loading ? 'Saving...' : 'Assign'}</Button>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>
