<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import { untrack } from 'svelte';
	import { toast } from 'svelte-sonner';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as Dialog from '#lib/components/ui/dialog/index.js';
	import * as Select from '#lib/components/ui/select/index.js';
	import { Textarea } from '#lib/components/ui/textarea/index.js';
	import { api, ApiError } from '#lib/api/client.js';
	import type { ReportOptions } from '#lib/nav/tools.js';
	import { ownPath } from '#lib/api/vrchat-auth.svelte.js';

	// The original's ModerationReportDialog: a category and reason from VRChat's config for the kind
	// of content, an optional description, and the report sent to VRChat.
	export type ReportTarget = { type: string; contentId: string; contentName?: string; details?: unknown };

	let { target = $bindable() }: { target?: ReportTarget } = $props();

	let options = $state<ReportOptions | undefined>();
	let category = $state('');
	let reason = $state('');
	let description = $state('');
	let submitting = $state(false);
	let error = $state('');

	const categoryOptions = $derived.by(() => {
		const forType = (target && options?.options?.[target.type]) ?? {};
		return Object.keys(forType)
			.map((key) => ({ value: key, label: options?.categories[key] ?? key, order: options?.categoryOrder?.[key] ?? 0 }))
			.sort((a, b) => a.order - b.order);
	});
	const reasonOptions = $derived(
		((target && options?.options?.[target.type]?.[category]) ?? []).map((key) => ({ value: key, label: options?.reasons[key] ?? key }))
	);
	const categoryLabel = $derived(categoryOptions.find((o) => o.value === category)?.label);
	const reasonLabel = $derived(reasonOptions.find((o) => o.value === reason)?.label);
	const canSubmit = $derived(!!target?.contentId && !!category && !!reason && !submitting);

	$effect(() => {
		if (!target) return;
		untrack(() => {
			category = '';
			reason = '';
			description = '';
			error = '';
			if (!options)
				api
					.get<ReportOptions>(ownPath('/moderation-reports/options'))
					.then((o) => (options = o))
					.catch((err) => (error = ApiError.from(err).message));
		});
	});

	async function submit() {
		const report = target;
		if (!report) return;
		submitting = true;
		error = '';
		try {
			await api.post(ownPath('/moderation-reports'), {
				type: report.type,
				category,
				reason,
				contentId: report.contentId,
				description: description.trim() || null,
				details: report.details ?? null
			});
			toast.success(m.dialog_moderation_report_success());
			target = undefined;
		} catch (err) {
			error = ApiError.from(err).message;
		} finally {
			submitting = false;
		}
	}
</script>

<Dialog.Root bind:open={() => !!target, (open) => { if (!open) target = undefined; }}>
	<Dialog.Content class="bg-background sm:max-w-lg">
		<Dialog.Header>
			<Dialog.Title>{m.dialog_moderation_report_header()}</Dialog.Title>
			<Dialog.Description>{target?.contentName || target?.contentId}</Dialog.Description>
		</Dialog.Header>

		{#if options && !categoryOptions.length}
			<div class="text-sm text-muted-foreground">{m.dialog_moderation_report_no_options()}</div>
		{:else}
			<div class="flex flex-col gap-4 text-sm">
				<div class="flex flex-col gap-1.5">
					<span class="font-medium">{m.dialog_moderation_report_category()}</span>
					<Select.Root type="single" value={category} onValueChange={(value) => { category = value; reason = ''; }}>
						<Select.Trigger size="sm" class="w-full">{categoryLabel ?? 'Category'}</Select.Trigger>
						<Select.Content>
							{#each categoryOptions as option (option.value)}
								<Select.Item value={option.value} label={option.label} />
							{/each}
						</Select.Content>
					</Select.Root>
					{#if options?.categoryTooltips?.[category]}<span class="text-xs text-muted-foreground">{options.categoryTooltips[category]}</span>{/if}
				</div>
				<div class="flex flex-col gap-1.5">
					<span class="font-medium">{m.dialog_moderation_report_reason()}</span>
					<Select.Root type="single" bind:value={reason} disabled={!category}>
						<Select.Trigger size="sm" class="w-full">{reasonLabel ?? 'Reason'}</Select.Trigger>
						<Select.Content>
							{#each reasonOptions as option (option.value)}
								<Select.Item value={option.value} label={option.label} />
							{/each}
						</Select.Content>
					</Select.Root>
					{#if options?.reasonTooltips?.[reason]}<span class="text-xs text-muted-foreground">{options.reasonTooltips[reason]}</span>{/if}
				</div>
				<div class="flex flex-col gap-1.5">
					<span class="font-medium">{m.dialog_moderation_report_description()}</span>
					<Textarea bind:value={description} rows={4} class="resize-none" placeholder={m.dialog_moderation_report_description_placeholder()} />
				</div>
			</div>
		{/if}
		{#if error}<p class="text-sm text-destructive">{error}</p>{/if}

		<Dialog.Footer>
			<Button size="sm" variant="secondary" onclick={() => (target = undefined)}>{m.dialog_moderation_report_cancel()}</Button>
			<Button size="sm" variant="destructive" disabled={!canSubmit} onclick={submit}>{m.dialog_moderation_report_submit()}</Button>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>
