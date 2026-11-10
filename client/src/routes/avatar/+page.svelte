<script lang="ts">
	import { DETAIL_PAGE } from '#lib/details.js';
	import { browser } from '$app/env';
	import { page } from '$app/state';
	import AvatarDetails from '#lib/components/details/avatar-details.svelte';

	// Where a avatar link opened in a new tab lands. In the app a click opens the pop-up instead.
	const id = $derived(browser ? page.url.searchParams.get('id') ?? '' : '');
</script>

<svelte:head>
	<title>Avatar · VRCXD</title>
</svelte:head>

<div class={DETAIL_PAGE}>
	<AvatarDetails avatarId={id} />
</div>
