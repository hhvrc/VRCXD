<script lang="ts" generics="P extends Record<string, any>">
	import type { Component } from 'svelte';
	import * as Tooltip from '#lib/components/ui/tooltip/index.js';

	// Renders a component inside the tooltip provider the app layout gives every page.
	let { component, props }: { component: Component<P>; props: P } = $props();
	const Inner = $derived(component);
</script>

<Tooltip.Provider>
	<Inner {...props} />
</Tooltip.Provider>
