import { m } from 'virtual:runic-translations/app';
import type { PageServerLoad } from './$types';

export const load: PageServerLoad = async () => {
	// Yield so that concurrent requests interleave before the message call.
	await new Promise((resolve) => setTimeout(resolve, Math.random() * 20));
	return { title: m.home_title() };
};
