<script lang="ts">
	import Trash2 from '@lucide/svelte/icons/trash-2';
	import X from '@lucide/svelte/icons/x';
	import { shiftKey } from '#lib/settings/table-settings.svelte.js';

	// A log table's delete button: deletes at once while Shift is held, as the original does,
	// and otherwise asks first.
	let {
		label,
		remove,
		confirm,
		class: className = ''
	}: { label: string; remove: () => void; confirm: () => void; class?: string } = $props();
</script>

<button
	type="button"
	aria-label={label}
	class="inline-flex h-6 items-center justify-center text-muted-foreground hover:text-foreground {className}"
	onclick={() => (shiftKey.held ? remove() : confirm())}
>
	{#if shiftKey.held}<X class="size-4 text-red-600" />{:else}<Trash2 class="size-4" />{/if}
</button>
