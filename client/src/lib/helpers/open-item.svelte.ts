import { untrack } from 'svelte';

/**
 * A detail dialog's start: calls `open` when the dialog shows another item or the account changes,
 * and at no other time. What `open` reads on its way (the shared friend list, the moderations) is
 * not tracked, so a friend's status change or the resync after a reconnect does not clear the
 * dialog and show it loading again. Call it during component setup, where `$effect` may run.
 */
export function openItem(owner: () => string, id: () => string, open: (id: string) => void): void {
	$effect(() => {
		const current = id();
		if (owner() && current) untrack(() => open(current));
	});
}
