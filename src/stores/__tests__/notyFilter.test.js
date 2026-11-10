import { describe, expect, test } from 'vitest';

import { NOTY_FILTER_RULES, passesNotyFilter } from '../notification/notyFilter';

// The inline checks each queue*Noty function used before passesNotyFilter existed.
const legacyChecks = {
    notification: (value, noty) =>
        value && (value === 'On' || value === 'Friends' || (value === 'VIP' && noty.isFavorite)),
    friendLog: (value, noty) =>
        value && (value === 'On' || value === 'Friends' || (value === 'VIP' && noty.isFavorite)),
    gameLog: (value, noty) =>
        value &&
        (value === 'On' ||
            value === 'Everyone' ||
            (value === 'Friends' && noty.isFriend) ||
            (value === 'VIP' && noty.isFavorite)),
    feed: (value, noty) =>
        value &&
        (value === 'Everyone' || (value === 'Friends' && noty.isFriend) || (value === 'VIP' && noty.isFavorite)),
    moderation: (value) => value && value === 'On'
};

const values = [undefined, null, '', 'Off', 'On', 'Everyone', 'Friends', 'VIP', 'Unknown'];
const flags = [undefined, false, true];

describe('passesNotyFilter', () => {
    test('has rules for every source', () => {
        expect(Object.keys(NOTY_FILTER_RULES).sort()).toEqual(Object.keys(legacyChecks).sort());
    });

    for (const source of Object.keys(legacyChecks)) {
        describe(source, () => {
            for (const value of values) {
                for (const isFriend of flags) {
                    for (const isFavorite of flags) {
                        const noty = { isFriend, isFavorite };
                        test(`${String(value)} isFriend=${isFriend} isFavorite=${isFavorite}`, () => {
                            expect(passesNotyFilter(value, noty, NOTY_FILTER_RULES[source])).toBe(
                                Boolean(legacyChecks[source](value, noty))
                            );
                        });
                    }
                }
            }
        });
    }
});
