// What a launch command leaves for the page it opens: the favorites import text for that
// favorites page, or the avatar search's provider list to show (the original's
// showAvatarProviderDialog after addavatardb). The page takes it and clears it.
export const launchHandoff = $state<{ import: { type: 'avatar' | 'world' | 'friend'; text: string } | null; showProviders: boolean }>({
	import: null,
	showProviders: false
});
