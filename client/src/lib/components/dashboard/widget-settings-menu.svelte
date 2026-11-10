<script lang="ts">
	import Settings from '@lucide/svelte/icons/settings';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
	import { filterActive, toggleFilter } from './widget-filters';

	type Config = Record<string, unknown>;

	// The widgets' settings dropdown: one checkbox per option, toggling config.filters unless the
	// widget passes its own checked/toggle, then a separator and any boolean config toggles.
	let {
		options,
		labels,
		config,
		configUpdater,
		checked = (option: string) => filterActive(config.filters, option),
		toggle = (option: string) => configUpdater?.({ ...config, filters: toggleFilter(options, config.filters, option) }),
		disabled = () => false,
		toggles = []
	}: {
		options: readonly string[];
		labels: Record<string, string>;
		config: Config;
		configUpdater?: (config: Config) => void;
		checked?: (option: string) => boolean;
		toggle?: (option: string) => void;
		disabled?: (option: string) => boolean;
		toggles?: { key: string; label: string; checked: boolean }[];
	} = $props();
</script>

{#if configUpdater}
	<DropdownMenu.Root>
		<DropdownMenu.Trigger>
			{#snippet child({ props })}
				<Button {...props} variant="ghost" size="icon-sm" aria-label="Widget settings"><Settings class="size-3.5" /></Button>
			{/snippet}
		</DropdownMenu.Trigger>
		<DropdownMenu.Content align="end" class="w-48">
			{#each options as option (option)}
				<DropdownMenu.CheckboxItem checked={checked(option)} disabled={disabled(option)} closeOnSelect={false} onCheckedChange={() => toggle(option)}>{labels[option]}</DropdownMenu.CheckboxItem>
			{/each}
			{#if toggles.length}
				<DropdownMenu.Separator />
				{#each toggles as item (item.key)}
					<DropdownMenu.CheckboxItem checked={item.checked} closeOnSelect={false} onCheckedChange={() => configUpdater({ ...config, [item.key]: !item.checked })}>{item.label}</DropdownMenu.CheckboxItem>
				{/each}
			{/if}
		</DropdownMenu.Content>
	</DropdownMenu.Root>
{/if}
