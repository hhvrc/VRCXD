import { readPinnedTools } from './tools';

// Tools pinned to the sidebar. Shared state so a pin on the Tools page shows up in the nav at
// once. Kept per browser, like the original's per-install nav layout.
export const pinnedTools = $state<{ keys: string[] }>({ keys: readPinnedTools() });
