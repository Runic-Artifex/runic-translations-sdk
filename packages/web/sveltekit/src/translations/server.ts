import type { CookieSerializeOptions } from "cookie";
import type { Handle, RequestEvent, Reroute } from "@sveltejs/kit";
import type { RunicLocaleRouting } from "./routing.js";

export interface RunicLocaleCookieOptions extends CookieSerializeOptions {
  readonly name?: string;
  readonly path?: string;
}

export interface RunicLocaleHandleOptions<Locale extends string> {
  readonly cookie?: false | RunicLocaleCookieOptions;
  readonly persistLocale?: boolean;
  readonly canonicalRedirect?: boolean;
  readonly redirectStatus?: 301 | 302 | 307 | 308;
  readonly localsKey?: string;
  readonly applicationLocale?: (event: RequestEvent) => string | null | undefined | Promise<string | null | undefined>;
  readonly setLocale?: (event: RequestEvent, locale: Locale) => void;
  /** Token placed in app.html, for example `<html lang="%runic.locale%">`. */
  readonly htmlLanguageToken?: string | false;
}

export function createRunicLocaleHandle<Locale extends string>(
  routing: RunicLocaleRouting<Locale>,
  options: RunicLocaleHandleOptions<Locale> = {},
): Handle {
  const configuredCookie = options.cookie === false ? undefined : options.cookie ?? {};
  const cookie = configuredCookie && {
    ...configuredCookie,
    name: configuredCookie.name ?? "runic_locale",
    path: configuredCookie.path ?? "/",
    httpOnly: configuredCookie.httpOnly ?? true,
    sameSite: configuredCookie.sameSite ?? "lax",
  } satisfies RunicLocaleCookieOptions;
  const localsKey = options.localsKey ?? "locale";
  const htmlLanguageToken = options.htmlLanguageToken ?? "%runic.locale%";

  return async ({ event, resolve }) => {
    const applicationLocale = await options.applicationLocale?.(event);
    const resolution = routing.resolveLocale({
      url: event.url,
      ...(cookie ? { cookieLocale: event.cookies.get(cookie.name) } : {}),
      ...(applicationLocale === undefined ? {} : { applicationLocale }),
      acceptLanguage: event.request.headers.get("accept-language"),
    });

    if (options.setLocale) {
      options.setLocale(event, resolution.locale);
    } else {
      (event.locals as Record<string, unknown>)[localsKey] = resolution.locale;
    }

    if (cookie && options.persistLocale) {
      const { name, ...serializeOptions } = cookie;
      event.cookies.set(name, resolution.locale, withoutUndefined(serializeOptions));
    }

    if (options.canonicalRedirect ?? true) {
      const canonical = routing.canonicalUrl(event.url, resolution.locale);
      const canonicalUrl = new URL(canonical, event.url);
      if (canonicalUrl.pathname !== event.url.pathname) {
        return new Response(null, {
          status: options.redirectStatus ?? 307,
          headers: { location: `${canonicalUrl.pathname}${canonicalUrl.search}${canonicalUrl.hash}` },
        });
      }
    }

    return resolve(event, htmlLanguageToken === false ? undefined : {
      transformPageChunk: ({ html }) => html.replaceAll(htmlLanguageToken, escapeHtmlAttribute(resolution.locale)),
    });
  };
}

export function createRunicLocaleReroute<Locale extends string>(
  routing: RunicLocaleRouting<Locale>,
): Reroute {
  return ({ url }) => {
    const unlocalized = routing.inspectUrl(url).unlocalizedPathname;
    return unlocalized === url.pathname ? undefined : unlocalized;
  };
}

export function localeFromLocals<Locale extends string>(
  locals: App.Locals,
  routing: RunicLocaleRouting<Locale>,
  key = "locale",
): Locale {
  const locale = (locals as Record<string, unknown>)[key];
  if (typeof locale !== "string" || !routing.isLocale(locale)) {
    throw new Error(`App.Locals.${key} does not contain a supported Runic locale.`);
  }
  return locale;
}

function withoutUndefined<T extends object>(value: T): T {
  return Object.fromEntries(Object.entries(value).filter(([, item]) => item !== undefined)) as T;
}

function escapeHtmlAttribute(value: string): string {
  return value
    .replaceAll("&", "&amp;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#39;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;");
}
