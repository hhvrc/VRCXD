import { API_TOKEN, control, expect, friends, test } from './fixtures';

test.describe('without a stored token', () => {
	test.use({ signedIn: false });

	test('a first visit asks for the server token and accepts the right one', async ({ page }) => {
		await page.goto('/');
		const gate = page.getByRole('dialog', { name: 'Connect to the VRCXD server' });
		await expect(gate).toBeVisible();

		await gate.getByLabel('API token').fill('not-the-token');
		await gate.getByRole('button', { name: 'Connect' }).click();
		await expect(gate).toBeVisible();

		await gate.getByLabel('API token').fill(API_TOKEN);
		await gate.getByRole('button', { name: 'Connect' }).click();
		await expect(gate).toBeHidden();
		expect(await page.evaluate(() => localStorage.getItem('vrcx.serverToken'))).toBe(API_TOKEN);
	});
});

test("the friend list shows the fake account's friends, with nothing loaded off the machine", async ({ page, external }) => {
	await page.goto('/friend-list');
	for (const friend of Object.values(friends)) {
		await expect(page.getByText(friend.name).first()).toBeVisible();
	}
	await page.waitForLoadState('networkidle');
	// Avatars come through the server's image cache (faked in the host), never from VRChat.
	expect(external).toEqual([]);
});

test('a friend coming online over the pipeline moves to ONLINE in the sidebar, live', async ({ page }) => {
	// Section headers read "ONLINE", a horizontal bar (U+2015), then the count.
	const header = (label: string, count: number) => page.getByText(new RegExp(`^${label}\\s*―\\s*${count}$`));
	await page.goto('/');
	await expect(header('ONLINE', 1)).toBeVisible();
	await expect(header('OFFLINE', 1)).toBeVisible();

	await control(`/friends/${friends.bob.id}/online`);
	await expect(header('ONLINE', 2)).toBeVisible();
	await expect(page.getByText(/^OFFLINE\s*―/)).toHaveCount(0);

	await control(`/friends/${friends.bob.id}/offline`);
	await expect(header('ONLINE', 1)).toBeVisible();
	await expect(header('OFFLINE', 1)).toBeVisible();
});
