<script lang="ts">
	import { DETAIL_PAGE } from '#lib/details.js';
	import { browser } from '$app/env';
	import { page } from '$app/state';
	import GroupDetails from '#lib/components/details/group-details.svelte';

	// Where a group link opened in a new tab lands. In the app a click opens the pop-up instead.
	const id = $derived(browser ? page.url.searchParams.get('id') ?? '' : '');
</script>

<svelte:head>
	<title>Group · VRCXD</title>
</svelte:head>

<div class={DETAIL_PAGE}>
	<GroupDetails groupId={id} />
</div>
