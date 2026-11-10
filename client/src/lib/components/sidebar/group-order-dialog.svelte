<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import GripVertical from '@lucide/svelte/icons/grip-vertical';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as Dialog from '#lib/components/ui/dialog/index.js';
	import { sidebarData } from '#lib/people/sidebar-data.svelte.js';
	import { sidebarSettings } from '#lib/settings/sidebar-settings.svelte.js';

	// The original's FavoriteFriendGroupOrderDialog: drag the shown favorite groups into order.
	let { open = $bindable(false) }: { open?: boolean } = $props();

	type Item = { key: string; name: string };
	let order = $state<Item[]>([]);
	let dragging = $state<number | null>(null);

	function build(): Item[] {
		const all = sidebarData.groups.map((g) => ({ key: g.key, name: g.name }));
		const byKey = new Map(all.map((g) => [g.key, g]));
		const selectedKeys = sidebarSettings.favoriteGroups.length ? sidebarSettings.favoriteGroups : all.map((g) => g.key);
		const selected = new Set(selectedKeys);
		const list: Item[] = [];
		for (const key of sidebarSettings.favoriteGroupOrder) {
			const item = byKey.get(key);
			if (item && selected.delete(key)) list.push(item);
		}
		for (const key of selectedKeys) {
			const item = byKey.get(key);
			if (item && selected.has(key)) list.push(item);
		}
		return list;
	}

	$effect(() => {
		if (open) order = build();
	});

	function drop(target: number) {
		if (dragging === null || dragging === target) return;
		const next = [...order];
		const [moved] = next.splice(dragging, 1);
		next.splice(target, 0, moved);
		order = next;
		dragging = null;
	}

	function confirm() {
		const merged = order.map((g) => g.key);
		for (const key of sidebarSettings.favoriteGroupOrder) if (!merged.includes(key)) merged.push(key);
		sidebarSettings.setFavoriteGroupOrder(merged);
		open = false;
	}

	function reset() {
		sidebarSettings.setFavoriteGroupOrder([]);
		order = build();
	}
</script>

<Dialog.Root bind:open>
	<Dialog.Content class="flex max-h-[80vh] w-80 flex-col">
		<Dialog.Header><Dialog.Title>{m.side_panel_settings_edit_group_order()}</Dialog.Title></Dialog.Header>
		<div class="flex-1 overflow-auto py-3">
			<div class="flex flex-col gap-1.5">
				{#each order as item, index (item.key)}
					<div
						class={['flex cursor-grab items-center gap-2 rounded-md border px-2 py-1.5 text-sm', dragging === index && 'opacity-50']}
						draggable="true"
						role="listitem"
						ondragstart={() => (dragging = index)}
						ondragend={() => (dragging = null)}
						ondragover={(e) => e.preventDefault()}
						ondrop={(e) => {
							e.preventDefault();
							drop(index);
						}}
					>
						<GripVertical class="size-4 text-muted-foreground" />
						<span class="truncate">{item.name}</span>
					</div>
				{/each}
			</div>
		</div>
		<Dialog.Footer>
			<Button variant="secondary" size="sm" onclick={reset}>{m.dialog_image_crop_reset()}</Button>
			<Button size="sm" onclick={confirm}>{m.dialog_alertdialog_confirm()}</Button>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>
