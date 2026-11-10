import { accountPath } from '#lib/api/client.js';
import { readText, storeText } from '#lib/settings/settings-storage.js';

const OWNER_KEY = 'vrcx.ownerUserId';


// `needsLogin`: whether the UI needs the user to log the server into VRChat. Set when no
// account is remembered, or when the server reports no valid session for the remembered one
// (see VrchatLoginGate); cleared on a successful login/2FA.
//
// `ownerUserId`: the VRChat account id this browser works as - one of the server's logged-in
// accounts (GET /1/accounts). Every owner-scoped page reads it.
export const vrchatAuth = $state({ needsLogin: false, ownerUserId: readText(OWNER_KEY) });

export function setOwnerUserId(ownerUserId: string): void {
	vrchatAuth.ownerUserId = ownerUserId;
	storeText(OWNER_KEY, ownerUserId);
}

/** An API path under the account this browser works as: `ownPath('/feed')`. */
export const ownPath = (rest = ''): string => accountPath(vrchatAuth.ownerUserId, rest);
