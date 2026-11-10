<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import { Button } from '#lib/components/ui/button/index.js';
	import { Input } from '#lib/components/ui/input/index.js';
	import * as Popover from '#lib/components/ui/popover/index.js';

	// The original's ColorPickerButton: a swatch and its hex code that opens a picker. The picker is
	// the browser's own colour input with a hex field beside it, in place of vue3-colorpicker.
	let {
		value,
		disabled = false,
		presets = [],
		label = null,
		clearable = false,
		emptyValue,
		cols = 6,
		onchange
	}: {
		value: string;
		disabled?: boolean;
		presets?: string[];
		/** Shown in place of the hex code. */
		label?: string | null;
		/** Adds an ✕ that sets the colour back to emptyValue. */
		clearable?: boolean;
		/** What clearing sets, and what an empty value reads as: the first preset, or black. */
		emptyValue?: string;
		/** Presets per row. */
		cols?: number;
		onchange: (color: string) => void;
	} = $props();

	const empty = $derived(emptyValue ?? (presets.length ? String(presets[0]) : '#000000'));
	const displayLabel = $derived(label ?? (value || empty));

	const safeValue = $derived(/^#[0-9a-f]{6}$/.test(String(value || '').trim().toLowerCase()) ? String(value).trim().toLowerCase() : '#ffffff');
	let draft = $state('');
	$effect(() => {
		draft = safeValue;
	});

	function set(color: string) {
		if (disabled) return;
		onchange(color);
	}

	function commitDraft() {
		const hex = draft.trim().replace(/^#?/, '#').toLowerCase();
		if (/^#[0-9a-f]{6}$/.test(hex)) set(hex);
		else draft = safeValue;
	}
</script>

<Popover.Root>
	<Popover.Trigger {disabled}>
		{#snippet child({ props })}
			<Button {...props} variant="outline" class="flex items-center gap-2 px-2" {disabled}>
				<span class="h-4 w-4 shrink-0 rounded" style:background-color={safeValue}></span>
				<span class="text-xs uppercase opacity-80">{displayLabel}</span>
				{#if clearable && value}
					<span
						role="button"
						tabindex="0"
						aria-label={m.view_favorite_clear()}
						class={['opacity-60 hover:opacity-100', disabled ? 'pointer-events-none cursor-not-allowed' : 'cursor-pointer']}
						onclick={(e) => {
							e.stopPropagation();
							set(empty);
						}}
						onkeydown={(e) => {
							if (e.key !== 'Enter' && e.key !== ' ') return;
							e.preventDefault();
							e.stopPropagation();
							set(empty);
						}}>✕</span
					>
				{/if}
			</Button>
		{/snippet}
	</Popover.Trigger>
	<Popover.Content class="z-10000 w-auto p-3" align="start">
		<input type="color" class="h-32 w-48 cursor-pointer rounded border-0 bg-transparent p-0" value={safeValue} {disabled} oninput={(e) => set(e.currentTarget.value)} />
		<Input class="h-7 w-48 font-mono text-xs uppercase" maxlength={7} bind:value={draft} {disabled} onchange={commitDraft} onkeydown={(e) => e.key === 'Enter' && commitDraft()} />
		{#if presets.length}
			<div class="mt-2 grid w-48 gap-1.5" style:grid-template-columns="repeat({Math.max(1, cols)}, minmax(0, 1fr))">
				{#each presets as preset (preset)}
					<button
						type="button"
						class="h-5 w-5 cursor-pointer rounded border border-border"
						style:background-color={preset}
						title={preset}
						aria-label={preset}
						{disabled}
						onclick={() => set(preset.toLowerCase())}
					></button>
				{/each}
			</div>
		{/if}
	</Popover.Content>
</Popover.Root>
