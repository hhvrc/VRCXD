// REST access to the server, which also serves this UI - calls go to the page's own origin
// (proxied to the server by `vite dev`, see vite.config.ts). Every /1/... route takes the
// server's API token; errors come back as RFC 9457 problem details with a stable `type`.
//
// Pages are prerendered, so only call api() at runtime in the browser (onMount, event
// handlers, $effect), never at module top level.

import { recentActions } from './recent-actions.svelte';
import { serverAuth } from './server-auth.svelte';
import { readText, storeText } from '../settings/settings-storage';

const TOKEN_KEY = 'vrcx.serverToken';

// The server's single shared API token (VRCX:Auth:Token), entered once by the user and
// remembered per browser.
export const getServerToken = (): string => readText(TOKEN_KEY);
export const setServerToken = (token: string): void => storeText(TOKEN_KEY, token);

export type Problem = {
	type: string;
	title: string;
	status: number;
	detail?: string;
	errors?: Record<string, string[]>;
};

export class ApiError extends Error {
	constructor(
		public readonly status: number,
		public readonly problem: Problem | undefined,
		message: string
	) {
		super(message);
	}

	/** The problem's stable type ("User.NotFound"), or "" for a non-problem failure. */
	get type(): string {
		return this.problem?.type ?? '';
	}

	static from(err: unknown): ApiError {
		if (err instanceof ApiError) return err;
		return new ApiError(0, undefined, err instanceof Error ? err.message : String(err));
	}
}

export type PagedResult<T> = {
	items: T[];
	page: number;
	pageSize: number;
	totalCount: number;
	totalPages: number;
};

type Query = Record<string, string | number | boolean | undefined | null>;

function withQuery(path: string, query?: Query): string {
	if (!query) return path;
	const params = new URLSearchParams();
	for (const [key, value] of Object.entries(query)) {
		if (value === undefined || value === null || value === '') continue;
		params.set(key, String(value));
	}
	const qs = params.toString();
	return qs ? `${path}?${qs}` : path;
}

async function request<T>(method: string, path: string, body?: unknown, query?: Query): Promise<T> {
	const headers: Record<string, string> = { accept: 'application/json' };
	const token = getServerToken();
	if (token) headers['VrcxToken'] = token;
	if (body !== undefined) headers['content-type'] = 'application/json';

	let response: Response;
	try {
		response = await fetch(withQuery(path, query), {
			method,
			headers,
			body: body === undefined ? undefined : JSON.stringify(body)
		});
	} catch (err) {
		throw new ApiError(0, undefined, `Couldn't reach the server: ${(err as Error).message}`);
	}

	if (response.status === 401) {
		serverAuth.needsToken = true;
	}
	if (!response.ok) {
		let problem: Problem | undefined;
		try {
			problem = (await response.json()) as Problem;
		} catch {
			// Not a problem body.
		}
		const validation = problem?.errors ? Object.values(problem.errors).flat().join(' ') : '';
		const message = problem?.detail || validation || problem?.title || `${response.status} ${response.statusText}`;
		throw new ApiError(response.status, problem, message);
	}
	recentActions.noteRequest(method, path, body);
	if (response.status === 204) return undefined as T;
	return (await response.json()) as T;
}

/** Sends a multipart form, for the uploads JSON can't carry. */
async function upload<T>(path: string, form: FormData): Promise<T> {
	const headers: Record<string, string> = { accept: 'application/json' };
	const token = getServerToken();
	if (token) headers['VrcxToken'] = token;
	const response = await fetch(path, { method: 'POST', headers, body: form });
	if (response.status === 401) serverAuth.needsToken = true;
	if (!response.ok) {
		const problem = (await response.json().catch(() => undefined)) as Problem | undefined;
		throw new ApiError(response.status, problem, problem?.detail || problem?.title || `${response.status} ${response.statusText}`);
	}
	recentActions.noteRequest('POST', path);
	if (response.status === 204) return undefined as T;
	return (await response.json()) as T;
}


export const api = {
	upload,
	get: <T>(path: string, query?: Query) => request<T>('GET', path, undefined, query),
	post: <T>(path: string, body?: unknown, query?: Query) => request<T>('POST', path, body ?? null, query),
	put: <T>(path: string, body?: unknown) => request<T>('PUT', path, body),
	patch: <T>(path: string, body?: unknown) => request<T>('PATCH', path, body),
	delete: <T = void>(path: string) => request<T>('DELETE', path)
};

export function accountPath(ownerUserId: string, rest = ''): string {
	return `/1/accounts/${encodeURIComponent(ownerUserId)}${rest}`;
}
