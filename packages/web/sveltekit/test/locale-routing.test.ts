import type { Cookies, Handle, RequestEvent } from "@sveltejs/kit";
import { describe, expect, test, vi } from "vitest";
import {
  createRunicLocaleHandle,
  createRunicLocaleReroute,
  createRunicLocaleRouting,
  localeFromLocals,
  parseAcceptLanguage,
} from "../src/translations/index.js";

const routing = createRunicLocaleRouting({
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
    const prefixed = createRunicLocaleRouting({
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
    })).toEqual({ locale: "de", source: "url" });

    const preferenceFirst = createRunicLocaleRouting({
      locales: ["en", "de"] as const,
      baseLocale: "en",
      baseLocalePath: "prefixed",
      resolutionOrder: ["application", "cookie", "browser", "url"],
    });
    expect(preferenceFirst.resolveLocale({
      url: "/setup",
      applicationLocale: "de-DE",
      cookieLocale: "en",
    })).toEqual({ locale: "de", source: "application" });
    expect(preferenceFirst.resolveLocale({
      url: "/setup",
      acceptLanguage: "fr;q=0.9, de-DE;q=0.8, en;q=0.7",
    })).toEqual({ locale: "de", source: "browser" });
    expect(parseAcceptLanguage("en;q=0.5, de;q=1, *;q=0.9")).toEqual(["de", "en"]);
  });

  test("reroutes localized URLs to one filesystem route", () => {
    const reroute = createRunicLocaleReroute(routing);
    expect(reroute({ url: new URL("https://example.test/de/setup"), fetch }))
      .toBe("/setup");
    expect(reroute({ url: new URL("https://example.test/setup"), fetch }))
      .toBeUndefined();
  });
});

describe("Runic SvelteKit request integration", () => {
  test("sets request-scoped locals, persists cookies, and supplies hydration locale", async () => {
    const { event, set } = requestEvent("/de/setup");
    const handle = createRunicLocaleHandle(routing, { persistLocale: true });
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

  test("redirects non-canonical locale URLs", async () => {
    const { event } = requestEvent("/en/setup?tab=runtime");
    const handle = createRunicLocaleHandle(routing);
    const response = await handle({ event, resolve: vi.fn() });
    expect(response.status).toBe(307);
    expect(response.headers.get("location")).toBe("/setup?tab=runtime");
  });

  test("isolates concurrent SSR requests and leaves explicit message locale authoritative", async () => {
    const english = requestEvent("/setup").event;
    const german = requestEvent("/de/setup").event;
    const handle = createRunicLocaleHandle(routing, { htmlLanguageToken: false });
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
    const handle = createRunicLocaleHandle(routing, {
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

// Compile-time assurance that the factory result is directly assignable to SvelteKit's hook type.
const _handle: Handle = createRunicLocaleHandle(routing);
void _handle;
