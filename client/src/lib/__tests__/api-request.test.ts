import { beforeEach, describe, expect, test, vi } from 'vitest';

const fetchMock = vi.fn();
vi.stubGlobal('fetch', fetchMock);
vi.stubGlobal('localStorage', { getItem: () => null, setItem: () => {}, removeItem: () => {} });

const { api, ApiError } = await import('../api/client');

// The error a request fails with.
const failure = (request: Promise<unknown>) => request.then(() => { throw new Error('the request succeeded'); }, (e: InstanceType<typeof ApiError>) => e);

const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } });
const sent = () => {
	const [url, init] = fetchMock.mock.calls.at(-1)!;
	return { url: url as string, init: init as RequestInit & { headers: Record<string, string> } };
};

// The original's request service (buildRequestInit, parseResponse) for VRChat now runs in the
// server; these are the same rules for the client's requests to the server.
describe('client requests to the server', () => {
	beforeEach(() => fetchMock.mockReset());

	test('a GET puts its parameters in the query string, leaves out empty ones and has no body', async () => {
		fetchMock.mockResolvedValue(json([]));
		await api.get('/1/users', { n: 50, offset: 0, search: '', sort: undefined, order: null });
		const { url, init } = sent();
		expect(url).toBe('/1/users?n=50&offset=0');
		expect(init.method).toBe('GET');
		expect(init.body).toBeUndefined();
		expect(init.headers['content-type']).toBeUndefined();
	});

	test('a POST or PUT sends its parameters as JSON', async () => {
		fetchMock.mockImplementation(async () => json({}));
		await api.post('/1/login', { username: 'test' });
		expect(sent().init.headers['content-type']).toBe('application/json');
		expect(sent().init.body).toBe('{"username":"test"}');
		await api.put('/1/thing', { a: 1 });
		expect(sent().init.method).toBe('PUT');
		expect(sent().init.body).toBe('{"a":1}');
	});

	test('a POST without parameters still sends a JSON body', async () => {
		fetchMock.mockResolvedValue(new Response(null, { status: 204 }));
		await expect(api.post('/1/logout')).resolves.toBeUndefined();
		expect(sent().init.headers['content-type']).toBe('application/json');
		expect(sent().init.body).toBe('null');
	});

	test('an upload sends the form as it is, without a JSON content type', async () => {
		fetchMock.mockResolvedValue(json({ id: 'file_1' }));
		const form = new FormData();
		form.set('file', new Blob(['x']), 'x.png');
		await api.upload('/1/files', form);
		expect(sent().init.body).toBe(form);
		expect(sent().init.headers['content-type']).toBeUndefined();
	});

	test('an error response is an ApiError with the problem type and detail', async () => {
		fetchMock.mockResolvedValue(json({ type: 'User.NotFound', title: 'Not found', status: 404, detail: 'No such user' }, 404));
		const error = await failure(api.get('/1/users/usr_x'));
		expect(error).toBeInstanceOf(ApiError);
		expect([error.status, error.type, error.message]).toEqual([404, 'User.NotFound', 'No such user']);
	});

	test('an error response that is not JSON falls back to the status', async () => {
		fetchMock.mockResolvedValue(new Response('not valid json{{{', { status: 502, statusText: 'Bad Gateway' }));
		const error = await failure(api.get('/1/users'));
		expect([error.status, error.type, error.message]).toEqual([502, '', '502 Bad Gateway']);
	});
});
