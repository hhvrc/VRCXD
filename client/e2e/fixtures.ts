import { test as base, expect } from '@playwright/test';

// What server/Testing.E2EHost seeds and listens on (see its Program.cs and playwright.config.ts).
export const API_TOKEN = 'vrcx-scenario-api-token';
export const ACCOUNT_ID = 'usr_e2e_me';
export const CONTROL_URL = 'http://127.0.0.1:5298';

export const friends = {
	alice: { id: 'usr_e2e_alice', name: 'Alice Example' },
	bob: { id: 'usr_e2e_bob', name: 'Bob Example' },
	carol: { id: 'usr_e2e_carol', name: 'Carol Example' }
};

/** Pushes something through the host's fake pipeline, as VRChat would. */
export async function control(path: string): Promise<void> {
	const response = await fetch(CONTROL_URL + path, { method: 'POST' });
	if (!response.ok) throw new Error(`POST ${path}: ${response.status} ${await response.text()}`);
}

type Fixtures = {
	/** Whether the page starts with the server token and owner account stored, as after a first visit. */
	signedIn: boolean;
	/** Requests the page tried to send off the machine (all of them are blocked). */
	external: string[];
};

export const test = base.extend<Fixtures>({
	signedIn: [true, { option: true }],
	external: async ({}, use) => {
		await use([]);
	},
	page: async ({ page, signedIn, external }, use) => {
		// Hermetic: only the preview server and the API host on this machine are reachable. A
		// VRChat image URL the client loads directly is recorded and aborted.
		await page.route(
			(url) => url.hostname !== '127.0.0.1' && url.hostname !== 'localhost',
			(route) => {
				external.push(route.request().url());
				return route.abort('blockedbyclient');
			}
		);
		if (signedIn) {
			await page.addInitScript(
				([token, owner]) => {
					localStorage.setItem('vrcx.serverToken', token);
					localStorage.setItem('vrcx.ownerUserId', owner);
				},
				[API_TOKEN, ACCOUNT_ID]
			);
		}
		await use(page);
	}
});

export { expect };
