<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import { toast } from 'svelte-sonner';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as Dialog from '#lib/components/ui/dialog/index.js';
	import { Input } from '#lib/components/ui/input/index.js';
	import { Label } from '#lib/components/ui/label/index.js';
	import * as Select from '#lib/components/ui/select/index.js';
	import { Textarea } from '#lib/components/ui/textarea/index.js';
	import { BIO_LANGUAGES, translation, type TranslationApiConfig } from '#lib/settings/translation.svelte.js';
	import { DEFAULT_CHAT_ENDPOINT, fetchModels, type TranslationApiType } from '#lib/settings/translation-api.js';

	// The original's TranslationApiDialog: the target language (saved as picked), then the provider
	// and its fields, saved with Save. Test tries the form as it stands, unsaved.
	let { open = $bindable(false) }: { open?: boolean } = $props();

	let form = $state<TranslationApiConfig>({ type: 'google', key: '', endpoint: DEFAULT_CHAT_ENDPOINT, model: '', prompt: '' });
	let showKey = $state(false);
	let models = $state<string[]>([]);
	let fetchingModels = $state(false);

	let wasOpen = false;
	$effect(() => {
		if (open && !wasOpen) {
			form = { ...translation.config, endpoint: translation.endpoint || DEFAULT_CHAT_ENDPOINT };
			showKey = false;
			models = [];
		}
		wasOpen = open;
	});

	const incomplete = () => form.type === 'openai' && (!form.endpoint || !form.model);

	function save() {
		if (incomplete()) {
			toast.warning(m.dialog_translation_api_msg_fill_endpoint_model());
			return;
		}
		translation.saveConfig($state.snapshot(form));
		toast.success(m.dialog_translation_api_msg_settings_saved());
		open = false;
	}

	async function loadModels() {
		if (!form.endpoint) {
			toast.warning(m.dialog_translation_api_msg_endpoint_required());
			return;
		}
		fetchingModels = true;
		try {
			models = await fetchModels(form.endpoint, form.key);
			if (models.length) toast.success(m.dialog_translation_api_msg_models_fetched({ count: models.length }));
			else toast.warning(m.dialog_translation_api_msg_no_models_found());
		} catch (err) {
			console.error('[TranslationAPI] Failed to fetch models', err);
			models = [];
			toast.warning(m.dialog_translation_api_msg_no_models_found());
		} finally {
			fetchingModels = false;
		}
	}

	async function test() {
		if (incomplete()) {
			toast.warning(m.dialog_translation_api_msg_fill_endpoint_model());
			return;
		}
		const result = await translation.translate('Hello world', 'fr', $state.snapshot(form));
		if (result) toast.success(m.dialog_translation_api_msg_test_success());
		else toast.error(m.dialog_translation_api_msg_test_failed());
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
			<Select.Root type="single" value={form.type} onValueChange={(v) => (form.type = v as TranslationApiType)}>
				<Select.Trigger size="sm" class="w-full">{form.type === 'openai' ? m.dialog_translation_api_mode_openai() : m.dialog_translation_api_mode_google()}</Select.Trigger>
				<Select.Content>
					<Select.Item value="google" label={m.dialog_translation_api_mode_google()} />
					<Select.Item value="openai" label={m.dialog_translation_api_mode_openai()} />
				</Select.Content>
			</Select.Root>
		</div>
		{#if form.type === 'openai'}
			<div class="grid gap-2">
				<Label for="translation-endpoint">{m.dialog_translation_api_openai_endpoint()}</Label>
				<Input id="translation-endpoint" bind:value={form.endpoint} placeholder={DEFAULT_CHAT_ENDPOINT} autocomplete="off" spellcheck={false} />
			</div>
		{/if}
		<div class="grid gap-2">
			<Label for="translation-key">{form.type === 'openai' ? m.dialog_translation_api_openai_api_key() : m.dialog_translation_api_description()}</Label>
			<div class="flex gap-2">
				<Input id="translation-key" type={showKey ? 'text' : 'password'} bind:value={form.key} placeholder={form.type === 'openai' ? 'sk-...' : 'AIzaSy...'} autocomplete="off" />
				<Button variant="outline" size="sm" onclick={() => (showKey = !showKey)}>{showKey ? 'Hide' : 'Show'}</Button>
			</div>
		</div>
		{#if form.type === 'openai'}
			<div class="grid gap-2">
				<Label for="translation-model">{m.dialog_translation_api_openai_model()}</Label>
				<div class="flex gap-2">
					{#if models.length}
						<Select.Root type="single" value={form.model} onValueChange={(v) => (form.model = v)}>
							<Select.Trigger id="translation-model" size="sm" class="min-w-0 flex-1">{form.model || m.dialog_translation_api_openai_model()}</Select.Trigger>
							<Select.Content>
								{#each models as model (model)}<Select.Item value={model} label={model} />{/each}
							</Select.Content>
						</Select.Root>
					{:else}
						<Input id="translation-model" class="min-w-0 flex-1" bind:value={form.model} autocomplete="off" spellcheck={false} />
					{/if}
					<Button variant="outline" size="sm" disabled={fetchingModels || !form.endpoint} onclick={loadModels}>{fetchingModels ? m.dialog_translation_api_fetching_models() : m.dialog_translation_api_fetch_models()}</Button>
				</div>
			</div>
			<div class="grid gap-2">
				<Label for="translation-prompt">{m.dialog_translation_api_openai_prompt_optional()}</Label>
				<Textarea id="translation-prompt" rows={3} bind:value={form.prompt} />
			</div>
		{/if}
		<Dialog.Footer>
			{#if form.type === 'google'}
				<Button variant="outline" href="https://translatepress.com/docs/automatic-translation/generate-google-api-key/" target="_blank" rel="noopener noreferrer">{m.dialog_translation_api_guide()}</Button>
			{:else}
				<Button variant="outline" onclick={test}>{m.dialog_translation_api_test()}</Button>
			{/if}
			<Button onclick={save}>{m.dialog_translation_api_save()}</Button>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>
