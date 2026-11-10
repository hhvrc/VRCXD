<script lang="ts">
	import Self from './json-tree.svelte';

	// The original's vue-json-pretty: objects and arrays fold, opened two levels deep by default.
	let { value, name, depth = 0, open = 2 }: { value: unknown; name?: string; depth?: number; open?: number } = $props();

	const isArray = $derived(Array.isArray(value));
	const isObject = $derived(value !== null && typeof value === 'object');
	const entries = $derived(isObject ? Object.entries(value as Record<string, unknown>) : []);
	const brackets = $derived(isArray ? ['[', ']'] : ['{', '}']);
	let expanded = $state<boolean | undefined>();
	const shown = $derived(expanded ?? depth < open);

	function scalarClass(v: unknown) {
		if (typeof v === 'string') return 'text-green-600 dark:text-green-400';
		if (typeof v === 'number') return 'text-blue-600 dark:text-blue-400';
		if (typeof v === 'boolean') return 'text-amber-600 dark:text-amber-400';
		return 'text-muted-foreground';
	}
</script>

<div class="font-mono text-xs leading-5" style:padding-left={depth ? '1rem' : undefined}>
	{#if isObject}
		<button type="button" class="cursor-pointer text-left hover:bg-muted/50" onclick={() => (expanded = !shown)}>
			<span class="inline-block w-3 text-muted-foreground">{shown ? '▾' : '▸'}</span>{#if name !== undefined}<span>{name}</span>: {/if}{brackets[0]}{#if !shown}<span class="text-muted-foreground"> {entries.length} {isArray ? 'items' : 'keys'} </span>{brackets[1]}{/if}
		</button>
		{#if shown}
			{#each entries as [key, child] (key)}
				<Self value={child} name={isArray ? undefined : key} depth={depth + 1} {open} />
			{/each}
			<div style:padding-left="0.75rem">{brackets[1]}</div>
		{/if}
	{:else}
		<span class="inline-block w-3"></span>{#if name !== undefined}<span>{name}</span>: {/if}<span class={['wrap-anywhere', scalarClass(value)]}>{typeof value === 'string' ? JSON.stringify(value) : String(value)}</span>
	{/if}
</div>
