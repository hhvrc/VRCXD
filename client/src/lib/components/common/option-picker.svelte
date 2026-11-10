<script lang="ts" module>
	export type PickerOption = { value: string; label: string; image?: string | null; section?: string };
</script>

<script lang="ts">
	import PersonAvatar from '#lib/components/common/person-avatar.svelte';
	import { m } from '#lib/paraglide/messages.js';
	import Check from '@lucide/svelte/icons/check';
	import ChevronDown from '@lucide/svelte/icons/chevron-down';
	import { imageSrc } from '#lib/api/links.js';
	import { Input } from '#lib/components/ui/input/index.js';
	import * as Popover from '#lib/components/ui/popover/index.js';

	// The original's VirtualCombobox, for picking a group, a creator or the friends to invite: search,
	// sections, one value or several, and picking a chosen value again clears it.
	let {
		options,
		values = $bindable([]),
		multiple = false,
		placeholder = '',
		disabled = false,
		onchange
	}: { options: PickerOption[]; values?: string[]; multiple?: boolean; placeholder?: string; disabled?: boolean; onchange?: () => void } = $props();

	let open = $state(false);
	let search = $state('');

	const chosen = $derived(values.map((v) => options.find((o) => o.value === v)?.label ?? v));
	const summary = $derived(chosen.length > 3 ? `${chosen.slice(0, 3).join(', ')} +${chosen.length - 3}` : chosen.join(', '));
	const sections = $derived.by(() => {
		const text = search.trim().toLowerCase();
		const found = new Map<string, PickerOption[]>();
		for (const option of options) {
			if (text && !option.label.toLowerCase().includes(text)) continue;
			const key = option.section ?? '';
			found.set(key, [...(found.get(key) ?? []), option]);
		}
		return [...found];
	});

	function pick(value: string) {
		if (multiple) values = values.includes(value) ? values.filter((v) => v !== value) : [...values, value];
		else {
			values = values.includes(value) ? [] : [value];
			open = false;
		}
		onchange?.();
	}
</script>

<Popover.Root bind:open>
	<Popover.Trigger {disabled} class="flex h-8 w-full cursor-pointer items-center justify-between gap-2 rounded-md border border-input bg-transparent px-3 text-sm shadow-xs outline-none focus-visible:ring-[3px] focus-visible:ring-ring/50 disabled:cursor-not-allowed disabled:opacity-50 dark:bg-input/30">
		<span class="truncate {chosen.length ? '' : 'text-muted-foreground'}">{chosen.length ? summary : placeholder}</span>
		<ChevronDown class="size-4 flex-none text-muted-foreground" />
	</Popover.Trigger>
	<Popover.Content class="w-(--bits-popover-anchor-width) p-1" align="start">
		<Input bind:value={search} {placeholder} class="mb-1 h-8" />
		<div class="max-h-64 overflow-y-auto">
			{#each sections as [section, items] (section)}
				{#if section}<div class="px-2 py-1 text-xs text-muted-foreground">{section}</div>{/if}
				{#each items as option (option.value)}
					<button type="button" class="flex w-full cursor-pointer items-center rounded-sm p-1.5 text-left text-compact hover:bg-accent" onclick={() => pick(option.value)}>
						{#if option.image !== undefined}
							<PersonAvatar src={imageSrc(option.image)} fallback={null} />
						{/if}
						<span class="flex-1 truncate leading-4.5 font-medium">{option.label}</span>
						<Check class="ml-auto size-4 flex-none {values.includes(option.value) ? 'opacity-100' : 'opacity-0'}" />
					</button>
				{/each}
			{:else}
				<p class="p-2 text-center text-xs text-muted-foreground">{m.common_no_data()}</p>
			{/each}
		</div>
	</Popover.Content>
</Popover.Root>
