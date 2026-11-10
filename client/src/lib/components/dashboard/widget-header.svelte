<script lang="ts">
	import type { Snippet } from 'svelte';
	import ExternalLink from '@lucide/svelte/icons/external-link';

	let { title, icon = '', href = '', children }: { title: string; icon?: string; href?: string; children?: Snippet } = $props();
</script>

<div class="group/header flex shrink-0 items-center justify-between border-b px-2.5 py-0">
	<a {href} class="flex cursor-pointer items-center gap-1.5 text-xs font-semibold text-muted-foreground transition-colors hover:text-foreground">
		<i class={[icon, 'text-sm']} aria-hidden="true"></i>
		<span>{title}</span>
		<ExternalLink class="size-3 opacity-0 transition-opacity group-hover/header:opacity-100" />
	</a>
	<div class="opacity-0 transition-opacity group-focus-within/header:opacity-100 group-hover/header:opacity-100">
		{@render children?.()}
	</div>
</div>
