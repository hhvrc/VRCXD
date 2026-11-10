// The Electron/CEF client's dual-write path to the first VRCX.Server port. That server is gone
// (Server/ is the rewrite - REST + a protobuf websocket - and this app does not talk to it);
// every caller already falls back to local SQLite when the server is unavailable, so this
// shim keeps the call sites compiling by always being unavailable. It goes away with the
// Vue app itself.
const unavailable = {
    get isAvailable() {
        return false;
    },
    get isOwnerScopedAvailable() {
        return false;
    },
    setOwnerUserId() {}
};

const grpcClientService = new Proxy(unavailable, {
    get(target, name) {
        if (name in target) return target[name];
        // Reads return null (= "fall back to SQLite"), writes resolve to nothing.
        return async () => null;
    }
});

window.grpcClientService = grpcClientService;

export { grpcClientService as default };
