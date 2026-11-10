import { beforeEach, describe, expect, test, vi } from 'vitest';

const mocks = vi.hoisted(() => ({
    queryPrintFavorites: vi.fn(),
    addPrintToFavorites: vi.fn(),
    removePrintFromFavorites: vi.fn()
}));

vi.mock('../../grpcClient.js', () => ({
    default: {
        queryPrintFavorites: mocks.queryPrintFavorites,
        addPrintToFavorites: mocks.addPrintToFavorites,
        removePrintFromFavorites: mocks.removePrintFromFavorites
    }
}));

import { printFavorites } from '../printFavorites.js';

describe('printFavorites', () => {
    beforeEach(() => {
        mocks.queryPrintFavorites.mockReset();
        mocks.addPrintToFavorites.mockReset();
        mocks.removePrintFromFavorites.mockReset();
    });

    test('returns the server rows as { printId } objects', async () => {
        mocks.queryPrintFavorites.mockResolvedValue([{ printId: 'prnt_0' }, { printId: 'prnt_1' }]);

        await expect(printFavorites.getPrintFavorites()).resolves.toEqual([
            { printId: 'prnt_0' },
            { printId: 'prnt_1' }
        ]);
    });

    // refreshPrintFavorites feeds the result straight into new Set(...), so
    // an unreachable server has to be an empty list rather than null.
    test('an unreachable server reads as no favorites', async () => {
        mocks.queryPrintFavorites.mockResolvedValue(null);
        await expect(printFavorites.getPrintFavorites()).resolves.toEqual([]);
    });

    test('writes forward the print id to the server', () => {
        printFavorites.addPrintToFavorites('prnt_0');
        printFavorites.removePrintFromFavorites('prnt_1');

        expect(mocks.addPrintToFavorites).toHaveBeenCalledWith('prnt_0');
        expect(mocks.removePrintFromFavorites).toHaveBeenCalledWith('prnt_1');
    });
});
