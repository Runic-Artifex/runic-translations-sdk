export type BaseLocalePath = "unprefixed" | "prefixed";
export type LocaleResolutionStrategy = "url" | "cookie" | "application" | "browser";
export type LocaleResolutionSource = LocaleResolutionStrategy | "default";

export interface RunicLocaleRoutingOptions<Locale extends string> {
  readonly locales: readonly Locale[];
  readonly baseLocale: Locale;
  readonly baseLocalePath?: BaseLocalePath;
  readonly basePath?: string;
  readonly resolutionOrder?: readonly LocaleResolutionStrategy[];
}

export interface LocaleResolutionInput {
  readonly url: string | URL;
  readonly cookieLocale?: string | null | undefined;
  readonly applicationLocale?: string | null | undefined;
  readonly browserLocales?: readonly string[] | undefined;
  readonly acceptLanguage?: string | null | undefined;
}

export interface LocaleResolution<Locale extends string> {
  readonly locale: Locale;
  readonly source: LocaleResolutionSource;
}

export interface LocalizedUrl<Locale extends string> {
  readonly locale: Locale | undefined;
  readonly pathname: string;
  readonly unlocalizedPathname: string;
  readonly hadLocalePrefix: boolean;
}

export interface RunicLocaleRouting<Locale extends string> {
  readonly locales: readonly Locale[];
  readonly baseLocale: Locale;
  readonly baseLocalePath: BaseLocalePath;
  readonly basePath: string;
  readonly resolutionOrder: readonly LocaleResolutionStrategy[];
  isLocale(value: string | null | undefined): value is Locale;
  matchLocale(value: string | null | undefined): Locale | undefined;
  inspectUrl(url: string | URL): LocalizedUrl<Locale>;
  resolveLocale(input: LocaleResolutionInput): LocaleResolution<Locale>;
  localizeUrl(url: string | URL, locale: Locale): string;
  delocalizeUrl(url: string | URL): string;
  canonicalUrl(url: string | URL, locale: Locale): string;
}

const defaultResolutionOrder = ["url", "cookie", "application", "browser"] as const;

export function createRunicLocaleRouting<const Locale extends string>(
  options: RunicLocaleRoutingOptions<Locale>,
): RunicLocaleRouting<Locale> {
  const locales = [...new Set(options.locales)];
  if (locales.length === 0) throw new Error("At least one supported locale is required.");
  if (!locales.includes(options.baseLocale)) {
    throw new Error(`The base locale ${JSON.stringify(options.baseLocale)} is not in locales.`);
  }
  const normalized = new Map(locales.map((locale) => [normalizeLocale(locale), locale]));
  if (normalized.size !== locales.length) {
    throw new Error("Supported locales must be unique when compared case-insensitively.");
  }
  const basePath = normalizeBasePath(options.basePath ?? "");
  const baseLocalePath = options.baseLocalePath ?? "unprefixed";
  const resolutionOrder = options.resolutionOrder ?? defaultResolutionOrder;
  assertResolutionOrder(resolutionOrder);

  function isLocale(value: string | null | undefined): value is Locale {
    return value !== null && value !== undefined && normalized.has(normalizeLocale(value));
  }

  function matchLocale(value: string | null | undefined): Locale | undefined {
    if (!value) return undefined;
    let candidate = normalizeLocale(value);
    while (candidate.length > 0) {
      const match = normalized.get(candidate);
      if (match) return match;
      const separator = candidate.lastIndexOf("-");
      if (separator < 0) return undefined;
      candidate = candidate.slice(0, separator);
    }
    return undefined;
  }

  function inspectUrl(input: string | URL): LocalizedUrl<Locale> {
    const url = toUrl(input);
    const pathname = url.pathname;
    const localPath = removeBasePath(pathname, basePath);
    if (localPath === undefined) {
      return { locale: undefined, pathname, unlocalizedPathname: pathname, hadLocalePrefix: false };
    }
    const segments = localPath.split("/");
    const locale = matchLocale(segments[1]);
    if (locale) {
      const remaining = `/${segments.slice(2).join("/")}`;
      return {
        locale,
        pathname,
        unlocalizedPathname: joinBasePath(basePath, remaining),
        hadLocalePrefix: true,
      };
    }
    return {
      locale: baseLocalePath === "unprefixed" ? options.baseLocale : undefined,
      pathname,
      unlocalizedPathname: pathname,
      hadLocalePrefix: false,
    };
  }

  function resolveLocale(input: LocaleResolutionInput): LocaleResolution<Locale> {
    const inspected = inspectUrl(input.url);
    for (const strategy of resolutionOrder) {
      const candidate = candidateFor(strategy, inspected, input, matchLocale);
      if (candidate) return { locale: candidate, source: strategy };
    }
    return { locale: options.baseLocale, source: "default" };
  }

  function localizeUrl(input: string | URL, locale: Locale): string {
    if (!isLocale(locale)) throw new Error(`Unsupported locale ${JSON.stringify(locale)}.`);
    const url = toUrl(input);
    const inspected = inspectUrl(url);
    if (removeBasePath(inspected.pathname, basePath) === undefined) {
      throw new Error(`The URL pathname is outside the configured base path ${JSON.stringify(basePath)}.`);
    }
    const localPath = removeBasePath(inspected.unlocalizedPathname, basePath) ?? "/";
    const prefix = baseLocalePath === "unprefixed" && locale === options.baseLocale ? "" : `/${locale}`;
    url.pathname = joinBasePath(basePath, `${prefix}${localPath === "/" ? "" : localPath}` || "/");
    return formatUrl(input, url);
  }

  function delocalizeUrl(input: string | URL): string {
    const url = toUrl(input);
    url.pathname = inspectUrl(url).unlocalizedPathname;
    return formatUrl(input, url);
  }

  function canonicalUrl(input: string | URL, locale: Locale): string {
    return localizeUrl(delocalizeUrl(input), locale);
  }

  return Object.freeze({
    locales: Object.freeze(locales),
    baseLocale: options.baseLocale,
    baseLocalePath,
    basePath,
    resolutionOrder: Object.freeze([...resolutionOrder]),
    isLocale,
    matchLocale,
    inspectUrl,
    resolveLocale,
    localizeUrl,
    delocalizeUrl,
    canonicalUrl,
  });
}

function candidateFor<Locale extends string>(
  strategy: LocaleResolutionStrategy,
  inspected: LocalizedUrl<Locale>,
  input: LocaleResolutionInput,
  match: (value: string | null | undefined) => Locale | undefined,
): Locale | undefined {
  switch (strategy) {
    case "url": return inspected.locale;
    case "cookie": return match(input.cookieLocale);
    case "application": return match(input.applicationLocale);
    case "browser": {
      const preferences = input.browserLocales ?? parseAcceptLanguage(input.acceptLanguage);
      for (const preference of preferences) {
        const locale = match(preference);
        if (locale) return locale;
      }
      return undefined;
    }
  }
}

export function parseAcceptLanguage(value: string | null | undefined): readonly string[] {
  if (!value) return [];
  return value.split(",")
    .map((part, index) => {
      const [locale = "", ...parameters] = part.trim().split(";");
      const q = parameters.find((parameter) => parameter.trim().startsWith("q="));
      const quality = q ? Number(q.trim().slice(2)) : 1;
      return { locale: locale.trim(), quality: Number.isFinite(quality) ? quality : 0, index };
    })
    .filter(({ locale, quality }) => locale !== "*" && locale.length > 0 && quality > 0)
    .sort((left, right) => right.quality - left.quality || left.index - right.index)
    .map(({ locale }) => locale);
}

function normalizeLocale(locale: string): string {
  return locale.trim().replaceAll("_", "-").toLowerCase();
}

function normalizeBasePath(value: string): string {
  if (value === "" || value === "/") return "";
  const path = value.startsWith("/") ? value : `/${value}`;
  return path.endsWith("/") ? path.slice(0, -1) : path;
}

function removeBasePath(pathname: string, basePath: string): string | undefined {
  if (basePath === "") return pathname;
  if (pathname === basePath) return "/";
  return pathname.startsWith(`${basePath}/`) ? pathname.slice(basePath.length) : undefined;
}

function joinBasePath(basePath: string, pathname: string): string {
  const local = pathname === "" ? "/" : pathname.startsWith("/") ? pathname : `/${pathname}`;
  if (basePath === "") return local;
  return local === "/" ? basePath || "/" : `${basePath}${local}`;
}

function toUrl(value: string | URL): URL {
  return value instanceof URL ? new URL(value) : new URL(value, "https://runic.invalid");
}

function formatUrl(original: string | URL, url: URL): string {
  if (original instanceof URL || /^[a-z][a-z\d+.-]*:/i.test(original)) return url.href;
  return `${url.pathname}${url.search}${url.hash}`;
}

function assertResolutionOrder(order: readonly LocaleResolutionStrategy[]): void {
  const allowed = new Set<LocaleResolutionStrategy>(["url", "cookie", "application", "browser"]);
  const seen = new Set<LocaleResolutionStrategy>();
  for (const strategy of order) {
    if (!allowed.has(strategy) || seen.has(strategy)) {
      throw new Error(`Invalid or duplicate locale resolution strategy ${JSON.stringify(strategy)}.`);
    }
    seen.add(strategy);
  }
}
