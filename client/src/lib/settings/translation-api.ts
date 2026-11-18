// The request side of the original's translateText and fetchAvailableModels
// (stores/settings/advanced.js), without the settings: Google Translate with an API key, or a chat
// completions endpoint ("OpenAI" in the dialog, any compatible server). The original sends these
// from its native side; here the browser does, so the endpoint has to allow cross-origin requests.

export type TranslationApiType = 'google' | 'openai';

export const DEFAULT_CHAT_ENDPOINT = 'https://api.openai.com/v1/chat/completions';

export type ChatTranslationConfig = { endpoint: string; model: string; prompt: string; key: string };

/** Where a chat completions endpoint lists its models: `.../chat/completions` becomes `.../models`. */
export function modelsUrl(endpoint: string): string {
	try {
		const url = new URL(endpoint);
		const basePath = url.pathname.replace(/\/+$/, '');
		if (basePath.endsWith('/chat/completions')) url.pathname = basePath.replace(/\/chat\/completions$/, '/models');
		else if (basePath.endsWith('/models')) url.pathname = basePath;
		else url.pathname = `${basePath}/models`;
		url.search = '';
		url.hash = '';
		return url.toString();
	} catch {
		const base = endpoint.endsWith('/') ? endpoint.slice(0, -1) : endpoint;
		if (base.endsWith('/chat/completions')) return base.replace(/\/chat\/completions$/, '/models');
		return base.endsWith('/models') ? base : `${base}/models`;
	}
}

/** The model ids in a `/models` answer, sorted; [] when it has no list. */
export function modelIds(data: unknown): string[] {
	const list = (data as { data?: unknown } | null)?.data;
	if (!Array.isArray(list)) return [];
	return list
		.map((model) => (model as { id?: unknown } | null)?.id)
		.filter((id): id is string => typeof id === 'string' && id !== '')
		.sort();
}

export const defaultPrompt = (target: string) => `You are a translation assistant. Translate the user message into ${target}. Only return the translated text.`;

const bearer = (key: string): Record<string, string> => (key ? { Authorization: `Bearer ${key}` } : {});

async function json(response: Response, what: string): Promise<unknown> {
	if (response.status !== 200) throw new Error(`${what}: ${response.status} - ${await response.text()}`);
	return response.json();
}

export async function fetchModels(endpoint: string, key: string): Promise<string[]> {
	const response = await fetch(modelsUrl(endpoint), { headers: bearer(key) });
	return modelIds(await json(response, 'Failed to fetch models'));
}

export async function translateGoogle(text: string, target: string, key: string): Promise<string> {
	const response = await fetch('https://translation.googleapis.com/language/translate/v2', {
		method: 'POST',
		headers: { 'Content-Type': 'application/json', 'X-Goog-Api-Key': key },
		body: JSON.stringify({ q: text, target, format: 'text' })
	});
	const data = (await json(response, 'Translation API error')) as { data: { translations: { translatedText: string }[] } };
	return data.data.translations[0].translatedText;
}

/** The translated text, or null when the endpoint answered without one. */
export async function translateChat(text: string, target: string, config: ChatTranslationConfig): Promise<string | null> {
	const response = await fetch(config.endpoint, {
		method: 'POST',
		headers: { 'Content-Type': 'application/json', ...bearer(config.key) },
		body: JSON.stringify({
			model: config.model,
			messages: [
				{ role: 'system', content: config.prompt || defaultPrompt(target) },
				{ role: 'user', content: text }
			]
		})
	});
	const data = (await json(response, 'Translation API error')) as { choices?: { message?: { content?: unknown } }[] } | null;
	const translated = data?.choices?.[0]?.message?.content;
	return typeof translated === 'string' ? translated.trim() : null;
}
