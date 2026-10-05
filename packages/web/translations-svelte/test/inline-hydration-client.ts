import { hydrate, mount, tick, unmount } from "svelte";
import Fixture from "./InlineHydrationFixture.svelte";
import { paymentNodes } from "./inline-hydration-data.js";

declare global { interface Window { runicInlineResult?: Record<string, unknown>; } }
let calls = 0;
let accessibleNameCalls = 0;
const target = document.querySelector<HTMLElement>("#fixture")!;
const button = target.querySelector("button")!;
const link = target.querySelector("a")!;
const component = hydrate(Fixture, { target, props: { nodes: paymentNodes(() => calls++, locale => { accessibleNameCalls++; return locale === "en" ? "Star" : "Stern"; }) }, recover: false });
await tick();
const result = {
  sameButton: button === target.querySelector("button"),
  sameLink: link === target.querySelector("a"),
  escaped: target.querySelector("script") === null && target.textContent!.includes("<script>inert</script>"),
  accessibleName: target.querySelector("[role=img]")?.getAttribute("aria-label"),
  accessibleNameCalls,
  badge: target.querySelector("[data-tone=positive]")?.textContent,
  initialCalls: calls,
};
button.click();
const activatedCalls = calls;
await unmount(component);
button.click();
let blankRejected = false;
try {
  mount(Fixture, { target: document.createElement("div"), props: { nodes: paymentNodes(() => undefined, () => " \t ") } });
} catch (error) {
  blankRejected = String(error).includes("Meaningful icon alternate text is empty.");
}
window.runicInlineResult = { ...result, activatedCalls, disposedCalls: calls, empty: target.textContent === "", blankRejected };
