// The original's Database Cleanup choices (AdvancedTab.vue).

/** VRCX_avatarAutoCleanup's options, Off first. */
export const AUTO_CLEANUP_VALUES = ['Off', '30', '90', '180', '365'] as const;

/** The purge dialog's Delete Avatar Data Older Than options; 180 is the original's default. */
export const PURGE_PERIODS = ['180', '365', '730', 'all'] as const;
export type PurgePeriod = (typeof PURGE_PERIODS)[number];

/** The server's purge request for a dialog choice: all, or rows older than the days. */
export function purgeBody(period: PurgePeriod): { all: true } | { olderThanDays: number } {
	return period === 'all' ? { all: true } : { olderThanDays: parseInt(period, 10) };
}
