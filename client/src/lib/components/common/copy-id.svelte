<script lang="ts">
	import { copyText } from '#lib/helpers/feedback.js';
	import { m } from '#lib/paraglide/messages.js';
	import Copy from '@lucide/svelte/icons/copy';
	import { Button } from '#lib/components/ui/button/index.js';

	// The ID a dialog failed to load, with a Copy button, so it can still be shared or looked up
	// (upstream PR 1523, issue 1496).
	let { id }: { id: string } = $props();

	const copy = () => copyText(id, 'ID copied to clipboard');
</script>

<div class="flex items-center gap-1 text-xs text-muted-foreground">
	<span class="font-mono break-all select-all">{id}</span>
	<Button variant="ghost" size="icon-sm" class="size-6" aria-label={m.dialog_user_info_copy_id()} title={m.dialog_user_info_copy_id()} onclick={copy}><Copy class="size-3.5" /></Button>
</div>
