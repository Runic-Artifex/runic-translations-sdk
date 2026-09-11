import { hydrate, tick, unmount } from "svelte";
import Fixture from "./InlineHydrationFixture.svelte";
import { paymentNodes } from "./inline-hydration-data.js";

declare global { interface Window { runicInlineResult?: Record<string, unknown>; } }
let calls = 0;
const target = document.querySelector<HTMLElement>("#fixture")!;
const button = target.querySelector("button")!;
const link = target.querySelector("a")!;
const component = hydrate(Fixture, { target, props: { nodes: paymentNodes(() => calls++) }, recover: false });
await tick();
const result = {
  sameButton: button === target.querySelector("button"),
  sameLink: link === target.querySelector("a"),
  escaped: target.querySelector("script") === null && target.textContent!.includes("<script>inert</script>"),
  accessibleName: target.querySelector("[role=img]")?.getAttribute("aria-label"),
  badge: target.querySelector("[data-tone=positive]")?.textContent,
  initialCalls: calls,
};
button.click();
const activatedCalls = calls;
await unmount(component);
button.click();
window.runicInlineResult = { ...result, activatedCalls, disposedCalls: calls, empty: target.textContent === "" };
