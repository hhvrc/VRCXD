<script lang="ts">
	import { onMount } from 'svelte';
	import { toast } from 'svelte-sonner';
	import { accountPath, api } from '#lib/api/client.js';
	import { onLiveEvent } from '#lib/api/live.svelte.js';
	import { parseLocation } from '#lib/instances/location.js';
	import { vrchatAuth } from '#lib/api/vrchat-auth.svelte.js';

	// The original's instance queue toasts (instanceQueueUpdate, removeQueuedInstance, applyQueuedInstance):
	// a lasting toast with the account's place in each queue, gone when it leaves, and a note when moving
	// elsewhere drops one (upstream issue 1420). Ready comes as a notification.
	type Queued = { location: string; position: number; queueSize: number; worldName: string; groupName: string };

	const toastId = (location: string) => `instance-queue:${location}`;

	/** The original's displayLocation. */
	function display(q: Pick<Queued, 'location' | 'worldName' | 'groupName'>): string {
		const L = parseLocation(q.location);
		const world = q.worldName || L.worldId;
		if (q.groupName) return `${world} ${L.accessTypeName}(${q.groupName})`;
		return L.instanceId ? `${world} ${L.accessTypeName}` : world;
	}

	function show(q: Queued) {
		toast.info(`You are in position ${q.position} of ${q.queueSize} in the queue for ${display(q)} `, {
			id: toastId(q.location),
			duration: Number.POSITIVE_INFINITY,
			position: 'bottom-right',
			closeButton: true
		});
	}

	let loadedFor = '';
	$effect(() => {
		const account = vrchatAuth.ownerUserId;
		if (!account || loadedFor === account) return;
		loadedFor = account;
		api.get<Queued[]>(accountPath(account, '/instance-queue'))
			.then((queues) => queues.forEach(show))
			.catch(() => {});
	});

	onMount(() =>
		onLiveEvent((event) => {
			if (event.event.case !== 'instanceQueue') return;
			const q = event.event.value;
			if (q.accountId !== vrchatAuth.ownerUserId) return;
			if (!q.left) {
				show(q);
				return;
			}
			toast.dismiss(toastId(q.location));
			if (q.removedByMove) toast.info(`Removed instance ${q.worldName || display(q)} from queue`);
		})
	);
</script>
