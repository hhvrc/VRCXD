<script lang="ts">
	import { INFO_ROW } from '#lib/details.js';

	let { label, value, title, truncate = false }: { label: string; value: string; title?: string; truncate?: boolean } = $props();
</script>

<div class={INFO_ROW} {title}>
	<span class="shrink-0 text-muted-foreground">{label}</span>
	<span class="text-right text-muted-foreground {truncate ? 'truncate' : ''}">{value}</span>
</div>
