<script lang="ts">
	import InviteMessageDialog from '#lib/components/invite/invite-message-dialog.svelte';
	import { api } from '#lib/api/client.js';
	import { ownPath } from '#lib/api/vrchat-auth.svelte.js';

	// Answering an invite or invite request from a notification: the message picker with the
	// notification's id, which the answer hides once sent.
	let {
		target = $bindable(),
		type,
		onsent
	}: {
		target?: string;
		type: 'response' | 'requestResponse';
		onsent: (id: string) => void;
	} = $props();

	async function send(slot: number, photo: Blob | null) {
		const id = target;
		if (!id) return;
		const path = ownPath(`/notifications/${encodeURIComponent(id)}/invite-response`);
		if (photo) {
			const form = new FormData();
			form.append('image', photo, 'image.png');
			form.append('responseSlot', String(slot));
			await api.upload(`${path}/photo`, form);
		} else {
			await api.post(path, { responseSlot: slot });
		}
		target = undefined;
		onsent(id);
	}
</script>

<InviteMessageDialog bind:open={() => !!target, (open) => { if (!open) target = undefined; }} {type} {send} />
