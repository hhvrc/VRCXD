<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import Info from '@lucide/svelte/icons/info';
	import TriangleAlert from '@lucide/svelte/icons/triangle-alert';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as Dialog from '#lib/components/ui/dialog/index.js';
	import * as ToggleGroup from '#lib/components/ui/toggle-group/index.js';
	import * as Tooltip from '#lib/components/ui/tooltip/index.js';
	import { defaultSharedFeedFilters, FILTER_LABEL, NOTY_FILTER_OPTIONS, PHOTON_FILTER_OPTIONS, WRIST_FILTER_OPTIONS, type FilterOption, type FilterValue } from '#lib/notifications/noty.js';
	import { PhotonLobby } from '#lib/photon/photon.svelte.js';
	import { notySettings } from '#lib/notifications/noty.svelte.js';

	// The original's FeedFiltersDialog: one Off/On/Favorites/Friends/Everyone choice per kind of
	// event, for desktop notifications (noty) or the wrist feed (wrist), saved as it changes.
	let { mode = $bindable('') }: { mode?: '' | 'noty' | 'wrist' } = $props();

	const options = $derived(mode === 'wrist' ? WRIST_FILTER_OPTIONS : NOTY_FILTER_OPTIONS);
	const filters = $derived(mode === 'wrist' ? notySettings.filters.wrist : notySettings.filters.noty);
	// The Photon rows only while the PC runs the Photon logger, as the original showed them.
	const lobby = new PhotonLobby();
	$effect(() => {
		if (mode) void lobby.load();
	});

	function set(key: string, value: string) {
		if (!value || !mode) return;
		notySettings.filters[mode][key] = value as FilterValue;
		notySettings.saveFilters();
	}

	function reset() {
		if (!mode) return;
		notySettings.filters[mode] = defaultSharedFeedFilters()[mode];
		notySettings.saveFilters();
	}
</script>

{#snippet row(setting: FilterOption)}
	<div class="mb-[5px] flex items-center">
		<span class="inline-block min-w-[190px] pr-2.5 text-right">
			{setting.name}
			{#if setting.tooltip}
				<Tooltip.Root>
					<Tooltip.Trigger class="ml-1.5 align-middle">
						{#if setting.tooltipWarning}<TriangleAlert class="inline-block size-4" />{:else}<Info class="inline-block size-4" />{/if}
					</Tooltip.Trigger>
					<Tooltip.Content side="top" class="max-w-80">{setting.tooltip}</Tooltip.Content>
				</Tooltip.Root>
			{/if}
		</span>
		<ToggleGroup.Root type="single" variant="outline" size="sm" value={filters[setting.key]} onValueChange={(v) => set(setting.key, v)}>
			{#each setting.options as option (option)}
				<ToggleGroup.Item value={option}>{FILTER_LABEL[option]}</ToggleGroup.Item>
			{/each}
		</ToggleGroup.Root>
	</div>
{/snippet}

<Dialog.Root open={!!mode} onOpenChange={(open) => !open && (mode = '')}>
	<Dialog.Content class="sm:max-w-140">
		<Dialog.Header>
			<Dialog.Title>{mode === 'wrist' ? 'Wrist Feed Filters' : 'Notification Filters'}</Dialog.Title>
		</Dialog.Header>
		<Tooltip.Provider>
			<div class="h-[75vh] overflow-y-auto text-[15px]">
				{#each options as setting (setting.key)}
					{@render row(setting)}
				{/each}
				{#if lobby.snapshot.enabled}
					<br />
					<div class="mb-[5px] flex items-center">
						<span class="inline-block min-w-[190px] pr-2.5 text-right">{m.view_feed_photon_event_logging()}</span>
					</div>
					{#each PHOTON_FILTER_OPTIONS as setting (setting.key)}
						{@render row(setting)}
					{/each}
				{/if}
			</div>
		</Tooltip.Provider>
		<Dialog.Footer>
			<Button variant="secondary" onclick={reset}>{m.dialog_image_crop_reset()}</Button>
			<Button onclick={() => (mode = '')}>{m.dialog_change_log_close()}</Button>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>
