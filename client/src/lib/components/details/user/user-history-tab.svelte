<script lang="ts">
	import UserActivityTab from '#lib/components/details/user-activity-tab.svelte';
	import Location from '#lib/components/instance/location.svelte';
	import { NONE, PANEL, PROFILE_SECTION, SECTION_ROW } from '#lib/details.js';
	import { imageSrc } from '#lib/api/links.js';
	import { formatDate, formatDuration } from '#lib/helpers/format.js';
	import type { UserHistory } from '#lib/api/types.js';

	// The Activity tab: the original's activity charts, then this server's own history of the user.
	let { userId, active, history }: { userId: string; active: boolean; history: UserHistory | undefined } = $props();
</script>

{#snippet sectionTitle(label: string)}
	<span class={PROFILE_SECTION}>{label}</span>
{/snippet}

{#snippet historyCard(label: string, body: import('svelte').Snippet)}
	<div class={PANEL}>
		<div class={SECTION_ROW}>{@render sectionTitle(label)}</div>
		{@render body()}
	</div>
{/snippet}

<div class="flex flex-col gap-2.5">
	<UserActivityTab {userId} {active} />
	{#if history?.visits.length}
		{#snippet visits()}
			<table class="w-full text-xs"><tbody>
				{#each history?.visits ?? [] as visit, index (`${visit.instanceLocation + visit.joinedAt}:${index}`)}
					<tr class="border-b border-muted-foreground/20 last:border-0">
						<td class="p-1.5"><Location location={visit.instanceLocation} hint={visit.worldName} disableTooltip /></td>
						<td class="whitespace-nowrap p-1.5 text-muted-foreground">{formatDate(visit.joinedAt)}</td>
						<td class="whitespace-nowrap p-1.5 text-muted-foreground">{visit.leftAt ? formatDuration(visit.joinedAt, visit.leftAt) : 'still there'}</td>
					</tr>
				{/each}
			</tbody></table>
		{/snippet}
		{@render historyCard('Recent Instances', visits)}
	{/if}
	{#if history?.presence.length}
		{#snippet presence()}
			<table class="w-full text-xs"><tbody>
				{#each history?.presence ?? [] as event, index (`${event.at + event.kind}:${index}`)}
					<tr class="border-b border-muted-foreground/20 last:border-0">
						<td class="whitespace-nowrap p-1.5 text-muted-foreground">{formatDate(event.at)}</td>
						<td class="p-1.5">{event.kind}</td>
						<td class="p-1.5 text-muted-foreground">{#if event.location}<Location location={event.location} disableTooltip />{/if}</td>
					</tr>
				{/each}
			</tbody></table>
		{/snippet}
		{@render historyCard('Online and Offline', presence)}
	{/if}
	{#if history?.displayNames.length || history?.trust.length}
		{#snippet names()}
			<table class="w-full text-xs"><tbody>
				{#each history?.displayNames ?? [] as change, index (`${change.changedAt + change.displayName}:${index}`)}
					<tr class="border-b border-muted-foreground/20 last:border-0">
						<td class="whitespace-nowrap p-1.5 text-muted-foreground">{formatDate(change.changedAt)}</td>
						<td class="p-1.5">{change.previousDisplayName} → {change.displayName}</td>
					</tr>
				{/each}
				{#each history?.trust ?? [] as change, index (`${change.changedAt + change.trustLevel}:${index}`)}
					<tr class="border-b border-muted-foreground/20 last:border-0">
						<td class="whitespace-nowrap p-1.5 text-muted-foreground">{formatDate(change.changedAt)}</td>
						<td class="p-1.5">{change.previousTrustLevel} → {change.trustLevel}</td>
					</tr>
				{/each}
			</tbody></table>
		{/snippet}
		{@render historyCard('Name and Trust Changes', names)}
	{/if}
	{#if history?.status.length || history?.bio.length}
		{#snippet statuses()}
			<table class="w-full text-xs"><tbody>
				{#each history?.status ?? [] as change, index (`${change.changedAt + change.status}:${index}`)}
					<tr class="border-b border-muted-foreground/20 last:border-0">
						<td class="whitespace-nowrap p-1.5 text-muted-foreground">{formatDate(change.changedAt)}</td>
						<td class="p-1.5">{change.status} {change.statusDescription ?? ''}</td>
					</tr>
				{/each}
				{#each history?.bio ?? [] as change, index (`${change.changedAt}:${index}`)}
					<tr class="border-b border-muted-foreground/20 last:border-0">
						<td class="whitespace-nowrap p-1.5 text-muted-foreground">{formatDate(change.changedAt)}</td>
						<td class="whitespace-pre-wrap p-1.5">{change.bio}</td>
					</tr>
				{/each}
			</tbody></table>
		{/snippet}
		{@render historyCard('Status and Bio Changes', statuses)}
	{/if}
	{#if history?.avatars.length}
		{#snippet worn()}
			<div class="flex flex-wrap gap-2">
				{#each history?.avatars ?? [] as change, index (`${change.changedAt}:${index}`)}
					<figure class="w-28 text-center">
						<img class="aspect-[4/3] w-full rounded-md object-cover" src={imageSrc(change.thumbnailImageUrl)} alt="" />
						<figcaption class="mt-1 text-3xs text-muted-foreground">{formatDate(change.changedAt)}</figcaption>
					</figure>
				{/each}
			</div>
		{/snippet}
		{@render historyCard('Avatars Worn', worn)}
	{/if}
	{#if !history || history.presence.length + history.visits.length + history.displayNames.length + history.trust.length + history.status.length + history.bio.length + history.avatars.length === 0}
		<div class="text-xs text-muted-foreground {PANEL}">{NONE}</div>
	{/if}
</div>
