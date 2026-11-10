<script lang="ts">
	import { toastError } from '#lib/helpers/feedback.js';
	import { m } from '#lib/paraglide/messages.js';
	import { untrack } from 'svelte';
	import { toast } from 'svelte-sonner';
	import { api } from '#lib/api/client.js';
	import { Button } from '#lib/components/ui/button/index.js';
	import { Checkbox } from '#lib/components/ui/checkbox/index.js';
	import * as Dialog from '#lib/components/ui/dialog/index.js';
	import { Textarea } from '#lib/components/ui/textarea/index.js';

	// The original's SetWorldTagsDialog: content warnings, world settings, author tags and the
	// default content settings, saved as the world's tags and disabledPropAbilities.
	let {
		open = $bindable(false),
		path,
		oldTags = [],
		oldDisabledPropAbilities = [],
		onsaved
	}: { open?: boolean; path: string; oldTags?: string[]; oldDisabledPropAbilities?: string[]; onsaved: () => void } = $props();

	let authorTags = $state('');
	let contentTags = '';
	let debugAllowed = $state(false);
	let avatarScalingDisabled = $state(false);
	let focusViewDisabled = $state(false);
	let contentHorror = $state(false);
	let contentGore = $state(false);
	let contentViolence = $state(false);
	let contentAdult = $state(false);
	let contentSex = $state(false);
	let emoji = $state(true);
	let stickers = $state(true);
	let pedestals = $state(true);
	let prints = $state(true);
	let drones = $state(true);
	let props_ = $state(true);
	let thirdPerson = $state(true);
	let propMovement = $state(true);
	let saving = $state(false);

	$effect(() => {
		if (open) untrack(show);
	});

	function show() {
		debugAllowed = avatarScalingDisabled = focusViewDisabled = false;
		contentHorror = contentGore = contentViolence = contentAdult = contentSex = false;
		emoji = stickers = pedestals = prints = drones = props_ = thirdPerson = true;
		const author: string[] = [];
		const content: string[] = [];
		for (const tag of oldTags) {
			if (tag.startsWith('author_tag_')) author.unshift(tag.substring(11));
			if (tag.startsWith('content_')) content.unshift(tag.substring(8));
			switch (tag) {
				case 'content_horror': contentHorror = true; break;
				case 'content_gore': contentGore = true; break;
				case 'content_violence': contentViolence = true; break;
				case 'content_adult': contentAdult = true; break;
				case 'content_sex': contentSex = true; break;
				case 'debug_allowed': debugAllowed = true; break;
				case 'feature_avatar_scaling_disabled': avatarScalingDisabled = true; break;
				case 'feature_focus_view_disabled': focusViewDisabled = true; break;
				case 'feature_emoji_disabled': emoji = false; break;
				case 'feature_stickers_disabled': stickers = false; break;
				case 'feature_pedestals_disabled': pedestals = false; break;
				case 'feature_prints_disabled': prints = false; break;
				case 'feature_drones_disabled': drones = false; break;
				case 'feature_props_disabled': props_ = false; break;
				case 'feature_third_person_view_disabled': thirdPerson = false; break;
			}
		}
		propMovement = !oldDisabledPropAbilities.includes('player_movement');
		authorTags = author.toString();
		contentTags = content.toString();
	}

	async function save() {
		const tags: string[] = [];
		const disabledPropAbilities = [...oldDisabledPropAbilities];
		for (const tag of authorTags.trim().split(',')) if (tag) tags.unshift(`author_tag_${tag}`);
		// Custom content tags go back as they were.
		for (const tag of contentTags.trim().split(',')) {
			if (!['horror', 'gore', 'violence', 'adult', 'sex', ''].includes(tag)) tags.unshift(`content_${tag}`);
		}
		if (contentHorror) tags.unshift('content_horror');
		if (contentGore) tags.unshift('content_gore');
		if (contentViolence) tags.unshift('content_violence');
		if (contentAdult) tags.unshift('content_adult');
		if (contentSex) tags.unshift('content_sex');
		if (debugAllowed) tags.unshift('debug_allowed');
		if (avatarScalingDisabled) tags.unshift('feature_avatar_scaling_disabled');
		if (focusViewDisabled) tags.unshift('feature_focus_view_disabled');
		if (!emoji) tags.unshift('feature_emoji_disabled');
		if (!stickers) tags.unshift('feature_stickers_disabled');
		if (!pedestals) tags.unshift('feature_pedestals_disabled');
		if (!prints) tags.unshift('feature_prints_disabled');
		if (!drones) tags.unshift('feature_drones_disabled');
		if (!props_) tags.unshift('feature_props_disabled');
		if (!thirdPerson) tags.unshift('feature_third_person_view_disabled');
		const disabledPlayerMovement = disabledPropAbilities.includes('player_movement');
		if (propMovement && disabledPlayerMovement) disabledPropAbilities.splice(disabledPropAbilities.indexOf('player_movement'), 1);
		else if (!propMovement && !disabledPlayerMovement) disabledPropAbilities.unshift('player_movement');
		saving = true;
		try {
			await api.put(path, { tags, disabledPropAbilities });
			toast.success('Tags updated');
			open = false;
			onsaved();
		} catch (err) {
			toastError(err);
		} finally {
			saving = false;
		}
	}
</script>

{#snippet option(label: string, checked: boolean, set: (value: boolean) => void, extra = '')}
	<label class="inline-flex items-center gap-2 {extra}">
		<Checkbox {checked} onCheckedChange={(value) => set(value === true)} />
		<span>{label}</span>
	</label>
{/snippet}

<Dialog.Root bind:open>
	<Dialog.Content class="gap-1 sm:max-w-100">
		<Dialog.Header>
			<Dialog.Title>{m.dialog_set_world_tags_header()}</Dialog.Title>
		</Dialog.Header>

		{@render option('Disable avatar scaling', avatarScalingDisabled, (v) => (avatarScalingDisabled = v), 'mt-2')}
		{@render option('Disable focus view', focusViewDisabled, (v) => (focusViewDisabled = v))}
		{@render option('Enable world debugging for others', debugAllowed, (v) => (debugAllowed = v))}
		<div class="mt-2 text-xs">{m.dialog_set_world_tags_author_tags()}<br /></div>
		<Textarea bind:value={authorTags} rows={2} class="mt-2 resize-none" />
		<div class="mt-2 text-xs">{m.dialog_set_world_tags_content_tags()}<br /></div>
		{@render option('Horror', contentHorror, (v) => (contentHorror = v))}
		{@render option('Gore', contentGore, (v) => (contentGore = v))}
		{@render option('Violence', contentViolence, (v) => (contentViolence = v))}
		{@render option('Adult', contentAdult, (v) => (contentAdult = v))}
		{@render option('Sexual', contentSex, (v) => (contentSex = v))}
		<div class="mt-2 text-xs">{m.dialog_set_world_tags_default_content_settings()}<br /></div>
		{@render option('Emoji', emoji, (v) => (emoji = v))}
		{@render option('Stickers', stickers, (v) => (stickers = v))}
		{@render option('Sharing Pedestals', pedestals, (v) => (pedestals = v))}
		{@render option('Prints', prints, (v) => (prints = v))}
		{@render option('Drones', drones, (v) => (drones = v))}
		{@render option('Items', props_, (v) => (props_ = v))}
		{@render option('Third Person', thirdPerson, (v) => (thirdPerson = v))}
		{@render option('Props that Modify Player Movement', propMovement, (v) => (propMovement = v))}

		<Dialog.Footer>
			<div class="flex gap-2">
				<Button variant="secondary" onclick={() => (open = false)}>{m.dialog_set_world_tags_cancel()}</Button>
				<Button disabled={saving} onclick={save}>{m.dialog_set_world_tags_save()}</Button>
			</div>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>
