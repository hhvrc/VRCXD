// Whether the UI needs the user to (re-)enter VRCX.Server's bearer token.
// Set when none is stored, or when the server rejects the stored one
// (see the interceptor in rpc.ts); cleared by ServerTokenGate.
export const serverAuth = $state({ needsToken: false });
