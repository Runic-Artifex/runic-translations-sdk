import "../../chunks/server.js";
//#region src/routes/+layout.svelte
function _layout($$renderer, $$props) {
	const { children } = $$props;
	$$renderer.push(`<div class="contents text-foreground">`);
	children($$renderer);
	$$renderer.push(`<!----></div>`);
}
//#endregion
export { _layout as default };
