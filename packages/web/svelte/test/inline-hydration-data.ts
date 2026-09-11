import { inlineFactory, type InlineNode } from "../src/inline/index.js";

export function paymentNodes(onActivate: () => void): InlineNode[] {
  const element = (name: string, children: InlineNode[], extra: object = {}): InlineNode => inlineFactory.element({ name, children, options: {}, occurrence: `payment:${name}`, locale: "en", ...extra });
  return [
    inlineFactory.text("<script>inert</script>"),
    element("runic:link", [inlineFactory.text("Terms")], { binding: { kind: "runic:link", href: "/terms" } }),
    element("runic:action", [inlineFactory.text("Retry")], { binding: { kind: "runic:action", onActivate } }),
    element("runic:icon", [], { standalone: true, binding: { kind: "runic:icon", asset: "★", decorative: false, accessibleName: (locale: string) => locale === "en" ? "Star" : "Stern" } }),
    element("shop:badge", [inlineFactory.text("Available")], { options: { tone: "positive" } }),
  ];
}
