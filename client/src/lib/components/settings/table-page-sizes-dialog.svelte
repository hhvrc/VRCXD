<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import Check from '@lucide/svelte/icons/check';
	import ChevronDown from '@lucide/svelte/icons/chevron-down';
	import X from '@lucide/svelte/icons/x';
	import { toast } from 'svelte-sonner';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as Dialog from '#lib/components/ui/dialog/index.js';
	import * as Popover from '#lib/components/ui/popover/index.js';
	import { appearance, normalizeTablePageSizes, TABLE_PAGE_SIZE_SUGGESTIONS } from '#lib/settings/appearance.svelte.js';

	// The original's Table Page Sizes dialog: the page sizes every table offers, as tags that can be
	// typed or picked from the suggestions, 1 to 1000 each.
	let { open = $bindable(false) }: { open?: boolean } = $props();

	let search = $state('');
	let listOpen = $state(false);

	const sizes = $derived(appearance.tablePageSizes.value);
	const options = $derived(
		Array.from(new Set([...TABLE_PAGE_SIZE_SUGGESTIONS, ...sizes].map(String)))
			.sort((a, b) => Number(a) - Number(b))
			.filter((option) => !search || option.includes(search.trim()))
	);

	function setSizes(values: (string | number)[]) {
		const next = normalizeTablePageSizes(values);
		appearance.tablePageSizes.value = next;
		if (values.length && values.length !== next.length) toast.error(m.view_settings_appearance_appearance_table_page_sizes_error());
	}

	function toggle(option: string) {
		const n = Number(option);
		setSizes(sizes.includes(n) ? sizes.filter((s) => s !== n) : [...sizes, n]);
		search = '';
	}

	function addTyped() {
		const value = search.trim();
		if (!value) return;
		setSizes([...sizes, value]);
		search = '';
	}
</script>

<Dialog.Root bind:open>
	<Dialog.Content class="sm:max-w-md">
		<Dialog.Header>
			<Dialog.Title>{m.view_settings_appearance_appearance_table_page_sizes()}</Dialog.Title>
		</Dialog.Header>

		<Popover.Root bind:open={listOpen}>
			<div class="flex w-full flex-wrap items-center gap-1.5 rounded-md border border-input bg-transparent px-2 py-1 text-sm shadow-xs dark:bg-input/30">
				{#each sizes as size (size)}
					<span class="inline-flex h-6 items-center gap-1 rounded bg-secondary px-2 text-xs text-secondary-foreground">
						{size}
						<button type="button" class="cursor-pointer opacity-70 hover:opacity-100" aria-label="Remove {size}" onclick={() => setSizes(sizes.filter((s) => s !== size))}>
							<X class="size-3" />
						</button>
					</span>
				{/each}
				<input
					class="h-7 min-w-20 flex-1 bg-transparent outline-none placeholder:text-muted-foreground"
					placeholder={m.view_settings_appearance_appearance_table_page_sizes()}
					inputmode="numeric"
					bind:value={search}
					oninput={() => (listOpen = !!search)}
					onkeydown={(e) => {
						if (e.key === 'Enter') {
							e.preventDefault();
							addTyped();
						} else if (e.key === 'ArrowDown') listOpen = true;
						else if (e.key === 'Backspace' && !search && sizes.length) setSizes(sizes.slice(0, -1));
					}}
				/>
				<Popover.Trigger>
					{#snippet child({ props })}
						<Button {...props} size="icon-sm" variant="ghost" class="ml-auto self-start" aria-label="Show page sizes">
							<ChevronDown class="size-3.5" />
						</Button>
					{/snippet}
				</Popover.Trigger>
			</div>
			<Popover.Content class="w-(--bits-popover-anchor-width) p-1" align="end" onOpenAutoFocus={(e) => e.preventDefault()}>
				<div class="max-h-75 overflow-y-auto">
					{#each options as option (option)}
						<button
							type="button"
							class="relative flex w-full cursor-default items-center gap-2 rounded-sm px-2 py-1.5 text-sm outline-hidden select-none hover:bg-accent hover:text-accent-foreground"
							onclick={() => toggle(option)}
						>
							<span>{option}</span>
							{#if sizes.includes(Number(option))}<Check class="ml-auto size-4" />{/if}
						</button>
					{:else}
						<p class="p-1 text-sm">No options</p>
					{/each}
				</div>
			</Popover.Content>
		</Popover.Root>

		<Dialog.Footer>
			<Button variant="outline" onclick={() => (open = false)}>{m.dialog_alertdialog_ok()}</Button>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>
