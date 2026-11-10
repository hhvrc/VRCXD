import { defineConfig, devices } from '@playwright/test';

// Browser smoke tests: the built client (vite preview) against the real API host with a fake
// VRChat (server/Testing.E2EHost, which needs Docker for its Postgres and Redis containers).
// Nothing here reaches VRChat. `pnpm run test:e2e` starts both servers; a running pair on these
// ports is reused outside CI.
const apiPort = 5299;
const controlPort = 5298;
const webPort = 4299;

const controlUrl = `http://127.0.0.1:${controlPort}`;

export default defineConfig({
	testDir: './e2e',
	fullyParallel: false,
	// One server, one fake account: tests that push pipeline frames share its state.
	workers: 1,
	forbidOnly: !!process.env.CI,
	retries: process.env.CI ? 1 : 0,
	reporter: process.env.CI ? [['list'], ['html', { open: 'never' }]] : 'list',
	use: {
		baseURL: `http://127.0.0.1:${webPort}`,
		trace: 'retain-on-failure'
	},
	projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
	webServer: [
		{
			command: `dotnet run --project ../server/Testing.E2EHost -- ${apiPort} ${controlPort}`,
			url: `${controlUrl}/health`,
			timeout: 300_000,
			reuseExistingServer: !process.env.CI,
			stdout: 'pipe'
		},
		{
			command: `pnpm run build && pnpm exec vite preview --host 127.0.0.1 --port ${webPort} --strictPort`,
			url: `http://127.0.0.1:${webPort}`,
			timeout: 300_000,
			reuseExistingServer: !process.env.CI,
			env: { VRCX_SERVER_URL: `http://127.0.0.1:${apiPort}` }
		}
	]
});
