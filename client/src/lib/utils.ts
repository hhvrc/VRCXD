import { type ClassValue, clsx } from 'clsx';
import { extendTailwindMerge } from 'tailwind-merge';
import type { HTMLAttributes } from 'svelte/elements';

// The app's own font sizes (app.css @theme): without this, tailwind-merge takes text-compact for a
// colour and drops it next to text-muted-foreground.
const twMerge = extendTailwindMerge({ extend: { theme: { text: ['3xs', '2xs', 'compact'] } } });

export function cn(...inputs: ClassValue[]) {
	return twMerge(clsx(inputs));
}

// Standard shadcn-svelte helper type: components that either render their
// own root element or forward a bindable ref to the caller.
export type WithElementRef<T, E extends HTMLElement = HTMLElement> = T & {
	ref?: E | null;
};

export type WithoutChildren<T> = T extends { children?: unknown } ? Omit<T, 'children'> : T;
export type WithoutChild<T> = T extends { child?: unknown } ? Omit<T, 'child'> : T;
export type WithoutChildrenOrChild<T> = WithoutChildren<WithoutChild<T>>;

export type HTMLDivAttributes = HTMLAttributes<HTMLDivElement>;
