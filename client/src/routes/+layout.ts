// Every route in this app is prerendered - see vite.config.ts for why
// (no server-rendering needs, all dynamic data comes from the server REST API/websocket
// calls at runtime). This one setting applies to the whole route tree.
export const prerender = true;
export const ssr = true;
