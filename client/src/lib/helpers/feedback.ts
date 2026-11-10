import { toast } from 'svelte-sonner';
import { m } from '#lib/paraglide/messages.js';
import { ApiError } from '#lib/api/client.js';

// The toasts every page shows the same way.

/** A failed request or action, as the server or the browser worded it. */
export function toastError(err: unknown): void {
	toast.error(ApiError.from(err).message);
}

/** The original's copyToClipboard: copies and says so, or says it could not. */
export function copyText(text: string, message = 'Copied successfully!'): Promise<boolean> {
	return navigator.clipboard.writeText(text).then(
		() => (toast.success(message), true),
		() => (toast.error(m.message_copy_failed()), false)
	);
}
