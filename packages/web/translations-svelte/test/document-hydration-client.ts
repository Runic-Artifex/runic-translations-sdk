import { flushSync, hydrate, unmount } from "svelte";
import Fixture from "./DocumentFixture.svelte";
import { canonicalHtml, corpusNodes, executions, expectedHtml } from "./document-corpus.js";
import { documentProps } from "./document-props.svelte.js";

declare global {
  interface Window {
    runicDocumentCalls: number;
    runicDocumentResult?: Readonly<{ hydrated: number; mismatches: string[]; initialCalls: number }>;
    runicDocument?: Readonly<{ replace(id: string): void; clear(): void; unmount(): void }>;
  }
}
window.runicDocumentCalls = 0;
const mismatches: string[] = [];
const mounted = executions.map((execution, index) => {
  const target = document.querySelector<HTMLElement>(`#fixture-${index}`)!;
  const elements = [...target.querySelectorAll("*")];
  const props = documentProps(corpusNodes(execution.id, () => window.runicDocumentCalls++));
  const component = hydrate(Fixture, { target, props, recover: false });
  flushSync();
  const after = [...target.querySelectorAll("*")];
  // Hydration must adopt every server element and keep the canonical semantic HTML.
  if (after.length !== elements.length || after.some((element, position) => element !== elements[position]) || canonicalHtml(target) !== expectedHtml[execution.id])
    mismatches.push(execution.id);
  return { component, props };
});
const example = mounted[executions.findIndex(execution => execution.id === "backup-en")]!;
window.runicDocument = Object.freeze({
  replace(id: string) { example.props.nodes = corpusNodes(id, () => window.runicDocumentCalls++); flushSync(); },
  clear() { example.props.nodes = []; flushSync(); },
  unmount() { for (const { component } of mounted) unmount(component); },
});
window.runicDocumentResult = { hydrated: mounted.length, mismatches, initialCalls: window.runicDocumentCalls };
