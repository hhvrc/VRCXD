import { afterEach, describe, expect, test, vi } from 'vitest';
import { defaultPrompt, fetchModels, modelIds, modelsUrl, translateChat } from '#lib/settings/translation-api.js';

afterEach(() => vi.unstubAllGlobals());

const answer = (status: number, body: unknown) => ({ status, json: async () => body, text: async () => JSON.stringify(body) }) as Response;

describe('modelsUrl', () => {
	test('swaps chat/completions for models', () => {
		expect(modelsUrl('https://api.openai.com/v1/chat/completions')).toBe('https://api.openai.com/v1/models');
		expect(modelsUrl('http://localhost:11434/v1/chat/completions/?a=1#b')).toBe('http://localhost:11434/v1/models');
	});
	test('keeps a models url and appends to a base', () => {
		expect(modelsUrl('https://host/v1/models/')).toBe('https://host/v1/models');
		expect(modelsUrl('https://host/v1')).toBe('https://host/v1/models');
	});
	test('handles text that is not a url', () => {
		expect(modelsUrl('host/v1/chat/completions')).toBe('host/v1/models');
		expect(modelsUrl('host/v1/')).toBe('host/v1/models');
	});
});

describe('modelIds', () => {
	test('sorted string ids only', () => {
		expect(modelIds({ data: [{ id: 'b' }, { id: '' }, { id: 3 }, null, { id: 'a' }] })).toEqual(['a', 'b']);
	});
	test('nothing without a list', () => {
		expect(modelIds({ models: [] })).toEqual([]);
		expect(modelIds(null)).toEqual([]);
	});
});

describe('fetchModels', () => {
	test('asks the models url with the key', async () => {
		const fetchMock = vi.fn(async () => answer(200, { data: [{ id: 'm1' }] }));
		vi.stubGlobal('fetch', fetchMock);
		expect(await fetchModels('https://host/v1/chat/completions', 'sk')).toEqual(['m1']);
		expect(fetchMock).toHaveBeenCalledWith('https://host/v1/models', { headers: { Authorization: 'Bearer sk' } });
	});
	test('sends no Authorization without a key and throws on an error status', async () => {
		const fetchMock = vi.fn(async () => answer(401, 'no'));
		vi.stubGlobal('fetch', fetchMock);
		await expect(fetchModels('https://host/v1', '')).rejects.toThrow('Failed to fetch models: 401');
		expect(fetchMock).toHaveBeenCalledWith('https://host/v1/models', { headers: {} });
	});
});

describe('translateChat', () => {
	const config = { endpoint: 'https://host/v1/chat/completions', model: 'm', prompt: '', key: '' };

	test('sends the default prompt and trims the answer', async () => {
		const fetchMock = vi.fn(async (_url: string, _init: RequestInit) => answer(200, { choices: [{ message: { content: ' Bonjour \n' } }] }));
		vi.stubGlobal('fetch', fetchMock);
		expect(await translateChat('Hello', 'fr', config)).toBe('Bonjour');
		const [url, init] = fetchMock.mock.calls[0];
		expect(url).toBe(config.endpoint);
		expect(init.headers).toEqual({ 'Content-Type': 'application/json' });
		expect(JSON.parse(init.body as string)).toEqual({
			model: 'm',
			messages: [
				{ role: 'system', content: defaultPrompt('fr') },
				{ role: 'user', content: 'Hello' }
			]
		});
	});
	test('uses the custom prompt and the key', async () => {
		const fetchMock = vi.fn(async (_url: string, _init: RequestInit) => answer(200, { choices: [{ message: { content: 'x' } }] }));
		vi.stubGlobal('fetch', fetchMock);
		await translateChat('Hello', 'fr', { ...config, prompt: 'Be brief', key: 'sk' });
		const init = fetchMock.mock.calls[0][1];
		expect((init.headers as Record<string, string>).Authorization).toBe('Bearer sk');
		expect(JSON.parse(init.body as string).messages[0].content).toBe('Be brief');
	});
	test('null when there is no text, throws on an error status', async () => {
		vi.stubGlobal('fetch', vi.fn(async () => answer(200, { choices: [] })));
		expect(await translateChat('Hello', 'fr', config)).toBeNull();
		vi.stubGlobal('fetch', vi.fn(async () => answer(500, 'boom')));
		await expect(translateChat('Hello', 'fr', config)).rejects.toThrow('Translation API error: 500');
	});
});
