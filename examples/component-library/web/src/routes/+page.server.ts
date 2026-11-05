import { describeStorage } from '@acme/widgets';
import type { PageServerLoad } from './$types';

export const load: PageServerLoad = async () => {
	// Yield first, so that concurrent requests interleave: the library's text still follows each request's locale.
	await new Promise((resolve) => setTimeout(resolve, Math.random() * 20));
	return { usage: describeStorage({ files: 3, used: 1.5, total: 15 }) };
};
