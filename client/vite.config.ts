/// <reference types="vitest/config" />
import { paraglideVitePlugin } from '@inlang/paraglide-js';
import tailwindcss from '@tailwindcss/vite';
import adapter from '@sveltejs/adapter-static';
import { sveltekit } from '@sveltejs/kit/vite';
import { defineConfig } from 'vite';

// Fully static/prerendered output for the server to serve from disk - this app has no
// server-rendering needs (all dynamic data comes from the server's REST API and websocket
// at runtime, see src/lib/api/client.ts and src/lib/api/live.svelte.ts), so adapter-static with every
// route prerendered (see src/routes/+layout.ts) is the right fit. Dashboards are the exception:
// their ids live in the browser, so /dashboard/[id] renders client-side from 200.html.
//
// In dev, /1/... (REST and the websocket) is proxied to a running server, so the page and
// its API stay same-origin exactly like in production - no CORS setup needed.
const serverUrl = process.env.VRCX_SERVER_URL ?? 'http://localhost:5280';

export default defineConfig({
	// Vite's .vite/license.md of the bundled packages, which scripts/generate-third-party-licenses.js
	// turns into the Open Source Software Notice dialog's manifest after the build.
	build: { license: true },
	server: {
		proxy: {
			'/1': { target: serverUrl, ws: true }
		}
	},
	// Unit tests for the pure modules in src/lib; the browser tests are Playwright's, under e2e/.
	test: {
		include: ['src/**/*.test.ts']
	},
	plugins: [
		tailwindcss(),
		sveltekit({
			compilerOptions: {
				// Force runes mode for the project, except for libraries. Can be removed in svelte 6.
				runes: ({ filename }) =>
					filename.split(/[/\\]/).includes('node_modules') ? undefined : true
			},
			adapter: adapter({
				pages: 'build',
				assets: 'build',
				fallback: '200.html',
				precompress: false,
				strict: true
			})
		}),
		// Compiles the 14 locale files (project.inlang/settings.json) into the message functions under
		// src/lib/paraglide; outdir and strategy are in project.inlang/paraglide.config.ts.
		paraglideVitePlugin({ project: './project.inlang' })
	]
});
