<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as Dialog from '#lib/components/ui/dialog/index.js';
	import { Input } from '#lib/components/ui/input/index.js';
	import { appearance, SEARCH_LIMIT_MAX, SEARCH_LIMIT_MIN, TABLE_MAX_SIZE_MAX, TABLE_MAX_SIZE_MIN } from '#lib/settings/appearance.svelte.js';

	// The original's TableLimitsDialog: how many rows the feed, game log and notifications load
	// (VRCX_maxTableSize_v2) and how many a search returns (VRCX_searchLimit).
	let { open = $bindable(false) }: { open?: boolean } = $props();

	let maxTableSize = $state('');
	let searchLimit = $state('');
	$effect(() => {
		if (open) {
			maxTableSize = String(appearance.maxTableSize.value);
			searchLimit = String(appearance.searchLimit.value);
		}
	});

	const inRange = (value: string, min: number, max: number) => {
		const n = Number(value);
		return /^\d+$/.test(value.trim()) && n >= min && n <= max;
	};
	const maxTableSizeError = $derived(inRange(maxTableSize, TABLE_MAX_SIZE_MIN, TABLE_MAX_SIZE_MAX) ? '' : `Enter a number between ${TABLE_MAX_SIZE_MIN} and ${TABLE_MAX_SIZE_MAX}.`);
	const searchLimitError = $derived(inRange(searchLimit, SEARCH_LIMIT_MIN, SEARCH_LIMIT_MAX) ? '' : `Enter a number between ${SEARCH_LIMIT_MIN} and ${SEARCH_LIMIT_MAX}.`);

	function save() {
		if (maxTableSizeError || searchLimitError) return;
		appearance.maxTableSize.value = Number(maxTableSize);
		appearance.searchLimit.value = Number(searchLimit);
		open = false;
	}
</script>

<Dialog.Root bind:open>
	<Dialog.Content class="sm:max-w-110">
		<Dialog.Header>
			<Dialog.Title>{m.view_settings_appearance_appearance_table_entries_settings()}</Dialog.Title>
			<Dialog.Description>{m.prompt_table_entries_settings_description()}</Dialog.Description>
		</Dialog.Header>

		<div class="mt-3 flex flex-col gap-3">
			<div class="flex flex-col gap-1.5">
				<span class="text-sm font-medium">{m.prompt_table_entries_settings_table_max_entries()}</span>
				<Input type="text" inputmode="numeric" bind:value={maxTableSize} />
				<p class="mt-1 text-xs {maxTableSizeError ? 'text-destructive' : 'text-muted-foreground'}">
					{maxTableSizeError || `Valid range: ${TABLE_MAX_SIZE_MIN} – ${TABLE_MAX_SIZE_MAX}.`}
				</p>
			</div>
			<div class="flex flex-col gap-1.5">
				<span class="text-sm font-medium">{m.prompt_table_entries_settings_search_limit_returns()}</span>
				<Input type="text" inputmode="numeric" bind:value={searchLimit} />
				<p class="mt-1 text-xs {searchLimitError ? 'text-destructive' : 'text-muted-foreground'}">
					{searchLimitError || `Valid range: ${SEARCH_LIMIT_MIN} – ${SEARCH_LIMIT_MAX}.`}
				</p>
				<p class="mt-1 text-xs text-muted-foreground">{m.prompt_table_entries_settings_search_limit_returns_warning()}</p>
			</div>
		</div>

		<Dialog.Footer>
			<Button variant="secondary" class="mr-2" onclick={() => (open = false)}>{m.dialog_alertdialog_cancel()}</Button>
			<Button disabled={!!maxTableSizeError || !!searchLimitError} onclick={save}>{m.dialog_config_json_save()}</Button>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>
