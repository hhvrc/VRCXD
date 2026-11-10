<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import Trash2 from '@lucide/svelte/icons/trash-2';
	import X from '@lucide/svelte/icons/x';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as Dialog from '#lib/components/ui/dialog/index.js';
	import { panelConfig, panelKey, type PanelValue } from '#lib/nav/dashboards.svelte.js';
	import { pageOptions, panelInfo, panelLoader, WIDGETS } from './panels';

	// The original's DashboardPanel and PanelSelector: in edit mode a slot to choose what it shows,
	// otherwise the page or widget itself.
	let {
		panel,
		editing = false,
		showRemove = false,
		class: className = '',
		onselect,
		onremove
	}: { panel: PanelValue; editing?: boolean; showRemove?: boolean; class?: string; onselect: (value: PanelValue) => void; onremove?: () => void } = $props();

	let selectorOpen = $state(false);
	const key = $derived(panelKey(panel));
	const info = $derived(panelInfo(key));
	const loader = $derived(panelLoader(key));
	const isWidget = $derived(!!key?.startsWith('widget:'));

	function choose(value: PanelValue) {
		onselect(value);
		selectorOpen = false;
	}
</script>

<div class={['relative flex min-h-0 flex-1 overflow-hidden rounded-md border bg-card', className]}>
	{#if editing}
		{#if showRemove}
			<Button variant="ghost" size="icon-sm" class="absolute top-1 right-1 z-20" aria-label="Remove panel" onclick={onremove}><X class="size-4" /></Button>
		{/if}
		<div class="flex min-h-0 w-full flex-col items-center justify-center gap-2 p-3">
			{#if key}
				<div class="flex items-center gap-2 text-base text-muted-foreground">
					{#if info?.icon}<i class={[info.icon, 'text-base']} aria-hidden="true"></i>{/if}
					<span>{info?.label ?? key}</span>
					<Button variant="ghost" size="icon-sm" aria-label={m.dashboard_actions_delete()} onclick={() => onselect(null)}><Trash2 class="size-3.5" /></Button>
				</div>
			{:else}
				<span class="text-base text-muted-foreground">{m.dashboard_panel_not_selected()}</span>
				<Button variant="outline" onclick={() => (selectorOpen = true)}>{m.dashboard_panel_select()}</Button>
			{/if}
		</div>
	{:else if key && loader}
		<div class="dashboard-panel h-full w-full overflow-y-auto">
			{#await loader() then module}
				{@const Panel = module.default}
				{#if isWidget}
					<Panel config={panelConfig(panel)} configUpdater={(config: Record<string, unknown>) => onselect({ key: key!, config })} />
				{:else}
					<div class="h-full p-3"><Panel /></div>
				{/if}
			{/await}
		</div>
	{:else}
		<div class="flex w-full items-center justify-center text-sm text-muted-foreground">{m.dashboard_panel_not_configured()}</div>
	{/if}
</div>

<Dialog.Root bind:open={selectorOpen}>
	<Dialog.Content class="bg-background sm:max-w-110">
		<Dialog.Header><Dialog.Title>{m.dashboard_selector_title()}</Dialog.Title></Dialog.Header>
		<div class="max-h-[50vh] overflow-y-auto">
			<div class="mb-3">
				<span class="px-1 text-xs font-medium tracking-wide text-muted-foreground uppercase">{m.dashboard_selector_widgets()}</span>
				<div class="mt-1.5 grid grid-cols-2 gap-2">
					{#each WIDGETS as option (option.key)}
						<button
							type="button"
							class={['flex cursor-pointer items-center gap-2 rounded-md border p-2 text-left text-sm hover:bg-accent', option.key === key ? 'border-primary bg-primary/5 ring-1 ring-primary/40' : 'border-primary/20']}
							onclick={() => choose({ key: option.key, config: { ...option.defaultConfig } })}
						>
							<i class={[option.icon, 'text-base']} aria-hidden="true"></i><span>{option.label}</span>
						</button>
					{/each}
				</div>
			</div>
			<div>
				<span class="px-1 text-xs font-medium tracking-wide text-muted-foreground uppercase">{m.dashboard_selector_pages()}</span>
				<div class="mt-1.5 grid grid-cols-2 gap-2">
					{#each pageOptions as option (option.key)}
						<button
							type="button"
							class={['flex cursor-pointer items-center gap-2 rounded-md border p-2 text-left text-sm hover:bg-accent', option.key === key && 'border-primary bg-primary/5 ring-1 ring-primary/40']}
							onclick={() => choose(option.key)}
						>
							<i class={[option.icon, 'text-base']} aria-hidden="true"></i><span>{option.label}</span>
						</button>
					{/each}
				</div>
			</div>
		</div>
		<Dialog.Footer>
			<Button variant="ghost" onclick={() => choose(null)}>{m.dashboard_selector_clear()}</Button>
			<Button variant="secondary" onclick={() => (selectorOpen = false)}>{m.dashboard_actions_cancel()}</Button>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>
