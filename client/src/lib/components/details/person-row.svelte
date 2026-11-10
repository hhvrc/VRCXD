<script lang="ts">
	import PersonAvatar from '#lib/components/common/person-avatar.svelte';
	import { clock } from '#lib/helpers/clock.svelte.js';
	import { Spinner } from '#lib/components/ui/spinner/index.js';
	import { imageSrc, userHref } from '#lib/api/links.js';
	import type { Friend } from '#lib/api/types.js';
	import { appearance } from '#lib/settings/appearance.svelte.js';
	import { ROW, PERSON_NAME, PERSON_SUBTEXT } from '#lib/details.js';
	import { timeToText } from '#lib/helpers/format.js';
	import { userStatusClass } from '#lib/components/sidebar/sidebar-utils.js';
	import { nameColour } from '#lib/people/user-colour.js';
	import Timer from '#lib/components/common/timer.svelte';

	// A person in an instance or member list: picture, name, and a line under it. Given the friend
	// row, it also has the original's status ring, icon frame and name colour, and with `timed` the
	// time they have been there (a spinner while traveling), counting up. `since` counts from a given
	// instant instead, as the original's Timer does in the user page's instance card.
	let {
		userId,
		name,
		image,
		detail,
		friend,
		timed = false,
		since
	}: { userId: string; name: string; image?: string | null; detail?: string; friend?: Friend; timed?: boolean; since?: string | null } = $props();

	const now = $derived(clock(1000).now);

	const sinceText = $derived(timed && friend?.locationSince ? timeToText(Math.max(0, now - Date.parse(friend.locationSince))) : '');
</script>

<a class={ROW} href={userHref(userId)}>
	<PersonAvatar src={imageSrc(image)} statusClass={friend && userStatusClass(friend, friend.pendingOffline)} iconFrame={friend?.iconFrame} />
	<div class="flex-1 overflow-hidden">
		<span class={PERSON_NAME} style:color={friend ? nameColour(friend, appearance.isDarkMode) : undefined}>{name || userId}</span>
		{#if detail}
			<span class={PERSON_SUBTEXT}>{detail}</span>
		{:else if since !== undefined}
			<span class={PERSON_SUBTEXT}>{#if friend?.location === 'traveling'}<Spinner class="mr-1 inline-block" />{/if}<Timer epoch={since} /></span>
		{:else if sinceText}
			<span class={PERSON_SUBTEXT}>{#if friend?.location === 'traveling'}<Spinner class="mr-1 inline-block" />{/if}{sinceText}</span>
		{/if}
	</div>
</a>
