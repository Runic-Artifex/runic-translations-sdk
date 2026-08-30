import { runicToolkitAdapter } from "@runic-artifex/sveltekit";

/** @type {import("@sveltejs/kit").Config} */
const config = {
  kit: {
    adapter: runicToolkitAdapter({ mode: "spa", desktop: true, fallback: "index.html" }),
    router: { type: "hash" },
  },
};

export default config;
