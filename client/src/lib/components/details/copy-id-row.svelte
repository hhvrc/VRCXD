<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import Copy from '@lucide/svelte/icons/copy';
	import { Button } from '#lib/components/ui/button/index.js';

	let { label, oncopy }: { label: string; oncopy: () => void } = $props();
</script>

<div class="flex items-center justify-between gap-2 text-xs">
	<span class="text-muted-foreground">{label}</span>
	<Button class="-mr-1.5 h-5 gap-1 px-1.5 text-xs font-normal text-muted-foreground" size="sm" variant="ghost" onclick={oncopy}>
		<Copy class="size-3" /> {m.dialog_user_info_copy_id()}
	</Button>
</div>
