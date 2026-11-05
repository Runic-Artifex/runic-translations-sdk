import type { Cookies, RequestEvent } from "@sveltejs/kit";
import type { Handle } from "@sveltejs/kit/hooks";
import { describe, expect, test, vi } from "vitest";
import {
  createLocaleHandle,
  createLocaleReroute,
  createLocaleRouting,
  localeFromLocals,
  parseAcceptLanguage,
  requestedLocaleFromLocals,
  textDirection,
} from "../src/translations/index.js";

const routing = createLocaleRouting({
  locales: ["en", "de"] as const,
  baseLocale: "en",
  baseLocalePath: "unprefixed",
});

function requestEvent(pathname: string, options: Readonly<{
  cookie?: string;
  acceptLanguage?: string;
}> = {}) {
  const url = new URL(pathname, "https://example.test");
  const values = new Map<string, string>();
  if (options.cookie) values.set("runic_locale", options.cookie);
  const set = vi.fn((name: string, value: string) => values.set(name, value));
  const cookies = {
    get: (name: string) => values.get(name),
    getAll: () => [...values].map(([name, value]) => ({ name, value })),
    set,
    delete: vi.fn(),
    serialize: vi.fn(),
  } as unknown as Cookies;
  const event = {
    url,
    request: new Request(url, options.acceptLanguage
      ? { headers: { "accept-language": options.acceptLanguage } }
      : {}),
    cookies,
    locals: {},
  } as unknown as RequestEvent;
  return { event, set };
}

describe("Runic locale routing", () => {
  test("localizes and delocalizes links while preserving query and hash", () => {
    expect(routing.localizeUrl("/setup?tab=runtime#compiler", "de"))
      .toBe("/de/setup?tab=runtime#compiler");
    expect(routing.localizeUrl("/de/setup", "en")).toBe("/setup");
    expect(routing.delocalizeUrl("/de/setup?tab=runtime")).toBe("/setup?tab=runtime");
    expect(routing.canonicalUrl("/en/setup", "en")).toBe("/setup");
  });

  test("supports a SvelteKit base path and prefixed base-locale policy", () => {
    const prefixed = createLocaleRouting({
      locales: ["en", "de"] as const,
      baseLocale: "en",
      baseLocalePath: "prefixed",
      basePath: "/app",
    });
    expect(prefixed.localizeUrl("/app/setup", "en")).toBe("/app/en/setup");
    expect(prefixed.localizeUrl("/app/en/setup", "de")).toBe("/app/de/setup");
    expect(prefixed.inspectUrl("/app/de/setup")).toMatchObject({
      locale: "de",
      unlocalizedPathname: "/app/setup",
      hadLocalePrefix: true,
    });
  });

  test("resolves deterministic URL, cookie, application, and browser precedence", () => {
    expect(routing.resolveLocale({
      url: "/de/setup",
      cookieLocale: "en",
      applicationLocale: "en",
    })).toEqual({ locale: "de", source: "url", requestedLocale: "de" });

    const preferenceFirst = createLocaleRouting({
      locales: ["en", "de"] as const,
      baseLocale: "en",
      baseLocalePath: "prefixed",
      resolutionOrder: ["application", "cookie", "browser", "url"],
    });
    expect(preferenceFirst.resolveLocale({
      url: "/setup",
      applicationLocale: "de-DE",
      cookieLocale: "en",
    })).toEqual({ locale: "de", source: "application", requestedLocale: "de-DE" });
    expect(preferenceFirst.resolveLocale({
      url: "/setup",
      acceptLanguage: "fr;q=0.9, de-DE;q=0.8, en;q=0.7",
    })).toEqual({ locale: "de", source: "browser", requestedLocale: "de-DE" });
    expect(parseAcceptLanguage("en;q=0.5, de;q=1, *;q=0.9")).toEqual(["de", "en"]);
  });

  test("reroutes localized URLs to one filesystem route", () => {
    const reroute = createLocaleReroute(routing);
    expect(reroute({ url: new URL("https://example.test/de/setup"), fetch }))
      .toBe("/setup");
    expect(reroute({ url: new URL("https://example.test/setup"), fetch }))
      .toBeUndefined();
  });
});

describe("Runic SvelteKit request integration", () => {
  test("sets request-scoped locals, persists cookies, and supplies hydration locale", async () => {
    const { event, set } = requestEvent("/de/setup");
    const handle = createLocaleHandle(routing, { persistLocale: true });
    const response = await handle({
      event,
      resolve: async (_event, options) => {
        const html = await options?.transformPageChunk?.({
          html: '<html lang="%runic.locale%"><body>Setup</body></html>',
          done: true,
        });
        return new Response(html);
      },
    });

    expect(localeFromLocals(event.locals, routing)).toBe("de");
    expect(await response.text()).toContain('lang="de"');
    expect(set).toHaveBeenCalledWith("runic_locale", "de", expect.objectContaining({
      path: "/",
      httpOnly: true,
      sameSite: "lax",
    }));
  });

  test("replaces %runic.dir% with the locale's writing direction", async () => {
    const page = '<html lang="%runic.locale%" dir="%runic.dir%"><body>Setup</body></html>';
    const render = async (handle: Handle, pathname: string) => {
      const { event } = requestEvent(pathname);
      const response = await handle({ event, resolve: async (_event, options) => new Response(await options?.transformPageChunk?.({ html: page, done: true })) });
      return response.text();
    };
    const rtl = createLocaleRouting({ locales: ["en", "ar"] as const, baseLocale: "en", baseLocalePath: "unprefixed" });
    expect(await render(createLocaleHandle(rtl), "/ar/setup")).toContain('<html lang="ar" dir="rtl">');
    expect(await render(createLocaleHandle(rtl), "/setup")).toContain('<html lang="en" dir="ltr">');
    expect(await render(createLocaleHandle(rtl, { direction: () => "rtl" }), "/setup")).toContain('dir="rtl"');
    expect(await render(createLocaleHandle(rtl, { htmlDirectionToken: false }), "/ar/setup")).toContain('<html lang="ar" dir="%runic.dir%">');
    expect(await render(createLocaleHandle(rtl, { htmlLanguageToken: false }), "/ar/setup")).toContain('<html lang="%runic.locale%" dir="rtl">');
    expect(textDirection("he-IL")).toBe("rtl");
    expect(textDirection("en")).toBe("ltr");
    // Extension and private-use subtags are never the script (the same rule as translations-svelte and the runtime).
    for (const [tag, direction] of Object.entries({ "ar-u-nu-latn": "rtl", "en-u-nu-arab": "ltr", "de-x-hebr": "ltr", "x-hebr": "ltr", "und-Hebr": "rtl" }))
      expect(textDirection(tag), tag).toBe(direction);
    // The SvelteKit and Svelte copies of the helper stay identical below their header comment.
    const { readFileSync } = await import("node:fs");
    const body = (path: string) => readFileSync(new URL(path, import.meta.url), "utf8").split("\n").slice(3).join("\n");
    expect(body("../src/translations/direction.ts")).toBe(body("../../translations-svelte/src/translations/direction.ts"));
  });

  test("redirects non-canonical locale URLs", async () => {
    const { event } = requestEvent("/en/setup?tab=runtime");
    const handle = createLocaleHandle(routing);
    const response = await handle({ event, resolve: vi.fn() });
    expect(response.status).toBe(307);
    expect(response.headers.get("location")).toBe("/setup?tab=runtime");
  });

  test("isolates concurrent SSR requests and leaves explicit message locale authoritative", async () => {
    const english = requestEvent("/setup").event;
    const german = requestEvent("/de/setup").event;
    const handle = createLocaleHandle(routing, { htmlLanguageToken: false });
    let release!: () => void;
    const barrier = new Promise<void>((resolve) => { release = resolve; });
    const resolve = async (event: RequestEvent) => {
      await barrier;
      const locale = localeFromLocals(event.locals, routing);
      const generatedMessage = (options: Readonly<{ locale: "en" | "de" }>) =>
        options.locale === "de" ? "Hallo" : "Hello";
      return new Response(generatedMessage({ locale }));
    };

    const englishResponse = handle({ event: english, resolve });
    const germanResponse = handle({ event: german, resolve });
    release();
    expect(await (await englishResponse).text()).toBe("Hello");
    expect(await (await germanResponse).text()).toBe("Hallo");
    expect(localeFromLocals(english.locals, routing)).toBe("en");
    expect(localeFromLocals(german.locals, routing)).toBe("de");
  });

  test("runs rendering in the generated request-local locale context", async () => {
    const { event } = requestEvent("/de/setup");
    let activeLocale: "en" | "de" = "en";
    const handle = createLocaleHandle(routing, {
      htmlLanguageToken: false,
      runWithLocale(locale, operation) {
        activeLocale = locale;
        return operation();
      },
    });
    const response = await handle({ event, resolve: async () => new Response(activeLocale) });
    expect(await response.text()).toBe("de");
  });
});

describe("Runic SvelteKit requested locale and time zone", () => {
  test("resolution reports the canonical tag the matching preference asked for", () => {
    const negotiated = createLocaleRouting({ locales: ["en", "de"] as const, baseLocale: "en", baseLocalePath: "prefixed" });
    expect(negotiated.resolveLocale({ url: "/setup", acceptLanguage: "fr;q=0.9, de-ch" }))
      .toEqual({ locale: "de", source: "browser", requestedLocale: "de-CH" });
    expect(negotiated.resolveLocale({ url: "/setup", cookieLocale: "en_gb" }))
      .toEqual({ locale: "en", source: "cookie", requestedLocale: "en-GB" });
    expect(negotiated.resolveLocale({ url: "/de/setup", acceptLanguage: "de-AT" }))
      .toEqual({ locale: "de", source: "url", requestedLocale: "de" });
    expect(negotiated.resolveLocale({ url: "/setup", applicationLocale: "de-CH.UTF-8" }))
      .toEqual({ locale: "de", source: "application", requestedLocale: "de" });
    expect(negotiated.resolveLocale({ url: "/setup", acceptLanguage: "fr" }))
      .toEqual({ locale: "en", source: "default", requestedLocale: "en" });
    expect(routing.resolveLocale({ url: "/setup", acceptLanguage: "de-CH" }))
      .toEqual({ locale: "en", source: "url", requestedLocale: "en" });
  });

  test("the handle stores the requested tag and passes it and the time zone to runWithLocale", async () => {
    const strict = createLocaleRouting({ locales: ["en", "de"] as const, baseLocale: "en", baseLocalePath: "prefixed", resolutionOrder: ["browser"] });
    const { event } = requestEvent("/de/setup", { acceptLanguage: "de-CH" });
    const calls: unknown[] = [];
    const handle = createLocaleHandle(strict, {
      htmlLanguageToken: false,
      canonicalRedirect: false,
      timeZone: (request) => request.cookies.get("tz") ?? "Europe/Zurich",
      runWithLocale(locale, operation, options) {
        calls.push([locale, options]);
        return operation();
      },
    });
    await handle({ event, resolve: async () => new Response("ok") });
    expect(calls).toEqual([["de", { requestedLocale: "de-CH", timeZone: "Europe/Zurich" }]]);
    expect(event.locals).toEqual({ locale: "de", requestedLocale: "de-CH" });
    expect(requestedLocaleFromLocals(event.locals, strict)).toBe("de-CH");
    expect(requestedLocaleFromLocals({ locale: "de", requestedLocale: "fr-CH" } as App.Locals, strict)).toBe("de");
    expect(requestedLocaleFromLocals({ locale: "de" } as App.Locals, strict)).toBe("de");

    const custom = requestEvent("/setup", { acceptLanguage: "de-CH" }).event;
    const setLocale = vi.fn();
    await createLocaleHandle(strict, { htmlLanguageToken: false, canonicalRedirect: false, requestedLocaleKey: false, setLocale })({ event: custom, resolve: async () => new Response("ok") });
    expect(setLocale).toHaveBeenCalledWith(custom, "de", { requestedLocale: "de-CH", timeZone: undefined });
  });
});

// Compile-time assurance that the factory result is directly assignable to SvelteKit's hook type.
const _handle: Handle = createLocaleHandle(routing);
void _handle;

test("the package exposes the root entry and ./navigation only, with unprefixed names", async () => {
  const { readFile } = await import("node:fs/promises");
  const manifest = JSON.parse(await readFile(new URL("../package.json", import.meta.url), "utf8"));
  expect(Object.keys(manifest.exports)).toEqual([".", "./navigation"]);
  const entry = await import("../src/translations/index.js");
  expect(Object.keys(entry).filter(name => name.includes("Runic"))).toEqual([]);
});
