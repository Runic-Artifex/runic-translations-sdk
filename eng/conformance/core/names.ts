// The protocol and format names the stack writes and accepts, in one place (D079.27: `runic.*` becomes `rax.*` in
// the rename minor). The driver writes the `write` names and reads both spellings, so one switch moves it.
export const PREFIX = (process.env.RAX_CONFORMANCE_PREFIX ?? "rax") as "rax" | "runic";

export const names = {
  adapterProtocol: `${PREFIX}.conformance-adapter/1`,
  receipt: `${PREFIX}.conformance-receipt/1`,
  status: `${PREFIX}.conformance-status/1`,
  cases: `${PREFIX}.conformance-cases/1`,
};

// A format name of either spelling: "runic.views.scenario/1" and "rax.views.scenario/1" are the same format.
export function sameFormat(actual: string | undefined, local: string): boolean {
  if (!actual) return false;
  const strip = (name: string) => name.replace(/^(runic|rax)\./, "");
  return strip(actual) === strip(local);
}
