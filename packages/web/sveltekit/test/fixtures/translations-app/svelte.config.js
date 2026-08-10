import adapter from "@sveltejs/adapter-static";

export default {
  kit: {
    adapter: adapter({ strict: true }),
    prerender: {
      entries: ["/setup", "/de/setup"],
    },
  },
};
