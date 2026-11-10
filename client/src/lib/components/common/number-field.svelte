<script lang="ts">
	import Minus from '@lucide/svelte/icons/minus';
	import Plus from '@lucide/svelte/icons/plus';
	import { cn } from '#lib/utils.js';

	// The original's NumberField (reka-ui): a number between a decrement and an increment button,
	// committed on blur or Enter and kept within min/max.
	let {
		value,
		min = -Infinity,
		max = Infinity,
		step = 1,
		decimals = 0,
		disabled = false,
		ariaLabel,
		class: className = '',
		onchange
	}: {
		value: number;
		min?: number;
		max?: number;
		step?: number;
		decimals?: number;
		disabled?: boolean;
		ariaLabel?: string;
		class?: string;
		onchange: (value: number) => void;
	} = $props();

	const format = (n: number) => String(Number(n.toFixed(decimals)));
	let draft = $state('');
	$effect(() => {
		draft = format(value);
	});

	function commit(next: number) {
		if (!Number.isFinite(next)) {
			draft = format(value);
			return;
		}
		const clamped = Math.min(max, Math.max(min, Number(next.toFixed(decimals))));
		draft = format(clamped);
		if (clamped !== value) onchange(clamped);
	}
</script>

<div class={cn('flex h-8 items-center rounded-md border border-input bg-transparent text-sm shadow-xs dark:bg-input/30', disabled && 'opacity-50', className)}>
	<button
		type="button"
		class="flex h-full cursor-pointer items-center justify-center px-2 text-muted-foreground hover:text-foreground disabled:cursor-not-allowed disabled:opacity-50"
		aria-label="Decrease"
		disabled={disabled || value <= min}
		onclick={() => commit(value - step)}
	>
		<Minus class="size-3.5" />
	</button>
	<input
		class="h-full w-full min-w-0 bg-transparent text-center tabular-nums outline-none"
		inputmode="decimal"
		aria-label={ariaLabel}
		{disabled}
		bind:value={draft}
		onblur={() => commit(Number(draft))}
		onkeydown={(e) => {
			if (e.key === 'Enter') commit(Number(draft));
			else if (e.key === 'ArrowUp') {
				e.preventDefault();
				commit(value + step);
			} else if (e.key === 'ArrowDown') {
				e.preventDefault();
				commit(value - step);
			}
		}}
	/>
	<button
		type="button"
		class="flex h-full cursor-pointer items-center justify-center px-2 text-muted-foreground hover:text-foreground disabled:cursor-not-allowed disabled:opacity-50"
		aria-label="Increase"
		disabled={disabled || value >= max}
		onclick={() => commit(value + step)}
	>
		<Plus class="size-3.5" />
	</button>
</div>
