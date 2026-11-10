<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import { toast } from 'svelte-sonner';
	import Trash2 from '@lucide/svelte/icons/trash-2';
	import TriangleAlert from '@lucide/svelte/icons/triangle-alert';
	import SettingsGroup from '#lib/components/settings/settings-group.svelte';
	import SettingsItem from '#lib/components/settings/settings-item.svelte';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as Dialog from '#lib/components/ui/dialog/index.js';
	import * as Select from '#lib/components/ui/select/index.js';
	import { api, ApiError } from '#lib/api/client.js';
	import { ownPath } from '#lib/api/vrchat-auth.svelte.js';
	import { AUTO_CLEANUP_VALUES, PURGE_PERIODS, purgeBody, type PurgePeriod } from '#lib/settings/database-cleanup.js';

	// The original's Advanced tab Database Cleanup group: the avatar auto-cleanup choice (kept with
	// the account's settings, run by the server) and Purge Avatar Feed Data with its confirm dialog.
	let { value, onchange }: { value: string; onchange: (value: string) => void } = $props();

	const autoCleanupLabel = (v: string) =>
		({
			Off: m.view_settings_advanced_advanced_database_cleanup_auto_cleanup_off(),
			'30': m.view_settings_advanced_advanced_database_cleanup_auto_cleanup_30(),
			'90': m.view_settings_advanced_advanced_database_cleanup_auto_cleanup_90(),
			'180': m.view_settings_advanced_advanced_database_cleanup_auto_cleanup_180(),
			'365': m.view_settings_advanced_advanced_database_cleanup_auto_cleanup_365()
		})[v] ?? v;
	const purgeLabel = (p: PurgePeriod) =>
		({
			'180': m.view_settings_advanced_advanced_database_cleanup_purge_option_180(),
			'365': m.view_settings_advanced_advanced_database_cleanup_purge_option_365(),
			'730': m.view_settings_advanced_advanced_database_cleanup_purge_option_730(),
			all: m.view_settings_advanced_advanced_database_cleanup_purge_option_all()
		})[p];

	let dialogOpen = $state(false);
	let selectedPurgePeriod = $state<PurgePeriod>('180');
	let purgeInProgress = $state(false);

	async function handlePurge() {
		dialogOpen = false;
		purgeInProgress = true;
		const msgBox = toast.warning(m.view_settings_advanced_advanced_database_cleanup_purge_in_progress(), { duration: Infinity });
		try {
			await api.post(ownPath('/database/purge-avatar-feed'), purgeBody(selectedPurgePeriod));
			toast.dismiss(msgBox);
			toast.success(m.view_settings_advanced_advanced_database_cleanup_purge_complete());
			// The original restarts; a reload is the page's restart.
			await new Promise((resolve) => setTimeout(resolve, 1500));
			location.reload();
		} catch (err) {
			console.error(err);
			toast.dismiss(msgBox);
			toast.error(m.view_settings_advanced_advanced_database_cleanup_purge_failed({ error: ApiError.from(err).message }));
		} finally {
			purgeInProgress = false;
		}
	}
</script>

<SettingsGroup title={m.view_settings_advanced_advanced_database_cleanup_header()}>
	<SettingsItem label={m.view_settings_advanced_advanced_database_cleanup_auto_cleanup()} description={m.view_settings_advanced_advanced_database_cleanup_auto_cleanup_description()}>
		<Select.Root type="single" {value} onValueChange={(v) => onchange(v)}>
			<Select.Trigger size="sm" class="w-36">{autoCleanupLabel(value)}</Select.Trigger>
			<Select.Content>
				{#each AUTO_CLEANUP_VALUES as v (v)}<Select.Item value={v} label={autoCleanupLabel(v)} />{/each}
			</Select.Content>
		</Select.Root>
	</SettingsItem>
	<SettingsItem label={m.view_settings_advanced_advanced_database_cleanup_purge_button()}>
		<Button size="sm" variant="outline" onclick={() => (dialogOpen = true)}>
			<Trash2 class="mr-1 h-4 w-4" />
			{m.view_settings_advanced_advanced_database_cleanup_purge()}
		</Button>
	</SettingsItem>
</SettingsGroup>

<Dialog.Root bind:open={dialogOpen}>
	<Dialog.Content class="bg-background sm:max-w-md">
		<Dialog.Header><Dialog.Title>{m.view_settings_advanced_advanced_database_cleanup_purge_confirm_title()}</Dialog.Title></Dialog.Header>
		<div role="alert" class="mb-3 flex items-start gap-2 rounded-lg border border-amber-500/50 px-4 py-3 text-sm text-amber-600 dark:text-amber-400">
			<TriangleAlert class="mt-0.5 size-4 shrink-0" />
			<span>{m.view_settings_advanced_advanced_database_cleanup_purge_confirm_alert()}</span>
		</div>
		<div class="mb-3 flex flex-col gap-1 text-sm text-muted-foreground">
			<p>{m.view_settings_advanced_advanced_database_cleanup_purge_confirm_description_1()}</p>
			<p>{m.view_settings_advanced_advanced_database_cleanup_purge_confirm_description_2()}</p>
			<p>{m.view_settings_advanced_advanced_database_cleanup_purge_confirm_description_3()}</p>
		</div>
		<SettingsItem label={m.view_settings_advanced_advanced_database_cleanup_purge_older_than()}>
			<Select.Root type="single" bind:value={selectedPurgePeriod}>
				<Select.Trigger size="sm" class="w-36">{purgeLabel(selectedPurgePeriod)}</Select.Trigger>
				<Select.Content>
					{#each PURGE_PERIODS as p (p)}<Select.Item value={p} label={purgeLabel(p)} />{/each}
				</Select.Content>
			</Select.Root>
		</SettingsItem>
		<Dialog.Footer>
			<Button variant="outline" size="sm" onclick={() => (dialogOpen = false)}>{m.confirm_cancel_button()}</Button>
			<Button size="sm" variant="destructive" disabled={purgeInProgress} onclick={handlePurge}>
				<Trash2 class="mr-1 h-4 w-4" />
				{m.view_settings_advanced_advanced_database_cleanup_purge_confirm_button()}
			</Button>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>
