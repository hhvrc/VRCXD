import { toast } from 'svelte-sonner';
import { readSetting, storeSetting } from '#lib/settings/settings-storage.js';

// The original's Bio Translation API (stores/settings/advanced.js translateText): Google Translate
// with an API key. The original calls it from its native side; here the browser calls it. The key
// stays in this browser, as the original keeps it in its local config. The original's second
// provider, a chat completions endpoint, is not ported yet.

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

class Translation {
	enabled = $state(readSetting('VRCX_translationAPI', false));
	bioLanguage = $state(BIO_LANGUAGES.some((l) => l.code === str('VRCX_bioLanguage', 'en')) ? str('VRCX_bioLanguage', 'en') : 'en');
	key = $state(str('VRCX_translationAPIKey', ''));

	setEnabled(value: boolean) {
		this.enabled = value;
		storeSetting('VRCX_translationAPI', value);
	}

	setBioLanguage(code: string) {
		this.bioLanguage = code;
		storeSetting('VRCX_bioLanguage', code);
	}

	saveKey(key: string) {
		this.key = key;
		storeSetting('VRCX_translationAPIType', 'google');
		storeSetting('VRCX_translationAPIKey', key);
	}

	readonly providerLabel = 'Google';

	/** translateText: null, with a toast saying why, when it can't. */
	async translate(text: string, target: string): Promise<string | null> {
		if (!this.enabled) {
			toast.warning('Translation API disabled');
			return null;
		}
		if (!this.key) {
			toast.warning('No Translation API key configured');
			return null;
		}
		try {
			const response = await fetch('https://translation.googleapis.com/language/translate/v2', {
				method: 'POST',
				headers: { 'Content-Type': 'application/json', 'X-Goog-Api-Key': this.key },
				body: JSON.stringify({ q: text, target, format: 'text' })
			});
			if (response.status !== 200) throw new Error(`Translation API error: ${response.status} - ${await response.text()}`);
			const data = await response.json();
			return data.data.translations[0].translatedText as string;
		} catch (err) {
			toast.error(`Translation failed: ${err instanceof Error ? err.message : String(err)}`);
			return null;
		}
	}
}

export const translation = new Translation();
