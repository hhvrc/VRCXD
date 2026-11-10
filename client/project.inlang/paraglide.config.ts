import { defineConfig } from '@inlang/paraglide-js';

// Shared by the vite plugin (dev, build, vitest) and the CLI compile in `pnpm run generate`
// (which svelte-check needs, as it runs without vite).
export default defineConfig({
	outdir: './src/lib/paraglide',
	// The locale is the appearance.appLanguage setting (VRCX_appLanguage in localStorage), read
	// through the custom strategy registered in src/lib/settings/appearance.svelte.ts. Reading that
	// $state makes every message call reactive, so switching languages re-renders in place
	// without a reload. The prerender, which has no stored setting, renders English.
	strategy: ['custom-appLanguage', 'baseLocale'],
	emitTsDeclarations: true
});
