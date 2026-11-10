<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import { toast } from 'svelte-sonner';
	import Repeat from '@lucide/svelte/icons/repeat';
	import Trash2 from '@lucide/svelte/icons/trash-2';
	import ActionDialogs, { type Confirming } from '#lib/components/dialogs/action-dialogs.svelte';
	import GallerySelectDialog from '#lib/components/dialogs/gallery-select-dialog.svelte';
	import MultiSelect from '#lib/components/common/multi-select.svelte';
	import CountedInput from '#lib/components/group/counted-input.svelte';
	import { Button } from '#lib/components/ui/button/index.js';
	import { Checkbox } from '#lib/components/ui/checkbox/index.js';
	import * as Dialog from '#lib/components/ui/dialog/index.js';
	import { Input } from '#lib/components/ui/input/index.js';
	import * as Select from '#lib/components/ui/select/index.js';
	import { accountPath, api } from '#lib/api/client.js';
	import { groupEventEdit, groupEvents, languageOptions } from '#lib/groups/group-dialog.svelte.js';
	import { vrchatAuth } from '#lib/api/vrchat-auth.svelte.js';

	// The original's GroupEventEditDialog, mounted once for the whole app.
	const CATEGORIES: [string, string][] = [
		['arts', 'Arts'],
		['avatars', 'Avatars'],
		['dance', 'Dance'],
		['education', 'Education'],
		['exploration', 'Exploration'],
		['film_media', 'Film & Media'],
		['gaming', 'Gaming'],
		['hangout', 'Hangout'],
		['music', 'Music'],
		['other', 'Other'],
		['performance', 'Performance'],
		['roleplaying', 'Roleplaying'],
		['wellness', 'Wellness']
	];
	const PLATFORMS = [
		{ value: 'android', get label() { return m.dialog_group_event_edit_platform_values_android(); } },
		{ value: 'ios', get label() { return m.dialog_group_event_edit_platform_values_ios(); } },
		{ value: 'standalonewindows', get label() { return m.dialog_group_event_edit_platform_values_standalonewindows(); } }
	];
	const WEEKDAYS = [
		{ value: 'MO', get label() { return m.dialog_group_event_edit_weekday_values_MO(); } },
		{ value: 'TU', get label() { return m.dialog_group_event_edit_weekday_values_TU(); } },
		{ value: 'WE', get label() { return m.dialog_group_event_edit_weekday_values_WE(); } },
		{ value: 'TH', get label() { return m.dialog_group_event_edit_weekday_values_TH(); } },
		{ value: 'FR', get label() { return m.dialog_group_event_edit_weekday_values_FR(); } },
		{ value: 'SA', get label() { return m.dialog_group_event_edit_weekday_values_SA(); } },
		{ value: 'SU', get label() { return m.dialog_group_event_edit_weekday_values_SU(); } }
	];
	const REPEATS: Record<string, string> = { none: 'Never', weekly: 'Week', monthly: 'Month', yearly: 'Year' };
	const PATTERNS: Record<string, string> = { everyWeek: 'Every week', weekdays: 'Every weekday', weekend: 'Every weekend', custom: 'Custom days' };
	const ENDS: Record<string, string> = { never: 'Never', afterOccurrences: 'After occurrences', afterDate: 'On date' };

	let loading = $state(false);
	let languageNames = $state<Record<string, string>>({});
	let galleryOpen = $state(false);
	let canReturnToNever = $state(true);
	let tagsText = $state('');
	let confirming = $state<Confirming | undefined>();

	const owner = $derived(vrchatAuth.ownerUserId);
	const D = $derived(groupEventEdit.form);
	const isEditMode = $derived(D.mode === 'edit');
	const languageList = $derived(Object.entries(languageNames).map(([value, label]) => ({ value, label })));
	const roleList = $derived((D.group?.roles ?? []).map((r) => ({ value: r.id, label: r.name })));
	const hasInvalidDateRange = $derived.by(() => {
		const starts = Date.parse(D.startsAt);
		const ends = Date.parse(D.endsAt);
		return Number.isFinite(starts) && Number.isFinite(ends) && ends <= starts;
	});
	const hasPastStartDate = $derived.by(() => {
		const starts = Date.parse(D.startsAt);
		return !isEditMode && Number.isFinite(starts) && starts <= Date.now();
	});
	const repeatFrequency = $derived(D.recurrence?.frequency ?? 'none');
	const endType = $derived(D.recurrence?.end?.type ?? 'never');
	const weeklyPattern = $derived.by(() => {
		const days = D.recurrence?.daysOfWeek ?? [];
		if (days.length === 5 && ['MO', 'TU', 'WE', 'TH', 'FR'].every((d) => days.includes(d))) return 'weekdays';
		if (days.length === 2 && ['SA', 'SU'].every((d) => days.includes(d))) return 'weekend';
		if (days.length > 0) return 'custom';
		return 'everyWeek';
	});
	const canSubmit = $derived(
		!loading &&
			D.title.trim().length >= 1 &&
			D.description.trim().length >= 1 &&
			Number.isFinite(Date.parse(D.startsAt)) &&
			Number.isFinite(Date.parse(D.endsAt)) &&
			!hasInvalidDateRange &&
			!hasPastStartDate &&
			[D.hostEarlyJoinMinutes, D.guestEarlyJoinMinutes, D.closeInstanceAfterEndMinutes].every((v) => Number.isInteger(Number(v)) && Number(v) >= 0)
	);

	$effect(() => {
		if (!groupEventEdit.open) return;
		// A series that already repeats can't be set back to Never.
		canReturnToNever = !groupEventEdit.form.recurrence;
		tagsText = groupEventEdit.form.tags.join(', ');
		if (owner) languageOptions(owner).then((names) => (languageNames = names));
	});

	function toLocalDateTime(value: string | undefined) {
		const date = new Date(value ?? '');
		if (!Number.isFinite(date.getTime())) return '';
		return new Date(date.getTime() - date.getTimezoneOffset() * 60_000).toISOString().slice(0, 16);
	}

	function toIsoDateTime(value: string) {
		if (!value) return '';
		const date = new Date(value);
		return Number.isFinite(date.getTime()) ? date.toISOString() : '';
	}

	function setTags(value: string) {
		tagsText = value;
		D.tags = value
			.split(',')
			.map((t) => t.trim())
			.filter(Boolean)
			.slice(0, 5);
	}

	function setRepeat(value: string) {
		if (value === 'none') {
			if (canReturnToNever) D.recurrence = null;
			return;
		}
		if (!D.recurrence) D.recurrence = { interval: 1 };
		D.recurrence.frequency = value;
		if (value !== 'weekly') D.recurrence.daysOfWeek = [];
	}

	function setPattern(value: string) {
		if (!D.recurrence) return;
		D.recurrence.daysOfWeek = value === 'weekdays' ? ['MO', 'TU', 'WE', 'TH', 'FR'] : value === 'weekend' ? ['SA', 'SU'] : value === 'custom' ? ['SA'] : [];
	}

	function setEndType(value: string) {
		const recurrence = D.recurrence;
		if (!recurrence) return;
		if (value === 'never') recurrence.end = null;
		else if (value === 'afterOccurrences') recurrence.end = { type: value, count: recurrence.end?.type === value ? recurrence.end.count : 1 };
		else if (value === 'afterDate') recurrence.end = { type: value, date: recurrence.end?.type === value ? recurrence.end.date : '' };
	}

	function close() {
		if (!loading) groupEventEdit.open = false;
	}

	async function editParent() {
		if (!isEditMode || !D.seriesId || loading) return;
		loading = true;
		await groupEventEdit.editById(owner, D.groupId, D.seriesId, D.group);
		loading = false;
	}

	async function save() {
		if (!canSubmit) return;
		const body: Record<string, unknown> = {
			startsAt: D.startsAt,
			endsAt: D.endsAt,
			title: D.title.trim(),
			accessType: D.accessType,
			description: D.description.trim(),
			category: D.category,
			tags: D.tags.map((t) => t.trim()),
			imageId: D.imageId || null,
			roleIds: [...D.roleIds],
			platforms: [...D.platforms],
			languages: D.languages.slice(0, 3),
			sendCreationNotification: D.sendCreationNotification,
			hostEarlyJoinMinutes: Number(D.hostEarlyJoinMinutes),
			guestEarlyJoinMinutes: Number(D.guestEarlyJoinMinutes),
			closeInstanceAfterEndMinutes: Number(D.closeInstanceAfterEndMinutes)
		};
		if (!D.seriesId && D.recurrence) {
			const recurrence: Record<string, unknown> = {
				frequency: D.recurrence.frequency,
				interval: Number(D.recurrence.interval) || 1,
				timezone: Intl.DateTimeFormat().resolvedOptions().timeZone
			};
			if (D.recurrence.frequency === 'weekly' && D.recurrence.daysOfWeek?.length) recurrence.daysOfWeek = [...D.recurrence.daysOfWeek];
			if (D.recurrence.end?.type === 'afterOccurrences') recurrence.end = { type: 'afterOccurrences', count: Number(D.recurrence.end.count) || 1 };
			else if (D.recurrence.end?.type === 'afterDate') recurrence.end = { type: 'afterDate', date: toLocalDateTime(D.recurrence.end.date) };
			body.recurrence = recurrence;
		}
		const base = accountPath(owner, `/groups/${encodeURIComponent(D.groupId)}/calendar`);
		const edited = isEditMode;
		loading = true;
		try {
			if (edited) await api.put(`${base}/${encodeURIComponent(D.eventId)}`, body);
			else await api.post(base, body);
			groupEventEdit.open = false;
			groupEvents.revision++;
			toast.success(edited ? 'Group event updated' : 'Group event created');
		} catch {
			toast.error(m.dialog_group_event_edit_save_error());
		} finally {
			loading = false;
		}
	}

	function deleteEvent() {
		if (!isEditMode || loading) return;
		confirming = {
			get title() { return m.dialog_alertdialog_confirm(); },
			get description() { return m.dialog_group_event_edit_delete_confirm(); },
			destructive: true,
			run: async () => {
				confirming = undefined;
				loading = true;
				try {
					await api.delete(accountPath(owner, `/groups/${encodeURIComponent(D.groupId)}/calendar/${encodeURIComponent(D.eventId)}`));
					groupEventEdit.open = false;
					groupEvents.revision++;
					toast.success(m.dialog_group_event_edit_delete_success());
				} catch {
					toast.error(m.dialog_group_event_edit_delete_error());
				} finally {
					loading = false;
				}
			}
		};
	}
</script>

{#snippet label(text: string)}
	<span class="text-sm leading-none font-medium select-none">{text}</span>
{/snippet}

{#snippet pick(value: string, options: Record<string, string>, onchange: (value: string) => void, disabledValue?: string)}
	<Select.Root type="single" {value} onValueChange={onchange} disabled={loading}>
		<Select.Trigger size="sm" class="w-full">{options[value]}</Select.Trigger>
		<Select.Content>
			{#each Object.entries(options) as [key, text] (key)}
				<Select.Item value={key} label={text} disabled={key === disabledValue}>{text}</Select.Item>
			{/each}
		</Select.Content>
	</Select.Root>
{/snippet}

<Dialog.Root bind:open={() => groupEventEdit.open, (next) => !next && close()}>
	<Dialog.Content class="flex max-h-[85vh] flex-col gap-0 overflow-hidden bg-background p-0 sm:max-w-180">
		<Dialog.Header class="px-6 pt-6 pb-4">
			<Dialog.Title>{isEditMode ? 'Edit Group Event' : 'Create Group Event'}</Dialog.Title>
		</Dialog.Header>

		<div class="min-h-0 flex-1 overflow-x-hidden overflow-y-auto px-6 py-2">
			<div class="flex flex-col gap-4">
				<div class="flex flex-col gap-2">
					{@render label('Title')}
					<CountedInput bind:value={D.title} maxlength={64} disabled={loading} />
				</div>
				<div class="flex flex-col gap-2">
					{@render label('Description')}
					<CountedInput bind:value={D.description} maxlength={1024} rows={4} disabled={loading} />
				</div>
				<div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
					<div class="flex flex-col gap-2">
						{@render label('Starts At')}
						<Input type="datetime-local" class="h-8" disabled={loading} bind:value={() => toLocalDateTime(D.startsAt), (v) => (D.startsAt = toIsoDateTime(v))} />
						{#if hasPastStartDate}<p class="mt-1 text-xs text-destructive">{m.dialog_group_event_edit_start_in_past()}</p>{/if}
					</div>
					<div class="flex flex-col gap-2">
						{@render label('Ends At')}
						<Input type="datetime-local" class="h-8" disabled={loading} bind:value={() => toLocalDateTime(D.endsAt), (v) => (D.endsAt = toIsoDateTime(v))} />
						{#if hasInvalidDateRange}<p class="mt-1 text-xs text-destructive">{m.dialog_group_event_edit_invalid_date_range()}</p>{/if}
					</div>
				</div>

				{#if !D.seriesId}
					<div class="space-y-4 rounded-md border p-4">
						<div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
							<div class="flex flex-col gap-2">
								{@render label('Repeat')}
								{@render pick(repeatFrequency, REPEATS, setRepeat, canReturnToNever ? undefined : 'none')}
							</div>
							{#if D.recurrence}
								{@const recurrence = D.recurrence}
								<div class="flex flex-col gap-2">
									{@render label('Repeat every')}
									<Input type="number" min="1" step="1" class="h-8" disabled={loading} bind:value={recurrence.interval} />
								</div>
							{/if}
						</div>
						{#if D.recurrence}
							{@const recurrence = D.recurrence}
							<div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
								{#if recurrence.frequency === 'weekly'}
									<div class="flex flex-col gap-2">
										{@render label('Weekly repeat pattern')}
										{@render pick(weeklyPattern, PATTERNS, setPattern)}
									</div>
									{#if weeklyPattern === 'custom'}
										<div class="flex flex-col gap-2">
											{@render label('Select days')}
											<MultiSelect disabled={loading}
												values={recurrence.daysOfWeek ?? []}
												options={WEEKDAYS}
												placeholder={m.dialog_group_event_edit_none_selected()}
												class="h-8 w-full"
												onchange={(values) => (recurrence.daysOfWeek = values.filter((d) => WEEKDAYS.some((w) => w.value === d)))} />
										</div>
									{/if}
								{/if}
							</div>
							<div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
								<div class="flex flex-col gap-2">
									{@render label('Ends')}
									{@render pick(endType, ENDS, setEndType)}
								</div>
								{#if recurrence.end?.type === 'afterOccurrences'}
									{@const end = recurrence.end}
									<div class="flex flex-col gap-2">
										{@render label('Number of occurrences')}
										<Input type="number" min="1" step="1" class="h-8" disabled={loading} bind:value={end.count} />
									</div>
								{:else if recurrence.end?.type === 'afterDate'}
									{@const end = recurrence.end}
									<div class="flex flex-col gap-2">
										{@render label('End date')}
										<Input type="datetime-local" class="h-8" disabled={loading} bind:value={end.date} />
									</div>
								{/if}
							</div>
						{/if}
					</div>
				{/if}

				<div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
					<div class="flex flex-col gap-2">
						{@render label('Access')}
						<Select.Root type="single" bind:value={D.accessType} disabled={loading || D.group?.privacy === 'private'}>
							<Select.Trigger size="sm" class="w-full">{D.accessType === 'public' ? 'Public' : 'Group'}</Select.Trigger>
							<Select.Content>
								<Select.Item value="group" label={m.dialog_new_instance_group()}>{m.dialog_new_instance_group()}</Select.Item>
								<Select.Item value="public" label={m.dialog_group_tags_public()}>{m.dialog_group_tags_public()}</Select.Item>
							</Select.Content>
						</Select.Root>
					</div>
					<div class="flex flex-col gap-2">
						{@render label('Category')}
						{@render pick(D.category, Object.fromEntries(CATEGORIES), (v) => (D.category = v))}
					</div>
				</div>
				<div class="flex flex-col gap-2">
					{@render label('Tags')}
					<Input class="h-8" disabled={loading} bind:value={() => tagsText, setTags} />
					<p class="mt-1 text-xs text-muted-foreground">{m.dialog_group_event_edit_tags_help()}</p>
				</div>
				<div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
					<div class="flex flex-col gap-2">
						{@render label('Languages')}
						<MultiSelect disabled={loading} values={D.languages} options={languageList} placeholder={m.dialog_group_event_edit_none_selected()} class="h-8 w-full" onchange={(values) => (D.languages = values.slice(0, 3))} />
					</div>
					<div class="flex flex-col gap-2">
						{@render label('Platforms')}
						<MultiSelect disabled={loading} values={D.platforms} options={PLATFORMS} placeholder={m.dialog_group_event_edit_none_selected()} class="h-8 w-full" onchange={(values) => (D.platforms = values.filter((p) => PLATFORMS.some((o) => o.value === p)))} />
					</div>
				</div>
				<div class="flex flex-col gap-2">
					{@render label('Roles')}
					<MultiSelect disabled={loading} values={D.roleIds} options={roleList} placeholder={m.dialog_group_event_edit_all_members()} class="h-8 w-full" onchange={(values) => (D.roleIds = values)} />
				</div>
				<div class="flex flex-col gap-2">
					{@render label('Image')}
					<div class="flex items-start gap-2">
						{#if D.imageUrl}<img src={D.imageUrl} alt="" class="size-20 rounded-md object-cover" loading="lazy" />{/if}
						<div class="flex gap-2">
							<Button size="sm" variant="outline" disabled={loading} onclick={() => (galleryOpen = true)}>{m.dialog_group_event_edit_select_image()}</Button>
							{#if D.imageUrl}<Button size="sm" variant="destructive" disabled={loading} onclick={() => ((D.imageId = ''), (D.imageUrl = ''))}>{m.dialog_group_event_edit_clear_image()}</Button>{/if}
						</div>
					</div>
				</div>
				<div class="grid grid-cols-1 gap-4 sm:grid-cols-3">
					<div class="flex flex-col gap-2">
						{@render label('Host Early Join (minutes)')}
						<Input type="number" min="0" max="60" step="1" class="h-8" disabled={loading || isEditMode} bind:value={D.hostEarlyJoinMinutes} />
					</div>
					<div class="flex flex-col gap-2">
						{@render label('Guest Early Join (minutes)')}
						<Input type="number" min="0" max="60" step="1" class="h-8" disabled={loading || isEditMode} bind:value={D.guestEarlyJoinMinutes} />
					</div>
					<div class="flex flex-col gap-2">
						{@render label('Close After End (minutes)')}
						<Input type="number" min="0" max="60" step="1" class="h-8" disabled={loading || isEditMode} bind:value={D.closeInstanceAfterEndMinutes} />
					</div>
				</div>
				{#if !isEditMode}
					<label class="inline-flex items-center gap-2"><Checkbox bind:checked={D.sendCreationNotification} disabled={loading} /><span>{m.dialog_group_event_edit_send_notification()}</span></label>
				{/if}
			</div>
		</div>

		<Dialog.Footer class="px-6 py-4">
			{#if isEditMode}
				<div class="mr-auto flex gap-2">
					<Button variant="destructive" disabled={loading} onclick={deleteEvent}><Trash2 /> {m.dialog_world_actions_delete()}</Button>
					{#if D.seriesId}<Button variant="outline" disabled={loading} onclick={editParent}><Repeat /> {m.dialog_group_event_edit_edit_parent()}</Button>{/if}
				</div>
			{/if}
			<Button variant="secondary" disabled={loading} onclick={close}>{m.dialog_group_transfer_cancel()}</Button>
			<Button disabled={!canSubmit} onclick={save}>{loading ? 'Saving...' : isEditMode ? 'Save Changes' : 'Create Event'}</Button>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>

<GallerySelectDialog bind:open={galleryOpen} onselect={({ fileId, imageUrl }) => ((D.imageId = fileId), (D.imageUrl = imageUrl))} />
<ActionDialogs bind:confirming busy={loading} />
