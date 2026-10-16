// The part of a generated runtime that useMessageLocale relies on: one module-level resolver, read while a message
// formats, as configureLocaleResolver, getLocale and the m.*() functions in generated runtime.js do.
let resolver: () => string = () => "en";

export function configureLocaleResolver(next: () => string): () => void {
  const previous = resolver;
  resolver = next;
  return () => { if (resolver === next) resolver = previous; };
}

export const getLocale = (): string => (["en", "de"].includes(resolver()) ? resolver() : "en");
export const m = {
  title: () => (getLocale() === "de" ? "Titel" : "Title"),
  count: (inputs: { count: number }) => `${inputs.count} ${getLocale() === "de" ? "Einträge" : "entries"}`,
};
