<script lang="ts">
	import RefreshCw from '@lucide/svelte/icons/refresh-cw';
	import { Button } from '#lib/components/ui/button/index.js';

	// The refresh button at the end of a tool dialog's toolbar, spinning while it loads.
	let { label, loading, onclick }: { label: string; loading: boolean; onclick: () => void } = $props();
</script>

<Button size="sm" variant="outline" class="ml-auto" disabled={loading} aria-label={label} {onclick}>
	<RefreshCw class="size-4 {loading ? 'animate-spin' : ''}" />
</Button>
