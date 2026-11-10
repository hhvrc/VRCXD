<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import { toast } from 'svelte-sonner';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as Dialog from '#lib/components/ui/dialog/index.js';
	import { Input } from '#lib/components/ui/input/index.js';
	import { Label } from '#lib/components/ui/label/index.js';
	import * as Select from '#lib/components/ui/select/index.js';
	import { BIO_LANGUAGES, translation } from '#lib/settings/translation.svelte.js';

	// The original's TranslationApiDialog: the target language (saved as picked) and the Google
	// Translate key, saved with Save.
	let { open = $bindable(false) }: { open?: boolean } = $props();

	let key = $state('');
	let showKey = $state(false);

	let wasOpen = false;
	$effect(() => {
		if (open && !wasOpen) {
			key = translation.key;
			showKey = false;
		}
		wasOpen = open;
	});

	function save() {
		translation.saveKey(key);
		toast.success(m.dialog_translation_api_msg_settings_saved());
		open = false;
	}

	const languageName = (code: string) => BIO_LANGUAGES.find((l) => l.code === code)?.name ?? code;
</script>

<Dialog.Root bind:open>
	<Dialog.Content>
		<Dialog.Header>
			<Dialog.Title>{m.dialog_translation_api_header()}</Dialog.Title>
		</Dialog.Header>
		<div class="flex items-center justify-between gap-3">
			<span class="text-sm">{m.view_settings_appearance_appearance_bio_language()}</span>
			<Select.Root type="single" value={translation.bioLanguage} onValueChange={(v) => translation.setBioLanguage(v)}>
				<Select.Trigger size="sm">{languageName(translation.bioLanguage)}</Select.Trigger>
				<Select.Content>
					{#each BIO_LANGUAGES as l (l.code)}<Select.Item value={l.code} label={l.name} />{/each}
				</Select.Content>
			</Select.Root>
		</div>
		<div class="mt-2 grid gap-2">
			<Label>{m.dialog_translation_api_mode()}</Label>
			<Select.Root type="single" value="google">
				<Select.Trigger size="sm" class="w-full">{m.dialog_translation_api_mode_google()}</Select.Trigger>
				<Select.Content>
					<Select.Item value="google" label={m.dialog_translation_api_mode_google()} />
				</Select.Content>
			</Select.Root>
		</div>
		<div class="grid gap-2">
			<Label for="translation-key">{m.dialog_translation_api_description()}</Label>
			<div class="flex gap-2">
				<Input id="translation-key" type={showKey ? 'text' : 'password'} bind:value={key} placeholder="AIzaSy..." autocomplete="off" />
				<Button variant="outline" size="sm" onclick={() => (showKey = !showKey)}>{showKey ? 'Hide' : 'Show'}</Button>
			</div>
		</div>
		<Dialog.Footer>
			<Button variant="outline" href="https://translatepress.com/docs/automatic-translation/generate-google-api-key/" target="_blank" rel="noopener noreferrer">{m.dialog_translation_api_guide()}</Button>
			<Button onclick={save}>{m.dialog_translation_api_save()}</Button>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>
