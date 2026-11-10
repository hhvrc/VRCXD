// Class strings shared across unrelated parts of the app. The pop-ups' own surfaces are in details.ts.

/** "No data" and "no results" lines under a list or table; pair it with a text size. */
export const EMPTY_STATE = 'py-6 text-center text-muted-foreground';

/** The original's small dialogs: 500px wide, anchored 10% from the top instead of centred. */
export const SMALL_DIALOG = 'top-[10vh] translate-y-0 bg-background sm:max-w-125';

/** A labelled form field: label over its control, as in the launch and new-instance dialogs. */
export const FORM_FIELD = 'flex w-full flex-col gap-3';

/** The small round count next to a Filters button. */
export const COUNT_BADGE = 'ml-0.5 h-4.5 min-w-4.5 rounded-full px-1 text-xs';

/** A dashboard widget's centred "nothing to show" note. */
export const CENTERED_NOTE = 'flex h-full items-center justify-center text-compact text-muted-foreground';

/** The page-size and pager row under a table. */
export const PAGER_ROW = 'mt-4 mb-1 flex w-full items-center gap-3';

/** The dimmed layer with a spinner over a table while it loads. */
export const LOADING_OVERLAY = 'absolute inset-0 z-20 flex items-center justify-center bg-background/60';
