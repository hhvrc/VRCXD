import { toast } from 'svelte-sonner';
import { readSetting, storeSetting } from '#lib/settings/settings-storage.js';
import { DEFAULT_CHAT_ENDPOINT, translateChat, translateGoogle, type TranslationApiType } from '#lib/settings/translation-api.js';

// The original's Bio Translation API settings (stores/settings/advanced.js): Google Translate with
// an API key, or a chat completions endpoint. The original calls them from its native side; here
// the browser does (translation-api.ts). The settings stay in this browser, as the original keeps
// them in its local config.

export const BIO_LANGUAGES: { code: string; name: string }[] = [
	{ code: 'cs', name: 'Čeština (cs)' },
	{ code: 'en', name: 'English (en)' },
	{ code: 'es', name: 'Español (es)' },
	{ code: 'fr', name: 'Français (fr)' },
	{ code: 'hu', name: 'Magyar (hu)' },
	{ code: 'ja', name: '日本語 (ja)' },
	{ code: 'ko', name: '한국어 (ko)' },
	{ code: 'pl', name: 'Polski (pl)' },
	{ code: 'pt', name: 'Português Brasileiro (pt-br)' },
	{ code: 'ru', name: 'Русский (ru)' },
	{ code: 'th', name: 'ภาษาไทย (th)' },
	{ code: 'vi', name: 'Tiếng Việt (vi)' },
	{ code: 'zh-CN', name: '简体中文 (zh-CN)' },
	{ code: 'zh-TW', name: '繁體中文 (zh-TW)' }
];

const str = (key: string, fallback: string) => {
	const value = readSetting<unknown>(key, fallback);
	return typeof value === 'string' ? value : fallback;
};

export type TranslationApiConfig = { type: TranslationApiType; key: string; endpoint: string; model: string; prompt: string };

class Translation {
	enabled = $state(readSetting('VRCX_translationAPI', false));
	bioLanguage = $state(BIO_LANGUAGES.some((l) => l.code === str('VRCX_bioLanguage', 'en')) ? str('VRCX_bioLanguage', 'en') : 'en');
	type = $state<TranslationApiType>(str('VRCX_translationAPIType', 'google') === 'openai' ? 'openai' : 'google');
	key = $state(str('VRCX_translationAPIKey', ''));
	endpoint = $state(str('VRCX_translationAPIEndpoint', ''));
	model = $state(str('VRCX_translationAPIModel', ''));
	prompt = $state(str('VRCX_translationAPIPrompt', ''));

	setEnabled(value: boolean) {
		this.enabled = value;
		storeSetting('VRCX_translationAPI', value);
	}

	setBioLanguage(code: string) {
		this.bioLanguage = code;
		storeSetting('VRCX_bioLanguage', code);
	}

	get config(): TranslationApiConfig {
		return { type: this.type, key: this.key, endpoint: this.endpoint, model: this.model, prompt: this.prompt };
	}

	/** saveTranslationApiConfig: every field, as the dialog has them. */
	saveConfig(config: TranslationApiConfig) {
		this.type = config.type;
		this.key = config.key;
		this.endpoint = config.endpoint;
		this.model = config.model;
		this.prompt = config.prompt;
		storeSetting('VRCX_translationAPIType', config.type);
		storeSetting('VRCX_translationAPIKey', config.key);
		storeSetting('VRCX_translationAPIEndpoint', config.endpoint);
		storeSetting('VRCX_translationAPIModel', config.model);
		storeSetting('VRCX_translationAPIPrompt', config.prompt);
	}

	get providerLabel() {
		return this.type === 'openai' ? 'OpenAI' : 'Google';
	}

	/** translateText: null, with a toast saying why, when it can't. `overrides` is the dialog's unsaved form, for its Test. */
	async translate(text: string, target: string, overrides?: TranslationApiConfig): Promise<string | null> {
		if (!this.enabled) {
			toast.warning('Translation API disabled');
			return null;
		}
		const config = overrides ?? this.config;
		if (config.type === 'google') {
			if (!config.key) {
				toast.warning('No Translation API key configured');
				return null;
			}
			try {
				return await translateGoogle(text, target, config.key);
			} catch (err) {
				toast.error(`Translation failed: ${err instanceof Error ? err.message : String(err)}`);
				return null;
			}
		}
		// The original's fallbacks, for settings saved before the dialog required both.
		const endpoint = config.endpoint || DEFAULT_CHAT_ENDPOINT;
		const model = config.model || 'gpt-5.1';
		try {
			return await translateChat(text, target, { endpoint, model, prompt: config.prompt, key: config.key });
		} catch (err) {
			console.error('[TranslationAPI]', err);
			toast.error('Translation failed');
			return null;
		}
	}
}

export const translation = new Translation();
