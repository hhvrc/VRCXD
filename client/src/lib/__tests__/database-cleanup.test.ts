import { describe, expect, it } from 'vitest';
import { AUTO_CLEANUP_VALUES, PURGE_PERIODS, purgeBody } from '../settings/database-cleanup.js';

describe('database cleanup', () => {
	it('offers the original choices, Off first', () => {
		expect(AUTO_CLEANUP_VALUES).toEqual(['Off', '30', '90', '180', '365']);
		expect(PURGE_PERIODS).toEqual(['180', '365', '730', 'all']);
	});

	it('asks for all rows only when All data is chosen', () => {
		expect(purgeBody('all')).toEqual({ all: true });
		expect(purgeBody('180')).toEqual({ olderThanDays: 180 });
		expect(purgeBody('730')).toEqual({ olderThanDays: 730 });
	});
});
