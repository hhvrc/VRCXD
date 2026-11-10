<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import { untrack } from 'svelte';
	import { toast } from 'svelte-sonner';
	import { api } from '#lib/api/client.js';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as Dialog from '#lib/components/ui/dialog/index.js';
	import * as Select from '#lib/components/ui/select/index.js';
	import { Textarea } from '#lib/components/ui/textarea/index.js';

	// The original's SetAvatarStylesDialog: primary and secondary style, and the author tags.
	let {
		open = $bindable(false),
		path,
		stylesPath,
		styles,
		tags = [],
		onsaved
	}: { open?: boolean; path: string; stylesPath: string; styles?: { primary?: string | null; secondary?: string | null } | null; tags?: string[]; onsaved: () => void } = $props();

	const CLEAR = '__clear__';

	let primaryStyle = $state('');
	let secondaryStyle = $state('');
	let initialPrimaryStyle = '';
	let initialSecondaryStyle = '';
	let initialTags: string[] = [];
	let authorTags = $state('');
	let available = $state<string[]>([]);
	let idByName = new Map<string, string>();

	$effect(() => {
		if (open) untrack(show);
	});

	function show() {
		primaryStyle = initialPrimaryStyle = styles?.primary || '';
		secondaryStyle = initialSecondaryStyle = styles?.secondary || '';
		initialTags = tags;
		authorTags = initialTags.filter((t) => t.startsWith('author_tag_')).map((t) => t.substring(11)).join(',');
		getAvatarStyles();
	}

	async function getAvatarStyles() {
		try {
			const list = await api.get<{ id: string; styleName: string }[]>(stylesPath);
			available = list.map((s) => s.styleName);
			idByName = new Map(list.map((s) => [s.styleName, s.id]));
		} catch (err) {
			console.error('Error loading avatar styles:', err);
		}
	}

	const arraysMatch = (a: string[], b: string[]) => a.length === b.length && a.every((v, i) => v === b[i]);

	async function save() {
		const primaryStyleId = idByName.get(primaryStyle) || '';
		const secondaryStyleId = idByName.get(secondaryStyle) || '';
		const nextTags = initialTags.filter((t) => !t.startsWith('author_tag_'));
		for (const tag of authorTags.split(',')) {
			if (!tag.trim()) continue;
			const name = `author_tag_${tag}`;
			if (!nextTags.includes(name)) nextTags.push(name);
		}
		if (initialPrimaryStyle === primaryStyle && initialSecondaryStyle === secondaryStyle && arraysMatch(initialTags, nextTags)) {
			open = false;
			return;
		}
		try {
			await api.put(path, { primaryStyle: primaryStyleId, secondaryStyle: secondaryStyleId, tags: nextTags });
			toast.success(m.dialog_set_avatar_styles_save_success());
			open = false;
			onsaved();
		} catch (err) {
			toast.error(m.dialog_set_avatar_styles_save_failed());
			console.error('Error saving avatar styles:', err);
		}
	}
</script>

{#snippet stylePicker(value: string, set: (value: string) => void)}
	<Select.Root type="single" {value} onValueChange={(v) => set(v === CLEAR ? '' : String(v))}>
		<Select.Trigger size="sm" class="inline-flex">{value || 'Select Style'}</Select.Trigger>
		<Select.Content>
			<Select.Item value={CLEAR} label={m.dialog_avatar_tags_performanceRating_None()} />
			{#each available as style (style)}
				<Select.Item value={style} label={style} />
			{/each}
		</Select.Content>
	</Select.Root>
{/snippet}

<Dialog.Root bind:open>
	<Dialog.Content class="sm:max-w-100">
		<Dialog.Header>
			<Dialog.Title>{m.dialog_set_avatar_styles_header()}</Dialog.Title>
		</Dialog.Header>

		<div>
			<span>{m.dialog_set_avatar_styles_primary_style()}</span>
			<br />
			{@render stylePicker(primaryStyle, (v) => (primaryStyle = v))}
		</div>
		<div>
			<span>{m.dialog_set_avatar_styles_secondary_style()}</span>
			<br />
			{@render stylePicker(secondaryStyle, (v) => (secondaryStyle = v))}
		</div>
		<div class="text-xs">{m.dialog_set_world_tags_author_tags()}</div>
		<Textarea bind:value={authorTags} rows={2} class="mt-2 max-h-30 resize-none" />

		<Dialog.Footer>
			<Button variant="secondary" class="mr-2" onclick={() => (open = false)}>{m.dialog_set_avatar_styles_cancel()}</Button>
			<Button onclick={save}>{m.dialog_set_avatar_styles_save()}</Button>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>
