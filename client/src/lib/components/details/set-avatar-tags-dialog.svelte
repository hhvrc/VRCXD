<script lang="ts">
	import { PERSON_NAME, PERSON_SUBTEXT } from '#lib/details.js';
	import PersonAvatar from '#lib/components/common/person-avatar.svelte';
	import { m } from '#lib/paraglide/messages.js';
	import { untrack } from 'svelte';
	import { toast } from 'svelte-sonner';
	import { accountPath, api } from '#lib/api/client.js';
	import { avatarHref, imageSrc } from '#lib/api/links.js';
	import type { OwnAvatar } from '#lib/api/types.js';
	import { Button } from '#lib/components/ui/button/index.js';
	import { Checkbox } from '#lib/components/ui/checkbox/index.js';
	import * as Dialog from '#lib/components/ui/dialog/index.js';
	import { Spinner } from '#lib/components/ui/spinner/index.js';
	import { Textarea } from '#lib/components/ui/textarea/index.js';

	// The original's SetAvatarTagsDialog: the content warning tags of this avatar, and of any other of
	// your own avatars picked below, replaced together.
	type TaggedAvatar = { id: string; name: string; thumbnailImageUrl?: string | null; releaseStatus?: string | null; tags: string[] };

	let {
		open = $bindable(false),
		owner,
		avatar,
		onsaved
	}: { open?: boolean; owner: string; avatar: TaggedAvatar; onsaved: () => void } = $props();

	const CHECKED = ['horror', 'gore', 'violence', 'adult', 'sex'] as const;

	let loading = $state(false);
	let ownAvatars = $state<TaggedAvatar[]>([]);
	let selectedAvatarIds = $state<string[]>([]);
	let selectedTags = $state<string[]>([]);
	let selectedTagsCsv = $state('');
	let checked = $state<Record<(typeof CHECKED)[number], boolean>>({ horror: false, gore: false, violence: false, adult: false, sex: false });

	const tagStrings = $derived(
		new Map(ownAvatars.map((ref) => [ref.id, ref.tags.filter((t) => t.startsWith('content_')).map((t) => t.substring(8)).join(', ')]))
	);

	$effect(() => {
		if (open) untrack(show);
	});

	async function show() {
		selectedAvatarIds = [avatar.id];
		selectedTags = [];
		selectedTagsCsv = '';
		checked = { horror: false, gore: false, violence: false, adult: false, sex: false };
		for (const tag of avatar.tags) {
			const name = tag.substring(8);
			if (tag.startsWith('content_') && (CHECKED as readonly string[]).includes(name)) checked[name as (typeof CHECKED)[number]] = true;
			else if (tag.startsWith('content_')) selectedTags.push(name);
		}
		updateSelectedAvatarTags();
		updateInputAvatarTags();
		// The original lists every own avatar it has cached; this asks for them.
		ownAvatars = [avatar];
		loading = true;
		try {
			const own = await api.get<OwnAvatar[]>(accountPath(owner, '/avatars'));
			const others = own.map((o) => o.avatar).filter((a) => a.id !== avatar.id);
			ownAvatars = [avatar, ...others.map((a) => ({ id: a.id, name: a.name, thumbnailImageUrl: a.thumbnailImageUrl, releaseStatus: a.releaseStatus, tags: a.tags ?? [] }))];
		} catch (err) {
			console.error(err);
		} finally {
			loading = false;
		}
	}

	function updateSelectedAvatarTags() {
		for (const name of CHECKED) {
			const tag = `content_${name}`;
			if (checked[name]) {
				if (!selectedTags.includes(tag)) selectedTags.push(tag);
			} else if (selectedTags.includes(tag)) {
				selectedTags.splice(selectedTags.indexOf(tag), 1);
			}
		}
		selectedTagsCsv = selectedTags.join(',').replace(/content_/g, '');
	}

	function updateInputAvatarTags() {
		checked = { horror: false, gore: false, violence: false, adult: false, sex: false };
		const next: string[] = [];
		for (const tag of selectedTagsCsv.split(',')) {
			if ((CHECKED as readonly string[]).includes(tag)) checked[tag as (typeof CHECKED)[number]] = true;
			if (tag && !next.includes(`content_${tag}`)) next.push(`content_${tag}`);
		}
		selectedTags = next;
	}

	function toggleAvatarSelection(id: string, value: boolean) {
		const isSelected = selectedAvatarIds.includes(id);
		if (value && !isSelected) selectedAvatarIds.push(id);
		else if (!value && isSelected) selectedAvatarIds.splice(selectedAvatarIds.indexOf(id), 1);
	}

	function selectToggle() {
		const allSelected = ownAvatars.length === selectedAvatarIds.length;
		selectedAvatarIds = allSelected ? [] : ownAvatars.map((a) => a.id);
	}

	async function save() {
		if (loading) return;
		loading = true;
		try {
			for (const avatarId of selectedAvatarIds) {
				const ref = ownAvatars.find((a) => a.id === avatarId);
				if (!open || !ref) break;
				const tags = [...selectedTags, ...ref.tags.filter((t) => !t.startsWith('content_'))];
				await api.put(`${accountPath(owner)}/avatars/${encodeURIComponent(ref.id)}`, { tags });
			}
		} catch (err) {
			console.error(err);
			toast.error('Error saving avatar tags');
		} finally {
			loading = false;
			open = false;
			onsaved();
		}
	}
</script>

<Dialog.Root bind:open>
	<Dialog.Content class="gap-1 sm:max-w-195">
		<Dialog.Header>
			<Dialog.Title>{m.dialog_set_avatar_tags_header()}</Dialog.Title>
		</Dialog.Header>

		{#each CHECKED as name, i (name)}
			<label class="inline-flex items-center gap-2 {i === 0 ? 'mt-2' : ''}">
				<Checkbox
					checked={checked[name]}
					onCheckedChange={(value) => {
						checked[name] = value === true;
						updateSelectedAvatarTags();
					}}
				/>
				<span>{{ horror: 'Horror', gore: 'Gore', violence: 'Violence', adult: 'Adult', sex: 'Sexual' }[name]}</span>
			</label>
		{/each}
		<Textarea bind:value={selectedTagsCsv} rows={2} placeholder={m.dialog_set_avatar_tags_custom_tags_placeholder()} class="mt-2 resize-none" oninput={updateInputAvatarTags} />
		<div>
			<Button size="sm" variant="outline" onclick={selectToggle}>{ownAvatars.length === selectedAvatarIds.length ? 'Select None' : 'Select All'}</Button>
			<span style="margin-left: 6px">{selectedAvatarIds.length} / {ownAvatars.length}</span>
			{#if loading}<Spinner class="ml-2 inline-block" />{/if}
		</div>
		<div class="flex max-h-[300px] flex-wrap items-start overflow-auto" style="margin-top: 8px; min-height: 60px">
			{#each ownAvatars as ref (ref.id)}
				<a class="box-border flex w-[335px] cursor-pointer items-center p-1.5 text-compact hover:rounded-[25px_5px_5px_25px] hover:bg-muted" href={avatarHref(ref.id)} onclick={() => (open = false)}>
					<PersonAvatar src={imageSrc(ref.thumbnailImageUrl)} fallback={null} />
					<div class="flex-1 overflow-hidden">
						<span class={PERSON_NAME}>{ref.name}</span>
						<span class={PERSON_SUBTEXT}>{ref.releaseStatus}</span>
						<span class={PERSON_SUBTEXT}>{tagStrings.get(ref.id)}</span>
					</div>
					<!-- svelte-ignore a11y_click_events_have_key_events, a11y_no_static_element_interactions -->
					<span class="ml-1.5 inline-flex items-center px-2" onclick={(e) => { e.preventDefault(); e.stopPropagation(); }}>
						<Checkbox checked={selectedAvatarIds.includes(ref.id)} onCheckedChange={(value) => toggleAvatarSelection(ref.id, value === true)} />
					</span>
				</a>
			{/each}
		</div>

		<Dialog.Footer>
			<Button variant="secondary" class="mr-2" onclick={() => (open = false)}>{m.dialog_set_avatar_tags_cancel()}</Button>
			<Button onclick={save}>{m.dialog_set_avatar_tags_save()}</Button>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>
