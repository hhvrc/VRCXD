/**
 * Per-source rules for the noty filter values ('On', 'Everyone', 'Friends', 'VIP').
 * Each queue*Noty source accepts a different subset; see passesNotyFilter.
 *
 * - `on`: 'On' passes
 * - `everyone`: 'Everyone' passes
 * - `friends`: 'any' means 'Friends' always passes, 'friend' requires noty.isFriend, false means 'Friends' never passes
 * - `vip`: 'VIP' passes when noty.isFavorite
 */
export const NOTY_FILTER_RULES = Object.freeze({
    notification: Object.freeze({ on: true, everyone: false, friends: 'any', vip: true }),
    friendLog: Object.freeze({ on: true, everyone: false, friends: 'any', vip: true }),
    gameLog: Object.freeze({ on: true, everyone: true, friends: 'friend', vip: true }),
    feed: Object.freeze({ on: false, everyone: true, friends: 'friend', vip: true }),
    moderation: Object.freeze({ on: true, everyone: false, friends: false, vip: false })
});

/**
 * @param {string | undefined} value Noty filter setting for the noty type
 * @param {{ isFriend?: boolean; isFavorite?: boolean }} noty
 * @param {{ on: boolean; everyone: boolean; friends: 'any' | 'friend' | false; vip: boolean }} options
 * @returns {boolean}
 */
export function passesNotyFilter(value, noty, options) {
    switch (value) {
        case 'On':
            return options.on;
        case 'Everyone':
            return options.everyone;
        case 'Friends':
            return options.friends === 'any' || (options.friends === 'friend' && Boolean(noty.isFriend));
        case 'VIP':
            return options.vip && Boolean(noty.isFavorite);
        default:
            return false;
    }
}
