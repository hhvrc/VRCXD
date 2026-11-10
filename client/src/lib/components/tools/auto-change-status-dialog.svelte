<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import { untrack } from 'svelte';
	import * as Dialog from '#lib/components/ui/dialog/index.js';
	import { Input } from '#lib/components/ui/input/index.js';
	import * as RadioGroup from '#lib/components/ui/radio-group/index.js';
	import * as Select from '#lib/components/ui/select/index.js';
	import { Separator } from '#lib/components/ui/separator/index.js';
	import { Switch } from '#lib/components/ui/switch/index.js';
	import { api, ApiError } from '#lib/api/client.js';
	import type { AccountSettings } from '#lib/api/types.js';
	import { ownPath } from '#lib/api/vrchat-auth.svelte.js';

	// The original's AutoChangeStatusDialog. The rules themselves run on the server, so they keep
	// working while no client is open. Local favorite groups are not on the server yet.

	let { open = $bindable(false) }: { open?: boolean } = $props();

	const STATUSES = [
		{ value: 'join me', get label() { return m.dialog_user_status_join_me(); }, dot: 'bg-sky-500' },
		{ value: 'active', get label() { return m.dialog_user_status_online(); }, dot: 'bg-green-500' },
		{ value: 'ask me', get label() { return m.dialog_user_status_ask_me(); }, dot: 'bg-orange-500' },
		{ value: 'busy', get label() { return m.dialog_user_status_busy(); }, dot: 'bg-red-500' }
	];
	const INSTANCE_TYPES = [
		{ value: 'invite', get label() { return m.dialog_launch_invite(); } },
		{ value: 'invite+', get label() { return m.dialog_new_instance_access_type_invite_plus(); } },
		{ value: 'friends', get label() { return m.dialog_group_tags_friends(); } },
		{ value: 'friends+', get label() { return m.dialog_new_instance_access_type_friend_plus(); } },
		{ value: 'public', get label() { return m.dialog_world_tags_public(); } },
		{ value: 'groupPublic', label: 'Group Public' },
		{ value: 'groupPlus', label: 'Group Plus' },
		{ value: 'groupOnly', label: 'Group Members' }
	];

	let settings = $state<AccountSettings | null>(null);
	let groups = $state<{ name: string; displayName: string }[]>([]);
	let localGroups = $state<{ name: string }[]>([]);
	let error = $state('');

	const path = ownPath;
	const statusOf = (value: string) => STATUSES.find((s) => s.value === value);
	const joined = (values: string[], options: { value: string; label: string }[]) => values.map((v) => options.find((o) => o.value === v)?.label ?? v).join(', ');
	// The original's group keys: VRChat groups as "friend:group_N", local groups as "local:Name".
	const groupOptions = $derived([
		...groups.map((g) => ({ value: `friend:${g.name}`, label: g.displayName || g.name })),
		...localGroups.map((g) => ({ value: `local:${g.name}`, label: g.name }))
	]);
	/** Settings stored before the keys matched the original's held bare group names. */
	const keyed = (values: string[]) => values.map((v) => (v.includes(':') ? v : `friend:${v}`));
	const enabled = $derived(settings?.autoStateChangeEnabled ?? false);
	const accepting = $derived(settings ? settings.autoAcceptInviteRequests !== 'Off' : false);
	const acceptMode = $derived(accepting && settings ? settings.autoAcceptInviteRequests : 'All Favorites');

	async function load() {
		error = '';
		try {
			const [loaded, favorites, locals] = await Promise.all([
				api.get<AccountSettings>(path('/settings')),
				api.get<{ groups: { name: string; displayName: string }[] }>(path('/favorites'), { type: 'friend' }),
				api.get<{ name: string }[]>(path('/favorites/local/groups'), { type: 'Friend' })
			]);
			settings = { ...loaded, autoStateChangeGroups: keyed(loaded.autoStateChangeGroups), autoAcceptInviteGroups: keyed(loaded.autoAcceptInviteGroups) };
			groups = favorites.groups;
			localGroups = locals;
		} catch (err) {
			error = ApiError.from(err).message;
		}
	}

	$effect(() => {
		if (open) untrack(load);
	});

	async function save(changes: Partial<AccountSettings>) {
		if (!settings) return;
		const next = { ...settings, ...changes };
		settings = next;
		try {
			settings = await api.put<AccountSettings>(path('/settings'), next);
			error = '';
		} catch (err) {
			error = ApiError.from(err).message;
		}
	}
</script>

{#snippet statusLabel(value: string)}
	<span class="inline-flex items-center gap-2"><span class="size-2 rounded-full {statusOf(value)?.dot}"></span>{statusOf(value)?.label ?? value}</span>
{/snippet}

{#snippet statusSelect(value: string, disabled: boolean, onchange: (v: string) => void)}
	<Select.Root type="single" {value} {disabled} onValueChange={onchange}>
		<Select.Trigger size="sm" class="w-full">{@render statusLabel(value)}</Select.Trigger>
		<Select.Content>
			{#each STATUSES as status (status.value)}
				<Select.Item value={status.value}>{@render statusLabel(status.value)}</Select.Item>
			{/each}
		</Select.Content>
	</Select.Root>
{/snippet}

{#snippet multiSelect(values: string[], options: { value: string; label: string }[], placeholder: string, disabled: boolean, onchange: (v: string[]) => void)}
	<Select.Root type="multiple" value={values} {disabled} onValueChange={onchange}>
		<Select.Trigger size="sm" class="w-full"><span class="truncate {values.length ? '' : 'text-muted-foreground'}">{values.length ? joined(values, options) : placeholder}</span></Select.Trigger>
		<Select.Content>
			{#each options as option (option.value)}
				<Select.Item value={option.value}>{option.label}</Select.Item>
			{/each}
		</Select.Content>
	</Select.Root>
{/snippet}

<Dialog.Root bind:open>
	<Dialog.Content class="max-h-[85vh] overflow-y-auto sm:max-w-lg">
		<Dialog.Header>
			<Dialog.Title>{m.view_settings_general_automation_auto_change_status()}</Dialog.Title>
		</Dialog.Header>
		{#if error}<p class="text-xs text-destructive">{error}</p>{/if}
		{#if settings}
			<div class="flex flex-col gap-4 text-sm">
				<div class="flex items-center justify-between gap-2" title={m.view_settings_general_automation_auto_state_change_switch_tooltip()}>
					<span>{m.view_settings_general_automation_auto_change_status_switch()}</span>
					<Switch checked={settings.autoStateChangeEnabled} aria-label={m.view_settings_general_automation_auto_change_status_switch()} onCheckedChange={(v) => save({ autoStateChangeEnabled: v })} />
				</div>

				<div class="flex flex-col gap-2">
					<span class="font-medium">{m.view_settings_general_automation_alone_condition()}</span>
					<RadioGroup.Root class="flex gap-4" value={settings.autoStateChangeNoFriends ? 'true' : 'false'} disabled={!enabled} onValueChange={(v) => save({ autoStateChangeNoFriends: v === 'true' })}>
						<label class="flex items-center gap-2"><RadioGroup.Item value="false" />{m.view_settings_general_automation_alone()}</label>
						<label class="flex items-center gap-2"><RadioGroup.Item value="true" />{m.view_settings_general_automation_no_friends()}</label>
					</RadioGroup.Root>
				</div>

				<div class="flex flex-col gap-2">
					<span class="font-medium">{m.view_settings_general_automation_auto_change_status_groups()}</span>
					{@render multiSelect(settings.autoStateChangeGroups, groupOptions, 'Choose Groups', !enabled || !settings.autoStateChangeNoFriends, (v) => save({ autoStateChangeGroups: v }))}
				</div>

				<div class="flex flex-col gap-2">
					<span class="font-medium">{m.view_settings_general_automation_allowed_instance_types()}</span>
					{@render multiSelect(settings.autoStateChangeInstanceTypes, INSTANCE_TYPES, 'All Instance Types', !enabled, (v) => save({ autoStateChangeInstanceTypes: v }))}
				</div>

				<div class="flex flex-col gap-2">
					<span class="font-medium">{m.view_settings_general_automation_alone_status()}</span>
					{@render statusSelect(settings.autoStateChangeAloneStatus, !enabled, (v) => save({ autoStateChangeAloneStatus: v }))}
					<div class="flex items-center justify-between gap-2">
						<span>{m.view_settings_general_automation_change_status_description()}</span>
						<Switch checked={settings.autoStateChangeAloneDescEnabled} disabled={!enabled} aria-label="Override alone status description" onCheckedChange={(v) => save({ autoStateChangeAloneDescEnabled: v })} />
					</div>
					{#if settings.autoStateChangeAloneDescEnabled}
						<Input value={settings.autoStateChangeAloneDesc} disabled={!enabled} maxlength={32} placeholder={m.view_settings_general_automation_status_description_placeholder()} onchange={(e) => save({ autoStateChangeAloneDesc: e.currentTarget.value })} />
					{/if}
				</div>

				<div class="flex flex-col gap-2">
					<span class="font-medium">{m.view_settings_general_automation_company_status()}</span>
					{@render statusSelect(settings.autoStateChangeCompanyStatus, !enabled, (v) => save({ autoStateChangeCompanyStatus: v }))}
					<div class="flex items-center justify-between gap-2">
						<span>{m.view_settings_general_automation_change_status_description()}</span>
						<Switch checked={settings.autoStateChangeCompanyDescEnabled} disabled={!enabled} aria-label="Override company status description" onCheckedChange={(v) => save({ autoStateChangeCompanyDescEnabled: v })} />
					</div>
					{#if settings.autoStateChangeCompanyDescEnabled}
						<Input value={settings.autoStateChangeCompanyDesc} disabled={!enabled} maxlength={32} placeholder={m.view_settings_general_automation_status_description_placeholder()} onchange={(e) => save({ autoStateChangeCompanyDesc: e.currentTarget.value })} />
					{/if}
				</div>

				<Separator />

				<div class="flex items-center justify-between gap-2" title={m.view_settings_general_automation_auto_invite_request_accept_tooltip()}>
					<span>{m.view_settings_general_automation_auto_invite_request_accept()}</span>
					<Switch checked={accepting} aria-label={m.view_settings_general_automation_auto_invite_request_accept()} onCheckedChange={(v) => save({ autoAcceptInviteRequests: v ? acceptMode : 'Off' })} />
				</div>

				<RadioGroup.Root class="flex gap-4" value={acceptMode} disabled={!accepting} onValueChange={(v) => save({ autoAcceptInviteRequests: v })}>
					<label class="flex items-center gap-2"><RadioGroup.Item value="All Favorites" />{m.dialog_world_export_all_favorites()}</label>
					<label class="flex items-center gap-2"><RadioGroup.Item value="Selected Favorites" />{m.view_settings_general_automation_auto_invite_request_accept_selected_favs()}</label>
				</RadioGroup.Root>

				<div class="flex flex-col gap-2">
					<span class="font-medium">{m.view_settings_general_automation_auto_accept_invite_groups()}</span>
					{@render multiSelect(settings.autoAcceptInviteGroups, groupOptions, 'Choose Groups', settings.autoAcceptInviteRequests !== 'Selected Favorites', (v) => save({ autoAcceptInviteGroups: v }))}
				</div>

				<div class="flex flex-col gap-2">
					<span class="font-medium">{m.view_settings_general_automation_auto_accept_invite_statuses()}</span>
					{@render multiSelect(settings.autoAcceptInviteStatuses, STATUSES, 'All Statuses', !accepting, (v) => save({ autoAcceptInviteStatuses: v }))}
				</div>
			</div>
		{/if}
	</Dialog.Content>
</Dialog.Root>
