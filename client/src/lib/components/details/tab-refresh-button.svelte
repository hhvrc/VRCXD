<script lang="ts">
	import RefreshCw from '@lucide/svelte/icons/refresh-cw';
	import { Button } from '#lib/components/ui/button/index.js';
	import { Spinner } from '#lib/components/ui/spinner/index.js';

	// A dialog tab's round refresh button, a spinner while it loads.
	let { label, loading, onclick }: { label: string; loading: boolean; onclick: () => void } = $props();
</script>

<Button class="rounded-full" variant="ghost" size="icon-sm" aria-label={label} disabled={loading} {onclick}>
	{#if loading}<Spinner />{:else}<RefreshCw />{/if}
</Button>
