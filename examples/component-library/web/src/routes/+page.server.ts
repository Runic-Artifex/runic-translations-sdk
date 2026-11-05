import { describeStorage } from '@acme/widgets';
import type { PageServerLoad } from './$types';

export const load: PageServerLoad = async ({ url }) => {
	// The text is in the request's locale, which the handle takes from the path (/de, /fr). SvelteKit does not track
	// locals, so reading the url is what makes it load again when a locale switch on the client changes the path.
	void url.pathname;
	// Only for the example's CI check: a random delay interleaves concurrent requests, and the library's text still
	// follows each request's locale.
	await new Promise((resolve) => setTimeout(resolve, Math.random() * 20));
	return { usage: describeStorage({ files: 3, used: 1.5, total: 15 }) };
};
