<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import { api, ApiError } from '#lib/api/client.js';
	import { CARD } from '#lib/details.js';
	import SectionCard from './section-card.svelte';

	// The memo box on the world and avatar pop-ups. Saves when it loses focus.
	let { path }: { path: string } = $props();

	let memo = $state('');
	let saved = '';
	let error = $state('');

	$effect(() => {
		const url = path;
		memo = saved = '';
		error = '';
		api.get<{ memo: string }>(url).then(
			(found) => {
				if (url === path) memo = saved = found.memo;
			},
			() => {}
		);
	});

	async function save() {
		if (memo === saved) return;
		try {
			await api.put(path, { memo });
			saved = memo;
			error = '';
		} catch (err) {
			error = ApiError.from(err).message;
		}
	}
</script>

<SectionCard title={m.dialog_user_info_memo()} class={CARD}>
	<textarea
		rows="1"
		bind:value={memo}
		onchange={save}
		placeholder={m.dialog_user_info_memo_placeholder()}
		class="field-sizing-content min-h-0 w-full resize-none rounded-md border border-input bg-transparent p-2 text-xs outline-none focus-visible:ring-1 focus-visible:ring-ring"
	></textarea>
	{#if error}<p class="mt-1 text-destructive">{error}</p>{/if}
</SectionCard>
