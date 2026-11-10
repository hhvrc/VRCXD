import { redirect } from '@sveltejs/kit';

// Matches the Vue app's router: '' redirects to the feed tab.
export const load = () => {
	redirect(307, '/feed');
};
