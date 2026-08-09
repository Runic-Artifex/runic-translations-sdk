import { c as on, i as unmount, n as mount, r as tick } from "../../chunks/index-server.js";
import { D as escape_html, E as clsx$1, St as run, T as attr, _ as getContext, a as derived, c as head, d as spread_props, et as snapshot$1, f as stringify, g as getAllContexts, i as bind_props, l as props_id, n as attr_style, o as element, r as attributes, s as ensure_array_like, t as attr_class, ut as ATTACHMENT_KEY, v as hasContext, y as setContext } from "../../chunks/server.js";
import { clsx } from "clsx";
import parse from "style-to-object";
import { focusable, isFocusable, isTabbable, tabbable } from "tabbable";
import { arrow, autoUpdate, computePosition, flip, hide, limitShift, offset, shift, size } from "@floating-ui/dom";
import { tv } from "tailwind-variants";
import { twMerge } from "tailwind-merge";
var locales = Object.freeze(["de", "en"]);
var localeSet = new Set(locales);
var localeResolver = () => "en";
function getLocale() {
	return resolveLocale(localeResolver());
}
function resolveLocale(requested) {
	if (typeof requested !== "string" || requested.length === 0) throw new RangeError("A non-empty locale is required.");
	const canonical = canonicalizeLocale(requested);
	if (localeSet.has(canonical)) return canonical;
	let parent = canonical;
	while (parent.includes("-")) {
		parent = parent.slice(0, parent.lastIndexOf("-"));
		if (localeSet.has(parent)) return parent;
	}
	return "en";
}
function localized(value) {
	return value;
}
function canonicalizeLocale(locale) {
	try {
		return Intl.getCanonicalLocales(locale)[0];
	} catch {
		throw new RangeError(`Invalid locale '${locale}'.`);
	}
}
//#endregion
//#region ../obj/Release/net10.0/linux-x64/text-resources/editor.esm/messages/m$App$Advanced.js
Object.freeze([]);
function m$App$Advanced(options) {
	const locale = resolveLocale(options?.locale ?? getLocale());
	switch (locale) {
		case "de": return localized("Varianten");
		case "en": return localized("Variants");
		default: throw new RangeError(`Unsupported locale '${locale}'.`);
	}
}
//#endregion
//#region ../obj/Release/net10.0/linux-x64/text-resources/editor.esm/messages/m$App$All.js
Object.freeze([]);
function m$App$All(options) {
	const locale = resolveLocale(options?.locale ?? getLocale());
	switch (locale) {
		case "de": return localized("Alle");
		case "en": return localized("All");
		default: throw new RangeError(`Unsupported locale '${locale}'.`);
	}
}
//#endregion
//#region ../obj/Release/net10.0/linux-x64/text-resources/editor.esm/messages/m$App$DefaultLocale.js
Object.freeze([]);
function m$App$DefaultLocale(options) {
	const locale = resolveLocale(options?.locale ?? getLocale());
	switch (locale) {
		case "de": return localized("Ausgangssprache");
		case "en": return localized("Source locale");
		default: throw new RangeError(`Unsupported locale '${locale}'.`);
	}
}
//#endregion
//#region ../obj/Release/net10.0/linux-x64/text-resources/editor.esm/messages/m$App$Diagnostics.js
Object.freeze([]);
function m$App$Diagnostics(options) {
	const locale = resolveLocale(options?.locale ?? getLocale());
	switch (locale) {
		case "de": return localized("Diagnosen");
		case "en": return localized("Diagnostics");
		default: throw new RangeError(`Unsupported locale '${locale}'.`);
	}
}
//#endregion
//#region ../obj/Release/net10.0/linux-x64/text-resources/editor.esm/messages/m$App$Eyebrow.js
Object.freeze([]);
function m$App$Eyebrow(options) {
	const locale = resolveLocale(options?.locale ?? getLocale());
	switch (locale) {
		case "de": return localized("Runic Text Resources");
		case "en": return localized("Runic Text Resources");
		default: throw new RangeError(`Unsupported locale '${locale}'.`);
	}
}
//#endregion
//#region ../obj/Release/net10.0/linux-x64/text-resources/editor.esm/messages/m$App$Invalid.js
Object.freeze([]);
function m$App$Invalid(options) {
	const locale = resolveLocale(options?.locale ?? getLocale());
	switch (locale) {
		case "de": return localized("Behebe die Validierungsfehler vor dem Speichern");
		case "en": return localized("Resolve validation errors before saving");
		default: throw new RangeError(`Unsupported locale '${locale}'.`);
	}
}
//#endregion
//#region ../obj/Release/net10.0/linux-x64/text-resources/editor.esm/messages/m$App$Missing.js
Object.freeze([]);
function m$App$Missing(options) {
	const locale = resolveLocale(options?.locale ?? getLocale());
	switch (locale) {
		case "de": return localized("Fehlend");
		case "en": return localized("Missing");
		default: throw new RangeError(`Unsupported locale '${locale}'.`);
	}
}
//#endregion
//#region ../obj/Release/net10.0/linux-x64/text-resources/editor.esm/messages/m$App$NoResults.js
Object.freeze([]);
function m$App$NoResults(options) {
	const locale = resolveLocale(options?.locale ?? getLocale());
	switch (locale) {
		case "de": return localized("Keine Nachrichten passen zu dieser Ansicht");
		case "en": return localized("No messages match this view");
		default: throw new RangeError(`Unsupported locale '${locale}'.`);
	}
}
//#endregion
//#region ../obj/Release/net10.0/linux-x64/text-resources/editor.esm/messages/m$App$NoSelection.js
Object.freeze([]);
function m$App$NoSelection(options) {
	const locale = resolveLocale(options?.locale ?? getLocale());
	switch (locale) {
		case "de": return localized("Wähle eine Nachricht aus, um sie zu übersetzen");
		case "en": return localized("Choose a message to start translating");
		default: throw new RangeError(`Unsupported locale '${locale}'.`);
	}
}
//#endregion
//#region ../obj/Release/net10.0/linux-x64/text-resources/editor.esm/messages/m$App$Raw.js
Object.freeze([]);
function m$App$Raw(options) {
	const locale = resolveLocale(options?.locale ?? getLocale());
	switch (locale) {
		case "de": return localized("Quelldatei");
		case "en": return localized("Source");
		default: throw new RangeError(`Unsupported locale '${locale}'.`);
	}
}
//#endregion
//#region ../obj/Release/net10.0/linux-x64/text-resources/editor.esm/messages/m$App$Reload.js
Object.freeze([]);
function m$App$Reload(options) {
	const locale = resolveLocale(options?.locale ?? getLocale());
	switch (locale) {
		case "de": return localized("Neu laden");
		case "en": return localized("Reload");
		default: throw new RangeError(`Unsupported locale '${locale}'.`);
	}
}
//#endregion
//#region ../obj/Release/net10.0/linux-x64/text-resources/editor.esm/messages/m$App$Save.js
Object.freeze([]);
function m$App$Save(options) {
	const locale = resolveLocale(options?.locale ?? getLocale());
	switch (locale) {
		case "de": return localized("Dokument speichern");
		case "en": return localized("Save document");
		default: throw new RangeError(`Unsupported locale '${locale}'.`);
	}
}
//#endregion
//#region ../obj/Release/net10.0/linux-x64/text-resources/editor.esm/messages/m$App$Saved.js
Object.freeze([]);
function m$App$Saved(options) {
	const locale = resolveLocale(options?.locale ?? getLocale());
	switch (locale) {
		case "de": return localized("Gespeichert");
		case "en": return localized("Saved");
		default: throw new RangeError(`Unsupported locale '${locale}'.`);
	}
}
//#endregion
//#region ../obj/Release/net10.0/linux-x64/text-resources/editor.esm/messages/m$App$Saving.js
Object.freeze([]);
function m$App$Saving(options) {
	const locale = resolveLocale(options?.locale ?? getLocale());
	switch (locale) {
		case "de": return localized("Speichern…");
		case "en": return localized("Saving…");
		default: throw new RangeError(`Unsupported locale '${locale}'.`);
	}
}
//#endregion
//#region ../obj/Release/net10.0/linux-x64/text-resources/editor.esm/messages/m$App$Search.js
Object.freeze([]);
function m$App$Search(options) {
	const locale = resolveLocale(options?.locale ?? getLocale());
	switch (locale) {
		case "de": return localized("Nachrichten suchen…");
		case "en": return localized("Search messages…");
		default: throw new RangeError(`Unsupported locale '${locale}'.`);
	}
}
//#endregion
//#region ../obj/Release/net10.0/linux-x64/text-resources/editor.esm/messages/m$App$Simple.js
Object.freeze([]);
function m$App$Simple(options) {
	const locale = resolveLocale(options?.locale ?? getLocale());
	switch (locale) {
		case "de": return localized("Übersetzung");
		case "en": return localized("Translation");
		default: throw new RangeError(`Unsupported locale '${locale}'.`);
	}
}
//#endregion
//#region ../obj/Release/net10.0/linux-x64/text-resources/editor.esm/messages/m$App$Structured.js
Object.freeze([]);
function m$App$Structured(options) {
	const locale = resolveLocale(options?.locale ?? getLocale());
	switch (locale) {
		case "de": return localized("Strukturiert");
		case "en": return localized("Structured");
		default: throw new RangeError(`Unsupported locale '${locale}'.`);
	}
}
//#endregion
//#region ../obj/Release/net10.0/linux-x64/text-resources/editor.esm/messages/m$App$Title.js
Object.freeze([]);
function m$App$Title(options) {
	const locale = resolveLocale(options?.locale ?? getLocale());
	switch (locale) {
		case "de": return localized("Übersetzungen");
		case "en": return localized("Translations");
		default: throw new RangeError(`Unsupported locale '${locale}'.`);
	}
}
//#endregion
//#region ../obj/Release/net10.0/linux-x64/text-resources/editor.esm/messages/m$App$Unsaved.js
Object.freeze([]);
function m$App$Unsaved(options) {
	const locale = resolveLocale(options?.locale ?? getLocale());
	switch (locale) {
		case "de": return localized("Ungespeicherte Änderungen");
		case "en": return localized("Unsaved changes");
		default: throw new RangeError(`Unsupported locale '${locale}'.`);
	}
}
//#endregion
//#region ../obj/Release/net10.0/linux-x64/text-resources/editor.esm/messages/m$App$Valid.js
Object.freeze([]);
function m$App$Valid(options) {
	const locale = resolveLocale(options?.locale ?? getLocale());
	switch (locale) {
		case "de": return localized("Bereit zum Speichern");
		case "en": return localized("Ready to save");
		default: throw new RangeError(`Unsupported locale '${locale}'.`);
	}
}
//#endregion
//#region ../obj/Release/net10.0/linux-x64/text-resources/editor.esm/messages/m$App$Workspace.js
Object.freeze([]);
function m$App$Workspace(options) {
	const locale = resolveLocale(options?.locale ?? getLocale());
	switch (locale) {
		case "de": return localized("Arbeitsbereich");
		case "en": return localized("Workspace");
		default: throw new RangeError(`Unsupported locale '${locale}'.`);
	}
}
var modeKey = "runic-text-resources.theme-mode";
var paletteKey = "runic-text-resources.theme-palette";
function applyAppearance(mode, palette) {
	if (typeof document === "undefined") return;
	const dark = mode === "dark" || mode === "system" && matchMedia("(prefers-color-scheme: dark)").matches;
	document.documentElement.classList.toggle("dark", dark);
	document.documentElement.dataset.theme = palette;
	document.documentElement.style.colorScheme = dark ? "dark" : "light";
}
function saveAppearance(mode, palette) {
	if (typeof localStorage !== "undefined") {
		localStorage.setItem(modeKey, mode);
		localStorage.setItem(paletteKey, palette);
	}
	applyAppearance(mode, palette);
}
//#endregion
//#region node_modules/svelte-toolbelt/dist/utils/is.js
function isFunction$1(value) {
	return typeof value === "function";
}
function isObject$2(value) {
	return value !== null && typeof value === "object";
}
var CLASS_VALUE_PRIMITIVE_TYPES = [
	"string",
	"number",
	"bigint",
	"boolean"
];
function isClassValue(value) {
	if (value === null || value === void 0) return true;
	if (CLASS_VALUE_PRIMITIVE_TYPES.includes(typeof value)) return true;
	if (Array.isArray(value)) return value.every((item) => isClassValue(item));
	if (typeof value === "object") {
		if (Object.getPrototypeOf(value) !== Object.prototype) return false;
		return true;
	}
	return false;
}
//#endregion
//#region node_modules/svelte-toolbelt/dist/box/box-extras.svelte.js
var BoxSymbol = Symbol("box");
var isWritableSymbol = Symbol("is-writable");
function boxWith(getter, setter) {
	const derived$1 = derived(getter);
	if (setter) return {
		[BoxSymbol]: true,
		[isWritableSymbol]: true,
		get current() {
			return derived$1();
		},
		set current(v) {
			setter(v);
		}
	};
	return {
		[BoxSymbol]: true,
		get current() {
			return getter();
		}
	};
}
/**
* @returns Whether the value is a Box
*
* @see {@link https://runed.dev/docs/functions/box}
*/
function isBox(value) {
	return isObject$2(value) && BoxSymbol in value;
}
/**
* @returns Whether the value is a WritableBox
*
* @see {@link https://runed.dev/docs/functions/box}
*/
function isWritableBox(value) {
	return isBox(value) && isWritableSymbol in value;
}
function boxFrom(value) {
	if (isBox(value)) return value;
	if (isFunction$1(value)) return boxWith(value);
	return simpleBox(value);
}
/**
* Function that gets an object of boxes, and returns an object of reactive values
*
* @example
* const count = box(0)
* const flat = box.flatten({ count, double: box.with(() => count.current) })
* // type of flat is { count: number, readonly double: number }
*
* @see {@link https://runed.dev/docs/functions/box}
*/
function boxFlatten(boxes) {
	return Object.entries(boxes).reduce((acc, [key, b]) => {
		if (!isBox(b)) return Object.assign(acc, { [key]: b });
		if (isWritableBox(b)) Object.defineProperty(acc, key, {
			get() {
				return b.current;
			},
			set(v) {
				b.current = v;
			}
		});
		else Object.defineProperty(acc, key, { get() {
			return b.current;
		} });
		return acc;
	}, {});
}
/**
* Function that converts a box to a readonly box.
*
* @example
* const count = box(0) // WritableBox<number>
* const countReadonly = box.readonly(count) // ReadableBox<number>
*
* @see {@link https://runed.dev/docs/functions/box}
*/
function toReadonlyBox(b) {
	if (!isWritableBox(b)) return b;
	return {
		[BoxSymbol]: true,
		get current() {
			return b.current;
		}
	};
}
function simpleBox(initialValue) {
	let current = initialValue;
	return {
		[BoxSymbol]: true,
		[isWritableSymbol]: true,
		get current() {
			return current;
		},
		set current(v) {
			current = v;
		}
	};
}
//#endregion
//#region node_modules/svelte-toolbelt/dist/box/box.svelte.js
function box(initialValue) {
	let current = initialValue;
	return {
		[BoxSymbol]: true,
		[isWritableSymbol]: true,
		get current() {
			return current;
		},
		set current(v) {
			current = v;
		}
	};
}
box.from = boxFrom;
box.with = boxWith;
box.flatten = boxFlatten;
box.readonly = toReadonlyBox;
box.isBox = isBox;
box.isWritableBox = isWritableBox;
//#endregion
//#region node_modules/svelte-toolbelt/dist/utils/compose-handlers.js
/**
* Composes event handlers into a single function that can be called with an event.
* If the previous handler cancels the event using `event.preventDefault()`, the handlers
* that follow will not be called.
*/
function composeHandlers(...handlers) {
	return function(e) {
		for (const handler of handlers) {
			if (!handler) continue;
			if (e.defaultPrevented) return;
			if (typeof handler === "function") handler.call(this, e);
			else handler.current?.call(this, e);
		}
	};
}
//#endregion
//#region node_modules/svelte-toolbelt/dist/utils/strings.js
var NUMBER_CHAR_RE = /\d/;
var STR_SPLITTERS = [
	"-",
	"_",
	"/",
	"."
];
function isUppercase(char = "") {
	if (NUMBER_CHAR_RE.test(char)) return void 0;
	return char !== char.toLowerCase();
}
function splitByCase(str) {
	const parts = [];
	let buff = "";
	let previousUpper;
	let previousSplitter;
	for (const char of str) {
		const isSplitter = STR_SPLITTERS.includes(char);
		if (isSplitter === true) {
			parts.push(buff);
			buff = "";
			previousUpper = void 0;
			continue;
		}
		const isUpper = isUppercase(char);
		if (previousSplitter === false) {
			if (previousUpper === false && isUpper === true) {
				parts.push(buff);
				buff = char;
				previousUpper = isUpper;
				continue;
			}
			if (previousUpper === true && isUpper === false && buff.length > 1) {
				const lastChar = buff.at(-1);
				parts.push(buff.slice(0, Math.max(0, buff.length - 1)));
				buff = lastChar + char;
				previousUpper = isUpper;
				continue;
			}
		}
		buff += char;
		previousUpper = isUpper;
		previousSplitter = isSplitter;
	}
	parts.push(buff);
	return parts;
}
function pascalCase(str) {
	if (!str) return "";
	return splitByCase(str).map((p) => upperFirst(p)).join("");
}
function camelCase(str) {
	return lowerFirst(pascalCase(str || ""));
}
function upperFirst(str) {
	return str ? str[0].toUpperCase() + str.slice(1) : "";
}
function lowerFirst(str) {
	return str ? str[0].toLowerCase() + str.slice(1) : "";
}
//#endregion
//#region node_modules/svelte-toolbelt/dist/utils/css-to-style-obj.js
function cssToStyleObj(css) {
	if (!css) return {};
	const styleObj = {};
	function iterator(name, value) {
		if (name.startsWith("-moz-") || name.startsWith("-webkit-") || name.startsWith("-ms-") || name.startsWith("-o-")) {
			styleObj[pascalCase(name)] = value;
			return;
		}
		if (name.startsWith("--")) {
			styleObj[name] = value;
			return;
		}
		styleObj[camelCase(name)] = value;
	}
	parse(css, iterator);
	return styleObj;
}
//#endregion
//#region node_modules/svelte-toolbelt/dist/utils/execute-callbacks.js
/**
* Executes an array of callback functions with the same arguments.
* @template T The types of the arguments that the callback functions take.
* @param callbacks array of callback functions to execute.
* @returns A new function that executes all of the original callback functions with the same arguments.
*/
function executeCallbacks(...callbacks) {
	return (...args) => {
		for (const callback of callbacks) if (typeof callback === "function") callback(...args);
	};
}
//#endregion
//#region node_modules/svelte-toolbelt/dist/utils/style-to-css.js
function createParser(matcher, replacer) {
	const regex = RegExp(matcher, "g");
	return (str) => {
		if (typeof str !== "string") throw new TypeError(`expected an argument of type string, but got ${typeof str}`);
		if (!str.match(regex)) return str;
		return str.replace(regex, replacer);
	};
}
var camelToKebab = createParser(/[A-Z]/, (match) => `-${match.toLowerCase()}`);
function styleToCSS(styleObj) {
	if (!styleObj || typeof styleObj !== "object" || Array.isArray(styleObj)) throw new TypeError(`expected an argument of type object, but got ${typeof styleObj}`);
	return Object.keys(styleObj).map((property) => `${camelToKebab(property)}: ${styleObj[property]};`).join("\n");
}
//#endregion
//#region node_modules/svelte-toolbelt/dist/utils/style.js
function styleToString(style = {}) {
	return styleToCSS(style).replace("\n", " ");
}
var EVENT_LIST_SET = /* @__PURE__ */ new Set([
	"onabort",
	"onanimationcancel",
	"onanimationend",
	"onanimationiteration",
	"onanimationstart",
	"onauxclick",
	"onbeforeinput",
	"onbeforetoggle",
	"onblur",
	"oncancel",
	"oncanplay",
	"oncanplaythrough",
	"onchange",
	"onclick",
	"onclose",
	"oncompositionend",
	"oncompositionstart",
	"oncompositionupdate",
	"oncontextlost",
	"oncontextmenu",
	"oncontextrestored",
	"oncopy",
	"oncuechange",
	"oncut",
	"ondblclick",
	"ondrag",
	"ondragend",
	"ondragenter",
	"ondragleave",
	"ondragover",
	"ondragstart",
	"ondrop",
	"ondurationchange",
	"onemptied",
	"onended",
	"onerror",
	"onfocus",
	"onfocusin",
	"onfocusout",
	"onformdata",
	"ongotpointercapture",
	"oninput",
	"oninvalid",
	"onkeydown",
	"onkeypress",
	"onkeyup",
	"onload",
	"onloadeddata",
	"onloadedmetadata",
	"onloadstart",
	"onlostpointercapture",
	"onmousedown",
	"onmouseenter",
	"onmouseleave",
	"onmousemove",
	"onmouseout",
	"onmouseover",
	"onmouseup",
	"onpaste",
	"onpause",
	"onplay",
	"onplaying",
	"onpointercancel",
	"onpointerdown",
	"onpointerenter",
	"onpointerleave",
	"onpointermove",
	"onpointerout",
	"onpointerover",
	"onpointerup",
	"onprogress",
	"onratechange",
	"onreset",
	"onresize",
	"onscroll",
	"onscrollend",
	"onsecuritypolicyviolation",
	"onseeked",
	"onseeking",
	"onselect",
	"onselectionchange",
	"onselectstart",
	"onslotchange",
	"onstalled",
	"onsubmit",
	"onsuspend",
	"ontimeupdate",
	"ontoggle",
	"ontouchcancel",
	"ontouchend",
	"ontouchmove",
	"ontouchstart",
	"ontransitioncancel",
	"ontransitionend",
	"ontransitionrun",
	"ontransitionstart",
	"onvolumechange",
	"onwaiting",
	"onwebkitanimationend",
	"onwebkitanimationiteration",
	"onwebkitanimationstart",
	"onwebkittransitionend",
	"onwheel"
]);
//#endregion
//#region node_modules/svelte-toolbelt/dist/utils/merge-props.js
/**
* Modified from https://github.com/adobe/react-spectrum/blob/main/packages/%40react-aria/utils/src/mergeProps.ts (see NOTICE.txt for source)
*/
function isEventHandler(key) {
	return EVENT_LIST_SET.has(key);
}
/**
* Given a list of prop objects, merges them into a single object.
* - Automatically composes event handlers (e.g. `onclick`, `oninput`, etc.)
* - Chains regular functions with the same name so they are called in order
* - Merges class strings with `clsx`
* - Merges style objects and converts them to strings
* - Handles a bug with Svelte where setting the `hidden` attribute to `false` doesn't remove it
* - Overrides other values with the last one
*/
function mergeProps(...args) {
	const result = { ...args[0] };
	for (let i = 1; i < args.length; i++) {
		const props = args[i];
		if (!props) continue;
		for (const key of Object.keys(props)) {
			const a = result[key];
			const b = props[key];
			const aIsFunction = typeof a === "function";
			const bIsFunction = typeof b === "function";
			if (aIsFunction && typeof bIsFunction && isEventHandler(key)) result[key] = composeHandlers(a, b);
			else if (aIsFunction && bIsFunction) result[key] = executeCallbacks(a, b);
			else if (key === "class") {
				const aIsClassValue = isClassValue(a);
				const bIsClassValue = isClassValue(b);
				if (aIsClassValue && bIsClassValue) result[key] = clsx(a, b);
				else if (aIsClassValue) result[key] = clsx(a);
				else if (bIsClassValue) result[key] = clsx(b);
			} else if (key === "style") {
				const aIsObject = typeof a === "object";
				const bIsObject = typeof b === "object";
				const aIsString = typeof a === "string";
				const bIsString = typeof b === "string";
				if (aIsObject && bIsObject) result[key] = {
					...a,
					...b
				};
				else if (aIsObject && bIsString) {
					const parsedStyle = cssToStyleObj(b);
					result[key] = {
						...a,
						...parsedStyle
					};
				} else if (aIsString && bIsObject) result[key] = {
					...cssToStyleObj(a),
					...b
				};
				else if (aIsString && bIsString) {
					const parsedStyleA = cssToStyleObj(a);
					const parsedStyleB = cssToStyleObj(b);
					result[key] = {
						...parsedStyleA,
						...parsedStyleB
					};
				} else if (aIsObject) result[key] = a;
				else if (bIsObject) result[key] = b;
				else if (aIsString) result[key] = a;
				else if (bIsString) result[key] = b;
			} else result[key] = b !== void 0 ? b : a;
		}
		for (const key of Object.getOwnPropertySymbols(props)) {
			const a = result[key];
			const b = props[key];
			result[key] = b !== void 0 ? b : a;
		}
	}
	if (typeof result.style === "object") result.style = styleToString(result.style).replaceAll("\n", " ");
	if (result.hidden === false) {
		result.hidden = void 0;
		delete result.hidden;
	}
	if (result.disabled === false) {
		result.disabled = void 0;
		delete result.disabled;
	}
	return result;
}
//#endregion
//#region node_modules/svelte-toolbelt/dist/utils/sr-only-styles.js
var srOnlyStyles = {
	position: "absolute",
	width: "1px",
	height: "1px",
	padding: "0",
	margin: "-1px",
	overflow: "hidden",
	clip: "rect(0, 0, 0, 0)",
	whiteSpace: "nowrap",
	borderWidth: "0",
	transform: "translateX(-100%)"
};
styleToString(srOnlyStyles);
//#endregion
//#region node_modules/runed/dist/internal/configurable-globals.js
var defaultWindow = void 0;
//#endregion
//#region node_modules/runed/dist/internal/utils/dom.js
/**
* Handles getting the active element in a document or shadow root.
* If the active element is within a shadow root, it will traverse the shadow root
* to find the active element.
* If not, it will return the active element in the document.
*
* @param document A document or shadow root to get the active element from.
* @returns The active element in the document or shadow root.
*/
function getActiveElement$1(document) {
	let activeElement = document.activeElement;
	while (activeElement?.shadowRoot) {
		const node = activeElement.shadowRoot.activeElement;
		if (node === activeElement) break;
		else activeElement = node;
	}
	return activeElement;
}
globalThis.Date;
globalThis.Set;
var SvelteMap = globalThis.Map;
globalThis.URL;
globalThis.URLSearchParams;
var MediaQuery = class {
	current;
	/**
	* @param {string} query
	* @param {boolean} [matches]
	*/
	constructor(query, matches = false) {
		this.current = matches;
	}
};
/**
* @param {any} _
*/
function createSubscriber(_) {
	return () => {};
}
//#endregion
//#region node_modules/runed/dist/utilities/active-element/active-element.svelte.js
var ActiveElement = class {
	#document;
	#subscribe;
	constructor(options = {}) {
		const { window = defaultWindow, document = window?.document } = options;
		if (window === void 0) return;
		this.#document = document;
		this.#subscribe = createSubscriber((update) => {
			const cleanupFocusIn = on(window, "focusin", update);
			const cleanupFocusOut = on(window, "focusout", update);
			return () => {
				cleanupFocusIn();
				cleanupFocusOut();
			};
		});
	}
	get current() {
		this.#subscribe?.();
		if (!this.#document) return null;
		return getActiveElement$1(this.#document);
	}
};
new ActiveElement();
//#endregion
//#region node_modules/runed/dist/internal/utils/is.js
function isFunction(value) {
	return typeof value === "function";
}
//#endregion
//#region node_modules/runed/dist/utilities/extract/extract.svelte.js
function extract(value, defaultValue) {
	if (isFunction(value)) {
		const gotten = value();
		if (gotten === void 0) return defaultValue;
		return gotten;
	}
	if (value === void 0) return defaultValue;
	return value;
}
//#endregion
//#region node_modules/runed/dist/utilities/context/context.js
var Context = class {
	#name;
	#key;
	/**
	* @param name The name of the context.
	* This is used for generating the context key and error messages.
	*/
	constructor(name) {
		this.#name = name;
		this.#key = Symbol(name);
	}
	/**
	* The key used to get and set the context.
	*
	* It is not recommended to use this value directly.
	* Instead, use the methods provided by this class.
	*/
	get key() {
		return this.#key;
	}
	/**
	* Checks whether this has been set in the context of a parent component.
	*
	* Must be called during component initialisation.
	*/
	exists() {
		return hasContext(this.#key);
	}
	/**
	* Retrieves the context that belongs to the closest parent component.
	*
	* Must be called during component initialisation.
	*
	* @throws An error if the context does not exist.
	*/
	get() {
		const context = getContext(this.#key);
		if (context === void 0) throw new Error(`Context "${this.#name}" not found`);
		return context;
	}
	/**
	* Retrieves the context that belongs to the closest parent component,
	* or the given fallback value if the context does not exist.
	*
	* Must be called during component initialisation.
	*/
	getOr(fallback) {
		const context = getContext(this.#key);
		if (context === void 0) return fallback;
		return context;
	}
	/**
	* Associates the given value with the current component and returns it.
	*
	* Must be called during component initialisation.
	*/
	set(context) {
		return setContext(this.#key, context);
	}
};
//#endregion
//#region node_modules/runed/dist/utilities/use-debounce/use-debounce.svelte.js
function useDebounce(callback, wait) {
	let context = null;
	const wait$ = derived(() => extract(wait, 250));
	function debounced(...args) {
		if (context) {
			if (context.timeout) clearTimeout(context.timeout);
		} else {
			let resolve;
			let reject;
			context = {
				timeout: null,
				runner: null,
				promise: new Promise((res, rej) => {
					resolve = res;
					reject = rej;
				}),
				resolve,
				reject
			};
		}
		context.runner = async () => {
			if (!context) return;
			const ctx = context;
			context = null;
			try {
				ctx.resolve(await callback.apply(this, args));
			} catch (error) {
				ctx.reject(error);
			}
		};
		context.timeout = setTimeout(context.runner, wait$());
		return context.promise;
	}
	debounced.cancel = async () => {
		if (!context || context.timeout === null) {
			await new Promise((resolve) => setTimeout(resolve, 0));
			if (!context || context.timeout === null) return;
		}
		clearTimeout(context.timeout);
		context.reject("Cancelled");
		context = null;
	};
	debounced.runScheduledNow = async () => {
		if (!context || !context.timeout) {
			await new Promise((resolve) => setTimeout(resolve, 0));
			if (!context || !context.timeout) return;
		}
		clearTimeout(context.timeout);
		context.timeout = null;
		await context.runner?.();
	};
	Object.defineProperty(debounced, "pending", {
		enumerable: true,
		get() {
			return !!context?.timeout;
		}
	});
	return debounced;
}
//#endregion
//#region node_modules/runed/dist/utilities/watch/watch.svelte.js
function runWatcher(sources, flush, effect, options = {}) {
	const { lazy = false } = options;
}
function watch(sources, effect, options) {
	runWatcher(sources, "post", effect, options);
}
function watchPre(sources, effect, options) {
	runWatcher(sources, "pre", effect, options);
}
watch.pre = watchPre;
function watchOnce(source, effect) {}
function watchOncePre(source, effect) {}
watchOnce.pre = watchOncePre;
//#endregion
//#region node_modules/runed/dist/internal/utils/get.js
function get$1(value) {
	if (isFunction(value)) return value();
	return value;
}
//#endregion
//#region node_modules/runed/dist/utilities/element-size/element-size.svelte.js
var ElementSize = class {
	#size = {
		width: 0,
		height: 0
	};
	#observed = false;
	#options;
	#node;
	#window;
	#width = derived(() => {
		this.#subscribe()?.();
		return this.getSize().width;
	});
	#height = derived(() => {
		this.#subscribe()?.();
		return this.getSize().height;
	});
	#subscribe = derived(() => {
		const node$ = get$1(this.#node);
		if (!node$) return;
		return createSubscriber((update) => {
			if (!this.#window) return;
			const observer = new this.#window.ResizeObserver((entries) => {
				this.#observed = true;
				for (const entry of entries) {
					const boxSize = this.#options.box === "content-box" ? entry.contentBoxSize : entry.borderBoxSize;
					const boxSizeArr = Array.isArray(boxSize) ? boxSize : [boxSize];
					this.#size.width = boxSizeArr.reduce((acc, size) => Math.max(acc, size.inlineSize), 0);
					this.#size.height = boxSizeArr.reduce((acc, size) => Math.max(acc, size.blockSize), 0);
				}
				update();
			});
			observer.observe(node$);
			return () => {
				this.#observed = false;
				observer.disconnect();
			};
		});
	});
	constructor(node, options = { box: "border-box" }) {
		this.#window = options.window ?? defaultWindow;
		this.#options = options;
		this.#node = node;
		this.#size = {
			width: 0,
			height: 0
		};
	}
	calculateSize() {
		const element = get$1(this.#node);
		if (!element || !this.#window) return;
		const offsetWidth = element.offsetWidth;
		const offsetHeight = element.offsetHeight;
		if (this.#options.box === "border-box") return {
			width: offsetWidth,
			height: offsetHeight
		};
		const style = this.#window.getComputedStyle(element);
		const paddingWidth = parseFloat(style.paddingLeft) + parseFloat(style.paddingRight);
		const paddingHeight = parseFloat(style.paddingTop) + parseFloat(style.paddingBottom);
		const borderWidth = parseFloat(style.borderLeftWidth) + parseFloat(style.borderRightWidth);
		const borderHeight = parseFloat(style.borderTopWidth) + parseFloat(style.borderBottomWidth);
		return {
			width: offsetWidth - paddingWidth - borderWidth,
			height: offsetHeight - paddingHeight - borderHeight
		};
	}
	getSize() {
		return this.#observed ? this.#size : this.calculateSize() ?? this.#size;
	}
	get current() {
		this.#subscribe()?.();
		return this.getSize();
	}
	get width() {
		return this.#width();
	}
	get height() {
		return this.#height();
	}
};
//#endregion
//#region node_modules/runed/dist/utilities/is-mounted/is-mounted.svelte.js
var IsMounted = class {
	#isMounted = false;
	constructor() {}
	get current() {
		return this.#isMounted;
	}
};
//#endregion
//#region node_modules/runed/dist/utilities/previous/previous.svelte.js
var Previous = class {
	#previousCallback = () => void 0;
	#previous = derived(() => this.#previousCallback());
	constructor(getter, initialValue) {
		let actualPrevious = void 0;
		if (initialValue !== void 0) actualPrevious = initialValue;
		this.#previousCallback = () => {
			try {
				return actualPrevious;
			} finally {
				actualPrevious = getter();
			}
		};
	}
	get current() {
		return this.#previous();
	}
};
//#endregion
//#region node_modules/runed/dist/utilities/resource/resource.svelte.js
function debounce$1(fn, delay) {
	let timeoutId;
	let lastResolve = null;
	return (...args) => {
		return new Promise((resolve) => {
			if (lastResolve) lastResolve(void 0);
			lastResolve = resolve;
			clearTimeout(timeoutId);
			timeoutId = setTimeout(async () => {
				const result = await fn(...args);
				if (lastResolve) {
					lastResolve(result);
					lastResolve = null;
				}
			}, delay);
		});
	};
}
function throttle(fn, delay) {
	let lastRun = 0;
	let lastPromise = null;
	return (...args) => {
		const now = Date.now();
		if (lastRun && now - lastRun < delay) return lastPromise ?? Promise.resolve(void 0);
		lastRun = now;
		lastPromise = fn(...args);
		return lastPromise;
	};
}
function runResource(source, fetcher, options = {}, effectFn) {
	const { lazy = false, once = false, initialValue, debounce: debounceTime, throttle: throttleTime } = options;
	let current = initialValue;
	let loading = false;
	let error = void 0;
	let cleanupFns = [];
	const runCleanup = () => {
		cleanupFns.forEach((fn) => fn());
		cleanupFns = [];
	};
	const onCleanup = (fn) => {
		cleanupFns = [...cleanupFns, fn];
	};
	const baseFetcher = async (value, previousValue, refetching = false) => {
		try {
			loading = true;
			error = void 0;
			runCleanup();
			const controller = new AbortController();
			onCleanup(() => controller.abort());
			const result = await fetcher(value, previousValue, {
				data: current,
				refetching,
				onCleanup,
				signal: controller.signal
			});
			current = result;
			return result;
		} catch (e) {
			if (!(e instanceof DOMException && e.name === "AbortError")) error = e;
			return;
		} finally {
			loading = false;
		}
	};
	const runFetcher = debounceTime ? debounce$1(baseFetcher, debounceTime) : throttleTime ? throttle(baseFetcher, throttleTime) : baseFetcher;
	const sources = Array.isArray(source) ? source : [source];
	let prevValues;
	effectFn((values, previousValues) => {
		if (once && prevValues) return;
		prevValues = values;
		runFetcher(Array.isArray(source) ? values : values[0], Array.isArray(source) ? previousValues : previousValues?.[0]);
	}, { lazy });
	return {
		get current() {
			return current;
		},
		get loading() {
			return loading;
		},
		get error() {
			return error;
		},
		mutate: (value) => {
			current = value;
		},
		refetch: (info) => {
			const values = sources.map((s) => s());
			return runFetcher(Array.isArray(source) ? values : values[0], Array.isArray(source) ? values : values[0], info ?? true);
		}
	};
}
function resource(source, fetcher, options) {
	return runResource(source, fetcher, options, (fn, options) => {
		const sources = Array.isArray(source) ? source : [source];
		const getters = () => sources.map((s) => s());
		watch(getters, (values, previousValues) => {
			fn(values, previousValues ?? []);
		}, options);
	});
}
function resourcePre(source, fetcher, options) {
	return runResource(source, fetcher, options, (fn, options) => {
		const sources = Array.isArray(source) ? source : [source];
		const getter = () => sources.map((s) => s());
		watch.pre(getter, (values, previousValues) => {
			fn(values, previousValues ?? []);
		}, options);
	});
}
resource.pre = resourcePre;
//#endregion
//#region node_modules/svelte-toolbelt/dist/utils/after-sleep.js
/**
* A utility function that executes a callback after a specified number of milliseconds.
*/
function afterSleep(ms, cb) {
	return setTimeout(cb, ms);
}
//#endregion
//#region node_modules/svelte-toolbelt/dist/utils/after-tick.js
function afterTick(fn) {
	(/* @__PURE__ */ tick()).then(fn);
}
//#endregion
//#region node_modules/svelte-toolbelt/dist/utils/dom.js
var ELEMENT_NODE = 1;
var DOCUMENT_NODE = 9;
var DOCUMENT_FRAGMENT_NODE = 11;
function isHTMLElement$1(node) {
	return isObject$2(node) && node.nodeType === ELEMENT_NODE && typeof node.nodeName === "string";
}
function isDocument(node) {
	return isObject$2(node) && node.nodeType === DOCUMENT_NODE;
}
function isWindow(node) {
	return isObject$2(node) && node.constructor?.name === "VisualViewport";
}
function isNode(node) {
	return isObject$2(node) && node.nodeType !== void 0;
}
function isShadowRoot(node) {
	return isNode(node) && node.nodeType === DOCUMENT_FRAGMENT_NODE && "host" in node;
}
function contains(parent, child) {
	if (!parent || !child) return false;
	if (!isHTMLElement$1(parent) || !isHTMLElement$1(child)) return false;
	const rootNode = child.getRootNode?.();
	if (parent === child) return true;
	if (parent.contains(child)) return true;
	if (rootNode && isShadowRoot(rootNode)) {
		let next = child;
		while (next) {
			if (parent === next) return true;
			next = next.parentNode || next.host;
		}
	}
	return false;
}
function getDocument(node) {
	if (isDocument(node)) return node;
	if (isWindow(node)) return node.document;
	return node?.ownerDocument ?? document;
}
function getWindow(node) {
	if (isShadowRoot(node)) return getWindow(node.host);
	if (isDocument(node)) return node.defaultView ?? window;
	if (isHTMLElement$1(node)) return node.ownerDocument?.defaultView ?? window;
	return window;
}
function getActiveElement(rootNode) {
	let activeElement = rootNode.activeElement;
	while (activeElement?.shadowRoot) {
		const el = activeElement.shadowRoot.activeElement;
		if (el === activeElement) break;
		else activeElement = el;
	}
	return activeElement;
}
//#endregion
//#region node_modules/svelte-toolbelt/dist/utils/dom-context.svelte.js
var DOMContext = class {
	element;
	#root = derived(() => {
		if (!this.element.current) return document;
		return this.element.current.getRootNode() ?? document;
	});
	get root() {
		return this.#root();
	}
	set root($$value) {
		return this.#root($$value);
	}
	constructor(element) {
		if (typeof element === "function") this.element = boxWith(element);
		else this.element = element;
	}
	getDocument = () => {
		return getDocument(this.root);
	};
	getWindow = () => {
		return this.getDocument().defaultView ?? window;
	};
	getActiveElement = () => {
		return getActiveElement(this.root);
	};
	isActiveElement = (node) => {
		return node === this.getActiveElement();
	};
	getElementById(id) {
		return this.root.getElementById(id);
	}
	querySelector = (selector) => {
		if (!this.root) return null;
		return this.root.querySelector(selector);
	};
	querySelectorAll = (selector) => {
		if (!this.root) return [];
		return this.root.querySelectorAll(selector);
	};
	setTimeout = (callback, delay) => {
		return this.getWindow().setTimeout(callback, delay);
	};
	clearTimeout = (timeoutId) => {
		return this.getWindow().clearTimeout(timeoutId);
	};
};
if (typeof HTMLElement === "function");
//#endregion
//#region node_modules/svelte/src/attachments/index.js
/**
* Creates an object key that will be recognised as an attachment when the object is spread onto an element,
* as a programmatic alternative to using `{@attach ...}`. This can be useful for library authors, though
* is generally not needed when building an app.
*
* ```svelte
* <script>
* 	import { createAttachmentKey } from 'svelte/attachments';
*
* 	const props = {
* 		class: 'cool',
* 		onclick: () => alert('clicked'),
* 		[createAttachmentKey()]: (node) => {
* 			node.textContent = 'attached!';
* 		}
* 	};
* <\/script>
*
* <button {...props}>click me</button>
* ```
* @since 5.29
*/
function createAttachmentKey() {
	return Symbol(ATTACHMENT_KEY);
}
//#endregion
//#region node_modules/svelte-toolbelt/dist/utils/attach-ref.js
/**
* Creates a Svelte Attachment that attaches a DOM element to a ref.
* The ref can be either a WritableBox or a callback function.
*
* @param ref - Either a WritableBox to store the element in, or a callback function that receives the element
* @param onChange - Optional callback that fires when the ref changes
* @returns An object with a spreadable attachment key that should be spread onto the element
*
* @example
* // Using with WritableBox
* const ref = box<HTMLDivElement | null>(null);
* <div {...attachRef(ref)}>Content</div>
*
* @example
* // Using with callback
* <div {...attachRef((node) => myNode = node)}>Content</div>
*
* @example
* // Using with onChange
* <div {...attachRef(ref, (node) => console.log(node))}>Content</div>
*/
function attachRef(ref, onChange) {
	return { [createAttachmentKey()]: (node) => {
		if (isBox(ref)) {
			ref.current = node;
			run(() => onChange?.(node));
			return () => {
				if ("isConnected" in node && node.isConnected) return;
				ref.current = null;
				onChange?.(null);
			};
		}
		ref(node);
		run(() => onChange?.(node));
		return () => {
			if ("isConnected" in node && node.isConnected) return;
			ref(null);
			onChange?.(null);
		};
	} };
}
//#endregion
//#region node_modules/bits-ui/dist/internal/attrs.js
function boolToStr(condition) {
	return condition ? "true" : "false";
}
function boolToStrTrueOrUndef(condition) {
	return condition ? "true" : void 0;
}
function boolToEmptyStrOrUndef(condition) {
	return condition ? "" : void 0;
}
function boolToTrueOrUndef(condition) {
	return condition ? true : void 0;
}
function getDataOpenClosed(condition) {
	return condition ? "open" : "closed";
}
function getDataTransitionAttrs(state) {
	if (state === "starting") return { "data-starting-style": "" };
	if (state === "ending") return { "data-ending-style": "" };
	return {};
}
function getAriaChecked(checked, indeterminate) {
	if (indeterminate) return "mixed";
	return checked ? "true" : "false";
}
var BitsAttrs = class {
	#variant;
	#prefix;
	attrs;
	constructor(config) {
		this.#variant = config.getVariant ? config.getVariant() : null;
		this.#prefix = this.#variant ? `data-${this.#variant}-` : `data-${config.component}-`;
		this.getAttr = this.getAttr.bind(this);
		this.selector = this.selector.bind(this);
		this.attrs = Object.fromEntries(config.parts.map((part) => [part, this.getAttr(part)]));
	}
	getAttr(part, variantOverride) {
		if (variantOverride) return `data-${variantOverride}-${part}`;
		return `${this.#prefix}${part}`;
	}
	selector(part, variantOverride) {
		return `[${this.getAttr(part, variantOverride)}]`;
	}
};
function createBitsAttrs(config) {
	const bitsAttrs = new BitsAttrs(config);
	return {
		...bitsAttrs.attrs,
		selector: bitsAttrs.selector,
		getAttr: bitsAttrs.getAttr
	};
}
var ARROW_DOWN = "ArrowDown";
var ARROW_LEFT = "ArrowLeft";
var ARROW_RIGHT = "ArrowRight";
var ARROW_UP = "ArrowUp";
var ENTER = "Enter";
var HOME = "Home";
var PAGE_DOWN = "PageDown";
var PAGE_UP = "PageUp";
//#endregion
//#region node_modules/bits-ui/dist/internal/locale.js
/**
* Detects the text direction in the element.
* @returns {Direction} The text direction ('ltr' for left-to-right or 'rtl' for right-to-left).
*/
function getElemDirection(elem) {
	return window.getComputedStyle(elem).getPropertyValue("direction");
}
//#endregion
//#region node_modules/bits-ui/dist/internal/get-directional-keys.js
var FIRST_KEYS$2 = [
	ARROW_DOWN,
	PAGE_UP,
	HOME
];
var LAST_KEYS$2 = [
	ARROW_UP,
	PAGE_DOWN,
	"End"
];
[...FIRST_KEYS$2, ...LAST_KEYS$2];
/**
* A utility function that returns the next key based on the direction and orientation.
*/
function getNextKey(dir = "ltr", orientation = "horizontal") {
	return {
		horizontal: dir === "rtl" ? ARROW_LEFT : ARROW_RIGHT,
		vertical: ARROW_DOWN
	}[orientation];
}
/**
* A utility function that returns the previous key based on the direction and orientation.
*/
function getPrevKey(dir = "ltr", orientation = "horizontal") {
	return {
		horizontal: dir === "rtl" ? ARROW_RIGHT : ARROW_LEFT,
		vertical: ARROW_UP
	}[orientation];
}
/**
* A utility function that returns the next and previous keys based on the direction
* and orientation.
*/
function getDirectionalKeys(dir = "ltr", orientation = "horizontal") {
	if (!["ltr", "rtl"].includes(dir)) dir = "ltr";
	if (!["horizontal", "vertical"].includes(orientation)) orientation = "horizontal";
	return {
		nextKey: getNextKey(dir, orientation),
		prevKey: getPrevKey(dir, orientation)
	};
}
//#endregion
//#region node_modules/bits-ui/dist/internal/is.js
var isBrowser = typeof document !== "undefined";
var isIOS = getIsIOS();
function getIsIOS() {
	return isBrowser && window?.navigator?.userAgent && (/iP(ad|hone|od)/.test(window.navigator.userAgent) || window?.navigator?.maxTouchPoints > 2 && /iPad|Macintosh/.test(window?.navigator.userAgent));
}
function isHTMLElement(element) {
	return element instanceof HTMLElement;
}
function isElement(element) {
	return element instanceof Element;
}
function isElementOrSVGElement(element) {
	return element instanceof Element || element instanceof SVGElement;
}
function isTouch(e) {
	return e.pointerType === "touch";
}
function isFocusVisible(element) {
	return element.matches(":focus-visible");
}
function isNotNull(value) {
	return value !== null;
}
/**
* Determines if the provided object is a valid `HTMLInputElement` with
* a `select` method available.
*/
function isSelectableInput(element) {
	return element instanceof HTMLInputElement && "select" in element;
}
//#endregion
//#region node_modules/bits-ui/dist/internal/roving-focus-group.js
var RovingFocusGroup = class {
	#opts;
	#currentTabStopId = box(null);
	constructor(opts) {
		this.#opts = opts;
	}
	getCandidateNodes() {
		return [];
	}
	focusFirstCandidate() {
		const items = this.getCandidateNodes();
		if (!items.length) return;
		items[0]?.focus();
	}
	handleKeydown(node, e, both = false) {
		const rootNode = this.#opts.rootNode.current;
		if (!rootNode || !node) return;
		const items = this.getCandidateNodes();
		if (!items.length) return;
		const currentIndex = items.indexOf(node);
		const { nextKey, prevKey } = getDirectionalKeys(getElemDirection(rootNode), this.#opts.orientation.current);
		const loop = this.#opts.loop.current;
		const keyToIndex = {
			[nextKey]: currentIndex + 1,
			[prevKey]: currentIndex - 1,
			[HOME]: 0,
			["End"]: items.length - 1
		};
		if (both) {
			const altNextKey = nextKey === "ArrowDown" ? ARROW_RIGHT : ARROW_DOWN;
			const altPrevKey = prevKey === "ArrowUp" ? ARROW_LEFT : ARROW_UP;
			keyToIndex[altNextKey] = currentIndex + 1;
			keyToIndex[altPrevKey] = currentIndex - 1;
		}
		let itemIndex = keyToIndex[e.key];
		if (itemIndex === void 0) return;
		e.preventDefault();
		if (itemIndex < 0 && loop) itemIndex = items.length - 1;
		else if (itemIndex === items.length && loop) itemIndex = 0;
		const itemToFocus = items[itemIndex];
		if (!itemToFocus) return;
		itemToFocus.focus();
		this.#currentTabStopId.current = itemToFocus.id;
		this.#opts.onCandidateFocus?.(itemToFocus);
		return itemToFocus;
	}
	getTabIndex(node) {
		const items = this.getCandidateNodes();
		const anyActive = this.#currentTabStopId.current !== null;
		if (node && !anyActive && items[0] === node) {
			this.#currentTabStopId.current = node.id;
			return 0;
		} else if (node?.id === this.#currentTabStopId.current) return 0;
		return -1;
	}
	setCurrentTabStopId(id) {
		this.#currentTabStopId.current = id;
	}
	focusCurrentTabStop() {
		const currentTabStopId = this.#currentTabStopId.current;
		if (!currentTabStopId) return;
		const currentTabStop = this.#opts.rootNode.current?.querySelector(`#${currentTabStopId}`);
		if (!currentTabStop || !isHTMLElement(currentTabStop)) return;
		currentTabStop.focus();
	}
};
//#endregion
//#region node_modules/bits-ui/dist/internal/animations-complete.js
var AnimationsComplete = class {
	#opts;
	#currentFrame = null;
	#observer = null;
	#runId = 0;
	constructor(opts) {
		this.#opts = opts;
	}
	#cleanup() {
		if (this.#currentFrame !== null) {
			window.cancelAnimationFrame(this.#currentFrame);
			this.#currentFrame = null;
		}
		this.#observer?.disconnect();
		this.#observer = null;
		this.#runId++;
	}
	run(fn) {
		this.#cleanup();
		const node = this.#opts.ref.current;
		if (!node) return;
		if (typeof node.getAnimations !== "function") {
			this.#executeCallback(fn);
			return;
		}
		const runId = this.#runId;
		const executeIfCurrent = () => {
			if (runId !== this.#runId) return;
			this.#executeCallback(fn);
		};
		const waitForAnimations = () => {
			if (runId !== this.#runId) return;
			const animations = node.getAnimations();
			if (animations.length === 0) {
				executeIfCurrent();
				return;
			}
			Promise.all(animations.map((animation) => animation.finished)).then(() => {
				executeIfCurrent();
			}).catch(() => {
				if (runId !== this.#runId) return;
				if (node.getAnimations().some((animation) => animation.pending || animation.playState !== "finished")) {
					waitForAnimations();
					return;
				}
				executeIfCurrent();
			});
		};
		const requestWaitForAnimations = () => {
			this.#currentFrame = window.requestAnimationFrame(() => {
				this.#currentFrame = null;
				waitForAnimations();
			});
		};
		if (!this.#opts.afterTick.current) {
			requestWaitForAnimations();
			return;
		}
		this.#currentFrame = window.requestAnimationFrame(() => {
			this.#currentFrame = null;
			const startingStyleAttr = "data-starting-style";
			if (!node.hasAttribute(startingStyleAttr)) {
				requestWaitForAnimations();
				return;
			}
			this.#observer = new MutationObserver(() => {
				if (runId !== this.#runId) return;
				if (node.hasAttribute(startingStyleAttr)) return;
				this.#observer?.disconnect();
				this.#observer = null;
				requestWaitForAnimations();
			});
			this.#observer.observe(node, {
				attributes: true,
				attributeFilter: [startingStyleAttr]
			});
		});
	}
	#executeCallback(fn) {
		const execute = () => {
			fn();
		};
		if (this.#opts.afterTick) afterTick(execute);
		else execute();
	}
};
//#endregion
//#region node_modules/bits-ui/dist/internal/presence-manager.svelte.js
var PresenceManager = class {
	#opts;
	#enabled;
	#afterAnimations;
	#shouldRender = false;
	#transitionStatus = void 0;
	#hasMounted = false;
	#transitionFrame = null;
	constructor(opts) {
		this.#opts = opts;
		this.#shouldRender = opts.open.current;
		this.#enabled = opts.enabled ?? true;
		this.#afterAnimations = new AnimationsComplete({
			ref: this.#opts.ref,
			afterTick: this.#opts.open
		});
		watch(() => this.#opts.open.current, (isOpen) => {
			if (!this.#hasMounted) {
				this.#hasMounted = true;
				return;
			}
			this.#clearTransitionFrame();
			if (!isOpen && this.#opts.shouldSkipExitAnimation?.()) {
				this.#shouldRender = false;
				this.#transitionStatus = void 0;
				this.#opts.onComplete?.();
				return;
			}
			if (isOpen) this.#shouldRender = true;
			this.#transitionStatus = isOpen ? "starting" : "ending";
			if (isOpen) this.#transitionFrame = window.requestAnimationFrame(() => {
				this.#transitionFrame = null;
				if (this.#opts.open.current) this.#transitionStatus = void 0;
			});
			if (!this.#enabled) {
				if (!isOpen) this.#shouldRender = false;
				this.#transitionStatus = void 0;
				this.#opts.onComplete?.();
				return;
			}
			this.#afterAnimations.run(() => {
				if (isOpen === this.#opts.open.current) {
					if (!this.#opts.open.current) this.#shouldRender = false;
					this.#transitionStatus = void 0;
					this.#opts.onComplete?.();
				}
			});
		});
	}
	get shouldRender() {
		return this.#shouldRender;
	}
	get transitionStatus() {
		return this.#transitionStatus;
	}
	#clearTransitionFrame() {
		if (this.#transitionFrame === null) return;
		window.cancelAnimationFrame(this.#transitionFrame);
		this.#transitionFrame = null;
	}
};
//#endregion
//#region node_modules/bits-ui/dist/internal/noop.js
/**
* A no operation function (does nothing)
*/
function noop() {}
//#endregion
//#region node_modules/bits-ui/dist/internal/create-id.js
function createId(prefixOrUid, uid) {
	if (uid === void 0) return `bits-${prefixOrUid}`;
	return `bits-${prefixOrUid}-${uid}`;
}
//#endregion
//#region node_modules/bits-ui/dist/bits/dialog/dialog.svelte.js
var dialogAttrs = createBitsAttrs({
	component: "dialog",
	parts: [
		"content",
		"trigger",
		"overlay",
		"title",
		"description",
		"close",
		"cancel",
		"action"
	]
});
var DialogRootContext = new Context("Dialog.Root | AlertDialog.Root");
var DialogRootState = class DialogRootState {
	static create(opts) {
		const parent = DialogRootContext.getOr(null);
		return DialogRootContext.set(new DialogRootState(opts, parent));
	}
	opts;
	triggerNode = null;
	contentNode = null;
	overlayNode = null;
	descriptionNode = null;
	contentId = void 0;
	titleId = void 0;
	triggerId = void 0;
	descriptionId = void 0;
	cancelNode = null;
	nestedOpenCount = 0;
	depth;
	parent;
	contentPresence;
	overlayPresence;
	constructor(opts, parent) {
		this.opts = opts;
		this.parent = parent;
		this.depth = parent ? parent.depth + 1 : 0;
		this.handleOpen = this.handleOpen.bind(this);
		this.handleClose = this.handleClose.bind(this);
		this.contentPresence = new PresenceManager({
			ref: boxWith(() => this.contentNode),
			open: this.opts.open,
			enabled: true,
			onComplete: () => {
				this.opts.onOpenChangeComplete.current(this.opts.open.current);
			}
		});
		this.overlayPresence = new PresenceManager({
			ref: boxWith(() => this.overlayNode),
			open: this.opts.open,
			enabled: true
		});
		watch(() => this.opts.open.current, (isOpen) => {
			if (!this.parent) return;
			if (isOpen) this.parent.incrementNested();
			else this.parent.decrementNested();
		}, { lazy: true });
	}
	handleOpen() {
		if (this.opts.open.current) return;
		this.opts.open.current = true;
	}
	handleClose() {
		if (!this.opts.open.current) return;
		this.opts.open.current = false;
	}
	getBitsAttr = (part) => {
		return dialogAttrs.getAttr(part, this.opts.variant.current);
	};
	incrementNested() {
		this.nestedOpenCount++;
		this.parent?.incrementNested();
	}
	decrementNested() {
		if (this.nestedOpenCount === 0) return;
		this.nestedOpenCount--;
		this.parent?.decrementNested();
	}
	#sharedProps = derived(() => ({ "data-state": getDataOpenClosed(this.opts.open.current) }));
	get sharedProps() {
		return this.#sharedProps();
	}
	set sharedProps($$value) {
		return this.#sharedProps($$value);
	}
};
var DialogCloseState = class DialogCloseState {
	static create(opts) {
		return new DialogCloseState(opts, DialogRootContext.get());
	}
	opts;
	root;
	attachment;
	constructor(opts, root) {
		this.opts = opts;
		this.root = root;
		this.attachment = attachRef(this.opts.ref);
		this.onclick = this.onclick.bind(this);
		this.onkeydown = this.onkeydown.bind(this);
	}
	onclick(e) {
		if (this.opts.disabled.current) return;
		if (e.button > 0) return;
		this.root.handleClose();
	}
	onkeydown(e) {
		if (this.opts.disabled.current) return;
		if (e.key === " " || e.key === "Enter") {
			e.preventDefault();
			this.root.handleClose();
		}
	}
	#props = derived(() => ({
		id: this.opts.id.current,
		[this.root.getBitsAttr(this.opts.variant.current)]: "",
		onclick: this.onclick,
		onkeydown: this.onkeydown,
		disabled: this.opts.disabled.current ? true : void 0,
		tabindex: 0,
		...this.root.sharedProps,
		...this.attachment
	}));
	get props() {
		return this.#props();
	}
	set props($$value) {
		return this.#props($$value);
	}
};
var DialogTitleState = class DialogTitleState {
	static create(opts) {
		return new DialogTitleState(opts, DialogRootContext.get());
	}
	opts;
	root;
	attachment;
	constructor(opts, root) {
		this.opts = opts;
		this.root = root;
		this.root.titleId = this.opts.id.current;
		this.attachment = attachRef(this.opts.ref);
		watch.pre(() => this.opts.id.current, (id) => {
			this.root.titleId = id;
		});
	}
	#props = derived(() => ({
		id: this.opts.id.current,
		role: "heading",
		"aria-level": this.opts.level.current,
		[this.root.getBitsAttr("title")]: "",
		...this.root.sharedProps,
		...this.attachment
	}));
	get props() {
		return this.#props();
	}
	set props($$value) {
		return this.#props($$value);
	}
};
var DialogDescriptionState = class DialogDescriptionState {
	static create(opts) {
		return new DialogDescriptionState(opts, DialogRootContext.get());
	}
	opts;
	root;
	attachment;
	constructor(opts, root) {
		this.opts = opts;
		this.root = root;
		this.root.descriptionId = this.opts.id.current;
		this.attachment = attachRef(this.opts.ref, (v) => {
			this.root.descriptionNode = v;
		});
		watch.pre(() => this.opts.id.current, (id) => {
			this.root.descriptionId = id;
		});
	}
	#props = derived(() => ({
		id: this.opts.id.current,
		[this.root.getBitsAttr("description")]: "",
		...this.root.sharedProps,
		...this.attachment
	}));
	get props() {
		return this.#props();
	}
	set props($$value) {
		return this.#props($$value);
	}
};
var DialogContentState = class DialogContentState {
	static create(opts) {
		return new DialogContentState(opts, DialogRootContext.get());
	}
	opts;
	root;
	attachment;
	constructor(opts, root) {
		this.opts = opts;
		this.root = root;
		this.attachment = attachRef(this.opts.ref, (v) => {
			this.root.contentNode = v;
			this.root.contentId = v?.id;
		});
	}
	#snippetProps = derived(() => ({ open: this.root.opts.open.current }));
	get snippetProps() {
		return this.#snippetProps();
	}
	set snippetProps($$value) {
		return this.#snippetProps($$value);
	}
	#props = derived(() => ({
		id: this.opts.id.current,
		role: this.root.opts.variant.current === "alert-dialog" ? "alertdialog" : "dialog",
		"aria-modal": "true",
		"aria-describedby": this.root.descriptionId,
		"aria-labelledby": this.root.titleId,
		[this.root.getBitsAttr("content")]: "",
		style: {
			pointerEvents: "auto",
			outline: this.root.opts.variant.current === "alert-dialog" ? "none" : void 0,
			"--bits-dialog-depth": this.root.depth,
			"--bits-dialog-nested-count": this.root.nestedOpenCount,
			contain: "layout style"
		},
		tabindex: this.root.opts.variant.current === "alert-dialog" ? -1 : void 0,
		"data-nested-open": boolToEmptyStrOrUndef(this.root.nestedOpenCount > 0),
		"data-nested": boolToEmptyStrOrUndef(this.root.parent !== null),
		...getDataTransitionAttrs(this.root.contentPresence.transitionStatus),
		...this.root.sharedProps,
		...this.attachment
	}));
	get props() {
		return this.#props();
	}
	set props($$value) {
		return this.#props($$value);
	}
	get shouldRender() {
		return this.root.contentPresence.shouldRender;
	}
};
var DialogOverlayState = class DialogOverlayState {
	static create(opts) {
		return new DialogOverlayState(opts, DialogRootContext.get());
	}
	opts;
	root;
	attachment;
	constructor(opts, root) {
		this.opts = opts;
		this.root = root;
		this.attachment = attachRef(this.opts.ref, (v) => this.root.overlayNode = v);
	}
	#snippetProps = derived(() => ({ open: this.root.opts.open.current }));
	get snippetProps() {
		return this.#snippetProps();
	}
	set snippetProps($$value) {
		return this.#snippetProps($$value);
	}
	#props = derived(() => ({
		id: this.opts.id.current,
		[this.root.getBitsAttr("overlay")]: "",
		style: {
			pointerEvents: "auto",
			"--bits-dialog-depth": this.root.depth,
			"--bits-dialog-nested-count": this.root.nestedOpenCount
		},
		"data-nested-open": boolToEmptyStrOrUndef(this.root.nestedOpenCount > 0),
		"data-nested": boolToEmptyStrOrUndef(this.root.parent !== null),
		...getDataTransitionAttrs(this.root.overlayPresence.transitionStatus),
		...this.root.sharedProps,
		...this.attachment
	}));
	get props() {
		return this.#props();
	}
	set props($$value) {
		return this.#props($$value);
	}
	get shouldRender() {
		return this.root.overlayPresence.shouldRender;
	}
};
//#endregion
//#region node_modules/bits-ui/dist/bits/dialog/components/dialog-title.svelte
function Dialog_title$1($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		const uid = props_id($$renderer);
		let { id = createId(uid), ref = null, child, children, level = 2, $$slots, $$events, ...restProps } = $$props;
		const titleState = DialogTitleState.create({
			id: boxWith(() => id),
			level: boxWith(() => level),
			ref: boxWith(() => ref, (v) => ref = v)
		});
		const mergedProps = derived(() => mergeProps(restProps, titleState.props));
		if (child) {
			$$renderer.push("<!--[0-->");
			child($$renderer, { props: mergedProps() });
			$$renderer.push(`<!---->`);
		} else {
			$$renderer.push("<!--[-1-->");
			$$renderer.push(`<div${attributes({ ...mergedProps() })}>`);
			children?.($$renderer);
			$$renderer.push(`<!----></div>`);
		}
		$$renderer.push(`<!--]-->`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region node_modules/bits-ui/dist/bits/utilities/portal/portal-consumer.svelte
function Portal_consumer($$renderer, $$props) {
	const { children } = $$props;
	$$renderer.push(`<!---->`);
	children?.($$renderer);
	$$renderer.push(`<!---->`);
	$$renderer.push(`<!---->`);
}
//#endregion
//#region node_modules/bits-ui/dist/bits/utilities/config/bits-config.js
var BitsConfigContext = new Context("BitsConfig");
/**
* Gets the current Bits UI configuration state from the context.
*
* Returns a default configuration (where all values are `undefined`) if no configuration is found.
*/
function getBitsConfig() {
	const fallback = new BitsConfigState(null, {});
	return BitsConfigContext.getOr(fallback).opts;
}
/**
* Configuration state that inherits from parent configurations.
*
* @example
* Config resolution:
* ```
* Level 1: { defaultPortalTo: "#some-element", theme: "dark" }
* Level 2: { spacing: "large" } // inherits defaultPortalTo="#some-element", theme="dark"
* Level 3: { theme: "light" }   // inherits defaultPortalTo="#some-element", spacing="large", overrides theme="light"
* ```
*/
var BitsConfigState = class {
	opts;
	constructor(parent, opts) {
		const resolveConfigOption = createConfigResolver(parent, opts);
		this.opts = {
			defaultPortalTo: resolveConfigOption((config) => config.defaultPortalTo),
			defaultLocale: resolveConfigOption((config) => config.defaultLocale)
		};
	}
};
/**
* Returns a config resolver that resolves a given config option's value.
*
* The resolver creates reactive boxes that resolve config option values using this priority:
* 1. Current level's value (if defined)
* 2. Parent level's value (if defined and current is undefined)
* 3. `undefined` (if no value is found in either parent or child)
*
* @param parent - Parent configuration state (null if this is root level)
* @param currentOpts - Current level's configuration options
*
* @example
* ```typescript
* // Given this hierarchy:
* // Root: { defaultPortalTo: "#some-element" }
* // Child: { someOtherProp: "value" } // no defaultPortalTo specified
*
* const resolveConfigOption = createConfigResolver(parent, opts);
* const portalTo = resolveConfigOption(config => config.defaultPortalTo);
*
* // portalTo.current === "#some-element" (inherited from parent)
* // even when child didn't specify `defaultPortalTo`
* ```
*/
function createConfigResolver(parent, currentOpts) {
	return (getter) => {
		return boxWith(() => {
			const value = getter(currentOpts)?.current;
			if (value !== void 0) return value;
			if (parent === null) return void 0;
			return getter(parent.opts)?.current;
		});
	};
}
//#endregion
//#region node_modules/bits-ui/dist/bits/utilities/config/prop-resolvers.js
/**
* Creates a generic prop resolver that follows a standard priority chain:
* 1. The getter's prop value (if defined)
* 2. The config default value (if no getter prop value is defined)
* 3. The fallback value (if no config value found)
*/
function createPropResolver(configOption, fallback) {
	return (getProp) => {
		const config = getBitsConfig();
		return boxWith(() => {
			const propValue = getProp();
			if (propValue !== void 0) return propValue;
			const option = configOption(config).current;
			if (option !== void 0) return option;
			return fallback;
		});
	};
}
/**
* Resolves a portal's `to` value using the prop, the config default, or a fallback.
*
* Default value: `"body"`
*/
var resolvePortalToProp = createPropResolver((config) => config.defaultPortalTo, "body");
//#endregion
//#region node_modules/bits-ui/dist/bits/utilities/portal/portal.svelte
function Portal($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { to: toProp, children, disabled } = $$props;
		const to = resolvePortalToProp(() => toProp);
		const context = getAllContexts();
		let target = derived(getTarget);
		function getTarget() {
			if (!isBrowser || disabled) return null;
			let localTarget = null;
			if (typeof to.current === "string") localTarget = document.querySelector(to.current);
			else localTarget = to.current;
			return localTarget;
		}
		let instance;
		function unmountInstance() {
			if (instance) {
				unmount(instance);
				instance = null;
			}
		}
		watch([() => target(), () => disabled], ([target, disabled]) => {
			if (!target || disabled) {
				unmountInstance();
				return;
			}
			instance = mount(Portal_consumer, {
				target,
				props: { children },
				context
			});
			return () => {
				unmountInstance();
			};
		});
		if (disabled) {
			$$renderer.push("<!--[0-->");
			children?.($$renderer);
			$$renderer.push(`<!---->`);
		} else $$renderer.push("<!--[-1-->");
		$$renderer.push(`<!--]-->`);
	});
}
//#endregion
//#region node_modules/bits-ui/dist/internal/events.js
/**
* Creates a typed event dispatcher and listener pair for custom events
* @template T - The type of data that will be passed in the event detail
* @param eventName - The name of the custom event
* @param options - CustomEvent options (bubbles, cancelable, etc.)
*/
var CustomEventDispatcher = class {
	eventName;
	options;
	constructor(eventName, options = {
		bubbles: true,
		cancelable: true
	}) {
		this.eventName = eventName;
		this.options = options;
	}
	createEvent(detail) {
		return new CustomEvent(this.eventName, {
			...this.options,
			detail
		});
	}
	dispatch(element, detail) {
		const event = this.createEvent(detail);
		element.dispatchEvent(event);
		return event;
	}
	listen(element, callback, options) {
		const handler = (event) => {
			callback(event);
		};
		return on(element, this.eventName, handler, options);
	}
};
//#endregion
//#region node_modules/bits-ui/dist/internal/debounce.js
function debounce(fn, wait = 500) {
	let timeout = null;
	const debounced = (...args) => {
		if (timeout !== null) clearTimeout(timeout);
		timeout = setTimeout(() => {
			fn(...args);
		}, wait);
	};
	debounced.destroy = () => {
		if (timeout !== null) {
			clearTimeout(timeout);
			timeout = null;
		}
	};
	return debounced;
}
//#endregion
//#region node_modules/bits-ui/dist/internal/elements.js
function isOrContainsTarget(node, target) {
	return node === target || node.contains(target);
}
function getOwnerDocument(el) {
	return el?.ownerDocument ?? document;
}
//#endregion
//#region node_modules/bits-ui/dist/internal/dom.js
/**
* Determines if the click event truly occurred outside the content node.
* This was added to handle password managers and other elements that may be injected
* into the DOM but visually appear inside the content.
*/
function isClickTrulyOutside(event, contentNode) {
	const { clientX, clientY } = event;
	const rect = contentNode.getBoundingClientRect();
	return clientX < rect.left || clientX > rect.right || clientY < rect.top || clientY > rect.bottom;
}
//#endregion
//#region node_modules/bits-ui/dist/bits/menu/utils.js
var SELECTION_KEYS$1 = [ENTER, " "];
var FIRST_KEYS$1 = [
	ARROW_DOWN,
	PAGE_UP,
	HOME
];
var LAST_KEYS$1 = [
	ARROW_UP,
	PAGE_DOWN,
	"End"
];
var FIRST_LAST_KEYS$1 = [...FIRST_KEYS$1, ...LAST_KEYS$1];
var SUB_OPEN_KEYS = {
	ltr: [...SELECTION_KEYS$1, ARROW_RIGHT],
	rtl: [...SELECTION_KEYS$1, ARROW_LEFT]
};
var SUB_CLOSE_KEYS = {
	ltr: [ARROW_LEFT],
	rtl: [ARROW_RIGHT]
};
function isIndeterminate(checked) {
	return checked === "indeterminate";
}
function getCheckedState(checked) {
	return isIndeterminate(checked) ? "indeterminate" : checked ? "checked" : "unchecked";
}
function isMouseEvent(event) {
	return event.pointerType === "mouse";
}
//#endregion
//#region node_modules/bits-ui/dist/internal/focus.js
/**
* A utility function that focuses an element.
*/
function focus(element, { select = false } = {}) {
	if (!element || !element.focus) return;
	const doc = getDocument(element);
	if (doc.activeElement === element) return;
	const previouslyFocusedElement = doc.activeElement;
	element.focus({ preventScroll: true });
	if (element !== previouslyFocusedElement && isSelectableInput(element) && select) element.select();
}
/**
* Attempts to focus the first element in a list of candidates.
* Stops when focus is successful.
*/
function focusFirst(candidates, { select = false } = {}, getActiveElement) {
	const previouslyFocusedElement = getActiveElement();
	for (const candidate of candidates) {
		focus(candidate, { select });
		if (getActiveElement() !== previouslyFocusedElement) return true;
	}
}
//#endregion
//#region node_modules/bits-ui/dist/bits/utilities/is-using-keyboard/is-using-keyboard.svelte.js
var isUsingKeyboard = false;
var IsUsingKeyboard = class {
	static _refs = 0;
	static _cleanup;
	constructor() {}
	get current() {
		return isUsingKeyboard;
	}
	set current(value) {
		isUsingKeyboard = value;
	}
};
//#endregion
//#region node_modules/bits-ui/dist/internal/tabbable.js
function getTabbableOptions() {
	return {
		getShadowRoot: true,
		displayCheck: typeof ResizeObserver === "function" && ResizeObserver.toString().includes("[native code]") ? "full" : "none"
	};
}
/**
* Gets all tabbable elements in the body and finds the next/previous tabbable element
* from the `currentNode` based on the `direction` provided.
* @param currentNode - the node we want to get the next/previous tabbable from
*/
function getTabbableFrom(currentNode, direction) {
	if (!isTabbable(currentNode, getTabbableOptions())) return getTabbableFromFocusable(currentNode, direction);
	const doc = getDocument(currentNode);
	const allTabbable = tabbable(doc.body, getTabbableOptions());
	if (direction === "prev") allTabbable.reverse();
	const activeIndex = allTabbable.indexOf(currentNode);
	if (activeIndex === -1) return doc.body;
	return allTabbable.slice(activeIndex + 1)[0];
}
function getTabbableFromFocusable(currentNode, direction) {
	const doc = getDocument(currentNode);
	if (!isFocusable(currentNode, getTabbableOptions())) return doc.body;
	const allFocusable = focusable(doc.body, getTabbableOptions());
	if (direction === "prev") allFocusable.reverse();
	const activeIndex = allFocusable.indexOf(currentNode);
	if (activeIndex === -1) return doc.body;
	return allFocusable.slice(activeIndex + 1).find((node) => isTabbable(node, getTabbableOptions())) ?? doc.body;
}
//#endregion
//#region node_modules/bits-ui/dist/internal/arrays.js
/**
* Returns the array element after the given index, or undefined for out-of-bounds or empty arrays.
* @param array the array.
* @param index the index of the current element.
* @param loop loop to the beginning of the array if the next index is out of bounds?
*/
/**
* Returns the array element after the given index, or undefined for out-of-bounds or empty arrays.
* For single-element arrays, returns the element if the index is 0.
* @param array the array.
* @param index the index of the current element.
* @param loop loop to the beginning of the array if the next index is out of bounds?
*/
function next(array, index, loop = true) {
	if (array.length === 0 || index < 0 || index >= array.length) return;
	if (array.length === 1 && index === 0) return array[0];
	if (index === array.length - 1) return loop ? array[0] : void 0;
	return array[index + 1];
}
/**
* Returns the array element prior to the given index, or undefined for out-of-bounds or empty arrays.
* For single-element arrays, returns the element if the index is 0.
* @param array the array.
* @param index the index of the current element.
* @param loop loop to the end of the array if the previous index is out of bounds?
*/
function prev(array, index, loop = true) {
	if (array.length === 0 || index < 0 || index >= array.length) return;
	if (array.length === 1 && index === 0) return array[0];
	if (index === 0) return loop ? array[array.length - 1] : void 0;
	return array[index - 1];
}
/**
* Returns the element some number after the given index. If the target index is out of bounds:
*   - If looping is disabled, the first or last element will be returned.
*   - If looping is enabled, it will wrap around the array.
* Returns undefined for empty arrays or out-of-bounds initial indices.
* @param array the array.
* @param index the index of the current element.
* @param increment the number of elements to move forward (can be negative).
* @param loop loop around the array if the target index is out of bounds?
*/
function forward(array, index, increment, loop = true) {
	if (array.length === 0 || index < 0 || index >= array.length) return;
	let targetIndex = index + increment;
	if (loop) targetIndex = (targetIndex % array.length + array.length) % array.length;
	else targetIndex = Math.max(0, Math.min(targetIndex, array.length - 1));
	return array[targetIndex];
}
/**
* Returns the element some number before the given index. If the target index is out of bounds:
*   - If looping is disabled, the first or last element will be returned.
*   - If looping is enabled, it will wrap around the array.
* Returns undefined for empty arrays or out-of-bounds initial indices.
* @param array the array.
* @param index the index of the current element.
* @param decrement the number of elements to move backward (can be negative).
* @param loop loop around the array if the target index is out of bounds?
*/
function backward(array, index, decrement, loop = true) {
	if (array.length === 0 || index < 0 || index >= array.length) return;
	let targetIndex = index - decrement;
	if (loop) targetIndex = (targetIndex % array.length + array.length) % array.length;
	else targetIndex = Math.max(0, Math.min(targetIndex, array.length - 1));
	return array[targetIndex];
}
/**
* Finds the next matching item from a list of values based on a search string.
*
* This function handles several special cases in typeahead behavior:
*
* 1. Space handling: When a search string ends with a space, it handles it specially:
*    - If there's only one match for the text before the space, it ignores the space
*    - If there are multiple matches and the current match already starts with the search prefix
*      followed by a space, it keeps the current match (doesn't change selection on space)
*    - Only after typing characters beyond the space will it move to a more specific match
*
* 2. Repeated character handling: If a search consists of repeated characters (e.g., "aaa"),
*    it treats it as a single character for matching purposes
*
* 3. Cycling behavior: The function wraps around the values array starting from the current match
*    to find the next appropriate match, creating a cycling selection behavior
*
* @param values - Array of string values to search through (e.g., the text content of menu items)
* @param search - The current search string typed by the user
* @param currentMatch - The currently selected/matched item, if any
* @returns The next matching value that should be selected, or undefined if no match is found
*/
function getNextMatch(values, search, currentMatch) {
	const lowerSearch = search.toLowerCase();
	if (lowerSearch.endsWith(" ")) {
		const searchWithoutSpace = lowerSearch.slice(0, -1);
		/**
		* If there's only one match for the prefix without space, we don't
		* watch to match with space.
		*/
		if (values.filter((value) => value.toLowerCase().startsWith(searchWithoutSpace)).length <= 1) return getNextMatch(values, searchWithoutSpace, currentMatch);
		const currentMatchLowercase = currentMatch?.toLowerCase();
		/**
		* If the current match already starts with the search prefix and has a space afterward,
		* and the user has only typed up to that space, keep the current match until they
		* disambiguate.
		*/
		if (currentMatchLowercase && currentMatchLowercase.startsWith(searchWithoutSpace) && currentMatchLowercase.charAt(searchWithoutSpace.length) === " " && search.trim() === searchWithoutSpace) return currentMatch;
		/**
		* With multiple matches, find items that match the full search string with space
		*/
		const spacedMatches = values.filter((value) => value.toLowerCase().startsWith(lowerSearch));
		/**
		* If we found matches with the space, use the first one that's not the current match
		*/
		if (spacedMatches.length > 0) {
			const currentMatchIndex = currentMatch ? values.indexOf(currentMatch) : -1;
			return wrapArray(spacedMatches, Math.max(currentMatchIndex, 0)).find((match) => match !== currentMatch) || currentMatch;
		}
	}
	const normalizedSearch = search.length > 1 && Array.from(search).every((char) => char === search[0]) ? search[0] : search;
	const normalizedLowerSearch = normalizedSearch.toLowerCase();
	const currentMatchIndex = currentMatch ? values.indexOf(currentMatch) : -1;
	let wrappedValues = wrapArray(values, Math.max(currentMatchIndex, 0));
	if (normalizedSearch.length === 1) wrappedValues = wrappedValues.filter((v) => v !== currentMatch);
	const nextMatch = wrappedValues.find((value) => value?.toLowerCase().startsWith(normalizedLowerSearch));
	return nextMatch !== currentMatch ? nextMatch : void 0;
}
/**
* Wraps an array around itself at a given start index
* Example: `wrapArray(['a', 'b', 'c', 'd'], 2) === ['c', 'd', 'a', 'b']`
*/
function wrapArray(array, startIndex) {
	return array.map((_, index) => array[(startIndex + index) % array.length]);
}
//#endregion
//#region node_modules/bits-ui/dist/internal/box-auto-reset.svelte.js
var defaultOptions = {
	afterMs: 1e4,
	onChange: noop
};
function boxAutoReset(defaultValue, options) {
	const { afterMs, onChange, getWindow } = {
		...defaultOptions,
		...options
	};
	let timeout = null;
	let value = defaultValue;
	function resetAfter() {
		return getWindow().setTimeout(() => {
			value = defaultValue;
			onChange?.(defaultValue);
		}, afterMs);
	}
	return boxWith(() => value, (v) => {
		value = v;
		onChange?.(v);
		if (timeout) getWindow().clearTimeout(timeout);
		timeout = resetAfter();
	});
}
//#endregion
//#region node_modules/bits-ui/dist/internal/dom-typeahead.svelte.js
var DOMTypeahead = class {
	#opts;
	#search;
	#onMatch = derived(() => {
		if (this.#opts.onMatch) return this.#opts.onMatch;
		return (node) => node.focus();
	});
	#getCurrentItem = derived(() => {
		if (this.#opts.getCurrentItem) return this.#opts.getCurrentItem;
		return this.#opts.getActiveElement;
	});
	constructor(opts) {
		this.#opts = opts;
		this.#search = boxAutoReset("", {
			afterMs: 1e3,
			getWindow: opts.getWindow
		});
		this.handleTypeaheadSearch = this.handleTypeaheadSearch.bind(this);
		this.resetTypeahead = this.resetTypeahead.bind(this);
	}
	handleTypeaheadSearch(key, candidates) {
		if (!candidates.length) return;
		this.#search.current = this.#search.current + key;
		const currentItem = this.#getCurrentItem()();
		const currentMatch = candidates.find((item) => item === currentItem)?.textContent?.trim() ?? "";
		const nextMatch = getNextMatch(candidates.map((item) => item.textContent?.trim() ?? ""), this.#search.current, currentMatch);
		const newItem = candidates.find((item) => item.textContent?.trim() === nextMatch);
		if (newItem) this.#onMatch()(newItem);
		return newItem;
	}
	resetTypeahead() {
		this.#search.current = "";
	}
	get search() {
		return this.#search.current;
	}
};
//#endregion
//#region node_modules/bits-ui/dist/bits/menu/menu.svelte.js
var CONTEXT_MENU_TRIGGER_ATTR = "data-context-menu-trigger";
var CONTEXT_MENU_CONTENT_ATTR = "data-context-menu-content";
var MenuRootContext = new Context("Menu.Root");
var MenuMenuContext = new Context("Menu.Root | Menu.Sub");
var MenuContentContext = new Context("Menu.Content");
var MenuGroupContext = new Context("Menu.Group | Menu.RadioGroup");
var MenuRadioGroupContext = new Context("Menu.RadioGroup");
new Context("Menu.CheckboxGroup");
var MenuOpenEvent = new CustomEventDispatcher("bitsmenuopen", {
	bubbles: false,
	cancelable: true
});
var menuAttrs = createBitsAttrs({
	component: "menu",
	parts: [
		"trigger",
		"content",
		"sub-trigger",
		"item",
		"group",
		"group-heading",
		"checkbox-group",
		"checkbox-item",
		"radio-group",
		"radio-item",
		"separator",
		"sub-content",
		"arrow"
	]
});
var MenuSubmenuIntent = class {
	#opts;
	#cleanupDocMove = null;
	#fallbackTimer = null;
	#active = false;
	#target = null;
	#apex = null;
	#pointerPoint = null;
	#launchPoint = null;
	constructor(opts) {
		this.#opts = opts;
		watch([
			opts.triggerNode,
			opts.contentNode,
			opts.enabled
		], ([triggerNode, contentNode, enabled]) => {
			this.#reset();
			if (!triggerNode || !contentNode || !enabled) return;
			const onTriggerMove = (e) => {
				if (!isMouseEvent(e)) return;
				this.#launchPoint = {
					x: e.clientX,
					y: e.clientY
				};
				if (!this.#active) this.#preview(e, "content");
			};
			const onTriggerLeave = (e) => {
				if (!isMouseEvent(e)) return;
				this.#engage(e, "content");
			};
			const onContentMove = (e) => {
				if (!isMouseEvent(e)) return;
				if (!this.#active) this.#preview(e, "trigger");
			};
			const onContentLeave = (e) => {
				if (!isMouseEvent(e)) return;
				if (isElement(e.relatedTarget)) {
					const selector = this.#opts.subContentSelector();
					const matchedSubContent = e.relatedTarget.closest(selector);
					if (matchedSubContent && matchedSubContent !== contentNode && matchedSubContent.id) {
						if (!!contentNode.querySelector(`[aria-controls="${matchedSubContent.id}"]`)) return;
					}
				}
				this.#engage(e, "trigger");
			};
			const onTriggerEnter = (e) => {
				if (!isMouseEvent(e)) return;
				this.#disengage();
			};
			const onContentEnter = (e) => {
				if (!isMouseEvent(e)) return;
				this.#disengage();
			};
			triggerNode.addEventListener("pointermove", onTriggerMove);
			triggerNode.addEventListener("pointerleave", onTriggerLeave);
			triggerNode.addEventListener("pointerenter", onTriggerEnter);
			contentNode.addEventListener("pointermove", onContentMove);
			contentNode.addEventListener("pointerleave", onContentLeave);
			contentNode.addEventListener("pointerenter", onContentEnter);
			return () => {
				triggerNode.removeEventListener("pointermove", onTriggerMove);
				triggerNode.removeEventListener("pointerleave", onTriggerLeave);
				triggerNode.removeEventListener("pointerenter", onTriggerEnter);
				contentNode.removeEventListener("pointermove", onContentMove);
				contentNode.removeEventListener("pointerleave", onContentLeave);
				contentNode.removeEventListener("pointerenter", onContentEnter);
				this.#reset();
			};
		});
	}
	#parentTargetRect() {
		const parent = this.#opts.parentContentNode();
		if (parent) return parent.getBoundingClientRect();
		return this.#opts.triggerNode()?.getBoundingClientRect() ?? null;
	}
	#computePolygons(pointerPt, target) {
		const triggerNode = this.#opts.triggerNode();
		const contentNode = this.#opts.contentNode();
		if (!triggerNode || !contentNode) return null;
		const triggerRect = triggerNode.getBoundingClientRect();
		const contentRect = contentNode.getBoundingClientRect();
		const side = getSide$1(triggerRect, contentRect);
		let apex;
		let targetRect;
		let sourceRect;
		if (target === "content") {
			apex = this.#active ? this.#apex ?? pointerPt : pointerPt;
			targetRect = contentRect;
		} else {
			apex = this.#launchPoint ?? pointerPt;
			targetRect = this.#parentTargetRect() ?? triggerRect;
			sourceRect = contentRect;
		}
		this.#apex = apex;
		return {
			corridor: getCorridorPolygon(triggerRect, contentRect, side),
			intent: getIntentPolygon(apex, targetRect, side, target, sourceRect),
			targetRect,
			side
		};
	}
	#isInSafeZone(pt, corridor, intent) {
		return isPointInPolygon$1(pt, corridor) || isPointInPolygon$1(pt, intent);
	}
	#preview(e, target) {
		const pt = {
			x: e.clientX,
			y: e.clientY
		};
		if (!this.#computePolygons(pt, target)) return;
		this.#target = target;
		this.#pointerPoint = pt;
	}
	#engage(e, target) {
		if (!this.#opts.enabled()) return;
		const triggerNode = this.#opts.triggerNode();
		const contentNode = this.#opts.contentNode();
		if (!triggerNode || !contentNode) return;
		const related = e.relatedTarget;
		if (isElement(related)) {
			if (target === "content" && contentNode.contains(related)) return;
			if (target === "trigger" && triggerNode.contains(related)) return;
		}
		const pt = {
			x: e.clientX,
			y: e.clientY
		};
		const geo = this.#computePolygons(pt, target);
		if (!geo) return;
		if (!isInsideRect$1(pt, geo.targetRect) && !this.#isInSafeZone(pt, geo.corridor, geo.intent)) {
			this.#clearVisuals();
			return;
		}
		this.#active = true;
		this.#target = target;
		this.#pointerPoint = pt;
		this.#opts.setIsPointerInTransit(true);
		this.#attachDocMove();
		this.#startFallback();
	}
	#disengageTimer = null;
	#disengage() {
		if (!this.#active) return;
		const wasReturning = this.#target === "trigger";
		this.#detachDocMove();
		this.#clearFallback();
		this.#active = false;
		this.#clearVisuals();
		if (wasReturning) {
			this.#clearDisengageTimer();
			this.#disengageTimer = setTimeout(() => {
				this.#disengageTimer = null;
				this.#opts.setIsPointerInTransit(false);
			}, 100);
		} else this.#opts.setIsPointerInTransit(false);
	}
	#clearDisengageTimer() {
		if (this.#disengageTimer === null) return;
		clearTimeout(this.#disengageTimer);
		this.#disengageTimer = null;
	}
	#intentExit() {
		const pointerPoint = this.#pointerPoint;
		this.#detachDocMove();
		this.#clearFallback();
		this.#clearDisengageTimer();
		this.#active = false;
		this.#opts.setIsPointerInTransit(false);
		this.#clearVisuals();
		this.#opts.onIntentExit(pointerPoint);
	}
	#reset() {
		this.#detachDocMove();
		this.#clearFallback();
		this.#clearDisengageTimer();
		if (this.#active) this.#opts.setIsPointerInTransit(false);
		this.#active = false;
		this.#target = null;
		this.#apex = null;
		this.#pointerPoint = null;
		this.#launchPoint = null;
	}
	#isPointerInDescendantSubContent(pt) {
		const contentNode = this.#opts.contentNode();
		if (!contentNode) return false;
		const el = contentNode.ownerDocument.elementFromPoint(pt.x, pt.y);
		if (!el) return false;
		const selector = this.#opts.subContentSelector();
		const subContent = el.closest(selector);
		if (!subContent || subContent === contentNode) return false;
		if (subContent.id) return !!contentNode.querySelector(`[aria-controls="${subContent.id}"]`);
		return false;
	}
	#onDocMove = (e) => {
		if (!this.#active || !this.#target) return;
		if (!isMouseEvent(e)) return;
		const triggerNode = this.#opts.triggerNode();
		const contentNode = this.#opts.contentNode();
		if (!triggerNode || !contentNode) {
			this.#intentExit();
			return;
		}
		this.#clearFallback();
		const pt = {
			x: e.clientX,
			y: e.clientY
		};
		this.#pointerPoint = pt;
		const triggerRect = triggerNode.getBoundingClientRect();
		const contentRect = contentNode.getBoundingClientRect();
		if (this.#target === "content" && isInsideRect$1(pt, contentRect)) {
			this.#disengage();
			return;
		}
		if (this.#target === "trigger" && isInsideInsetRect(pt, triggerRect, 4)) {
			this.#disengage();
			return;
		}
		if (this.#isPointerInDescendantSubContent(pt)) {
			this.#startFallback();
			return;
		}
		const geo = this.#computePolygons(pt, this.#target);
		if (!geo) {
			this.#intentExit();
			return;
		}
		if (this.#isInSafeZone(pt, geo.corridor, geo.intent)) {
			this.#startFallback();
			return;
		}
		this.#intentExit();
	};
	#attachDocMove() {
		if (this.#cleanupDocMove) return;
		const doc = getDocument(this.#opts.triggerNode() ?? this.#opts.contentNode());
		if (!doc) return;
		doc.addEventListener("pointermove", this.#onDocMove, true);
		this.#cleanupDocMove = () => {
			doc.removeEventListener("pointermove", this.#onDocMove, true);
			this.#cleanupDocMove = null;
		};
	}
	#detachDocMove() {
		this.#cleanupDocMove?.();
	}
	#startFallback() {
		this.#clearFallback();
		this.#fallbackTimer = setTimeout(() => {
			this.#fallbackTimer = null;
			if (this.#active) this.#intentExit();
		}, 500);
	}
	#clearFallback() {
		if (this.#fallbackTimer === null) return;
		clearTimeout(this.#fallbackTimer);
		this.#fallbackTimer = null;
	}
	#clearVisuals() {
		this.#target = null;
		this.#apex = null;
		this.#pointerPoint = null;
	}
};
function isPointInPolygon$1(point, polygon) {
	const { x, y } = point;
	let inside = false;
	for (let i = 0, j = polygon.length - 1; i < polygon.length; j = i++) {
		const xi = polygon[i].x;
		const yi = polygon[i].y;
		const xj = polygon[j].x;
		const yj = polygon[j].y;
		if (yi > y !== yj > y && x < (xj - xi) * (y - yi) / (yj - yi) + xi) inside = !inside;
	}
	return inside;
}
function isInsideRect$1(point, rect) {
	return point.x >= rect.left && point.x <= rect.right && point.y >= rect.top && point.y <= rect.bottom;
}
function isInsideInsetRect(point, rect, inset) {
	return point.x >= rect.left + inset && point.x <= rect.right - inset && point.y >= rect.top + inset && point.y <= rect.bottom - inset;
}
function getSide$1(triggerRect, contentRect) {
	const triggerCenterX = triggerRect.left + triggerRect.width / 2;
	const triggerCenterY = triggerRect.top + triggerRect.height / 2;
	const contentCenterX = contentRect.left + contentRect.width / 2;
	const contentCenterY = contentRect.top + contentRect.height / 2;
	const deltaX = contentCenterX - triggerCenterX;
	const deltaY = contentCenterY - triggerCenterY;
	if (Math.abs(deltaX) > Math.abs(deltaY)) return deltaX > 0 ? "right" : "left";
	return deltaY > 0 ? "bottom" : "top";
}
function getCorridorPolygon(triggerRect, contentRect, side) {
	const buffer = 2;
	switch (side) {
		case "top": return [
			{
				x: Math.min(triggerRect.left, contentRect.left) - buffer,
				y: triggerRect.top
			},
			{
				x: Math.min(triggerRect.left, contentRect.left) - buffer,
				y: contentRect.bottom
			},
			{
				x: Math.max(triggerRect.right, contentRect.right) + buffer,
				y: contentRect.bottom
			},
			{
				x: Math.max(triggerRect.right, contentRect.right) + buffer,
				y: triggerRect.top
			}
		];
		case "bottom": return [
			{
				x: Math.min(triggerRect.left, contentRect.left) - buffer,
				y: triggerRect.bottom
			},
			{
				x: Math.min(triggerRect.left, contentRect.left) - buffer,
				y: contentRect.top
			},
			{
				x: Math.max(triggerRect.right, contentRect.right) + buffer,
				y: contentRect.top
			},
			{
				x: Math.max(triggerRect.right, contentRect.right) + buffer,
				y: triggerRect.bottom
			}
		];
		case "left": return [
			{
				x: triggerRect.left,
				y: Math.min(triggerRect.top, contentRect.top) - buffer
			},
			{
				x: contentRect.right,
				y: Math.min(triggerRect.top, contentRect.top) - buffer
			},
			{
				x: contentRect.right,
				y: Math.max(triggerRect.bottom, contentRect.bottom) + buffer
			},
			{
				x: triggerRect.left,
				y: Math.max(triggerRect.bottom, contentRect.bottom) + buffer
			}
		];
		case "right": return [
			{
				x: triggerRect.right,
				y: Math.min(triggerRect.top, contentRect.top) - buffer
			},
			{
				x: contentRect.left,
				y: Math.min(triggerRect.top, contentRect.top) - buffer
			},
			{
				x: contentRect.left,
				y: Math.max(triggerRect.bottom, contentRect.bottom) + buffer
			},
			{
				x: triggerRect.right,
				y: Math.max(triggerRect.bottom, contentRect.bottom) + buffer
			}
		];
	}
}
function getIntentPolygon(exitPoint, targetRect, side, target, sourceRect) {
	const edgeBuffer = 8;
	const effectiveSide = target === "trigger" ? flipSide(side) : side;
	const top = sourceRect ? Math.min(targetRect.top, sourceRect.top) - edgeBuffer : targetRect.top - edgeBuffer;
	const bottom = sourceRect ? Math.max(targetRect.bottom, sourceRect.bottom) + edgeBuffer : targetRect.bottom + edgeBuffer;
	const left = sourceRect ? Math.min(targetRect.left, sourceRect.left) - edgeBuffer : targetRect.left - edgeBuffer;
	const right = sourceRect ? Math.max(targetRect.right, sourceRect.right) + edgeBuffer : targetRect.right + edgeBuffer;
	switch (effectiveSide) {
		case "right": return [
			exitPoint,
			{
				x: targetRect.left,
				y: top
			},
			{
				x: targetRect.left,
				y: bottom
			}
		];
		case "left": return [
			exitPoint,
			{
				x: targetRect.right,
				y: top
			},
			{
				x: targetRect.right,
				y: bottom
			}
		];
		case "bottom": return [
			exitPoint,
			{
				x: left,
				y: targetRect.top
			},
			{
				x: right,
				y: targetRect.top
			}
		];
		case "top": return [
			exitPoint,
			{
				x: left,
				y: targetRect.bottom
			},
			{
				x: right,
				y: targetRect.bottom
			}
		];
	}
}
function flipSide(side) {
	switch (side) {
		case "top": return "bottom";
		case "bottom": return "top";
		case "left": return "right";
		case "right": return "left";
	}
}
var MenuRootState = class MenuRootState {
	static create(opts) {
		const root = new MenuRootState(opts);
		return MenuRootContext.set(root);
	}
	opts;
	isUsingKeyboard = new IsUsingKeyboard();
	ignoreCloseAutoFocus = false;
	isPointerInTransit = false;
	constructor(opts) {
		this.opts = opts;
	}
	getBitsAttr = (part) => {
		return menuAttrs.getAttr(part, this.opts.variant.current);
	};
};
var MenuMenuState = class MenuMenuState {
	static create(opts, root) {
		return MenuMenuContext.set(new MenuMenuState(opts, root, null));
	}
	opts;
	root;
	parentMenu;
	contentId = boxWith(() => "");
	contentNode = null;
	contentPresence;
	triggerNode = null;
	constructor(opts, root, parentMenu) {
		this.opts = opts;
		this.root = root;
		this.parentMenu = parentMenu;
		this.contentPresence = new PresenceManager({
			ref: boxWith(() => this.contentNode),
			open: this.opts.open,
			onComplete: () => {
				this.opts.onOpenChangeComplete.current(this.opts.open.current);
			},
			shouldSkipExitAnimation: () => {
				if (this.root.opts.variant.current !== "menubar" || this.parentMenu !== null) return false;
				return this.root.opts.shouldSkipExitAnimation?.() ?? false;
			}
		});
		if (parentMenu) watch(() => parentMenu.opts.open.current, () => {
			if (parentMenu.opts.open.current) return;
			this.opts.open.current = false;
		});
	}
	toggleOpen() {
		this.opts.open.current = !this.opts.open.current;
	}
	onOpen() {
		this.opts.open.current = true;
	}
	onClose() {
		this.opts.open.current = false;
	}
};
var MenuContentState = class MenuContentState {
	static create(opts) {
		return MenuContentContext.set(new MenuContentState(opts, MenuMenuContext.get()));
	}
	opts;
	parentMenu;
	rovingFocusGroup;
	domContext;
	attachment;
	search = "";
	#timer = 0;
	#handleTypeaheadSearch;
	mounted = false;
	#isSub;
	constructor(opts, parentMenu) {
		this.opts = opts;
		this.parentMenu = parentMenu;
		this.domContext = new DOMContext(opts.ref);
		this.attachment = attachRef(this.opts.ref, (v) => {
			if (this.parentMenu.contentNode !== v) this.parentMenu.contentNode = v;
		});
		parentMenu.contentId = opts.id;
		this.#isSub = opts.isSub ?? false;
		this.onkeydown = this.onkeydown.bind(this);
		this.onblur = this.onblur.bind(this);
		this.onfocus = this.onfocus.bind(this);
		this.handleInteractOutside = this.handleInteractOutside.bind(this);
		new MenuSubmenuIntent({
			contentNode: () => this.parentMenu.contentNode,
			triggerNode: () => this.parentMenu.triggerNode,
			parentContentNode: () => this.parentMenu.parentMenu?.contentNode ?? null,
			subContentSelector: () => `[${this.parentMenu.root.getBitsAttr("sub-content")}]`,
			enabled: () => this.parentMenu.opts.open.current && Boolean(this.parentMenu.triggerNode?.hasAttribute(this.parentMenu.root.getBitsAttr("sub-trigger"))),
			onIntentExit: (pointerPoint) => {
				this.parentMenu.opts.open.current = false;
				this.#dispatchPointerMoveToHoveredSubTrigger(pointerPoint);
			},
			setIsPointerInTransit: (value) => {
				this.parentMenu.root.isPointerInTransit = value;
			}
		});
		this.#handleTypeaheadSearch = new DOMTypeahead({
			getActiveElement: () => this.domContext.getActiveElement(),
			getWindow: () => this.domContext.getWindow()
		}).handleTypeaheadSearch;
		this.rovingFocusGroup = new RovingFocusGroup({
			rootNode: boxWith(() => this.parentMenu.contentNode),
			candidateAttr: this.parentMenu.root.getBitsAttr("item"),
			loop: this.opts.loop,
			orientation: boxWith(() => "vertical")
		});
		watch(() => this.parentMenu.contentNode, (contentNode) => {
			if (!contentNode) return;
			const handler = () => {
				afterTick(() => {
					if (!this.parentMenu.root.isUsingKeyboard.current) return;
					this.rovingFocusGroup.focusFirstCandidate();
				});
			};
			return MenuOpenEvent.listen(contentNode, handler);
		});
	}
	#getCandidateNodes() {
		const node = this.parentMenu.contentNode;
		if (!node) return [];
		return Array.from(node.querySelectorAll(`[${this.parentMenu.root.getBitsAttr("item")}]:not([data-disabled])`));
	}
	#isPointerMovingToSubmenu() {
		return this.parentMenu.root.isPointerInTransit;
	}
	#dispatchPointerMoveToHoveredSubTrigger(pointerPoint) {
		if (!pointerPoint) return;
		const parentContentNode = this.parentMenu.parentMenu?.contentNode;
		if (!parentContentNode) return;
		const hoveredNode = this.domContext.getDocument().elementFromPoint(pointerPoint.x, pointerPoint.y);
		if (!isElement(hoveredNode)) return;
		const hoveredSubTrigger = hoveredNode.closest(`[${this.parentMenu.root.getBitsAttr("sub-trigger")}]`);
		if (!hoveredSubTrigger || !parentContentNode.contains(hoveredSubTrigger)) return;
		if (hoveredSubTrigger === this.parentMenu.triggerNode) return;
		hoveredSubTrigger.dispatchEvent(new PointerEvent("pointermove", {
			bubbles: true,
			cancelable: true,
			pointerType: "mouse",
			clientX: pointerPoint.x,
			clientY: pointerPoint.y
		}));
	}
	onCloseAutoFocus = (e) => {
		this.opts.onCloseAutoFocus.current?.(e);
		if (e.defaultPrevented || this.#isSub) return;
		if (this.parentMenu.root.ignoreCloseAutoFocus) {
			e.preventDefault();
			return;
		}
		if (this.parentMenu.triggerNode && isTabbable(this.parentMenu.triggerNode)) {
			e.preventDefault();
			this.parentMenu.triggerNode.focus();
		}
	};
	handleTabKeyDown(e) {
		/**
		* We locate the root `menu`'s trigger by going up the tree until
		* we find a menu that has no parent. This will allow us to focus the next
		* tabbable element before/after the root trigger.
		*/
		let rootMenu = this.parentMenu;
		while (rootMenu.parentMenu !== null) rootMenu = rootMenu.parentMenu;
		if (!rootMenu.triggerNode) return;
		e.preventDefault();
		const nodeToFocus = getTabbableFrom(rootMenu.triggerNode, e.shiftKey ? "prev" : "next");
		if (nodeToFocus) {
			/**
			* We set a flag to ignore the `onCloseAutoFocus` event handler
			* as well as the fallbacks inside the focus scope to prevent
			* race conditions causing focus to fall back to the body even
			* though we're trying to focus the next tabbable element.
			*/
			this.parentMenu.root.ignoreCloseAutoFocus = true;
			rootMenu.onClose();
			afterTick(() => {
				nodeToFocus.focus();
				afterTick(() => {
					this.parentMenu.root.ignoreCloseAutoFocus = false;
				});
			});
		} else this.domContext.getDocument().body.focus();
	}
	onkeydown(e) {
		if (e.defaultPrevented) return;
		if (e.key === "Tab") {
			this.handleTabKeyDown(e);
			return;
		}
		const target = e.target;
		const currentTarget = e.currentTarget;
		if (!isHTMLElement(target) || !isHTMLElement(currentTarget)) return;
		const isKeydownInside = target.closest(`[${this.parentMenu.root.getBitsAttr("content")}]`)?.id === this.parentMenu.contentId.current;
		const isModifierKey = e.ctrlKey || e.altKey || e.metaKey;
		const isCharacterKey = e.key.length === 1;
		if (this.rovingFocusGroup.handleKeydown(target, e)) return;
		if (e.code === "Space") return;
		const candidateNodes = this.#getCandidateNodes();
		if (isKeydownInside) {
			if (!isModifierKey && isCharacterKey) this.#handleTypeaheadSearch(e.key, candidateNodes);
		}
		if (e.target?.id !== this.parentMenu.contentId.current) return;
		if (!FIRST_LAST_KEYS$1.includes(e.key)) return;
		e.preventDefault();
		if (LAST_KEYS$1.includes(e.key)) candidateNodes.reverse();
		focusFirst(candidateNodes, { select: false }, () => this.domContext.getActiveElement());
	}
	onblur(e) {
		if (!isElement(e.currentTarget)) return;
		if (!isElement(e.target)) return;
		if (!e.currentTarget.contains?.(e.target)) {
			this.domContext.getWindow().clearTimeout(this.#timer);
			this.search = "";
		}
	}
	onfocus(_) {
		if (!this.parentMenu.root.isUsingKeyboard.current) return;
		afterTick(() => this.rovingFocusGroup.focusFirstCandidate());
	}
	onItemEnter() {
		return this.#isPointerMovingToSubmenu();
	}
	onItemLeave(e) {
		if (e.currentTarget.hasAttribute(this.parentMenu.root.getBitsAttr("sub-trigger"))) return;
		if (this.#isPointerMovingToSubmenu() || this.parentMenu.root.isUsingKeyboard.current) return;
		this.parentMenu.contentNode?.focus({ preventScroll: true });
		this.rovingFocusGroup.setCurrentTabStopId("");
	}
	onTriggerLeave() {
		if (this.#isPointerMovingToSubmenu()) return true;
		return false;
	}
	handleInteractOutside(e) {
		if (!isElementOrSVGElement(e.target)) return;
		const triggerId = this.parentMenu.triggerNode?.id;
		if (e.target.id === triggerId) {
			e.preventDefault();
			return;
		}
		if (e.target.closest(`#${triggerId}`)) {
			e.preventDefault();
			return;
		}
		/**
		* when the menu closes due to an outside pointer interaction (for example,
		* clicking another dropdown trigger), avoid focusing this menu's trigger
		* to prevent stealing focus from the new interaction target.
		*/
		this.parentMenu.root.ignoreCloseAutoFocus = true;
		afterTick(() => {
			this.parentMenu.root.ignoreCloseAutoFocus = false;
		});
	}
	get shouldRender() {
		return this.parentMenu.contentPresence.shouldRender;
	}
	#snippetProps = derived(() => ({ open: this.parentMenu.opts.open.current }));
	get snippetProps() {
		return this.#snippetProps();
	}
	set snippetProps($$value) {
		return this.#snippetProps($$value);
	}
	#props = derived(() => ({
		id: this.opts.id.current,
		role: "menu",
		"aria-orientation": "vertical",
		[this.parentMenu.root.getBitsAttr("content")]: "",
		"data-state": getDataOpenClosed(this.parentMenu.opts.open.current),
		...getDataTransitionAttrs(this.parentMenu.contentPresence.transitionStatus),
		onkeydown: this.onkeydown,
		onblur: this.onblur,
		onfocus: this.onfocus,
		dir: this.parentMenu.root.opts.dir.current,
		style: {
			pointerEvents: "auto",
			contain: "layout style"
		},
		...this.attachment
	}));
	get props() {
		return this.#props();
	}
	set props($$value) {
		return this.#props($$value);
	}
	popperProps = { onCloseAutoFocus: (e) => this.onCloseAutoFocus(e) };
};
var MenuItemSharedState = class {
	opts;
	content;
	attachment;
	#isFocused = false;
	constructor(opts, content) {
		this.opts = opts;
		this.content = content;
		this.attachment = attachRef(this.opts.ref);
		this.onpointermove = this.onpointermove.bind(this);
		this.onpointerleave = this.onpointerleave.bind(this);
		this.onfocus = this.onfocus.bind(this);
		this.onblur = this.onblur.bind(this);
	}
	onpointermove(e) {
		if (e.defaultPrevented) return;
		if (!isMouseEvent(e)) return;
		if (this.opts.disabled.current) this.content.onItemLeave(e);
		else {
			if (this.content.onItemEnter()) return;
			const item = e.currentTarget;
			if (!isHTMLElement(item)) return;
			item.focus({ preventScroll: true });
		}
	}
	onpointerleave(e) {
		if (e.defaultPrevented) return;
		if (!isMouseEvent(e)) return;
		this.content.onItemLeave(e);
	}
	onfocus(e) {
		afterTick(() => {
			if (e.defaultPrevented || this.opts.disabled.current) return;
			this.#isFocused = true;
		});
	}
	onblur(e) {
		afterTick(() => {
			if (e.defaultPrevented) return;
			this.#isFocused = false;
		});
	}
	#props = derived(() => ({
		id: this.opts.id.current,
		tabindex: -1,
		role: "menuitem",
		"aria-disabled": boolToStr(this.opts.disabled.current),
		"data-disabled": boolToEmptyStrOrUndef(this.opts.disabled.current),
		"data-highlighted": this.#isFocused ? "" : void 0,
		[this.content.parentMenu.root.getBitsAttr("item")]: "",
		onpointermove: this.onpointermove,
		onpointerleave: this.onpointerleave,
		onfocus: this.onfocus,
		onblur: this.onblur,
		...this.attachment
	}));
	get props() {
		return this.#props();
	}
	set props($$value) {
		return this.#props($$value);
	}
};
var MenuItemState = class MenuItemState {
	static create(opts) {
		const item = new MenuItemSharedState(opts, MenuContentContext.get());
		return new MenuItemState(opts, item);
	}
	opts;
	item;
	root;
	#isPointerDown = false;
	constructor(opts, item) {
		this.opts = opts;
		this.item = item;
		this.root = item.content.parentMenu.root;
		this.onkeydown = this.onkeydown.bind(this);
		this.onclick = this.onclick.bind(this);
		this.onpointerdown = this.onpointerdown.bind(this);
		this.onpointerup = this.onpointerup.bind(this);
	}
	#handleSelect() {
		if (this.item.opts.disabled.current) return;
		const selectEvent = new CustomEvent("menuitemselect", {
			bubbles: true,
			cancelable: true
		});
		this.opts.onSelect.current(selectEvent);
		if (selectEvent.defaultPrevented) {
			this.item.content.parentMenu.root.isUsingKeyboard.current = false;
			return;
		}
		if (this.opts.closeOnSelect.current) this.item.content.parentMenu.root.opts.onClose();
	}
	onkeydown(e) {
		const isTypingAhead = this.item.content.search !== "";
		if (this.item.opts.disabled.current || isTypingAhead && e.key === " ") return;
		if (SELECTION_KEYS$1.includes(e.key)) {
			if (!isHTMLElement(e.currentTarget)) return;
			e.currentTarget.click();
			/**
			* We prevent default browser behavior for selection keys as they should trigger
			* a selection only:
			* - prevents space from scrolling the page.
			* - if keydown causes focus to move, prevents keydown from firing on the new target.
			*/
			e.preventDefault();
		}
	}
	onclick(_) {
		if (this.item.opts.disabled.current) return;
		this.#handleSelect();
	}
	onpointerup(e) {
		if (e.defaultPrevented) return;
		if (!this.#isPointerDown) {
			if (!isHTMLElement(e.currentTarget)) return;
			e.currentTarget?.click();
		}
	}
	onpointerdown(_) {
		this.#isPointerDown = true;
	}
	#props = derived(() => mergeProps(this.item.props, {
		onclick: this.onclick,
		onpointerdown: this.onpointerdown,
		onpointerup: this.onpointerup,
		onkeydown: this.onkeydown
	}));
	get props() {
		return this.#props();
	}
	set props($$value) {
		return this.#props($$value);
	}
};
var MenuSubTriggerState = class MenuSubTriggerState {
	static create(opts) {
		const content = MenuContentContext.get();
		const item = new MenuItemSharedState(opts, content);
		const submenu = MenuMenuContext.get();
		return new MenuSubTriggerState(opts, item, content, submenu);
	}
	opts;
	item;
	content;
	submenu;
	attachment;
	#openTimer = null;
	constructor(opts, item, content, submenu) {
		this.opts = opts;
		this.item = item;
		this.content = content;
		this.submenu = submenu;
		this.attachment = attachRef(this.opts.ref, (v) => this.submenu.triggerNode = v);
		this.onpointerleave = this.onpointerleave.bind(this);
		this.onpointermove = this.onpointermove.bind(this);
		this.onkeydown = this.onkeydown.bind(this);
		this.onclick = this.onclick.bind(this);
	}
	#clearOpenTimer() {
		if (this.#openTimer === null) return;
		this.content.domContext.getWindow().clearTimeout(this.#openTimer);
		this.#openTimer = null;
	}
	onpointermove(e) {
		if (!isMouseEvent(e)) return;
		if (this.submenu.root.isPointerInTransit) {
			if (this.#openTimer !== null) this.#clearOpenTimer();
			return;
		}
		if (!this.item.opts.disabled.current && !this.submenu.opts.open.current && !this.#openTimer) {
			const openDelay = this.opts.openDelay.current;
			if (openDelay <= 0) {
				this.submenu.onOpen();
				return;
			}
			this.#openTimer = this.content.domContext.setTimeout(() => {
				if (this.submenu.root.isPointerInTransit) {
					this.#clearOpenTimer();
					return;
				}
				this.submenu.onOpen();
				this.#clearOpenTimer();
			}, openDelay);
		}
	}
	onpointerleave(e) {
		if (!isMouseEvent(e)) return;
		this.#clearOpenTimer();
	}
	onkeydown(e) {
		const isTypingAhead = this.content.search !== "";
		if (this.item.opts.disabled.current || isTypingAhead && e.key === " ") return;
		if (SUB_OPEN_KEYS[this.submenu.root.opts.dir.current].includes(e.key)) {
			e.currentTarget.click();
			e.preventDefault();
		}
	}
	onclick(e) {
		if (this.item.opts.disabled.current) return;
		/**
		* We manually focus because iOS Safari doesn't always focus on click (e.g. buttons)
		* and we rely heavily on `onFocusOutside` for submenus to close when switching
		* between separate submenus.
		*/
		if (!isHTMLElement(e.currentTarget)) return;
		e.currentTarget.focus();
		const selectEvent = new CustomEvent("menusubtriggerselect", {
			bubbles: true,
			cancelable: true
		});
		this.opts.onSelect.current(selectEvent);
		if (!this.submenu.opts.open.current) {
			this.submenu.onOpen();
			afterTick(() => {
				const contentNode = this.submenu.contentNode;
				if (!contentNode) return;
				MenuOpenEvent.dispatch(contentNode);
			});
		}
	}
	#props = derived(() => mergeProps({
		"aria-haspopup": "menu",
		"aria-expanded": boolToStr(this.submenu.opts.open.current),
		"data-state": getDataOpenClosed(this.submenu.opts.open.current),
		"aria-controls": this.submenu.opts.open.current ? this.submenu.contentId.current : void 0,
		[this.submenu.root.getBitsAttr("sub-trigger")]: "",
		onclick: this.onclick,
		onpointermove: this.onpointermove,
		onpointerleave: this.onpointerleave,
		onkeydown: this.onkeydown,
		...this.attachment
	}, this.item.props));
	get props() {
		return this.#props();
	}
	set props($$value) {
		return this.#props($$value);
	}
};
var MenuGroupState = class MenuGroupState {
	static create(opts) {
		return MenuGroupContext.set(new MenuGroupState(opts, MenuRootContext.get()));
	}
	opts;
	root;
	attachment;
	groupHeadingId = void 0;
	constructor(opts, root) {
		this.opts = opts;
		this.root = root;
		this.attachment = attachRef(this.opts.ref);
	}
	#props = derived(() => ({
		id: this.opts.id.current,
		role: "group",
		"aria-labelledby": this.groupHeadingId,
		[this.root.getBitsAttr("group")]: "",
		...this.attachment
	}));
	get props() {
		return this.#props();
	}
	set props($$value) {
		return this.#props($$value);
	}
};
var MenuSeparatorState = class MenuSeparatorState {
	static create(opts) {
		return new MenuSeparatorState(opts, MenuRootContext.get());
	}
	opts;
	root;
	attachment;
	constructor(opts, root) {
		this.opts = opts;
		this.root = root;
		this.attachment = attachRef(this.opts.ref);
	}
	#props = derived(() => ({
		id: this.opts.id.current,
		role: "group",
		[this.root.getBitsAttr("separator")]: "",
		...this.attachment
	}));
	get props() {
		return this.#props();
	}
	set props($$value) {
		return this.#props($$value);
	}
};
var MenuRadioGroupState = class MenuRadioGroupState {
	static create(opts) {
		return MenuGroupContext.set(MenuRadioGroupContext.set(new MenuRadioGroupState(opts, MenuContentContext.get())));
	}
	opts;
	content;
	attachment;
	groupHeadingId = null;
	root;
	constructor(opts, content) {
		this.opts = opts;
		this.content = content;
		this.root = content.parentMenu.root;
		this.attachment = attachRef(this.opts.ref);
	}
	setValue(v) {
		this.opts.value.current = v;
	}
	#props = derived(() => ({
		id: this.opts.id.current,
		[this.root.getBitsAttr("radio-group")]: "",
		role: "group",
		"aria-labelledby": this.groupHeadingId,
		...this.attachment
	}));
	get props() {
		return this.#props();
	}
	set props($$value) {
		return this.#props($$value);
	}
};
var MenuRadioItemState = class MenuRadioItemState {
	static create(opts) {
		const radioGroup = MenuRadioGroupContext.get();
		const item = new MenuItemState(opts, new MenuItemSharedState(opts, radioGroup.content));
		return new MenuRadioItemState(opts, item, radioGroup);
	}
	opts;
	item;
	group;
	attachment;
	#isChecked = derived(() => this.group.opts.value.current === this.opts.value.current);
	get isChecked() {
		return this.#isChecked();
	}
	set isChecked($$value) {
		return this.#isChecked($$value);
	}
	constructor(opts, item, group) {
		this.opts = opts;
		this.item = item;
		this.group = group;
		this.attachment = attachRef(this.opts.ref);
	}
	selectValue() {
		this.group.setValue(this.opts.value.current);
	}
	#props = derived(() => ({
		[this.group.root.getBitsAttr("radio-item")]: "",
		...this.item.props,
		role: "menuitemradio",
		"aria-checked": getAriaChecked(this.isChecked, false),
		"data-state": getCheckedState(this.isChecked),
		...this.attachment
	}));
	get props() {
		return this.#props();
	}
	set props($$value) {
		return this.#props($$value);
	}
};
var DropdownMenuTriggerState = class DropdownMenuTriggerState {
	static create(opts) {
		return new DropdownMenuTriggerState(opts, MenuMenuContext.get());
	}
	opts;
	parentMenu;
	attachment;
	constructor(opts, parentMenu) {
		this.opts = opts;
		this.parentMenu = parentMenu;
		this.attachment = attachRef(this.opts.ref, (v) => this.parentMenu.triggerNode = v);
	}
	onclick = (e) => {
		/**
		* MacOS VoiceOver sends a click in Safari/Firefox bypassing the keydown event
		* when V0+Space is pressed. Since we already handle the keydown event and the
		* pointerdown events separately, we ignore it if the detail is not 0.
		*/
		if (this.opts.disabled.current || e.detail !== 0) return;
		this.parentMenu.toggleOpen();
		e.preventDefault();
	};
	onpointerdown = (e) => {
		if (this.opts.disabled.current) return;
		if (e.pointerType === "touch") return e.preventDefault();
		if (e.button === 0 && e.ctrlKey === false) {
			this.parentMenu.toggleOpen();
			if (!this.parentMenu.opts.open.current) e.preventDefault();
		}
	};
	onpointerup = (e) => {
		if (this.opts.disabled.current) return;
		if (e.pointerType === "touch") {
			e.preventDefault();
			this.parentMenu.toggleOpen();
		}
	};
	onkeydown = (e) => {
		if (this.opts.disabled.current) return;
		if (e.key === " " || e.key === "Enter") {
			this.parentMenu.toggleOpen();
			e.preventDefault();
			return;
		}
		if (e.key === "ArrowDown") {
			this.parentMenu.onOpen();
			e.preventDefault();
		}
	};
	#ariaControls = derived(() => {
		if (this.parentMenu.opts.open.current && this.parentMenu.contentId.current) return this.parentMenu.contentId.current;
	});
	#props = derived(() => ({
		id: this.opts.id.current,
		disabled: this.opts.disabled.current,
		"aria-haspopup": "menu",
		"aria-expanded": boolToStr(this.parentMenu.opts.open.current),
		"aria-controls": this.#ariaControls(),
		"data-disabled": boolToEmptyStrOrUndef(this.opts.disabled.current),
		"data-state": getDataOpenClosed(this.parentMenu.opts.open.current),
		[this.parentMenu.root.getBitsAttr("trigger")]: "",
		onclick: this.onclick,
		onpointerdown: this.onpointerdown,
		onpointerup: this.onpointerup,
		onkeydown: this.onkeydown,
		...this.attachment
	}));
	get props() {
		return this.#props();
	}
	set props($$value) {
		return this.#props($$value);
	}
};
var MenuSubmenuState = class {
	static create(opts) {
		const menu = MenuMenuContext.get();
		return MenuMenuContext.set(new MenuMenuState(opts, menu.root, menu));
	}
};
//#endregion
//#region node_modules/bits-ui/dist/bits/utilities/dismissible-layer/use-dismissable-layer.svelte.js
globalThis.bitsDismissableLayers ??= /* @__PURE__ */ new Map();
var DismissibleLayerState = class DismissibleLayerState {
	static create(opts) {
		return new DismissibleLayerState(opts);
	}
	opts;
	#interactOutsideProp;
	#behaviorType;
	#interceptedEvents = { pointerdown: false };
	#isResponsibleLayer = false;
	#isFocusInsideDOMTree = false;
	#documentObj = void 0;
	#onFocusOutside;
	#unsubClickListener = noop;
	constructor(opts) {
		this.opts = opts;
		this.#behaviorType = opts.interactOutsideBehavior;
		this.#interactOutsideProp = opts.onInteractOutside;
		this.#onFocusOutside = opts.onFocusOutside;
		let unsubEvents = noop;
		const cleanup = () => {
			this.#resetState();
			globalThis.bitsDismissableLayers.delete(this);
			this.#handleInteractOutside.destroy();
			unsubEvents();
		};
		watch([() => this.opts.enabled.current, () => this.opts.ref.current], () => {
			if (!this.opts.enabled.current || !this.opts.ref.current) return;
			afterSleep(1, () => {
				if (!this.opts.ref.current) return;
				globalThis.bitsDismissableLayers.set(this, this.#behaviorType);
				unsubEvents();
				unsubEvents = this.#addEventListeners();
			});
			return cleanup;
		});
	}
	#handleFocus = (event) => {
		if (event.defaultPrevented) return;
		if (!this.opts.ref.current) return;
		afterTick(() => {
			if (!this.opts.ref.current || this.#isTargetWithinLayer(event.target)) return;
			if (event.target && !this.#isFocusInsideDOMTree) this.#onFocusOutside.current?.(event);
		});
	};
	#addEventListeners() {
		return executeCallbacks(
			/**
			* CAPTURE INTERACTION START
			* mark interaction-start event as intercepted.
			* mark responsible layer during interaction start
			* to avoid checking if is responsible layer during interaction end
			* when a new floating element may have been opened.
			*/
			on(this.#documentObj, "pointerdown", executeCallbacks(this.#markInterceptedEvent, this.#markResponsibleLayer), { capture: true }),
			/**
			* BUBBLE INTERACTION START
			* Mark interaction-start event as non-intercepted. Debounce `onInteractOutsideStart`
			* to avoid prematurely checking if other events were intercepted.
			*/
			on(this.#documentObj, "pointerdown", executeCallbacks(this.#markNonInterceptedEvent, this.#handleInteractOutside)),
			/**
			* HANDLE FOCUS OUTSIDE
			*/
			on(this.#documentObj, "focusin", this.#handleFocus)
		);
	}
	#handleDismiss = (e) => {
		let event = e;
		if (event.defaultPrevented) event = createWrappedEvent(e);
		this.#interactOutsideProp.current(e);
	};
	#handleInteractOutside = debounce((e) => {
		if (!this.opts.ref.current) {
			this.#unsubClickListener();
			return;
		}
		const isEventValid = this.opts.isValidEvent.current(e, this.opts.ref.current) || isValidEvent(e, this.opts.ref.current);
		if (!this.#isResponsibleLayer || this.#isAnyEventIntercepted() || !isEventValid) {
			this.#unsubClickListener();
			return;
		}
		let event = e;
		if (event.defaultPrevented) event = createWrappedEvent(event);
		if (this.#behaviorType.current !== "close" && this.#behaviorType.current !== "defer-otherwise-close") {
			this.#unsubClickListener();
			return;
		}
		if (e.pointerType === "touch") {
			this.#unsubClickListener();
			this.#unsubClickListener = on(this.#documentObj, "click", this.#handleDismiss, { once: true });
		} else this.#interactOutsideProp.current(event);
	}, 10);
	#markInterceptedEvent = (e) => {
		this.#interceptedEvents[e.type] = true;
	};
	#markNonInterceptedEvent = (e) => {
		this.#interceptedEvents[e.type] = false;
	};
	#markResponsibleLayer = () => {
		if (!this.opts.ref.current) return;
		this.#isResponsibleLayer = isResponsibleLayer(this.opts.ref.current);
	};
	#isTargetWithinLayer = (target) => {
		if (!this.opts.ref.current) return false;
		return isOrContainsTarget(this.opts.ref.current, target);
	};
	#resetState = debounce(() => {
		for (const eventType in this.#interceptedEvents) this.#interceptedEvents[eventType] = false;
		this.#isResponsibleLayer = false;
	}, 20);
	#isAnyEventIntercepted() {
		return Object.values(this.#interceptedEvents).some(Boolean);
	}
	#onfocuscapture = () => {
		this.#isFocusInsideDOMTree = true;
	};
	#onblurcapture = () => {
		this.#isFocusInsideDOMTree = false;
	};
	props = {
		onfocuscapture: this.#onfocuscapture,
		onblurcapture: this.#onblurcapture
	};
};
function getTopMostDismissableLayer(layersArr = [...globalThis.bitsDismissableLayers]) {
	return layersArr.findLast(([_, { current: behaviorType }]) => behaviorType === "close" || behaviorType === "ignore");
}
function isResponsibleLayer(node) {
	const layersArr = [...globalThis.bitsDismissableLayers];
	/**
	* We first check if we can find a top layer with `close` or `ignore`.
	* If that top layer was found and matches the provided node, then the node is
	* responsible for the outside interaction. Otherwise, we know that all layers defer so
	* the first layer is the responsible one.
	*/
	const topMostLayer = getTopMostDismissableLayer(layersArr);
	if (topMostLayer) return topMostLayer[0].opts.ref.current === node;
	const [firstLayerNode] = layersArr[0];
	return firstLayerNode.opts.ref.current === node;
}
function isValidEvent(e, node) {
	const target = e.target;
	if (!isElementOrSVGElement(target)) return false;
	const targetIsContextMenuTrigger = Boolean(target.closest(`[${CONTEXT_MENU_TRIGGER_ATTR}]`));
	const nodeIsContextMenu = Boolean(node.closest(`[${CONTEXT_MENU_CONTENT_ATTR}]`));
	if ("button" in e && e.button > 0 && !targetIsContextMenuTrigger) return false;
	if ("button" in e && e.button === 0 && targetIsContextMenuTrigger && nodeIsContextMenu) return true;
	if (targetIsContextMenuTrigger && nodeIsContextMenu) return false;
	return getOwnerDocument(target).documentElement.contains(target) && !isOrContainsTarget(node, target) && isClickTrulyOutside(e, node);
}
function createWrappedEvent(e) {
	const capturedCurrentTarget = e.currentTarget;
	const capturedTarget = e.target;
	let newEvent;
	if (e instanceof PointerEvent) newEvent = new PointerEvent(e.type, e);
	else newEvent = new PointerEvent("pointerdown", e);
	let isPrevented = false;
	return new Proxy(newEvent, { get: (target, prop) => {
		if (prop === "currentTarget") return capturedCurrentTarget;
		if (prop === "target") return capturedTarget;
		if (prop === "preventDefault") return () => {
			isPrevented = true;
			if (typeof target.preventDefault === "function") target.preventDefault();
		};
		if (prop === "defaultPrevented") return isPrevented;
		if (prop in target) return target[prop];
		return e[prop];
	} });
}
//#endregion
//#region node_modules/bits-ui/dist/bits/utilities/dismissible-layer/dismissible-layer.svelte
function Dismissible_layer($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { interactOutsideBehavior = "close", onInteractOutside = noop, onFocusOutside = noop, id, children, enabled, isValidEvent = () => false, ref } = $$props;
		const dismissibleLayerState = DismissibleLayerState.create({
			id: boxWith(() => id),
			interactOutsideBehavior: boxWith(() => interactOutsideBehavior),
			onInteractOutside: boxWith(() => onInteractOutside),
			enabled: boxWith(() => enabled),
			onFocusOutside: boxWith(() => onFocusOutside),
			isValidEvent: boxWith(() => isValidEvent),
			ref
		});
		children?.($$renderer, { props: dismissibleLayerState.props });
		$$renderer.push(`<!---->`);
	});
}
//#endregion
//#region node_modules/bits-ui/dist/bits/utilities/escape-layer/use-escape-layer.svelte.js
globalThis.bitsEscapeLayers ??= /* @__PURE__ */ new Map();
var EscapeLayerState = class EscapeLayerState {
	static create(opts) {
		return new EscapeLayerState(opts);
	}
	opts;
	domContext;
	constructor(opts) {
		this.opts = opts;
		this.domContext = new DOMContext(this.opts.ref);
		let unsubEvents = noop;
		watch(() => opts.enabled.current, (enabled) => {
			if (enabled) {
				globalThis.bitsEscapeLayers.set(this, opts.escapeKeydownBehavior);
				unsubEvents = this.#addEventListener();
			}
			return () => {
				unsubEvents();
				globalThis.bitsEscapeLayers.delete(this);
			};
		});
	}
	#addEventListener = () => {
		return on(this.domContext.getDocument(), "keydown", this.#onkeydown, { passive: false });
	};
	#onkeydown = (e) => {
		if (e.key !== "Escape" || !isResponsibleEscapeLayer(this)) return;
		const clonedEvent = new KeyboardEvent(e.type, e);
		e.preventDefault();
		const behaviorType = this.opts.escapeKeydownBehavior.current;
		if (behaviorType !== "close" && behaviorType !== "defer-otherwise-close") return;
		this.opts.onEscapeKeydown.current(clonedEvent);
	};
};
function isResponsibleEscapeLayer(instance) {
	const layersArr = [...globalThis.bitsEscapeLayers];
	/**
	* We first check if we can find a top layer with `close` or `ignore`.
	* If that top layer was found and matches the provided node, then the node is
	* responsible for the escape. Otherwise, we know that all layers defer so
	* the first layer is the responsible one.
	*/
	const topMostLayer = layersArr.findLast(([_, { current: behaviorType }]) => behaviorType === "close" || behaviorType === "ignore");
	if (topMostLayer) return topMostLayer[0] === instance;
	const [firstLayerNode] = layersArr[0];
	return firstLayerNode === instance;
}
//#endregion
//#region node_modules/bits-ui/dist/bits/utilities/escape-layer/escape-layer.svelte
function Escape_layer($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { escapeKeydownBehavior = "close", onEscapeKeydown = noop, children, enabled, ref } = $$props;
		EscapeLayerState.create({
			escapeKeydownBehavior: boxWith(() => escapeKeydownBehavior),
			onEscapeKeydown: boxWith(() => onEscapeKeydown),
			enabled: boxWith(() => enabled),
			ref
		});
		children?.($$renderer);
		$$renderer.push(`<!---->`);
	});
}
//#endregion
//#region node_modules/bits-ui/dist/bits/utilities/focus-scope/focus-scope-manager.js
var FocusScopeManager = class FocusScopeManager {
	static instance;
	#scopeStack = simpleBox([]);
	#focusHistory = /* @__PURE__ */ new WeakMap();
	#preFocusHistory = /* @__PURE__ */ new WeakMap();
	static getInstance() {
		if (!this.instance) this.instance = new FocusScopeManager();
		return this.instance;
	}
	register(scope) {
		const current = this.getActive();
		if (current && current !== scope) current.pause();
		const activeElement = document.activeElement;
		if (activeElement && activeElement !== document.body) this.#preFocusHistory.set(scope, activeElement);
		this.#scopeStack.current = this.#scopeStack.current.filter((s) => s !== scope);
		this.#scopeStack.current.unshift(scope);
	}
	unregister(scope) {
		this.#scopeStack.current = this.#scopeStack.current.filter((s) => s !== scope);
		const next = this.getActive();
		if (next) next.resume();
	}
	getActive() {
		return this.#scopeStack.current[0];
	}
	setFocusMemory(scope, element) {
		this.#focusHistory.set(scope, element);
	}
	getFocusMemory(scope) {
		return this.#focusHistory.get(scope);
	}
	isActiveScope(scope) {
		return this.getActive() === scope;
	}
	setPreFocusMemory(scope, element) {
		this.#preFocusHistory.set(scope, element);
	}
	getPreFocusMemory(scope) {
		return this.#preFocusHistory.get(scope);
	}
	clearPreFocusMemory(scope) {
		this.#preFocusHistory.delete(scope);
	}
};
//#endregion
//#region node_modules/bits-ui/dist/bits/utilities/focus-scope/focus-scope.svelte.js
var FocusScope = class FocusScope {
	#paused = false;
	#container = null;
	#manager = FocusScopeManager.getInstance();
	#cleanupFns = [];
	#opts;
	constructor(opts) {
		this.#opts = opts;
	}
	get paused() {
		return this.#paused;
	}
	pause() {
		this.#paused = true;
	}
	resume() {
		this.#paused = false;
	}
	#cleanup() {
		for (const fn of this.#cleanupFns) fn();
		this.#cleanupFns = [];
	}
	mount(container) {
		if (this.#container) this.unmount();
		this.#container = container;
		this.#manager.register(this);
		this.#setupEventListeners();
		this.#handleOpenAutoFocus();
	}
	unmount() {
		if (!this.#container) return;
		this.#cleanup();
		this.#handleCloseAutoFocus();
		this.#manager.unregister(this);
		this.#manager.clearPreFocusMemory(this);
		this.#container = null;
	}
	#handleOpenAutoFocus() {
		if (!this.#container) return;
		const event = new CustomEvent("focusScope.onOpenAutoFocus", {
			bubbles: false,
			cancelable: true
		});
		this.#opts.onOpenAutoFocus.current(event);
		if (!event.defaultPrevented) requestAnimationFrame(() => {
			if (!this.#container) return;
			const firstTabbable = this.#getFirstTabbable();
			if (firstTabbable) {
				firstTabbable.focus();
				this.#manager.setFocusMemory(this, firstTabbable);
			} else this.#container.focus();
		});
	}
	#handleCloseAutoFocus() {
		const event = new CustomEvent("focusScope.onCloseAutoFocus", {
			bubbles: false,
			cancelable: true
		});
		this.#opts.onCloseAutoFocus.current?.(event);
		if (!event.defaultPrevented) {
			const preFocusedElement = this.#manager.getPreFocusMemory(this);
			if (preFocusedElement && document.contains(preFocusedElement)) try {
				preFocusedElement.focus();
			} catch {
				document.body.focus();
			}
		}
	}
	#setupEventListeners() {
		if (!this.#container || !this.#opts.trap.current) return;
		const container = this.#container;
		const doc = container.ownerDocument;
		const handleFocus = (e) => {
			if (this.#paused || !this.#manager.isActiveScope(this)) return;
			const target = e.target;
			if (!target) return;
			if (container.contains(target)) this.#manager.setFocusMemory(this, target);
			else {
				const lastFocused = this.#manager.getFocusMemory(this);
				if (lastFocused && container.contains(lastFocused) && isFocusable(lastFocused)) {
					e.preventDefault();
					lastFocused.focus();
				} else {
					const firstTabbable = this.#getFirstTabbable();
					const firstFocusable = this.#getAllFocusables()[0];
					(firstTabbable || firstFocusable || container).focus();
				}
			}
		};
		const handleKeydown = (e) => {
			if (!this.#opts.loop || this.#paused || e.key !== "Tab") return;
			if (!this.#manager.isActiveScope(this)) return;
			const tabbables = this.#getTabbables();
			if (tabbables.length === 0) return;
			const first = tabbables[0];
			const last = tabbables[tabbables.length - 1];
			if (!e.shiftKey && doc.activeElement === last) {
				e.preventDefault();
				first.focus();
			} else if (e.shiftKey && doc.activeElement === first) {
				e.preventDefault();
				last.focus();
			}
		};
		this.#cleanupFns.push(on(doc, "focusin", handleFocus, { capture: true }), on(container, "keydown", handleKeydown));
		const observer = new MutationObserver(() => {
			const lastFocused = this.#manager.getFocusMemory(this);
			if (lastFocused && !container.contains(lastFocused)) {
				const firstTabbable = this.#getFirstTabbable();
				const firstFocusable = this.#getAllFocusables()[0];
				const elementToFocus = firstTabbable || firstFocusable;
				if (elementToFocus) {
					elementToFocus.focus();
					this.#manager.setFocusMemory(this, elementToFocus);
				} else container.focus();
			}
		});
		observer.observe(container, {
			childList: true,
			subtree: true
		});
		this.#cleanupFns.push(() => observer.disconnect());
	}
	#getTabbables() {
		if (!this.#container) return [];
		return tabbable(this.#container, {
			includeContainer: false,
			getShadowRoot: true
		});
	}
	#getFirstTabbable() {
		return this.#getTabbables()[0] || null;
	}
	#getAllFocusables() {
		if (!this.#container) return [];
		return focusable(this.#container, {
			includeContainer: false,
			getShadowRoot: true
		});
	}
	static use(opts) {
		let scope = null;
		watch([() => opts.ref.current, () => opts.enabled.current], ([ref, enabled]) => {
			if (ref && enabled) {
				if (!scope) scope = new FocusScope(opts);
				scope.mount(ref);
			} else if (scope) {
				scope.unmount();
				scope = null;
			}
		});
		return { get props() {
			return { tabindex: -1 };
		} };
	}
};
//#endregion
//#region node_modules/bits-ui/dist/bits/utilities/focus-scope/focus-scope.svelte
function Focus_scope($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { enabled = false, trapFocus = false, loop = false, onCloseAutoFocus = noop, onOpenAutoFocus = noop, focusScope, ref } = $$props;
		const focusScopeState = FocusScope.use({
			enabled: boxWith(() => enabled),
			trap: boxWith(() => trapFocus),
			loop,
			onCloseAutoFocus: boxWith(() => onCloseAutoFocus),
			onOpenAutoFocus: boxWith(() => onOpenAutoFocus),
			ref
		});
		focusScope?.($$renderer, { props: focusScopeState.props });
		$$renderer.push(`<!---->`);
	});
}
//#endregion
//#region node_modules/bits-ui/dist/bits/utilities/text-selection-layer/use-text-selection-layer.svelte.js
var noopPointer = () => {};
globalThis.bitsTextSelectionLayers ??= /* @__PURE__ */ new Map();
var TextSelectionLayerState = class TextSelectionLayerState {
	static create(opts) {
		return new TextSelectionLayerState(opts);
	}
	opts;
	domContext;
	#unsubSelectionLock = noop;
	#enabledSnapshot = false;
	#onPointerDownSnapshot = noopPointer;
	#onPointerUpSnapshot = noopPointer;
	constructor(opts) {
		this.opts = opts;
		this.domContext = new DOMContext(opts.ref);
		let unsubEvents = noop;
		watch(() => [
			this.opts.enabled.current,
			this.opts.onPointerDown.current,
			this.opts.onPointerUp.current
		], ([enabled, onPointerDown, onPointerUp]) => {
			this.#enabledSnapshot = enabled;
			this.#onPointerDownSnapshot = onPointerDown;
			this.#onPointerUpSnapshot = onPointerUp;
			if (enabled) {
				globalThis.bitsTextSelectionLayers.set(this, this.opts.enabled);
				unsubEvents();
				unsubEvents = this.#addEventListeners();
			}
			return () => {
				this.#enabledSnapshot = false;
				unsubEvents();
				this.#resetSelectionLock();
				globalThis.bitsTextSelectionLayers.delete(this);
			};
		});
	}
	#addEventListeners() {
		return executeCallbacks(on(this.domContext.getDocument(), "pointerdown", this.#pointerdown), on(this.domContext.getDocument(), "pointerup", composeHandlers(this.#resetSelectionLock, this.#pointerupUserHandler)));
	}
	#pointerupUserHandler = (e) => {
		this.#onPointerUpSnapshot(e);
	};
	#pointerdown = (e) => {
		const node = this.opts.ref.current;
		const target = e.target;
		if (!isHTMLElement(node) || !isHTMLElement(target) || !this.#enabledSnapshot) return;
		/**
		* We only lock user-selection overflow if layer is the top most layer and
		* pointerdown occurred inside the node. You are still allowed to select text
		* outside the node provided pointerdown occurs outside the node.
		*/
		if (!isHighestLayer(this) || !contains(node, target)) return;
		this.#onPointerDownSnapshot(e);
		if (e.defaultPrevented) return;
		this.#unsubSelectionLock = preventTextSelectionOverflow(node, this.domContext.getDocument().body);
	};
	#resetSelectionLock = () => {
		this.#unsubSelectionLock();
		this.#unsubSelectionLock = noop;
	};
};
var getUserSelect = (node) => node.style.userSelect || node.style.webkitUserSelect;
function preventTextSelectionOverflow(node, body) {
	const originalBodyUserSelect = getUserSelect(body);
	const originalNodeUserSelect = getUserSelect(node);
	setUserSelect(body, "none");
	setUserSelect(node, "text");
	return () => {
		setUserSelect(body, originalBodyUserSelect);
		setUserSelect(node, originalNodeUserSelect);
	};
}
function setUserSelect(node, value) {
	node.style.userSelect = value;
	node.style.webkitUserSelect = value;
}
function isHighestLayer(instance) {
	const layersArr = [...globalThis.bitsTextSelectionLayers];
	if (!layersArr.length) return false;
	const highestLayer = layersArr.at(-1);
	if (!highestLayer) return false;
	return highestLayer[0] === instance;
}
//#endregion
//#region node_modules/bits-ui/dist/bits/utilities/text-selection-layer/text-selection-layer.svelte
function Text_selection_layer($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { preventOverflowTextSelection = true, onPointerDown = noop, onPointerUp = noop, id, children, enabled, ref } = $$props;
		TextSelectionLayerState.create({
			id: boxWith(() => id),
			onPointerDown: boxWith(() => onPointerDown),
			onPointerUp: boxWith(() => onPointerUp),
			enabled: boxWith(() => enabled && preventOverflowTextSelection),
			ref
		});
		children?.($$renderer);
		$$renderer.push(`<!---->`);
	});
}
//#endregion
//#region node_modules/bits-ui/dist/internal/use-id.js
globalThis.bitsIdCounter ??= { current: 0 };
/**
* Generates a unique ID based on a global counter.
*/
function useId(prefix = "bits") {
	globalThis.bitsIdCounter.current++;
	return `${prefix}-${globalThis.bitsIdCounter.current}`;
}
//#endregion
//#region node_modules/bits-ui/dist/internal/shared-state.svelte.js
var SharedState = class {
	#factory;
	#subscribers = 0;
	#state;
	#scope;
	constructor(factory) {
		this.#factory = factory;
	}
	#dispose() {
		this.#subscribers -= 1;
		if (this.#scope && this.#subscribers <= 0) {
			this.#scope();
			this.#state = void 0;
			this.#scope = void 0;
		}
	}
	get(...args) {
		this.#subscribers += 1;
		if (this.#state === void 0) this.#scope = () => {};
		return this.#state;
	}
};
//#endregion
//#region node_modules/bits-ui/dist/internal/body-scroll-lock.svelte.js
var lockMap = new SvelteMap();
var initialBodyStyle = null;
var cleanupTimeoutId = null;
var isInCleanupTransition = false;
var anyLocked = boxWith(() => {
	for (const value of lockMap.values()) if (value) return true;
	return false;
});
/**
* We track the time we scheduled the cleanup to prevent race conditions
* when multiple locks are created/destroyed in the same tick, ensuring
* only the last one to schedule the cleanup will run.
*
* reference: https://github.com/huntabyte/bits-ui/issues/1639
*/
var cleanupScheduledAt = null;
var bodyLockStackCount = new SharedState(() => {
	function resetBodyStyle() {}
	function cancelPendingCleanup() {
		if (cleanupTimeoutId === null) return;
		window.clearTimeout(cleanupTimeoutId);
		cleanupTimeoutId = null;
	}
	function scheduleCleanupIfNoNewLocks(delay, callback) {
		cancelPendingCleanup();
		isInCleanupTransition = true;
		cleanupScheduledAt = Date.now();
		const currentCleanupId = cleanupScheduledAt;
		/**
		* We schedule the cleanup to run after a delay to allow new locks to register
		* that might have been added in the same tick as the current cleanup.
		*
		* If a new lock is added in the same tick, the cleanup will be cancelled and
		* a new cleanup will be scheduled.
		*
		* This is to prevent the cleanup from running too early and resetting the body
		* style before the new lock has had a chance to apply its styles.
		*/
		const cleanupFn = () => {
			cleanupTimeoutId = null;
			if (cleanupScheduledAt !== currentCleanupId) return;
			if (!isAnyLocked(lockMap)) {
				isInCleanupTransition = false;
				callback();
			} else isInCleanupTransition = false;
		};
		const actualDelay = delay === null ? 24 : delay;
		cleanupTimeoutId = window.setTimeout(cleanupFn, actualDelay);
	}
	function ensureInitialStyleCaptured() {
		if (initialBodyStyle === null && lockMap.size === 0 && !isInCleanupTransition) initialBodyStyle = document.body.getAttribute("style");
	}
	watch(() => anyLocked.current, () => {
		if (!anyLocked.current) return;
		ensureInitialStyleCaptured();
		isInCleanupTransition = false;
		const htmlStyle = getComputedStyle(document.documentElement);
		const bodyStyle = getComputedStyle(document.body);
		const hasStableGutter = htmlStyle.scrollbarGutter?.includes("stable") || bodyStyle.scrollbarGutter?.includes("stable");
		const verticalScrollbarWidth = window.innerWidth - document.documentElement.clientWidth;
		const config = {
			padding: Number.parseInt(bodyStyle.paddingRight ?? "0", 10) + verticalScrollbarWidth,
			margin: Number.parseInt(bodyStyle.marginRight ?? "0", 10)
		};
		if (verticalScrollbarWidth > 0 && !hasStableGutter) {
			document.body.style.paddingRight = `${config.padding}px`;
			document.body.style.marginRight = `${config.margin}px`;
			document.body.style.setProperty("--scrollbar-width", `${verticalScrollbarWidth}px`);
		}
		document.body.style.overflow = "hidden";
		if (isIOS) on(document, "touchmove", (e) => {
			if (e.target !== document.documentElement) return;
			if (e.touches.length > 1) return;
			e.preventDefault();
		}, { passive: false });
		/**
		* We ensure pointer-events: none is applied _after_ DOM updates, so that any focus/
		* interaction changes from opening overlays/menus complete _before_ we block pointer
		* events.
		*
		* this avoids race conditions where pointer-events could be set too early and break
		* focus/interaction.
		*/
		afterTick(() => {
			document.body.style.pointerEvents = "none";
			document.body.style.overflow = "hidden";
		});
	});
	return {
		get lockMap() {
			return lockMap;
		},
		resetBodyStyle,
		scheduleCleanupIfNoNewLocks,
		cancelPendingCleanup,
		ensureInitialStyleCaptured
	};
});
var BodyScrollLock = class {
	#id = useId();
	#initialState;
	#restoreScrollDelay = () => null;
	#countState;
	locked;
	constructor(initialState, restoreScrollDelay = () => null) {
		this.#initialState = initialState;
		this.#restoreScrollDelay = restoreScrollDelay;
		this.#countState = bodyLockStackCount.get();
		if (!this.#countState) return;
		/**
		* Since a new lock is being created, we cancel any pending cleanup to
		* prevent the cleanup from running too early and resetting the body style
		* before the new lock has had a chance to apply its styles.
		*
		* reference: https://github.com/huntabyte/bits-ui/issues/1639
		*/
		this.#countState.cancelPendingCleanup();
		this.#countState.ensureInitialStyleCaptured();
		this.#countState.lockMap.set(this.#id, this.#initialState ?? false);
		this.locked = boxWith(() => this.#countState.lockMap.get(this.#id) ?? false, (v) => this.#countState.lockMap.set(this.#id, v));
	}
};
function isAnyLocked(map) {
	for (const [_, value] of map) if (value) return true;
	return false;
}
//#endregion
//#region node_modules/bits-ui/dist/bits/utilities/scroll-lock/scroll-lock.svelte
function Scroll_lock($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { preventScroll = true, restoreScrollDelay = null } = $$props;
		if (preventScroll) new BodyScrollLock(preventScroll, () => restoreScrollDelay);
	});
}
//#endregion
//#region node_modules/bits-ui/dist/bits/dialog/components/dialog-overlay.svelte
function Dialog_overlay$1($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		const uid = props_id($$renderer);
		let { id = createId(uid), forceMount = false, child, children, ref = null, $$slots, $$events, ...restProps } = $$props;
		const overlayState = DialogOverlayState.create({
			id: boxWith(() => id),
			ref: boxWith(() => ref, (v) => ref = v)
		});
		const mergedProps = derived(() => mergeProps(restProps, overlayState.props));
		if (overlayState.shouldRender || forceMount) {
			$$renderer.push("<!--[0-->");
			if (child) {
				$$renderer.push("<!--[0-->");
				child($$renderer, {
					props: mergeProps(mergedProps()),
					...overlayState.snippetProps
				});
				$$renderer.push(`<!---->`);
			} else {
				$$renderer.push("<!--[-1-->");
				$$renderer.push(`<div${attributes({ ...mergeProps(mergedProps()) })}>`);
				children?.($$renderer, overlayState.snippetProps);
				$$renderer.push(`<!----></div>`);
			}
			$$renderer.push(`<!--]-->`);
		} else $$renderer.push("<!--[-1-->");
		$$renderer.push(`<!--]-->`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region node_modules/bits-ui/dist/bits/dialog/components/dialog-description.svelte
function Dialog_description$1($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		const uid = props_id($$renderer);
		let { id = createId(uid), children, child, ref = null, $$slots, $$events, ...restProps } = $$props;
		const descriptionState = DialogDescriptionState.create({
			id: boxWith(() => id),
			ref: boxWith(() => ref, (v) => ref = v)
		});
		const mergedProps = derived(() => mergeProps(restProps, descriptionState.props));
		if (child) {
			$$renderer.push("<!--[0-->");
			child($$renderer, { props: mergedProps() });
			$$renderer.push(`<!---->`);
		} else {
			$$renderer.push("<!--[-1-->");
			$$renderer.push(`<div${attributes({ ...mergedProps() })}>`);
			children?.($$renderer);
			$$renderer.push(`<!----></div>`);
		}
		$$renderer.push(`<!--]-->`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region node_modules/bits-ui/dist/bits/checkbox/checkbox.svelte.js
var checkboxAttrs = createBitsAttrs({
	component: "checkbox",
	parts: [
		"root",
		"group",
		"group-label",
		"input"
	]
});
var CheckboxGroupContext = new Context("Checkbox.Group");
var CheckboxRootContext = new Context("Checkbox.Root");
var CheckboxRootState = class CheckboxRootState {
	static create(opts, group = null) {
		return CheckboxRootContext.set(new CheckboxRootState(opts, group));
	}
	opts;
	group;
	#trueName = derived(() => {
		if (this.group && this.group.opts.name.current) return this.group.opts.name.current;
		return this.opts.name.current;
	});
	get trueName() {
		return this.#trueName();
	}
	set trueName($$value) {
		return this.#trueName($$value);
	}
	#trueRequired = derived(() => {
		if (this.group && this.group.opts.required.current) return true;
		return this.opts.required.current;
	});
	get trueRequired() {
		return this.#trueRequired();
	}
	set trueRequired($$value) {
		return this.#trueRequired($$value);
	}
	#trueDisabled = derived(() => {
		if (this.group && this.group.opts.disabled.current) return true;
		return this.opts.disabled.current;
	});
	get trueDisabled() {
		return this.#trueDisabled();
	}
	set trueDisabled($$value) {
		return this.#trueDisabled($$value);
	}
	#trueReadonly = derived(() => {
		if (this.group && this.group.opts.readonly.current) return true;
		return this.opts.readonly.current;
	});
	get trueReadonly() {
		return this.#trueReadonly();
	}
	set trueReadonly($$value) {
		return this.#trueReadonly($$value);
	}
	attachment;
	constructor(opts, group) {
		this.opts = opts;
		this.group = group;
		this.attachment = attachRef(this.opts.ref);
		this.onkeydown = this.onkeydown.bind(this);
		this.onclick = this.onclick.bind(this);
		watch.pre([() => snapshot$1(this.group?.opts.value.current), () => this.opts.value.current], ([groupValue, value]) => {
			if (!groupValue || !value) return;
			this.opts.checked.current = groupValue.includes(value);
		});
		watch.pre(() => this.opts.checked.current, (checked) => {
			if (!this.group) return;
			if (checked) this.group?.addValue(this.opts.value.current);
			else this.group?.removeValue(this.opts.value.current);
		});
	}
	onkeydown(e) {
		if (this.trueDisabled || this.trueReadonly) return;
		if (e.key === "Enter") {
			e.preventDefault();
			if (this.opts.type.current === "submit") e.currentTarget.closest("form")?.requestSubmit();
			return;
		}
		if (e.key === " ") {
			e.preventDefault();
			this.#toggle();
		}
	}
	#toggle() {
		if (this.opts.indeterminate.current) {
			this.opts.indeterminate.current = false;
			this.opts.checked.current = true;
		} else this.opts.checked.current = !this.opts.checked.current;
	}
	onclick(e) {
		if (this.trueDisabled || this.trueReadonly) return;
		if (this.opts.type.current === "submit") {
			this.#toggle();
			return;
		}
		e.preventDefault();
		this.#toggle();
	}
	#snippetProps = derived(() => ({
		checked: this.opts.checked.current,
		indeterminate: this.opts.indeterminate.current
	}));
	get snippetProps() {
		return this.#snippetProps();
	}
	set snippetProps($$value) {
		return this.#snippetProps($$value);
	}
	#props = derived(() => ({
		id: this.opts.id.current,
		role: "checkbox",
		type: this.opts.type.current,
		disabled: this.trueDisabled,
		"aria-checked": getAriaChecked(this.opts.checked.current, this.opts.indeterminate.current),
		"aria-required": boolToStr(this.trueRequired),
		"aria-readonly": boolToStr(this.trueReadonly),
		"data-disabled": boolToEmptyStrOrUndef(this.trueDisabled),
		"data-readonly": boolToEmptyStrOrUndef(this.trueReadonly),
		"data-state": getCheckboxDataState(this.opts.checked.current, this.opts.indeterminate.current),
		[checkboxAttrs.root]: "",
		onclick: this.onclick,
		onkeydown: this.onkeydown,
		...this.attachment
	}));
	get props() {
		return this.#props();
	}
	set props($$value) {
		return this.#props($$value);
	}
};
var CheckboxInputState = class CheckboxInputState {
	static create() {
		return new CheckboxInputState(CheckboxRootContext.get());
	}
	root;
	#trueChecked = derived(() => {
		if (!this.root.group) return this.root.opts.checked.current;
		if (this.root.opts.value.current !== void 0 && this.root.group.opts.value.current.includes(this.root.opts.value.current)) return true;
		return false;
	});
	get trueChecked() {
		return this.#trueChecked();
	}
	set trueChecked($$value) {
		return this.#trueChecked($$value);
	}
	#shouldRender = derived(() => Boolean(this.root.trueName));
	get shouldRender() {
		return this.#shouldRender();
	}
	set shouldRender($$value) {
		return this.#shouldRender($$value);
	}
	constructor(root) {
		this.root = root;
		this.onfocus = this.onfocus.bind(this);
	}
	onfocus(_) {
		if (!isHTMLElement(this.root.opts.ref.current)) return;
		this.root.opts.ref.current.focus();
	}
	#props = derived(() => ({
		type: "checkbox",
		checked: this.root.opts.checked.current === true,
		disabled: this.root.trueDisabled,
		required: this.root.trueRequired,
		name: this.root.trueName,
		value: this.root.opts.value.current,
		readonly: this.root.trueReadonly,
		onfocus: this.onfocus
	}));
	get props() {
		return this.#props();
	}
	set props($$value) {
		return this.#props($$value);
	}
};
function getCheckboxDataState(checked, indeterminate) {
	if (indeterminate) return "indeterminate";
	return checked ? "checked" : "unchecked";
}
//#endregion
//#region node_modules/bits-ui/dist/bits/utilities/hidden-input.svelte
function Hidden_input($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { value = void 0, $$slots, $$events, ...restProps } = $$props;
		const mergedProps = derived(() => mergeProps(restProps, {
			"aria-hidden": "true",
			tabindex: -1,
			style: {
				...srOnlyStyles,
				position: "absolute",
				top: "0",
				left: "0"
			}
		}));
		if (mergedProps().type === "checkbox") {
			$$renderer.push("<!--[0-->");
			$$renderer.push(`<input${attributes({
				...mergedProps(),
				value
			}, void 0, void 0, void 0, 4)}/>`);
		} else {
			$$renderer.push("<!--[-1-->");
			$$renderer.push(`<input${attributes({
				value,
				...mergedProps()
			}, void 0, void 0, void 0, 4)}/>`);
		}
		$$renderer.push(`<!--]-->`);
		bind_props($$props, { value });
	});
}
//#endregion
//#region node_modules/bits-ui/dist/bits/checkbox/components/checkbox-input.svelte
function Checkbox_input($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		const inputState = CheckboxInputState.create();
		if (inputState.shouldRender) {
			$$renderer.push("<!--[0-->");
			Hidden_input($$renderer, spread_props([inputState.props]));
		} else $$renderer.push("<!--[-1-->");
		$$renderer.push(`<!--]-->`);
	});
}
//#endregion
//#region node_modules/bits-ui/dist/bits/checkbox/components/checkbox.svelte
function Checkbox$1($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		const uid = props_id($$renderer);
		let { checked = false, ref = null, onCheckedChange, children, disabled = false, required = false, name = void 0, value = "on", id = createId(uid), indeterminate = false, onIndeterminateChange, child, type = "button", readonly, $$slots, $$events, ...restProps } = $$props;
		const group = CheckboxGroupContext.getOr(null);
		if (group && value) {
			if (group.opts.value.current.includes(value)) checked = true;
			else checked = false;
		}
		watch.pre(() => value, () => {
			if (group && value) {
				if (group.opts.value.current.includes(value)) checked = true;
				else checked = false;
			}
		});
		const rootState = CheckboxRootState.create({
			checked: boxWith(() => checked, (v) => {
				checked = v;
				onCheckedChange?.(v);
			}),
			disabled: boxWith(() => disabled ?? false),
			required: boxWith(() => required),
			name: boxWith(() => name),
			value: boxWith(() => value),
			id: boxWith(() => id),
			ref: boxWith(() => ref, (v) => ref = v),
			indeterminate: boxWith(() => indeterminate, (v) => {
				indeterminate = v;
				onIndeterminateChange?.(v);
			}),
			type: boxWith(() => type),
			readonly: boxWith(() => Boolean(readonly))
		}, group);
		const mergedProps = derived(() => mergeProps({ ...restProps }, rootState.props));
		if (child) {
			$$renderer.push("<!--[0-->");
			child($$renderer, {
				props: mergedProps(),
				...rootState.snippetProps
			});
			$$renderer.push(`<!---->`);
		} else {
			$$renderer.push("<!--[-1-->");
			$$renderer.push(`<button${attributes({ ...mergedProps() })}>`);
			children?.($$renderer, rootState.snippetProps);
			$$renderer.push(`<!----></button>`);
		}
		$$renderer.push(`<!--]--> `);
		Checkbox_input($$renderer, {});
		$$renderer.push(`<!---->`);
		bind_props($$props, {
			checked,
			ref,
			indeterminate
		});
	});
}
//#endregion
//#region node_modules/bits-ui/dist/bits/collapsible/collapsible.svelte.js
var collapsibleAttrs = createBitsAttrs({
	component: "collapsible",
	parts: [
		"root",
		"content",
		"trigger"
	]
});
var CollapsibleRootContext = new Context("Collapsible.Root");
var CollapsibleRootState = class CollapsibleRootState {
	static create(opts) {
		return CollapsibleRootContext.set(new CollapsibleRootState(opts));
	}
	opts;
	attachment;
	contentNode = null;
	contentPresence;
	contentId = void 0;
	constructor(opts) {
		this.opts = opts;
		this.toggleOpen = this.toggleOpen.bind(this);
		this.attachment = attachRef(this.opts.ref);
		this.contentPresence = new PresenceManager({
			ref: boxWith(() => this.contentNode),
			open: this.opts.open,
			onComplete: () => {
				this.opts.onOpenChangeComplete.current(this.opts.open.current);
			}
		});
	}
	toggleOpen() {
		this.opts.open.current = !this.opts.open.current;
	}
	#props = derived(() => ({
		id: this.opts.id.current,
		"data-state": getDataOpenClosed(this.opts.open.current),
		"data-disabled": boolToEmptyStrOrUndef(this.opts.disabled.current),
		[collapsibleAttrs.root]: "",
		...this.attachment
	}));
	get props() {
		return this.#props();
	}
	set props($$value) {
		return this.#props($$value);
	}
};
var CollapsibleContentState = class CollapsibleContentState {
	static create(opts) {
		return new CollapsibleContentState(opts, CollapsibleRootContext.get());
	}
	opts;
	root;
	attachment;
	#present = derived(() => {
		if (this.opts.hiddenUntilFound.current) return this.root.opts.open.current;
		return this.opts.forceMount.current || this.root.opts.open.current;
	});
	get present() {
		return this.#present();
	}
	set present($$value) {
		return this.#present($$value);
	}
	#originalStyles;
	#isMountAnimationPrevented = false;
	#width = 0;
	#height = 0;
	constructor(opts, root) {
		this.opts = opts;
		this.root = root;
		this.#isMountAnimationPrevented = root.opts.open.current;
		this.root.contentId = this.opts.id.current;
		this.attachment = attachRef(this.opts.ref, (v) => this.root.contentNode = v);
		watch.pre(() => this.opts.id.current, (id) => {
			this.root.contentId = id;
		});
		watch.pre([() => this.opts.ref.current, () => this.opts.hiddenUntilFound.current], ([node, hiddenUntilFound]) => {
			if (!node || !hiddenUntilFound) return;
			const handleBeforeMatch = () => {
				if (this.root.opts.open.current) return;
				requestAnimationFrame(() => {
					this.root.opts.open.current = true;
				});
			};
			return on(node, "beforematch", handleBeforeMatch);
		});
		watch([() => this.opts.ref.current, () => this.present], ([node]) => {
			if (!node) return;
			afterTick(() => {
				if (!this.opts.ref.current) return;
				this.#originalStyles = this.#originalStyles || {
					transitionDuration: node.style.transitionDuration,
					animationName: node.style.animationName
				};
				node.style.transitionDuration = "0s";
				node.style.animationName = "none";
				const rect = node.getBoundingClientRect();
				this.#height = rect.height;
				this.#width = rect.width;
				if (!this.#isMountAnimationPrevented) {
					const { animationName, transitionDuration } = this.#originalStyles;
					node.style.transitionDuration = transitionDuration;
					node.style.animationName = animationName;
				}
			});
		});
	}
	get shouldRender() {
		return this.root.contentPresence.shouldRender;
	}
	#snippetProps = derived(() => ({ open: this.root.opts.open.current }));
	get snippetProps() {
		return this.#snippetProps();
	}
	set snippetProps($$value) {
		return this.#snippetProps($$value);
	}
	#props = derived(() => ({
		id: this.opts.id.current,
		style: {
			"--bits-collapsible-content-height": this.#height ? `${this.#height}px` : void 0,
			"--bits-collapsible-content-width": this.#width ? `${this.#width}px` : void 0
		},
		hidden: this.opts.hiddenUntilFound.current && !this.root.opts.open.current ? "until-found" : void 0,
		"data-state": getDataOpenClosed(this.root.opts.open.current),
		...getDataTransitionAttrs(this.root.contentPresence.transitionStatus),
		"data-disabled": boolToEmptyStrOrUndef(this.root.opts.disabled.current),
		[collapsibleAttrs.content]: "",
		...this.opts.hiddenUntilFound.current && !this.shouldRender ? {} : { hidden: this.opts.hiddenUntilFound.current ? !this.shouldRender : this.opts.forceMount.current ? void 0 : !this.shouldRender },
		...this.attachment
	}));
	get props() {
		return this.#props();
	}
	set props($$value) {
		return this.#props($$value);
	}
};
var CollapsibleTriggerState = class CollapsibleTriggerState {
	static create(opts) {
		return new CollapsibleTriggerState(opts, CollapsibleRootContext.get());
	}
	opts;
	root;
	attachment;
	#isDisabled = derived(() => this.opts.disabled.current || this.root.opts.disabled.current);
	constructor(opts, root) {
		this.opts = opts;
		this.root = root;
		this.attachment = attachRef(this.opts.ref);
		this.onclick = this.onclick.bind(this);
		this.onkeydown = this.onkeydown.bind(this);
	}
	onclick(e) {
		if (this.#isDisabled()) return;
		if (e.button !== 0) return e.preventDefault();
		this.root.toggleOpen();
	}
	onkeydown(e) {
		if (this.#isDisabled()) return;
		if (e.key === " " || e.key === "Enter") {
			e.preventDefault();
			this.root.toggleOpen();
		}
	}
	#props = derived(() => ({
		id: this.opts.id.current,
		type: "button",
		disabled: this.#isDisabled(),
		"aria-controls": this.root.contentId,
		"aria-expanded": boolToStr(this.root.opts.open.current),
		"data-state": getDataOpenClosed(this.root.opts.open.current),
		"data-disabled": boolToEmptyStrOrUndef(this.#isDisabled()),
		[collapsibleAttrs.trigger]: "",
		onclick: this.onclick,
		onkeydown: this.onkeydown,
		...this.attachment
	}));
	get props() {
		return this.#props();
	}
	set props($$value) {
		return this.#props($$value);
	}
};
//#endregion
//#region node_modules/bits-ui/dist/bits/collapsible/components/collapsible.svelte
function Collapsible$1($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		const uid = props_id($$renderer);
		let { children, child, id = createId(uid), ref = null, open = false, disabled = false, onOpenChange = noop, onOpenChangeComplete = noop, $$slots, $$events, ...restProps } = $$props;
		const rootState = CollapsibleRootState.create({
			open: boxWith(() => open, (v) => {
				open = v;
				onOpenChange(v);
			}),
			disabled: boxWith(() => disabled),
			id: boxWith(() => id),
			ref: boxWith(() => ref, (v) => ref = v),
			onOpenChangeComplete: boxWith(() => onOpenChangeComplete)
		});
		const mergedProps = derived(() => mergeProps(restProps, rootState.props));
		if (child) {
			$$renderer.push("<!--[0-->");
			child($$renderer, { props: mergedProps() });
			$$renderer.push(`<!---->`);
		} else {
			$$renderer.push("<!--[-1-->");
			$$renderer.push(`<div${attributes({ ...mergedProps() })}>`);
			children?.($$renderer);
			$$renderer.push(`<!----></div>`);
		}
		$$renderer.push(`<!--]-->`);
		bind_props($$props, {
			ref,
			open
		});
	});
}
//#endregion
//#region node_modules/bits-ui/dist/bits/collapsible/components/collapsible-content.svelte
function Collapsible_content$1($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		const uid = props_id($$renderer);
		let { child, ref = null, forceMount = false, hiddenUntilFound = false, children, id = createId(uid), $$slots, $$events, ...restProps } = $$props;
		const contentState = CollapsibleContentState.create({
			id: boxWith(() => id),
			forceMount: boxWith(() => forceMount),
			hiddenUntilFound: boxWith(() => hiddenUntilFound),
			ref: boxWith(() => ref, (v) => ref = v)
		});
		const mergedProps = derived(() => mergeProps(restProps, contentState.props));
		if (child) {
			$$renderer.push("<!--[0-->");
			child($$renderer, {
				...contentState.snippetProps,
				props: mergedProps()
			});
			$$renderer.push(`<!---->`);
		} else {
			$$renderer.push("<!--[-1-->");
			$$renderer.push(`<div${attributes({ ...mergedProps() })}>`);
			children?.($$renderer);
			$$renderer.push(`<!----></div>`);
		}
		$$renderer.push(`<!--]-->`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region node_modules/bits-ui/dist/bits/collapsible/components/collapsible-trigger.svelte
function Collapsible_trigger$1($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		const uid = props_id($$renderer);
		let { children, child, ref = null, id = createId(uid), disabled = false, $$slots, $$events, ...restProps } = $$props;
		const triggerState = CollapsibleTriggerState.create({
			id: boxWith(() => id),
			ref: boxWith(() => ref, (v) => ref = v),
			disabled: boxWith(() => disabled)
		});
		const mergedProps = derived(() => mergeProps(restProps, triggerState.props));
		if (child) {
			$$renderer.push("<!--[0-->");
			child($$renderer, { props: mergedProps() });
			$$renderer.push(`<!---->`);
		} else {
			$$renderer.push("<!--[-1-->");
			$$renderer.push(`<button${attributes({ ...mergedProps() })}>`);
			children?.($$renderer);
			$$renderer.push(`<!----></button>`);
		}
		$$renderer.push(`<!--]-->`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region node_modules/bits-ui/dist/internal/floating-svelte/floating-utils.svelte.js
function get(valueOrGetValue) {
	return typeof valueOrGetValue === "function" ? valueOrGetValue() : valueOrGetValue;
}
function getDPR(element) {
	if (typeof window === "undefined") return 1;
	return (element.ownerDocument.defaultView || window).devicePixelRatio || 1;
}
function roundByDPR(element, value) {
	const dpr = getDPR(element);
	return Math.round(value * dpr) / dpr;
}
function getFloatingContentCSSVars(name) {
	return {
		[`--bits-${name}-content-transform-origin`]: `var(--bits-floating-transform-origin)`,
		[`--bits-${name}-content-available-width`]: `var(--bits-floating-available-width)`,
		[`--bits-${name}-content-available-height`]: `var(--bits-floating-available-height)`,
		[`--bits-${name}-anchor-width`]: `var(--bits-floating-anchor-width)`,
		[`--bits-${name}-anchor-height`]: `var(--bits-floating-anchor-height)`
	};
}
//#endregion
//#region node_modules/bits-ui/dist/internal/floating-svelte/use-floating.svelte.js
function useFloating(options) {
	options.whileElementsMounted;
	const openOption = derived(() => get(options.open) ?? true);
	const middlewareOption = derived(() => get(options.middleware));
	const transformOption = derived(() => get(options.transform) ?? true);
	const placementOption = derived(() => get(options.placement) ?? "bottom");
	const strategyOption = derived(() => get(options.strategy) ?? "absolute");
	const sideOffsetOption = derived(() => get(options.sideOffset) ?? 0);
	const alignOffsetOption = derived(() => get(options.alignOffset) ?? 0);
	const reference = options.reference;
	/** State */
	let x = 0;
	let y = 0;
	const floating = simpleBox(null);
	let strategy = strategyOption();
	let placement = placementOption();
	let middlewareData = {};
	let isPositioned = false;
	let updateRequestId = 0;
	const floatingStyles = derived(() => {
		const xVal = floating.current ? roundByDPR(floating.current, x) : x;
		const yVal = floating.current ? roundByDPR(floating.current, y) : y;
		if (transformOption()) return {
			position: strategy,
			left: "0",
			top: "0",
			transform: `translate(${xVal}px, ${yVal}px)`,
			...floating.current && getDPR(floating.current) >= 1.5 && { willChange: "transform" }
		};
		return {
			position: strategy,
			left: `${xVal}px`,
			top: `${yVal}px`
		};
	});
	function update() {
		if (reference.current === null || floating.current === null) return;
		const referenceNode = reference.current;
		const floatingNode = floating.current;
		const requestId = ++updateRequestId;
		computePosition(referenceNode, floatingNode, {
			middleware: middlewareOption(),
			placement: placementOption(),
			strategy: strategyOption()
		}).then((position) => {
			if (requestId !== updateRequestId) return;
			if (reference.current !== referenceNode || floating.current !== floatingNode) return;
			if (isReferenceHidden(referenceNode)) {
				middlewareData = {
					...middlewareData,
					hide: {
						...middlewareData.hide,
						referenceHidden: true
					}
				};
				return;
			}
			if (!openOption() && x !== 0 && y !== 0) {
				const maxExpectedOffset = Math.max(Math.abs(sideOffsetOption()), Math.abs(alignOffsetOption()), 15);
				if (position.x <= maxExpectedOffset && position.y <= maxExpectedOffset) return;
			}
			x = position.x;
			y = position.y;
			strategy = position.strategy;
			placement = position.placement;
			middlewareData = position.middlewareData;
			isPositioned = true;
		});
	}
	return {
		floating,
		reference,
		get strategy() {
			return strategy;
		},
		get placement() {
			return placement;
		},
		get middlewareData() {
			return middlewareData;
		},
		get isPositioned() {
			return isPositioned;
		},
		get floatingStyles() {
			return floatingStyles();
		},
		get update() {
			return update;
		}
	};
}
function isReferenceHidden(node) {
	if (!(node instanceof Element)) return false;
	if (!node.isConnected) return true;
	if (node instanceof HTMLElement && node.hidden) return true;
	return node.getClientRects().length === 0;
}
//#endregion
//#region node_modules/bits-ui/dist/bits/utilities/floating-layer/use-floating-layer.svelte.js
var OPPOSITE_SIDE = {
	top: "bottom",
	right: "left",
	bottom: "top",
	left: "right"
};
var FloatingRootContext = new Context("Floating.Root");
var FloatingContentContext = new Context("Floating.Content");
var FloatingTooltipRootContext = new Context("Floating.Root");
var FloatingRootState = class FloatingRootState {
	static create(tooltip = false) {
		return tooltip ? FloatingTooltipRootContext.set(new FloatingRootState()) : FloatingRootContext.set(new FloatingRootState());
	}
	anchorNode = simpleBox(null);
	customAnchorNode = simpleBox(null);
	triggerNode = simpleBox(null);
	constructor() {}
};
var FloatingContentState = class FloatingContentState {
	static create(opts, tooltip = false) {
		return tooltip ? FloatingContentContext.set(new FloatingContentState(opts, FloatingTooltipRootContext.get())) : FloatingContentContext.set(new FloatingContentState(opts, FloatingRootContext.get()));
	}
	opts;
	root;
	contentRef = simpleBox(null);
	wrapperRef = simpleBox(null);
	arrowRef = simpleBox(null);
	contentAttachment = attachRef(this.contentRef);
	wrapperAttachment = attachRef(this.wrapperRef);
	arrowAttachment = attachRef(this.arrowRef);
	arrowId = simpleBox(useId());
	#transformedStyle = derived(() => {
		if (typeof this.opts.style === "string") return cssToStyleObj(this.opts.style);
		if (!this.opts.style) return {};
	});
	#updatePositionStrategy = void 0;
	#arrowSize = new ElementSize(() => this.arrowRef.current ?? void 0);
	#arrowWidth = derived(() => this.#arrowSize?.width ?? 0);
	#arrowHeight = derived(() => this.#arrowSize?.height ?? 0);
	#desiredPlacement = derived(() => this.opts.side?.current + (this.opts.align.current !== "center" ? `-${this.opts.align.current}` : ""));
	#boundary = derived(() => Array.isArray(this.opts.collisionBoundary.current) ? this.opts.collisionBoundary.current : [this.opts.collisionBoundary.current]);
	#hasExplicitBoundaries = derived(() => this.#boundary().length > 0);
	get hasExplicitBoundaries() {
		return this.#hasExplicitBoundaries();
	}
	set hasExplicitBoundaries($$value) {
		return this.#hasExplicitBoundaries($$value);
	}
	#detectOverflowOptions = derived(() => ({
		padding: this.opts.collisionPadding.current,
		boundary: this.#boundary().filter(isNotNull),
		altBoundary: this.hasExplicitBoundaries
	}));
	get detectOverflowOptions() {
		return this.#detectOverflowOptions();
	}
	set detectOverflowOptions($$value) {
		return this.#detectOverflowOptions($$value);
	}
	#availableWidth = void 0;
	#availableHeight = void 0;
	#anchorWidth = void 0;
	#anchorHeight = void 0;
	#middleware = derived(() => [
		offset({
			mainAxis: this.opts.sideOffset.current + this.#arrowHeight(),
			alignmentAxis: this.opts.alignOffset.current
		}),
		this.opts.avoidCollisions.current && shift({
			mainAxis: true,
			crossAxis: false,
			limiter: this.opts.sticky.current === "partial" ? limitShift() : void 0,
			...this.detectOverflowOptions
		}),
		this.opts.avoidCollisions.current && flip({ ...this.detectOverflowOptions }),
		size({
			...this.detectOverflowOptions,
			apply: ({ rects, availableWidth, availableHeight }) => {
				const { width: anchorWidth, height: anchorHeight } = rects.reference;
				this.#availableWidth = availableWidth;
				this.#availableHeight = availableHeight;
				this.#anchorWidth = anchorWidth;
				this.#anchorHeight = anchorHeight;
			}
		}),
		this.arrowRef.current && arrow({
			element: this.arrowRef.current,
			padding: this.opts.arrowPadding.current
		}),
		transformOrigin({
			arrowWidth: this.#arrowWidth(),
			arrowHeight: this.#arrowHeight()
		}),
		this.opts.hideWhenDetached.current && hide({
			strategy: "referenceHidden",
			...this.detectOverflowOptions
		})
	].filter(Boolean));
	get middleware() {
		return this.#middleware();
	}
	set middleware($$value) {
		return this.#middleware($$value);
	}
	floating;
	#placedSide = derived(() => getSideFromPlacement(this.floating.placement));
	get placedSide() {
		return this.#placedSide();
	}
	set placedSide($$value) {
		return this.#placedSide($$value);
	}
	#placedAlign = derived(() => getAlignFromPlacement(this.floating.placement));
	get placedAlign() {
		return this.#placedAlign();
	}
	set placedAlign($$value) {
		return this.#placedAlign($$value);
	}
	#arrowX = derived(() => this.floating.middlewareData.arrow?.x ?? 0);
	get arrowX() {
		return this.#arrowX();
	}
	set arrowX($$value) {
		return this.#arrowX($$value);
	}
	#arrowY = derived(() => this.floating.middlewareData.arrow?.y ?? 0);
	get arrowY() {
		return this.#arrowY();
	}
	set arrowY($$value) {
		return this.#arrowY($$value);
	}
	#cannotCenterArrow = derived(() => this.floating.middlewareData.arrow?.centerOffset !== 0);
	get cannotCenterArrow() {
		return this.#cannotCenterArrow();
	}
	set cannotCenterArrow($$value) {
		return this.#cannotCenterArrow($$value);
	}
	contentZIndex;
	#arrowBaseSide = derived(() => OPPOSITE_SIDE[this.placedSide]);
	get arrowBaseSide() {
		return this.#arrowBaseSide();
	}
	set arrowBaseSide($$value) {
		return this.#arrowBaseSide($$value);
	}
	#wrapperProps = derived(() => ({
		id: this.opts.wrapperId.current,
		"data-bits-floating-content-wrapper": "",
		style: {
			...this.floating.floatingStyles,
			transform: this.floating.isPositioned ? this.floating.floatingStyles.transform : "translate(0, -200%)",
			minWidth: "max-content",
			zIndex: this.contentZIndex,
			"--bits-floating-transform-origin": `${this.floating.middlewareData.transformOrigin?.x} ${this.floating.middlewareData.transformOrigin?.y}`,
			"--bits-floating-available-width": `${this.#availableWidth}px`,
			"--bits-floating-available-height": `${this.#availableHeight}px`,
			"--bits-floating-anchor-width": `${this.#anchorWidth}px`,
			"--bits-floating-anchor-height": `${this.#anchorHeight}px`,
			...this.floating.middlewareData.hide?.referenceHidden && {
				visibility: "hidden",
				"pointer-events": "none"
			},
			...this.#transformedStyle()
		},
		dir: this.opts.dir.current,
		...this.wrapperAttachment
	}));
	get wrapperProps() {
		return this.#wrapperProps();
	}
	set wrapperProps($$value) {
		return this.#wrapperProps($$value);
	}
	#props = derived(() => ({
		"data-side": this.placedSide,
		"data-align": this.placedAlign,
		style: styleToString({ ...this.#transformedStyle() }),
		...this.contentAttachment
	}));
	get props() {
		return this.#props();
	}
	set props($$value) {
		return this.#props($$value);
	}
	#arrowStyle = derived(() => ({
		position: "absolute",
		left: this.arrowX ? `${this.arrowX}px` : void 0,
		top: this.arrowY ? `${this.arrowY}px` : void 0,
		[this.arrowBaseSide]: 0,
		"transform-origin": {
			top: "",
			right: "0 0",
			bottom: "center 0",
			left: "100% 0"
		}[this.placedSide],
		transform: {
			top: "translateY(100%)",
			right: "translateY(50%) rotate(90deg) translateX(-50%)",
			bottom: "rotate(180deg)",
			left: "translateY(50%) rotate(-90deg) translateX(50%)"
		}[this.placedSide],
		visibility: this.cannotCenterArrow ? "hidden" : void 0
	}));
	get arrowStyle() {
		return this.#arrowStyle();
	}
	set arrowStyle($$value) {
		return this.#arrowStyle($$value);
	}
	constructor(opts, root) {
		this.opts = opts;
		this.root = root;
		this.#updatePositionStrategy = opts.updatePositionStrategy;
		if (opts.customAnchor) this.root.customAnchorNode.current = opts.customAnchor.current;
		watch(() => opts.customAnchor.current, (customAnchor) => {
			this.root.customAnchorNode.current = customAnchor;
		});
		this.floating = useFloating({
			strategy: () => this.opts.strategy.current,
			placement: () => this.#desiredPlacement(),
			middleware: () => this.middleware,
			reference: this.root.anchorNode,
			whileElementsMounted: (...args) => {
				return autoUpdate(...args, { animationFrame: this.#updatePositionStrategy?.current === "always" });
			},
			open: () => this.opts.enabled.current,
			sideOffset: () => this.opts.sideOffset.current,
			alignOffset: () => this.opts.alignOffset.current
		});
		watch(() => this.contentRef.current, (contentNode) => {
			if (!contentNode || !this.opts.enabled.current) return;
			const win = getWindow(contentNode);
			const rafId = win.requestAnimationFrame(() => {
				if (this.contentRef.current !== contentNode || !this.opts.enabled.current) return;
				const zIndex = win.getComputedStyle(contentNode).zIndex;
				if (zIndex !== this.contentZIndex) this.contentZIndex = zIndex;
			});
			return () => {
				win.cancelAnimationFrame(rafId);
			};
		});
	}
};
var FloatingArrowState = class FloatingArrowState {
	static create(opts) {
		return new FloatingArrowState(opts, FloatingContentContext.get());
	}
	opts;
	content;
	constructor(opts, content) {
		this.opts = opts;
		this.content = content;
	}
	#props = derived(() => ({
		id: this.opts.id.current,
		style: this.content.arrowStyle,
		"data-side": this.content.placedSide,
		...this.content.arrowAttachment
	}));
	get props() {
		return this.#props();
	}
	set props($$value) {
		return this.#props($$value);
	}
};
var FloatingAnchorState = class FloatingAnchorState {
	static create(opts, tooltip = false) {
		return tooltip ? new FloatingAnchorState(opts, FloatingTooltipRootContext.get()) : new FloatingAnchorState(opts, FloatingRootContext.get());
	}
	opts;
	root;
	constructor(opts, root) {
		this.opts = opts;
		this.root = root;
		if (opts.virtualEl && opts.virtualEl.current) root.triggerNode = boxFrom(opts.virtualEl.current);
		else root.triggerNode = opts.ref;
	}
};
function transformOrigin(options) {
	return {
		name: "transformOrigin",
		options,
		fn(data) {
			const { placement, rects, middlewareData } = data;
			const isArrowHidden = middlewareData.arrow?.centerOffset !== 0;
			const arrowWidth = isArrowHidden ? 0 : options.arrowWidth;
			const arrowHeight = isArrowHidden ? 0 : options.arrowHeight;
			const [placedSide, placedAlign] = getSideAndAlignFromPlacement(placement);
			const noArrowAlign = {
				start: "0%",
				center: "50%",
				end: "100%"
			}[placedAlign];
			const arrowXCenter = (middlewareData.arrow?.x ?? 0) + arrowWidth / 2;
			const arrowYCenter = (middlewareData.arrow?.y ?? 0) + arrowHeight / 2;
			let x = "";
			let y = "";
			if (placedSide === "bottom") {
				x = isArrowHidden ? noArrowAlign : `${arrowXCenter}px`;
				y = `${-arrowHeight}px`;
			} else if (placedSide === "top") {
				x = isArrowHidden ? noArrowAlign : `${arrowXCenter}px`;
				y = `${rects.floating.height + arrowHeight}px`;
			} else if (placedSide === "right") {
				x = `${-arrowHeight}px`;
				y = isArrowHidden ? noArrowAlign : `${arrowYCenter}px`;
			} else if (placedSide === "left") {
				x = `${rects.floating.width + arrowHeight}px`;
				y = isArrowHidden ? noArrowAlign : `${arrowYCenter}px`;
			}
			return { data: {
				x,
				y
			} };
		}
	};
}
function getSideAndAlignFromPlacement(placement) {
	const [side, align = "center"] = placement.split("-");
	return [side, align];
}
function getSideFromPlacement(placement) {
	return getSideAndAlignFromPlacement(placement)[0];
}
function getAlignFromPlacement(placement) {
	return getSideAndAlignFromPlacement(placement)[1];
}
//#endregion
//#region node_modules/bits-ui/dist/bits/utilities/floating-layer/components/floating-layer.svelte
function Floating_layer($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { children, tooltip = false } = $$props;
		FloatingRootState.create(tooltip);
		children?.($$renderer);
		$$renderer.push(`<!---->`);
	});
}
//#endregion
//#region node_modules/bits-ui/dist/internal/data-typeahead.svelte.js
var DataTypeahead = class {
	#opts;
	#candidateValues = derived(() => this.#opts.candidateValues());
	#search;
	constructor(opts) {
		this.#opts = opts;
		this.#search = boxAutoReset("", {
			afterMs: 1e3,
			getWindow: this.#opts.getWindow
		});
		this.handleTypeaheadSearch = this.handleTypeaheadSearch.bind(this);
		this.resetTypeahead = this.resetTypeahead.bind(this);
	}
	handleTypeaheadSearch(key) {
		if (!this.#opts.enabled() || !this.#candidateValues().length) return;
		this.#search.current = this.#search.current + key;
		const currentItem = this.#opts.getCurrentItem();
		const currentMatch = this.#candidateValues().find((item) => item === currentItem) ?? "";
		const nextMatch = getNextMatch(this.#candidateValues().map((item) => item ?? ""), this.#search.current, currentMatch);
		const newItem = this.#candidateValues().find((item) => item === nextMatch);
		if (newItem) this.#opts.onMatch(newItem);
		return newItem;
	}
	resetTypeahead() {
		this.#search.current = "";
	}
};
var FIRST_KEYS = [
	ARROW_DOWN,
	PAGE_UP,
	HOME
];
var LAST_KEYS = [
	ARROW_UP,
	PAGE_DOWN,
	"End"
];
var FIRST_LAST_KEYS = [...FIRST_KEYS, ...LAST_KEYS];
var selectAttrs = createBitsAttrs({
	component: "select",
	parts: [
		"trigger",
		"content",
		"item",
		"viewport",
		"scroll-up-button",
		"scroll-down-button",
		"group",
		"group-label",
		"separator",
		"arrow",
		"input",
		"content-wrapper",
		"item-text",
		"value"
	]
});
var SelectRootContext = new Context("Select.Root | Combobox.Root");
var SelectGroupContext = new Context("Select.Group | Combobox.Group");
var SelectContentContext = new Context("Select.Content | Combobox.Content");
var SelectBaseRootState = class {
	opts;
	touchedInput = false;
	inputNode = null;
	contentNode = null;
	contentPresence;
	viewportNode = null;
	triggerNode = null;
	valueNode = null;
	valueId = "";
	highlightedNode = null;
	#highlightedValue = derived(() => {
		if (!this.highlightedNode) return null;
		return this.highlightedNode.getAttribute("data-value");
	});
	get highlightedValue() {
		return this.#highlightedValue();
	}
	set highlightedValue($$value) {
		return this.#highlightedValue($$value);
	}
	#highlightedId = derived(() => {
		if (!this.highlightedNode) return void 0;
		return this.highlightedNode.id;
	});
	get highlightedId() {
		return this.#highlightedId();
	}
	set highlightedId($$value) {
		return this.#highlightedId($$value);
	}
	#highlightedLabel = derived(() => {
		if (!this.highlightedNode) return null;
		return this.highlightedNode.getAttribute("data-label");
	});
	get highlightedLabel() {
		return this.#highlightedLabel();
	}
	set highlightedLabel($$value) {
		return this.#highlightedLabel($$value);
	}
	contentIsPositioned = false;
	isUsingKeyboard = false;
	isCombobox = false;
	domContext = new DOMContext(() => null);
	constructor(opts) {
		this.opts = opts;
		this.isCombobox = opts.isCombobox;
		this.contentPresence = new PresenceManager({
			ref: boxWith(() => this.contentNode),
			open: this.opts.open,
			onComplete: () => {
				this.opts.onOpenChangeComplete.current(this.opts.open.current);
			}
		});
	}
	setHighlightedNode(node, initial = false) {
		this.highlightedNode = node;
		if (node && (this.isUsingKeyboard || initial)) this.scrollHighlightedNodeIntoView(node);
	}
	scrollHighlightedNodeIntoView(node) {
		if (!this.viewportNode || !this.contentIsPositioned) return;
		node.scrollIntoView({ block: this.opts.scrollAlignment.current });
	}
	getCandidateNodes() {
		const node = this.contentNode;
		if (!node) return [];
		return Array.from(node.querySelectorAll(`[${this.getBitsAttr("item")}]:not([data-disabled])`));
	}
	setHighlightedToFirstCandidate(initial = false) {
		this.setHighlightedNode(null);
		let nodes = this.getCandidateNodes();
		if (!nodes.length) return;
		if (this.viewportNode) {
			const viewportRect = this.viewportNode.getBoundingClientRect();
			nodes = nodes.filter((node) => {
				if (!this.viewportNode) return false;
				const nodeRect = node.getBoundingClientRect();
				return nodeRect.right <= viewportRect.right && nodeRect.left >= viewportRect.left && nodeRect.bottom <= viewportRect.bottom && nodeRect.top >= viewportRect.top;
			});
		}
		this.setHighlightedNode(nodes[0], initial);
	}
	getNodeByValue(value) {
		return this.getCandidateNodes().find((node) => node.dataset.value === value) ?? null;
	}
	/**
	* Resolves the display label for a value: `items` entry when present, otherwise the
	* mounted item's `data-label` or its text content.
	*/
	getLabelForValue(value) {
		if (value === "") return "";
		const fromItems = this.opts.items.current.find((item) => item.value === value)?.label;
		if (fromItems !== void 0) return fromItems;
		const node = this.getNodeByValue(value);
		if (node) {
			const dataLabel = node.getAttribute("data-label");
			if (dataLabel !== null && dataLabel !== "") return dataLabel;
			return node.textContent?.trim() ?? value;
		}
		return value;
	}
	setOpen(open) {
		this.opts.open.current = open;
	}
	toggleOpen() {
		this.opts.open.current = !this.opts.open.current;
	}
	handleOpen() {
		this.setOpen(true);
	}
	handleClose() {
		this.setHighlightedNode(null);
		this.setOpen(false);
	}
	toggleMenu() {
		this.toggleOpen();
	}
	getBitsAttr = (part) => {
		return selectAttrs.getAttr(part, this.isCombobox ? "combobox" : void 0);
	};
};
var SelectSingleRootState = class extends SelectBaseRootState {
	opts;
	isMulti = false;
	#hasValue = derived(() => this.opts.value.current !== "");
	get hasValue() {
		return this.#hasValue();
	}
	set hasValue($$value) {
		return this.#hasValue($$value);
	}
	#currentLabel = derived(() => {
		if (!this.opts.items.current.length) return "";
		return this.opts.items.current.find((item) => item.value === this.opts.value.current)?.label ?? "";
	});
	get currentLabel() {
		return this.#currentLabel();
	}
	set currentLabel($$value) {
		return this.#currentLabel($$value);
	}
	#candidateLabels = derived(() => {
		if (!this.opts.items.current.length) return [];
		return this.opts.items.current.filter((item) => !item.disabled).map((item) => item.label);
	});
	get candidateLabels() {
		return this.#candidateLabels();
	}
	set candidateLabels($$value) {
		return this.#candidateLabels($$value);
	}
	#dataTypeaheadEnabled = derived(() => {
		if (this.isMulti) return false;
		if (this.opts.items.current.length === 0) return false;
		return true;
	});
	get dataTypeaheadEnabled() {
		return this.#dataTypeaheadEnabled();
	}
	set dataTypeaheadEnabled($$value) {
		return this.#dataTypeaheadEnabled($$value);
	}
	constructor(opts) {
		super(opts);
		this.opts = opts;
		watch(() => this.opts.open.current, () => {
			if (!this.opts.open.current) return;
			this.setInitialHighlightedNode();
		});
	}
	includesItem(itemValue) {
		return this.opts.value.current === itemValue;
	}
	toggleItem(itemValue, itemLabel = itemValue) {
		const newValue = this.includesItem(itemValue) ? "" : itemValue;
		this.opts.value.current = newValue;
		if (newValue !== "") this.opts.inputValue.current = itemLabel;
	}
	setInitialHighlightedNode() {
		afterTick(() => {
			if (this.highlightedNode && this.domContext.getDocument().contains(this.highlightedNode)) return;
			if (this.opts.value.current !== "") {
				const node = this.getNodeByValue(this.opts.value.current);
				if (node) {
					this.setHighlightedNode(node, true);
					return;
				}
			}
			this.setHighlightedToFirstCandidate(true);
		});
	}
};
var SelectMultipleRootState = class extends SelectBaseRootState {
	opts;
	isMulti = true;
	#hasValue = derived(() => this.opts.value.current.length > 0);
	get hasValue() {
		return this.#hasValue();
	}
	set hasValue($$value) {
		return this.#hasValue($$value);
	}
	constructor(opts) {
		super(opts);
		this.opts = opts;
		watch(() => this.opts.open.current, () => {
			if (!this.opts.open.current) return;
			this.setInitialHighlightedNode();
		});
	}
	includesItem(itemValue) {
		return this.opts.value.current.includes(itemValue);
	}
	toggleItem(itemValue, itemLabel = itemValue) {
		if (this.includesItem(itemValue)) this.opts.value.current = this.opts.value.current.filter((v) => v !== itemValue);
		else this.opts.value.current = [...this.opts.value.current, itemValue];
		this.opts.inputValue.current = itemLabel;
	}
	setInitialHighlightedNode() {
		afterTick(() => {
			if (!this.domContext) return;
			if (this.highlightedNode && this.domContext.getDocument().contains(this.highlightedNode)) return;
			if (this.opts.value.current.length && this.opts.value.current[0] !== "") {
				const node = this.getNodeByValue(this.opts.value.current[0]);
				if (node) {
					this.setHighlightedNode(node, true);
					return;
				}
			}
			this.setHighlightedToFirstCandidate(true);
		});
	}
};
var SelectRootState = class {
	static create(props) {
		const { type, ...rest } = props;
		const rootState = type === "single" ? new SelectSingleRootState(rest) : new SelectMultipleRootState(rest);
		return SelectRootContext.set(rootState);
	}
};
var SelectTriggerState = class SelectTriggerState {
	static create(opts) {
		return new SelectTriggerState(opts, SelectRootContext.get());
	}
	opts;
	root;
	attachment;
	#domTypeahead;
	#dataTypeahead;
	constructor(opts, root) {
		this.opts = opts;
		this.root = root;
		this.attachment = attachRef(opts.ref, (v) => this.root.triggerNode = v);
		this.root.domContext = new DOMContext(opts.ref);
		this.#domTypeahead = new DOMTypeahead({
			getCurrentItem: () => this.root.highlightedNode,
			onMatch: (node) => {
				this.root.setHighlightedNode(node);
			},
			getActiveElement: () => this.root.domContext.getActiveElement(),
			getWindow: () => this.root.domContext.getWindow()
		});
		this.#dataTypeahead = new DataTypeahead({
			getCurrentItem: () => {
				if (this.root.isMulti) return "";
				return this.root.currentLabel;
			},
			onMatch: (label) => {
				if (this.root.isMulti) return;
				if (!this.root.opts.items.current) return;
				const matchedItem = this.root.opts.items.current.find((item) => item.label === label);
				if (!matchedItem) return;
				this.root.opts.value.current = matchedItem.value;
			},
			enabled: () => !this.root.isMulti && this.root.dataTypeaheadEnabled,
			candidateValues: () => this.root.isMulti ? [] : this.root.candidateLabels,
			getWindow: () => this.root.domContext.getWindow()
		});
		this.onkeydown = this.onkeydown.bind(this);
		this.onpointerdown = this.onpointerdown.bind(this);
		this.onpointerup = this.onpointerup.bind(this);
		this.onclick = this.onclick.bind(this);
	}
	#handleOpen() {
		this.root.opts.open.current = true;
		this.#dataTypeahead.resetTypeahead();
		this.#domTypeahead.resetTypeahead();
	}
	#handlePointerOpen(_) {
		this.#handleOpen();
	}
	/**
	* Logic used to handle keyboard selection/deselection.
	*
	* If it returns true, it means the item was selected and whatever is calling
	* this function should return early
	*
	*/
	#handleKeyboardSelection() {
		const isCurrentSelectedValue = this.root.highlightedValue === this.root.opts.value.current;
		if (!this.root.opts.allowDeselect.current && isCurrentSelectedValue && !this.root.isMulti) {
			this.root.handleClose();
			return true;
		}
		if (this.root.highlightedValue !== null) this.root.toggleItem(this.root.highlightedValue, this.root.highlightedLabel ?? void 0);
		if (!this.root.isMulti && !isCurrentSelectedValue) {
			this.root.handleClose();
			return true;
		}
		return false;
	}
	onkeydown(e) {
		this.root.isUsingKeyboard = true;
		if (e.key === "ArrowUp" || e.key === "ArrowDown") e.preventDefault();
		if (!this.root.opts.open.current) {
			if (e.key === "Enter" || e.key === " " || e.key === "ArrowDown" || e.key === "ArrowUp") {
				e.preventDefault();
				this.root.handleOpen();
			} else if (!this.root.isMulti && this.root.dataTypeaheadEnabled) {
				this.#dataTypeahead.handleTypeaheadSearch(e.key);
				return;
			}
			if (this.root.hasValue) return;
			const candidateNodes = this.root.getCandidateNodes();
			if (!candidateNodes.length) return;
			if (e.key === "ArrowDown") {
				const firstCandidate = candidateNodes[0];
				this.root.setHighlightedNode(firstCandidate);
			} else if (e.key === "ArrowUp") {
				const lastCandidate = candidateNodes[candidateNodes.length - 1];
				this.root.setHighlightedNode(lastCandidate);
			}
			return;
		}
		if (e.key === "Tab") {
			this.root.handleClose();
			return;
		}
		if ((e.key === "Enter" || e.key === " " && this.#domTypeahead.search === "") && !e.isComposing) {
			e.preventDefault();
			if (this.#handleKeyboardSelection()) return;
		}
		if (e.key === "ArrowUp" && e.altKey) this.root.handleClose();
		if (FIRST_LAST_KEYS.includes(e.key)) {
			e.preventDefault();
			const candidateNodes = this.root.getCandidateNodes();
			const currHighlightedNode = this.root.highlightedNode;
			const currIndex = currHighlightedNode ? candidateNodes.indexOf(currHighlightedNode) : -1;
			const loop = this.root.opts.loop.current;
			let nextItem;
			if (e.key === "ArrowDown") nextItem = next(candidateNodes, currIndex, loop);
			else if (e.key === "ArrowUp") nextItem = prev(candidateNodes, currIndex, loop);
			else if (e.key === "PageDown") nextItem = forward(candidateNodes, currIndex, 10, loop);
			else if (e.key === "PageUp") nextItem = backward(candidateNodes, currIndex, 10, loop);
			else if (e.key === "Home") nextItem = candidateNodes[0];
			else if (e.key === "End") nextItem = candidateNodes[candidateNodes.length - 1];
			if (!nextItem) return;
			this.root.setHighlightedNode(nextItem);
			return;
		}
		const isModifierKey = e.ctrlKey || e.altKey || e.metaKey;
		const isCharacterKey = e.key.length === 1;
		const isSpaceKey = e.key === " ";
		const candidateNodes = this.root.getCandidateNodes();
		if (e.key === "Tab") return;
		if (!isModifierKey && (isCharacterKey || isSpaceKey)) {
			if (!this.#domTypeahead.handleTypeaheadSearch(e.key, candidateNodes) && isSpaceKey) {
				e.preventDefault();
				this.#handleKeyboardSelection();
			}
			return;
		}
		if (!this.root.highlightedNode) this.root.setHighlightedToFirstCandidate();
	}
	onclick(e) {
		e.currentTarget.focus();
	}
	onpointerdown(e) {
		if (this.root.opts.disabled.current) return;
		if (e.pointerType === "touch") return e.preventDefault();
		const target = e.target;
		if (target?.hasPointerCapture(e.pointerId)) target?.releasePointerCapture(e.pointerId);
		if (e.button === 0 && e.ctrlKey === false) {
			if (this.root.opts.open.current === false) this.#handlePointerOpen(e);
			else this.root.handleClose();
		}
	}
	onpointerup(e) {
		if (this.root.opts.disabled.current) return;
		e.preventDefault();
		if (e.pointerType === "touch") {
			if (this.root.opts.open.current === false) this.#handlePointerOpen(e);
			else this.root.handleClose();
		}
	}
	#props = derived(() => ({
		id: this.opts.id.current,
		disabled: this.root.opts.disabled.current ? true : void 0,
		"aria-haspopup": "listbox",
		"aria-expanded": boolToStr(this.root.opts.open.current),
		"aria-activedescendant": this.root.highlightedId,
		"data-state": getDataOpenClosed(this.root.opts.open.current),
		"data-disabled": boolToEmptyStrOrUndef(this.root.opts.disabled.current),
		"data-placeholder": this.root.hasValue ? void 0 : "",
		[this.root.getBitsAttr("trigger")]: "",
		onpointerdown: this.onpointerdown,
		onkeydown: this.onkeydown,
		onclick: this.onclick,
		onpointerup: this.onpointerup,
		...this.attachment
	}));
	get props() {
		return this.#props();
	}
	set props($$value) {
		return this.#props($$value);
	}
};
var SelectContentState = class SelectContentState {
	static create(opts) {
		return SelectContentContext.set(new SelectContentState(opts, SelectRootContext.get()));
	}
	opts;
	root;
	attachment;
	isPositioned = false;
	domContext;
	constructor(opts, root) {
		this.opts = opts;
		this.root = root;
		this.attachment = attachRef(opts.ref, (v) => this.root.contentNode = v);
		this.domContext = new DOMContext(this.opts.ref);
		if (this.root.domContext === null) this.root.domContext = this.domContext;
		watch(() => this.root.opts.open.current, () => {
			if (this.root.opts.open.current) return;
			this.root.contentIsPositioned = false;
			this.isPositioned = false;
		});
		watch([() => this.isPositioned, () => this.root.highlightedNode], () => {
			if (!this.isPositioned || !this.root.highlightedNode) return;
			this.root.scrollHighlightedNodeIntoView(this.root.highlightedNode);
		});
		this.onpointermove = this.onpointermove.bind(this);
	}
	onpointermove(_) {
		this.root.isUsingKeyboard = false;
	}
	#styles = derived(() => {
		return getFloatingContentCSSVars(this.root.isCombobox ? "combobox" : "select");
	});
	onInteractOutside = (e) => {
		if (e.target === this.root.triggerNode || e.target === this.root.inputNode) {
			e.preventDefault();
			return;
		}
		this.opts.onInteractOutside.current(e);
		if (e.defaultPrevented) return;
		this.root.handleClose();
	};
	onEscapeKeydown = (e) => {
		this.opts.onEscapeKeydown.current(e);
		if (e.defaultPrevented) return;
		this.root.handleClose();
	};
	onOpenAutoFocus = (e) => {
		e.preventDefault();
	};
	onCloseAutoFocus = (e) => {
		e.preventDefault();
	};
	get shouldRender() {
		return this.root.contentPresence.shouldRender;
	}
	#snippetProps = derived(() => ({ open: this.root.opts.open.current }));
	get snippetProps() {
		return this.#snippetProps();
	}
	set snippetProps($$value) {
		return this.#snippetProps($$value);
	}
	#props = derived(() => ({
		id: this.opts.id.current,
		role: "listbox",
		"aria-multiselectable": this.root.isMulti ? "true" : void 0,
		"data-state": getDataOpenClosed(this.root.opts.open.current),
		...getDataTransitionAttrs(this.root.contentPresence.transitionStatus),
		[this.root.getBitsAttr("content")]: "",
		style: {
			display: "flex",
			flexDirection: "column",
			outline: "none",
			boxSizing: "border-box",
			pointerEvents: "auto",
			...this.#styles()
		},
		onpointermove: this.onpointermove,
		...this.attachment
	}));
	get props() {
		return this.#props();
	}
	set props($$value) {
		return this.#props($$value);
	}
	popperProps = {
		onInteractOutside: this.onInteractOutside,
		onEscapeKeydown: this.onEscapeKeydown,
		onOpenAutoFocus: this.onOpenAutoFocus,
		onCloseAutoFocus: this.onCloseAutoFocus,
		trapFocus: false,
		loop: false,
		onPlaced: () => {
			if (this.root.opts.open.current) {
				this.root.contentIsPositioned = true;
				this.isPositioned = true;
			}
		}
	};
};
var SelectItemState = class SelectItemState {
	static create(opts) {
		return new SelectItemState(opts, SelectRootContext.get());
	}
	opts;
	root;
	attachment;
	#isSelected = derived(() => this.root.includesItem(this.opts.value.current));
	get isSelected() {
		return this.#isSelected();
	}
	set isSelected($$value) {
		return this.#isSelected($$value);
	}
	#isHighlighted = derived(() => this.root.highlightedValue === this.opts.value.current);
	get isHighlighted() {
		return this.#isHighlighted();
	}
	set isHighlighted($$value) {
		return this.#isHighlighted($$value);
	}
	prevHighlighted = new Previous(() => this.isHighlighted);
	mounted = false;
	constructor(opts, root) {
		this.opts = opts;
		this.root = root;
		this.attachment = attachRef(opts.ref);
		watch([() => this.isHighlighted, () => this.prevHighlighted.current], () => {
			if (this.isHighlighted) this.opts.onHighlight.current();
			else if (this.prevHighlighted.current) this.opts.onUnhighlight.current();
		});
		watch(() => this.mounted, () => {
			if (!this.mounted) return;
			this.root.setInitialHighlightedNode();
		});
		this.onpointerdown = this.onpointerdown.bind(this);
		this.onpointerup = this.onpointerup.bind(this);
		this.onpointermove = this.onpointermove.bind(this);
	}
	handleSelect() {
		if (this.opts.disabled.current) return;
		const isCurrentSelectedValue = this.opts.value.current === this.root.opts.value.current;
		if (!this.root.opts.allowDeselect.current && isCurrentSelectedValue && !this.root.isMulti) {
			this.root.handleClose();
			return;
		}
		this.root.toggleItem(this.opts.value.current, this.opts.label.current);
		if (!this.root.isMulti && !isCurrentSelectedValue) this.root.handleClose();
	}
	#snippetProps = derived(() => ({
		selected: this.isSelected,
		highlighted: this.isHighlighted
	}));
	get snippetProps() {
		return this.#snippetProps();
	}
	set snippetProps($$value) {
		return this.#snippetProps($$value);
	}
	onpointerdown(e) {
		e.preventDefault();
	}
	/**
	* Using `pointerup` instead of `click` allows power users to pointerdown
	* the trigger, then release pointerup on an item to select it vs having to do
	* multiple clicks.
	*/
	onpointerup(e) {
		if (e.defaultPrevented || !this.opts.ref.current) return;
		/**
		* For one reason or another, when it's a touch pointer and _not_ on IOS,
		* we need to listen for the immediate click event to handle the selection,
		* otherwise a click event will fire on the element _behind_ the item.
		*/
		if (e.pointerType === "touch" && !isIOS) {
			on(this.opts.ref.current, "click", () => {
				this.handleSelect();
				this.root.setHighlightedNode(this.opts.ref.current);
			}, { once: true });
			return;
		}
		e.preventDefault();
		this.handleSelect();
		if (e.pointerType === "touch") this.root.setHighlightedNode(this.opts.ref.current);
	}
	onpointermove(e) {
		/**
		* We don't want to highlight items on touch devices when scrolling,
		* as this is confusing behavior, so we return here and instead handle
		* the highlighting on the `pointerup` (or following `click`) event for
		* touch devices only.
		*/
		if (e.pointerType === "touch") return;
		if (this.root.highlightedNode !== this.opts.ref.current) this.root.setHighlightedNode(this.opts.ref.current);
	}
	#props = derived(() => ({
		id: this.opts.id.current,
		role: "option",
		"aria-selected": this.root.includesItem(this.opts.value.current) ? "true" : void 0,
		"data-value": this.opts.value.current,
		"data-disabled": boolToEmptyStrOrUndef(this.opts.disabled.current),
		"data-highlighted": this.root.highlightedValue === this.opts.value.current && !this.opts.disabled.current ? "" : void 0,
		"data-selected": this.root.includesItem(this.opts.value.current) ? "" : void 0,
		"data-label": this.opts.label.current,
		[this.root.getBitsAttr("item")]: "",
		onpointermove: this.onpointermove,
		onpointerdown: this.onpointerdown,
		onpointerup: this.onpointerup,
		...this.attachment
	}));
	get props() {
		return this.#props();
	}
	set props($$value) {
		return this.#props($$value);
	}
};
var SelectGroupState = class SelectGroupState {
	static create(opts) {
		return SelectGroupContext.set(new SelectGroupState(opts, SelectRootContext.get()));
	}
	opts;
	root;
	labelNode = null;
	attachment;
	constructor(opts, root) {
		this.opts = opts;
		this.root = root;
		this.attachment = attachRef(opts.ref);
	}
	#props = derived(() => ({
		id: this.opts.id.current,
		role: "group",
		[this.root.getBitsAttr("group")]: "",
		"aria-labelledby": this.labelNode?.id ?? void 0,
		...this.attachment
	}));
	get props() {
		return this.#props();
	}
	set props($$value) {
		return this.#props($$value);
	}
};
var SelectHiddenInputState = class SelectHiddenInputState {
	static create(opts) {
		return new SelectHiddenInputState(opts, SelectRootContext.get());
	}
	opts;
	root;
	#shouldRender = derived(() => this.root.opts.name.current !== "");
	get shouldRender() {
		return this.#shouldRender();
	}
	set shouldRender($$value) {
		return this.#shouldRender($$value);
	}
	constructor(opts, root) {
		this.opts = opts;
		this.root = root;
		this.onfocus = this.onfocus.bind(this);
	}
	onfocus(e) {
		e.preventDefault();
		if (!this.root.isCombobox) this.root.triggerNode?.focus();
		else this.root.inputNode?.focus();
	}
	#props = derived(() => ({
		disabled: boolToTrueOrUndef(this.root.opts.disabled.current),
		required: boolToTrueOrUndef(this.root.opts.required.current),
		name: this.root.opts.name.current,
		value: this.opts.value.current,
		onfocus: this.onfocus
	}));
	get props() {
		return this.#props();
	}
	set props($$value) {
		return this.#props($$value);
	}
};
var SelectViewportState = class SelectViewportState {
	static create(opts) {
		return new SelectViewportState(opts, SelectContentContext.get());
	}
	opts;
	content;
	root;
	attachment;
	prevScrollTop = 0;
	constructor(opts, content) {
		this.opts = opts;
		this.content = content;
		this.root = content.root;
		this.attachment = attachRef(opts.ref, (v) => {
			this.root.viewportNode = v;
		});
	}
	#props = derived(() => ({
		id: this.opts.id.current,
		role: "presentation",
		[this.root.getBitsAttr("viewport")]: "",
		style: {
			position: "relative",
			flex: 1,
			overflow: "auto"
		},
		...this.attachment
	}));
	get props() {
		return this.#props();
	}
	set props($$value) {
		return this.#props($$value);
	}
};
var SelectScrollButtonImplState = class {
	opts;
	content;
	root;
	attachment;
	autoScrollTimer = null;
	userScrollTimer = -1;
	isUserScrolling = false;
	onAutoScroll = noop;
	mounted = false;
	constructor(opts, content) {
		this.opts = opts;
		this.content = content;
		this.root = content.root;
		this.attachment = attachRef(opts.ref);
		watch([() => this.mounted], () => {
			if (!this.mounted) {
				this.isUserScrolling = false;
				return;
			}
			if (this.isUserScrolling) return;
		});
		this.onpointerdown = this.onpointerdown.bind(this);
		this.onpointermove = this.onpointermove.bind(this);
		this.onpointerleave = this.onpointerleave.bind(this);
	}
	handleUserScroll() {
		this.content.domContext.clearTimeout(this.userScrollTimer);
		this.isUserScrolling = true;
		this.userScrollTimer = this.content.domContext.setTimeout(() => {
			this.isUserScrolling = false;
		}, 200);
	}
	clearAutoScrollInterval() {
		if (this.autoScrollTimer === null) return;
		this.content.domContext.clearTimeout(this.autoScrollTimer);
		this.autoScrollTimer = null;
	}
	onpointerdown(_) {
		if (this.autoScrollTimer !== null) return;
		const autoScroll = (tick) => {
			this.onAutoScroll();
			this.autoScrollTimer = this.content.domContext.setTimeout(() => autoScroll(tick + 1), this.opts.delay.current(tick));
		};
		this.autoScrollTimer = this.content.domContext.setTimeout(() => autoScroll(1), this.opts.delay.current(0));
	}
	onpointermove(e) {
		this.onpointerdown(e);
	}
	onpointerleave(_) {
		this.clearAutoScrollInterval();
	}
	#props = derived(() => ({
		id: this.opts.id.current,
		"aria-hidden": boolToStrTrueOrUndef(true),
		style: { flexShrink: 0 },
		onpointerdown: this.onpointerdown,
		onpointermove: this.onpointermove,
		onpointerleave: this.onpointerleave,
		...this.attachment
	}));
	get props() {
		return this.#props();
	}
	set props($$value) {
		return this.#props($$value);
	}
};
var SelectScrollDownButtonState = class SelectScrollDownButtonState {
	static create(opts) {
		return new SelectScrollDownButtonState(new SelectScrollButtonImplState(opts, SelectContentContext.get()));
	}
	scrollButtonState;
	content;
	root;
	canScrollDown = false;
	scrollIntoViewTimer = null;
	constructor(scrollButtonState) {
		this.scrollButtonState = scrollButtonState;
		this.content = scrollButtonState.content;
		this.root = scrollButtonState.root;
		this.scrollButtonState.onAutoScroll = this.handleAutoScroll;
		watch([() => this.root.viewportNode, () => this.content.isPositioned], () => {
			if (!this.root.viewportNode || !this.content.isPositioned) return;
			this.handleScroll(true);
			return on(this.root.viewportNode, "scroll", () => this.handleScroll());
		});
		/**
		* If the input value changes, this means that the filtered items may have changed,
		* so we need to re-evaluate the scroll-ability of the list.
		*/
		watch([
			() => this.root.opts.inputValue.current,
			() => this.root.viewportNode,
			() => this.content.isPositioned
		], () => {
			if (!this.root.viewportNode || !this.content.isPositioned) return;
			this.handleScroll(true);
		});
		watch(() => this.scrollButtonState.mounted, () => {
			if (!this.scrollButtonState.mounted) return;
			if (this.scrollIntoViewTimer) clearTimeout(this.scrollIntoViewTimer);
			this.scrollIntoViewTimer = afterSleep(5, () => {
				const activeItem = this.root.highlightedNode;
				if (!activeItem) return;
				this.root.scrollHighlightedNodeIntoView(activeItem);
			});
		});
	}
	/**
	* @param manual - if true, it means the function was invoked manually outside of an event
	* listener, so we don't call `handleUserScroll` to prevent the auto scroll from kicking in.
	*/
	handleScroll = (manual = false) => {
		if (!manual) this.scrollButtonState.handleUserScroll();
		if (!this.root.viewportNode) return;
		const maxScroll = this.root.viewportNode.scrollHeight - this.root.viewportNode.clientHeight;
		const paddingTop = Number.parseInt(getComputedStyle(this.root.viewportNode).paddingTop, 10);
		this.canScrollDown = Math.ceil(this.root.viewportNode.scrollTop) < maxScroll - paddingTop;
	};
	handleAutoScroll = () => {
		const viewport = this.root.viewportNode;
		const selectedItem = this.root.highlightedNode;
		if (!viewport || !selectedItem) return;
		viewport.scrollTop = viewport.scrollTop + selectedItem.offsetHeight;
	};
	#props = derived(() => ({
		...this.scrollButtonState.props,
		[this.root.getBitsAttr("scroll-down-button")]: ""
	}));
	get props() {
		return this.#props();
	}
	set props($$value) {
		return this.#props($$value);
	}
};
var SelectScrollUpButtonState = class SelectScrollUpButtonState {
	static create(opts) {
		return new SelectScrollUpButtonState(new SelectScrollButtonImplState(opts, SelectContentContext.get()));
	}
	scrollButtonState;
	content;
	root;
	canScrollUp = false;
	constructor(scrollButtonState) {
		this.scrollButtonState = scrollButtonState;
		this.content = scrollButtonState.content;
		this.root = scrollButtonState.root;
		this.scrollButtonState.onAutoScroll = this.handleAutoScroll;
		watch([() => this.root.viewportNode, () => this.content.isPositioned], () => {
			if (!this.root.viewportNode || !this.content.isPositioned) return;
			this.handleScroll(true);
			return on(this.root.viewportNode, "scroll", () => this.handleScroll());
		});
	}
	/**
	* @param manual - if true, it means the function was invoked manually outside of an event
	* listener, so we don't call `handleUserScroll` to prevent the auto scroll from kicking in.
	*/
	handleScroll = (manual = false) => {
		if (!manual) this.scrollButtonState.handleUserScroll();
		if (!this.root.viewportNode) return;
		const paddingTop = Number.parseInt(getComputedStyle(this.root.viewportNode).paddingTop, 10);
		this.canScrollUp = this.root.viewportNode.scrollTop - paddingTop > .1;
	};
	handleAutoScroll = () => {
		if (!this.root.viewportNode || !this.root.highlightedNode) return;
		this.root.viewportNode.scrollTop = this.root.viewportNode.scrollTop - this.root.highlightedNode.offsetHeight;
	};
	#props = derived(() => ({
		...this.scrollButtonState.props,
		[this.root.getBitsAttr("scroll-up-button")]: ""
	}));
	get props() {
		return this.#props();
	}
	set props($$value) {
		return this.#props($$value);
	}
};
//#endregion
//#region node_modules/bits-ui/dist/bits/select/components/select-hidden-input.svelte
function Select_hidden_input($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { value = void 0, autocomplete } = $$props;
		const hiddenInputState = SelectHiddenInputState.create({ value: boxWith(() => value) });
		let $$settled = true;
		let $$inner_renderer;
		function $$render_inner($$renderer) {
			if (hiddenInputState.shouldRender) {
				$$renderer.push("<!--[0-->");
				Hidden_input($$renderer, spread_props([hiddenInputState.props, {
					autocomplete,
					get value() {
						return value;
					},
					set value($$value) {
						value = $$value;
						$$settled = false;
					}
				}]));
			} else $$renderer.push("<!--[-1-->");
			$$renderer.push(`<!--]-->`);
		}
		do {
			$$settled = true;
			$$inner_renderer = $$renderer.copy();
			$$render_inner($$inner_renderer);
		} while (!$$settled);
		$$renderer.subsume($$inner_renderer);
		bind_props($$props, { value });
	});
}
//#endregion
//#region node_modules/bits-ui/dist/bits/utilities/floating-layer/components/floating-layer-anchor.svelte
function Floating_layer_anchor($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { id, children, virtualEl, ref, tooltip = false } = $$props;
		FloatingAnchorState.create({
			id: boxWith(() => id),
			virtualEl: boxWith(() => virtualEl),
			ref
		}, tooltip);
		children?.($$renderer);
		$$renderer.push(`<!---->`);
	});
}
//#endregion
//#region node_modules/bits-ui/dist/bits/utilities/arrow/arrow.svelte
function Arrow($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { id = useId(), children, child, width = 10, height = 5, $$slots, $$events, ...restProps } = $$props;
		const mergedProps = derived(() => mergeProps(restProps, { id }));
		if (child) {
			$$renderer.push("<!--[0-->");
			child($$renderer, { props: mergedProps() });
			$$renderer.push(`<!---->`);
		} else {
			$$renderer.push("<!--[-1-->");
			$$renderer.push(`<span${attributes({ ...mergedProps() })}>`);
			if (children) {
				$$renderer.push("<!--[0-->");
				children?.($$renderer);
				$$renderer.push(`<!---->`);
			} else {
				$$renderer.push("<!--[-1-->");
				$$renderer.push(`<svg${attr("width", width)}${attr("height", height)} viewBox="0 0 30 10" preserveAspectRatio="none" data-arrow=""><polygon points="0,0 30,0 15,10" fill="currentColor"></polygon></svg>`);
			}
			$$renderer.push(`<!--]--></span>`);
		}
		$$renderer.push(`<!--]-->`);
	});
}
//#endregion
//#region node_modules/bits-ui/dist/bits/utilities/floating-layer/components/floating-layer-arrow.svelte
function Floating_layer_arrow($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { id = useId(), ref = null, $$slots, $$events, ...restProps } = $$props;
		const arrowState = FloatingArrowState.create({
			id: boxWith(() => id),
			ref: boxWith(() => ref, (v) => ref = v)
		});
		const mergedProps = derived(() => mergeProps(restProps, arrowState.props));
		Arrow($$renderer, spread_props([mergedProps()]));
		bind_props($$props, { ref });
	});
}
//#endregion
//#region node_modules/bits-ui/dist/bits/utilities/floating-layer/components/floating-layer-content.svelte
function Floating_layer_content($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { content, side = "bottom", sideOffset = 0, align = "center", alignOffset = 0, id, arrowPadding = 0, avoidCollisions = true, collisionBoundary = [], collisionPadding = 0, hideWhenDetached = false, onPlaced = () => {}, sticky = "partial", updatePositionStrategy = "optimized", strategy = "fixed", dir = "ltr", style = {}, wrapperId = useId(), customAnchor = null, enabled, tooltip = false } = $$props;
		const contentState = FloatingContentState.create({
			side: boxWith(() => side),
			sideOffset: boxWith(() => sideOffset),
			align: boxWith(() => align),
			alignOffset: boxWith(() => alignOffset),
			id: boxWith(() => id),
			arrowPadding: boxWith(() => arrowPadding),
			avoidCollisions: boxWith(() => avoidCollisions),
			collisionBoundary: boxWith(() => collisionBoundary),
			collisionPadding: boxWith(() => collisionPadding),
			hideWhenDetached: boxWith(() => hideWhenDetached),
			onPlaced: boxWith(() => onPlaced),
			sticky: boxWith(() => sticky),
			updatePositionStrategy: boxWith(() => updatePositionStrategy),
			strategy: boxWith(() => strategy),
			dir: boxWith(() => dir),
			style: boxWith(() => style),
			enabled: boxWith(() => enabled),
			wrapperId: boxWith(() => wrapperId),
			customAnchor: boxWith(() => customAnchor)
		}, tooltip);
		const mergedProps = derived(() => mergeProps(contentState.wrapperProps, { style: { pointerEvents: "auto" } }));
		content?.($$renderer, {
			props: contentState.props,
			wrapperProps: mergedProps()
		});
		$$renderer.push(`<!---->`);
	});
}
//#endregion
//#region node_modules/bits-ui/dist/bits/utilities/floating-layer/components/floating-layer-content-static.svelte
function Floating_layer_content_static($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { content, onPlaced } = $$props;
		content?.($$renderer, {
			props: {},
			wrapperProps: {}
		});
		$$renderer.push(`<!---->`);
	});
}
//#endregion
//#region node_modules/bits-ui/dist/bits/separator/separator.svelte.js
var separatorAttrs = createBitsAttrs({
	component: "separator",
	parts: ["root"]
});
var SeparatorRootState = class SeparatorRootState {
	static create(opts) {
		return new SeparatorRootState(opts);
	}
	opts;
	attachment;
	constructor(opts) {
		this.opts = opts;
		this.attachment = attachRef(opts.ref);
	}
	#props = derived(() => ({
		id: this.opts.id.current,
		role: this.opts.decorative.current ? "none" : "separator",
		"aria-orientation": this.opts.orientation.current,
		"aria-hidden": boolToStrTrueOrUndef(this.opts.decorative.current),
		"data-orientation": this.opts.orientation.current,
		[separatorAttrs.root]: "",
		...this.attachment
	}));
	get props() {
		return this.#props();
	}
	set props($$value) {
		return this.#props($$value);
	}
};
//#endregion
//#region node_modules/bits-ui/dist/bits/separator/components/separator.svelte
function Separator$1($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		const uid = props_id($$renderer);
		let { id = createId(uid), ref = null, child, children, decorative = false, orientation = "horizontal", $$slots, $$events, ...restProps } = $$props;
		const rootState = SeparatorRootState.create({
			ref: boxWith(() => ref, (v) => ref = v),
			id: boxWith(() => id),
			decorative: boxWith(() => decorative),
			orientation: boxWith(() => orientation)
		});
		const mergedProps = derived(() => mergeProps(restProps, rootState.props));
		if (child) {
			$$renderer.push("<!--[0-->");
			child($$renderer, { props: mergedProps() });
			$$renderer.push(`<!---->`);
		} else {
			$$renderer.push("<!--[-1-->");
			$$renderer.push(`<div${attributes({ ...mergedProps() })}>`);
			children?.($$renderer);
			$$renderer.push(`<!----></div>`);
		}
		$$renderer.push(`<!--]-->`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region node_modules/bits-ui/dist/bits/utilities/popper-layer/popper-content.svelte
function Popper_content($$renderer, $$props) {
	let { content, isStatic = false, onPlaced, $$slots, $$events, ...restProps } = $$props;
	if (isStatic) {
		$$renderer.push("<!--[0-->");
		Floating_layer_content_static($$renderer, {
			content,
			onPlaced
		});
	} else {
		$$renderer.push("<!--[-1-->");
		Floating_layer_content($$renderer, spread_props([{
			content,
			onPlaced
		}, restProps]));
	}
	$$renderer.push(`<!--]-->`);
}
//#endregion
//#region node_modules/bits-ui/dist/bits/utilities/popper-layer/popper-layer-inner.svelte
function Popper_layer_inner($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { popper, onEscapeKeydown, escapeKeydownBehavior, preventOverflowTextSelection, id, onPointerDown, onPointerUp, side, sideOffset, align, alignOffset, arrowPadding, avoidCollisions, collisionBoundary, collisionPadding, sticky, hideWhenDetached, updatePositionStrategy, strategy, dir, preventScroll, wrapperId, style, onPlaced, onInteractOutside, onCloseAutoFocus, onOpenAutoFocus, onFocusOutside, interactOutsideBehavior = "close", loop, trapFocus = true, isValidEvent = () => false, customAnchor = null, isStatic = false, enabled, ref, tooltip = false, contentPointerEvents = "auto", $$slots, $$events, ...restProps } = $$props;
		const resolvedPreventScroll = derived(() => preventScroll ?? true);
		const effectiveStrategy = derived(() => strategy ?? (resolvedPreventScroll() ? "fixed" : "absolute"));
		{
			function content($$renderer, { props: floatingProps, wrapperProps }) {
				if (restProps.forceMount && enabled) {
					$$renderer.push("<!--[0-->");
					Scroll_lock($$renderer, { preventScroll: resolvedPreventScroll() });
				} else if (!restProps.forceMount) {
					$$renderer.push("<!--[1-->");
					Scroll_lock($$renderer, { preventScroll: resolvedPreventScroll() });
				} else $$renderer.push("<!--[-1-->");
				$$renderer.push(`<!--]--> `);
				{
					function focusScope($$renderer, { props: focusScopeProps }) {
						Escape_layer($$renderer, {
							onEscapeKeydown,
							escapeKeydownBehavior,
							enabled,
							ref,
							children: ($$renderer) => {
								{
									function children($$renderer, { props: dismissibleProps }) {
										Text_selection_layer($$renderer, {
											id,
											preventOverflowTextSelection,
											onPointerDown,
											onPointerUp,
											enabled,
											ref,
											children: ($$renderer) => {
												popper?.($$renderer, {
													props: mergeProps(restProps, floatingProps, dismissibleProps, focusScopeProps, { style: { pointerEvents: contentPointerEvents } }),
													wrapperProps
												});
												$$renderer.push(`<!---->`);
											},
											$$slots: { default: true }
										});
									}
									Dismissible_layer($$renderer, {
										id,
										onInteractOutside,
										onFocusOutside,
										interactOutsideBehavior,
										isValidEvent,
										enabled,
										ref,
										children,
										$$slots: { default: true }
									});
								}
							},
							$$slots: { default: true }
						});
					}
					Focus_scope($$renderer, {
						onOpenAutoFocus,
						onCloseAutoFocus,
						loop,
						enabled,
						trapFocus,
						forceMount: restProps.forceMount,
						ref,
						focusScope,
						$$slots: { focusScope: true }
					});
				}
				$$renderer.push(`<!---->`);
			}
			Popper_content($$renderer, {
				isStatic,
				id,
				side,
				sideOffset,
				align,
				alignOffset,
				arrowPadding,
				avoidCollisions,
				collisionBoundary,
				collisionPadding,
				sticky,
				hideWhenDetached,
				updatePositionStrategy,
				strategy: effectiveStrategy(),
				dir,
				wrapperId,
				style,
				onPlaced,
				customAnchor,
				enabled,
				tooltip,
				content,
				$$slots: { content: true }
			});
		}
	});
}
//#endregion
//#region node_modules/bits-ui/dist/bits/utilities/popper-layer/popper-layer.svelte
function Popper_layer($$renderer, $$props) {
	let { popper, open, onEscapeKeydown, escapeKeydownBehavior, preventOverflowTextSelection, id, onPointerDown, onPointerUp, side, sideOffset, align, alignOffset, arrowPadding, avoidCollisions, collisionBoundary, collisionPadding, sticky, hideWhenDetached, updatePositionStrategy, strategy, dir, preventScroll, wrapperId, style, onPlaced, onInteractOutside, onCloseAutoFocus, onOpenAutoFocus, onFocusOutside, interactOutsideBehavior = "close", loop, trapFocus = true, isValidEvent = () => false, customAnchor = null, isStatic = false, ref, shouldRender, $$slots, $$events, ...restProps } = $$props;
	if (shouldRender) {
		$$renderer.push("<!--[0-->");
		Popper_layer_inner($$renderer, spread_props([{
			popper,
			onEscapeKeydown,
			escapeKeydownBehavior,
			preventOverflowTextSelection,
			id,
			onPointerDown,
			onPointerUp,
			side,
			sideOffset,
			align,
			alignOffset,
			arrowPadding,
			avoidCollisions,
			collisionBoundary,
			collisionPadding,
			sticky,
			hideWhenDetached,
			updatePositionStrategy,
			strategy,
			dir,
			preventScroll,
			wrapperId,
			style,
			onPlaced,
			customAnchor,
			isStatic,
			enabled: open,
			onInteractOutside,
			onCloseAutoFocus,
			onOpenAutoFocus,
			interactOutsideBehavior,
			loop,
			trapFocus,
			isValidEvent,
			onFocusOutside,
			forceMount: false,
			ref
		}, restProps]));
	} else $$renderer.push("<!--[-1-->");
	$$renderer.push(`<!--]-->`);
}
//#endregion
//#region node_modules/bits-ui/dist/bits/utilities/popper-layer/popper-layer-force-mount.svelte
function Popper_layer_force_mount($$renderer, $$props) {
	let { popper, onEscapeKeydown, escapeKeydownBehavior, preventOverflowTextSelection, id, onPointerDown, onPointerUp, side, sideOffset, align, alignOffset, arrowPadding, avoidCollisions, collisionBoundary, collisionPadding, sticky, hideWhenDetached, updatePositionStrategy, strategy, dir, preventScroll, wrapperId, style, onPlaced, onInteractOutside, onCloseAutoFocus, onOpenAutoFocus, onFocusOutside, interactOutsideBehavior = "close", loop, trapFocus = true, isValidEvent = () => false, customAnchor = null, isStatic = false, enabled, $$slots, $$events, ...restProps } = $$props;
	Popper_layer_inner($$renderer, spread_props([
		{
			popper,
			onEscapeKeydown,
			escapeKeydownBehavior,
			preventOverflowTextSelection,
			id,
			onPointerDown,
			onPointerUp,
			side,
			sideOffset,
			align,
			alignOffset,
			arrowPadding,
			avoidCollisions,
			collisionBoundary,
			collisionPadding,
			sticky,
			hideWhenDetached,
			updatePositionStrategy,
			strategy,
			dir,
			preventScroll,
			wrapperId,
			style,
			onPlaced,
			customAnchor,
			isStatic,
			enabled,
			onInteractOutside,
			onCloseAutoFocus,
			onOpenAutoFocus,
			interactOutsideBehavior,
			loop,
			trapFocus,
			isValidEvent,
			onFocusOutside
		},
		restProps,
		{ forceMount: true }
	]));
}
//#endregion
//#region node_modules/bits-ui/dist/bits/select/components/select-content.svelte
function Select_content$1($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		const uid = props_id($$renderer);
		let { id = createId(uid), ref = null, forceMount = false, side = "bottom", onInteractOutside = noop, onEscapeKeydown = noop, children, child, preventScroll = false, style, $$slots, $$events, ...restProps } = $$props;
		const contentState = SelectContentState.create({
			id: boxWith(() => id),
			ref: boxWith(() => ref, (v) => ref = v),
			onInteractOutside: boxWith(() => onInteractOutside),
			onEscapeKeydown: boxWith(() => onEscapeKeydown)
		});
		const mergedProps = derived(() => mergeProps(restProps, contentState.props));
		if (forceMount) {
			$$renderer.push("<!--[0-->");
			{
				function popper($$renderer, { props, wrapperProps }) {
					const finalProps = mergeProps(props, { style: contentState.props.style }, { style });
					if (child) {
						$$renderer.push("<!--[0-->");
						child($$renderer, {
							props: finalProps,
							wrapperProps,
							...contentState.snippetProps
						});
						$$renderer.push(`<!---->`);
					} else {
						$$renderer.push("<!--[-1-->");
						$$renderer.push(`<div${attributes({ ...wrapperProps })}><div${attributes({ ...finalProps })}>`);
						children?.($$renderer);
						$$renderer.push(`<!----></div></div>`);
					}
					$$renderer.push(`<!--]-->`);
				}
				Popper_layer_force_mount($$renderer, spread_props([
					mergedProps(),
					contentState.popperProps,
					{
						ref: contentState.opts.ref,
						side,
						enabled: contentState.root.opts.open.current,
						id,
						preventScroll,
						forceMount: true,
						shouldRender: contentState.shouldRender,
						popper,
						$$slots: { popper: true }
					}
				]));
			}
		} else if (!forceMount) {
			$$renderer.push("<!--[1-->");
			{
				function popper($$renderer, { props, wrapperProps }) {
					const finalProps = mergeProps(props, { style: contentState.props.style }, { style });
					if (child) {
						$$renderer.push("<!--[0-->");
						child($$renderer, {
							props: finalProps,
							wrapperProps,
							...contentState.snippetProps
						});
						$$renderer.push(`<!---->`);
					} else {
						$$renderer.push("<!--[-1-->");
						$$renderer.push(`<div${attributes({ ...wrapperProps })}><div${attributes({ ...finalProps })}>`);
						children?.($$renderer);
						$$renderer.push(`<!----></div></div>`);
					}
					$$renderer.push(`<!--]-->`);
				}
				Popper_layer($$renderer, spread_props([
					mergedProps(),
					contentState.popperProps,
					{
						ref: contentState.opts.ref,
						side,
						open: contentState.root.opts.open.current,
						id,
						preventScroll,
						forceMount: false,
						shouldRender: contentState.shouldRender,
						popper,
						$$slots: { popper: true }
					}
				]));
			}
		} else $$renderer.push("<!--[-1-->");
		$$renderer.push(`<!--]-->`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region node_modules/bits-ui/dist/bits/utilities/mounted.svelte
function Mounted($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { mounted = false, onMountedChange = noop } = $$props;
		bind_props($$props, { mounted });
	});
}
//#endregion
//#region node_modules/bits-ui/dist/bits/select/components/select-item.svelte
function Select_item$1($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		const uid = props_id($$renderer);
		let { id = createId(uid), ref = null, value, label = value, disabled = false, children, child, onHighlight = noop, onUnhighlight = noop, $$slots, $$events, ...restProps } = $$props;
		const itemState = SelectItemState.create({
			id: boxWith(() => id),
			ref: boxWith(() => ref, (v) => ref = v),
			value: boxWith(() => value),
			disabled: boxWith(() => disabled),
			label: boxWith(() => label),
			onHighlight: boxWith(() => onHighlight),
			onUnhighlight: boxWith(() => onUnhighlight)
		});
		const mergedProps = derived(() => mergeProps(restProps, itemState.props));
		let $$settled = true;
		let $$inner_renderer;
		function $$render_inner($$renderer) {
			if (child) {
				$$renderer.push("<!--[0-->");
				child($$renderer, {
					props: mergedProps(),
					...itemState.snippetProps
				});
				$$renderer.push(`<!---->`);
			} else {
				$$renderer.push("<!--[-1-->");
				$$renderer.push(`<div${attributes({ ...mergedProps() })}>`);
				children?.($$renderer, itemState.snippetProps);
				$$renderer.push(`<!----></div>`);
			}
			$$renderer.push(`<!--]--> `);
			Mounted($$renderer, {
				get mounted() {
					return itemState.mounted;
				},
				set mounted($$value) {
					itemState.mounted = $$value;
					$$settled = false;
				}
			});
			$$renderer.push(`<!---->`);
		}
		do {
			$$settled = true;
			$$inner_renderer = $$renderer.copy();
			$$render_inner($$inner_renderer);
		} while (!$$settled);
		$$renderer.subsume($$inner_renderer);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region node_modules/bits-ui/dist/bits/select/components/select-group.svelte
function Select_group$1($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		const uid = props_id($$renderer);
		let { id = createId(uid), ref = null, children, child, $$slots, $$events, ...restProps } = $$props;
		const groupState = SelectGroupState.create({
			id: boxWith(() => id),
			ref: boxWith(() => ref, (v) => ref = v)
		});
		const mergedProps = derived(() => mergeProps(restProps, groupState.props));
		if (child) {
			$$renderer.push("<!--[0-->");
			child($$renderer, { props: mergedProps() });
			$$renderer.push(`<!---->`);
		} else {
			$$renderer.push("<!--[-1-->");
			$$renderer.push(`<div${attributes({ ...mergedProps() })}>`);
			children?.($$renderer);
			$$renderer.push(`<!----></div>`);
		}
		$$renderer.push(`<!--]-->`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region node_modules/bits-ui/dist/bits/select/components/select-viewport.svelte
function Select_viewport($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		const uid = props_id($$renderer);
		let { id = createId(uid), ref = null, children, child, $$slots, $$events, ...restProps } = $$props;
		const viewportState = SelectViewportState.create({
			id: boxWith(() => id),
			ref: boxWith(() => ref, (v) => ref = v)
		});
		const mergedProps = derived(() => mergeProps(restProps, viewportState.props));
		if (child) {
			$$renderer.push("<!--[0-->");
			child($$renderer, { props: mergedProps() });
			$$renderer.push(`<!---->`);
		} else {
			$$renderer.push("<!--[-1-->");
			$$renderer.push(`<div${attributes({ ...mergedProps() })}>`);
			children?.($$renderer);
			$$renderer.push(`<!----></div>`);
		}
		$$renderer.push(`<!--]-->`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region node_modules/bits-ui/dist/bits/select/components/select-scroll-down-button.svelte
function Select_scroll_down_button$1($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		const uid = props_id($$renderer);
		let { id = createId(uid), ref = null, delay = () => 50, child, children, $$slots, $$events, ...restProps } = $$props;
		const scrollButtonState = SelectScrollDownButtonState.create({
			id: boxWith(() => id),
			ref: boxWith(() => ref, (v) => ref = v),
			delay: boxWith(() => delay)
		});
		const mergedProps = derived(() => mergeProps(restProps, scrollButtonState.props));
		let $$settled = true;
		let $$inner_renderer;
		function $$render_inner($$renderer) {
			if (scrollButtonState.canScrollDown) {
				$$renderer.push("<!--[0-->");
				Mounted($$renderer, {
					get mounted() {
						return scrollButtonState.scrollButtonState.mounted;
					},
					set mounted($$value) {
						scrollButtonState.scrollButtonState.mounted = $$value;
						$$settled = false;
					}
				});
				$$renderer.push(`<!----> `);
				if (child) {
					$$renderer.push("<!--[0-->");
					child($$renderer, { props: restProps });
					$$renderer.push(`<!---->`);
				} else {
					$$renderer.push("<!--[-1-->");
					$$renderer.push(`<div${attributes({ ...mergedProps() })}>`);
					children?.($$renderer);
					$$renderer.push(`<!----></div>`);
				}
				$$renderer.push(`<!--]-->`);
			} else $$renderer.push("<!--[-1-->");
			$$renderer.push(`<!--]-->`);
		}
		do {
			$$settled = true;
			$$inner_renderer = $$renderer.copy();
			$$render_inner($$inner_renderer);
		} while (!$$settled);
		$$renderer.subsume($$inner_renderer);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region node_modules/bits-ui/dist/bits/select/components/select-scroll-up-button.svelte
function Select_scroll_up_button$1($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		const uid = props_id($$renderer);
		let { id = createId(uid), ref = null, delay = () => 50, child, children, $$slots, $$events, ...restProps } = $$props;
		const scrollButtonState = SelectScrollUpButtonState.create({
			id: boxWith(() => id),
			ref: boxWith(() => ref, (v) => ref = v),
			delay: boxWith(() => delay)
		});
		const mergedProps = derived(() => mergeProps(restProps, scrollButtonState.props));
		let $$settled = true;
		let $$inner_renderer;
		function $$render_inner($$renderer) {
			if (scrollButtonState.canScrollUp) {
				$$renderer.push("<!--[0-->");
				Mounted($$renderer, {
					get mounted() {
						return scrollButtonState.scrollButtonState.mounted;
					},
					set mounted($$value) {
						scrollButtonState.scrollButtonState.mounted = $$value;
						$$settled = false;
					}
				});
				$$renderer.push(`<!----> `);
				if (child) {
					$$renderer.push("<!--[0-->");
					child($$renderer, { props: restProps });
					$$renderer.push(`<!---->`);
				} else {
					$$renderer.push("<!--[-1-->");
					$$renderer.push(`<div${attributes({ ...mergedProps() })}>`);
					children?.($$renderer);
					$$renderer.push(`<!----></div>`);
				}
				$$renderer.push(`<!--]-->`);
			} else $$renderer.push("<!--[-1-->");
			$$renderer.push(`<!--]-->`);
		}
		do {
			$$settled = true;
			$$inner_renderer = $$renderer.copy();
			$$render_inner($$inner_renderer);
		} while (!$$settled);
		$$renderer.subsume($$inner_renderer);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region node_modules/bits-ui/dist/bits/menu/components/menu-sub.svelte
function Menu_sub($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { open = false, onOpenChange = noop, onOpenChangeComplete = noop, children } = $$props;
		MenuSubmenuState.create({
			open: boxWith(() => open, (v) => {
				open = v;
				onOpenChange?.(v);
			}),
			onOpenChangeComplete: boxWith(() => onOpenChangeComplete)
		});
		Floating_layer($$renderer, {
			children: ($$renderer) => {
				children?.($$renderer);
				$$renderer.push(`<!---->`);
			},
			$$slots: { default: true }
		});
		bind_props($$props, { open });
	});
}
//#endregion
//#region node_modules/bits-ui/dist/bits/menu/components/menu-item.svelte
function Menu_item($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		const uid = props_id($$renderer);
		let { child, children, ref = null, id = createId(uid), disabled = false, onSelect = noop, closeOnSelect = true, $$slots, $$events, ...restProps } = $$props;
		const itemState = MenuItemState.create({
			id: boxWith(() => id),
			disabled: boxWith(() => disabled),
			onSelect: boxWith(() => onSelect),
			ref: boxWith(() => ref, (v) => ref = v),
			closeOnSelect: boxWith(() => closeOnSelect)
		});
		const mergedProps = derived(() => mergeProps(restProps, itemState.props));
		if (child) {
			$$renderer.push("<!--[0-->");
			child($$renderer, { props: mergedProps() });
			$$renderer.push(`<!---->`);
		} else {
			$$renderer.push("<!--[-1-->");
			$$renderer.push(`<div${attributes({ ...mergedProps() })}>`);
			children?.($$renderer);
			$$renderer.push(`<!----></div>`);
		}
		$$renderer.push(`<!--]-->`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region node_modules/bits-ui/dist/bits/menu/components/menu-group.svelte
function Menu_group($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		const uid = props_id($$renderer);
		let { children, child, ref = null, id = createId(uid), $$slots, $$events, ...restProps } = $$props;
		const groupState = MenuGroupState.create({
			id: boxWith(() => id),
			ref: boxWith(() => ref, (v) => ref = v)
		});
		const mergedProps = derived(() => mergeProps(restProps, groupState.props));
		if (child) {
			$$renderer.push("<!--[0-->");
			child($$renderer, { props: mergedProps() });
			$$renderer.push(`<!---->`);
		} else {
			$$renderer.push("<!--[-1-->");
			$$renderer.push(`<div${attributes({ ...mergedProps() })}>`);
			children?.($$renderer);
			$$renderer.push(`<!----></div>`);
		}
		$$renderer.push(`<!--]-->`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region node_modules/bits-ui/dist/bits/menu/components/menu-radio-item.svelte
function Menu_radio_item($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		const uid = props_id($$renderer);
		let { children, child, ref = null, value, onSelect = noop, id = createId(uid), disabled = false, closeOnSelect = true, $$slots, $$events, ...restProps } = $$props;
		const radioItemState = MenuRadioItemState.create({
			value: boxWith(() => value),
			id: boxWith(() => id),
			disabled: boxWith(() => disabled),
			onSelect: boxWith(() => handleSelect),
			ref: boxWith(() => ref, (v) => ref = v),
			closeOnSelect: boxWith(() => closeOnSelect)
		});
		function handleSelect(e) {
			onSelect(e);
			if (e.defaultPrevented) return;
			radioItemState.selectValue();
		}
		const mergedProps = derived(() => mergeProps(restProps, radioItemState.props));
		if (child) {
			$$renderer.push("<!--[0-->");
			child($$renderer, {
				props: mergedProps(),
				checked: radioItemState.isChecked
			});
			$$renderer.push(`<!---->`);
		} else {
			$$renderer.push("<!--[-1-->");
			$$renderer.push(`<div${attributes({ ...mergedProps() })}>`);
			children?.($$renderer, { checked: radioItemState.isChecked });
			$$renderer.push(`<!----></div>`);
		}
		$$renderer.push(`<!--]-->`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region node_modules/bits-ui/dist/bits/menu/components/menu-separator.svelte
function Menu_separator($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		const uid = props_id($$renderer);
		let { ref = null, id = createId(uid), child, children, $$slots, $$events, ...restProps } = $$props;
		const separatorState = MenuSeparatorState.create({
			id: boxWith(() => id),
			ref: boxWith(() => ref, (v) => ref = v)
		});
		const mergedProps = derived(() => mergeProps(restProps, separatorState.props));
		if (child) {
			$$renderer.push("<!--[0-->");
			child($$renderer, { props: mergedProps() });
			$$renderer.push(`<!---->`);
		} else {
			$$renderer.push("<!--[-1-->");
			$$renderer.push(`<div${attributes({ ...mergedProps() })}>`);
			children?.($$renderer);
			$$renderer.push(`<!----></div>`);
		}
		$$renderer.push(`<!--]-->`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region node_modules/bits-ui/dist/bits/menu/components/menu-radio-group.svelte
function Menu_radio_group($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		const uid = props_id($$renderer);
		let { id = createId(uid), children, child, ref = null, value = "", onValueChange = noop, $$slots, $$events, ...restProps } = $$props;
		const radioGroupState = MenuRadioGroupState.create({
			value: boxWith(() => value, (v) => {
				value = v;
				onValueChange(v);
			}),
			ref: boxWith(() => ref, (v) => ref = v),
			id: boxWith(() => id)
		});
		const mergedProps = derived(() => mergeProps(restProps, radioGroupState.props));
		if (child) {
			$$renderer.push("<!--[0-->");
			child($$renderer, { props: mergedProps() });
			$$renderer.push(`<!---->`);
		} else {
			$$renderer.push("<!--[-1-->");
			$$renderer.push(`<div${attributes({ ...mergedProps() })}>`);
			children?.($$renderer);
			$$renderer.push(`<!----></div>`);
		}
		$$renderer.push(`<!--]-->`);
		bind_props($$props, {
			ref,
			value
		});
	});
}
//#endregion
//#region node_modules/bits-ui/dist/bits/menu/components/menu-sub-content.svelte
function Menu_sub_content($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		const uid = props_id($$renderer);
		let { id = createId(uid), ref = null, children, child, loop = true, onInteractOutside = noop, forceMount = false, onEscapeKeydown = noop, interactOutsideBehavior = "defer-otherwise-close", escapeKeydownBehavior = "defer-otherwise-close", onOpenAutoFocus: onOpenAutoFocusProp = noop, onCloseAutoFocus: onCloseAutoFocusProp = noop, onFocusOutside = noop, side = "right", trapFocus = false, style, $$slots, $$events, ...restProps } = $$props;
		const subContentState = MenuContentState.create({
			id: boxWith(() => id),
			loop: boxWith(() => loop),
			ref: boxWith(() => ref, (v) => ref = v),
			isSub: true,
			onCloseAutoFocus: boxWith(() => handleCloseAutoFocus)
		});
		function onkeydown(e) {
			const isKeyDownInside = e.currentTarget.contains(e.target);
			const isCloseKey = SUB_CLOSE_KEYS[subContentState.parentMenu.root.opts.dir.current].includes(e.key);
			if (isKeyDownInside && isCloseKey) {
				subContentState.parentMenu.onClose();
				subContentState.parentMenu.triggerNode?.focus();
				e.preventDefault();
			}
		}
		const dataAttr = derived(() => subContentState.parentMenu.root.getBitsAttr("sub-content"));
		const mergedProps = derived(() => mergeProps(restProps, subContentState.props, {
			side,
			onkeydown,
			[dataAttr()]: ""
		}));
		function handleOpenAutoFocus(e) {
			onOpenAutoFocusProp(e);
			if (e.defaultPrevented) return;
			e.preventDefault();
			if (subContentState.parentMenu.root.isUsingKeyboard && subContentState.parentMenu.contentNode) MenuOpenEvent.dispatch(subContentState.parentMenu.contentNode);
		}
		function handleCloseAutoFocus(e) {
			onCloseAutoFocusProp(e);
			if (e.defaultPrevented) return;
			e.preventDefault();
		}
		function handleInteractOutside(e) {
			onInteractOutside(e);
			if (e.defaultPrevented) return;
			subContentState.parentMenu.onClose();
		}
		function handleEscapeKeydown(e) {
			onEscapeKeydown(e);
			if (e.defaultPrevented) return;
			subContentState.parentMenu.onClose();
		}
		function handleOnFocusOutside(e) {
			onFocusOutside(e);
			if (e.defaultPrevented) return;
			if (!isHTMLElement(e.target)) return;
			if (e.target.id === subContentState.parentMenu.triggerNode?.id) return;
			if ((subContentState.parentMenu.parentMenu?.contentNode)?.contains(e.target)) {
				subContentState.parentMenu.onClose();
				e.preventDefault();
				return;
			}
			const subContentSelector = `[${subContentState.parentMenu.root.getBitsAttr("sub-content")}]`;
			if (e.target.closest(subContentSelector)) {
				e.preventDefault();
				return;
			}
			subContentState.parentMenu.onClose();
		}
		if (forceMount) {
			$$renderer.push("<!--[0-->");
			{
				function popper($$renderer, { props, wrapperProps }) {
					const finalProps = mergeProps(props, mergedProps(), { style: getFloatingContentCSSVars("menu") }, { style });
					if (child) {
						$$renderer.push("<!--[0-->");
						child($$renderer, {
							props: finalProps,
							wrapperProps,
							...subContentState.snippetProps
						});
						$$renderer.push(`<!---->`);
					} else {
						$$renderer.push("<!--[-1-->");
						$$renderer.push(`<div${attributes({ ...wrapperProps })}><div${attributes({ ...finalProps })}>`);
						children?.($$renderer);
						$$renderer.push(`<!----></div></div>`);
					}
					$$renderer.push(`<!--]-->`);
				}
				Popper_layer_force_mount($$renderer, spread_props([mergedProps(), {
					ref: subContentState.opts.ref,
					interactOutsideBehavior,
					escapeKeydownBehavior,
					onOpenAutoFocus: handleOpenAutoFocus,
					enabled: subContentState.parentMenu.opts.open.current,
					onInteractOutside: handleInteractOutside,
					onEscapeKeydown: handleEscapeKeydown,
					onFocusOutside: handleOnFocusOutside,
					preventScroll: false,
					loop,
					trapFocus,
					shouldRender: subContentState.shouldRender,
					popper,
					$$slots: { popper: true }
				}]));
			}
		} else if (!forceMount) {
			$$renderer.push("<!--[1-->");
			{
				function popper($$renderer, { props, wrapperProps }) {
					const finalProps = mergeProps(props, mergedProps(), { style: getFloatingContentCSSVars("menu") }, { style });
					if (child) {
						$$renderer.push("<!--[0-->");
						child($$renderer, {
							props: finalProps,
							wrapperProps,
							...subContentState.snippetProps
						});
						$$renderer.push(`<!---->`);
					} else {
						$$renderer.push("<!--[-1-->");
						$$renderer.push(`<div${attributes({ ...wrapperProps })}><div${attributes({ ...finalProps })}>`);
						children?.($$renderer);
						$$renderer.push(`<!----></div></div>`);
					}
					$$renderer.push(`<!--]-->`);
				}
				Popper_layer($$renderer, spread_props([mergedProps(), {
					ref: subContentState.opts.ref,
					interactOutsideBehavior,
					escapeKeydownBehavior,
					onCloseAutoFocus: handleCloseAutoFocus,
					onOpenAutoFocus: handleOpenAutoFocus,
					open: subContentState.parentMenu.opts.open.current,
					onInteractOutside: handleInteractOutside,
					onEscapeKeydown: handleEscapeKeydown,
					onFocusOutside: handleOnFocusOutside,
					preventScroll: false,
					loop,
					trapFocus,
					shouldRender: subContentState.shouldRender,
					popper,
					$$slots: { popper: true }
				}]));
			}
		} else $$renderer.push("<!--[-1-->");
		$$renderer.push(`<!--]-->`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region node_modules/bits-ui/dist/bits/menu/components/menu-sub-trigger.svelte
function Menu_sub_trigger($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		const uid = props_id($$renderer);
		let { id = createId(uid), disabled = false, ref = null, children, child, onSelect = noop, openDelay = 0, $$slots, $$events, ...restProps } = $$props;
		const subTriggerState = MenuSubTriggerState.create({
			disabled: boxWith(() => disabled),
			onSelect: boxWith(() => onSelect),
			id: boxWith(() => id),
			ref: boxWith(() => ref, (v) => ref = v),
			openDelay: boxWith(() => openDelay)
		});
		const mergedProps = derived(() => mergeProps(restProps, subTriggerState.props));
		Floating_layer_anchor($$renderer, {
			id,
			ref: subTriggerState.opts.ref,
			children: ($$renderer) => {
				if (child) {
					$$renderer.push("<!--[0-->");
					child($$renderer, { props: mergedProps() });
					$$renderer.push(`<!---->`);
				} else {
					$$renderer.push("<!--[-1-->");
					$$renderer.push(`<div${attributes({ ...mergedProps() })}>`);
					children?.($$renderer);
					$$renderer.push(`<!----></div>`);
				}
				$$renderer.push(`<!--]-->`);
			},
			$$slots: { default: true }
		});
		bind_props($$props, { ref });
	});
}
//#endregion
//#region node_modules/bits-ui/dist/internal/safe-polygon.svelte.js
function isPointInPolygon(point, polygon) {
	const [x, y] = point;
	let isInside = false;
	const length = polygon.length;
	for (let i = 0, j = length - 1; i < length; j = i++) {
		const [xi, yi] = polygon[i] ?? [0, 0];
		const [xj, yj] = polygon[j] ?? [0, 0];
		if (yi >= y !== yj >= y && x <= (xj - xi) * (y - yi) / (yj - yi) + xi) isInside = !isInside;
	}
	return isInside;
}
function isInsideRect(point, rect) {
	return point[0] >= rect.left && point[0] <= rect.right && point[1] >= rect.top && point[1] <= rect.bottom;
}
function getSide(triggerRect, contentRect) {
	const triggerCenterX = triggerRect.left + triggerRect.width / 2;
	const triggerCenterY = triggerRect.top + triggerRect.height / 2;
	const contentCenterX = contentRect.left + contentRect.width / 2;
	const contentCenterY = contentRect.top + contentRect.height / 2;
	const deltaX = contentCenterX - triggerCenterX;
	const deltaY = contentCenterY - triggerCenterY;
	if (Math.abs(deltaX) > Math.abs(deltaY)) return deltaX > 0 ? "right" : "left";
	return deltaY > 0 ? "bottom" : "top";
}
/**
* Creates a safe polygon area that allows users to move their cursor between
* the trigger and floating content without closing it.
*/
var SafePolygon = class {
	#opts;
	#buffer;
	#transitIntentTimeout;
	#exitPoint = null;
	#exitTarget = null;
	#transitTargets = [];
	#trackedTriggerNode = null;
	#leaveFallbackRafId = null;
	#transitIntentTimeoutId = null;
	#cancelLeaveFallback() {
		if (this.#leaveFallbackRafId !== null) {
			cancelAnimationFrame(this.#leaveFallbackRafId);
			this.#leaveFallbackRafId = null;
		}
	}
	#scheduleLeaveFallback() {
		this.#cancelLeaveFallback();
		this.#leaveFallbackRafId = requestAnimationFrame(() => {
			this.#leaveFallbackRafId = null;
			if (!this.#exitPoint || !this.#exitTarget) return;
			this.#clearTracking();
			this.#opts.onPointerExit();
		});
	}
	#cancelTransitIntentTimeout() {
		if (this.#transitIntentTimeoutId !== null) {
			clearTimeout(this.#transitIntentTimeoutId);
			this.#transitIntentTimeoutId = null;
		}
	}
	#scheduleTransitIntentTimeout() {
		if (this.#transitIntentTimeout === null) return;
		this.#cancelTransitIntentTimeout();
		this.#transitIntentTimeoutId = window.setTimeout(() => {
			this.#transitIntentTimeoutId = null;
			if (!this.#exitPoint || !this.#exitTarget) return;
			this.#clearTracking();
			this.#opts.onPointerExit();
		}, this.#transitIntentTimeout);
	}
	constructor(opts) {
		this.#opts = opts;
		this.#buffer = opts.buffer ?? 1;
		const transitIntentTimeout = opts.transitIntentTimeout;
		this.#transitIntentTimeout = typeof transitIntentTimeout === "number" && transitIntentTimeout > 0 ? transitIntentTimeout : null;
		watch([
			opts.triggerNode,
			opts.contentNode,
			opts.enabled
		], ([triggerNode, contentNode, enabled]) => {
			if (!triggerNode || !contentNode || !enabled) {
				this.#trackedTriggerNode = null;
				this.#clearTracking();
				return;
			}
			if (this.#trackedTriggerNode && this.#trackedTriggerNode !== triggerNode) this.#clearTracking();
			this.#trackedTriggerNode = triggerNode;
			const doc = getDocument(triggerNode);
			const handlePointerMove = (e) => {
				this.#onPointerMove([e.clientX, e.clientY], triggerNode, contentNode);
			};
			const handleTriggerLeave = (e) => {
				const target = e.relatedTarget;
				if (isElement(target) && contentNode.contains(target)) return;
				const ignoredTargets = this.#opts.ignoredTargets?.() ?? [];
				if (isElement(target) && ignoredTargets.some((n) => n === target || n.contains(target))) return;
				this.#transitTargets = isElement(target) && ignoredTargets.length > 0 ? ignoredTargets.filter((n) => target.contains(n)) : [];
				this.#exitPoint = [e.clientX, e.clientY];
				this.#exitTarget = "content";
				this.#scheduleLeaveFallback();
			};
			const handleTriggerEnter = () => {
				this.#clearTracking();
			};
			const handleContentEnter = () => {
				this.#clearTracking();
			};
			const handleContentLeave = (e) => {
				const target = e.relatedTarget;
				if (isElement(target) && triggerNode.contains(target)) return;
				this.#exitPoint = [e.clientX, e.clientY];
				this.#exitTarget = "trigger";
				this.#scheduleLeaveFallback();
			};
			return [
				on(doc, "pointermove", handlePointerMove),
				on(triggerNode, "pointerleave", handleTriggerLeave),
				on(triggerNode, "pointerenter", handleTriggerEnter),
				on(contentNode, "pointerenter", handleContentEnter),
				on(contentNode, "pointerleave", handleContentLeave)
			].reduce((acc, cleanup) => () => {
				acc();
				cleanup();
			}, () => {});
		});
	}
	#onPointerMove(clientPoint, triggerNode, contentNode) {
		if (!this.#exitPoint || !this.#exitTarget) return;
		this.#cancelLeaveFallback();
		this.#scheduleTransitIntentTimeout();
		const triggerRect = triggerNode.getBoundingClientRect();
		const contentRect = contentNode.getBoundingClientRect();
		if (this.#exitTarget === "content" && isInsideRect(clientPoint, contentRect)) {
			this.#clearTracking();
			return;
		}
		if (this.#exitTarget === "trigger" && isInsideRect(clientPoint, triggerRect)) {
			this.#clearTracking();
			return;
		}
		if (this.#exitTarget === "content" && this.#transitTargets.length > 0) for (const transitTarget of this.#transitTargets) {
			const transitRect = transitTarget.getBoundingClientRect();
			if (isInsideRect(clientPoint, transitRect)) return;
			const transitSide = getSide(triggerRect, transitRect);
			const transitCorridor = this.#getCorridorPolygon(triggerRect, transitRect, transitSide);
			if (transitCorridor && isPointInPolygon(clientPoint, transitCorridor)) return;
		}
		const side = getSide(triggerRect, contentRect);
		const corridorPoly = this.#getCorridorPolygon(triggerRect, contentRect, side);
		if (corridorPoly && isPointInPolygon(clientPoint, corridorPoly)) return;
		const targetRect = this.#exitTarget === "content" ? contentRect : triggerRect;
		if (isPointInPolygon(clientPoint, this.#getSafePolygon(this.#exitPoint, targetRect, side, this.#exitTarget))) return;
		this.#clearTracking();
		this.#opts.onPointerExit();
	}
	#clearTracking() {
		this.#exitPoint = null;
		this.#exitTarget = null;
		this.#transitTargets = [];
		this.#cancelLeaveFallback();
		this.#cancelTransitIntentTimeout();
	}
	/**
	* Creates a rectangular corridor between trigger and content
	* This prevents closing when cursor is in the gap between them
	*/
	#getCorridorPolygon(triggerRect, contentRect, side) {
		const buffer = this.#buffer;
		switch (side) {
			case "top": return [
				[Math.min(triggerRect.left, contentRect.left) - buffer, triggerRect.top],
				[Math.min(triggerRect.left, contentRect.left) - buffer, contentRect.bottom],
				[Math.max(triggerRect.right, contentRect.right) + buffer, contentRect.bottom],
				[Math.max(triggerRect.right, contentRect.right) + buffer, triggerRect.top]
			];
			case "bottom": return [
				[Math.min(triggerRect.left, contentRect.left) - buffer, triggerRect.bottom],
				[Math.min(triggerRect.left, contentRect.left) - buffer, contentRect.top],
				[Math.max(triggerRect.right, contentRect.right) + buffer, contentRect.top],
				[Math.max(triggerRect.right, contentRect.right) + buffer, triggerRect.bottom]
			];
			case "left": return [
				[triggerRect.left, Math.min(triggerRect.top, contentRect.top) - buffer],
				[contentRect.right, Math.min(triggerRect.top, contentRect.top) - buffer],
				[contentRect.right, Math.max(triggerRect.bottom, contentRect.bottom) + buffer],
				[triggerRect.left, Math.max(triggerRect.bottom, contentRect.bottom) + buffer]
			];
			case "right": return [
				[triggerRect.right, Math.min(triggerRect.top, contentRect.top) - buffer],
				[contentRect.left, Math.min(triggerRect.top, contentRect.top) - buffer],
				[contentRect.left, Math.max(triggerRect.bottom, contentRect.bottom) + buffer],
				[triggerRect.right, Math.max(triggerRect.bottom, contentRect.bottom) + buffer]
			];
		}
	}
	/**
	* Creates a triangular/trapezoidal safe zone from the exit point to the target
	*/
	#getSafePolygon(exitPoint, targetRect, side, exitTarget) {
		const buffer = this.#buffer * 4;
		const [x, y] = exitPoint;
		switch (exitTarget === "trigger" ? this.#flipSide(side) : side) {
			case "top": return [
				[x - buffer, y + buffer],
				[x + buffer, y + buffer],
				[targetRect.right + buffer, targetRect.bottom],
				[targetRect.right + buffer, targetRect.top],
				[targetRect.left - buffer, targetRect.top],
				[targetRect.left - buffer, targetRect.bottom]
			];
			case "bottom": return [
				[x - buffer, y - buffer],
				[x + buffer, y - buffer],
				[targetRect.right + buffer, targetRect.top],
				[targetRect.right + buffer, targetRect.bottom],
				[targetRect.left - buffer, targetRect.bottom],
				[targetRect.left - buffer, targetRect.top]
			];
			case "left": return [
				[x + buffer, y - buffer],
				[x + buffer, y + buffer],
				[targetRect.right, targetRect.bottom + buffer],
				[targetRect.left, targetRect.bottom + buffer],
				[targetRect.left, targetRect.top - buffer],
				[targetRect.right, targetRect.top - buffer]
			];
			case "right": return [
				[x - buffer, y - buffer],
				[x - buffer, y + buffer],
				[targetRect.left, targetRect.bottom + buffer],
				[targetRect.right, targetRect.bottom + buffer],
				[targetRect.right, targetRect.top - buffer],
				[targetRect.left, targetRect.top - buffer]
			];
		}
	}
	#flipSide(side) {
		switch (side) {
			case "top": return "bottom";
			case "bottom": return "top";
			case "left": return "right";
			case "right": return "left";
		}
	}
};
//#endregion
//#region node_modules/bits-ui/dist/bits/popover/popover.svelte.js
var popoverAttrs = createBitsAttrs({
	component: "popover",
	parts: [
		"root",
		"trigger",
		"content",
		"close",
		"overlay"
	]
});
var PopoverRootContext = new Context("Popover.Root");
var PopoverRootState = class PopoverRootState {
	static create(opts) {
		return PopoverRootContext.set(new PopoverRootState(opts));
	}
	opts;
	contentNode = null;
	contentPresence;
	triggerNode = null;
	overlayNode = null;
	overlayPresence;
	openedViaHover = false;
	hasInteractedWithContent = false;
	hoverCooldown = false;
	closeDelay = 0;
	#closeTimeout = null;
	#domContext = null;
	constructor(opts) {
		this.opts = opts;
		this.contentPresence = new PresenceManager({
			ref: boxWith(() => this.contentNode),
			open: this.opts.open,
			onComplete: () => {
				this.opts.onOpenChangeComplete.current(this.opts.open.current);
			}
		});
		this.overlayPresence = new PresenceManager({
			ref: boxWith(() => this.overlayNode),
			open: this.opts.open
		});
		watch(() => this.opts.open.current, (isOpen) => {
			if (!isOpen) {
				this.openedViaHover = false;
				this.hasInteractedWithContent = false;
				this.#clearCloseTimeout();
			}
		});
	}
	setDomContext(ctx) {
		this.#domContext = ctx;
	}
	#clearCloseTimeout() {
		if (this.#closeTimeout !== null && this.#domContext) {
			this.#domContext.clearTimeout(this.#closeTimeout);
			this.#closeTimeout = null;
		}
	}
	toggleOpen() {
		this.#clearCloseTimeout();
		this.opts.open.current = !this.opts.open.current;
	}
	handleClose() {
		this.#clearCloseTimeout();
		if (!this.opts.open.current) return;
		this.opts.open.current = false;
	}
	handleHoverOpen() {
		this.#clearCloseTimeout();
		if (this.opts.open.current) return;
		this.openedViaHover = true;
		this.opts.open.current = true;
	}
	handleHoverClose() {
		if (!this.opts.open.current) return;
		if (this.openedViaHover && !this.hasInteractedWithContent) this.opts.open.current = false;
	}
	handleDelayedHoverClose() {
		if (!this.opts.open.current) return;
		if (!this.openedViaHover || this.hasInteractedWithContent) return;
		this.#clearCloseTimeout();
		if (this.closeDelay <= 0) this.opts.open.current = false;
		else if (this.#domContext) this.#closeTimeout = this.#domContext.setTimeout(() => {
			if (this.openedViaHover && !this.hasInteractedWithContent) this.opts.open.current = false;
			this.#closeTimeout = null;
		}, this.closeDelay);
	}
	cancelDelayedClose() {
		this.#clearCloseTimeout();
	}
	markInteraction() {
		this.hasInteractedWithContent = true;
		this.#clearCloseTimeout();
	}
};
var PopoverTriggerState = class PopoverTriggerState {
	static create(opts) {
		return new PopoverTriggerState(opts, PopoverRootContext.get());
	}
	opts;
	root;
	attachment;
	domContext;
	#openTimeout = null;
	#closeTimeout = null;
	#isHovering = false;
	constructor(opts, root) {
		this.opts = opts;
		this.root = root;
		this.attachment = attachRef(this.opts.ref, (v) => this.root.triggerNode = v);
		this.domContext = new DOMContext(opts.ref);
		this.root.setDomContext(this.domContext);
		this.onclick = this.onclick.bind(this);
		this.onkeydown = this.onkeydown.bind(this);
		this.onpointerenter = this.onpointerenter.bind(this);
		this.onpointerleave = this.onpointerleave.bind(this);
		watch(() => this.opts.closeDelay.current, (delay) => {
			this.root.closeDelay = delay;
		});
	}
	#clearOpenTimeout() {
		if (this.#openTimeout !== null) {
			this.domContext.clearTimeout(this.#openTimeout);
			this.#openTimeout = null;
		}
	}
	#clearCloseTimeout() {
		if (this.#closeTimeout !== null) {
			this.domContext.clearTimeout(this.#closeTimeout);
			this.#closeTimeout = null;
		}
	}
	#clearAllTimeouts() {
		this.#clearOpenTimeout();
		this.#clearCloseTimeout();
	}
	onpointerenter(e) {
		if (this.opts.disabled.current) return;
		if (!this.opts.openOnHover.current) return;
		if (isTouch(e)) return;
		this.#isHovering = true;
		this.#clearCloseTimeout();
		this.root.cancelDelayedClose();
		if (this.root.opts.open.current || this.root.hoverCooldown) return;
		const delay = this.opts.openDelay.current;
		if (delay <= 0) this.root.handleHoverOpen();
		else this.#openTimeout = this.domContext.setTimeout(() => {
			this.root.handleHoverOpen();
			this.#openTimeout = null;
		}, delay);
	}
	onpointerleave(e) {
		if (this.opts.disabled.current) return;
		if (!this.opts.openOnHover.current) return;
		if (isTouch(e)) return;
		this.#isHovering = false;
		this.#clearOpenTimeout();
		this.root.hoverCooldown = false;
	}
	onclick(e) {
		if (this.opts.disabled.current) return;
		if (e.button !== 0) return;
		this.#clearAllTimeouts();
		if (this.#isHovering && this.root.opts.open.current && this.root.openedViaHover) {
			this.root.openedViaHover = false;
			this.root.hasInteractedWithContent = true;
			return;
		}
		if (this.#isHovering && this.opts.openOnHover.current && this.root.opts.open.current) this.root.hoverCooldown = true;
		if (this.root.hoverCooldown && !this.root.opts.open.current) this.root.hoverCooldown = false;
		this.root.toggleOpen();
	}
	onkeydown(e) {
		if (this.opts.disabled.current) return;
		if (!(e.key === "Enter" || e.key === " ")) return;
		e.preventDefault();
		this.#clearAllTimeouts();
		this.root.toggleOpen();
	}
	#getAriaControls() {
		if (this.root.opts.open.current && this.root.contentNode?.id) return this.root.contentNode?.id;
	}
	#props = derived(() => ({
		id: this.opts.id.current,
		"aria-haspopup": "dialog",
		"aria-expanded": boolToStr(this.root.opts.open.current),
		"data-state": getDataOpenClosed(this.root.opts.open.current),
		"aria-controls": this.#getAriaControls(),
		[popoverAttrs.trigger]: "",
		disabled: this.opts.disabled.current,
		onkeydown: this.onkeydown,
		onclick: this.onclick,
		onpointerenter: this.onpointerenter,
		onpointerleave: this.onpointerleave,
		...this.attachment
	}));
	get props() {
		return this.#props();
	}
	set props($$value) {
		return this.#props($$value);
	}
};
var PopoverContentState = class PopoverContentState {
	static create(opts) {
		return new PopoverContentState(opts, PopoverRootContext.get());
	}
	opts;
	root;
	attachment;
	constructor(opts, root) {
		this.opts = opts;
		this.root = root;
		this.attachment = attachRef(this.opts.ref, (v) => this.root.contentNode = v);
		this.onpointerdown = this.onpointerdown.bind(this);
		this.onfocusin = this.onfocusin.bind(this);
		this.onpointerenter = this.onpointerenter.bind(this);
		this.onpointerleave = this.onpointerleave.bind(this);
		new SafePolygon({
			triggerNode: () => this.root.triggerNode,
			contentNode: () => this.root.contentNode,
			enabled: () => this.root.opts.open.current && this.root.openedViaHover && !this.root.hasInteractedWithContent,
			onPointerExit: () => {
				this.root.handleDelayedHoverClose();
			}
		});
	}
	onpointerdown(_) {
		this.root.markInteraction();
	}
	onfocusin(e) {
		const target = e.target;
		if (isElement(target) && isTabbable(target)) this.root.markInteraction();
	}
	onpointerenter(e) {
		if (isTouch(e)) return;
		this.root.cancelDelayedClose();
	}
	onpointerleave(e) {
		if (isTouch(e)) return;
	}
	onInteractOutside = (e) => {
		this.opts.onInteractOutside.current(e);
		if (e.defaultPrevented) return;
		if (!isElement(e.target)) return;
		const closestTrigger = e.target.closest(popoverAttrs.selector("trigger"));
		if (closestTrigger && closestTrigger === this.root.triggerNode) return;
		if (this.opts.customAnchor.current) {
			if (isElement(this.opts.customAnchor.current)) {
				if (this.opts.customAnchor.current.contains(e.target)) return;
			} else if (typeof this.opts.customAnchor.current === "string") {
				const el = document.querySelector(this.opts.customAnchor.current);
				if (el && el.contains(e.target)) return;
			}
		}
		this.root.handleClose();
	};
	onEscapeKeydown = (e) => {
		this.opts.onEscapeKeydown.current(e);
		if (e.defaultPrevented) return;
		this.root.handleClose();
	};
	get shouldRender() {
		return this.root.contentPresence.shouldRender;
	}
	get shouldTrapFocus() {
		if (this.root.openedViaHover && !this.root.hasInteractedWithContent) return false;
		return true;
	}
	#snippetProps = derived(() => ({ open: this.root.opts.open.current }));
	get snippetProps() {
		return this.#snippetProps();
	}
	set snippetProps($$value) {
		return this.#snippetProps($$value);
	}
	#props = derived(() => ({
		id: this.opts.id.current,
		tabindex: -1,
		"data-state": getDataOpenClosed(this.root.opts.open.current),
		...getDataTransitionAttrs(this.root.contentPresence.transitionStatus),
		[popoverAttrs.content]: "",
		style: {
			pointerEvents: "auto",
			contain: "layout style"
		},
		onpointerdown: this.onpointerdown,
		onfocusin: this.onfocusin,
		onpointerenter: this.onpointerenter,
		onpointerleave: this.onpointerleave,
		...this.attachment
	}));
	get props() {
		return this.#props();
	}
	set props($$value) {
		return this.#props($$value);
	}
	popperProps = {
		onInteractOutside: this.onInteractOutside,
		onEscapeKeydown: this.onEscapeKeydown
	};
};
//#endregion
//#region node_modules/bits-ui/dist/bits/popover/components/popover-content.svelte
function Popover_content$1($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		const uid = props_id($$renderer);
		let { child, children, ref = null, id = createId(uid), forceMount = false, onOpenAutoFocus = noop, onCloseAutoFocus = noop, onEscapeKeydown = noop, onInteractOutside = noop, trapFocus = true, preventScroll = false, customAnchor = null, style, $$slots, $$events, ...restProps } = $$props;
		const contentState = PopoverContentState.create({
			id: boxWith(() => id),
			ref: boxWith(() => ref, (v) => ref = v),
			onInteractOutside: boxWith(() => onInteractOutside),
			onEscapeKeydown: boxWith(() => onEscapeKeydown),
			customAnchor: boxWith(() => customAnchor)
		});
		const mergedProps = derived(() => mergeProps(restProps, contentState.props));
		const effectiveTrapFocus = derived(() => trapFocus && contentState.shouldTrapFocus);
		function handleOpenAutoFocus(e) {
			if (!contentState.shouldTrapFocus) e.preventDefault();
			onOpenAutoFocus(e);
		}
		if (forceMount) {
			$$renderer.push("<!--[0-->");
			{
				function popper($$renderer, { props, wrapperProps }) {
					const finalProps = mergeProps(props, { style: getFloatingContentCSSVars("popover") }, { style });
					if (child) {
						$$renderer.push("<!--[0-->");
						child($$renderer, {
							props: finalProps,
							wrapperProps,
							...contentState.snippetProps
						});
						$$renderer.push(`<!---->`);
					} else {
						$$renderer.push("<!--[-1-->");
						$$renderer.push(`<div${attributes({ ...wrapperProps })}><div${attributes({ ...finalProps })}>`);
						children?.($$renderer);
						$$renderer.push(`<!----></div></div>`);
					}
					$$renderer.push(`<!--]-->`);
				}
				Popper_layer_force_mount($$renderer, spread_props([
					mergedProps(),
					contentState.popperProps,
					{
						ref: contentState.opts.ref,
						enabled: contentState.root.opts.open.current,
						id,
						trapFocus: effectiveTrapFocus(),
						preventScroll,
						loop: true,
						forceMount: true,
						customAnchor,
						onOpenAutoFocus: handleOpenAutoFocus,
						onCloseAutoFocus,
						shouldRender: contentState.shouldRender,
						popper,
						$$slots: { popper: true }
					}
				]));
			}
		} else if (!forceMount) {
			$$renderer.push("<!--[1-->");
			{
				function popper($$renderer, { props, wrapperProps }) {
					const finalProps = mergeProps(props, { style: getFloatingContentCSSVars("popover") }, { style });
					if (child) {
						$$renderer.push("<!--[0-->");
						child($$renderer, {
							props: finalProps,
							wrapperProps,
							...contentState.snippetProps
						});
						$$renderer.push(`<!---->`);
					} else {
						$$renderer.push("<!--[-1-->");
						$$renderer.push(`<div${attributes({ ...wrapperProps })}><div${attributes({ ...finalProps })}>`);
						children?.($$renderer);
						$$renderer.push(`<!----></div></div>`);
					}
					$$renderer.push(`<!--]-->`);
				}
				Popper_layer($$renderer, spread_props([
					mergedProps(),
					contentState.popperProps,
					{
						ref: contentState.opts.ref,
						open: contentState.root.opts.open.current,
						id,
						trapFocus: effectiveTrapFocus(),
						preventScroll,
						loop: true,
						forceMount: false,
						customAnchor,
						onOpenAutoFocus: handleOpenAutoFocus,
						onCloseAutoFocus,
						shouldRender: contentState.shouldRender,
						popper,
						$$slots: { popper: true }
					}
				]));
			}
		} else $$renderer.push("<!--[-1-->");
		$$renderer.push(`<!--]-->`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region node_modules/bits-ui/dist/bits/popover/components/popover-trigger.svelte
function Popover_trigger$1($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		const uid = props_id($$renderer);
		let { children, child, id = createId(uid), ref = null, type = "button", disabled = false, openOnHover = false, openDelay = 700, closeDelay = 300, $$slots, $$events, ...restProps } = $$props;
		const triggerState = PopoverTriggerState.create({
			id: boxWith(() => id),
			ref: boxWith(() => ref, (v) => ref = v),
			disabled: boxWith(() => Boolean(disabled)),
			openOnHover: boxWith(() => openOnHover),
			openDelay: boxWith(() => openDelay),
			closeDelay: boxWith(() => closeDelay)
		});
		const mergedProps = derived(() => mergeProps(restProps, triggerState.props, { type }));
		Floating_layer_anchor($$renderer, {
			id,
			ref: triggerState.opts.ref,
			children: ($$renderer) => {
				if (child) {
					$$renderer.push("<!--[0-->");
					child($$renderer, { props: mergedProps() });
					$$renderer.push(`<!---->`);
				} else {
					$$renderer.push("<!--[-1-->");
					$$renderer.push(`<button${attributes({ ...mergedProps() })}>`);
					children?.($$renderer);
					$$renderer.push(`<!----></button>`);
				}
				$$renderer.push(`<!--]-->`);
			},
			$$slots: { default: true }
		});
		bind_props($$props, { ref });
	});
}
//#endregion
//#region node_modules/bits-ui/dist/bits/dialog/components/dialog.svelte
function Dialog$1($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { open = false, onOpenChange = noop, onOpenChangeComplete = noop, children } = $$props;
		DialogRootState.create({
			variant: boxWith(() => "dialog"),
			open: boxWith(() => open, (v) => {
				open = v;
				onOpenChange(v);
			}),
			onOpenChangeComplete: boxWith(() => onOpenChangeComplete)
		});
		children?.($$renderer);
		$$renderer.push(`<!---->`);
		bind_props($$props, { open });
	});
}
//#endregion
//#region node_modules/bits-ui/dist/bits/dialog/components/dialog-close.svelte
function Dialog_close($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		const uid = props_id($$renderer);
		let { children, child, id = createId(uid), ref = null, disabled = false, $$slots, $$events, ...restProps } = $$props;
		const closeState = DialogCloseState.create({
			variant: boxWith(() => "close"),
			id: boxWith(() => id),
			ref: boxWith(() => ref, (v) => ref = v),
			disabled: boxWith(() => Boolean(disabled))
		});
		const mergedProps = derived(() => mergeProps(restProps, closeState.props));
		if (child) {
			$$renderer.push("<!--[0-->");
			child($$renderer, { props: mergedProps() });
			$$renderer.push(`<!---->`);
		} else {
			$$renderer.push("<!--[-1-->");
			$$renderer.push(`<button${attributes({ ...mergedProps() })}>`);
			children?.($$renderer);
			$$renderer.push(`<!----></button>`);
		}
		$$renderer.push(`<!--]-->`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region node_modules/bits-ui/dist/bits/dialog/components/dialog-content.svelte
function Dialog_content$1($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		const uid = props_id($$renderer);
		let { id = createId(uid), children, child, ref = null, forceMount = false, onCloseAutoFocus = noop, onOpenAutoFocus = noop, onEscapeKeydown = noop, onInteractOutside = noop, trapFocus = true, preventScroll = true, restoreScrollDelay = null, $$slots, $$events, ...restProps } = $$props;
		const contentState = DialogContentState.create({
			id: boxWith(() => id),
			ref: boxWith(() => ref, (v) => ref = v)
		});
		const mergedProps = derived(() => mergeProps(restProps, contentState.props));
		if (contentState.shouldRender || forceMount) {
			$$renderer.push("<!--[0-->");
			{
				function focusScope($$renderer, { props: focusScopeProps }) {
					Escape_layer($$renderer, spread_props([mergedProps(), {
						enabled: contentState.root.opts.open.current,
						ref: contentState.opts.ref,
						onEscapeKeydown: (e) => {
							onEscapeKeydown(e);
							if (e.defaultPrevented) return;
							contentState.root.handleClose();
						},
						children: ($$renderer) => {
							Dismissible_layer($$renderer, spread_props([mergedProps(), {
								ref: contentState.opts.ref,
								enabled: contentState.root.opts.open.current,
								onInteractOutside: (e) => {
									onInteractOutside(e);
									if (e.defaultPrevented) return;
									contentState.root.handleClose();
								},
								children: ($$renderer) => {
									Text_selection_layer($$renderer, spread_props([mergedProps(), {
										ref: contentState.opts.ref,
										enabled: contentState.root.opts.open.current,
										children: ($$renderer) => {
											if (child) {
												$$renderer.push("<!--[0-->");
												if (contentState.root.opts.open.current) {
													$$renderer.push("<!--[0-->");
													Scroll_lock($$renderer, {
														preventScroll,
														restoreScrollDelay
													});
												} else $$renderer.push("<!--[-1-->");
												$$renderer.push(`<!--]--> `);
												child($$renderer, {
													props: mergeProps(mergedProps(), focusScopeProps),
													...contentState.snippetProps
												});
												$$renderer.push(`<!---->`);
											} else {
												$$renderer.push("<!--[-1-->");
												Scroll_lock($$renderer, { preventScroll });
												$$renderer.push(`<!----> <div${attributes({ ...mergeProps(mergedProps(), focusScopeProps) })}>`);
												children?.($$renderer);
												$$renderer.push(`<!----></div>`);
											}
											$$renderer.push(`<!--]-->`);
										},
										$$slots: { default: true }
									}]));
								},
								$$slots: { default: true }
							}]));
						},
						$$slots: { default: true }
					}]));
				}
				Focus_scope($$renderer, {
					ref: contentState.opts.ref,
					loop: true,
					trapFocus,
					enabled: contentState.root.opts.open.current,
					onOpenAutoFocus,
					onCloseAutoFocus,
					focusScope,
					$$slots: { focusScope: true }
				});
			}
		} else $$renderer.push("<!--[-1-->");
		$$renderer.push(`<!--]-->`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region node_modules/bits-ui/dist/bits/menu/components/menu.svelte
function Menu($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { open = false, dir = "ltr", onOpenChange = noop, onOpenChangeComplete = noop, _internal_variant: variant = "dropdown-menu", _internal_should_skip_exit_animation: shouldSkipExitAnimation = void 0, children } = $$props;
		const root = MenuRootState.create({
			variant: boxWith(() => variant),
			dir: boxWith(() => dir),
			onClose: () => {
				open = false;
				onOpenChange(false);
			},
			shouldSkipExitAnimation: () => shouldSkipExitAnimation?.() ?? false
		});
		MenuMenuState.create({
			open: boxWith(() => open, (v) => {
				open = v;
				onOpenChange(v);
			}),
			onOpenChangeComplete: boxWith(() => onOpenChangeComplete)
		}, root);
		Floating_layer($$renderer, {
			children: ($$renderer) => {
				children?.($$renderer);
				$$renderer.push(`<!---->`);
			},
			$$slots: { default: true }
		});
		bind_props($$props, { open });
	});
}
//#endregion
//#region node_modules/bits-ui/dist/bits/dropdown-menu/components/dropdown-menu-content.svelte
function Dropdown_menu_content$1($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		const uid = props_id($$renderer);
		let { id = createId(uid), child, children, ref = null, loop = true, onInteractOutside = noop, onEscapeKeydown = noop, onCloseAutoFocus = noop, forceMount = false, trapFocus = false, style, $$slots, $$events, ...restProps } = $$props;
		const contentState = MenuContentState.create({
			id: boxWith(() => id),
			loop: boxWith(() => loop),
			ref: boxWith(() => ref, (v) => ref = v),
			onCloseAutoFocus: boxWith(() => onCloseAutoFocus)
		});
		const mergedProps = derived(() => mergeProps(restProps, contentState.props));
		function handleInteractOutside(e) {
			contentState.handleInteractOutside(e);
			if (e.defaultPrevented) return;
			onInteractOutside(e);
			if (e.defaultPrevented) return;
			if (e.target && e.target instanceof Element) {
				const subContentSelector = `[${contentState.parentMenu.root.getBitsAttr("sub-content")}]`;
				if (e.target.closest(subContentSelector)) return;
			}
			contentState.parentMenu.onClose();
		}
		function handleEscapeKeydown(e) {
			onEscapeKeydown(e);
			if (e.defaultPrevented) return;
			contentState.parentMenu.onClose();
		}
		if (forceMount) {
			$$renderer.push("<!--[0-->");
			{
				function popper($$renderer, { props, wrapperProps }) {
					const finalProps = mergeProps(props, { style: getFloatingContentCSSVars("dropdown-menu") }, { style });
					if (child) {
						$$renderer.push("<!--[0-->");
						child($$renderer, {
							props: finalProps,
							wrapperProps,
							...contentState.snippetProps
						});
						$$renderer.push(`<!---->`);
					} else {
						$$renderer.push("<!--[-1-->");
						$$renderer.push(`<div${attributes({ ...wrapperProps })}><div${attributes({ ...finalProps })}>`);
						children?.($$renderer);
						$$renderer.push(`<!----></div></div>`);
					}
					$$renderer.push(`<!--]-->`);
				}
				Popper_layer_force_mount($$renderer, spread_props([
					mergedProps(),
					contentState.popperProps,
					{
						ref: contentState.opts.ref,
						enabled: contentState.parentMenu.opts.open.current,
						onInteractOutside: handleInteractOutside,
						onEscapeKeydown: handleEscapeKeydown,
						trapFocus,
						loop,
						forceMount: true,
						id,
						shouldRender: contentState.shouldRender,
						popper,
						$$slots: { popper: true }
					}
				]));
			}
		} else if (!forceMount) {
			$$renderer.push("<!--[1-->");
			{
				function popper($$renderer, { props, wrapperProps }) {
					const finalProps = mergeProps(props, { style: getFloatingContentCSSVars("dropdown-menu") }, { style });
					if (child) {
						$$renderer.push("<!--[0-->");
						child($$renderer, {
							props: finalProps,
							wrapperProps,
							...contentState.snippetProps
						});
						$$renderer.push(`<!---->`);
					} else {
						$$renderer.push("<!--[-1-->");
						$$renderer.push(`<div${attributes({ ...wrapperProps })}><div${attributes({ ...finalProps })}>`);
						children?.($$renderer);
						$$renderer.push(`<!----></div></div>`);
					}
					$$renderer.push(`<!--]-->`);
				}
				Popper_layer($$renderer, spread_props([
					mergedProps(),
					contentState.popperProps,
					{
						ref: contentState.opts.ref,
						open: contentState.parentMenu.opts.open.current,
						onInteractOutside: handleInteractOutside,
						onEscapeKeydown: handleEscapeKeydown,
						trapFocus,
						loop,
						forceMount: false,
						id,
						shouldRender: contentState.shouldRender,
						popper,
						$$slots: { popper: true }
					}
				]));
			}
		} else $$renderer.push("<!--[-1-->");
		$$renderer.push(`<!--]-->`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region node_modules/bits-ui/dist/bits/menu/components/menu-trigger.svelte
function Menu_trigger($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		const uid = props_id($$renderer);
		let { id = createId(uid), ref = null, child, children, disabled = false, type = "button", $$slots, $$events, ...restProps } = $$props;
		const triggerState = DropdownMenuTriggerState.create({
			id: boxWith(() => id),
			disabled: boxWith(() => disabled ?? false),
			ref: boxWith(() => ref, (v) => ref = v)
		});
		const mergedProps = derived(() => mergeProps(restProps, triggerState.props, { type }));
		Floating_layer_anchor($$renderer, {
			id,
			ref: triggerState.opts.ref,
			children: ($$renderer) => {
				if (child) {
					$$renderer.push("<!--[0-->");
					child($$renderer, { props: mergedProps() });
					$$renderer.push(`<!---->`);
				} else {
					$$renderer.push("<!--[-1-->");
					$$renderer.push(`<button${attributes({ ...mergedProps() })}>`);
					children?.($$renderer);
					$$renderer.push(`<!----></button>`);
				}
				$$renderer.push(`<!--]-->`);
			},
			$$slots: { default: true }
		});
		bind_props($$props, { ref });
	});
}
//#endregion
//#region node_modules/bits-ui/dist/bits/label/label.svelte.js
var labelAttrs = createBitsAttrs({
	component: "label",
	parts: ["root"]
});
var LabelRootState = class LabelRootState {
	static create(opts) {
		return new LabelRootState(opts);
	}
	opts;
	attachment;
	constructor(opts) {
		this.opts = opts;
		this.attachment = attachRef(this.opts.ref);
		this.onmousedown = this.onmousedown.bind(this);
	}
	onmousedown(e) {
		if (e.detail > 1) e.preventDefault();
	}
	#props = derived(() => ({
		id: this.opts.id.current,
		[labelAttrs.root]: "",
		onmousedown: this.onmousedown,
		...this.attachment
	}));
	get props() {
		return this.#props();
	}
	set props($$value) {
		return this.#props($$value);
	}
};
//#endregion
//#region node_modules/bits-ui/dist/bits/label/components/label.svelte
function Label$1($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		const uid = props_id($$renderer);
		let { children, child, id = createId(uid), ref = null, for: forProp, $$slots, $$events, ...restProps } = $$props;
		const rootState = LabelRootState.create({
			id: boxWith(() => id),
			ref: boxWith(() => ref, (v) => ref = v)
		});
		const mergedProps = derived(() => mergeProps(restProps, rootState.props, { for: forProp }));
		if (child) {
			$$renderer.push("<!--[0-->");
			child($$renderer, { props: mergedProps() });
			$$renderer.push(`<!---->`);
		} else {
			$$renderer.push("<!--[-1-->");
			$$renderer.push(`<label${attributes({
				...mergedProps(),
				for: forProp
			})}>`);
			children?.($$renderer);
			$$renderer.push(`<!----></label>`);
		}
		$$renderer.push(`<!--]-->`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region node_modules/bits-ui/dist/internal/svelte-resize-observer.svelte.js
var SvelteResizeObserver = class {
	#node;
	#onResize;
	constructor(node, onResize) {
		this.#node = node;
		this.#onResize = onResize;
		this.handler = this.handler.bind(this);
	}
	handler() {
		let rAF = 0;
		const _node = this.#node();
		if (!_node) return;
		const resizeObserver = new ResizeObserver(() => {
			cancelAnimationFrame(rAF);
			rAF = window.requestAnimationFrame(this.#onResize);
		});
		resizeObserver.observe(_node);
		return () => {
			window.cancelAnimationFrame(rAF);
			resizeObserver.unobserve(_node);
		};
	}
};
//#endregion
//#region node_modules/bits-ui/dist/bits/utilities/presence-layer/presence.svelte.js
var Presence = class {
	opts;
	present;
	#afterAnimations;
	#isPresent = false;
	#hasMounted = false;
	#transitionStatus = void 0;
	#transitionFrame = null;
	constructor(opts) {
		this.opts = opts;
		this.present = this.opts.open;
		this.#isPresent = opts.open.current;
		this.#afterAnimations = new AnimationsComplete({
			ref: this.opts.ref,
			afterTick: this.opts.open
		});
		watch(() => this.present.current, (isOpen) => {
			if (!this.#hasMounted) {
				this.#hasMounted = true;
				return;
			}
			this.#clearTransitionFrame();
			if (isOpen) this.#isPresent = true;
			this.#transitionStatus = isOpen ? "starting" : "ending";
			if (isOpen) this.#transitionFrame = window.requestAnimationFrame(() => {
				this.#transitionFrame = null;
				if (this.present.current) this.#transitionStatus = void 0;
			});
			this.#afterAnimations.run(() => {
				if (isOpen !== this.present.current) return;
				if (!isOpen) this.#isPresent = false;
				this.#transitionStatus = void 0;
			});
		});
	}
	#_isPresent = derived(() => {
		return this.#isPresent;
	});
	get isPresent() {
		return this.#_isPresent();
	}
	set isPresent($$value) {
		return this.#_isPresent($$value);
	}
	get transitionStatus() {
		return this.#transitionStatus;
	}
	#clearTransitionFrame() {
		if (this.#transitionFrame === null) return;
		window.cancelAnimationFrame(this.#transitionFrame);
		this.#transitionFrame = null;
	}
};
//#endregion
//#region node_modules/bits-ui/dist/bits/utilities/presence-layer/presence-layer.svelte
function Presence_layer($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { open, forceMount, presence, ref } = $$props;
		const presenceState = new Presence({
			open: boxWith(() => open),
			ref
		});
		if (forceMount || open || presenceState.isPresent) {
			$$renderer.push("<!--[0-->");
			presence?.($$renderer, {
				present: presenceState.isPresent,
				transitionStatus: presenceState.transitionStatus
			});
			$$renderer.push(`<!---->`);
		} else $$renderer.push("<!--[-1-->");
		$$renderer.push(`<!--]-->`);
	});
}
//#endregion
//#region node_modules/bits-ui/dist/bits/popover/components/popover.svelte
function Popover$1($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { open = false, onOpenChange = noop, onOpenChangeComplete = noop, children } = $$props;
		PopoverRootState.create({
			open: boxWith(() => open, (v) => {
				open = v;
				onOpenChange(v);
			}),
			onOpenChangeComplete: boxWith(() => onOpenChangeComplete)
		});
		Floating_layer($$renderer, {
			children: ($$renderer) => {
				children?.($$renderer);
				$$renderer.push(`<!---->`);
			},
			$$slots: { default: true }
		});
		bind_props($$props, { open });
	});
}
//#endregion
//#region node_modules/bits-ui/dist/internal/clamp.js
/**
* Clamps a number between a minimum and maximum value.
*/
function clamp(n, min, max) {
	return Math.min(max, Math.max(min, n));
}
//#endregion
//#region node_modules/bits-ui/dist/internal/state-machine.js
var StateMachine = class {
	state;
	#machine;
	constructor(initialState, machine) {
		this.state = simpleBox(initialState);
		this.#machine = machine;
		this.dispatch = this.dispatch.bind(this);
	}
	#reducer(event) {
		return this.#machine[this.state.current][event] ?? this.state.current;
	}
	dispatch(event) {
		this.state.current = this.#reducer(event);
	}
};
//#endregion
//#region node_modules/bits-ui/dist/bits/scroll-area/scroll-area.svelte.js
var scrollAreaAttrs = createBitsAttrs({
	component: "scroll-area",
	parts: [
		"root",
		"viewport",
		"corner",
		"thumb",
		"scrollbar"
	]
});
var ScrollAreaRootContext = new Context("ScrollArea.Root");
var ScrollAreaScrollbarContext = new Context("ScrollArea.Scrollbar");
var ScrollAreaScrollbarVisibleContext = new Context("ScrollArea.ScrollbarVisible");
var ScrollAreaScrollbarAxisContext = new Context("ScrollArea.ScrollbarAxis");
var ScrollAreaScrollbarSharedContext = new Context("ScrollArea.ScrollbarShared");
var ScrollAreaRootState = class ScrollAreaRootState {
	static create(opts) {
		return ScrollAreaRootContext.set(new ScrollAreaRootState(opts));
	}
	opts;
	attachment;
	scrollAreaNode = null;
	viewportNode = null;
	contentNode = null;
	scrollbarXNode = null;
	scrollbarYNode = null;
	cornerWidth = 0;
	cornerHeight = 0;
	scrollbarXEnabled = false;
	scrollbarYEnabled = false;
	domContext;
	constructor(opts) {
		this.opts = opts;
		this.attachment = attachRef(opts.ref, (v) => this.scrollAreaNode = v);
		this.domContext = new DOMContext(opts.ref);
	}
	#props = derived(() => ({
		id: this.opts.id.current,
		dir: this.opts.dir.current,
		style: {
			position: "relative",
			"--bits-scroll-area-corner-height": `${this.cornerHeight}px`,
			"--bits-scroll-area-corner-width": `${this.cornerWidth}px`
		},
		[scrollAreaAttrs.root]: "",
		...this.attachment
	}));
	get props() {
		return this.#props();
	}
	set props($$value) {
		return this.#props($$value);
	}
};
var ScrollAreaViewportState = class ScrollAreaViewportState {
	static create(opts) {
		return new ScrollAreaViewportState(opts, ScrollAreaRootContext.get());
	}
	opts;
	root;
	attachment;
	#contentId = simpleBox(useId());
	#contentRef = simpleBox(null);
	contentAttachment = attachRef(this.#contentRef, (v) => this.root.contentNode = v);
	constructor(opts, root) {
		this.opts = opts;
		this.root = root;
		this.attachment = attachRef(opts.ref, (v) => this.root.viewportNode = v);
	}
	#props = derived(() => ({
		id: this.opts.id.current,
		style: {
			overflowX: this.root.scrollbarXEnabled ? "scroll" : "hidden",
			overflowY: this.root.scrollbarYEnabled ? "scroll" : "hidden"
		},
		[scrollAreaAttrs.viewport]: "",
		...this.attachment
	}));
	get props() {
		return this.#props();
	}
	set props($$value) {
		return this.#props($$value);
	}
	#contentProps = derived(() => ({
		id: this.#contentId.current,
		"data-scroll-area-content": "",
		style: { minWidth: this.root.scrollbarXEnabled ? "fit-content" : void 0 },
		...this.contentAttachment
	}));
	get contentProps() {
		return this.#contentProps();
	}
	set contentProps($$value) {
		return this.#contentProps($$value);
	}
};
var ScrollAreaScrollbarState = class ScrollAreaScrollbarState {
	static create(opts) {
		return ScrollAreaScrollbarContext.set(new ScrollAreaScrollbarState(opts, ScrollAreaRootContext.get()));
	}
	opts;
	root;
	#isHorizontal = derived(() => this.opts.orientation.current === "horizontal");
	get isHorizontal() {
		return this.#isHorizontal();
	}
	set isHorizontal($$value) {
		return this.#isHorizontal($$value);
	}
	hasThumb = false;
	constructor(opts, root) {
		this.opts = opts;
		this.root = root;
		watch(() => this.isHorizontal, (isHorizontal) => {
			if (isHorizontal) {
				this.root.scrollbarXEnabled = true;
				return () => {
					this.root.scrollbarXEnabled = false;
				};
			} else {
				this.root.scrollbarYEnabled = true;
				return () => {
					this.root.scrollbarYEnabled = false;
				};
			}
		});
	}
};
var ScrollAreaScrollbarHoverState = class ScrollAreaScrollbarHoverState {
	static create() {
		return new ScrollAreaScrollbarHoverState(ScrollAreaScrollbarContext.get());
	}
	scrollbar;
	root;
	isVisible = false;
	constructor(scrollbar) {
		this.scrollbar = scrollbar;
		this.root = scrollbar.root;
	}
	#props = derived(() => ({ "data-state": this.isVisible ? "visible" : "hidden" }));
	get props() {
		return this.#props();
	}
	set props($$value) {
		return this.#props($$value);
	}
};
var ScrollAreaScrollbarScrollState = class ScrollAreaScrollbarScrollState {
	static create() {
		return new ScrollAreaScrollbarScrollState(ScrollAreaScrollbarContext.get());
	}
	scrollbar;
	root;
	machine = new StateMachine("hidden", {
		hidden: { SCROLL: "scrolling" },
		scrolling: {
			SCROLL_END: "idle",
			POINTER_ENTER: "interacting"
		},
		interacting: {
			SCROLL: "interacting",
			POINTER_LEAVE: "idle"
		},
		idle: {
			HIDE: "hidden",
			SCROLL: "scrolling",
			POINTER_ENTER: "interacting"
		}
	});
	#isHidden = derived(() => this.machine.state.current === "hidden");
	get isHidden() {
		return this.#isHidden();
	}
	set isHidden($$value) {
		return this.#isHidden($$value);
	}
	constructor(scrollbar) {
		this.scrollbar = scrollbar;
		this.root = scrollbar.root;
		useDebounce(() => this.machine.dispatch("SCROLL_END"), 100);
		this.onpointerenter = this.onpointerenter.bind(this);
		this.onpointerleave = this.onpointerleave.bind(this);
	}
	onpointerenter(_) {
		this.machine.dispatch("POINTER_ENTER");
	}
	onpointerleave(_) {
		this.machine.dispatch("POINTER_LEAVE");
	}
	#props = derived(() => ({
		"data-state": this.machine.state.current === "hidden" ? "hidden" : "visible",
		onpointerenter: this.onpointerenter,
		onpointerleave: this.onpointerleave
	}));
	get props() {
		return this.#props();
	}
	set props($$value) {
		return this.#props($$value);
	}
};
var ScrollAreaScrollbarAutoState = class ScrollAreaScrollbarAutoState {
	static create() {
		return new ScrollAreaScrollbarAutoState(ScrollAreaScrollbarContext.get());
	}
	scrollbar;
	root;
	isVisible = false;
	constructor(scrollbar) {
		this.scrollbar = scrollbar;
		this.root = scrollbar.root;
		const handleResize = useDebounce(() => {
			const viewportNode = this.root.viewportNode;
			if (!viewportNode) return;
			const isOverflowX = viewportNode.offsetWidth < viewportNode.scrollWidth;
			const isOverflowY = viewportNode.offsetHeight < viewportNode.scrollHeight;
			this.isVisible = this.scrollbar.isHorizontal ? isOverflowX : isOverflowY;
		}, 10);
		new SvelteResizeObserver(() => this.root.viewportNode, handleResize);
		new SvelteResizeObserver(() => this.root.contentNode, handleResize);
	}
	#props = derived(() => ({ "data-state": this.isVisible ? "visible" : "hidden" }));
	get props() {
		return this.#props();
	}
	set props($$value) {
		return this.#props($$value);
	}
};
var ScrollAreaScrollbarVisibleState = class ScrollAreaScrollbarVisibleState {
	static create() {
		return ScrollAreaScrollbarVisibleContext.set(new ScrollAreaScrollbarVisibleState(ScrollAreaScrollbarContext.get()));
	}
	scrollbar;
	root;
	thumbNode = null;
	pointerOffset = 0;
	sizes = {
		content: 0,
		viewport: 0,
		scrollbar: {
			size: 0,
			paddingStart: 0,
			paddingEnd: 0
		}
	};
	#thumbRatio = derived(() => getThumbRatio(this.sizes.viewport, this.sizes.content));
	get thumbRatio() {
		return this.#thumbRatio();
	}
	set thumbRatio($$value) {
		return this.#thumbRatio($$value);
	}
	#hasThumb = derived(() => Boolean(this.thumbRatio > 0 && this.thumbRatio < 1));
	get hasThumb() {
		return this.#hasThumb();
	}
	set hasThumb($$value) {
		return this.#hasThumb($$value);
	}
	prevTransformStyle = "";
	constructor(scrollbar) {
		this.scrollbar = scrollbar;
		this.root = scrollbar.root;
	}
	setSizes(sizes) {
		this.sizes = sizes;
	}
	getScrollPosition(pointerPos, dir) {
		return getScrollPositionFromPointer({
			pointerPos,
			pointerOffset: this.pointerOffset,
			sizes: this.sizes,
			dir
		});
	}
	onThumbPointerUp() {
		this.pointerOffset = 0;
	}
	onThumbPointerDown(pointerPos) {
		this.pointerOffset = pointerPos;
	}
	xOnThumbPositionChange() {
		if (!(this.root.viewportNode && this.thumbNode)) return;
		const scrollPos = this.root.viewportNode.scrollLeft;
		const transformStyle = `translate3d(${getThumbOffsetFromScroll({
			scrollPos,
			sizes: this.sizes,
			dir: this.root.opts.dir.current
		})}px, 0, 0)`;
		this.thumbNode.style.transform = transformStyle;
		this.prevTransformStyle = transformStyle;
	}
	xOnWheelScroll(scrollPos) {
		if (!this.root.viewportNode) return;
		this.root.viewportNode.scrollLeft = scrollPos;
	}
	xOnDragScroll(pointerPos) {
		if (!this.root.viewportNode) return;
		this.root.viewportNode.scrollLeft = this.getScrollPosition(pointerPos, this.root.opts.dir.current);
	}
	yOnThumbPositionChange() {
		if (!(this.root.viewportNode && this.thumbNode)) return;
		const scrollPos = this.root.viewportNode.scrollTop;
		const transformStyle = `translate3d(0, ${getThumbOffsetFromScroll({
			scrollPos,
			sizes: this.sizes
		})}px, 0)`;
		this.thumbNode.style.transform = transformStyle;
		this.prevTransformStyle = transformStyle;
	}
	yOnWheelScroll(scrollPos) {
		if (!this.root.viewportNode) return;
		this.root.viewportNode.scrollTop = scrollPos;
	}
	yOnDragScroll(pointerPos) {
		if (!this.root.viewportNode) return;
		this.root.viewportNode.scrollTop = this.getScrollPosition(pointerPos, this.root.opts.dir.current);
	}
};
var ScrollAreaScrollbarXState = class ScrollAreaScrollbarXState {
	static create(opts) {
		return ScrollAreaScrollbarAxisContext.set(new ScrollAreaScrollbarXState(opts, ScrollAreaScrollbarVisibleContext.get()));
	}
	opts;
	scrollbarVis;
	root;
	scrollbar;
	attachment;
	computedStyle;
	constructor(opts, scrollbarVis) {
		this.opts = opts;
		this.scrollbarVis = scrollbarVis;
		this.root = scrollbarVis.root;
		this.scrollbar = scrollbarVis.scrollbar;
		this.attachment = attachRef(this.scrollbar.opts.ref, (v) => this.root.scrollbarXNode = v);
	}
	onThumbPointerDown = (pointerPos) => {
		this.scrollbarVis.onThumbPointerDown(pointerPos.x);
	};
	onDragScroll = (pointerPos) => {
		this.scrollbarVis.xOnDragScroll(pointerPos.x);
	};
	onThumbPointerUp = () => {
		this.scrollbarVis.onThumbPointerUp();
	};
	onThumbPositionChange = () => {
		this.scrollbarVis.xOnThumbPositionChange();
	};
	onWheelScroll = (e, maxScrollPos) => {
		if (!this.root.viewportNode) return;
		const scrollPos = this.root.viewportNode.scrollLeft + e.deltaX;
		this.scrollbarVis.xOnWheelScroll(scrollPos);
		if (isScrollingWithinScrollbarBounds(scrollPos, maxScrollPos)) e.preventDefault();
	};
	onResize = () => {
		if (!(this.scrollbar.opts.ref.current && this.root.viewportNode && this.computedStyle)) return;
		this.scrollbarVis.setSizes({
			content: this.root.viewportNode.scrollWidth,
			viewport: this.root.viewportNode.offsetWidth,
			scrollbar: {
				size: this.scrollbar.opts.ref.current.clientWidth,
				paddingStart: toInt(this.computedStyle.paddingLeft),
				paddingEnd: toInt(this.computedStyle.paddingRight)
			}
		});
	};
	#thumbSize = derived(() => {
		return getThumbSize(this.scrollbarVis.sizes);
	});
	get thumbSize() {
		return this.#thumbSize();
	}
	set thumbSize($$value) {
		return this.#thumbSize($$value);
	}
	#props = derived(() => ({
		id: this.scrollbar.opts.id.current,
		"data-orientation": "horizontal",
		style: {
			bottom: 0,
			left: this.root.opts.dir.current === "rtl" ? "var(--bits-scroll-area-corner-width)" : 0,
			right: this.root.opts.dir.current === "ltr" ? "var(--bits-scroll-area-corner-width)" : 0,
			"--bits-scroll-area-thumb-width": `${this.thumbSize}px`
		},
		...this.attachment
	}));
	get props() {
		return this.#props();
	}
	set props($$value) {
		return this.#props($$value);
	}
};
var ScrollAreaScrollbarYState = class ScrollAreaScrollbarYState {
	static create(opts) {
		return ScrollAreaScrollbarAxisContext.set(new ScrollAreaScrollbarYState(opts, ScrollAreaScrollbarVisibleContext.get()));
	}
	opts;
	scrollbarVis;
	root;
	scrollbar;
	attachment;
	computedStyle;
	constructor(opts, scrollbarVis) {
		this.opts = opts;
		this.scrollbarVis = scrollbarVis;
		this.root = scrollbarVis.root;
		this.scrollbar = scrollbarVis.scrollbar;
		this.attachment = attachRef(this.scrollbar.opts.ref, (v) => this.root.scrollbarYNode = v);
		this.onThumbPointerDown = this.onThumbPointerDown.bind(this);
		this.onDragScroll = this.onDragScroll.bind(this);
		this.onThumbPointerUp = this.onThumbPointerUp.bind(this);
		this.onThumbPositionChange = this.onThumbPositionChange.bind(this);
		this.onWheelScroll = this.onWheelScroll.bind(this);
		this.onResize = this.onResize.bind(this);
	}
	onThumbPointerDown(pointerPos) {
		this.scrollbarVis.onThumbPointerDown(pointerPos.y);
	}
	onDragScroll(pointerPos) {
		this.scrollbarVis.yOnDragScroll(pointerPos.y);
	}
	onThumbPointerUp() {
		this.scrollbarVis.onThumbPointerUp();
	}
	onThumbPositionChange() {
		this.scrollbarVis.yOnThumbPositionChange();
	}
	onWheelScroll(e, maxScrollPos) {
		if (!this.root.viewportNode) return;
		const scrollPos = this.root.viewportNode.scrollTop + e.deltaY;
		this.scrollbarVis.yOnWheelScroll(scrollPos);
		if (isScrollingWithinScrollbarBounds(scrollPos, maxScrollPos)) e.preventDefault();
	}
	onResize() {
		if (!(this.scrollbar.opts.ref.current && this.root.viewportNode && this.computedStyle)) return;
		this.scrollbarVis.setSizes({
			content: this.root.viewportNode.scrollHeight,
			viewport: this.root.viewportNode.offsetHeight,
			scrollbar: {
				size: this.scrollbar.opts.ref.current.clientHeight,
				paddingStart: toInt(this.computedStyle.paddingTop),
				paddingEnd: toInt(this.computedStyle.paddingBottom)
			}
		});
	}
	#thumbSize = derived(() => {
		return getThumbSize(this.scrollbarVis.sizes);
	});
	get thumbSize() {
		return this.#thumbSize();
	}
	set thumbSize($$value) {
		return this.#thumbSize($$value);
	}
	#props = derived(() => ({
		id: this.scrollbar.opts.id.current,
		"data-orientation": "vertical",
		style: {
			top: 0,
			right: this.root.opts.dir.current === "ltr" ? 0 : void 0,
			left: this.root.opts.dir.current === "rtl" ? 0 : void 0,
			bottom: "var(--bits-scroll-area-corner-height)",
			"--bits-scroll-area-thumb-height": `${this.thumbSize}px`
		},
		...this.attachment
	}));
	get props() {
		return this.#props();
	}
	set props($$value) {
		return this.#props($$value);
	}
};
var ScrollAreaScrollbarSharedState = class ScrollAreaScrollbarSharedState {
	static create() {
		return ScrollAreaScrollbarSharedContext.set(new ScrollAreaScrollbarSharedState(ScrollAreaScrollbarAxisContext.get()));
	}
	scrollbarState;
	root;
	scrollbarVis;
	scrollbar;
	rect = null;
	prevWebkitUserSelect = "";
	handleResize;
	handleThumbPositionChange;
	handleWheelScroll;
	handleThumbPointerDown;
	handleThumbPointerUp;
	#maxScrollPos = derived(() => this.scrollbarVis.sizes.content - this.scrollbarVis.sizes.viewport);
	get maxScrollPos() {
		return this.#maxScrollPos();
	}
	set maxScrollPos($$value) {
		return this.#maxScrollPos($$value);
	}
	constructor(scrollbarState) {
		this.scrollbarState = scrollbarState;
		this.root = scrollbarState.root;
		this.scrollbarVis = scrollbarState.scrollbarVis;
		this.scrollbar = scrollbarState.scrollbarVis.scrollbar;
		this.handleResize = useDebounce(() => this.scrollbarState.onResize(), 10);
		this.handleThumbPositionChange = this.scrollbarState.onThumbPositionChange;
		this.handleWheelScroll = this.scrollbarState.onWheelScroll;
		this.handleThumbPointerDown = this.scrollbarState.onThumbPointerDown;
		this.handleThumbPointerUp = this.scrollbarState.onThumbPointerUp;
		new SvelteResizeObserver(() => this.scrollbar.opts.ref.current, this.handleResize);
		new SvelteResizeObserver(() => this.root.contentNode, this.handleResize);
		this.onpointerdown = this.onpointerdown.bind(this);
		this.onpointermove = this.onpointermove.bind(this);
		this.onpointerup = this.onpointerup.bind(this);
		this.onlostpointercapture = this.onlostpointercapture.bind(this);
	}
	handleDragScroll(e) {
		if (!this.rect) return;
		const x = e.clientX - this.rect.left;
		const y = e.clientY - this.rect.top;
		this.scrollbarState.onDragScroll({
			x,
			y
		});
	}
	#cleanupPointerState() {
		if (this.rect === null) return;
		this.root.domContext.getDocument().body.style.webkitUserSelect = this.prevWebkitUserSelect;
		if (this.root.viewportNode) this.root.viewportNode.style.scrollBehavior = "";
		this.rect = null;
	}
	onpointerdown(e) {
		if (e.button !== 0) return;
		e.target.setPointerCapture(e.pointerId);
		this.rect = this.scrollbar.opts.ref.current?.getBoundingClientRect() ?? null;
		this.prevWebkitUserSelect = this.root.domContext.getDocument().body.style.webkitUserSelect;
		this.root.domContext.getDocument().body.style.webkitUserSelect = "none";
		if (this.root.viewportNode) this.root.viewportNode.style.scrollBehavior = "auto";
		this.handleDragScroll(e);
	}
	onpointermove(e) {
		this.handleDragScroll(e);
	}
	onpointerup(e) {
		const target = e.target;
		if (target.hasPointerCapture(e.pointerId)) target.releasePointerCapture(e.pointerId);
		this.#cleanupPointerState();
	}
	onlostpointercapture(_) {
		this.#cleanupPointerState();
	}
	#props = derived(() => mergeProps({
		...this.scrollbarState.props,
		style: {
			position: "absolute",
			...this.scrollbarState.props.style
		},
		[scrollAreaAttrs.scrollbar]: "",
		onpointerdown: this.onpointerdown,
		onpointermove: this.onpointermove,
		onpointerup: this.onpointerup,
		onlostpointercapture: this.onlostpointercapture
	}));
	get props() {
		return this.#props();
	}
	set props($$value) {
		return this.#props($$value);
	}
};
var ScrollAreaThumbImplState = class ScrollAreaThumbImplState {
	static create(opts) {
		return new ScrollAreaThumbImplState(opts, ScrollAreaScrollbarSharedContext.get());
	}
	opts;
	scrollbarState;
	attachment;
	#root;
	#removeUnlinkedScrollListener;
	#debounceScrollEnd = useDebounce(() => {
		if (this.#removeUnlinkedScrollListener) {
			this.#removeUnlinkedScrollListener();
			this.#removeUnlinkedScrollListener = void 0;
		}
	}, 100);
	constructor(opts, scrollbarState) {
		this.opts = opts;
		this.scrollbarState = scrollbarState;
		this.#root = scrollbarState.root;
		this.attachment = attachRef(this.opts.ref, (v) => this.scrollbarState.scrollbarVis.thumbNode = v);
		this.onpointerdowncapture = this.onpointerdowncapture.bind(this);
		this.onpointerup = this.onpointerup.bind(this);
	}
	onpointerdowncapture(e) {
		const thumb = e.target;
		if (!thumb) return;
		const thumbRect = thumb.getBoundingClientRect();
		const x = e.clientX - thumbRect.left;
		const y = e.clientY - thumbRect.top;
		this.scrollbarState.handleThumbPointerDown({
			x,
			y
		});
	}
	onpointerup(_) {
		this.scrollbarState.handleThumbPointerUp();
	}
	#props = derived(() => ({
		id: this.opts.id.current,
		"data-state": this.scrollbarState.scrollbarVis.hasThumb ? "visible" : "hidden",
		style: {
			width: "var(--bits-scroll-area-thumb-width)",
			height: "var(--bits-scroll-area-thumb-height)",
			transform: this.scrollbarState.scrollbarVis.prevTransformStyle
		},
		onpointerdowncapture: this.onpointerdowncapture,
		onpointerup: this.onpointerup,
		[scrollAreaAttrs.thumb]: "",
		...this.attachment
	}));
	get props() {
		return this.#props();
	}
	set props($$value) {
		return this.#props($$value);
	}
};
var ScrollAreaCornerImplState = class ScrollAreaCornerImplState {
	static create(opts) {
		return new ScrollAreaCornerImplState(opts, ScrollAreaRootContext.get());
	}
	opts;
	root;
	attachment;
	#width = 0;
	#height = 0;
	#hasSize = derived(() => Boolean(this.#width && this.#height));
	get hasSize() {
		return this.#hasSize();
	}
	set hasSize($$value) {
		return this.#hasSize($$value);
	}
	constructor(opts, root) {
		this.opts = opts;
		this.root = root;
		this.attachment = attachRef(this.opts.ref);
		new SvelteResizeObserver(() => this.root.scrollbarXNode, () => {
			const height = this.root.scrollbarXNode?.offsetHeight || 0;
			this.root.cornerHeight = height;
			this.#height = height;
		});
		new SvelteResizeObserver(() => this.root.scrollbarYNode, () => {
			const width = this.root.scrollbarYNode?.offsetWidth || 0;
			this.root.cornerWidth = width;
			this.#width = width;
		});
	}
	#props = derived(() => ({
		id: this.opts.id.current,
		style: {
			width: this.#width,
			height: this.#height,
			position: "absolute",
			right: this.root.opts.dir.current === "ltr" ? 0 : void 0,
			left: this.root.opts.dir.current === "rtl" ? 0 : void 0,
			bottom: 0
		},
		[scrollAreaAttrs.corner]: "",
		...this.attachment
	}));
	get props() {
		return this.#props();
	}
	set props($$value) {
		return this.#props($$value);
	}
};
function toInt(value) {
	return value ? Number.parseInt(value, 10) : 0;
}
function getThumbRatio(viewportSize, contentSize) {
	const ratio = viewportSize / contentSize;
	return Number.isNaN(ratio) ? 0 : ratio;
}
function getThumbSize(sizes) {
	const ratio = getThumbRatio(sizes.viewport, sizes.content);
	const scrollbarPadding = sizes.scrollbar.paddingStart + sizes.scrollbar.paddingEnd;
	const thumbSize = (sizes.scrollbar.size - scrollbarPadding) * ratio;
	return Math.max(thumbSize, 18);
}
function getScrollPositionFromPointer({ pointerPos, pointerOffset, sizes, dir = "ltr" }) {
	const thumbSizePx = getThumbSize(sizes);
	const thumbCenter = thumbSizePx / 2;
	const offset = pointerOffset || thumbCenter;
	const thumbOffsetFromEnd = thumbSizePx - offset;
	const minPointerPos = sizes.scrollbar.paddingStart + offset;
	const maxPointerPos = sizes.scrollbar.size - sizes.scrollbar.paddingEnd - thumbOffsetFromEnd;
	const maxScrollPos = sizes.content - sizes.viewport;
	const scrollRange = dir === "ltr" ? [0, maxScrollPos] : [maxScrollPos * -1, 0];
	return linearScale([minPointerPos, maxPointerPos], scrollRange)(pointerPos);
}
function getThumbOffsetFromScroll({ scrollPos, sizes, dir = "ltr" }) {
	const thumbSizePx = getThumbSize(sizes);
	const scrollbarPadding = sizes.scrollbar.paddingStart + sizes.scrollbar.paddingEnd;
	const scrollbar = sizes.scrollbar.size - scrollbarPadding;
	const maxScrollPos = sizes.content - sizes.viewport;
	const maxThumbPos = scrollbar - thumbSizePx;
	const scrollClampRange = dir === "ltr" ? [0, maxScrollPos] : [maxScrollPos * -1, 0];
	const scrollWithoutMomentum = clamp(scrollPos, scrollClampRange[0], scrollClampRange[1]);
	return linearScale([0, maxScrollPos], [0, maxThumbPos])(scrollWithoutMomentum);
}
function linearScale(input, output) {
	return (value) => {
		if (input[0] === input[1] || output[0] === output[1]) return output[0];
		const ratio = (output[1] - output[0]) / (input[1] - input[0]);
		return output[0] + ratio * (value - input[0]);
	};
}
function isScrollingWithinScrollbarBounds(scrollPos, maxScrollPos) {
	return scrollPos > 0 && scrollPos < maxScrollPos;
}
//#endregion
//#region node_modules/bits-ui/dist/bits/scroll-area/components/scroll-area.svelte
function Scroll_area$1($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		const uid = props_id($$renderer);
		let { ref = null, id = createId(uid), type = "hover", dir = "ltr", scrollHideDelay = 600, children, child, $$slots, $$events, ...restProps } = $$props;
		const rootState = ScrollAreaRootState.create({
			type: boxWith(() => type),
			dir: boxWith(() => dir),
			scrollHideDelay: boxWith(() => scrollHideDelay),
			id: boxWith(() => id),
			ref: boxWith(() => ref, (v) => ref = v)
		});
		const mergedProps = derived(() => mergeProps(restProps, rootState.props));
		if (child) {
			$$renderer.push("<!--[0-->");
			child($$renderer, { props: mergedProps() });
			$$renderer.push(`<!---->`);
		} else {
			$$renderer.push("<!--[-1-->");
			$$renderer.push(`<div${attributes({ ...mergedProps() })}>`);
			children?.($$renderer);
			$$renderer.push(`<!----></div>`);
		}
		$$renderer.push(`<!--]-->`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region node_modules/bits-ui/dist/bits/scroll-area/components/scroll-area-viewport.svelte
function Scroll_area_viewport($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		const uid = props_id($$renderer);
		let { ref = null, id = createId(uid), children, $$slots, $$events, ...restProps } = $$props;
		const viewportState = ScrollAreaViewportState.create({
			id: boxWith(() => id),
			ref: boxWith(() => ref, (v) => ref = v)
		});
		const mergedProps = derived(() => mergeProps(restProps, viewportState.props));
		const mergedContentProps = derived(() => mergeProps({}, viewportState.contentProps));
		$$renderer.push(`<div${attributes({ ...mergedProps() })}><div${attributes({ ...mergedContentProps() })}>`);
		children?.($$renderer);
		$$renderer.push(`<!----></div></div>`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region node_modules/bits-ui/dist/bits/scroll-area/components/scroll-area-scrollbar-shared.svelte
function Scroll_area_scrollbar_shared($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { child, children, $$slots, $$events, ...restProps } = $$props;
		const scrollbarSharedState = ScrollAreaScrollbarSharedState.create();
		const mergedProps = derived(() => mergeProps(restProps, scrollbarSharedState.props));
		if (child) {
			$$renderer.push("<!--[0-->");
			child($$renderer, { props: mergedProps() });
			$$renderer.push(`<!---->`);
		} else {
			$$renderer.push("<!--[-1-->");
			$$renderer.push(`<div${attributes({ ...mergedProps() })}>`);
			children?.($$renderer);
			$$renderer.push(`<!----></div>`);
		}
		$$renderer.push(`<!--]-->`);
	});
}
//#endregion
//#region node_modules/bits-ui/dist/bits/scroll-area/components/scroll-area-scrollbar-x.svelte
function Scroll_area_scrollbar_x($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { $$slots, $$events, ...restProps } = $$props;
		const isMounted = new IsMounted();
		const scrollbarXState = ScrollAreaScrollbarXState.create({ mounted: boxWith(() => isMounted.current) });
		const mergedProps = derived(() => mergeProps(restProps, scrollbarXState.props));
		Scroll_area_scrollbar_shared($$renderer, spread_props([mergedProps()]));
	});
}
//#endregion
//#region node_modules/bits-ui/dist/bits/scroll-area/components/scroll-area-scrollbar-y.svelte
function Scroll_area_scrollbar_y($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { $$slots, $$events, ...restProps } = $$props;
		const isMounted = new IsMounted();
		const scrollbarYState = ScrollAreaScrollbarYState.create({ mounted: boxWith(() => isMounted.current) });
		const mergedProps = derived(() => mergeProps(restProps, scrollbarYState.props));
		Scroll_area_scrollbar_shared($$renderer, spread_props([mergedProps()]));
	});
}
//#endregion
//#region node_modules/bits-ui/dist/bits/scroll-area/components/scroll-area-scrollbar-visible.svelte
function Scroll_area_scrollbar_visible($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { $$slots, $$events, ...restProps } = $$props;
		if (ScrollAreaScrollbarVisibleState.create().scrollbar.opts.orientation.current === "horizontal") {
			$$renderer.push("<!--[0-->");
			Scroll_area_scrollbar_x($$renderer, spread_props([restProps]));
		} else {
			$$renderer.push("<!--[-1-->");
			Scroll_area_scrollbar_y($$renderer, spread_props([restProps]));
		}
		$$renderer.push(`<!--]-->`);
	});
}
//#endregion
//#region node_modules/bits-ui/dist/bits/scroll-area/components/scroll-area-scrollbar-auto.svelte
function Scroll_area_scrollbar_auto($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { forceMount = false, $$slots, $$events, ...restProps } = $$props;
		const scrollbarAutoState = ScrollAreaScrollbarAutoState.create();
		const mergedProps = derived(() => mergeProps(restProps, scrollbarAutoState.props));
		{
			function presence($$renderer) {
				Scroll_area_scrollbar_visible($$renderer, spread_props([mergedProps()]));
			}
			Presence_layer($$renderer, {
				open: forceMount || scrollbarAutoState.isVisible,
				ref: scrollbarAutoState.scrollbar.opts.ref,
				presence,
				$$slots: { presence: true }
			});
		}
	});
}
//#endregion
//#region node_modules/bits-ui/dist/bits/scroll-area/components/scroll-area-scrollbar-scroll.svelte
function Scroll_area_scrollbar_scroll($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { forceMount = false, $$slots, $$events, ...restProps } = $$props;
		const scrollbarScrollState = ScrollAreaScrollbarScrollState.create();
		const mergedProps = derived(() => mergeProps(restProps, scrollbarScrollState.props));
		{
			function presence($$renderer) {
				Scroll_area_scrollbar_visible($$renderer, spread_props([mergedProps()]));
			}
			Presence_layer($$renderer, spread_props([mergedProps(), {
				open: forceMount || !scrollbarScrollState.isHidden,
				ref: scrollbarScrollState.scrollbar.opts.ref,
				presence,
				$$slots: { presence: true }
			}]));
		}
	});
}
//#endregion
//#region node_modules/bits-ui/dist/bits/scroll-area/components/scroll-area-scrollbar-hover.svelte
function Scroll_area_scrollbar_hover($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { forceMount = false, $$slots, $$events, ...restProps } = $$props;
		const scrollbarHoverState = ScrollAreaScrollbarHoverState.create();
		const scrollbarAutoState = ScrollAreaScrollbarAutoState.create();
		const mergedProps = derived(() => mergeProps(restProps, scrollbarHoverState.props, scrollbarAutoState.props, { "data-state": scrollbarHoverState.isVisible ? "visible" : "hidden" }));
		const open = derived(() => forceMount || scrollbarHoverState.isVisible && scrollbarAutoState.isVisible);
		{
			function presence($$renderer) {
				Scroll_area_scrollbar_visible($$renderer, spread_props([mergedProps()]));
			}
			Presence_layer($$renderer, {
				open: open(),
				ref: scrollbarAutoState.scrollbar.opts.ref,
				presence,
				$$slots: { presence: true }
			});
		}
	});
}
//#endregion
//#region node_modules/bits-ui/dist/bits/scroll-area/components/scroll-area-scrollbar.svelte
function Scroll_area_scrollbar$1($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		const uid = props_id($$renderer);
		let { ref = null, id = createId(uid), orientation, $$slots, $$events, ...restProps } = $$props;
		const scrollbarState = ScrollAreaScrollbarState.create({
			orientation: boxWith(() => orientation),
			id: boxWith(() => id),
			ref: boxWith(() => ref, (v) => ref = v)
		});
		const type = derived(() => scrollbarState.root.opts.type.current);
		if (type() === "hover") {
			$$renderer.push("<!--[0-->");
			Scroll_area_scrollbar_hover($$renderer, spread_props([restProps, { id }]));
		} else if (type() === "scroll") {
			$$renderer.push("<!--[1-->");
			Scroll_area_scrollbar_scroll($$renderer, spread_props([restProps, { id }]));
		} else if (type() === "auto") {
			$$renderer.push("<!--[2-->");
			Scroll_area_scrollbar_auto($$renderer, spread_props([restProps, { id }]));
		} else if (type() === "always") {
			$$renderer.push("<!--[3-->");
			Scroll_area_scrollbar_visible($$renderer, spread_props([restProps, { id }]));
		} else $$renderer.push("<!--[-1-->");
		$$renderer.push(`<!--]-->`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region node_modules/bits-ui/dist/bits/scroll-area/components/scroll-area-thumb-impl.svelte
function Scroll_area_thumb_impl($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, id, child, children, present, $$slots, $$events, ...restProps } = $$props;
		const isMounted = new IsMounted();
		const thumbState = ScrollAreaThumbImplState.create({
			id: boxWith(() => id),
			ref: boxWith(() => ref, (v) => ref = v),
			mounted: boxWith(() => isMounted.current)
		});
		const mergedProps = derived(() => mergeProps(restProps, thumbState.props, { style: { hidden: !present } }));
		if (child) {
			$$renderer.push("<!--[0-->");
			child($$renderer, { props: mergedProps() });
			$$renderer.push(`<!---->`);
		} else {
			$$renderer.push("<!--[-1-->");
			$$renderer.push(`<div${attributes({ ...mergedProps() })}>`);
			children?.($$renderer);
			$$renderer.push(`<!----></div>`);
		}
		$$renderer.push(`<!--]-->`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region node_modules/bits-ui/dist/bits/scroll-area/components/scroll-area-thumb.svelte
function Scroll_area_thumb($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		const uid = props_id($$renderer);
		let { id = createId(uid), ref = null, forceMount = false, $$slots, $$events, ...restProps } = $$props;
		const scrollbarState = ScrollAreaScrollbarVisibleContext.get();
		let $$settled = true;
		let $$inner_renderer;
		function $$render_inner($$renderer) {
			{
				function presence($$renderer, { present }) {
					Scroll_area_thumb_impl($$renderer, spread_props([restProps, {
						id,
						present,
						get ref() {
							return ref;
						},
						set ref($$value) {
							ref = $$value;
							$$settled = false;
						}
					}]));
				}
				Presence_layer($$renderer, {
					open: forceMount || scrollbarState.hasThumb,
					ref: scrollbarState.scrollbar.opts.ref,
					presence,
					$$slots: { presence: true }
				});
			}
		}
		do {
			$$settled = true;
			$$inner_renderer = $$renderer.copy();
			$$render_inner($$inner_renderer);
		} while (!$$settled);
		$$renderer.subsume($$inner_renderer);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region node_modules/bits-ui/dist/bits/scroll-area/components/scroll-area-corner-impl.svelte
function Scroll_area_corner_impl($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, id, children, child, $$slots, $$events, ...restProps } = $$props;
		const cornerState = ScrollAreaCornerImplState.create({
			id: boxWith(() => id),
			ref: boxWith(() => ref, (v) => ref = v)
		});
		const mergedProps = derived(() => mergeProps(restProps, cornerState.props));
		if (child) {
			$$renderer.push("<!--[0-->");
			child($$renderer, { props: mergedProps() });
			$$renderer.push(`<!---->`);
		} else {
			$$renderer.push("<!--[-1-->");
			$$renderer.push(`<div${attributes({ ...mergedProps() })}>`);
			children?.($$renderer);
			$$renderer.push(`<!----></div>`);
		}
		$$renderer.push(`<!--]-->`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region node_modules/bits-ui/dist/bits/scroll-area/components/scroll-area-corner.svelte
function Scroll_area_corner($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		const uid = props_id($$renderer);
		let { ref = null, id = createId(uid), $$slots, $$events, ...restProps } = $$props;
		const scrollAreaState = ScrollAreaRootContext.get();
		const hasBothScrollbarsVisible = derived(() => Boolean(scrollAreaState.scrollbarXNode && scrollAreaState.scrollbarYNode));
		const hasCorner = derived(() => scrollAreaState.opts.type.current !== "scroll" && hasBothScrollbarsVisible());
		let $$settled = true;
		let $$inner_renderer;
		function $$render_inner($$renderer) {
			if (hasCorner()) {
				$$renderer.push("<!--[0-->");
				Scroll_area_corner_impl($$renderer, spread_props([restProps, {
					id,
					get ref() {
						return ref;
					},
					set ref($$value) {
						ref = $$value;
						$$settled = false;
					}
				}]));
			} else $$renderer.push("<!--[-1-->");
			$$renderer.push(`<!--]-->`);
		}
		do {
			$$settled = true;
			$$inner_renderer = $$renderer.copy();
			$$render_inner($$inner_renderer);
		} while (!$$settled);
		$$renderer.subsume($$inner_renderer);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region node_modules/bits-ui/dist/bits/select/components/select.svelte
function Select$1($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { value = void 0, onValueChange = noop, name = "", disabled = false, type, open = false, onOpenChange = noop, onOpenChangeComplete = noop, loop = false, scrollAlignment = "nearest", required = false, items = [], allowDeselect = false, autocomplete, children } = $$props;
		function handleDefaultValue() {
			if (value !== void 0) return;
			value = type === "single" ? "" : [];
		}
		handleDefaultValue();
		watch.pre(() => value, () => {
			handleDefaultValue();
		});
		let inputValue = "";
		const rootState = SelectRootState.create({
			type,
			value: boxWith(() => value, (v) => {
				value = v;
				onValueChange(v);
			}),
			disabled: boxWith(() => disabled),
			required: boxWith(() => required),
			open: boxWith(() => open, (v) => {
				open = v;
				onOpenChange(v);
			}),
			loop: boxWith(() => loop),
			scrollAlignment: boxWith(() => scrollAlignment),
			name: boxWith(() => name),
			isCombobox: false,
			items: boxWith(() => items),
			allowDeselect: boxWith(() => allowDeselect),
			inputValue: boxWith(() => inputValue, (v) => inputValue = v),
			onOpenChangeComplete: boxWith(() => onOpenChangeComplete)
		});
		let $$settled = true;
		let $$inner_renderer;
		function $$render_inner($$renderer) {
			Floating_layer($$renderer, {
				children: ($$renderer) => {
					children?.($$renderer);
					$$renderer.push(`<!---->`);
				},
				$$slots: { default: true }
			});
			$$renderer.push(`<!----> `);
			if (Array.isArray(rootState.opts.value.current)) {
				$$renderer.push("<!--[0-->");
				if (rootState.opts.value.current.length === 0) {
					$$renderer.push("<!--[0-->");
					Select_hidden_input($$renderer, { autocomplete });
				} else {
					$$renderer.push("<!--[-1-->");
					$$renderer.push(`<!--[-->`);
					const each_array = ensure_array_like(rootState.opts.value.current);
					for (let $$index = 0, $$length = each_array.length; $$index < $$length; $$index++) {
						let item = each_array[$$index];
						Select_hidden_input($$renderer, {
							value: item,
							autocomplete
						});
					}
					$$renderer.push(`<!--]-->`);
				}
				$$renderer.push(`<!--]-->`);
			} else {
				$$renderer.push("<!--[-1-->");
				Select_hidden_input($$renderer, {
					autocomplete,
					get value() {
						return rootState.opts.value.current;
					},
					set value($$value) {
						rootState.opts.value.current = $$value;
						$$settled = false;
					}
				});
			}
			$$renderer.push(`<!--]-->`);
		}
		do {
			$$settled = true;
			$$inner_renderer = $$renderer.copy();
			$$render_inner($$inner_renderer);
		} while (!$$settled);
		$$renderer.subsume($$inner_renderer);
		bind_props($$props, {
			value,
			open
		});
	});
}
//#endregion
//#region node_modules/bits-ui/dist/bits/select/components/select-trigger.svelte
function Select_trigger$1($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		const uid = props_id($$renderer);
		let { id = createId(uid), ref = null, child, children, type = "button", $$slots, $$events, ...restProps } = $$props;
		const triggerState = SelectTriggerState.create({
			id: boxWith(() => id),
			ref: boxWith(() => ref, (v) => ref = v)
		});
		const mergedProps = derived(() => mergeProps(restProps, triggerState.props, { type }));
		if (Floating_layer_anchor) {
			$$renderer.push("<!--[-->");
			Floating_layer_anchor($$renderer, {
				id,
				ref: triggerState.opts.ref,
				children: ($$renderer) => {
					if (child) {
						$$renderer.push("<!--[0-->");
						child($$renderer, { props: mergedProps() });
						$$renderer.push(`<!---->`);
					} else {
						$$renderer.push("<!--[-1-->");
						$$renderer.push(`<button${attributes({ ...mergedProps() })}>`);
						children?.($$renderer);
						$$renderer.push(`<!----></button>`);
					}
					$$renderer.push(`<!--]-->`);
				},
				$$slots: { default: true }
			});
			$$renderer.push("<!--]-->");
		} else {
			$$renderer.push("<!--[!-->");
			$$renderer.push("<!--]-->");
		}
		bind_props($$props, { ref });
	});
}
//#endregion
//#region node_modules/bits-ui/dist/bits/toggle-group/toggle-group.svelte.js
var toggleGroupAttrs = createBitsAttrs({
	component: "toggle-group",
	parts: ["root", "item"]
});
var ToggleGroupRootContext = new Context("ToggleGroup.Root");
var ToggleGroupBaseState = class {
	opts;
	rovingFocusGroup;
	attachment;
	constructor(opts) {
		this.opts = opts;
		this.attachment = attachRef(this.opts.ref);
		this.rovingFocusGroup = new RovingFocusGroup({
			candidateAttr: toggleGroupAttrs.item,
			rootNode: opts.ref,
			loop: opts.loop,
			orientation: opts.orientation
		});
	}
	#props = derived(() => ({
		id: this.opts.id.current,
		[toggleGroupAttrs.root]: "",
		role: "group",
		"data-orientation": this.opts.orientation.current,
		"data-disabled": boolToEmptyStrOrUndef(this.opts.disabled.current),
		...this.attachment
	}));
	get props() {
		return this.#props();
	}
	set props($$value) {
		return this.#props($$value);
	}
};
var ToggleGroupSingleState = class extends ToggleGroupBaseState {
	opts;
	isMulti = false;
	#anyPressed = derived(() => this.opts.value.current !== "");
	get anyPressed() {
		return this.#anyPressed();
	}
	set anyPressed($$value) {
		return this.#anyPressed($$value);
	}
	constructor(opts) {
		super(opts);
		this.opts = opts;
	}
	includesItem(item) {
		return this.opts.value.current === item;
	}
	toggleItem(item, id) {
		if (this.includesItem(item)) this.opts.value.current = "";
		else {
			this.opts.value.current = item;
			this.rovingFocusGroup.setCurrentTabStopId(id);
		}
	}
};
var ToggleGroupMultipleState = class extends ToggleGroupBaseState {
	opts;
	isMulti = true;
	#anyPressed = derived(() => this.opts.value.current.length > 0);
	get anyPressed() {
		return this.#anyPressed();
	}
	set anyPressed($$value) {
		return this.#anyPressed($$value);
	}
	constructor(opts) {
		super(opts);
		this.opts = opts;
	}
	includesItem(item) {
		return this.opts.value.current.includes(item);
	}
	toggleItem(item, id) {
		if (this.includesItem(item)) this.opts.value.current = this.opts.value.current.filter((v) => v !== item);
		else {
			this.opts.value.current = [...this.opts.value.current, item];
			this.rovingFocusGroup.setCurrentTabStopId(id);
		}
	}
};
var ToggleGroupRootState = class {
	static create(opts) {
		const { type, ...rest } = opts;
		const rootState = type === "single" ? new ToggleGroupSingleState(rest) : new ToggleGroupMultipleState(rest);
		return ToggleGroupRootContext.set(rootState);
	}
};
var ToggleGroupItemState = class ToggleGroupItemState {
	static create(opts) {
		return new ToggleGroupItemState(opts, ToggleGroupRootContext.get());
	}
	opts;
	root;
	attachment;
	#isDisabled = derived(() => this.opts.disabled.current || this.root.opts.disabled.current);
	#isPressed = derived(() => this.root.includesItem(this.opts.value.current));
	get isPressed() {
		return this.#isPressed();
	}
	set isPressed($$value) {
		return this.#isPressed($$value);
	}
	#ariaChecked = derived(() => {
		return this.root.isMulti ? void 0 : getAriaChecked(this.isPressed, false);
	});
	#ariaPressed = derived(() => {
		return this.root.isMulti ? boolToStr(this.isPressed) : void 0;
	});
	constructor(opts, root) {
		this.opts = opts;
		this.root = root;
		this.attachment = attachRef(this.opts.ref);
		this.onclick = this.onclick.bind(this);
		this.onkeydown = this.onkeydown.bind(this);
	}
	#toggleItem() {
		if (this.#isDisabled()) return;
		this.root.toggleItem(this.opts.value.current, this.opts.id.current);
	}
	onclick(_) {
		if (this.#isDisabled()) return;
		this.root.toggleItem(this.opts.value.current, this.opts.id.current);
	}
	onkeydown(e) {
		if (this.#isDisabled()) return;
		if (e.key === "Enter" || e.key === " ") {
			e.preventDefault();
			this.#toggleItem();
			return;
		}
		if (!this.root.opts.rovingFocus.current) return;
		this.root.rovingFocusGroup.handleKeydown(this.opts.ref.current, e);
	}
	#tabIndex = 0;
	#snippetProps = derived(() => ({ pressed: this.isPressed }));
	get snippetProps() {
		return this.#snippetProps();
	}
	set snippetProps($$value) {
		return this.#snippetProps($$value);
	}
	#props = derived(() => ({
		id: this.opts.id.current,
		role: this.root.isMulti ? void 0 : "radio",
		tabindex: this.#tabIndex,
		"data-orientation": this.root.opts.orientation.current,
		"data-disabled": boolToEmptyStrOrUndef(this.#isDisabled()),
		"data-state": getToggleItemDataState(this.isPressed),
		"data-value": this.opts.value.current,
		"aria-pressed": this.#ariaPressed(),
		"aria-checked": this.#ariaChecked(),
		disabled: boolToTrueOrUndef(this.#isDisabled()),
		[toggleGroupAttrs.item]: "",
		onclick: this.onclick,
		onkeydown: this.onkeydown,
		...this.attachment
	}));
	get props() {
		return this.#props();
	}
	set props($$value) {
		return this.#props($$value);
	}
};
function getToggleItemDataState(condition) {
	return condition ? "on" : "off";
}
//#endregion
//#region node_modules/bits-ui/dist/bits/toggle-group/components/toggle-group.svelte
function Toggle_group$1($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		const uid = props_id($$renderer);
		let { id = createId(uid), ref = null, value = void 0, onValueChange = noop, type, disabled = false, loop = true, orientation = "horizontal", rovingFocus = true, child, children, $$slots, $$events, ...restProps } = $$props;
		function handleDefaultValue() {
			if (value !== void 0) return;
			value = type === "single" ? "" : [];
		}
		handleDefaultValue();
		watch.pre(() => value, () => {
			handleDefaultValue();
		});
		const rootState = ToggleGroupRootState.create({
			id: boxWith(() => id),
			value: boxWith(() => value, (v) => {
				value = v;
				onValueChange(v);
			}),
			disabled: boxWith(() => disabled),
			loop: boxWith(() => loop),
			orientation: boxWith(() => orientation),
			rovingFocus: boxWith(() => rovingFocus),
			type,
			ref: boxWith(() => ref, (v) => ref = v)
		});
		const mergedProps = derived(() => mergeProps(restProps, rootState.props));
		if (child) {
			$$renderer.push("<!--[0-->");
			child($$renderer, { props: mergedProps() });
			$$renderer.push(`<!---->`);
		} else {
			$$renderer.push("<!--[-1-->");
			$$renderer.push(`<div${attributes({ ...mergedProps() })}>`);
			children?.($$renderer);
			$$renderer.push(`<!----></div>`);
		}
		$$renderer.push(`<!--]-->`);
		bind_props($$props, {
			ref,
			value
		});
	});
}
//#endregion
//#region node_modules/bits-ui/dist/bits/toggle-group/components/toggle-group-item.svelte
function Toggle_group_item$1($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		const uid = props_id($$renderer);
		let { children, child, ref = null, value, disabled = false, id = createId(uid), type = "button", $$slots, $$events, ...restProps } = $$props;
		const itemState = ToggleGroupItemState.create({
			id: boxWith(() => id),
			value: boxWith(() => value),
			disabled: boxWith(() => disabled ?? false),
			ref: boxWith(() => ref, (v) => ref = v)
		});
		const mergedProps = derived(() => mergeProps(restProps, itemState.props, { type }));
		if (child) {
			$$renderer.push("<!--[0-->");
			child($$renderer, {
				props: mergedProps(),
				...itemState.snippetProps
			});
			$$renderer.push(`<!---->`);
		} else {
			$$renderer.push("<!--[-1-->");
			$$renderer.push(`<button${attributes({ ...mergedProps() })}>`);
			children?.($$renderer, itemState.snippetProps);
			$$renderer.push(`<!----></button>`);
		}
		$$renderer.push(`<!--]-->`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region node_modules/bits-ui/dist/internal/timeout-fn.js
var TimeoutFn = class {
	#interval;
	#cb;
	#timer = null;
	constructor(cb, interval) {
		this.#cb = cb;
		this.#interval = interval;
		this.stop = this.stop.bind(this);
		this.start = this.start.bind(this);
		this.stop;
	}
	#clear() {
		if (this.#timer !== null) {
			window.clearTimeout(this.#timer);
			this.#timer = null;
		}
	}
	stop() {
		this.#clear();
	}
	start(...args) {
		this.#clear();
		this.#timer = window.setTimeout(() => {
			this.#timer = null;
			this.#cb(...args);
		}, this.#interval);
	}
};
//#endregion
//#region node_modules/bits-ui/dist/bits/tooltip/tooltip.svelte.js
var tooltipAttrs = createBitsAttrs({
	component: "tooltip",
	parts: ["content", "trigger"]
});
var TooltipProviderContext = new Context("Tooltip.Provider");
var TooltipRootContext = new Context("Tooltip.Root");
var TooltipTriggerRegistryState = class {
	triggers = /* @__PURE__ */ new Map();
	activeTriggerId = null;
	#activeTriggerNode = derived(() => {
		const activeTriggerId = this.activeTriggerId;
		if (activeTriggerId === null) return null;
		return this.triggers.get(activeTriggerId)?.node ?? null;
	});
	get activeTriggerNode() {
		return this.#activeTriggerNode();
	}
	set activeTriggerNode($$value) {
		return this.#activeTriggerNode($$value);
	}
	#activePayload = derived(() => {
		const activeTriggerId = this.activeTriggerId;
		if (activeTriggerId === null) return null;
		return this.triggers.get(activeTriggerId)?.payload ?? null;
	});
	get activePayload() {
		return this.#activePayload();
	}
	set activePayload($$value) {
		return this.#activePayload($$value);
	}
	register = (record) => {
		const next = new Map(this.triggers);
		next.set(record.id, record);
		this.triggers = next;
		this.#coerceActiveTrigger();
	};
	update = (record) => {
		const next = new Map(this.triggers);
		next.set(record.id, record);
		this.triggers = next;
		this.#coerceActiveTrigger();
	};
	unregister = (id) => {
		if (!this.triggers.has(id)) return;
		const next = new Map(this.triggers);
		next.delete(id);
		this.triggers = next;
		if (this.activeTriggerId === id) this.activeTriggerId = null;
	};
	setActiveTrigger = (id) => {
		if (id === null) {
			this.activeTriggerId = null;
			return;
		}
		if (!this.triggers.has(id)) {
			this.activeTriggerId = null;
			return;
		}
		this.activeTriggerId = id;
	};
	get = (id) => {
		return this.triggers.get(id);
	};
	has = (id) => {
		return this.triggers.has(id);
	};
	getFirstTriggerId = () => {
		const firstEntry = this.triggers.entries().next();
		if (firstEntry.done) return null;
		return firstEntry.value[0];
	};
	#coerceActiveTrigger = () => {
		const activeTriggerId = this.activeTriggerId;
		if (activeTriggerId === null) return;
		if (!this.triggers.has(activeTriggerId)) this.activeTriggerId = null;
	};
};
var TooltipProviderState = class TooltipProviderState {
	static create(opts) {
		return TooltipProviderContext.set(new TooltipProviderState(opts));
	}
	opts;
	isOpenDelayed = true;
	isPointerInTransit = simpleBox(false);
	#timerFn;
	#openTooltip = null;
	constructor(opts) {
		this.opts = opts;
		this.#timerFn = new TimeoutFn(() => {
			this.isOpenDelayed = true;
		}, this.opts.skipDelayDuration.current);
	}
	#startTimer = () => {
		if (this.opts.skipDelayDuration.current === 0) {
			this.isOpenDelayed = true;
			return;
		} else this.#timerFn.start();
	};
	#clearTimer = () => {
		this.#timerFn.stop();
	};
	onOpen = (tooltip) => {
		if (this.#openTooltip && this.#openTooltip !== tooltip) this.#openTooltip.handleClose();
		this.#clearTimer();
		this.isOpenDelayed = false;
		this.#openTooltip = tooltip;
	};
	onClose = (tooltip) => {
		if (this.#openTooltip === tooltip) {
			this.#openTooltip = null;
			this.#startTimer();
		}
	};
	isTooltipOpen = (tooltip) => {
		return this.#openTooltip === tooltip;
	};
};
var TooltipRootState = class TooltipRootState {
	static create(opts) {
		return TooltipRootContext.set(new TooltipRootState(opts, TooltipProviderContext.get()));
	}
	opts;
	provider;
	#delayDuration = derived(() => this.opts.delayDuration.current ?? this.provider.opts.delayDuration.current);
	get delayDuration() {
		return this.#delayDuration();
	}
	set delayDuration($$value) {
		return this.#delayDuration($$value);
	}
	#disableHoverableContent = derived(() => this.opts.disableHoverableContent.current ?? this.provider.opts.disableHoverableContent.current);
	get disableHoverableContent() {
		return this.#disableHoverableContent();
	}
	set disableHoverableContent($$value) {
		return this.#disableHoverableContent($$value);
	}
	#disableCloseOnTriggerClick = derived(() => this.opts.disableCloseOnTriggerClick.current ?? this.provider.opts.disableCloseOnTriggerClick.current);
	get disableCloseOnTriggerClick() {
		return this.#disableCloseOnTriggerClick();
	}
	set disableCloseOnTriggerClick($$value) {
		return this.#disableCloseOnTriggerClick($$value);
	}
	#disabled = derived(() => this.opts.disabled.current ?? this.provider.opts.disabled.current);
	get disabled() {
		return this.#disabled();
	}
	set disabled($$value) {
		return this.#disabled($$value);
	}
	#ignoreNonKeyboardFocus = derived(() => this.opts.ignoreNonKeyboardFocus.current ?? this.provider.opts.ignoreNonKeyboardFocus.current);
	get ignoreNonKeyboardFocus() {
		return this.#ignoreNonKeyboardFocus();
	}
	set ignoreNonKeyboardFocus($$value) {
		return this.#ignoreNonKeyboardFocus($$value);
	}
	registry;
	tether;
	contentNode = null;
	contentPresence;
	#wasOpenDelayed = false;
	#timerFn;
	#stateAttr = derived(() => {
		if (!this.opts.open.current) return "closed";
		return this.#wasOpenDelayed ? "delayed-open" : "instant-open";
	});
	get stateAttr() {
		return this.#stateAttr();
	}
	set stateAttr($$value) {
		return this.#stateAttr($$value);
	}
	constructor(opts, provider) {
		this.opts = opts;
		this.provider = provider;
		this.tether = opts.tether.current?.state ?? null;
		this.registry = this.tether?.registry ?? new TooltipTriggerRegistryState();
		this.#timerFn = new TimeoutFn(() => {
			this.#wasOpenDelayed = true;
			this.opts.open.current = true;
		}, this.delayDuration ?? 0);
		if (this.tether) this.tether.root = this;
		this.contentPresence = new PresenceManager({
			open: this.opts.open,
			ref: boxWith(() => this.contentNode),
			onComplete: () => {
				this.opts.onOpenChangeComplete.current(this.opts.open.current);
			}
		});
		watch(() => this.delayDuration, () => {
			if (this.delayDuration === void 0) return;
			this.#timerFn = new TimeoutFn(() => {
				this.#wasOpenDelayed = true;
				this.opts.open.current = true;
			}, this.delayDuration);
		});
		watch(() => this.opts.open.current, (isOpen) => {
			if (isOpen) {
				this.ensureActiveTrigger();
				this.provider.onOpen(this);
			} else this.provider.onClose(this);
		}, { lazy: true });
		watch(() => this.opts.triggerId.current, (triggerId) => {
			if (triggerId === this.registry.activeTriggerId) return;
			this.registry.setActiveTrigger(triggerId);
		});
		watch(() => this.registry.activeTriggerId, (activeTriggerId) => {
			if (this.opts.triggerId.current === activeTriggerId) return;
			this.opts.triggerId.current = activeTriggerId;
		});
	}
	handleOpen = () => {
		this.#timerFn.stop();
		this.#wasOpenDelayed = false;
		this.ensureActiveTrigger();
		this.opts.open.current = true;
	};
	handleClose = () => {
		this.#timerFn.stop();
		this.opts.open.current = false;
	};
	#handleDelayedOpen = () => {
		this.#timerFn.stop();
		const shouldSkipDelay = !this.provider.isOpenDelayed;
		const delayDuration = this.delayDuration ?? 0;
		if (shouldSkipDelay || delayDuration === 0) {
			this.#wasOpenDelayed = false;
			this.opts.open.current = true;
		} else this.#timerFn.start();
	};
	onTriggerEnter = (triggerId) => {
		this.setActiveTrigger(triggerId);
		this.#handleDelayedOpen();
	};
	onTriggerLeave = () => {
		if (this.disableHoverableContent) this.handleClose();
		else this.#timerFn.stop();
	};
	ensureActiveTrigger = () => {
		if (this.registry.activeTriggerId !== null && this.registry.has(this.registry.activeTriggerId)) return;
		if (this.opts.triggerId.current !== null && this.registry.has(this.opts.triggerId.current)) {
			this.registry.setActiveTrigger(this.opts.triggerId.current);
			return;
		}
		const firstTriggerId = this.registry.getFirstTriggerId();
		this.registry.setActiveTrigger(firstTriggerId);
	};
	setActiveTrigger = (triggerId) => {
		this.registry.setActiveTrigger(triggerId);
	};
	registerTrigger = (trigger) => {
		this.registry.register(trigger);
		if (trigger.disabled && this.registry.activeTriggerId === trigger.id && this.opts.open.current) this.handleClose();
	};
	updateTrigger = (trigger) => {
		this.registry.update(trigger);
		if (trigger.disabled && this.registry.activeTriggerId === trigger.id && this.opts.open.current) this.handleClose();
	};
	unregisterTrigger = (id) => {
		const isActive = this.registry.activeTriggerId === id;
		this.registry.unregister(id);
		if (isActive && this.opts.open.current) this.handleClose();
	};
	isActiveTrigger = (triggerId) => {
		return this.registry.activeTriggerId === triggerId;
	};
	get triggerNode() {
		return this.registry.activeTriggerNode;
	}
	get activePayload() {
		return this.registry.activePayload;
	}
	get activeTriggerId() {
		return this.registry.activeTriggerId;
	}
};
var TooltipTriggerState = class TooltipTriggerState {
	static create(opts) {
		if (opts.tether.current) return new TooltipTriggerState(opts, null, opts.tether.current.state);
		return new TooltipTriggerState(opts, TooltipRootContext.get(), null);
	}
	opts;
	root;
	tether;
	attachment;
	#isPointerDown = simpleBox(false);
	#hasPointerMoveOpened = false;
	domContext;
	#transitCheckTimeout = null;
	#mounted = false;
	#lastRegisteredId = null;
	constructor(opts, root, tether) {
		this.opts = opts;
		this.root = root;
		this.tether = tether;
		this.domContext = new DOMContext(opts.ref);
		this.attachment = attachRef(this.opts.ref, (v) => this.#register(v));
		watch(() => this.opts.id.current, () => {
			this.#register(this.opts.ref.current);
		});
		watch(() => this.opts.payload.current, () => {
			this.#register(this.opts.ref.current);
		});
		watch(() => this.opts.disabled.current, () => {
			this.#register(this.opts.ref.current);
		});
	}
	#getRoot = () => {
		return this.tether?.root ?? this.root;
	};
	#isDisabled = () => {
		const root = this.#getRoot();
		return this.opts.disabled.current || Boolean(root?.disabled);
	};
	#register = (node) => {
		if (!this.#mounted) return;
		const id = this.opts.id.current;
		const payload = this.opts.payload.current;
		const disabled = this.opts.disabled.current;
		if (this.#lastRegisteredId && this.#lastRegisteredId !== id) {
			const root = this.#getRoot();
			if (this.tether) this.tether.registry.unregister(this.#lastRegisteredId);
			else root?.unregisterTrigger(this.#lastRegisteredId);
		}
		const triggerRecord = {
			id,
			node,
			payload,
			disabled
		};
		const root = this.#getRoot();
		if (this.tether) {
			if (this.tether.registry.has(id)) this.tether.registry.update(triggerRecord);
			else this.tether.registry.register(triggerRecord);
			if (disabled && this.tether.registry.activeTriggerId === id && root?.opts.open.current) root.handleClose();
		} else if (root?.registry.has(id)) root.updateTrigger(triggerRecord);
		else root?.registerTrigger(triggerRecord);
		this.#lastRegisteredId = id;
	};
	#clearTransitCheck = () => {
		if (this.#transitCheckTimeout !== null) {
			clearTimeout(this.#transitCheckTimeout);
			this.#transitCheckTimeout = null;
		}
	};
	handlePointerUp = () => {
		this.#isPointerDown.current = false;
	};
	#onpointerup = () => {
		if (this.#isDisabled()) return;
		this.#isPointerDown.current = false;
	};
	#onpointerdown = () => {
		if (this.#isDisabled()) return;
		this.#isPointerDown.current = true;
		this.domContext.getDocument().addEventListener("pointerup", () => {
			this.handlePointerUp();
		}, { once: true });
	};
	#onpointerenter = (e) => {
		const root = this.#getRoot();
		if (!root) return;
		if (this.#isDisabled()) {
			if (root.opts.open.current) root.handleClose();
			return;
		}
		if (e.pointerType === "touch") return;
		if (root.provider.isPointerInTransit.current) {
			this.#clearTransitCheck();
			this.#transitCheckTimeout = window.setTimeout(() => {
				if (root.provider.isPointerInTransit.current) {
					root.provider.isPointerInTransit.current = false;
					root.onTriggerEnter(this.opts.id.current);
					this.#hasPointerMoveOpened = true;
				}
			}, 250);
			return;
		}
		root.onTriggerEnter(this.opts.id.current);
		this.#hasPointerMoveOpened = true;
	};
	#onpointermove = (e) => {
		const root = this.#getRoot();
		if (!root) return;
		if (this.#isDisabled()) {
			if (root.opts.open.current) root.handleClose();
			return;
		}
		if (e.pointerType === "touch") return;
		if (this.#hasPointerMoveOpened) return;
		this.#clearTransitCheck();
		root.provider.isPointerInTransit.current = false;
		root.onTriggerEnter(this.opts.id.current);
		this.#hasPointerMoveOpened = true;
	};
	#onpointerleave = (e) => {
		const root = this.#getRoot();
		if (!root) return;
		if (this.#isDisabled()) return;
		this.#clearTransitCheck();
		if (!root.isActiveTrigger(this.opts.id.current)) {
			this.#hasPointerMoveOpened = false;
			return;
		}
		const relatedTarget = e.relatedTarget;
		if (isElement(relatedTarget)) for (const record of root.registry.triggers.values()) {
			if (record.node !== relatedTarget) continue;
			if (root.provider.opts.skipDelayDuration.current > 0) {
				this.#hasPointerMoveOpened = false;
				return;
			}
			root.handleClose();
			this.#hasPointerMoveOpened = false;
			return;
		}
		root.onTriggerLeave();
		this.#hasPointerMoveOpened = false;
	};
	#onfocus = (e) => {
		const root = this.#getRoot();
		if (!root) return;
		if (this.#isPointerDown.current) return;
		if (this.#isDisabled()) {
			if (root.opts.open.current) root.handleClose();
			return;
		}
		if (root.ignoreNonKeyboardFocus && !isFocusVisible(e.currentTarget)) return;
		root.setActiveTrigger(this.opts.id.current);
		root.handleOpen();
	};
	#onblur = () => {
		const root = this.#getRoot();
		if (!root || this.#isDisabled()) return;
		root.handleClose();
	};
	#onclick = () => {
		const root = this.#getRoot();
		if (!root || root.disableCloseOnTriggerClick || this.#isDisabled()) return;
		root.handleClose();
	};
	#props = derived(() => {
		const root = this.#getRoot();
		const isOpenForTrigger = Boolean(root?.opts.open.current && root.isActiveTrigger(this.opts.id.current));
		const isDisabled = this.#isDisabled();
		return {
			id: this.opts.id.current,
			"aria-describedby": isOpenForTrigger ? root?.contentNode?.id : void 0,
			"data-state": isOpenForTrigger ? root?.stateAttr : "closed",
			"data-disabled": boolToEmptyStrOrUndef(isDisabled),
			"data-delay-duration": `${root?.delayDuration ?? 0}`,
			[tooltipAttrs.trigger]: "",
			tabindex: isDisabled ? void 0 : this.opts.tabindex.current,
			disabled: this.opts.disabled.current,
			onpointerup: this.#onpointerup,
			onpointerdown: this.#onpointerdown,
			onpointerenter: this.#onpointerenter,
			onpointermove: this.#onpointermove,
			onpointerleave: this.#onpointerleave,
			onfocus: this.#onfocus,
			onblur: this.#onblur,
			onclick: this.#onclick,
			...this.attachment
		};
	});
	get props() {
		return this.#props();
	}
	set props($$value) {
		return this.#props($$value);
	}
};
var TooltipContentState = class TooltipContentState {
	static create(opts) {
		return new TooltipContentState(opts, TooltipRootContext.get());
	}
	opts;
	root;
	attachment;
	constructor(opts, root) {
		this.opts = opts;
		this.root = root;
		this.attachment = attachRef(this.opts.ref, (v) => this.root.contentNode = v);
		new SafePolygon({
			triggerNode: () => this.root.triggerNode,
			contentNode: () => this.root.contentNode,
			enabled: () => this.root.opts.open.current && !this.root.disableHoverableContent,
			transitIntentTimeout: 180,
			ignoredTargets: () => {
				if (this.root.provider.opts.skipDelayDuration.current === 0) return [];
				const nodes = [];
				const activeTriggerNode = this.root.triggerNode;
				for (const record of this.root.registry.triggers.values()) if (record.node && record.node !== activeTriggerNode) nodes.push(record.node);
				return nodes;
			},
			onPointerExit: () => {
				if (this.root.provider.isTooltipOpen(this.root)) this.root.handleClose();
			}
		});
	}
	onInteractOutside = (e) => {
		if (isElement(e.target) && this.root.triggerNode?.contains(e.target) && this.root.disableCloseOnTriggerClick) {
			e.preventDefault();
			return;
		}
		this.opts.onInteractOutside.current(e);
		if (e.defaultPrevented) return;
		this.root.handleClose();
	};
	onEscapeKeydown = (e) => {
		this.opts.onEscapeKeydown.current?.(e);
		if (e.defaultPrevented) return;
		this.root.handleClose();
	};
	onOpenAutoFocus = (e) => {
		e.preventDefault();
	};
	onCloseAutoFocus = (e) => {
		e.preventDefault();
	};
	get shouldRender() {
		return this.root.contentPresence.shouldRender;
	}
	#snippetProps = derived(() => ({ open: this.root.opts.open.current }));
	get snippetProps() {
		return this.#snippetProps();
	}
	set snippetProps($$value) {
		return this.#snippetProps($$value);
	}
	#props = derived(() => ({
		id: this.opts.id.current,
		"data-state": this.root.stateAttr,
		"data-disabled": boolToEmptyStrOrUndef(this.root.disabled),
		...getDataTransitionAttrs(this.root.contentPresence.transitionStatus),
		style: { outline: "none" },
		[tooltipAttrs.content]: "",
		...this.attachment
	}));
	get props() {
		return this.#props();
	}
	set props($$value) {
		return this.#props($$value);
	}
	popperProps = {
		onInteractOutside: this.onInteractOutside,
		onEscapeKeydown: this.onEscapeKeydown,
		onOpenAutoFocus: this.onOpenAutoFocus,
		onCloseAutoFocus: this.onCloseAutoFocus
	};
};
//#endregion
//#region node_modules/bits-ui/dist/bits/tooltip/components/tooltip.svelte
function Tooltip$1($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { open = false, triggerId = null, onOpenChange = noop, onOpenChangeComplete = noop, disabled, delayDuration, disableCloseOnTriggerClick, disableHoverableContent, ignoreNonKeyboardFocus, tether, children } = $$props;
		const rootState = TooltipRootState.create({
			open: boxWith(() => open, (v) => {
				open = v;
				onOpenChange(v);
			}),
			triggerId: boxWith(() => triggerId, (v) => {
				triggerId = v;
			}),
			delayDuration: boxWith(() => delayDuration),
			disableCloseOnTriggerClick: boxWith(() => disableCloseOnTriggerClick),
			disableHoverableContent: boxWith(() => disableHoverableContent),
			ignoreNonKeyboardFocus: boxWith(() => ignoreNonKeyboardFocus),
			disabled: boxWith(() => disabled),
			onOpenChangeComplete: boxWith(() => onOpenChangeComplete),
			tether: boxWith(() => tether)
		});
		Floating_layer($$renderer, {
			tooltip: true,
			children: ($$renderer) => {
				children?.($$renderer, {
					open: rootState.opts.open.current,
					triggerId: rootState.activeTriggerId,
					payload: rootState.activePayload
				});
				$$renderer.push(`<!---->`);
			},
			$$slots: { default: true }
		});
		bind_props($$props, {
			open,
			triggerId
		});
	});
}
//#endregion
//#region node_modules/bits-ui/dist/bits/tooltip/components/tooltip-content.svelte
function Tooltip_content$1($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		const uid = props_id($$renderer);
		let { children, child, id = createId(uid), ref = null, side = "top", sideOffset = 0, align = "center", avoidCollisions = true, arrowPadding = 0, sticky = "partial", strategy, hideWhenDetached = false, customAnchor, collisionPadding = 0, onInteractOutside = noop, onEscapeKeydown = noop, forceMount = false, style, $$slots, $$events, ...restProps } = $$props;
		const contentState = TooltipContentState.create({
			id: boxWith(() => id),
			ref: boxWith(() => ref, (v) => ref = v),
			onInteractOutside: boxWith(() => onInteractOutside),
			onEscapeKeydown: boxWith(() => onEscapeKeydown)
		});
		const floatingProps = derived(() => ({
			side,
			sideOffset,
			align,
			avoidCollisions,
			arrowPadding,
			sticky,
			hideWhenDetached,
			collisionPadding,
			strategy,
			customAnchor: customAnchor ?? contentState.root.triggerNode
		}));
		const mergedProps = derived(() => mergeProps(restProps, floatingProps(), contentState.props));
		if (forceMount) {
			$$renderer.push("<!--[0-->");
			{
				function popper($$renderer, { props, wrapperProps }) {
					const finalWrapperProps = mergeProps(wrapperProps, { style: { pointerEvents: contentState.root.disableHoverableContent ? "none" : void 0 } });
					const finalProps = mergeProps(props, { style: getFloatingContentCSSVars("tooltip") }, { style });
					if (child) {
						$$renderer.push("<!--[0-->");
						child($$renderer, {
							props: finalProps,
							wrapperProps: finalWrapperProps,
							...contentState.snippetProps
						});
						$$renderer.push(`<!---->`);
					} else {
						$$renderer.push("<!--[-1-->");
						$$renderer.push(`<div${attributes({ ...finalWrapperProps })}><div${attributes({ ...finalProps })}>`);
						children?.($$renderer);
						$$renderer.push(`<!----></div></div>`);
					}
					$$renderer.push(`<!--]-->`);
				}
				Popper_layer_force_mount($$renderer, spread_props([
					mergedProps(),
					contentState.popperProps,
					{
						enabled: contentState.root.opts.open.current,
						id,
						trapFocus: false,
						loop: false,
						preventScroll: false,
						forceMount: true,
						ref: contentState.opts.ref,
						tooltip: true,
						shouldRender: contentState.shouldRender,
						contentPointerEvents: contentState.root.disableHoverableContent ? "none" : "auto",
						popper,
						$$slots: { popper: true }
					}
				]));
			}
		} else if (!forceMount) {
			$$renderer.push("<!--[1-->");
			{
				function popper($$renderer, { props, wrapperProps }) {
					const finalWrapperProps = mergeProps(wrapperProps, { style: { pointerEvents: contentState.root.disableHoverableContent ? "none" : void 0 } });
					const finalProps = mergeProps(props, { style: getFloatingContentCSSVars("tooltip") }, { style });
					if (child) {
						$$renderer.push("<!--[0-->");
						child($$renderer, {
							props: finalProps,
							wrapperProps: finalWrapperProps,
							...contentState.snippetProps
						});
						$$renderer.push(`<!---->`);
					} else {
						$$renderer.push("<!--[-1-->");
						$$renderer.push(`<div${attributes({ ...finalWrapperProps })}><div${attributes({ ...finalProps })}>`);
						children?.($$renderer);
						$$renderer.push(`<!----></div></div>`);
					}
					$$renderer.push(`<!--]-->`);
				}
				Popper_layer($$renderer, spread_props([
					mergedProps(),
					contentState.popperProps,
					{
						open: contentState.root.opts.open.current,
						id,
						trapFocus: false,
						loop: false,
						preventScroll: false,
						forceMount: false,
						ref: contentState.opts.ref,
						tooltip: true,
						shouldRender: contentState.shouldRender,
						contentPointerEvents: contentState.root.disableHoverableContent ? "none" : "auto",
						popper,
						$$slots: { popper: true }
					}
				]));
			}
		} else $$renderer.push("<!--[-1-->");
		$$renderer.push(`<!--]-->`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region node_modules/bits-ui/dist/bits/tooltip/components/tooltip-trigger.svelte
function Tooltip_trigger$1($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		const uid = props_id($$renderer);
		let { children, child, id = createId(uid), disabled = false, payload, tether, type = "button", tabindex = 0, ref = null, $$slots, $$events, ...restProps } = $$props;
		const triggerState = TooltipTriggerState.create({
			id: boxWith(() => id),
			disabled: boxWith(() => disabled ?? false),
			tabindex: boxWith(() => tabindex ?? 0),
			payload: boxWith(() => payload),
			tether: boxWith(() => tether),
			ref: boxWith(() => ref, (v) => ref = v)
		});
		const mergedProps = derived(() => mergeProps(restProps, triggerState.props, { type }));
		if (child) {
			$$renderer.push("<!--[0-->");
			child($$renderer, { props: mergedProps() });
			$$renderer.push(`<!---->`);
		} else {
			$$renderer.push("<!--[-1-->");
			$$renderer.push(`<button${attributes({ ...mergedProps() })}>`);
			children?.($$renderer);
			$$renderer.push(`<!----></button>`);
		}
		$$renderer.push(`<!--]-->`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region node_modules/bits-ui/dist/bits/tooltip/components/tooltip-arrow.svelte
function Tooltip_arrow($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, $$slots, $$events, ...restProps } = $$props;
		let $$settled = true;
		let $$inner_renderer;
		function $$render_inner($$renderer) {
			Floating_layer_arrow($$renderer, spread_props([restProps, {
				get ref() {
					return ref;
				},
				set ref($$value) {
					ref = $$value;
					$$settled = false;
				}
			}]));
		}
		do {
			$$settled = true;
			$$inner_renderer = $$renderer.copy();
			$$render_inner($$inner_renderer);
		} while (!$$settled);
		$$renderer.subsume($$inner_renderer);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region node_modules/bits-ui/dist/bits/tooltip/components/tooltip-provider.svelte
function Tooltip_provider$1($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { children, delayDuration = 700, disableCloseOnTriggerClick = false, disableHoverableContent = false, disabled = false, ignoreNonKeyboardFocus = false, skipDelayDuration = 300 } = $$props;
		TooltipProviderState.create({
			delayDuration: boxWith(() => delayDuration),
			disableCloseOnTriggerClick: boxWith(() => disableCloseOnTriggerClick),
			disableHoverableContent: boxWith(() => disableHoverableContent),
			disabled: boxWith(() => disabled),
			ignoreNonKeyboardFocus: boxWith(() => ignoreNonKeyboardFocus),
			skipDelayDuration: boxWith(() => skipDelayDuration)
		});
		children?.($$renderer);
		$$renderer.push(`<!---->`);
	});
}
//#endregion
//#region node_modules/@lucide/svelte/dist/defaultAttributes.js
/**
* @file
* @license @lucide/svelte v1.30.0 - ISC
*
* This source code is licensed under the ISC license.
* See the LICENSE file in the root directory of this source tree.
*/
var defaultAttributes = {
	xmlns: "http://www.w3.org/2000/svg",
	width: 24,
	height: 24,
	viewBox: "0 0 24 24",
	fill: "none",
	stroke: "currentColor",
	"stroke-width": 2,
	"stroke-linecap": "round",
	"stroke-linejoin": "round"
};
//#endregion
//#region node_modules/@lucide/svelte/dist/utils/hasA11yProp.js
/**
* @file
* @license @lucide/svelte v1.30.0 - ISC
*
* This source code is licensed under the ISC license.
* See the LICENSE file in the root directory of this source tree.
*/
/**
* Check if a component has an accessibility prop
*
* @param {object} props
* @returns {boolean} Whether the component has an accessibility prop
*/
var hasA11yProp = (props) => {
	for (const prop in props) if (prop.startsWith("aria-") || prop === "role" || prop === "title") return true;
	return false;
};
//#endregion
//#region node_modules/@lucide/svelte/dist/context.js
/**
* @file
* @license @lucide/svelte v1.30.0 - ISC
*
* This source code is licensed under the ISC license.
* See the LICENSE file in the root directory of this source tree.
*/
var LucideContext = Symbol("lucide-context");
var getLucideContext = () => getContext(LucideContext);
//#endregion
//#region node_modules/@lucide/svelte/dist/Icon.svelte
function Icon($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		const globalProps = getLucideContext() ?? {};
		const { name, color = globalProps.color ?? "currentColor", size = globalProps.size ?? 24, strokeWidth = globalProps.strokeWidth ?? 2, absoluteStrokeWidth = globalProps.absoluteStrokeWidth ?? false, iconNode = [], children, $$slots, $$events, ...props } = $$props;
		const calculatedStrokeWidth = derived(() => absoluteStrokeWidth ? Number(strokeWidth) * 24 / Number(size) : strokeWidth);
		$$renderer.push(`<svg${attributes({
			...defaultAttributes,
			...!children && !hasA11yProp(props) && { "aria-hidden": "true" },
			...props,
			width: size,
			height: size,
			stroke: color,
			"stroke-width": calculatedStrokeWidth(),
			class: clsx$1([
				"lucide-icon lucide",
				globalProps.class,
				name && `lucide-${name}`,
				props.class
			])
		}, void 0, void 0, void 0, 3)}><!--[-->`);
		const each_array = ensure_array_like(iconNode);
		for (let $$index = 0, $$length = each_array.length; $$index < $$length; $$index++) {
			let [tag, attrs] = each_array[$$index];
			element($$renderer, tag, () => {
				$$renderer.push(`${attributes({ ...attrs }, void 0, void 0, void 0, 3)}`);
			});
		}
		$$renderer.push(`<!--]-->`);
		children?.($$renderer);
		$$renderer.push(`<!----></svg>`);
	});
}
//#endregion
//#region node_modules/@lucide/svelte/dist/icons/x.svelte
function X($$renderer, $$props) {
	let { $$slots, $$events, ...props } = $$props;
	Icon($$renderer, spread_props([
		{ name: "x" },
		props,
		{ iconNode: [["path", { "d": "M18 6 6 18" }], ["path", { "d": "m6 6 12 12" }]] }
	]));
}
//#endregion
//#region src/lib/utils.ts
function cn(...inputs) {
	return twMerge(clsx(inputs));
}
//#endregion
//#region src/lib/components/ui/button/button.svelte
var buttonVariants = tv({
	base: "rounded-4xl border border-transparent bg-clip-padding text-sm font-medium focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/30 active:not-aria-[haspopup]:translate-y-px aria-invalid:border-destructive aria-invalid:ring-3 aria-invalid:ring-destructive/20 dark:aria-invalid:border-destructive/50 dark:aria-invalid:ring-destructive/40 [&_svg:not([class*='size-'])]:size-4 group/button inline-flex shrink-0 items-center justify-center whitespace-nowrap transition-all outline-none select-none disabled:pointer-events-none disabled:opacity-50 [&_svg]:pointer-events-none [&_svg]:shrink-0",
	variants: {
		variant: {
			default: "bg-primary text-primary-foreground hover:bg-primary/80",
			outline: "border-border bg-background hover:bg-muted hover:text-foreground aria-expanded:bg-muted aria-expanded:text-foreground dark:bg-transparent dark:hover:bg-input/30",
			secondary: "bg-secondary text-secondary-foreground hover:bg-secondary/80 aria-expanded:bg-secondary aria-expanded:text-secondary-foreground",
			ghost: "hover:bg-muted hover:text-foreground aria-expanded:bg-muted aria-expanded:text-foreground dark:hover:bg-muted/50",
			destructive: "bg-destructive/10 text-destructive hover:bg-destructive/20 focus-visible:border-destructive/40 focus-visible:ring-destructive/20 dark:bg-destructive/20 dark:hover:bg-destructive/30 dark:focus-visible:ring-destructive/40",
			link: "text-primary underline-offset-4 hover:underline"
		},
		size: {
			default: "h-9 gap-1.5 px-3 has-data-[icon=inline-end]:pr-2.5 has-data-[icon=inline-start]:pl-2.5",
			xs: "h-6 gap-1 px-2.5 text-xs has-data-[icon=inline-end]:pr-2 has-data-[icon=inline-start]:pl-2 [&_svg:not([class*='size-'])]:size-3",
			sm: "h-8 gap-1 px-3 has-data-[icon=inline-end]:pr-2 has-data-[icon=inline-start]:pl-2",
			lg: "h-10 gap-1.5 px-4 has-data-[icon=inline-end]:pr-3 has-data-[icon=inline-start]:pl-3",
			icon: "size-9",
			"icon-xs": "size-6 [&_svg:not([class*='size-'])]:size-3",
			"icon-sm": "size-8",
			"icon-lg": "size-10"
		}
	},
	defaultVariants: {
		variant: "default",
		size: "default"
	}
});
function Button($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { class: className, variant = "default", size = "default", ref = null, href = void 0, type = "button", disabled, children, $$slots, $$events, ...restProps } = $$props;
		if (href) {
			$$renderer.push("<!--[0-->");
			$$renderer.push(`<a${attributes({
				"data-slot": "button",
				class: clsx$1(cn(buttonVariants({
					variant,
					size
				}), className)),
				href: disabled ? void 0 : href,
				"aria-disabled": disabled,
				role: disabled ? "link" : void 0,
				tabindex: disabled ? -1 : void 0,
				...restProps
			})}>`);
			children?.($$renderer);
			$$renderer.push(`<!----></a>`);
		} else {
			$$renderer.push("<!--[-1-->");
			$$renderer.push(`<button${attributes({
				"data-slot": "button",
				class: clsx$1(cn(buttonVariants({
					variant,
					size
				}), className)),
				type,
				disabled,
				...restProps
			})}>`);
			children?.($$renderer);
			$$renderer.push(`<!----></button>`);
		}
		$$renderer.push(`<!--]-->`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/dialog/dialog-portal.svelte
function Dialog_portal($$renderer, $$props) {
	let { $$slots, $$events, ...restProps } = $$props;
	if (Portal) {
		$$renderer.push("<!--[-->");
		Portal($$renderer, spread_props([restProps]));
		$$renderer.push("<!--]-->");
	} else {
		$$renderer.push("<!--[!-->");
		$$renderer.push("<!--]-->");
	}
}
//#endregion
//#region src/lib/components/ui/dialog/dialog-content.svelte
function Dialog_content($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, portalProps, children, showCloseButton = true, $$slots, $$events, ...restProps } = $$props;
		let $$settled = true;
		let $$inner_renderer;
		function $$render_inner($$renderer) {
			Dialog_portal($$renderer, spread_props([portalProps, {
				children: ($$renderer) => {
					if (Dialog_overlay) {
						$$renderer.push("<!--[-->");
						Dialog_overlay($$renderer, {});
						$$renderer.push("<!--]-->");
					} else {
						$$renderer.push("<!--[!-->");
						$$renderer.push("<!--]-->");
					}
					$$renderer.push(` `);
					if (Dialog_content$1) {
						$$renderer.push("<!--[-->");
						Dialog_content$1($$renderer, spread_props([
							{
								"data-slot": "dialog-content",
								class: cn("grid max-w-[calc(100%-2rem)] gap-6 rounded-4xl bg-popover p-6 text-sm text-popover-foreground shadow-xl ring-1 ring-foreground/5 duration-100 sm:max-w-md dark:ring-foreground/10 data-open:animate-in data-open:fade-in-0 data-open:zoom-in-95 data-closed:animate-out data-closed:fade-out-0 data-closed:zoom-out-95 fixed top-1/2 left-1/2 z-50 w-full -translate-x-1/2 -translate-y-1/2 outline-none", className)
							},
							restProps,
							{
								get ref() {
									return ref;
								},
								set ref($$value) {
									ref = $$value;
									$$settled = false;
								},
								children: ($$renderer) => {
									children?.($$renderer);
									$$renderer.push(`<!----> `);
									if (showCloseButton) {
										$$renderer.push("<!--[0-->");
										{
											function child($$renderer, { props }) {
												Button($$renderer, spread_props([
													{
														variant: "ghost",
														class: "absolute top-4 right-4 bg-secondary",
														size: "icon-sm"
													},
													props,
													{
														children: ($$renderer) => {
															X($$renderer, {});
															$$renderer.push(`<!----> <span class="sr-only">Close</span>`);
														},
														$$slots: { default: true }
													}
												]));
											}
											if (Dialog_close) {
												$$renderer.push("<!--[-->");
												Dialog_close($$renderer, {
													"data-slot": "dialog-close",
													child,
													$$slots: { child: true }
												});
												$$renderer.push("<!--]-->");
											} else {
												$$renderer.push("<!--[!-->");
												$$renderer.push("<!--]-->");
											}
										}
									} else $$renderer.push("<!--[-1-->");
									$$renderer.push(`<!--]-->`);
								},
								$$slots: { default: true }
							}
						]));
						$$renderer.push("<!--]-->");
					} else {
						$$renderer.push("<!--[!-->");
						$$renderer.push("<!--]-->");
					}
				},
				$$slots: { default: true }
			}]));
		}
		do {
			$$settled = true;
			$$inner_renderer = $$renderer.copy();
			$$render_inner($$inner_renderer);
		} while (!$$settled);
		$$renderer.subsume($$inner_renderer);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/dialog/dialog-description.svelte
function Dialog_description($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, $$slots, $$events, ...restProps } = $$props;
		let $$settled = true;
		let $$inner_renderer;
		function $$render_inner($$renderer) {
			if (Dialog_description$1) {
				$$renderer.push("<!--[-->");
				Dialog_description$1($$renderer, spread_props([
					{
						"data-slot": "dialog-description",
						class: cn("text-sm text-muted-foreground *:[a]:underline *:[a]:underline-offset-3 *:[a]:hover:text-foreground", className)
					},
					restProps,
					{
						get ref() {
							return ref;
						},
						set ref($$value) {
							ref = $$value;
							$$settled = false;
						}
					}
				]));
				$$renderer.push("<!--]-->");
			} else {
				$$renderer.push("<!--[!-->");
				$$renderer.push("<!--]-->");
			}
		}
		do {
			$$settled = true;
			$$inner_renderer = $$renderer.copy();
			$$render_inner($$inner_renderer);
		} while (!$$settled);
		$$renderer.subsume($$inner_renderer);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/dialog/dialog-footer.svelte
function Dialog_footer($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, children, showCloseButton = false, $$slots, $$events, ...restProps } = $$props;
		$$renderer.push(`<div${attributes({
			"data-slot": "dialog-footer",
			class: clsx$1(cn("gap-2 flex flex-col-reverse gap-2 sm:flex-row sm:justify-end", className)),
			...restProps
		})}>`);
		children?.($$renderer);
		$$renderer.push(`<!----> `);
		if (showCloseButton) {
			$$renderer.push("<!--[0-->");
			{
				function child($$renderer, { props }) {
					Button($$renderer, spread_props([
						{ variant: "outline" },
						props,
						{
							children: ($$renderer) => {
								$$renderer.push(`<!---->Close`);
							},
							$$slots: { default: true }
						}
					]));
				}
				if (Dialog_close) {
					$$renderer.push("<!--[-->");
					Dialog_close($$renderer, {
						child,
						$$slots: { child: true }
					});
					$$renderer.push("<!--]-->");
				} else {
					$$renderer.push("<!--[!-->");
					$$renderer.push("<!--]-->");
				}
			}
		} else $$renderer.push("<!--[-1-->");
		$$renderer.push(`<!--]--></div>`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/dialog/dialog-header.svelte
function Dialog_header($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, children, $$slots, $$events, ...restProps } = $$props;
		$$renderer.push(`<div${attributes({
			"data-slot": "dialog-header",
			class: clsx$1(cn("gap-1.5 flex flex-col", className)),
			...restProps
		})}>`);
		children?.($$renderer);
		$$renderer.push(`<!----></div>`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/dialog/dialog-overlay.svelte
function Dialog_overlay($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, $$slots, $$events, ...restProps } = $$props;
		let $$settled = true;
		let $$inner_renderer;
		function $$render_inner($$renderer) {
			if (Dialog_overlay$1) {
				$$renderer.push("<!--[-->");
				Dialog_overlay$1($$renderer, spread_props([
					{
						"data-slot": "dialog-overlay",
						class: cn("bg-black/30 duration-100 supports-backdrop-filter:backdrop-blur-sm data-open:animate-in data-open:fade-in-0 data-closed:animate-out data-closed:fade-out-0 fixed inset-0 isolate z-50", className)
					},
					restProps,
					{
						get ref() {
							return ref;
						},
						set ref($$value) {
							ref = $$value;
							$$settled = false;
						}
					}
				]));
				$$renderer.push("<!--]-->");
			} else {
				$$renderer.push("<!--[!-->");
				$$renderer.push("<!--]-->");
			}
		}
		do {
			$$settled = true;
			$$inner_renderer = $$renderer.copy();
			$$render_inner($$inner_renderer);
		} while (!$$settled);
		$$renderer.subsume($$inner_renderer);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/dialog/dialog-title.svelte
function Dialog_title($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, $$slots, $$events, ...restProps } = $$props;
		let $$settled = true;
		let $$inner_renderer;
		function $$render_inner($$renderer) {
			if (Dialog_title$1) {
				$$renderer.push("<!--[-->");
				Dialog_title$1($$renderer, spread_props([
					{
						"data-slot": "dialog-title",
						class: cn("text-base leading-none font-medium", className)
					},
					restProps,
					{
						get ref() {
							return ref;
						},
						set ref($$value) {
							ref = $$value;
							$$settled = false;
						}
					}
				]));
				$$renderer.push("<!--]-->");
			} else {
				$$renderer.push("<!--[!-->");
				$$renderer.push("<!--]-->");
			}
		}
		do {
			$$settled = true;
			$$inner_renderer = $$renderer.copy();
			$$render_inner($$inner_renderer);
		} while (!$$settled);
		$$renderer.subsume($$inner_renderer);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/dialog/dialog.svelte
function Dialog($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { open = false, $$slots, $$events, ...restProps } = $$props;
		let $$settled = true;
		let $$inner_renderer;
		function $$render_inner($$renderer) {
			if (Dialog$1) {
				$$renderer.push("<!--[-->");
				Dialog$1($$renderer, spread_props([restProps, {
					get open() {
						return open;
					},
					set open($$value) {
						open = $$value;
						$$settled = false;
					}
				}]));
				$$renderer.push("<!--]-->");
			} else {
				$$renderer.push("<!--[!-->");
				$$renderer.push("<!--]-->");
			}
		}
		do {
			$$settled = true;
			$$inner_renderer = $$renderer.copy();
			$$render_inner($$inner_renderer);
		} while (!$$settled);
		$$renderer.subsume($$inner_renderer);
		bind_props($$props, { open });
	});
}
//#endregion
//#region src/lib/AppDialog.svelte
function AppDialog($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { open, title, description, class: className, bodyClass, showCloseButton = true, onopenchange, children, footer } = $$props;
		if (Dialog) {
			$$renderer.push("<!--[-->");
			Dialog($$renderer, {
				open,
				onOpenChange: onopenchange,
				children: ($$renderer) => {
					if (Dialog_content) {
						$$renderer.push("<!--[-->");
						Dialog_content($$renderer, {
							showCloseButton,
							class: cn("flex max-h-[calc(100svh-2rem)] min-h-0 flex-col gap-0 overflow-hidden p-0 sm:max-w-2xl", className),
							children: ($$renderer) => {
								if (Dialog_header) {
									$$renderer.push("<!--[-->");
									Dialog_header($$renderer, {
										class: "shrink-0 border-b px-6 py-5 pr-16",
										children: ($$renderer) => {
											if (Dialog_title) {
												$$renderer.push("<!--[-->");
												Dialog_title($$renderer, {
													class: "font-serif text-xl font-medium",
													children: ($$renderer) => {
														$$renderer.push(`<!---->${escape_html(title)}`);
													},
													$$slots: { default: true }
												});
												$$renderer.push("<!--]-->");
											} else {
												$$renderer.push("<!--[!-->");
												$$renderer.push("<!--]-->");
											}
											$$renderer.push(` `);
											if (description) {
												$$renderer.push("<!--[0-->");
												if (Dialog_description) {
													$$renderer.push("<!--[-->");
													Dialog_description($$renderer, {
														children: ($$renderer) => {
															$$renderer.push(`<!---->${escape_html(description)}`);
														},
														$$slots: { default: true }
													});
													$$renderer.push("<!--]-->");
												} else {
													$$renderer.push("<!--[!-->");
													$$renderer.push("<!--]-->");
												}
											} else $$renderer.push("<!--[-1-->");
											$$renderer.push(`<!--]-->`);
										},
										$$slots: { default: true }
									});
									$$renderer.push("<!--]-->");
								} else {
									$$renderer.push("<!--[!-->");
									$$renderer.push("<!--]-->");
								}
								$$renderer.push(` <div${attr_class(clsx$1(cn("min-h-0 flex-1 overflow-y-auto px-6 py-5", bodyClass)))}>`);
								children($$renderer);
								$$renderer.push(`<!----></div> `);
								if (footer) {
									$$renderer.push("<!--[0-->");
									if (Dialog_footer) {
										$$renderer.push("<!--[-->");
										Dialog_footer($$renderer, {
											class: "shrink-0 border-t px-6 py-4",
											children: ($$renderer) => {
												footer($$renderer);
												$$renderer.push(`<!---->`);
											},
											$$slots: { default: true }
										});
										$$renderer.push("<!--]-->");
									} else {
										$$renderer.push("<!--[!-->");
										$$renderer.push("<!--]-->");
									}
								} else $$renderer.push("<!--[-1-->");
								$$renderer.push(`<!--]-->`);
							},
							$$slots: { default: true }
						});
						$$renderer.push("<!--]-->");
					} else {
						$$renderer.push("<!--[!-->");
						$$renderer.push("<!--]-->");
					}
				},
				$$slots: { default: true }
			});
			$$renderer.push("<!--]-->");
		} else {
			$$renderer.push("<!--[!-->");
			$$renderer.push("<!--]-->");
		}
	});
}
//#endregion
//#region src/lib/message-composer.ts
var inputTypes = [
	"string",
	"bool",
	"int64",
	"decimal",
	"date",
	"time",
	"instant",
	"uuid"
];
var formatFunctions = [
	"string",
	"integer",
	"number",
	"date",
	"time",
	"datetime",
	"uuid",
	"relativeTime"
];
var selectorFunctions = [
	"plural",
	"ordinal",
	"literal"
];
var relativeTimeUnits = [
	"second",
	"minute",
	"hour",
	"day",
	"week",
	"month",
	"year"
];
function toStructuredMessage(value) {
	if (isStructuredMessage(value)) return structuredClone(value);
	return {
		inputs: {},
		selectors: [],
		variants: [{
			match: {},
			value: typeof value === "string" ? value : ""
		}]
	};
}
function isStructuredMessage(value) {
	return isObject$1(value) && isObject$1(value.inputs) && Array.isArray(value.selectors) && Array.isArray(value.variants);
}
function nextIdentifier(prefix, names) {
	const used = new Set(names);
	if (!used.has(prefix)) return prefix;
	for (let index = 2;; index += 1) {
		const candidate = `${prefix}${index}`;
		if (!used.has(candidate)) return candidate;
	}
}
function synchronizeMatches(message) {
	const next = structuredClone(message);
	const names = next.selectors.map((selector) => selector.name);
	for (const variant of next.variants) variant.match = Object.fromEntries(names.map((name) => [name, variant.match[name] || "*"]));
	ensureCatchAll(next);
	return next;
}
function ensureCatchAll(message) {
	if (message.variants.some((variant) => Object.values(variant.match).every((match) => match === "*"))) return;
	message.variants.push({
		match: Object.fromEntries(message.selectors.map((selector) => [selector.name, "*"])),
		value: ""
	});
}
function renameInput(message, previous, nextName) {
	const next = structuredClone(message);
	if (previous === nextName || !(previous in next.inputs)) return next;
	const inputs = {};
	for (const [name, descriptor] of Object.entries(next.inputs)) inputs[name === previous ? nextName : name] = descriptor;
	next.inputs = inputs;
	for (const declaration of next.declarations ?? []) if (declaration.input === previous) declaration.input = nextName;
	for (const selector of next.selectors) if (selector.input === previous) selector.input = nextName;
	for (const variant of next.variants) renameInputInNodes(patternNodes(variant.value), previous, nextName);
	return next;
}
function renameDeclaration(message, previous, nextName) {
	const next = structuredClone(message);
	const declaration = next.declarations?.find((candidate) => candidate.name === previous);
	if (declaration !== void 0) declaration.name = nextName;
	for (const variant of next.variants) renameLocalInNodes(patternNodes(variant.value), previous, nextName);
	return next;
}
function renameSelector(message, previous, nextName) {
	const next = structuredClone(message);
	const selector = next.selectors.find((candidate) => candidate.name === previous);
	if (selector !== void 0) selector.name = nextName;
	for (const variant of next.variants) {
		const value = variant.match[previous] ?? "*";
		delete variant.match[previous];
		variant.match[nextName] = value;
	}
	return synchronizeMatches(next);
}
function patternNodes(value) {
	if (typeof value !== "string") return value;
	const nodes = [];
	let text = "";
	const flush = () => {
		if (text !== "") nodes.push(text);
		text = "";
	};
	for (let index = 0; index < value.length;) {
		if (value.startsWith("{{", index)) {
			text += "{";
			index += 2;
			continue;
		}
		if (value.startsWith("}}", index)) {
			text += "}";
			index += 2;
			continue;
		}
		if (value[index] === "{") {
			const end = value.indexOf("}", index + 1);
			const name = end < 0 ? "" : value.slice(index + 1, end);
			if (/^[A-Za-z_][A-Za-z0-9_]*$/.test(name)) {
				flush();
				nodes.push({ input: name });
				index = end + 1;
				continue;
			}
		}
		text += value[index];
		index += 1;
	}
	flush();
	return nodes;
}
function patternText(nodes) {
	let result = "";
	for (const node of nodes) if (typeof node === "string") result += node.replaceAll("{", "{{").replaceAll("}", "}}");
	else if ("input" in node) result += `{${node.input}}`;
	else return void 0;
	return result;
}
function renameInputInNodes(nodes, previous, nextName) {
	for (const node of nodes) {
		if (typeof node === "string") continue;
		if ("input" in node && node.input === previous) node.input = nextName;
		else if ("format" in node && node.format.input === previous) node.format.input = nextName;
		else if ("markup" in node) renameInputInNodes(node.markup.children, previous, nextName);
	}
}
function renameLocalInNodes(nodes, previous, nextName) {
	for (const node of nodes) {
		if (typeof node === "string") continue;
		if ("local" in node && node.local === previous) node.local = nextName;
		else if ("markup" in node) renameLocalInNodes(node.markup.children, previous, nextName);
	}
}
function isObject$1(value) {
	return typeof value === "object" && value !== null && !Array.isArray(value);
}
//#endregion
//#region src/lib/mock-bridge.ts
var manifest = document$1("product.catalog.json", void 0, void 0, {
	schemaVersion: 2,
	catalog: "customer-product",
	code: {
		namespace: "Customer.Product",
		className: "ProductText",
		visibility: "public"
	},
	defaultLocale: "de",
	locales: [
		{ tag: "de" },
		{
			tag: "en",
			fallback: "de"
		},
		{
			tag: "fr",
			fallback: "de"
		}
	],
	layers: [{
		name: "base",
		priority: 0
	}],
	validation: {
		translationCompleteness: "warning",
		extraLocaleKeys: "error",
		emptyValues: "error"
	}
});
manifest.isManifest = true;
document$1("product.de.json", "de", "base", resources("Speichern", "Abbrechen", "Willkommen zurück, {name}")), document$1("product.en.json", "en", "base", resources("Save", "Cancel", "Welcome back, {name}")), document$1("product.fr.json", "fr", "base", {
	schemaVersion: 2,
	catalog: "customer-product",
	locale: "fr",
	layer: "base",
	resources: { Common: { Save: "Enregistrer" } }
});
function document$1(path, locale, layer, value) {
	return {
		path,
		locale,
		layer,
		isManifest: false,
		isMalformed: false,
		revision: `mock-${path}`,
		content: `${JSON.stringify(value, null, 2)}\n`
	};
}
function resources(save, cancel, welcome) {
	return {
		schemaVersion: 2,
		catalog: "customer-product",
		locale: save === "Speichern" ? "de" : "en",
		layer: "base",
		resources: {
			Common: {
				Save: save,
				Cancel: cancel
			},
			Dashboard: { Welcome: {
				$value: welcome,
				$description: "Greeting on the dashboard",
				$tags: ["dashboard", "customer-facing"],
				$placeholders: { name: { type: "string" } }
			} },
			Files: { Selected: { $value: {
				inputs: { count: { type: "int64" } },
				selectors: [{
					name: "quantity",
					input: "count",
					function: "plural"
				}],
				variants: [{
					match: { quantity: "one" },
					value: save === "Speichern" ? "Eine Datei ausgewählt" : "One file selected"
				}, {
					match: { quantity: "*" },
					value: save === "Speichern" ? "{count} Dateien ausgewählt" : "{count} files selected"
				}]
			} } }
		}
	};
}
//#endregion
//#region src/lib/editor-bridge.ts
function createEditorBridge() {
	return {
		async load() {
			return parse$1(await binding("runicEditorLoad", globalThis.runicEditorLoad)());
		},
		async checkExternalChanges() {
			return parse$1(await binding("runicEditorCheckExternalChanges", globalThis.runicEditorCheckExternalChanges)());
		},
		async pickWorkspace() {
			return parse$1(await binding("runicEditorPickWorkspace", globalThis.runicEditorPickWorkspace)());
		},
		async previewMutation(request) {
			return parse$1(await binding("runicEditorPreviewMutation", globalThis.runicEditorPreviewMutation)(JSON.stringify(request)));
		},
		async applyMutation(request) {
			return parse$1(await binding("runicEditorApplyMutation", globalThis.runicEditorApplyMutation)(JSON.stringify(request)));
		},
		async recoverTransaction(mode) {
			return parse$1(await binding("runicEditorRecoverTransaction", globalThis.runicEditorRecoverTransaction)(JSON.stringify({ mode })));
		},
		async validate(path, content) {
			return parse$1(await binding("runicEditorValidate", globalThis.runicEditorValidate)(path, content));
		},
		async previewMessage(path, content, locale, key) {
			return parse$1(await binding("runicEditorPreviewMessage", globalThis.runicEditorPreviewMessage)(path, content, locale, key));
		},
		async saveReview(request) {
			return parse$1(await binding("runicEditorSaveReview", globalThis.runicEditorSaveReview)(JSON.stringify(request)));
		},
		async about() {
			return parse$1(await binding("runicEditorAbout", globalThis.runicEditorAbout)());
		},
		async createDiagnosticBundle() {
			return parse$1(await binding("runicEditorCreateDiagnosticBundle", globalThis.runicEditorCreateDiagnosticBundle)());
		},
		async save(path, content, revision) {
			return parse$1(await binding("runicEditorSave", globalThis.runicEditorSave)(path, content, revision));
		},
		async previewProject(request) {
			return parse$1(await binding("runicEditorPreviewProject", globalThis.runicEditorPreviewProject)(JSON.stringify(request)));
		},
		async createProject(request) {
			return parse$1(await binding("runicEditorCreateProject", globalThis.runicEditorCreateProject)(JSON.stringify(request)));
		},
		async openWorkspace(request) {
			return parse$1(await binding("runicEditorOpenWorkspace", globalThis.runicEditorOpenWorkspace)(JSON.stringify(request)));
		}
	};
}
function binding(name, value) {
	if (value === void 0) throw new Error(`${name} is unavailable. Start the native editor, or use npm run dev:mock.`);
	return value;
}
function parse$1(value) {
	return JSON.parse(value);
}
//#endregion
//#region src/lib/components/ui/toggle/toggle.svelte
var toggleVariants = tv({
	base: "gap-1 rounded-3xl text-sm font-medium transition-colors hover:text-foreground focus-visible:border-ring focus-visible:ring-ring/30 aria-invalid:border-destructive aria-invalid:ring-destructive/20 aria-pressed:bg-muted dark:aria-invalid:ring-destructive/40 [&_svg:not([class*='size-'])]:size-4 group/toggle inline-flex items-center justify-center whitespace-nowrap outline-none hover:bg-muted focus-visible:ring-[3px] disabled:pointer-events-none disabled:opacity-50 [&_svg]:pointer-events-none [&_svg]:shrink-0",
	variants: {
		variant: {
			default: "bg-transparent",
			outline: "border border-input bg-transparent hover:bg-muted"
		},
		size: {
			default: "h-9 min-w-9 px-3 has-data-[icon=inline-end]:pr-2.5 has-data-[icon=inline-start]:pl-2.5",
			sm: "h-8 min-w-8 px-3 has-data-[icon=inline-end]:pr-2 has-data-[icon=inline-start]:pl-2",
			lg: "h-10 min-w-10 px-4 has-data-[icon=inline-end]:pr-3 has-data-[icon=inline-start]:pl-3"
		}
	},
	defaultVariants: {
		variant: "default",
		size: "default"
	}
});
//#endregion
//#region src/lib/components/ui/toggle-group/toggle-group.svelte
function setToggleGroupCtx(props) {
	setContext("toggleGroup", props);
}
function getToggleGroupCtx() {
	return getContext("toggleGroup");
}
function Toggle_group($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, value = void 0, class: className, size = "default", spacing = 0, orientation = "horizontal", variant = "default", $$slots, $$events, ...restProps } = $$props;
		setToggleGroupCtx({
			get variant() {
				return variant;
			},
			get size() {
				return size;
			},
			get spacing() {
				return spacing;
			},
			get orientation() {
				return orientation;
			}
		});
		let $$settled = true;
		let $$inner_renderer;
		function $$render_inner($$renderer) {
			if (Toggle_group$1) {
				$$renderer.push("<!--[-->");
				Toggle_group$1($$renderer, spread_props([
					{
						orientation,
						"data-slot": "toggle-group",
						"data-variant": variant,
						"data-size": size,
						"data-spacing": spacing,
						style: `--gap: ${spacing}`,
						class: cn("data-[spacing=0]:data-[variant=outline]:rounded-3xl group/toggle-group flex w-fit flex-row items-center gap-[--spacing(var(--gap))] data-vertical:flex-col data-vertical:items-stretch", className)
					},
					restProps,
					{
						get value() {
							return value;
						},
						set value($$value) {
							value = $$value;
							$$settled = false;
						},
						get ref() {
							return ref;
						},
						set ref($$value) {
							ref = $$value;
							$$settled = false;
						}
					}
				]));
				$$renderer.push("<!--]-->");
			} else {
				$$renderer.push("<!--[!-->");
				$$renderer.push("<!--]-->");
			}
		}
		do {
			$$settled = true;
			$$inner_renderer = $$renderer.copy();
			$$render_inner($$inner_renderer);
		} while (!$$settled);
		$$renderer.subsume($$inner_renderer);
		bind_props($$props, {
			ref,
			value
		});
	});
}
//#endregion
//#region src/lib/components/ui/toggle-group/toggle-group-item.svelte
function Toggle_group_item($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, value = void 0, class: className, size, variant, $$slots, $$events, ...restProps } = $$props;
		const ctx = getToggleGroupCtx();
		let $$settled = true;
		let $$inner_renderer;
		function $$render_inner($$renderer) {
			if (Toggle_group_item$1) {
				$$renderer.push("<!--[-->");
				Toggle_group_item$1($$renderer, spread_props([
					{
						"data-slot": "toggle-group-item",
						"data-variant": ctx.variant || variant,
						"data-size": ctx.size || size,
						"data-spacing": ctx.spacing,
						class: cn("group-data-[spacing=0]/toggle-group:rounded-none group-data-[spacing=0]/toggle-group:px-3 group-data-[spacing=0]/toggle-group:shadow-none group-data-[spacing=0]/toggle-group:has-data-[icon=inline-end]:pr-2.5 group-data-[spacing=0]/toggle-group:has-data-[icon=inline-start]:pl-2.5 group-data-horizontal/toggle-group:data-[spacing=0]:first:rounded-l-3xl group-data-vertical/toggle-group:data-[spacing=0]:first:rounded-t-3xl group-data-horizontal/toggle-group:data-[spacing=0]:last:rounded-r-3xl group-data-vertical/toggle-group:data-[spacing=0]:last:rounded-b-3xl data-[state=on]:bg-muted shrink-0 focus:z-10 focus-visible:z-10 group-data-horizontal/toggle-group:data-[spacing=0]:data-[variant=outline]:border-l-0 group-data-vertical/toggle-group:data-[spacing=0]:data-[variant=outline]:border-t-0 group-data-horizontal/toggle-group:data-[spacing=0]:data-[variant=outline]:first:border-l group-data-vertical/toggle-group:data-[spacing=0]:data-[variant=outline]:first:border-t", toggleVariants({
							variant: ctx.variant || variant,
							size: ctx.size || size
						}), className),
						value
					},
					restProps,
					{
						get ref() {
							return ref;
						},
						set ref($$value) {
							ref = $$value;
							$$settled = false;
						}
					}
				]));
				$$renderer.push("<!--]-->");
			} else {
				$$renderer.push("<!--[!-->");
				$$renderer.push("<!--]-->");
			}
		}
		do {
			$$settled = true;
			$$inner_renderer = $$renderer.copy();
			$$render_inner($$inner_renderer);
		} while (!$$settled);
		$$renderer.subsume($$inner_renderer);
		bind_props($$props, {
			ref,
			value
		});
	});
}
//#endregion
//#region src/lib/EditorModeSwitcher.svelte
function EditorModeSwitcher($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { mode, simpleLabel, rawLabel, onchange } = $$props;
		let options = derived(() => [{
			value: "translation",
			label: simpleLabel
		}, {
			value: "raw",
			label: rawLabel
		}]);
		$$renderer.push(`<div class="mx-auto max-w-[1000px] border-b pb-2">`);
		if (Toggle_group) {
			$$renderer.push("<!--[-->");
			Toggle_group($$renderer, {
				type: "single",
				variant: "outline",
				size: "sm",
				spacing: 1,
				value: mode,
				class: "grid w-full grid-cols-2 sm:flex sm:w-auto",
				"aria-label": "Editing mode",
				onValueChange: (value) => {
					if (value !== "") onchange(value);
				},
				children: ($$renderer) => {
					$$renderer.push(`<!--[-->`);
					const each_array = ensure_array_like(options());
					for (let $$index = 0, $$length = each_array.length; $$index < $$length; $$index++) {
						let option = each_array[$$index];
						if (Toggle_group_item) {
							$$renderer.push("<!--[-->");
							Toggle_group_item($$renderer, {
								value: option.value,
								class: "min-w-0 overflow-hidden px-1 text-[0.6875rem] sm:w-auto sm:flex-none sm:px-3 sm:text-sm",
								title: option.label,
								onclick: (event) => {
									if (mode === option.value) event.preventDefault();
								},
								children: ($$renderer) => {
									$$renderer.push(`<span>${escape_html(option.label)}</span>`);
								},
								$$slots: { default: true }
							});
							$$renderer.push("<!--]-->");
						} else {
							$$renderer.push("<!--[!-->");
							$$renderer.push("<!--]-->");
						}
					}
					$$renderer.push(`<!--]-->`);
				},
				$$slots: { default: true }
			});
			$$renderer.push("<!--]-->");
		} else {
			$$renderer.push("<!--[!-->");
			$$renderer.push("<!--]-->");
		}
		$$renderer.push(`</div>`);
	});
}
//#endregion
//#region node_modules/@lucide/svelte/dist/icons/info.svelte
function Info($$renderer, $$props) {
	let { $$slots, $$events, ...props } = $$props;
	Icon($$renderer, spread_props([
		{ name: "info" },
		props,
		{ iconNode: [
			["circle", {
				"cx": "12",
				"cy": "12",
				"r": "10"
			}],
			["path", { "d": "M12 16v-4" }],
			["path", { "d": "M12 8h.01" }]
		] }
	]));
}
//#endregion
//#region node_modules/@lucide/svelte/dist/icons/languages.svelte
function Languages($$renderer, $$props) {
	let { $$slots, $$events, ...props } = $$props;
	Icon($$renderer, spread_props([
		{ name: "languages" },
		props,
		{ iconNode: [
			["path", { "d": "m5 8 6 6" }],
			["path", { "d": "m4 14 6-6 2-3" }],
			["path", { "d": "M2 5h12" }],
			["path", { "d": "M7 2h1" }],
			["path", { "d": "m22 22-5-10-5 10" }],
			["path", { "d": "M14 18h6" }]
		] }
	]));
}
//#endregion
//#region node_modules/@lucide/svelte/dist/icons/monitor.svelte
function Monitor($$renderer, $$props) {
	let { $$slots, $$events, ...props } = $$props;
	Icon($$renderer, spread_props([
		{ name: "monitor" },
		props,
		{ iconNode: [
			["rect", {
				"width": "20",
				"height": "14",
				"x": "2",
				"y": "3",
				"rx": "2"
			}],
			["line", {
				"x1": "8",
				"x2": "16",
				"y1": "21",
				"y2": "21"
			}],
			["line", {
				"x1": "12",
				"x2": "12",
				"y1": "17",
				"y2": "21"
			}]
		] }
	]));
}
//#endregion
//#region node_modules/@lucide/svelte/dist/icons/moon.svelte
function Moon($$renderer, $$props) {
	let { $$slots, $$events, ...props } = $$props;
	Icon($$renderer, spread_props([
		{ name: "moon" },
		props,
		{ iconNode: [["path", { "d": "M20.985 12.486a9 9 0 1 1-9.473-9.472c.405-.022.617.46.402.803a6 6 0 0 0 8.268 8.268c.344-.215.825-.004.803.401" }]] }
	]));
}
//#endregion
//#region node_modules/@lucide/svelte/dist/icons/palette.svelte
function Palette($$renderer, $$props) {
	let { $$slots, $$events, ...props } = $$props;
	Icon($$renderer, spread_props([
		{ name: "palette" },
		props,
		{ iconNode: [
			["path", { "d": "M12 22a1 1 0 0 1 0-20 10 9 0 0 1 10 9 5 5 0 0 1-5 5h-2.25a1.75 1.75 0 0 0-1.4 2.8l.3.4a1.75 1.75 0 0 1-1.4 2.8z" }],
			["circle", {
				"cx": "13.5",
				"cy": "6.5",
				"r": ".5",
				"fill": "currentColor"
			}],
			["circle", {
				"cx": "17.5",
				"cy": "10.5",
				"r": ".5",
				"fill": "currentColor"
			}],
			["circle", {
				"cx": "6.5",
				"cy": "12.5",
				"r": ".5",
				"fill": "currentColor"
			}],
			["circle", {
				"cx": "8.5",
				"cy": "7.5",
				"r": ".5",
				"fill": "currentColor"
			}]
		] }
	]));
}
//#endregion
//#region node_modules/@lucide/svelte/dist/icons/settings-2.svelte
function Settings_2($$renderer, $$props) {
	let { $$slots, $$events, ...props } = $$props;
	Icon($$renderer, spread_props([
		{ name: "settings-2" },
		props,
		{ iconNode: [
			["path", { "d": "M14 17H5" }],
			["path", { "d": "M19 7h-9" }],
			["circle", {
				"cx": "17",
				"cy": "17",
				"r": "3"
			}],
			["circle", {
				"cx": "7",
				"cy": "7",
				"r": "3"
			}]
		] }
	]));
}
//#endregion
//#region node_modules/@lucide/svelte/dist/icons/sun.svelte
function Sun($$renderer, $$props) {
	let { $$slots, $$events, ...props } = $$props;
	Icon($$renderer, spread_props([
		{ name: "sun" },
		props,
		{ iconNode: [
			["circle", {
				"cx": "12",
				"cy": "12",
				"r": "4"
			}],
			["path", { "d": "M12 2v2" }],
			["path", { "d": "M12 20v2" }],
			["path", { "d": "m4.93 4.93 1.41 1.41" }],
			["path", { "d": "m17.66 17.66 1.41 1.41" }],
			["path", { "d": "M2 12h2" }],
			["path", { "d": "M20 12h2" }],
			["path", { "d": "m6.34 17.66-1.41 1.41" }],
			["path", { "d": "m19.07 4.93-1.41 1.41" }]
		] }
	]));
}
//#endregion
//#region node_modules/@lucide/svelte/dist/icons/chevrons-up-down.svelte
function Chevrons_up_down($$renderer, $$props) {
	let { $$slots, $$events, ...props } = $$props;
	Icon($$renderer, spread_props([
		{ name: "chevrons-up-down" },
		props,
		{ iconNode: [["path", { "d": "m7 15 5 5 5-5" }], ["path", { "d": "m7 9 5-5 5 5" }]] }
	]));
}
//#endregion
//#region src/lib/components/ui/badge/badge.svelte
var badgeVariants = tv({
	base: "h-5 gap-1 rounded-3xl border border-transparent px-2 py-0.5 text-xs font-medium transition-all has-data-[icon=inline-end]:pr-1.5 has-data-[icon=inline-start]:pl-1.5 [&>svg]:size-3! group/badge inline-flex w-fit shrink-0 items-center justify-center overflow-hidden whitespace-nowrap transition-colors focus-visible:border-ring focus-visible:ring-[3px] focus-visible:ring-ring/50 aria-invalid:border-destructive aria-invalid:ring-destructive/20 dark:aria-invalid:ring-destructive/40 [&>svg]:pointer-events-none",
	variants: { variant: {
		default: "bg-primary text-primary-foreground [a]:hover:bg-primary/80",
		secondary: "bg-secondary text-secondary-foreground [a]:hover:bg-secondary/80",
		destructive: "bg-destructive/10 text-destructive focus-visible:ring-destructive/20 dark:bg-destructive/20 dark:focus-visible:ring-destructive/40 [a]:hover:bg-destructive/20",
		outline: "border-border text-foreground [a]:hover:bg-muted [a]:hover:text-muted-foreground",
		ghost: "hover:bg-muted hover:text-muted-foreground dark:hover:bg-muted/50",
		link: "text-primary underline-offset-4 hover:underline"
	} },
	defaultVariants: { variant: "default" }
});
function Badge($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, href, class: className, variant = "default", children, $$slots, $$events, ...restProps } = $$props;
		element($$renderer, href ? "a" : "span", () => {
			$$renderer.push(`${attributes({
				"data-slot": "badge",
				href,
				class: clsx$1(cn(badgeVariants({ variant }), className)),
				...restProps
			})}`);
		}, () => {
			children?.($$renderer);
			$$renderer.push(`<!---->`);
		});
		bind_props($$props, { ref });
	});
}
//#endregion
//#region node_modules/@lucide/svelte/dist/icons/minus.svelte
function Minus($$renderer, $$props) {
	let { $$slots, $$events, ...props } = $$props;
	Icon($$renderer, spread_props([
		{ name: "minus" },
		props,
		{ iconNode: [["path", { "d": "M5 12h14" }]] }
	]));
}
//#endregion
//#region node_modules/@lucide/svelte/dist/icons/check.svelte
function Check($$renderer, $$props) {
	let { $$slots, $$events, ...props } = $$props;
	Icon($$renderer, spread_props([
		{ name: "check" },
		props,
		{ iconNode: [["path", { "d": "M20 6 9 17l-5-5" }]] }
	]));
}
//#endregion
//#region src/lib/components/ui/dropdown-menu/dropdown-menu-portal.svelte
function Dropdown_menu_portal($$renderer, $$props) {
	let { $$slots, $$events, ...restProps } = $$props;
	if (Portal) {
		$$renderer.push("<!--[-->");
		Portal($$renderer, spread_props([restProps]));
		$$renderer.push("<!--]-->");
	} else {
		$$renderer.push("<!--[!-->");
		$$renderer.push("<!--]-->");
	}
}
//#endregion
//#region src/lib/components/ui/dropdown-menu/dropdown-menu-content.svelte
function Dropdown_menu_content($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, sideOffset = 4, align = "start", portalProps, class: className, $$slots, $$events, ...restProps } = $$props;
		let $$settled = true;
		let $$inner_renderer;
		function $$render_inner($$renderer) {
			Dropdown_menu_portal($$renderer, spread_props([portalProps, {
				children: ($$renderer) => {
					if (Dropdown_menu_content$1) {
						$$renderer.push("<!--[-->");
						Dropdown_menu_content$1($$renderer, spread_props([
							{
								"data-slot": "dropdown-menu-content",
								sideOffset,
								align,
								class: cn("min-w-48 rounded-3xl bg-popover p-1.5 text-popover-foreground shadow-lg ring-1 ring-foreground/5 duration-100 data-[side=bottom]:slide-in-from-top-2 data-[side=left]:slide-in-from-right-2 data-[side=right]:slide-in-from-left-2 data-[side=top]:slide-in-from-bottom-2 dark:ring-foreground/10 data-open:animate-in data-open:fade-in-0 data-open:zoom-in-95 data-closed:animate-out data-closed:fade-out-0 data-closed:zoom-out-95 data-[side=inline-end]:slide-in-from-left-2 data-[side=inline-start]:slide-in-from-right-2 z-50 w-(--bits-dropdown-menu-anchor-width) overflow-x-hidden overflow-y-auto outline-none data-closed:overflow-hidden", className)
							},
							restProps,
							{
								get ref() {
									return ref;
								},
								set ref($$value) {
									ref = $$value;
									$$settled = false;
								}
							}
						]));
						$$renderer.push("<!--]-->");
					} else {
						$$renderer.push("<!--[!-->");
						$$renderer.push("<!--]-->");
					}
				},
				$$slots: { default: true }
			}]));
		}
		do {
			$$settled = true;
			$$inner_renderer = $$renderer.copy();
			$$render_inner($$inner_renderer);
		} while (!$$settled);
		$$renderer.subsume($$inner_renderer);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/dropdown-menu/dropdown-menu-group.svelte
function Dropdown_menu_group($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, $$slots, $$events, ...restProps } = $$props;
		let $$settled = true;
		let $$inner_renderer;
		function $$render_inner($$renderer) {
			if (Menu_group) {
				$$renderer.push("<!--[-->");
				Menu_group($$renderer, spread_props([
					{ "data-slot": "dropdown-menu-group" },
					restProps,
					{
						get ref() {
							return ref;
						},
						set ref($$value) {
							ref = $$value;
							$$settled = false;
						}
					}
				]));
				$$renderer.push("<!--]-->");
			} else {
				$$renderer.push("<!--[!-->");
				$$renderer.push("<!--]-->");
			}
		}
		do {
			$$settled = true;
			$$inner_renderer = $$renderer.copy();
			$$render_inner($$inner_renderer);
		} while (!$$settled);
		$$renderer.subsume($$inner_renderer);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/dropdown-menu/dropdown-menu-item.svelte
function Dropdown_menu_item($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, inset, variant = "default", $$slots, $$events, ...restProps } = $$props;
		let $$settled = true;
		let $$inner_renderer;
		function $$render_inner($$renderer) {
			if (Menu_item) {
				$$renderer.push("<!--[-->");
				Menu_item($$renderer, spread_props([
					{
						"data-slot": "dropdown-menu-item",
						"data-inset": inset,
						"data-variant": variant,
						class: cn("gap-2.5 rounded-2xl px-3 py-2 text-sm font-medium focus:bg-accent focus:text-accent-foreground not-data-[variant=destructive]:focus:**:text-accent-foreground data-inset:pl-9.5 data-[variant=destructive]:text-destructive data-[variant=destructive]:focus:bg-destructive/10 data-[variant=destructive]:focus:text-destructive dark:data-[variant=destructive]:focus:bg-destructive/20 [&_svg:not([class*='size-'])]:size-4 data-[variant=destructive]:*:[svg]:text-destructive group/dropdown-menu-item relative flex cursor-default items-center outline-hidden select-none data-[inset]:pl-8 data-disabled:pointer-events-none data-disabled:opacity-50 [&_svg]:pointer-events-none [&_svg]:shrink-0", className)
					},
					restProps,
					{
						get ref() {
							return ref;
						},
						set ref($$value) {
							ref = $$value;
							$$settled = false;
						}
					}
				]));
				$$renderer.push("<!--]-->");
			} else {
				$$renderer.push("<!--[!-->");
				$$renderer.push("<!--]-->");
			}
		}
		do {
			$$settled = true;
			$$inner_renderer = $$renderer.copy();
			$$render_inner($$inner_renderer);
		} while (!$$settled);
		$$renderer.subsume($$inner_renderer);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/dropdown-menu/dropdown-menu-label.svelte
function Dropdown_menu_label($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, inset, children, $$slots, $$events, ...restProps } = $$props;
		$$renderer.push(`<div${attributes({
			"data-slot": "dropdown-menu-label",
			"data-inset": inset,
			class: clsx$1(cn("px-3 py-2.5 text-xs text-muted-foreground data-inset:pl-9.5 data-[inset]:pl-8", className)),
			...restProps
		})}>`);
		children?.($$renderer);
		$$renderer.push(`<!----></div>`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/dropdown-menu/dropdown-menu-radio-group.svelte
function Dropdown_menu_radio_group($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, value = void 0, $$slots, $$events, ...restProps } = $$props;
		let $$settled = true;
		let $$inner_renderer;
		function $$render_inner($$renderer) {
			if (Menu_radio_group) {
				$$renderer.push("<!--[-->");
				Menu_radio_group($$renderer, spread_props([
					{ "data-slot": "dropdown-menu-radio-group" },
					restProps,
					{
						get ref() {
							return ref;
						},
						set ref($$value) {
							ref = $$value;
							$$settled = false;
						},
						get value() {
							return value;
						},
						set value($$value) {
							value = $$value;
							$$settled = false;
						}
					}
				]));
				$$renderer.push("<!--]-->");
			} else {
				$$renderer.push("<!--[!-->");
				$$renderer.push("<!--]-->");
			}
		}
		do {
			$$settled = true;
			$$inner_renderer = $$renderer.copy();
			$$render_inner($$inner_renderer);
		} while (!$$settled);
		$$renderer.subsume($$inner_renderer);
		bind_props($$props, {
			ref,
			value
		});
	});
}
//#endregion
//#region src/lib/components/ui/dropdown-menu/dropdown-menu-radio-item.svelte
function Dropdown_menu_radio_item($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, children: childrenProp, $$slots, $$events, ...restProps } = $$props;
		let $$settled = true;
		let $$inner_renderer;
		function $$render_inner($$renderer) {
			{
				function children($$renderer, { checked }) {
					$$renderer.push(`<span class="absolute right-2 flex items-center justify-center pointer-events-none" data-slot="dropdown-menu-radio-item-indicator">`);
					if (checked) {
						$$renderer.push("<!--[0-->");
						Check($$renderer, {});
					} else $$renderer.push("<!--[-1-->");
					$$renderer.push(`<!--]--></span> `);
					childrenProp?.($$renderer, { checked });
					$$renderer.push(`<!---->`);
				}
				if (Menu_radio_item) {
					$$renderer.push("<!--[-->");
					Menu_radio_item($$renderer, spread_props([
						{
							"data-slot": "dropdown-menu-radio-item",
							class: cn("gap-2.5 rounded-2xl py-2 pr-8 pl-3 text-sm font-medium focus:bg-accent focus:text-accent-foreground focus:**:text-accent-foreground data-inset:pl-9.5 [&_svg:not([class*='size-'])]:size-4 relative flex cursor-default items-center outline-hidden select-none data-[disabled]:pointer-events-none data-[disabled]:opacity-50 [&_svg]:pointer-events-none [&_svg]:shrink-0", className)
						},
						restProps,
						{
							get ref() {
								return ref;
							},
							set ref($$value) {
								ref = $$value;
								$$settled = false;
							},
							children,
							$$slots: { default: true }
						}
					]));
					$$renderer.push("<!--]-->");
				} else {
					$$renderer.push("<!--[!-->");
					$$renderer.push("<!--]-->");
				}
			}
		}
		do {
			$$settled = true;
			$$inner_renderer = $$renderer.copy();
			$$render_inner($$inner_renderer);
		} while (!$$settled);
		$$renderer.subsume($$inner_renderer);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/dropdown-menu/dropdown-menu-separator.svelte
function Dropdown_menu_separator($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, $$slots, $$events, ...restProps } = $$props;
		let $$settled = true;
		let $$inner_renderer;
		function $$render_inner($$renderer) {
			if (Menu_separator) {
				$$renderer.push("<!--[-->");
				Menu_separator($$renderer, spread_props([
					{
						"data-slot": "dropdown-menu-separator",
						class: cn("-mx-1.5 my-1.5 h-px bg-border/50", className)
					},
					restProps,
					{
						get ref() {
							return ref;
						},
						set ref($$value) {
							ref = $$value;
							$$settled = false;
						}
					}
				]));
				$$renderer.push("<!--]-->");
			} else {
				$$renderer.push("<!--[!-->");
				$$renderer.push("<!--]-->");
			}
		}
		do {
			$$settled = true;
			$$inner_renderer = $$renderer.copy();
			$$render_inner($$inner_renderer);
		} while (!$$settled);
		$$renderer.subsume($$inner_renderer);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/dropdown-menu/dropdown-menu-sub-content.svelte
function Dropdown_menu_sub_content($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, $$slots, $$events, ...restProps } = $$props;
		let $$settled = true;
		let $$inner_renderer;
		function $$render_inner($$renderer) {
			if (Menu_sub_content) {
				$$renderer.push("<!--[-->");
				Menu_sub_content($$renderer, spread_props([
					{
						"data-slot": "dropdown-menu-sub-content",
						class: cn("min-w-36 rounded-3xl bg-popover p-1.5 text-popover-foreground shadow-lg ring-1 ring-foreground/5 duration-100 data-[side=bottom]:slide-in-from-top-2 data-[side=left]:slide-in-from-right-2 data-[side=right]:slide-in-from-left-2 data-[side=top]:slide-in-from-bottom-2 dark:ring-foreground/10 data-open:animate-in data-open:fade-in-0 data-open:zoom-in-95 data-closed:animate-out data-closed:fade-out-0 data-closed:zoom-out-95 w-auto", className)
					},
					restProps,
					{
						get ref() {
							return ref;
						},
						set ref($$value) {
							ref = $$value;
							$$settled = false;
						}
					}
				]));
				$$renderer.push("<!--]-->");
			} else {
				$$renderer.push("<!--[!-->");
				$$renderer.push("<!--]-->");
			}
		}
		do {
			$$settled = true;
			$$inner_renderer = $$renderer.copy();
			$$render_inner($$inner_renderer);
		} while (!$$settled);
		$$renderer.subsume($$inner_renderer);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region node_modules/@lucide/svelte/dist/icons/chevron-right.svelte
function Chevron_right($$renderer, $$props) {
	let { $$slots, $$events, ...props } = $$props;
	Icon($$renderer, spread_props([
		{ name: "chevron-right" },
		props,
		{ iconNode: [["path", { "d": "m9 18 6-6-6-6" }]] }
	]));
}
//#endregion
//#region src/lib/components/ui/dropdown-menu/dropdown-menu-sub-trigger.svelte
function Dropdown_menu_sub_trigger($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, inset, children, $$slots, $$events, ...restProps } = $$props;
		let $$settled = true;
		let $$inner_renderer;
		function $$render_inner($$renderer) {
			if (Menu_sub_trigger) {
				$$renderer.push("<!--[-->");
				Menu_sub_trigger($$renderer, spread_props([
					{
						"data-slot": "dropdown-menu-sub-trigger",
						"data-inset": inset,
						class: cn("gap-2 rounded-2xl px-3 py-2 text-sm font-medium focus:bg-accent focus:text-accent-foreground not-data-[variant=destructive]:focus:**:text-accent-foreground data-inset:pl-9.5 data-open:bg-accent data-open:text-accent-foreground [&_svg:not([class*='size-'])]:size-4 flex cursor-default items-center outline-hidden select-none data-[inset]:pl-8 [&_svg]:pointer-events-none [&_svg]:shrink-0", className)
					},
					restProps,
					{
						get ref() {
							return ref;
						},
						set ref($$value) {
							ref = $$value;
							$$settled = false;
						},
						children: ($$renderer) => {
							children?.($$renderer);
							$$renderer.push(`<!----> `);
							Chevron_right($$renderer, { class: "ml-auto" });
							$$renderer.push(`<!---->`);
						},
						$$slots: { default: true }
					}
				]));
				$$renderer.push("<!--]-->");
			} else {
				$$renderer.push("<!--[!-->");
				$$renderer.push("<!--]-->");
			}
		}
		do {
			$$settled = true;
			$$inner_renderer = $$renderer.copy();
			$$render_inner($$inner_renderer);
		} while (!$$settled);
		$$renderer.subsume($$inner_renderer);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/dropdown-menu/dropdown-menu-sub.svelte
function Dropdown_menu_sub($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { open = false, $$slots, $$events, ...restProps } = $$props;
		let $$settled = true;
		let $$inner_renderer;
		function $$render_inner($$renderer) {
			if (Menu_sub) {
				$$renderer.push("<!--[-->");
				Menu_sub($$renderer, spread_props([restProps, {
					get open() {
						return open;
					},
					set open($$value) {
						open = $$value;
						$$settled = false;
					}
				}]));
				$$renderer.push("<!--]-->");
			} else {
				$$renderer.push("<!--[!-->");
				$$renderer.push("<!--]-->");
			}
		}
		do {
			$$settled = true;
			$$inner_renderer = $$renderer.copy();
			$$render_inner($$inner_renderer);
		} while (!$$settled);
		$$renderer.subsume($$inner_renderer);
		bind_props($$props, { open });
	});
}
//#endregion
//#region src/lib/components/ui/dropdown-menu/dropdown-menu-trigger.svelte
function Dropdown_menu_trigger($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, $$slots, $$events, ...restProps } = $$props;
		let $$settled = true;
		let $$inner_renderer;
		function $$render_inner($$renderer) {
			if (Menu_trigger) {
				$$renderer.push("<!--[-->");
				Menu_trigger($$renderer, spread_props([
					{ "data-slot": "dropdown-menu-trigger" },
					restProps,
					{
						get ref() {
							return ref;
						},
						set ref($$value) {
							ref = $$value;
							$$settled = false;
						}
					}
				]));
				$$renderer.push("<!--]-->");
			} else {
				$$renderer.push("<!--[!-->");
				$$renderer.push("<!--]-->");
			}
		}
		do {
			$$settled = true;
			$$inner_renderer = $$renderer.copy();
			$$render_inner($$inner_renderer);
		} while (!$$settled);
		$$renderer.subsume($$inner_renderer);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/dropdown-menu/dropdown-menu.svelte
function Dropdown_menu($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { open = false, $$slots, $$events, ...restProps } = $$props;
		let $$settled = true;
		let $$inner_renderer;
		function $$render_inner($$renderer) {
			if (Menu) {
				$$renderer.push("<!--[-->");
				Menu($$renderer, spread_props([restProps, {
					get open() {
						return open;
					},
					set open($$value) {
						open = $$value;
						$$settled = false;
					}
				}]));
				$$renderer.push("<!--]-->");
			} else {
				$$renderer.push("<!--[!-->");
				$$renderer.push("<!--]-->");
			}
		}
		do {
			$$settled = true;
			$$inner_renderer = $$renderer.copy();
			$$render_inner($$inner_renderer);
		} while (!$$settled);
		$$renderer.subsume($$inner_renderer);
		bind_props($$props, { open });
	});
}
//#endregion
//#region src/lib/components/ui/sidebar/sidebar-content.svelte
function Sidebar_content($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, children, $$slots, $$events, ...restProps } = $$props;
		$$renderer.push(`<div${attributes({
			"data-slot": "sidebar-content",
			"data-sidebar": "content",
			class: clsx$1(cn("no-scrollbar gap-2 [--radius:var(--radius-xl)] flex min-h-0 flex-1 flex-col overflow-auto group-data-[collapsible=icon]:overflow-hidden", className)),
			...restProps
		})}>`);
		children?.($$renderer);
		$$renderer.push(`<!----></div>`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/sidebar/sidebar-footer.svelte
function Sidebar_footer($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, children, $$slots, $$events, ...restProps } = $$props;
		$$renderer.push(`<div${attributes({
			"data-slot": "sidebar-footer",
			"data-sidebar": "footer",
			class: clsx$1(cn("gap-2 p-2 flex flex-col", className)),
			...restProps
		})}>`);
		children?.($$renderer);
		$$renderer.push(`<!----></div>`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/sidebar/sidebar-group-action.svelte
function Sidebar_group_action($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, children, child, $$slots, $$events, ...restProps } = $$props;
		const mergedProps = derived(() => ({
			class: cn("absolute top-3.5 right-3 w-5 rounded-xl p-0 text-sidebar-foreground ring-sidebar-ring hover:bg-sidebar-accent hover:text-sidebar-accent-foreground focus-visible:ring-2 [&>svg]:size-4 flex aspect-square items-center justify-center outline-hidden transition-transform group-data-[collapsible=icon]:hidden after:absolute after:-inset-2 md:after:hidden [&>svg]:shrink-0", className),
			"data-slot": "sidebar-group-action",
			"data-sidebar": "group-action",
			...restProps
		}));
		if (child) {
			$$renderer.push("<!--[0-->");
			child($$renderer, { props: mergedProps() });
			$$renderer.push(`<!---->`);
		} else {
			$$renderer.push("<!--[-1-->");
			$$renderer.push(`<button${attributes({ ...mergedProps() })}>`);
			children?.($$renderer);
			$$renderer.push(`<!----></button>`);
		}
		$$renderer.push(`<!--]-->`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/sidebar/sidebar-group-content.svelte
function Sidebar_group_content($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, children, $$slots, $$events, ...restProps } = $$props;
		$$renderer.push(`<div${attributes({
			"data-slot": "sidebar-group-content",
			"data-sidebar": "group-content",
			class: clsx$1(cn("text-sm w-full", className)),
			...restProps
		})}>`);
		children?.($$renderer);
		$$renderer.push(`<!----></div>`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/sidebar/sidebar-group-label.svelte
function Sidebar_group_label($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, children, child, class: className, $$slots, $$events, ...restProps } = $$props;
		const mergedProps = derived(() => ({
			class: cn("h-8 rounded-xl px-3 text-xs font-medium text-sidebar-foreground/70 ring-sidebar-ring transition-[margin,opacity] duration-200 ease-linear group-data-[collapsible=icon]:-mt-8 group-data-[collapsible=icon]:opacity-0 focus-visible:ring-2 [&>svg]:size-4 flex shrink-0 items-center outline-hidden [&>svg]:shrink-0", className),
			"data-slot": "sidebar-group-label",
			"data-sidebar": "group-label",
			...restProps
		}));
		if (child) {
			$$renderer.push("<!--[0-->");
			child($$renderer, { props: mergedProps() });
			$$renderer.push(`<!---->`);
		} else {
			$$renderer.push("<!--[-1-->");
			$$renderer.push(`<div${attributes({ ...mergedProps() })}>`);
			children?.($$renderer);
			$$renderer.push(`<!----></div>`);
		}
		$$renderer.push(`<!--]-->`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/sidebar/sidebar-group.svelte
function Sidebar_group($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, children, $$slots, $$events, ...restProps } = $$props;
		$$renderer.push(`<div${attributes({
			"data-slot": "sidebar-group",
			"data-sidebar": "group",
			class: clsx$1(cn("p-2 relative flex w-full min-w-0 flex-col", className)),
			...restProps
		})}>`);
		children?.($$renderer);
		$$renderer.push(`<!----></div>`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/sidebar/sidebar-header.svelte
function Sidebar_header($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, children, $$slots, $$events, ...restProps } = $$props;
		$$renderer.push(`<div${attributes({
			"data-slot": "sidebar-header",
			"data-sidebar": "header",
			class: clsx$1(cn("gap-2 p-2 [--radius:var(--radius-xl)] flex flex-col", className)),
			...restProps
		})}>`);
		children?.($$renderer);
		$$renderer.push(`<!----></div>`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/input/input.svelte
function Input($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, value = void 0, type, files = void 0, class: className, "data-slot": dataSlot = "input", $$slots, $$events, ...restProps } = $$props;
		if (type === "file") {
			$$renderer.push("<!--[0-->");
			$$renderer.push(`<input${attributes({
				"data-slot": dataSlot,
				class: clsx$1(cn("h-9 rounded-3xl border border-transparent bg-input/50 px-3 py-1 text-base transition-[color,box-shadow,background-color] file:h-7 file:text-sm file:font-medium focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/30 aria-invalid:border-destructive aria-invalid:ring-3 aria-invalid:ring-destructive/20 md:text-sm dark:aria-invalid:border-destructive/50 dark:aria-invalid:ring-destructive/40 w-full min-w-0 outline-none file:inline-flex file:border-0 file:bg-transparent file:text-foreground placeholder:text-muted-foreground disabled:pointer-events-none disabled:cursor-not-allowed disabled:opacity-50", className)),
				type: "file",
				...restProps
			}, void 0, void 0, void 0, 4)}/>`);
		} else {
			$$renderer.push("<!--[-1-->");
			$$renderer.push(`<input${attributes({
				"data-slot": dataSlot,
				class: clsx$1(cn("h-9 rounded-3xl border border-transparent bg-input/50 px-3 py-1 text-base transition-[color,box-shadow,background-color] file:h-7 file:text-sm file:font-medium focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/30 aria-invalid:border-destructive aria-invalid:ring-3 aria-invalid:ring-destructive/20 md:text-sm dark:aria-invalid:border-destructive/50 dark:aria-invalid:ring-destructive/40 w-full min-w-0 outline-none file:inline-flex file:border-0 file:bg-transparent file:text-foreground placeholder:text-muted-foreground disabled:pointer-events-none disabled:cursor-not-allowed disabled:opacity-50", className)),
				type,
				value,
				...restProps
			}, void 0, void 0, void 0, 4)}/>`);
		}
		$$renderer.push(`<!--]-->`);
		bind_props($$props, {
			ref,
			value,
			files
		});
	});
}
//#endregion
//#region src/lib/components/ui/sidebar/sidebar-inset.svelte
function Sidebar_inset($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, children, $$slots, $$events, ...restProps } = $$props;
		$$renderer.push(`<main${attributes({
			"data-slot": "sidebar-inset",
			class: clsx$1(cn("bg-background md:peer-data-[variant=inset]:m-2 md:peer-data-[variant=inset]:ml-0 md:peer-data-[variant=inset]:rounded-2xl md:peer-data-[variant=inset]:shadow-sm md:peer-data-[variant=inset]:peer-data-[state=collapsed]:ml-2 relative flex w-full flex-1 flex-col", className)),
			...restProps
		})}>`);
		children?.($$renderer);
		$$renderer.push(`<!----></main>`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/sidebar/sidebar-menu-badge.svelte
function Sidebar_menu_badge($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, children, $$slots, $$events, ...restProps } = $$props;
		$$renderer.push(`<div${attributes({
			"data-slot": "sidebar-menu-badge",
			"data-sidebar": "menu-badge",
			class: clsx$1(cn("pointer-events-none absolute right-1 flex h-5 min-w-5 rounded-xl px-1 text-xs font-medium text-sidebar-foreground peer-hover/menu-button:text-sidebar-accent-foreground peer-data-[size=default]/menu-button:top-1.5 peer-data-[size=lg]/menu-button:top-2.5 peer-data-[size=sm]/menu-button:top-1 peer-data-active/menu-button:text-sidebar-accent-foreground flex items-center justify-center tabular-nums select-none group-data-[collapsible=icon]:hidden", className)),
			...restProps
		})}>`);
		children?.($$renderer);
		$$renderer.push(`<!----></div>`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/tooltip/tooltip-portal.svelte
function Tooltip_portal($$renderer, $$props) {
	let { $$slots, $$events, ...restProps } = $$props;
	if (Portal) {
		$$renderer.push("<!--[-->");
		Portal($$renderer, spread_props([restProps]));
		$$renderer.push("<!--]-->");
	} else {
		$$renderer.push("<!--[!-->");
		$$renderer.push("<!--]-->");
	}
}
//#endregion
//#region src/lib/components/ui/tooltip/tooltip-content.svelte
function Tooltip_content($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, sideOffset = 0, side = "top", children, arrowClasses, portalProps, $$slots, $$events, ...restProps } = $$props;
		let $$settled = true;
		let $$inner_renderer;
		function $$render_inner($$renderer) {
			Tooltip_portal($$renderer, spread_props([portalProps, {
				children: ($$renderer) => {
					if (Tooltip_content$1) {
						$$renderer.push("<!--[-->");
						Tooltip_content$1($$renderer, spread_props([
							{
								"data-slot": "tooltip-content",
								sideOffset,
								side,
								class: cn("inline-flex items-center gap-1.5 rounded-xl px-3 py-1.5 text-xs has-data-[slot=kbd]:pr-1.5 data-[side=bottom]:slide-in-from-top-2 data-[side=left]:slide-in-from-right-2 data-[side=right]:slide-in-from-left-2 data-[side=top]:slide-in-from-bottom-2 **:data-[slot=kbd]:relative **:data-[slot=kbd]:isolate **:data-[slot=kbd]:z-50 **:data-[slot=kbd]:rounded-lg data-[state=delayed-open]:animate-in data-[state=delayed-open]:fade-in-0 data-[state=delayed-open]:zoom-in-95 data-open:animate-in data-open:fade-in-0 data-open:zoom-in-95 data-closed:animate-out data-closed:fade-out-0 data-closed:zoom-out-95 z-50 w-fit max-w-xs origin-(--bits-tooltip-content-transform-origin) bg-foreground text-background", className)
							},
							restProps,
							{
								get ref() {
									return ref;
								},
								set ref($$value) {
									ref = $$value;
									$$settled = false;
								},
								children: ($$renderer) => {
									children?.($$renderer);
									$$renderer.push(`<!----> `);
									{
										function child($$renderer, { props }) {
											$$renderer.push(`<div${attributes({
												class: clsx$1(cn("size-2.5 translate-y-[calc(-50%-2px)] rotate-45 rounded-[2px] data-[side=left]:translate-x-[-1.5px] data-[side=right]:translate-x-[1.5px] z-50 bg-foreground fill-foreground", "data-[side=top]:translate-x-1/2 data-[side=top]:translate-y-[calc(-50%+2px)]", "data-[side=bottom]:-translate-x-1/2 data-[side=bottom]:-translate-y-[calc(-50%+1px)]", "data-[side=right]:translate-x-[calc(50%+2px)] data-[side=right]:translate-y-1/2", "data-[side=left]:-translate-y-[calc(50%-3px)]", arrowClasses)),
												...props
											})}></div>`);
										}
										if (Tooltip_arrow) {
											$$renderer.push("<!--[-->");
											Tooltip_arrow($$renderer, {
												child,
												$$slots: { child: true }
											});
											$$renderer.push("<!--]-->");
										} else {
											$$renderer.push("<!--[!-->");
											$$renderer.push("<!--]-->");
										}
									}
								},
								$$slots: { default: true }
							}
						]));
						$$renderer.push("<!--]-->");
					} else {
						$$renderer.push("<!--[!-->");
						$$renderer.push("<!--]-->");
					}
				},
				$$slots: { default: true }
			}]));
		}
		do {
			$$settled = true;
			$$inner_renderer = $$renderer.copy();
			$$render_inner($$inner_renderer);
		} while (!$$settled);
		$$renderer.subsume($$inner_renderer);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/tooltip/tooltip-provider.svelte
function Tooltip_provider($$renderer, $$props) {
	let { delayDuration = 0, $$slots, $$events, ...restProps } = $$props;
	if (Tooltip_provider$1) {
		$$renderer.push("<!--[-->");
		Tooltip_provider$1($$renderer, spread_props([{ delayDuration }, restProps]));
		$$renderer.push("<!--]-->");
	} else {
		$$renderer.push("<!--[!-->");
		$$renderer.push("<!--]-->");
	}
}
//#endregion
//#region src/lib/components/ui/tooltip/tooltip-trigger.svelte
function Tooltip_trigger($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, $$slots, $$events, ...restProps } = $$props;
		let $$settled = true;
		let $$inner_renderer;
		function $$render_inner($$renderer) {
			if (Tooltip_trigger$1) {
				$$renderer.push("<!--[-->");
				Tooltip_trigger$1($$renderer, spread_props([
					{ "data-slot": "tooltip-trigger" },
					restProps,
					{
						get ref() {
							return ref;
						},
						set ref($$value) {
							ref = $$value;
							$$settled = false;
						}
					}
				]));
				$$renderer.push("<!--]-->");
			} else {
				$$renderer.push("<!--[!-->");
				$$renderer.push("<!--]-->");
			}
		}
		do {
			$$settled = true;
			$$inner_renderer = $$renderer.copy();
			$$render_inner($$inner_renderer);
		} while (!$$settled);
		$$renderer.subsume($$inner_renderer);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/tooltip/tooltip.svelte
function Tooltip($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { open = false, $$slots, $$events, ...restProps } = $$props;
		let $$settled = true;
		let $$inner_renderer;
		function $$render_inner($$renderer) {
			if (Tooltip$1) {
				$$renderer.push("<!--[-->");
				Tooltip$1($$renderer, spread_props([restProps, {
					get open() {
						return open;
					},
					set open($$value) {
						open = $$value;
						$$settled = false;
					}
				}]));
				$$renderer.push("<!--]-->");
			} else {
				$$renderer.push("<!--[!-->");
				$$renderer.push("<!--]-->");
			}
		}
		do {
			$$settled = true;
			$$inner_renderer = $$renderer.copy();
			$$render_inner($$inner_renderer);
		} while (!$$settled);
		$$renderer.subsume($$inner_renderer);
		bind_props($$props, { open });
	});
}
//#endregion
//#region src/lib/hooks/is-mobile.svelte.ts
var DEFAULT_MOBILE_BREAKPOINT = 768;
var IsMobile = class extends MediaQuery {
	constructor(breakpoint = DEFAULT_MOBILE_BREAKPOINT) {
		super(`max-width: ${breakpoint - 1}px`);
	}
};
//#endregion
//#region src/lib/components/ui/sidebar/constants.ts
var SIDEBAR_COOKIE_NAME = "sidebar_state";
var SIDEBAR_COOKIE_MAX_AGE = 604800;
var SIDEBAR_WIDTH = "16rem";
var SIDEBAR_WIDTH_MOBILE = "min(20rem, calc(100vw - 1rem))";
var SIDEBAR_WIDTH_ICON = "3rem";
//#endregion
//#region src/lib/components/ui/sidebar/context.svelte.ts
var SidebarState = class {
	props;
	#open = derived(() => this.props.open());
	get open() {
		return this.#open();
	}
	set open($$value) {
		return this.#open($$value);
	}
	openMobile = false;
	setOpen;
	#isMobile;
	#state = derived(() => this.open ? "expanded" : "collapsed");
	get state() {
		return this.#state();
	}
	set state($$value) {
		return this.#state($$value);
	}
	constructor(props) {
		this.setOpen = props.setOpen;
		this.#isMobile = new IsMobile();
		this.props = props;
	}
	get isMobile() {
		return this.#isMobile.current;
	}
	handleShortcutKeydown = (e) => {
		if (e.key === "b" && (e.metaKey || e.ctrlKey)) {
			e.preventDefault();
			this.toggle();
		}
	};
	setOpenMobile = (value) => {
		this.openMobile = value;
	};
	toggle = () => {
		return this.#isMobile.current ? this.openMobile = !this.openMobile : this.setOpen(!this.open);
	};
};
var SYMBOL_KEY = "scn-sidebar";
/**
* Instantiates a new `SidebarState` instance and sets it in the context.
*
* @param props The constructor props for the `SidebarState` class.
* @returns  The `SidebarState` instance.
*/
function setSidebar(props) {
	return setContext(Symbol.for(SYMBOL_KEY), new SidebarState(props));
}
/**
* Retrieves the `SidebarState` instance from the context. This is a class instance,
* so you cannot destructure it.
* @returns The `SidebarState` instance.
*/
function useSidebar() {
	return getContext(Symbol.for(SYMBOL_KEY));
}
//#endregion
//#region src/lib/components/ui/sidebar/sidebar-menu-button.svelte
var sidebarMenuButtonVariants = tv({
	base: "gap-2 rounded-xl px-3 py-2 text-left text-sm ring-sidebar-ring transition-[width,height,padding] group-has-data-[sidebar=menu-action]/menu-item:pr-8 group-data-[collapsible=icon]:size-8! group-data-[collapsible=icon]:p-2! hover:bg-sidebar-accent hover:text-sidebar-accent-foreground focus-visible:ring-2 active:bg-sidebar-accent active:text-sidebar-accent-foreground data-open:hover:bg-sidebar-accent data-open:hover:text-sidebar-accent-foreground data-active:bg-sidebar-accent data-active:font-medium data-active:text-sidebar-accent-foreground peer/menu-button group/menu-button flex w-full items-center overflow-hidden outline-hidden disabled:pointer-events-none disabled:opacity-50 aria-disabled:pointer-events-none aria-disabled:opacity-50 [&_svg]:size-4 [&_svg]:shrink-0 [&>span:last-child]:truncate",
	variants: {
		variant: {
			default: "hover:bg-sidebar-accent hover:text-sidebar-accent-foreground",
			outline: "bg-background shadow-[0_0_0_1px_var(--sidebar-border)] hover:bg-sidebar-accent hover:text-sidebar-accent-foreground hover:shadow-[0_0_0_1px_var(--sidebar-accent)]"
		},
		size: {
			default: "h-9 text-sm",
			sm: "h-8 text-xs",
			lg: "h-14 px-3 text-sm group-data-[collapsible=icon]:p-0!"
		}
	},
	defaultVariants: {
		variant: "default",
		size: "default"
	}
});
function Sidebar_menu_button($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, children, child, variant = "default", size = "default", isActive = false, tooltipContent, tooltipContentProps, $$slots, $$events, ...restProps } = $$props;
		const sidebar = useSidebar();
		const buttonProps = derived(() => ({
			class: cn(sidebarMenuButtonVariants({
				variant,
				size
			}), className),
			"data-slot": "sidebar-menu-button",
			"data-sidebar": "menu-button",
			"data-size": size,
			"data-active": isActive,
			...restProps
		}));
		function Button($$renderer, { props }) {
			const mergedProps = mergeProps(buttonProps(), props);
			if (child) {
				$$renderer.push("<!--[0-->");
				child($$renderer, { props: mergedProps });
				$$renderer.push(`<!---->`);
			} else {
				$$renderer.push("<!--[-1-->");
				$$renderer.push(`<button${attributes({ ...mergedProps })}>`);
				children?.($$renderer);
				$$renderer.push(`<!----></button>`);
			}
			$$renderer.push(`<!--]-->`);
		}
		if (!tooltipContent) {
			$$renderer.push("<!--[0-->");
			Button($$renderer, {});
		} else {
			$$renderer.push("<!--[-1-->");
			if (Tooltip) {
				$$renderer.push("<!--[-->");
				Tooltip($$renderer, {
					children: ($$renderer) => {
						{
							function child($$renderer, { props }) {
								Button($$renderer, { props });
							}
							if (Tooltip_trigger) {
								$$renderer.push("<!--[-->");
								Tooltip_trigger($$renderer, {
									child,
									$$slots: { child: true }
								});
								$$renderer.push("<!--]-->");
							} else {
								$$renderer.push("<!--[!-->");
								$$renderer.push("<!--]-->");
							}
						}
						$$renderer.push(` `);
						if (Tooltip_content) {
							$$renderer.push("<!--[-->");
							Tooltip_content($$renderer, spread_props([
								{
									side: "right",
									align: "center",
									hidden: sidebar.state !== "collapsed" || sidebar.isMobile
								},
								tooltipContentProps,
								{
									children: ($$renderer) => {
										if (typeof tooltipContent === "string") {
											$$renderer.push("<!--[0-->");
											$$renderer.push(`${escape_html(tooltipContent)}`);
										} else if (tooltipContent) {
											$$renderer.push("<!--[1-->");
											tooltipContent($$renderer);
											$$renderer.push(`<!---->`);
										} else $$renderer.push("<!--[-1-->");
										$$renderer.push(`<!--]-->`);
									},
									$$slots: { default: true }
								}
							]));
							$$renderer.push("<!--]-->");
						} else {
							$$renderer.push("<!--[!-->");
							$$renderer.push("<!--]-->");
						}
					},
					$$slots: { default: true }
				});
				$$renderer.push("<!--]-->");
			} else {
				$$renderer.push("<!--[!-->");
				$$renderer.push("<!--]-->");
			}
		}
		$$renderer.push(`<!--]-->`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/sidebar/sidebar-menu-item.svelte
function Sidebar_menu_item($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, children, $$slots, $$events, ...restProps } = $$props;
		$$renderer.push(`<li${attributes({
			"data-slot": "sidebar-menu-item",
			"data-sidebar": "menu-item",
			class: clsx$1(cn("group/menu-item relative", className)),
			...restProps
		})}>`);
		children?.($$renderer);
		$$renderer.push(`<!----></li>`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/sidebar/sidebar-menu-sub.svelte
function Sidebar_menu_sub($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, children, $$slots, $$events, ...restProps } = $$props;
		$$renderer.push(`<ul${attributes({
			"data-slot": "sidebar-menu-sub",
			"data-sidebar": "menu-sub",
			class: clsx$1(cn("mx-3.5 translate-x-px gap-1 border-l border-sidebar-border px-2.5 py-0.5 group-data-[collapsible=icon]:hidden flex min-w-0 flex-col", className)),
			...restProps
		})}>`);
		children?.($$renderer);
		$$renderer.push(`<!----></ul>`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/sidebar/sidebar-menu.svelte
function Sidebar_menu($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, children, $$slots, $$events, ...restProps } = $$props;
		$$renderer.push(`<ul${attributes({
			"data-slot": "sidebar-menu",
			"data-sidebar": "menu",
			class: clsx$1(cn("gap-0.5 flex w-full min-w-0 flex-col", className)),
			...restProps
		})}>`);
		children?.($$renderer);
		$$renderer.push(`<!----></ul>`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/sidebar/sidebar-provider.svelte
function Sidebar_provider($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, open = true, onOpenChange = () => {}, class: className, style, children, $$slots, $$events, ...restProps } = $$props;
		setSidebar({
			open: () => open,
			setOpen: (value) => {
				open = value;
				onOpenChange(value);
				document.cookie = `${SIDEBAR_COOKIE_NAME}=${open}; path=/; max-age=${SIDEBAR_COOKIE_MAX_AGE}`;
			}
		});
		if (Tooltip_provider) {
			$$renderer.push("<!--[-->");
			Tooltip_provider($$renderer, {
				delayDuration: 0,
				children: ($$renderer) => {
					$$renderer.push(`<div${attributes({
						"data-slot": "sidebar-wrapper",
						style: `--sidebar-width: ${stringify(SIDEBAR_WIDTH)}; --sidebar-width-icon: ${stringify(SIDEBAR_WIDTH_ICON)}; ${stringify(style)}`,
						class: clsx$1(cn("group/sidebar-wrapper flex min-h-svh w-full has-data-[variant=inset]:bg-sidebar", className)),
						...restProps
					})}>`);
					children?.($$renderer);
					$$renderer.push(`<!----></div>`);
				},
				$$slots: { default: true }
			});
			$$renderer.push("<!--]-->");
		} else {
			$$renderer.push("<!--[!-->");
			$$renderer.push("<!--]-->");
		}
		bind_props($$props, {
			ref,
			open
		});
	});
}
//#endregion
//#region src/lib/components/ui/sidebar/sidebar-rail.svelte
function Sidebar_rail($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, children, $$slots, $$events, ...restProps } = $$props;
		useSidebar();
		$$renderer.push(`<button${attributes({
			"data-sidebar": "rail",
			"data-slot": "sidebar-rail",
			"aria-label": "Toggle Sidebar",
			tabindex: -1,
			title: "Toggle Sidebar",
			class: clsx$1(cn("hover:after:bg-sidebar-border absolute inset-y-0 z-20 hidden w-4 -translate-x-1/2 transition-all ease-linear group-data-[side=left]:-right-4 group-data-[side=right]:left-0 after:absolute after:inset-y-0 after:left-1/2 after:w-[2px] sm:flex", "in-data-[side=left]:cursor-w-resize in-data-[side=right]:cursor-e-resize", "[[data-side=left][data-state=collapsed]_&]:cursor-e-resize [[data-side=right][data-state=collapsed]_&]:cursor-w-resize", "group-data-[collapsible=offcanvas]:translate-x-0 group-data-[collapsible=offcanvas]:after:left-full hover:group-data-[collapsible=offcanvas]:bg-sidebar", "[[data-side=left][data-collapsible=offcanvas]_&]:-right-2", "[[data-side=right][data-collapsible=offcanvas]_&]:-left-2", className)),
			...restProps
		})}>`);
		children?.($$renderer);
		$$renderer.push(`<!----></button>`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/separator/separator.svelte
function Separator($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, "data-slot": dataSlot = "separator", $$slots, $$events, ...restProps } = $$props;
		let $$settled = true;
		let $$inner_renderer;
		function $$render_inner($$renderer) {
			if (Separator$1) {
				$$renderer.push("<!--[-->");
				Separator$1($$renderer, spread_props([
					{
						"data-slot": dataSlot,
						class: cn("shrink-0 bg-border data-[orientation=horizontal]:h-px data-[orientation=horizontal]:w-full data-[orientation=vertical]:w-px", "data-[orientation=vertical]:h-full", className)
					},
					restProps,
					{
						get ref() {
							return ref;
						},
						set ref($$value) {
							ref = $$value;
							$$settled = false;
						}
					}
				]));
				$$renderer.push("<!--]-->");
			} else {
				$$renderer.push("<!--[!-->");
				$$renderer.push("<!--]-->");
			}
		}
		do {
			$$settled = true;
			$$inner_renderer = $$renderer.copy();
			$$render_inner($$inner_renderer);
		} while (!$$settled);
		$$renderer.subsume($$inner_renderer);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region node_modules/@lucide/svelte/dist/icons/panel-left.svelte
function Panel_left($$renderer, $$props) {
	let { $$slots, $$events, ...props } = $$props;
	Icon($$renderer, spread_props([
		{ name: "panel-left" },
		props,
		{ iconNode: [["rect", {
			"width": "18",
			"height": "18",
			"x": "3",
			"y": "3",
			"rx": "2"
		}], ["path", { "d": "M9 3v18" }]] }
	]));
}
//#endregion
//#region src/lib/components/ui/sidebar/sidebar-trigger.svelte
function Sidebar_trigger($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, onclick, $$slots, $$events, ...restProps } = $$props;
		const sidebar = useSidebar();
		let $$settled = true;
		let $$inner_renderer;
		function $$render_inner($$renderer) {
			Button($$renderer, spread_props([
				{
					"data-sidebar": "trigger",
					"data-slot": "sidebar-trigger",
					variant: "ghost",
					size: "icon-sm",
					class: cn("cn-sidebar-trigger", className),
					type: "button",
					onclick: (e) => {
						onclick?.(e);
						sidebar.toggle();
					}
				},
				restProps,
				{
					get ref() {
						return ref;
					},
					set ref($$value) {
						ref = $$value;
						$$settled = false;
					},
					children: ($$renderer) => {
						Panel_left($$renderer, {});
						$$renderer.push(`<!----> <span class="sr-only">Toggle Sidebar</span>`);
					},
					$$slots: { default: true }
				}
			]));
		}
		do {
			$$settled = true;
			$$inner_renderer = $$renderer.copy();
			$$render_inner($$inner_renderer);
		} while (!$$settled);
		$$renderer.subsume($$inner_renderer);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/sheet/sheet-overlay.svelte
function Sheet_overlay($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, $$slots, $$events, ...restProps } = $$props;
		let $$settled = true;
		let $$inner_renderer;
		function $$render_inner($$renderer) {
			if (Dialog_overlay$1) {
				$$renderer.push("<!--[-->");
				Dialog_overlay$1($$renderer, spread_props([
					{
						"data-slot": "sheet-overlay",
						class: cn("bg-black/30 supports-backdrop-filter:backdrop-blur-sm fixed inset-0 z-50", className)
					},
					restProps,
					{
						get ref() {
							return ref;
						},
						set ref($$value) {
							ref = $$value;
							$$settled = false;
						}
					}
				]));
				$$renderer.push("<!--]-->");
			} else {
				$$renderer.push("<!--[!-->");
				$$renderer.push("<!--]-->");
			}
		}
		do {
			$$settled = true;
			$$inner_renderer = $$renderer.copy();
			$$render_inner($$inner_renderer);
		} while (!$$settled);
		$$renderer.subsume($$inner_renderer);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/sheet/sheet-portal.svelte
function Sheet_portal($$renderer, $$props) {
	let { $$slots, $$events, ...restProps } = $$props;
	if (Portal) {
		$$renderer.push("<!--[-->");
		Portal($$renderer, spread_props([restProps]));
		$$renderer.push("<!--]-->");
	} else {
		$$renderer.push("<!--[!-->");
		$$renderer.push("<!--]-->");
	}
}
//#endregion
//#region src/lib/components/ui/sheet/sheet-content.svelte
function Sheet_content($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, side = "right", showCloseButton = true, portalProps, children, $$slots, $$events, ...restProps } = $$props;
		let $$settled = true;
		let $$inner_renderer;
		function $$render_inner($$renderer) {
			Sheet_portal($$renderer, spread_props([portalProps, {
				children: ($$renderer) => {
					Sheet_overlay($$renderer, {});
					$$renderer.push(`<!----> `);
					if (Dialog_content$1) {
						$$renderer.push("<!--[-->");
						Dialog_content$1($$renderer, spread_props([
							{
								"data-slot": "sheet-content",
								"data-side": side,
								class: cn("fixed z-50 flex flex-col bg-popover bg-clip-padding text-sm text-popover-foreground shadow-xl transition duration-200 ease-in-out data-[side=bottom]:inset-x-0 data-[side=bottom]:bottom-0 data-[side=bottom]:h-auto data-[side=bottom]:border-t data-[side=left]:inset-y-0 data-[side=left]:left-0 data-[side=left]:h-full data-[side=left]:w-3/4 data-[side=left]:border-r data-[side=right]:inset-y-0 data-[side=right]:right-0 data-[side=right]:h-full data-[side=right]:w-3/4 data-[side=right]:border-l data-[side=top]:inset-x-0 data-[side=top]:top-0 data-[side=top]:h-auto data-[side=top]:border-b data-[side=left]:sm:max-w-sm data-[side=right]:sm:max-w-sm data-open:animate-in data-open:fade-in-0 data-[side=bottom]:data-open:slide-in-from-bottom-10 data-[side=left]:data-open:slide-in-from-left-10 data-[side=right]:data-open:slide-in-from-right-10 data-[side=top]:data-open:slide-in-from-top-10 data-closed:animate-out data-closed:fade-out-0 data-[side=bottom]:data-closed:slide-out-to-bottom-10 data-[side=left]:data-closed:slide-out-to-left-10 data-[side=right]:data-closed:slide-out-to-right-10 data-[side=top]:data-closed:slide-out-to-top-10", className)
							},
							restProps,
							{
								get ref() {
									return ref;
								},
								set ref($$value) {
									ref = $$value;
									$$settled = false;
								},
								children: ($$renderer) => {
									children?.($$renderer);
									$$renderer.push(`<!----> `);
									if (showCloseButton) {
										$$renderer.push("<!--[0-->");
										{
											function child($$renderer, { props }) {
												Button($$renderer, spread_props([
													{
														variant: "ghost",
														class: "absolute top-4 right-4 bg-secondary",
														size: "icon-sm"
													},
													props,
													{
														children: ($$renderer) => {
															X($$renderer, {});
															$$renderer.push(`<!----> <span class="sr-only">Close</span>`);
														},
														$$slots: { default: true }
													}
												]));
											}
											if (Dialog_close) {
												$$renderer.push("<!--[-->");
												Dialog_close($$renderer, {
													"data-slot": "sheet-close",
													child,
													$$slots: { child: true }
												});
												$$renderer.push("<!--]-->");
											} else {
												$$renderer.push("<!--[!-->");
												$$renderer.push("<!--]-->");
											}
										}
									} else $$renderer.push("<!--[-1-->");
									$$renderer.push(`<!--]-->`);
								},
								$$slots: { default: true }
							}
						]));
						$$renderer.push("<!--]-->");
					} else {
						$$renderer.push("<!--[!-->");
						$$renderer.push("<!--]-->");
					}
				},
				$$slots: { default: true }
			}]));
		}
		do {
			$$settled = true;
			$$inner_renderer = $$renderer.copy();
			$$render_inner($$inner_renderer);
		} while (!$$settled);
		$$renderer.subsume($$inner_renderer);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/sheet/sheet-description.svelte
function Sheet_description($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, $$slots, $$events, ...restProps } = $$props;
		let $$settled = true;
		let $$inner_renderer;
		function $$render_inner($$renderer) {
			if (Dialog_description$1) {
				$$renderer.push("<!--[-->");
				Dialog_description$1($$renderer, spread_props([
					{
						"data-slot": "sheet-description",
						class: cn("text-sm text-muted-foreground", className)
					},
					restProps,
					{
						get ref() {
							return ref;
						},
						set ref($$value) {
							ref = $$value;
							$$settled = false;
						}
					}
				]));
				$$renderer.push("<!--]-->");
			} else {
				$$renderer.push("<!--[!-->");
				$$renderer.push("<!--]-->");
			}
		}
		do {
			$$settled = true;
			$$inner_renderer = $$renderer.copy();
			$$render_inner($$inner_renderer);
		} while (!$$settled);
		$$renderer.subsume($$inner_renderer);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/sheet/sheet-header.svelte
function Sheet_header($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, children, $$slots, $$events, ...restProps } = $$props;
		$$renderer.push(`<div${attributes({
			"data-slot": "sheet-header",
			class: clsx$1(cn("gap-1.5 p-6 flex flex-col", className)),
			...restProps
		})}>`);
		children?.($$renderer);
		$$renderer.push(`<!----></div>`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/sheet/sheet-title.svelte
function Sheet_title($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, $$slots, $$events, ...restProps } = $$props;
		let $$settled = true;
		let $$inner_renderer;
		function $$render_inner($$renderer) {
			if (Dialog_title$1) {
				$$renderer.push("<!--[-->");
				Dialog_title$1($$renderer, spread_props([
					{
						"data-slot": "sheet-title",
						class: cn("text-base font-medium text-foreground", className)
					},
					restProps,
					{
						get ref() {
							return ref;
						},
						set ref($$value) {
							ref = $$value;
							$$settled = false;
						}
					}
				]));
				$$renderer.push("<!--]-->");
			} else {
				$$renderer.push("<!--[!-->");
				$$renderer.push("<!--]-->");
			}
		}
		do {
			$$settled = true;
			$$inner_renderer = $$renderer.copy();
			$$render_inner($$inner_renderer);
		} while (!$$settled);
		$$renderer.subsume($$inner_renderer);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/sheet/sheet.svelte
function Sheet($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { open = false, $$slots, $$events, ...restProps } = $$props;
		let $$settled = true;
		let $$inner_renderer;
		function $$render_inner($$renderer) {
			if (Dialog$1) {
				$$renderer.push("<!--[-->");
				Dialog$1($$renderer, spread_props([restProps, {
					get open() {
						return open;
					},
					set open($$value) {
						open = $$value;
						$$settled = false;
					}
				}]));
				$$renderer.push("<!--]-->");
			} else {
				$$renderer.push("<!--[!-->");
				$$renderer.push("<!--]-->");
			}
		}
		do {
			$$settled = true;
			$$inner_renderer = $$renderer.copy();
			$$render_inner($$inner_renderer);
		} while (!$$settled);
		$$renderer.subsume($$inner_renderer);
		bind_props($$props, { open });
	});
}
//#endregion
//#region src/lib/components/ui/sidebar/sidebar.svelte
function Sidebar($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, side = "left", variant = "sidebar", collapsible = "offcanvas", class: className, children, $$slots, $$events, ...restProps } = $$props;
		const sidebar = useSidebar();
		let $$settled = true;
		let $$inner_renderer;
		function $$render_inner($$renderer) {
			if (collapsible === "none") {
				$$renderer.push("<!--[0-->");
				$$renderer.push(`<div${attributes({
					class: clsx$1(cn("flex h-full w-(--sidebar-width) flex-col bg-sidebar text-sidebar-foreground", className)),
					...restProps
				})}>`);
				children?.($$renderer);
				$$renderer.push(`<!----></div>`);
			} else if (sidebar.isMobile) {
				$$renderer.push("<!--[1-->");
				var bind_get = () => sidebar.openMobile;
				var bind_set = (v) => sidebar.setOpenMobile(v);
				if (Sheet) {
					$$renderer.push("<!--[-->");
					Sheet($$renderer, spread_props([
						{
							get open() {
								return bind_get();
							},
							set open($$value) {
								bind_set($$value);
							}
						},
						restProps,
						{
							children: ($$renderer) => {
								if (Sheet_content) {
									$$renderer.push("<!--[-->");
									Sheet_content($$renderer, {
										"data-sidebar": "sidebar",
										"data-slot": "sidebar",
										"data-mobile": "true",
										class: cn("w-(--sidebar-width)! bg-sidebar p-0 text-sidebar-foreground data-[side=left]:w-(--sidebar-width)! data-[side=right]:w-(--sidebar-width)!", className),
										style: `--sidebar-width: var(--sidebar-width-mobile, ${stringify(SIDEBAR_WIDTH_MOBILE)});`,
										side,
										get ref() {
											return ref;
										},
										set ref($$value) {
											ref = $$value;
											$$settled = false;
										},
										children: ($$renderer) => {
											if (Sheet_header) {
												$$renderer.push("<!--[-->");
												Sheet_header($$renderer, {
													class: "sr-only",
													children: ($$renderer) => {
														if (Sheet_title) {
															$$renderer.push("<!--[-->");
															Sheet_title($$renderer, {
																children: ($$renderer) => {
																	$$renderer.push(`<!---->Sidebar`);
																},
																$$slots: { default: true }
															});
															$$renderer.push("<!--]-->");
														} else {
															$$renderer.push("<!--[!-->");
															$$renderer.push("<!--]-->");
														}
														$$renderer.push(` `);
														if (Sheet_description) {
															$$renderer.push("<!--[-->");
															Sheet_description($$renderer, {
																children: ($$renderer) => {
																	$$renderer.push(`<!---->Displays the mobile sidebar.`);
																},
																$$slots: { default: true }
															});
															$$renderer.push("<!--]-->");
														} else {
															$$renderer.push("<!--[!-->");
															$$renderer.push("<!--]-->");
														}
													},
													$$slots: { default: true }
												});
												$$renderer.push("<!--]-->");
											} else {
												$$renderer.push("<!--[!-->");
												$$renderer.push("<!--]-->");
											}
											$$renderer.push(` <div class="flex h-full w-full flex-col">`);
											children?.($$renderer);
											$$renderer.push(`<!----></div>`);
										},
										$$slots: { default: true }
									});
									$$renderer.push("<!--]-->");
								} else {
									$$renderer.push("<!--[!-->");
									$$renderer.push("<!--]-->");
								}
							},
							$$slots: { default: true }
						}
					]));
					$$renderer.push("<!--]-->");
				} else {
					$$renderer.push("<!--[!-->");
					$$renderer.push("<!--]-->");
				}
			} else {
				$$renderer.push("<!--[-1-->");
				$$renderer.push(`<div class="group peer hidden text-sidebar-foreground md:block"${attr("data-state", sidebar.state)}${attr("data-collapsible", sidebar.state === "collapsed" ? collapsible : "")}${attr("data-variant", variant)}${attr("data-side", side)} data-slot="sidebar"><div data-slot="sidebar-gap"${attr_class(clsx$1(cn("transition-[width] duration-200 ease-linear relative w-(--sidebar-width) bg-transparent", "group-data-[collapsible=offcanvas]:w-0", "group-data-[side=right]:rotate-180", variant === "floating" || variant === "inset" ? "group-data-[collapsible=icon]:w-[calc(var(--sidebar-width-icon)+(--spacing(4)))]" : "group-data-[collapsible=icon]:w-(--sidebar-width-icon)")))}></div> <div${attributes({
					"data-slot": "sidebar-container",
					class: clsx$1(cn("fixed inset-y-0 z-10 hidden h-svh w-(--sidebar-width) transition-[left,right,width] duration-200 ease-linear md:flex", side === "left" ? "start-0 group-data-[collapsible=offcanvas]:start-[calc(var(--sidebar-width)*-1)]" : "end-0 group-data-[collapsible=offcanvas]:end-[calc(var(--sidebar-width)*-1)]", variant === "floating" || variant === "inset" ? "p-2 group-data-[collapsible=icon]:w-[calc(var(--sidebar-width-icon)+(--spacing(4))+2px)]" : "group-data-[collapsible=icon]:w-(--sidebar-width-icon) group-data-[side=left]:border-e group-data-[side=right]:border-s", className)),
					...restProps
				})}><div data-sidebar="sidebar" data-slot="sidebar-inner" class="bg-sidebar group-data-[variant=floating]:rounded-2xl group-data-[variant=floating]:shadow-sm group-data-[variant=floating]:ring-1 group-data-[variant=floating]:ring-sidebar-border flex size-full flex-col">`);
				children?.($$renderer);
				$$renderer.push(`<!----></div></div></div>`);
			}
			$$renderer.push(`<!--]-->`);
		}
		do {
			$$settled = true;
			$$inner_renderer = $$renderer.copy();
			$$render_inner($$inner_renderer);
		} while (!$$settled);
		$$renderer.subsume($$inner_renderer);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/EditorSettingsFooter.svelte
function EditorSettingsFooter($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { locale, themeMode, themePalette, onlocalechange, onthememodechange, onthemepalettechange, onabout } = $$props;
		const modeNames = {
			system: "System",
			light: "Light",
			dark: "Dark"
		};
		const paletteNames = {
			runic: "Runic Gold",
			moss: "Moss",
			fjord: "Fjord",
			ember: "Ember"
		};
		let localeName = derived(() => locale === "de" ? "Deutsch" : "English");
		let appearanceName = derived(() => `${paletteNames[themePalette]} · ${modeNames[themeMode]}`);
		if (Sidebar_footer) {
			$$renderer.push("<!--[-->");
			Sidebar_footer($$renderer, {
				class: "border-t border-sidebar-border p-2",
				children: ($$renderer) => {
					if (Sidebar_menu) {
						$$renderer.push("<!--[-->");
						Sidebar_menu($$renderer, {
							children: ($$renderer) => {
								if (Sidebar_menu_item) {
									$$renderer.push("<!--[-->");
									Sidebar_menu_item($$renderer, {
										children: ($$renderer) => {
											if (Dropdown_menu) {
												$$renderer.push("<!--[-->");
												Dropdown_menu($$renderer, {
													children: ($$renderer) => {
														{
															function child($$renderer, { props }) {
																if (Sidebar_menu_button) {
																	$$renderer.push("<!--[-->");
																	Sidebar_menu_button($$renderer, spread_props([props, {
																		size: "lg",
																		"aria-label": `Editor settings, ${appearanceName()}, interface language ${localeName()}`,
																		tooltipContent: "Editor settings",
																		children: ($$renderer) => {
																			Badge($$renderer, {
																				variant: "outline",
																				class: "size-8 shrink-0 justify-center p-0",
																				children: ($$renderer) => {
																					Settings_2($$renderer, { "aria-hidden": "true" });
																				},
																				$$slots: { default: true }
																			});
																			$$renderer.push(`<!----> <span class="grid min-w-0 flex-1 text-left text-sm leading-tight"><span class="truncate font-medium">Editor settings</span> <span class="truncate text-xs text-muted-foreground">${escape_html(appearanceName())} · ${escape_html(localeName())}</span></span> `);
																			Chevrons_up_down($$renderer, {
																				class: "ml-auto",
																				"aria-hidden": "true"
																			});
																			$$renderer.push(`<!---->`);
																		},
																		$$slots: { default: true }
																	}]));
																	$$renderer.push("<!--]-->");
																} else {
																	$$renderer.push("<!--[!-->");
																	$$renderer.push("<!--]-->");
																}
															}
															if (Dropdown_menu_trigger) {
																$$renderer.push("<!--[-->");
																Dropdown_menu_trigger($$renderer, {
																	child,
																	$$slots: { child: true }
																});
																$$renderer.push("<!--]-->");
															} else {
																$$renderer.push("<!--[!-->");
																$$renderer.push("<!--]-->");
															}
														}
														$$renderer.push(` `);
														if (Dropdown_menu_content) {
															$$renderer.push("<!--[-->");
															Dropdown_menu_content($$renderer, {
																class: "w-(--bits-dropdown-menu-anchor-width) min-w-64",
																align: "start",
																side: "top",
																children: ($$renderer) => {
																	if (Dropdown_menu_label) {
																		$$renderer.push("<!--[-->");
																		Dropdown_menu_label($$renderer, {
																			children: ($$renderer) => {
																				$$renderer.push(`<!---->Appearance`);
																			},
																			$$slots: { default: true }
																		});
																		$$renderer.push("<!--]-->");
																	} else {
																		$$renderer.push("<!--[!-->");
																		$$renderer.push("<!--]-->");
																	}
																	$$renderer.push(` `);
																	if (Dropdown_menu_radio_group) {
																		$$renderer.push("<!--[-->");
																		Dropdown_menu_radio_group($$renderer, {
																			value: themeMode,
																			onValueChange: (value) => onthememodechange(value),
																			children: ($$renderer) => {
																				if (Dropdown_menu_radio_item) {
																					$$renderer.push("<!--[-->");
																					Dropdown_menu_radio_item($$renderer, {
																						value: "system",
																						children: ($$renderer) => {
																							Monitor($$renderer, {});
																							$$renderer.push(`<!---->System`);
																						},
																						$$slots: { default: true }
																					});
																					$$renderer.push("<!--]-->");
																				} else {
																					$$renderer.push("<!--[!-->");
																					$$renderer.push("<!--]-->");
																				}
																				$$renderer.push(` `);
																				if (Dropdown_menu_radio_item) {
																					$$renderer.push("<!--[-->");
																					Dropdown_menu_radio_item($$renderer, {
																						value: "light",
																						children: ($$renderer) => {
																							Sun($$renderer, {});
																							$$renderer.push(`<!---->Light`);
																						},
																						$$slots: { default: true }
																					});
																					$$renderer.push("<!--]-->");
																				} else {
																					$$renderer.push("<!--[!-->");
																					$$renderer.push("<!--]-->");
																				}
																				$$renderer.push(` `);
																				if (Dropdown_menu_radio_item) {
																					$$renderer.push("<!--[-->");
																					Dropdown_menu_radio_item($$renderer, {
																						value: "dark",
																						children: ($$renderer) => {
																							Moon($$renderer, {});
																							$$renderer.push(`<!---->Dark`);
																						},
																						$$slots: { default: true }
																					});
																					$$renderer.push("<!--]-->");
																				} else {
																					$$renderer.push("<!--[!-->");
																					$$renderer.push("<!--]-->");
																				}
																			},
																			$$slots: { default: true }
																		});
																		$$renderer.push("<!--]-->");
																	} else {
																		$$renderer.push("<!--[!-->");
																		$$renderer.push("<!--]-->");
																	}
																	$$renderer.push(` `);
																	if (Dropdown_menu_separator) {
																		$$renderer.push("<!--[-->");
																		Dropdown_menu_separator($$renderer, {});
																		$$renderer.push("<!--]-->");
																	} else {
																		$$renderer.push("<!--[!-->");
																		$$renderer.push("<!--]-->");
																	}
																	$$renderer.push(` `);
																	if (Dropdown_menu_label) {
																		$$renderer.push("<!--[-->");
																		Dropdown_menu_label($$renderer, {
																			children: ($$renderer) => {
																				$$renderer.push(`<!---->Color theme`);
																			},
																			$$slots: { default: true }
																		});
																		$$renderer.push("<!--]-->");
																	} else {
																		$$renderer.push("<!--[!-->");
																		$$renderer.push("<!--]-->");
																	}
																	$$renderer.push(` `);
																	if (Dropdown_menu_radio_group) {
																		$$renderer.push("<!--[-->");
																		Dropdown_menu_radio_group($$renderer, {
																			value: themePalette,
																			onValueChange: (value) => onthemepalettechange(value),
																			children: ($$renderer) => {
																				if (Dropdown_menu_radio_item) {
																					$$renderer.push("<!--[-->");
																					Dropdown_menu_radio_item($$renderer, {
																						value: "runic",
																						children: ($$renderer) => {
																							Palette($$renderer, {});
																							$$renderer.push(`<!---->Runic Gold`);
																						},
																						$$slots: { default: true }
																					});
																					$$renderer.push("<!--]-->");
																				} else {
																					$$renderer.push("<!--[!-->");
																					$$renderer.push("<!--]-->");
																				}
																				$$renderer.push(` `);
																				if (Dropdown_menu_radio_item) {
																					$$renderer.push("<!--[-->");
																					Dropdown_menu_radio_item($$renderer, {
																						value: "moss",
																						children: ($$renderer) => {
																							Palette($$renderer, {});
																							$$renderer.push(`<!---->Moss`);
																						},
																						$$slots: { default: true }
																					});
																					$$renderer.push("<!--]-->");
																				} else {
																					$$renderer.push("<!--[!-->");
																					$$renderer.push("<!--]-->");
																				}
																				$$renderer.push(` `);
																				if (Dropdown_menu_radio_item) {
																					$$renderer.push("<!--[-->");
																					Dropdown_menu_radio_item($$renderer, {
																						value: "fjord",
																						children: ($$renderer) => {
																							Palette($$renderer, {});
																							$$renderer.push(`<!---->Fjord`);
																						},
																						$$slots: { default: true }
																					});
																					$$renderer.push("<!--]-->");
																				} else {
																					$$renderer.push("<!--[!-->");
																					$$renderer.push("<!--]-->");
																				}
																				$$renderer.push(` `);
																				if (Dropdown_menu_radio_item) {
																					$$renderer.push("<!--[-->");
																					Dropdown_menu_radio_item($$renderer, {
																						value: "ember",
																						children: ($$renderer) => {
																							Palette($$renderer, {});
																							$$renderer.push(`<!---->Ember`);
																						},
																						$$slots: { default: true }
																					});
																					$$renderer.push("<!--]-->");
																				} else {
																					$$renderer.push("<!--[!-->");
																					$$renderer.push("<!--]-->");
																				}
																			},
																			$$slots: { default: true }
																		});
																		$$renderer.push("<!--]-->");
																	} else {
																		$$renderer.push("<!--[!-->");
																		$$renderer.push("<!--]-->");
																	}
																	$$renderer.push(` `);
																	if (Dropdown_menu_separator) {
																		$$renderer.push("<!--[-->");
																		Dropdown_menu_separator($$renderer, {});
																		$$renderer.push("<!--]-->");
																	} else {
																		$$renderer.push("<!--[!-->");
																		$$renderer.push("<!--]-->");
																	}
																	$$renderer.push(` `);
																	if (Dropdown_menu_label) {
																		$$renderer.push("<!--[-->");
																		Dropdown_menu_label($$renderer, {
																			children: ($$renderer) => {
																				$$renderer.push(`<!---->Interface language`);
																			},
																			$$slots: { default: true }
																		});
																		$$renderer.push("<!--]-->");
																	} else {
																		$$renderer.push("<!--[!-->");
																		$$renderer.push("<!--]-->");
																	}
																	$$renderer.push(` `);
																	if (Dropdown_menu_radio_group) {
																		$$renderer.push("<!--[-->");
																		Dropdown_menu_radio_group($$renderer, {
																			value: locale,
																			onValueChange: onlocalechange,
																			children: ($$renderer) => {
																				if (Dropdown_menu_radio_item) {
																					$$renderer.push("<!--[-->");
																					Dropdown_menu_radio_item($$renderer, {
																						value: "en",
																						children: ($$renderer) => {
																							Languages($$renderer, {});
																							$$renderer.push(`<!----> English`);
																						},
																						$$slots: { default: true }
																					});
																					$$renderer.push("<!--]-->");
																				} else {
																					$$renderer.push("<!--[!-->");
																					$$renderer.push("<!--]-->");
																				}
																				$$renderer.push(` `);
																				if (Dropdown_menu_radio_item) {
																					$$renderer.push("<!--[-->");
																					Dropdown_menu_radio_item($$renderer, {
																						value: "de",
																						children: ($$renderer) => {
																							Languages($$renderer, {});
																							$$renderer.push(`<!----> Deutsch`);
																						},
																						$$slots: { default: true }
																					});
																					$$renderer.push("<!--]-->");
																				} else {
																					$$renderer.push("<!--[!-->");
																					$$renderer.push("<!--]-->");
																				}
																			},
																			$$slots: { default: true }
																		});
																		$$renderer.push("<!--]-->");
																	} else {
																		$$renderer.push("<!--[!-->");
																		$$renderer.push("<!--]-->");
																	}
																	$$renderer.push(` `);
																	if (Dropdown_menu_separator) {
																		$$renderer.push("<!--[-->");
																		Dropdown_menu_separator($$renderer, {});
																		$$renderer.push("<!--]-->");
																	} else {
																		$$renderer.push("<!--[!-->");
																		$$renderer.push("<!--]-->");
																	}
																	$$renderer.push(` `);
																	if (Dropdown_menu_item) {
																		$$renderer.push("<!--[-->");
																		Dropdown_menu_item($$renderer, {
																			onclick: onabout,
																			children: ($$renderer) => {
																				Info($$renderer, {});
																				$$renderer.push(`<!----> About &amp; diagnostics`);
																			},
																			$$slots: { default: true }
																		});
																		$$renderer.push("<!--]-->");
																	} else {
																		$$renderer.push("<!--[!-->");
																		$$renderer.push("<!--]-->");
																	}
																},
																$$slots: { default: true }
															});
															$$renderer.push("<!--]-->");
														} else {
															$$renderer.push("<!--[!-->");
															$$renderer.push("<!--]-->");
														}
													},
													$$slots: { default: true }
												});
												$$renderer.push("<!--]-->");
											} else {
												$$renderer.push("<!--[!-->");
												$$renderer.push("<!--]-->");
											}
										},
										$$slots: { default: true }
									});
									$$renderer.push("<!--]-->");
								} else {
									$$renderer.push("<!--[!-->");
									$$renderer.push("<!--]-->");
								}
							},
							$$slots: { default: true }
						});
						$$renderer.push("<!--]-->");
					} else {
						$$renderer.push("<!--[!-->");
						$$renderer.push("<!--]-->");
					}
				},
				$$slots: { default: true }
			});
			$$renderer.push("<!--]-->");
		} else {
			$$renderer.push("<!--[!-->");
			$$renderer.push("<!--]-->");
		}
	});
}
//#endregion
//#region node_modules/@lucide/svelte/dist/icons/circle-alert.svelte
function Circle_alert($$renderer, $$props) {
	let { $$slots, $$events, ...props } = $$props;
	Icon($$renderer, spread_props([
		{ name: "circle-alert" },
		props,
		{ iconNode: [
			["circle", {
				"cx": "12",
				"cy": "12",
				"r": "10"
			}],
			["line", {
				"x1": "12",
				"x2": "12",
				"y1": "8",
				"y2": "12"
			}],
			["line", {
				"x1": "12",
				"x2": "12.01",
				"y1": "16",
				"y2": "16"
			}]
		] }
	]));
}
//#endregion
//#region node_modules/@lucide/svelte/dist/icons/folder-open.svelte
function Folder_open($$renderer, $$props) {
	let { $$slots, $$events, ...props } = $$props;
	Icon($$renderer, spread_props([
		{ name: "folder-open" },
		props,
		{ iconNode: [["path", { "d": "m6 14 1.5-2.9A2 2 0 0 1 9.24 10H20a2 2 0 0 1 1.94 2.5l-1.54 6a2 2 0 0 1-1.95 1.5H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h3.9a2 2 0 0 1 1.69.9l.81 1.2a2 2 0 0 0 1.67.9H18a2 2 0 0 1 2 2v2" }]] }
	]));
}
//#endregion
//#region node_modules/@lucide/svelte/dist/icons/plus.svelte
function Plus($$renderer, $$props) {
	let { $$slots, $$events, ...props } = $$props;
	Icon($$renderer, spread_props([
		{ name: "plus" },
		props,
		{ iconNode: [["path", { "d": "M5 12h14" }], ["path", { "d": "M12 5v14" }]] }
	]));
}
//#endregion
//#region node_modules/@lucide/svelte/dist/icons/refresh-cw.svelte
function Refresh_cw($$renderer, $$props) {
	let { $$slots, $$events, ...props } = $$props;
	Icon($$renderer, spread_props([
		{ name: "refresh-cw" },
		props,
		{ iconNode: [
			["path", { "d": "M3 12a9 9 0 0 1 9-9 9.75 9.75 0 0 1 6.74 2.74L21 8" }],
			["path", { "d": "M21 3v5h-5" }],
			["path", { "d": "M21 12a9 9 0 0 1-9 9 9.75 9.75 0 0 1-6.74-2.74L3 16" }],
			["path", { "d": "M8 16H3v5" }]
		] }
	]));
}
//#endregion
//#region src/lib/EditorSidebarHeader.svelte
function EditorSidebarHeader($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { catalogId, localeCount, schemaVersion, root, success, reloadLabel, recentProjects, onreload, onopenworkspace, onnewproject, onopenrecent } = $$props;
		if (Sidebar_header) {
			$$renderer.push("<!--[-->");
			Sidebar_header($$renderer, {
				class: "border-b border-sidebar-border p-2 pr-12 md:pr-2",
				children: ($$renderer) => {
					if (Sidebar_menu) {
						$$renderer.push("<!--[-->");
						Sidebar_menu($$renderer, {
							children: ($$renderer) => {
								if (Sidebar_menu_item) {
									$$renderer.push("<!--[-->");
									Sidebar_menu_item($$renderer, {
										children: ($$renderer) => {
											if (Dropdown_menu) {
												$$renderer.push("<!--[-->");
												Dropdown_menu($$renderer, {
													children: ($$renderer) => {
														{
															function child($$renderer, { props }) {
																if (Sidebar_menu_button) {
																	$$renderer.push("<!--[-->");
																	Sidebar_menu_button($$renderer, spread_props([props, {
																		size: "lg",
																		class: "h-auto min-h-16 py-2",
																		"aria-label": `Project ${catalogId}`,
																		tooltipContent: catalogId,
																		children: ($$renderer) => {
																			Badge($$renderer, {
																				variant: success ? "default" : "destructive",
																				class: "size-10 shrink-0 justify-center rounded-xl p-0",
																				children: ($$renderer) => {
																					if (success) {
																						$$renderer.push("<!--[0-->");
																						Languages($$renderer, { "aria-hidden": "true" });
																					} else {
																						$$renderer.push("<!--[-1-->");
																						Circle_alert($$renderer, { "aria-hidden": "true" });
																					}
																					$$renderer.push(`<!--]-->`);
																				},
																				$$slots: { default: true }
																			});
																			$$renderer.push(`<!----> <span class="grid min-w-0 flex-1 text-left leading-tight"><span class="truncate font-semibold">${escape_html(catalogId)}</span> <span class="truncate text-xs text-muted-foreground">${escape_html(localeCount)} ${escape_html(localeCount === 1 ? "locale" : "locales")} · schema v${escape_html(schemaVersion)}</span></span> `);
																			Chevrons_up_down($$renderer, {
																				class: "ml-auto",
																				"aria-hidden": "true"
																			});
																			$$renderer.push(`<!---->`);
																		},
																		$$slots: { default: true }
																	}]));
																	$$renderer.push("<!--]-->");
																} else {
																	$$renderer.push("<!--[!-->");
																	$$renderer.push("<!--]-->");
																}
															}
															if (Dropdown_menu_trigger) {
																$$renderer.push("<!--[-->");
																Dropdown_menu_trigger($$renderer, {
																	child,
																	$$slots: { child: true }
																});
																$$renderer.push("<!--]-->");
															} else {
																$$renderer.push("<!--[!-->");
																$$renderer.push("<!--]-->");
															}
														}
														$$renderer.push(` `);
														if (Dropdown_menu_content) {
															$$renderer.push("<!--[-->");
															Dropdown_menu_content($$renderer, {
																class: "w-(--bits-dropdown-menu-anchor-width) min-w-72",
																align: "start",
																children: ($$renderer) => {
																	if (Dropdown_menu_label) {
																		$$renderer.push("<!--[-->");
																		Dropdown_menu_label($$renderer, {
																			class: "grid gap-1",
																			children: ($$renderer) => {
																				$$renderer.push(`<span>Current project</span> <span class="truncate font-mono text-xs font-normal text-muted-foreground"${attr("title", root)}>${escape_html(root)}</span>`);
																			},
																			$$slots: { default: true }
																		});
																		$$renderer.push("<!--]-->");
																	} else {
																		$$renderer.push("<!--[!-->");
																		$$renderer.push("<!--]-->");
																	}
																	$$renderer.push(` `);
																	if (recentProjects.length > 0) {
																		$$renderer.push("<!--[0-->");
																		if (Dropdown_menu_separator) {
																			$$renderer.push("<!--[-->");
																			Dropdown_menu_separator($$renderer, {});
																			$$renderer.push("<!--]-->");
																		} else {
																			$$renderer.push("<!--[!-->");
																			$$renderer.push("<!--]-->");
																		}
																		$$renderer.push(` `);
																		if (Dropdown_menu_group) {
																			$$renderer.push("<!--[-->");
																			Dropdown_menu_group($$renderer, {
																				children: ($$renderer) => {
																					if (Dropdown_menu_label) {
																						$$renderer.push("<!--[-->");
																						Dropdown_menu_label($$renderer, {
																							children: ($$renderer) => {
																								$$renderer.push(`<!---->Recent projects`);
																							},
																							$$slots: { default: true }
																						});
																						$$renderer.push("<!--]-->");
																					} else {
																						$$renderer.push("<!--[!-->");
																						$$renderer.push("<!--]-->");
																					}
																					$$renderer.push(` <!--[-->`);
																					const each_array = ensure_array_like(recentProjects.slice(0, 5));
																					for (let $$index = 0, $$length = each_array.length; $$index < $$length; $$index++) {
																						let project = each_array[$$index];
																						if (Dropdown_menu_item) {
																							$$renderer.push("<!--[-->");
																							Dropdown_menu_item($$renderer, {
																								onclick: () => onopenrecent(project),
																								children: ($$renderer) => {
																									Languages($$renderer, {});
																									$$renderer.push(`<!----> <span class="grid min-w-0"><span class="truncate">${escape_html(project.catalogId)}</span> <span class="truncate text-xs text-muted-foreground">${escape_html(project.root)}</span></span>`);
																								},
																								$$slots: { default: true }
																							});
																							$$renderer.push("<!--]-->");
																						} else {
																							$$renderer.push("<!--[!-->");
																							$$renderer.push("<!--]-->");
																						}
																					}
																					$$renderer.push(`<!--]-->`);
																				},
																				$$slots: { default: true }
																			});
																			$$renderer.push("<!--]-->");
																		} else {
																			$$renderer.push("<!--[!-->");
																			$$renderer.push("<!--]-->");
																		}
																	} else $$renderer.push("<!--[-1-->");
																	$$renderer.push(`<!--]--> `);
																	if (Dropdown_menu_separator) {
																		$$renderer.push("<!--[-->");
																		Dropdown_menu_separator($$renderer, {});
																		$$renderer.push("<!--]-->");
																	} else {
																		$$renderer.push("<!--[!-->");
																		$$renderer.push("<!--]-->");
																	}
																	$$renderer.push(` `);
																	if (Dropdown_menu_group) {
																		$$renderer.push("<!--[-->");
																		Dropdown_menu_group($$renderer, {
																			children: ($$renderer) => {
																				if (Dropdown_menu_item) {
																					$$renderer.push("<!--[-->");
																					Dropdown_menu_item($$renderer, {
																						onclick: onreload,
																						children: ($$renderer) => {
																							Refresh_cw($$renderer, {});
																							$$renderer.push(`<!----> ${escape_html(reloadLabel)}`);
																						},
																						$$slots: { default: true }
																					});
																					$$renderer.push("<!--]-->");
																				} else {
																					$$renderer.push("<!--[!-->");
																					$$renderer.push("<!--]-->");
																				}
																				$$renderer.push(` `);
																				if (Dropdown_menu_item) {
																					$$renderer.push("<!--[-->");
																					Dropdown_menu_item($$renderer, {
																						onclick: onopenworkspace,
																						children: ($$renderer) => {
																							Folder_open($$renderer, {});
																							$$renderer.push(`<!----> Open workspace`);
																						},
																						$$slots: { default: true }
																					});
																					$$renderer.push("<!--]-->");
																				} else {
																					$$renderer.push("<!--[!-->");
																					$$renderer.push("<!--]-->");
																				}
																				$$renderer.push(` `);
																				if (Dropdown_menu_item) {
																					$$renderer.push("<!--[-->");
																					Dropdown_menu_item($$renderer, {
																						onclick: onnewproject,
																						children: ($$renderer) => {
																							Plus($$renderer, {});
																							$$renderer.push(`<!----> New project`);
																						},
																						$$slots: { default: true }
																					});
																					$$renderer.push("<!--]-->");
																				} else {
																					$$renderer.push("<!--[!-->");
																					$$renderer.push("<!--]-->");
																				}
																			},
																			$$slots: { default: true }
																		});
																		$$renderer.push("<!--]-->");
																	} else {
																		$$renderer.push("<!--[!-->");
																		$$renderer.push("<!--]-->");
																	}
																},
																$$slots: { default: true }
															});
															$$renderer.push("<!--]-->");
														} else {
															$$renderer.push("<!--[!-->");
															$$renderer.push("<!--]-->");
														}
													},
													$$slots: { default: true }
												});
												$$renderer.push("<!--]-->");
											} else {
												$$renderer.push("<!--[!-->");
												$$renderer.push("<!--]-->");
											}
										},
										$$slots: { default: true }
									});
									$$renderer.push("<!--]-->");
								} else {
									$$renderer.push("<!--[!-->");
									$$renderer.push("<!--]-->");
								}
							},
							$$slots: { default: true }
						});
						$$renderer.push("<!--]-->");
					} else {
						$$renderer.push("<!--[!-->");
						$$renderer.push("<!--]-->");
					}
				},
				$$slots: { default: true }
			});
			$$renderer.push("<!--]-->");
		} else {
			$$renderer.push("<!--[!-->");
			$$renderer.push("<!--]-->");
		}
	});
}
//#endregion
//#region node_modules/@lucide/svelte/dist/icons/save.svelte
function Save($$renderer, $$props) {
	let { $$slots, $$events, ...props } = $$props;
	Icon($$renderer, spread_props([
		{ name: "save" },
		props,
		{ iconNode: [
			["path", { "d": "M15.2 3a2 2 0 0 1 1.4.6l3.8 3.8a2 2 0 0 1 .6 1.4V19a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z" }],
			["path", { "d": "M17 21v-7a1 1 0 0 0-1-1H8a1 1 0 0 0-1 1v7" }],
			["path", { "d": "M7 3v4a1 1 0 0 0 1 1h7" }]
		] }
	]));
}
//#endregion
//#region node_modules/@lucide/svelte/dist/icons/undo-2.svelte
function Undo_2($$renderer, $$props) {
	let { $$slots, $$events, ...props } = $$props;
	Icon($$renderer, spread_props([
		{ name: "undo-2" },
		props,
		{ iconNode: [["path", { "d": "M9 14 4 9l5-5" }], ["path", { "d": "M4 9h10.5a5.5 5.5 0 0 1 5.5 5.5a5.5 5.5 0 0 1-5.5 5.5H11" }]] }
	]));
}
//#endregion
//#region src/lib/components/ui/kbd/kbd.svelte
function Kbd($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, children, $$slots, $$events, ...restProps } = $$props;
		$$renderer.push(`<kbd${attributes({
			"data-slot": "kbd",
			class: clsx$1(cn("h-5.5 w-fit min-w-5.5 gap-1 rounded-lg bg-muted px-1.5 font-sans text-xs font-medium text-muted-foreground in-data-[slot=input-group]:bg-input in-data-[slot=tooltip-content]:bg-background/20 in-data-[slot=tooltip-content]:text-background dark:in-data-[slot=tooltip-content]:bg-background/10 [&_svg:not([class*='size-'])]:size-3 pointer-events-none inline-flex items-center justify-center select-none", className)),
			...restProps
		})}>`);
		children?.($$renderer);
		$$renderer.push(`<!----></kbd>`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region node_modules/@lucide/svelte/dist/icons/loader-circle.svelte
function Loader_circle($$renderer, $$props) {
	let { $$slots, $$events, ...props } = $$props;
	Icon($$renderer, spread_props([
		{ name: "loader-circle" },
		props,
		{ iconNode: [["path", { "d": "M21 12a9 9 0 1 1-6.219-8.56" }]] }
	]));
}
//#endregion
//#region src/lib/components/ui/spinner/spinner.svelte
function Spinner($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { class: className, role = "status", name, color, stroke, "aria-label": ariaLabel = "Loading", $$slots, $$events, ...restProps } = $$props;
		Loader_circle($$renderer, spread_props([{
			role,
			name: name === null ? void 0 : name,
			color: color === null ? void 0 : color,
			stroke: stroke === null ? void 0 : stroke,
			"aria-label": ariaLabel,
			class: cn("size-4 animate-spin", className)
		}, restProps]));
	});
}
//#endregion
//#region src/lib/EditorToolbar.svelte
function EditorToolbar($$renderer, $$props) {
	let { reviewDirty, reviewSaving, reviewDisabled, saveDisabled, saving, saveLabel, savingLabel, saveState, isDirty, ondiscardreview, onsavereview, onsave } = $$props;
	$$renderer.push(`<header class="flex min-h-16 items-center gap-2 border-b bg-background/80 px-3 backdrop-blur-md sm:px-4 xl:gap-4 xl:px-6">`);
	if (Sidebar_trigger) {
		$$renderer.push("<!--[-->");
		Sidebar_trigger($$renderer, {
			class: "shrink-0 md:hidden",
			"aria-label": "Open editor navigation"
		});
		$$renderer.push("<!--]-->");
	} else {
		$$renderer.push("<!--[!-->");
		$$renderer.push("<!--]-->");
	}
	$$renderer.push(` <div class="min-w-0 flex-1"></div> <div class="flex shrink-0 items-center gap-2">`);
	if (reviewDirty) {
		$$renderer.push("<!--[0-->");
		Button($$renderer, {
			variant: "ghost",
			size: "icon-xs",
			class: "hidden sm:inline-flex",
			disabled: reviewSaving,
			onclick: ondiscardreview,
			"aria-label": "Discard workflow changes",
			title: "Discard workflow changes",
			children: ($$renderer) => {
				Undo_2($$renderer, { "data-icon": "inline-start" });
			},
			$$slots: { default: true }
		});
		$$renderer.push(`<!----> `);
		Button($$renderer, {
			variant: "outline",
			size: "xs",
			class: "hidden lg:inline-flex",
			disabled: reviewSaving || reviewDisabled,
			onclick: onsavereview,
			children: ($$renderer) => {
				if (reviewSaving) {
					$$renderer.push("<!--[0-->");
					Spinner($$renderer, { "data-icon": "inline-start" });
				} else $$renderer.push("<!--[-1-->");
				$$renderer.push(`<!--]--> ${escape_html(reviewSaving ? "Saving workflow…" : "Save workflow")}`);
			},
			$$slots: { default: true }
		});
		$$renderer.push(`<!---->`);
	} else $$renderer.push("<!--[-1-->");
	$$renderer.push(`<!--]--> `);
	Button($$renderer, {
		size: "sm",
		variant: isDirty ? "default" : "secondary",
		disabled: saveDisabled,
		onclick: onsave,
		"aria-label": saving ? savingLabel : isDirty ? saveLabel : saveState,
		title: saving ? savingLabel : isDirty ? saveLabel : saveState,
		children: ($$renderer) => {
			if (saving) {
				$$renderer.push("<!--[0-->");
				Spinner($$renderer, { "data-icon": "inline-start" });
			} else if (!isDirty) {
				$$renderer.push("<!--[1-->");
				Check($$renderer, { "data-icon": "inline-start" });
			} else {
				$$renderer.push("<!--[-1-->");
				Save($$renderer, { "data-icon": "inline-start" });
			}
			$$renderer.push(`<!--]--> <span class="hidden sm:inline">${escape_html(saving ? savingLabel : isDirty ? saveLabel : saveState)}</span> `);
			if (isDirty) {
				$$renderer.push("<!--[0-->");
				if (Kbd) {
					$$renderer.push("<!--[-->");
					Kbd($$renderer, {
						class: "hidden xl:inline-flex",
						children: ($$renderer) => {
							$$renderer.push(`<!---->⌘ S`);
						},
						$$slots: { default: true }
					});
					$$renderer.push("<!--]-->");
				} else {
					$$renderer.push("<!--[!-->");
					$$renderer.push("<!--]-->");
				}
			} else $$renderer.push("<!--[-1-->");
			$$renderer.push(`<!--]-->`);
		},
		$$slots: { default: true }
	});
	$$renderer.push(`<!----></div></header>`);
}
//#endregion
//#region node_modules/@lucide/svelte/dist/icons/chevron-down.svelte
function Chevron_down($$renderer, $$props) {
	let { $$slots, $$events, ...props } = $$props;
	Icon($$renderer, spread_props([
		{ name: "chevron-down" },
		props,
		{ iconNode: [["path", { "d": "m6 9 6 6 6-6" }]] }
	]));
}
//#endregion
//#region src/lib/components/ui/collapsible/collapsible-content.svelte
function Collapsible_content($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, $$slots, $$events, ...restProps } = $$props;
		let $$settled = true;
		let $$inner_renderer;
		function $$render_inner($$renderer) {
			if (Collapsible_content$1) {
				$$renderer.push("<!--[-->");
				Collapsible_content$1($$renderer, spread_props([
					{ "data-slot": "collapsible-content" },
					restProps,
					{
						get ref() {
							return ref;
						},
						set ref($$value) {
							ref = $$value;
							$$settled = false;
						}
					}
				]));
				$$renderer.push("<!--]-->");
			} else {
				$$renderer.push("<!--[!-->");
				$$renderer.push("<!--]-->");
			}
		}
		do {
			$$settled = true;
			$$inner_renderer = $$renderer.copy();
			$$render_inner($$inner_renderer);
		} while (!$$settled);
		$$renderer.subsume($$inner_renderer);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/collapsible/collapsible-trigger.svelte
function Collapsible_trigger($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, $$slots, $$events, ...restProps } = $$props;
		let $$settled = true;
		let $$inner_renderer;
		function $$render_inner($$renderer) {
			if (Collapsible_trigger$1) {
				$$renderer.push("<!--[-->");
				Collapsible_trigger$1($$renderer, spread_props([
					{ "data-slot": "collapsible-trigger" },
					restProps,
					{
						get ref() {
							return ref;
						},
						set ref($$value) {
							ref = $$value;
							$$settled = false;
						}
					}
				]));
				$$renderer.push("<!--]-->");
			} else {
				$$renderer.push("<!--[!-->");
				$$renderer.push("<!--]-->");
			}
		}
		do {
			$$settled = true;
			$$inner_renderer = $$renderer.copy();
			$$render_inner($$inner_renderer);
		} while (!$$settled);
		$$renderer.subsume($$inner_renderer);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/collapsible/collapsible.svelte
function Collapsible($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, open = false, $$slots, $$events, ...restProps } = $$props;
		let $$settled = true;
		let $$inner_renderer;
		function $$render_inner($$renderer) {
			if (Collapsible$1) {
				$$renderer.push("<!--[-->");
				Collapsible$1($$renderer, spread_props([
					{ "data-slot": "collapsible" },
					restProps,
					{
						get ref() {
							return ref;
						},
						set ref($$value) {
							ref = $$value;
							$$settled = false;
						},
						get open() {
							return open;
						},
						set open($$value) {
							open = $$value;
							$$settled = false;
						}
					}
				]));
				$$renderer.push("<!--]-->");
			} else {
				$$renderer.push("<!--[!-->");
				$$renderer.push("<!--]-->");
			}
		}
		do {
			$$settled = true;
			$$inner_renderer = $$renderer.copy();
			$$render_inner($$inner_renderer);
		} while (!$$settled);
		$$renderer.subsume($$inner_renderer);
		bind_props($$props, {
			ref,
			open
		});
	});
}
//#endregion
//#region src/lib/components/ui/item/item-actions.svelte
function Item_actions($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, children, $$slots, $$events, ...restProps } = $$props;
		$$renderer.push(`<div${attributes({
			"data-slot": "item-actions",
			class: clsx$1(cn("gap-2 flex items-center", className)),
			...restProps
		})}>`);
		children?.($$renderer);
		$$renderer.push(`<!----></div>`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/item/item-content.svelte
function Item_content($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, children, $$slots, $$events, ...restProps } = $$props;
		$$renderer.push(`<div${attributes({
			"data-slot": "item-content",
			class: clsx$1(cn("gap-1 group-data-[size=xs]/item:gap-0.5 flex flex-1 flex-col [&+[data-slot=item-content]]:flex-none", className)),
			...restProps
		})}>`);
		children?.($$renderer);
		$$renderer.push(`<!----></div>`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/item/item-group.svelte
function Item_group($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, children, $$slots, $$events, ...restProps } = $$props;
		$$renderer.push(`<div${attributes({
			role: "list",
			"data-slot": "item-group",
			class: clsx$1(cn("gap-4 has-data-[size=sm]:gap-2.5 has-data-[size=xs]:gap-2 group/item-group flex w-full flex-col", className)),
			...restProps
		})}>`);
		children?.($$renderer);
		$$renderer.push(`<!----></div>`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/item/item-media.svelte
var itemMediaVariants = tv({
	base: "gap-2 group-has-data-[slot=item-description]/item:translate-y-0.5 group-has-data-[slot=item-description]/item:self-start flex shrink-0 items-center justify-center [&_svg]:pointer-events-none",
	variants: { variant: {
		default: "bg-transparent",
		icon: "[&_svg:not([class*='size-'])]:size-4",
		image: "size-10 overflow-hidden rounded-xl group-data-[size=sm]/item:size-8 group-data-[size=xs]/item:size-6 group-data-[size=xs]/item:rounded-lg [&_img]:size-full [&_img]:object-cover"
	} },
	defaultVariants: { variant: "default" }
});
function Item_media($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, children, variant = "default", $$slots, $$events, ...restProps } = $$props;
		$$renderer.push(`<div${attributes({
			"data-slot": "item-media",
			"data-variant": variant,
			class: clsx$1(cn(itemMediaVariants({ variant }), className)),
			...restProps
		})}>`);
		children?.($$renderer);
		$$renderer.push(`<!----></div>`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/item/item-title.svelte
function Item_title($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, children, $$slots, $$events, ...restProps } = $$props;
		$$renderer.push(`<div${attributes({
			"data-slot": "item-title",
			class: clsx$1(cn("gap-2 text-sm leading-snug font-medium underline-offset-4 line-clamp-1 flex w-fit items-center", className)),
			...restProps
		})}>`);
		children?.($$renderer);
		$$renderer.push(`<!----></div>`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/item/item.svelte
var itemVariants = tv({
	base: "rounded-2xl border text-sm [a]:hover:bg-muted group/item flex w-full flex-wrap items-center transition-colors duration-100 outline-none focus-visible:border-ring focus-visible:ring-[3px] focus-visible:ring-ring/50 [a]:transition-colors",
	variants: {
		variant: {
			default: "border-transparent",
			outline: "border-border",
			muted: "border-transparent bg-muted/50"
		},
		size: {
			default: "gap-3.5 px-4 py-3.5",
			sm: "gap-3.5 px-3.5 py-3",
			xs: "gap-2.5 px-3 py-2.5 in-data-[slot=dropdown-menu-content]:p-0"
		}
	},
	defaultVariants: {
		variant: "default",
		size: "default"
	}
});
function Item($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, child, variant, size, $$slots, $$events, ...restProps } = $$props;
		const mergedProps = derived(() => ({
			class: cn(itemVariants({
				variant,
				size
			}), className),
			"data-slot": "item",
			"data-variant": variant,
			"data-size": size,
			...restProps
		}));
		if (child) {
			$$renderer.push("<!--[0-->");
			child($$renderer, { props: mergedProps() });
			$$renderer.push(`<!---->`);
		} else {
			$$renderer.push("<!--[-1-->");
			$$renderer.push(`<div${attributes({ ...mergedProps() })}>`);
			mergedProps().children?.($$renderer);
			$$renderer.push(`<!----></div>`);
		}
		$$renderer.push(`<!--]-->`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/scroll-area/scroll-area-scrollbar.svelte
function Scroll_area_scrollbar($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, orientation = "vertical", children, $$slots, $$events, ...restProps } = $$props;
		let $$settled = true;
		let $$inner_renderer;
		function $$render_inner($$renderer) {
			if (Scroll_area_scrollbar$1) {
				$$renderer.push("<!--[-->");
				Scroll_area_scrollbar$1($$renderer, spread_props([
					{
						"data-slot": "scroll-area-scrollbar",
						"data-orientation": orientation,
						orientation,
						class: cn("data-horizontal:h-2.5 data-horizontal:flex-col data-horizontal:border-t data-horizontal:border-t-transparent data-vertical:h-full data-vertical:w-2.5 data-vertical:border-l data-vertical:border-l-transparent flex touch-none p-px transition-colors select-none", className)
					},
					restProps,
					{
						get ref() {
							return ref;
						},
						set ref($$value) {
							ref = $$value;
							$$settled = false;
						},
						children: ($$renderer) => {
							children?.($$renderer);
							$$renderer.push(`<!----> `);
							if (Scroll_area_thumb) {
								$$renderer.push("<!--[-->");
								Scroll_area_thumb($$renderer, {
									"data-slot": "scroll-area-thumb",
									class: "rounded-full relative flex-1 bg-border"
								});
								$$renderer.push("<!--]-->");
							} else {
								$$renderer.push("<!--[!-->");
								$$renderer.push("<!--]-->");
							}
						},
						$$slots: { default: true }
					}
				]));
				$$renderer.push("<!--]-->");
			} else {
				$$renderer.push("<!--[!-->");
				$$renderer.push("<!--]-->");
			}
		}
		do {
			$$settled = true;
			$$inner_renderer = $$renderer.copy();
			$$render_inner($$inner_renderer);
		} while (!$$settled);
		$$renderer.subsume($$inner_renderer);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/scroll-area/scroll-area.svelte
function Scroll_area($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, viewportRef = null, class: className, orientation = "vertical", scrollbarXClasses = "", scrollbarYClasses = "", children, $$slots, $$events, ...restProps } = $$props;
		let $$settled = true;
		let $$inner_renderer;
		function $$render_inner($$renderer) {
			if (Scroll_area$1) {
				$$renderer.push("<!--[-->");
				Scroll_area$1($$renderer, spread_props([
					{
						"data-slot": "scroll-area",
						class: cn("relative", className)
					},
					restProps,
					{
						get ref() {
							return ref;
						},
						set ref($$value) {
							ref = $$value;
							$$settled = false;
						},
						children: ($$renderer) => {
							if (Scroll_area_viewport) {
								$$renderer.push("<!--[-->");
								Scroll_area_viewport($$renderer, {
									"data-slot": "scroll-area-viewport",
									class: "cn-scroll-area-viewport size-full rounded-[inherit] transition-[color,box-shadow] outline-none focus-visible:ring-[3px] focus-visible:ring-ring/50 focus-visible:outline-1",
									get ref() {
										return viewportRef;
									},
									set ref($$value) {
										viewportRef = $$value;
										$$settled = false;
									},
									children: ($$renderer) => {
										children?.($$renderer);
										$$renderer.push(`<!---->`);
									},
									$$slots: { default: true }
								});
								$$renderer.push("<!--]-->");
							} else {
								$$renderer.push("<!--[!-->");
								$$renderer.push("<!--]-->");
							}
							$$renderer.push(` `);
							if (orientation === "vertical" || orientation === "both") {
								$$renderer.push("<!--[0-->");
								Scroll_area_scrollbar($$renderer, {
									orientation: "vertical",
									class: scrollbarYClasses
								});
							} else $$renderer.push("<!--[-1-->");
							$$renderer.push(`<!--]--> `);
							if (orientation === "horizontal" || orientation === "both") {
								$$renderer.push("<!--[0-->");
								Scroll_area_scrollbar($$renderer, {
									orientation: "horizontal",
									class: scrollbarXClasses
								});
							} else $$renderer.push("<!--[-1-->");
							$$renderer.push(`<!--]--> `);
							if (Scroll_area_corner) {
								$$renderer.push("<!--[-->");
								Scroll_area_corner($$renderer, {});
								$$renderer.push("<!--]-->");
							} else {
								$$renderer.push("<!--[!-->");
								$$renderer.push("<!--]-->");
							}
						},
						$$slots: { default: true }
					}
				]));
				$$renderer.push("<!--]-->");
			} else {
				$$renderer.push("<!--[!-->");
				$$renderer.push("<!--]-->");
			}
		}
		do {
			$$settled = true;
			$$inner_renderer = $$renderer.copy();
			$$render_inner($$inner_renderer);
		} while (!$$settled);
		$$renderer.subsume($$inner_renderer);
		bind_props($$props, {
			ref,
			viewportRef
		});
	});
}
//#endregion
//#region src/lib/LocaleSwitcher.svelte
function LocaleSwitcher($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { locales, selectedLocale, onselect, onmanage, open = true } = $$props;
		const sidebar = useSidebar();
		function persistOpen(value) {
			localStorage.setItem("runic.sidebar.languages", value ? "open" : "closed");
		}
		function selectLocale(locale) {
			onselect(locale);
			if (sidebar.isMobile) sidebar.setOpenMobile(false);
		}
		let $$settled = true;
		let $$inner_renderer;
		function $$render_inner($$renderer) {
			if (Collapsible) {
				$$renderer.push("<!--[-->");
				Collapsible($$renderer, {
					onOpenChange: persistOpen,
					class: ["group/languages", open && "min-h-0 flex flex-1 flex-col"],
					get open() {
						return open;
					},
					set open($$value) {
						open = $$value;
						$$settled = false;
					},
					children: ($$renderer) => {
						if (Sidebar_group) {
							$$renderer.push("<!--[-->");
							Sidebar_group($$renderer, {
								"aria-label": "Locale coverage",
								class: ["py-1", open && "min-h-0 flex-1"],
								children: ($$renderer) => {
									if (Sidebar_group_label) {
										$$renderer.push("<!--[-->");
										Sidebar_group_label($$renderer, {
											class: "pr-10",
											children: ($$renderer) => {
												if (Collapsible_trigger) {
													$$renderer.push("<!--[-->");
													Collapsible_trigger($$renderer, {
														class: "flex min-w-0 flex-1 items-center gap-2 text-left",
														children: ($$renderer) => {
															$$renderer.push(`<span>Languages</span> `);
															Badge($$renderer, {
																variant: "secondary",
																children: ($$renderer) => {
																	$$renderer.push(`<!---->${escape_html(locales.length)}`);
																},
																$$slots: { default: true }
															});
															$$renderer.push(`<!----> `);
															Chevron_down($$renderer, { class: "ml-auto transition-transform group-data-[state=open]/languages:rotate-180" });
															$$renderer.push(`<!---->`);
														},
														$$slots: { default: true }
													});
													$$renderer.push("<!--]-->");
												} else {
													$$renderer.push("<!--[!-->");
													$$renderer.push("<!--]-->");
												}
											},
											$$slots: { default: true }
										});
										$$renderer.push("<!--]-->");
									} else {
										$$renderer.push("<!--[!-->");
										$$renderer.push("<!--]-->");
									}
									$$renderer.push(` `);
									if (Sidebar_group_action) {
										$$renderer.push("<!--[-->");
										Sidebar_group_action($$renderer, {
											"aria-label": "Manage languages",
											title: "Manage languages",
											onclick: onmanage,
											children: ($$renderer) => {
												Settings_2($$renderer, {});
											},
											$$slots: { default: true }
										});
										$$renderer.push("<!--]-->");
									} else {
										$$renderer.push("<!--[!-->");
										$$renderer.push("<!--]-->");
									}
									$$renderer.push(` `);
									if (Collapsible_content) {
										$$renderer.push("<!--[-->");
										Collapsible_content($$renderer, {
											class: "min-h-0 flex-1 overflow-hidden",
											children: ($$renderer) => {
												if (Sidebar_group_content) {
													$$renderer.push("<!--[-->");
													Sidebar_group_content($$renderer, {
														class: "min-h-0 flex-1",
														children: ($$renderer) => {
															if (Scroll_area) {
																$$renderer.push("<!--[-->");
																Scroll_area($$renderer, {
																	class: "h-full min-h-0",
																	children: ($$renderer) => {
																		if (Item_group) {
																			$$renderer.push("<!--[-->");
																			Item_group($$renderer, {
																				class: "gap-1 pr-2",
																				children: ($$renderer) => {
																					$$renderer.push(`<!--[-->`);
																					const each_array = ensure_array_like(locales);
																					for (let $$index = 0, $$length = each_array.length; $$index < $$length; $$index++) {
																						let locale = each_array[$$index];
																						{
																							function child($$renderer, { props }) {
																								$$renderer.push(`<button${attributes({
																									type: "button",
																									...props
																								})}>`);
																								if (Item_media) {
																									$$renderer.push("<!--[-->");
																									Item_media($$renderer, {
																										children: ($$renderer) => {
																											Badge($$renderer, {
																												variant: selectedLocale === locale.tag ? "default" : "outline",
																												class: "min-w-8",
																												children: ($$renderer) => {
																													$$renderer.push(`<code>${escape_html(locale.tag)}</code>`);
																												},
																												$$slots: { default: true }
																											});
																										},
																										$$slots: { default: true }
																									});
																									$$renderer.push("<!--]-->");
																								} else {
																									$$renderer.push("<!--[!-->");
																									$$renderer.push("<!--]-->");
																								}
																								$$renderer.push(` `);
																								if (Item_content) {
																									$$renderer.push("<!--[-->");
																									Item_content($$renderer, {
																										class: "min-w-0",
																										children: ($$renderer) => {
																											if (Item_title) {
																												$$renderer.push("<!--[-->");
																												Item_title($$renderer, {
																													class: "min-w-0",
																													children: ($$renderer) => {
																														$$renderer.push(`<span class="truncate">${escape_html(locale.name)}</span> `);
																														Badge($$renderer, {
																															variant: "ghost",
																															"aria-hidden": "true",
																															title: locale.isSource ? "Source language" : `Falls back to ${locale.fallback ?? "no language"}`,
																															children: ($$renderer) => {
																																$$renderer.push(`<!---->${escape_html(locale.isSource ? "source" : `← ${locale.fallback ?? "none"}`)}`);
																															},
																															$$slots: { default: true }
																														});
																														$$renderer.push(`<!---->`);
																													},
																													$$slots: { default: true }
																												});
																												$$renderer.push("<!--]-->");
																											} else {
																												$$renderer.push("<!--[!-->");
																												$$renderer.push("<!--]-->");
																											}
																										},
																										$$slots: { default: true }
																									});
																									$$renderer.push("<!--]-->");
																								} else {
																									$$renderer.push("<!--[!-->");
																									$$renderer.push("<!--]-->");
																								}
																								$$renderer.push(` `);
																								if (Item_actions) {
																									$$renderer.push("<!--[-->");
																									Item_actions($$renderer, {
																										children: ($$renderer) => {
																											Badge($$renderer, {
																												variant: "outline",
																												"aria-label": `${locale.percent}% translated`,
																												children: ($$renderer) => {
																													$$renderer.push(`<!---->${escape_html(locale.translated)}/${escape_html(locale.total)}`);
																												},
																												$$slots: { default: true }
																											});
																										},
																										$$slots: { default: true }
																									});
																									$$renderer.push("<!--]-->");
																								} else {
																									$$renderer.push("<!--[!-->");
																									$$renderer.push("<!--]-->");
																								}
																								$$renderer.push(`</button>`);
																							}
																							if (Item) {
																								$$renderer.push("<!--[-->");
																								Item($$renderer, {
																									variant: selectedLocale === locale.tag ? "muted" : "default",
																									size: "xs",
																									"aria-pressed": selectedLocale === locale.tag,
																									"aria-current": selectedLocale === locale.tag ? "true" : void 0,
																									onclick: () => selectLocale(locale.tag),
																									class: "cursor-pointer",
																									"aria-label": `${locale.tag} ${locale.name}, ${locale.isSource ? "source language" : `falls back to ${locale.fallback ?? "no language"}`}, ${locale.percent}% translated`,
																									child,
																									$$slots: { child: true }
																								});
																								$$renderer.push("<!--]-->");
																							} else {
																								$$renderer.push("<!--[!-->");
																								$$renderer.push("<!--]-->");
																							}
																						}
																					}
																					$$renderer.push(`<!--]-->`);
																				},
																				$$slots: { default: true }
																			});
																			$$renderer.push("<!--]-->");
																		} else {
																			$$renderer.push("<!--[!-->");
																			$$renderer.push("<!--]-->");
																		}
																	},
																	$$slots: { default: true }
																});
																$$renderer.push("<!--]-->");
															} else {
																$$renderer.push("<!--[!-->");
																$$renderer.push("<!--]-->");
															}
														},
														$$slots: { default: true }
													});
													$$renderer.push("<!--]-->");
												} else {
													$$renderer.push("<!--[!-->");
													$$renderer.push("<!--]-->");
												}
											},
											$$slots: { default: true }
										});
										$$renderer.push("<!--]-->");
									} else {
										$$renderer.push("<!--[!-->");
										$$renderer.push("<!--]-->");
									}
								},
								$$slots: { default: true }
							});
							$$renderer.push("<!--]-->");
						} else {
							$$renderer.push("<!--[!-->");
							$$renderer.push("<!--]-->");
						}
					},
					$$slots: { default: true }
				});
				$$renderer.push("<!--]-->");
			} else {
				$$renderer.push("<!--[!-->");
				$$renderer.push("<!--]-->");
			}
		}
		do {
			$$settled = true;
			$$inner_renderer = $$renderer.copy();
			$$render_inner($$inner_renderer);
		} while (!$$settled);
		$$renderer.subsume($$inner_renderer);
		bind_props($$props, { open });
	});
}
//#endregion
//#region node_modules/@lucide/svelte/dist/icons/copy.svelte
function Copy($$renderer, $$props) {
	let { $$slots, $$events, ...props } = $$props;
	Icon($$renderer, spread_props([
		{ name: "copy" },
		props,
		{ iconNode: [["rect", {
			"width": "14",
			"height": "14",
			"x": "8",
			"y": "8",
			"rx": "2",
			"ry": "2"
		}], ["path", { "d": "M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2" }]] }
	]));
}
//#endregion
//#region node_modules/@lucide/svelte/dist/icons/pencil.svelte
function Pencil($$renderer, $$props) {
	let { $$slots, $$events, ...props } = $$props;
	Icon($$renderer, spread_props([
		{ name: "pencil" },
		props,
		{ iconNode: [["path", { "d": "M21.174 6.812a1 1 0 0 0-3.986-3.987L3.842 16.174a2 2 0 0 0-.5.83l-1.321 4.352a.5.5 0 0 0 .623.622l4.353-1.32a2 2 0 0 0 .83-.497z" }], ["path", { "d": "m15 5 4 4" }]] }
	]));
}
//#endregion
//#region node_modules/@lucide/svelte/dist/icons/trash-2.svelte
function Trash_2($$renderer, $$props) {
	let { $$slots, $$events, ...props } = $$props;
	Icon($$renderer, spread_props([
		{ name: "trash-2" },
		props,
		{ iconNode: [
			["path", { "d": "M10 11v6" }],
			["path", { "d": "M14 11v6" }],
			["path", { "d": "M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6" }],
			["path", { "d": "M3 6h18" }],
			["path", { "d": "M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2" }]
		] }
	]));
}
//#endregion
//#region src/lib/MessageHeading.svelte
function MessageHeading($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { messageKey, description, tags, locale, layer, inheritedFrom, onrename, onduplicate, ondelete } = $$props;
		$$renderer.push(`<header class="mx-auto mb-6 flex max-w-[1000px] flex-col items-start justify-between gap-4 xl:flex-row xl:gap-8"><div class="min-w-0"><div class="mb-2 flex flex-wrap items-center gap-2 font-mono text-xs text-muted-foreground"><!--[-->`);
		const each_array = ensure_array_like(messageKey.split("."));
		for (let index = 0, $$length = each_array.length; index < $$length; index++) {
			let segment = each_array[index];
			if (index > 0) {
				$$renderer.push("<!--[0-->");
				$$renderer.push(`<span aria-hidden="true">/</span>`);
			} else $$renderer.push("<!--[-1-->");
			$$renderer.push(`<!--]--><span>${escape_html(segment)}</span>`);
		}
		$$renderer.push(`<!--]--></div> <h2 class="font-serif text-4xl tracking-tight sm:text-5xl">${escape_html(messageKey.split(".").at(-1))}</h2> `);
		if (description) {
			$$renderer.push("<!--[0-->");
			$$renderer.push(`<p class="mt-2 max-w-2xl text-sm text-muted-foreground">${escape_html(description)}</p>`);
		} else $$renderer.push("<!--[-1-->");
		$$renderer.push(`<!--]--> `);
		if (tags.length > 0) {
			$$renderer.push("<!--[0-->");
			$$renderer.push(`<div class="mt-3 flex flex-wrap gap-1.5"><!--[-->`);
			const each_array_1 = ensure_array_like(tags);
			for (let $$index_1 = 0, $$length = each_array_1.length; $$index_1 < $$length; $$index_1++) {
				let tag = each_array_1[$$index_1];
				Badge($$renderer, {
					variant: "outline",
					children: ($$renderer) => {
						$$renderer.push(`<!---->${escape_html(tag)}`);
					},
					$$slots: { default: true }
				});
			}
			$$renderer.push(`<!--]--></div>`);
		} else $$renderer.push("<!--[-1-->");
		$$renderer.push(`<!--]--></div> <div class="flex shrink-0 flex-col items-start gap-2 xl:items-end"><div class="flex flex-wrap gap-1.5 xl:justify-end">`);
		Badge($$renderer, {
			variant: "outline",
			children: ($$renderer) => {
				$$renderer.push(`<!---->${escape_html(locale)}`);
			},
			$$slots: { default: true }
		});
		$$renderer.push(`<!----> `);
		Badge($$renderer, {
			variant: "outline",
			children: ($$renderer) => {
				$$renderer.push(`<!---->${escape_html(layer)}`);
			},
			$$slots: { default: true }
		});
		$$renderer.push(`<!----> `);
		if (inheritedFrom) {
			$$renderer.push("<!--[0-->");
			Badge($$renderer, {
				variant: "secondary",
				children: ($$renderer) => {
					$$renderer.push(`<!---->falls back to ${escape_html(inheritedFrom)}`);
				},
				$$slots: { default: true }
			});
		} else $$renderer.push("<!--[-1-->");
		$$renderer.push(`<!--]--></div> <div class="flex gap-1.5">`);
		Button($$renderer, {
			variant: "outline",
			size: "xs",
			"aria-label": "Rename",
			title: "Rename or move this message",
			onclick: onrename,
			children: ($$renderer) => {
				Pencil($$renderer, { "data-icon": "inline-start" });
				$$renderer.push(`<!----> <span class="hidden min-[360px]:inline">Rename</span>`);
			},
			$$slots: { default: true }
		});
		$$renderer.push(`<!----> `);
		Button($$renderer, {
			variant: "outline",
			size: "xs",
			"aria-label": "Duplicate",
			title: "Duplicate this message",
			onclick: onduplicate,
			children: ($$renderer) => {
				Copy($$renderer, { "data-icon": "inline-start" });
				$$renderer.push(`<!----> <span class="hidden min-[360px]:inline">Duplicate</span>`);
			},
			$$slots: { default: true }
		});
		$$renderer.push(`<!----> `);
		Button($$renderer, {
			variant: "destructive",
			size: "xs",
			"aria-label": "Delete",
			title: "Delete this message",
			onclick: ondelete,
			children: ($$renderer) => {
				Trash_2($$renderer, { "data-icon": "inline-start" });
				$$renderer.push(`<!----> <span class="hidden min-[360px]:inline">Delete</span>`);
			},
			$$slots: { default: true }
		});
		$$renderer.push(`<!----></div></div></header>`);
	});
}
//#endregion
//#region node_modules/@lucide/svelte/dist/icons/check-check.svelte
function Check_check($$renderer, $$props) {
	let { $$slots, $$events, ...props } = $$props;
	Icon($$renderer, spread_props([
		{ name: "check-check" },
		props,
		{ iconNode: [["path", { "d": "M18 6 7 17l-5-5" }], ["path", { "d": "m22 10-7.5 7.5L13 16" }]] }
	]));
}
//#endregion
//#region node_modules/@lucide/svelte/dist/icons/list-checks.svelte
function List_checks($$renderer, $$props) {
	let { $$slots, $$events, ...props } = $$props;
	Icon($$renderer, spread_props([
		{ name: "list-checks" },
		props,
		{ iconNode: [
			["path", { "d": "M13 5h8" }],
			["path", { "d": "M13 12h8" }],
			["path", { "d": "M13 19h8" }],
			["path", { "d": "m3 17 2 2 4-4" }],
			["path", { "d": "m3 7 2 2 4-4" }]
		] }
	]));
}
//#endregion
//#region node_modules/@lucide/svelte/dist/icons/message-square-off.svelte
function Message_square_off($$renderer, $$props) {
	let { $$slots, $$events, ...props } = $$props;
	Icon($$renderer, spread_props([
		{ name: "message-square-off" },
		props,
		{ iconNode: [
			["path", { "d": "M19 19H6.828a2 2 0 0 0-1.414.586l-2.202 2.202A.7.7 0 0 1 2 21.286V5a2 2 0 0 1 1.184-1.826" }],
			["path", { "d": "m2 2 20 20" }],
			["path", { "d": "M8.656 3H20a2 2 0 0 1 2 2v11.344" }]
		] }
	]));
}
//#endregion
//#region node_modules/@lucide/svelte/dist/icons/ellipsis.svelte
function Ellipsis($$renderer, $$props) {
	let { $$slots, $$events, ...props } = $$props;
	Icon($$renderer, spread_props([
		{ name: "ellipsis" },
		props,
		{ iconNode: [
			["circle", {
				"cx": "12",
				"cy": "12",
				"r": "1"
			}],
			["circle", {
				"cx": "19",
				"cy": "12",
				"r": "1"
			}],
			["circle", {
				"cx": "5",
				"cy": "12",
				"r": "1"
			}]
		] }
	]));
}
//#endregion
//#region src/lib/components/ui/empty/empty-description.svelte
function Empty_description($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, children, $$slots, $$events, ...restProps } = $$props;
		$$renderer.push(`<div${attributes({
			"data-slot": "empty-description",
			class: clsx$1(cn("text-sm/relaxed text-sm/relaxed text-muted-foreground [&>a]:underline [&>a]:underline-offset-4 [&>a:hover]:text-primary", className)),
			...restProps
		})}>`);
		children?.($$renderer);
		$$renderer.push(`<!----></div>`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/empty/empty-header.svelte
function Empty_header($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, children, $$slots, $$events, ...restProps } = $$props;
		$$renderer.push(`<div${attributes({
			"data-slot": "empty-header",
			class: clsx$1(cn("gap-2 flex max-w-sm flex-col items-center", className)),
			...restProps
		})}>`);
		children?.($$renderer);
		$$renderer.push(`<!----></div>`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/empty/empty-media.svelte
var emptyMediaVariants = tv({
	base: "mb-2 flex shrink-0 items-center justify-center [&_svg]:pointer-events-none [&_svg]:shrink-0",
	variants: { variant: {
		default: "bg-transparent",
		icon: "flex size-10 shrink-0 items-center justify-center rounded-xl bg-muted text-foreground [&_svg:not([class*='size-'])]:size-5"
	} },
	defaultVariants: { variant: "default" }
});
function Empty_media($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, children, variant = "default", $$slots, $$events, ...restProps } = $$props;
		$$renderer.push(`<div${attributes({
			"data-slot": "empty-icon",
			"data-variant": variant,
			class: clsx$1(cn(emptyMediaVariants({ variant }), className)),
			...restProps
		})}>`);
		children?.($$renderer);
		$$renderer.push(`<!----></div>`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/empty/empty-title.svelte
function Empty_title($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, children, $$slots, $$events, ...restProps } = $$props;
		$$renderer.push(`<div${attributes({
			"data-slot": "empty-title",
			class: clsx$1(cn("text-lg font-medium tracking-tight", className)),
			...restProps
		})}>`);
		children?.($$renderer);
		$$renderer.push(`<!----></div>`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/empty/empty.svelte
function Empty($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, children, $$slots, $$events, ...restProps } = $$props;
		$$renderer.push(`<div${attributes({
			"data-slot": "empty",
			class: clsx$1(cn("gap-4 rounded-2xl border-dashed p-12 flex w-full min-w-0 flex-1 flex-col items-center justify-center text-center text-balance", className)),
			...restProps
		})}>`);
		children?.($$renderer);
		$$renderer.push(`<!----></div>`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/MessageTreeNode.svelte
function MessageTreeNode($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { node, selectedKey, onselect } = $$props;
		let open = derived(() => nodeContains(node, selectedKey));
		let count = derived(() => messageCount(node));
		function nodeContains(candidate, key) {
			return candidate.item?.key === key || candidate.children.some((child) => nodeContains(child, key));
		}
		function messageCount(candidate) {
			return (candidate.item === void 0 ? 0 : 1) + candidate.children.reduce((total, child) => total + messageCount(child), 0);
		}
		function status(item) {
			if (item.stale) return "stale";
			if (item.needsReview) return "review";
			if (item.structured) return "structured";
		}
		function messageLeaf($$renderer, item, label) {
			if (Sidebar_menu_item) {
				$$renderer.push("<!--[-->");
				Sidebar_menu_item($$renderer, {
					children: ($$renderer) => {
						if (Sidebar_menu_button) {
							$$renderer.push("<!--[-->");
							Sidebar_menu_button($$renderer, {
								size: "sm",
								isActive: selectedKey === item.key,
								class: cn("cursor-pointer", status(item) && "pr-16"),
								"aria-current": selectedKey === item.key ? "page" : void 0,
								"aria-label": `${item.key}: ${item.preview}`,
								title: `${item.key}\n${item.preview}`,
								onclick: () => onselect(item.key),
								children: ($$renderer) => {
									Badge($$renderer, {
										variant: item.missing ? "outline" : item.structured ? "default" : "secondary",
										class: "size-2 shrink-0 p-0",
										"aria-label": item.missing ? "Missing translation" : item.structured ? "Structured message" : "Translated"
									});
									$$renderer.push(`<!----> <span class="truncate font-mono">${escape_html(label)}</span>`);
								},
								$$slots: { default: true }
							});
							$$renderer.push("<!--]-->");
						} else {
							$$renderer.push("<!--[!-->");
							$$renderer.push("<!--]-->");
						}
						$$renderer.push(` `);
						if (status(item)) {
							$$renderer.push("<!--[0-->");
							if (Sidebar_menu_badge) {
								$$renderer.push("<!--[-->");
								Sidebar_menu_badge($$renderer, {
									class: "w-auto p-0",
									children: ($$renderer) => {
										Badge($$renderer, {
											variant: item.stale ? "destructive" : "outline",
											children: ($$renderer) => {
												$$renderer.push(`<!---->${escape_html(status(item))}`);
											},
											$$slots: { default: true }
										});
									},
									$$slots: { default: true }
								});
								$$renderer.push("<!--]-->");
							} else {
								$$renderer.push("<!--[!-->");
								$$renderer.push("<!--]-->");
							}
						} else $$renderer.push("<!--[-1-->");
						$$renderer.push(`<!--]-->`);
					},
					$$slots: { default: true }
				});
				$$renderer.push("<!--]-->");
			} else {
				$$renderer.push("<!--[!-->");
				$$renderer.push("<!--]-->");
			}
		}
		let $$settled = true;
		let $$inner_renderer;
		function $$render_inner($$renderer) {
			if (node.children.length > 0) {
				$$renderer.push("<!--[0-->");
				if (Sidebar_menu_item) {
					$$renderer.push("<!--[-->");
					Sidebar_menu_item($$renderer, {
						children: ($$renderer) => {
							if (Collapsible) {
								$$renderer.push("<!--[-->");
								Collapsible($$renderer, {
									class: "group/message-branch",
									get open() {
										return open();
									},
									set open($$value) {
										open($$value);
										$$settled = false;
									},
									children: ($$renderer) => {
										{
											function child($$renderer, { props }) {
												if (Sidebar_menu_button) {
													$$renderer.push("<!--[-->");
													Sidebar_menu_button($$renderer, spread_props([props, {
														size: "sm",
														"aria-label": `${node.segment}, ${count()} ${count() === 1 ? "message" : "messages"}`,
														children: ($$renderer) => {
															Chevron_right($$renderer, { class: "transition-transform group-data-[state=open]/message-branch:rotate-90" });
															$$renderer.push(`<!----> <span class="truncate font-medium">${escape_html(node.segment)}</span>`);
														},
														$$slots: { default: true }
													}]));
													$$renderer.push("<!--]-->");
												} else {
													$$renderer.push("<!--[!-->");
													$$renderer.push("<!--]-->");
												}
											}
											if (Collapsible_trigger) {
												$$renderer.push("<!--[-->");
												Collapsible_trigger($$renderer, {
													child,
													$$slots: { child: true }
												});
												$$renderer.push("<!--]-->");
											} else {
												$$renderer.push("<!--[!-->");
												$$renderer.push("<!--]-->");
											}
										}
										$$renderer.push(` `);
										if (Sidebar_menu_badge) {
											$$renderer.push("<!--[-->");
											Sidebar_menu_badge($$renderer, {
												children: ($$renderer) => {
													$$renderer.push(`<!---->${escape_html(count())}`);
												},
												$$slots: { default: true }
											});
											$$renderer.push("<!--]-->");
										} else {
											$$renderer.push("<!--[!-->");
											$$renderer.push("<!--]-->");
										}
										$$renderer.push(` `);
										if (Collapsible_content) {
											$$renderer.push("<!--[-->");
											Collapsible_content($$renderer, {
												children: ($$renderer) => {
													if (Sidebar_menu_sub) {
														$$renderer.push("<!--[-->");
														Sidebar_menu_sub($$renderer, {
															children: ($$renderer) => {
																if (node.item) {
																	$$renderer.push("<!--[0-->");
																	messageLeaf($$renderer, node.item, "Overview");
																} else $$renderer.push("<!--[-1-->");
																$$renderer.push(`<!--]--> <!--[-->`);
																const each_array = ensure_array_like(node.children);
																for (let $$index = 0, $$length = each_array.length; $$index < $$length; $$index++) {
																	let child = each_array[$$index];
																	MessageTreeNode($$renderer, {
																		node: child,
																		selectedKey,
																		onselect
																	});
																}
																$$renderer.push(`<!--]-->`);
															},
															$$slots: { default: true }
														});
														$$renderer.push("<!--]-->");
													} else {
														$$renderer.push("<!--[!-->");
														$$renderer.push("<!--]-->");
													}
												},
												$$slots: { default: true }
											});
											$$renderer.push("<!--]-->");
										} else {
											$$renderer.push("<!--[!-->");
											$$renderer.push("<!--]-->");
										}
									},
									$$slots: { default: true }
								});
								$$renderer.push("<!--]-->");
							} else {
								$$renderer.push("<!--[!-->");
								$$renderer.push("<!--]-->");
							}
						},
						$$slots: { default: true }
					});
					$$renderer.push("<!--]-->");
				} else {
					$$renderer.push("<!--[!-->");
					$$renderer.push("<!--]-->");
				}
			} else if (node.item) {
				$$renderer.push("<!--[1-->");
				messageLeaf($$renderer, node.item, node.segment);
			} else $$renderer.push("<!--[-1-->");
			$$renderer.push(`<!--]-->`);
		}
		do {
			$$settled = true;
			$$inner_renderer = $$renderer.copy();
			$$render_inner($$inner_renderer);
		} while (!$$settled);
		$$renderer.subsume($$inner_renderer);
	});
}
//#endregion
//#region src/lib/MessageList.svelte
function buildTree(items) {
	const roots = [];
	for (const item of items) {
		const segments = item.key.split(".").filter(Boolean);
		const safeSegments = segments.length === 0 ? [item.key] : segments;
		let siblings = roots;
		let path = "";
		for (const segment of safeSegments) {
			path = path === "" ? segment : `${path}.${segment}`;
			let node = siblings.find((candidate) => candidate.segment === segment);
			if (node === void 0) {
				node = {
					segment,
					path,
					children: []
				};
				siblings.push(node);
			}
			siblings = node.children;
			if (path === item.key) node.item = item;
		}
	}
	return roots;
}
function MessageList($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { items, selectedKey, visibleCount, remainingCount, noResultsLabel, toolbar, onselect, onadd, onmarkreview, onapprove, onloadmore, open = true } = $$props;
		const sidebar = useSidebar();
		let tree = derived(() => buildTree(items));
		function persistOpen(value) {
			localStorage.setItem("runic.sidebar.messages", value ? "open" : "closed");
		}
		function selectMessage(key) {
			onselect(key);
			if (sidebar.isMobile) sidebar.setOpenMobile(false);
		}
		function addMessage() {
			onadd();
			if (sidebar.isMobile) sidebar.setOpenMobile(false);
		}
		let $$settled = true;
		let $$inner_renderer;
		function $$render_inner($$renderer) {
			if (Collapsible) {
				$$renderer.push("<!--[-->");
				Collapsible($$renderer, {
					onOpenChange: persistOpen,
					class: cn("group/messages", open && "min-h-0 flex flex-1 flex-col"),
					get open() {
						return open;
					},
					set open($$value) {
						open = $$value;
						$$settled = false;
					},
					children: ($$renderer) => {
						if (Sidebar_group) {
							$$renderer.push("<!--[-->");
							Sidebar_group($$renderer, {
								class: cn("py-1", open && "min-h-0 flex-1"),
								"aria-label": "Messages",
								children: ($$renderer) => {
									if (Sidebar_group_label) {
										$$renderer.push("<!--[-->");
										Sidebar_group_label($$renderer, {
											class: "justify-between",
											children: ($$renderer) => {
												if (Collapsible_trigger) {
													$$renderer.push("<!--[-->");
													Collapsible_trigger($$renderer, {
														class: "flex min-w-0 flex-1 items-center gap-2 text-left",
														children: ($$renderer) => {
															$$renderer.push(`<span>Messages</span> `);
															Badge($$renderer, {
																variant: "secondary",
																children: ($$renderer) => {
																	$$renderer.push(`<!---->${escape_html(visibleCount)}`);
																},
																$$slots: { default: true }
															});
															$$renderer.push(`<!----> `);
															Chevron_down($$renderer, { class: "ml-auto transition-transform group-data-[state=open]/messages:rotate-180" });
															$$renderer.push(`<!---->`);
														},
														$$slots: { default: true }
													});
													$$renderer.push("<!--]-->");
												} else {
													$$renderer.push("<!--[!-->");
													$$renderer.push("<!--]-->");
												}
												$$renderer.push(` <div class="flex items-center gap-1">`);
												if (Dropdown_menu) {
													$$renderer.push("<!--[-->");
													Dropdown_menu($$renderer, {
														children: ($$renderer) => {
															if (Dropdown_menu_trigger) {
																$$renderer.push("<!--[-->");
																Dropdown_menu_trigger($$renderer, {
																	class: buttonVariants({
																		variant: "ghost",
																		size: "icon-xs"
																	}),
																	"aria-label": "Message bulk actions",
																	title: "Message bulk actions",
																	children: ($$renderer) => {
																		Ellipsis($$renderer, {});
																	},
																	$$slots: { default: true }
																});
																$$renderer.push("<!--]-->");
															} else {
																$$renderer.push("<!--[!-->");
																$$renderer.push("<!--]-->");
															}
															$$renderer.push(` `);
															if (Dropdown_menu_content) {
																$$renderer.push("<!--[-->");
																Dropdown_menu_content($$renderer, {
																	align: "end",
																	class: "w-64",
																	children: ($$renderer) => {
																		if (Dropdown_menu_label) {
																			$$renderer.push("<!--[-->");
																			Dropdown_menu_label($$renderer, {
																				children: ($$renderer) => {
																					$$renderer.push(`<!---->Visible messages`);
																				},
																				$$slots: { default: true }
																			});
																			$$renderer.push("<!--]-->");
																		} else {
																			$$renderer.push("<!--[!-->");
																			$$renderer.push("<!--]-->");
																		}
																		$$renderer.push(` `);
																		if (Dropdown_menu_group) {
																			$$renderer.push("<!--[-->");
																			Dropdown_menu_group($$renderer, {
																				children: ($$renderer) => {
																					if (Dropdown_menu_item) {
																						$$renderer.push("<!--[-->");
																						Dropdown_menu_item($$renderer, {
																							disabled: visibleCount === 0,
																							onclick: onmarkreview,
																							children: ($$renderer) => {
																								List_checks($$renderer, {});
																								$$renderer.push(`<!----> Mark for review`);
																							},
																							$$slots: { default: true }
																						});
																						$$renderer.push("<!--]-->");
																					} else {
																						$$renderer.push("<!--[!-->");
																						$$renderer.push("<!--]-->");
																					}
																					$$renderer.push(` `);
																					if (Dropdown_menu_item) {
																						$$renderer.push("<!--[-->");
																						Dropdown_menu_item($$renderer, {
																							disabled: visibleCount === 0,
																							onclick: onapprove,
																							children: ($$renderer) => {
																								Check_check($$renderer, {});
																								$$renderer.push(`<!----> Approve translations`);
																							},
																							$$slots: { default: true }
																						});
																						$$renderer.push("<!--]-->");
																					} else {
																						$$renderer.push("<!--[!-->");
																						$$renderer.push("<!--]-->");
																					}
																				},
																				$$slots: { default: true }
																			});
																			$$renderer.push("<!--]-->");
																		} else {
																			$$renderer.push("<!--[!-->");
																			$$renderer.push("<!--]-->");
																		}
																	},
																	$$slots: { default: true }
																});
																$$renderer.push("<!--]-->");
															} else {
																$$renderer.push("<!--[!-->");
																$$renderer.push("<!--]-->");
															}
														},
														$$slots: { default: true }
													});
													$$renderer.push("<!--]-->");
												} else {
													$$renderer.push("<!--[!-->");
													$$renderer.push("<!--]-->");
												}
												$$renderer.push(` `);
												Button($$renderer, {
													variant: "ghost",
													size: "icon-xs",
													"aria-label": "Add message",
													title: "Add message",
													onclick: addMessage,
													children: ($$renderer) => {
														Plus($$renderer, {});
													},
													$$slots: { default: true }
												});
												$$renderer.push(`<!----></div>`);
											},
											$$slots: { default: true }
										});
										$$renderer.push("<!--]-->");
									} else {
										$$renderer.push("<!--[!-->");
										$$renderer.push("<!--]-->");
									}
									$$renderer.push(` `);
									if (Collapsible_content) {
										$$renderer.push("<!--[-->");
										Collapsible_content($$renderer, {
											class: "min-h-0 flex-1 overflow-hidden",
											children: ($$renderer) => {
												toolbar($$renderer);
												$$renderer.push(`<!----> `);
												if (Sidebar_group_content) {
													$$renderer.push("<!--[-->");
													Sidebar_group_content($$renderer, {
														class: "min-h-0 flex-1",
														children: ($$renderer) => {
															$$renderer.push(`<nav class="min-h-0 flex-1 overflow-y-auto pb-3" aria-label="Translation messages">`);
															if (items.length === 0) {
																$$renderer.push("<!--[0-->");
																if (Empty) {
																	$$renderer.push("<!--[-->");
																	Empty($$renderer, {
																		class: "p-6",
																		children: ($$renderer) => {
																			if (Empty_header) {
																				$$renderer.push("<!--[-->");
																				Empty_header($$renderer, {
																					children: ($$renderer) => {
																						if (Empty_media) {
																							$$renderer.push("<!--[-->");
																							Empty_media($$renderer, {
																								variant: "icon",
																								children: ($$renderer) => {
																									Message_square_off($$renderer, {});
																								},
																								$$slots: { default: true }
																							});
																							$$renderer.push("<!--]-->");
																						} else {
																							$$renderer.push("<!--[!-->");
																							$$renderer.push("<!--]-->");
																						}
																						$$renderer.push(` `);
																						if (Empty_title) {
																							$$renderer.push("<!--[-->");
																							Empty_title($$renderer, {
																								children: ($$renderer) => {
																									$$renderer.push(`<!---->No matching messages`);
																								},
																								$$slots: { default: true }
																							});
																							$$renderer.push("<!--]-->");
																						} else {
																							$$renderer.push("<!--[!-->");
																							$$renderer.push("<!--]-->");
																						}
																						$$renderer.push(` `);
																						if (Empty_description) {
																							$$renderer.push("<!--[-->");
																							Empty_description($$renderer, {
																								children: ($$renderer) => {
																									$$renderer.push(`<!---->${escape_html(noResultsLabel)}`);
																								},
																								$$slots: { default: true }
																							});
																							$$renderer.push("<!--]-->");
																						} else {
																							$$renderer.push("<!--[!-->");
																							$$renderer.push("<!--]-->");
																						}
																					},
																					$$slots: { default: true }
																				});
																				$$renderer.push("<!--]-->");
																			} else {
																				$$renderer.push("<!--[!-->");
																				$$renderer.push("<!--]-->");
																			}
																		},
																		$$slots: { default: true }
																	});
																	$$renderer.push("<!--]-->");
																} else {
																	$$renderer.push("<!--[!-->");
																	$$renderer.push("<!--]-->");
																}
															} else {
																$$renderer.push("<!--[-1-->");
																if (Sidebar_menu) {
																	$$renderer.push("<!--[-->");
																	Sidebar_menu($$renderer, {
																		"aria-label": "Message namespaces",
																		class: "px-2",
																		children: ($$renderer) => {
																			$$renderer.push(`<!--[-->`);
																			const each_array = ensure_array_like(tree());
																			for (let $$index = 0, $$length = each_array.length; $$index < $$length; $$index++) {
																				let node = each_array[$$index];
																				MessageTreeNode($$renderer, {
																					node,
																					selectedKey,
																					onselect: selectMessage
																				});
																			}
																			$$renderer.push(`<!--]-->`);
																		},
																		$$slots: { default: true }
																	});
																	$$renderer.push("<!--]-->");
																} else {
																	$$renderer.push("<!--[!-->");
																	$$renderer.push("<!--]-->");
																}
															}
															$$renderer.push(`<!--]--> `);
															if (remainingCount > 0) {
																$$renderer.push("<!--[0-->");
																Button($$renderer, {
																	variant: "outline",
																	size: "xs",
																	class: "mx-2 mt-2 w-[calc(100%_-_1rem)]",
																	onclick: onloadmore,
																	children: ($$renderer) => {
																		$$renderer.push(`<!---->Show 300 more · ${escape_html(remainingCount)} remaining`);
																	},
																	$$slots: { default: true }
																});
															} else $$renderer.push("<!--[-1-->");
															$$renderer.push(`<!--]--></nav>`);
														},
														$$slots: { default: true }
													});
													$$renderer.push("<!--]-->");
												} else {
													$$renderer.push("<!--[!-->");
													$$renderer.push("<!--]-->");
												}
											},
											$$slots: { default: true }
										});
										$$renderer.push("<!--]-->");
									} else {
										$$renderer.push("<!--[!-->");
										$$renderer.push("<!--]-->");
									}
								},
								$$slots: { default: true }
							});
							$$renderer.push("<!--]-->");
						} else {
							$$renderer.push("<!--[!-->");
							$$renderer.push("<!--]-->");
						}
					},
					$$slots: { default: true }
				});
				$$renderer.push("<!--]-->");
			} else {
				$$renderer.push("<!--[!-->");
				$$renderer.push("<!--]-->");
			}
		}
		do {
			$$settled = true;
			$$inner_renderer = $$renderer.copy();
			$$render_inner($$inner_renderer);
		} while (!$$settled);
		$$renderer.subsume($$inner_renderer);
		bind_props($$props, { open });
	});
}
//#endregion
//#region node_modules/@lucide/svelte/dist/icons/list-filter.svelte
function List_filter($$renderer, $$props) {
	let { $$slots, $$events, ...props } = $$props;
	Icon($$renderer, spread_props([
		{ name: "list-filter" },
		props,
		{ iconNode: [
			["path", { "d": "M2 5h20" }],
			["path", { "d": "M6 12h12" }],
			["path", { "d": "M9 19h6" }]
		] }
	]));
}
//#endregion
//#region node_modules/@lucide/svelte/dist/icons/search.svelte
function Search($$renderer, $$props) {
	let { $$slots, $$events, ...props } = $$props;
	Icon($$renderer, spread_props([
		{ name: "search" },
		props,
		{ iconNode: [["path", { "d": "m21 21-4.34-4.34" }], ["circle", {
			"cx": "11",
			"cy": "11",
			"r": "8"
		}]] }
	]));
}
//#endregion
//#region src/lib/components/ui/input-group/input-group-addon.svelte
var inputGroupAddonVariants = tv({
	base: "h-auto gap-2 py-2 text-sm font-medium text-muted-foreground group-data-[disabled=true]/input-group:opacity-50 **:data-[slot=kbd]:rounded-3xl **:data-[slot=kbd]:bg-muted-foreground/10 **:data-[slot=kbd]:px-1.5 [&>svg:not([class*='size-'])]:size-4 flex cursor-text items-center justify-center select-none",
	variants: { align: {
		"inline-start": "pl-3 has-[>button]:-ml-1 has-[>kbd]:-ml-1 order-first",
		"inline-end": "pr-3 has-[>button]:-mr-1 has-[>kbd]:-mr-1 order-last",
		"block-start": "px-3 pt-3 group-has-[>input]/input-group:pt-3.5 [.border-b]:pb-3.5 order-first w-full justify-start",
		"block-end": "px-3 pb-3 group-has-[>input]/input-group:pb-3.5 [.border-t]:pt-3.5 order-last w-full justify-start"
	} },
	defaultVariants: { align: "inline-start" }
});
function Input_group_addon($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, children, align = "inline-start", $$slots, $$events, ...restProps } = $$props;
		$$renderer.push(`<div${attributes({
			role: "group",
			"data-slot": "input-group-addon",
			"data-align": align,
			class: clsx$1(cn(inputGroupAddonVariants({ align }), className)),
			...restProps
		})}>`);
		children?.($$renderer);
		$$renderer.push(`<!----></div>`);
		bind_props($$props, { ref });
	});
}
tv({
	base: "gap-2 rounded-4xl text-sm flex items-center shadow-none",
	variants: { size: {
		xs: "h-6 gap-1 rounded-xl px-1.5 [&>svg:not([class*='size-'])]:size-3.5",
		sm: "cn-input-group-button-size-sm",
		"icon-xs": "size-6 rounded-xl p-0 has-[>svg]:p-0",
		"icon-sm": "size-8 p-0 has-[>svg]:p-0"
	} },
	defaultVariants: { size: "xs" }
});
//#endregion
//#region src/lib/components/ui/input-group/input-group-input.svelte
function Input_group_input($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, value = void 0, class: className, $$slots, $$events, ...props } = $$props;
		let $$settled = true;
		let $$inner_renderer;
		function $$render_inner($$renderer) {
			Input($$renderer, spread_props([
				{
					"data-slot": "input-group-control",
					class: cn("rounded-none border-0 bg-transparent shadow-none ring-0 focus-visible:ring-0 aria-invalid:ring-0 dark:bg-transparent flex-1", className)
				},
				props,
				{
					get ref() {
						return ref;
					},
					set ref($$value) {
						ref = $$value;
						$$settled = false;
					},
					get value() {
						return value;
					},
					set value($$value) {
						value = $$value;
						$$settled = false;
					}
				}
			]));
		}
		do {
			$$settled = true;
			$$inner_renderer = $$renderer.copy();
			$$render_inner($$inner_renderer);
		} while (!$$settled);
		$$renderer.subsume($$inner_renderer);
		bind_props($$props, {
			ref,
			value
		});
	});
}
//#endregion
//#region src/lib/components/ui/textarea/textarea.svelte
function Textarea($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, value = void 0, class: className, "data-slot": dataSlot = "textarea", $$slots, $$events, ...restProps } = $$props;
		$$renderer.push(`<textarea${attributes({
			"data-slot": dataSlot,
			class: clsx$1(cn("resize-none rounded-2xl border border-transparent bg-input/50 px-3 py-3 text-base transition-[color,box-shadow,background-color] focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/30 aria-invalid:border-destructive aria-invalid:ring-3 aria-invalid:ring-destructive/20 md:text-sm dark:aria-invalid:border-destructive/50 dark:aria-invalid:ring-destructive/40 flex field-sizing-content min-h-16 w-full outline-none placeholder:text-muted-foreground disabled:cursor-not-allowed disabled:opacity-50", className)),
			...restProps
		})}>`);
		const $$body = escape_html(value);
		if ($$body) $$renderer.push(`${$$body}`);
		$$renderer.push(`</textarea>`);
		bind_props($$props, {
			ref,
			value
		});
	});
}
//#endregion
//#region src/lib/components/ui/input-group/input-group.svelte
function Input_group($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, children, $$slots, $$events, ...props } = $$props;
		$$renderer.push(`<div${attributes({
			"data-slot": "input-group",
			role: "group",
			class: clsx$1(cn("group/input-group h-9 rounded-4xl border border-transparent bg-input/50 transition-[color,box-shadow,background-color] in-data-[slot=combobox-content]:focus-within:border-inherit in-data-[slot=combobox-content]:focus-within:ring-0 has-data-[align=block-end]:rounded-3xl has-data-[align=block-start]:rounded-3xl has-[[data-slot=input-group-control]:focus-visible]:border-ring has-[[data-slot=input-group-control]:focus-visible]:ring-3 has-[[data-slot=input-group-control]:focus-visible]:ring-ring/30 has-[[data-slot][aria-invalid=true]]:border-destructive has-[[data-slot][aria-invalid=true]]:ring-3 has-[[data-slot][aria-invalid=true]]:ring-destructive/20 has-[textarea]:rounded-2xl has-[>[data-align=block-end]]:h-auto has-[>[data-align=block-end]]:flex-col has-[>[data-align=block-start]]:h-auto has-[>[data-align=block-start]]:flex-col dark:has-[[data-slot][aria-invalid=true]]:ring-destructive/40 has-[>[data-align=block-end]]:[&>input]:pt-3 has-[>[data-align=block-start]]:[&>input]:pb-3 has-[>[data-align=inline-end]]:[&>input]:pr-1.5 has-[>[data-align=inline-start]]:[&>input]:pl-1.5 relative flex w-full min-w-0 items-center outline-none has-[>textarea]:h-auto", className)),
			...props
		})}>`);
		children?.($$renderer);
		$$renderer.push(`<!----></div>`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/MessageToolbar.svelte
function MessageToolbar($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { query = void 0, filter = void 0, inputRef = null, placeholder, options, filterLabel } = $$props;
		let selectedOption = derived(() => options.find((option) => option.value === filter) ?? options[0]);
		let $$settled = true;
		let $$inner_renderer;
		function $$render_inner($$renderer) {
			$$renderer.push(`<div class="px-2 pb-2"><label class="sr-only" for="message-search">Search messages</label> `);
			if (Input_group) {
				$$renderer.push("<!--[-->");
				Input_group($$renderer, {
					children: ($$renderer) => {
						if (Input_group_input) {
							$$renderer.push("<!--[-->");
							Input_group_input($$renderer, {
								id: "message-search",
								type: "search",
								placeholder,
								get ref() {
									return inputRef;
								},
								set ref($$value) {
									inputRef = $$value;
									$$settled = false;
								},
								get value() {
									return query;
								},
								set value($$value) {
									query = $$value;
									$$settled = false;
								}
							});
							$$renderer.push("<!--]-->");
						} else {
							$$renderer.push("<!--[!-->");
							$$renderer.push("<!--]-->");
						}
						$$renderer.push(` `);
						if (Input_group_addon) {
							$$renderer.push("<!--[-->");
							Input_group_addon($$renderer, {
								children: ($$renderer) => {
									Search($$renderer, {});
								},
								$$slots: { default: true }
							});
							$$renderer.push("<!--]-->");
						} else {
							$$renderer.push("<!--[!-->");
							$$renderer.push("<!--]-->");
						}
						$$renderer.push(` `);
						if (Input_group_addon) {
							$$renderer.push("<!--[-->");
							Input_group_addon($$renderer, {
								align: "inline-end",
								class: "gap-1",
								children: ($$renderer) => {
									if (filter !== "all") {
										$$renderer.push("<!--[0-->");
										Badge($$renderer, {
											variant: "secondary",
											class: "max-w-24 truncate",
											children: ($$renderer) => {
												$$renderer.push(`<!---->${escape_html(selectedOption()?.label)}`);
											},
											$$slots: { default: true }
										});
									} else $$renderer.push("<!--[-1-->");
									$$renderer.push(`<!--]--> `);
									if (Dropdown_menu) {
										$$renderer.push("<!--[-->");
										Dropdown_menu($$renderer, {
											children: ($$renderer) => {
												if (Dropdown_menu_trigger) {
													$$renderer.push("<!--[-->");
													Dropdown_menu_trigger($$renderer, {
														class: buttonVariants({
															variant: filter === "all" ? "ghost" : "secondary",
															size: "icon-xs"
														}),
														"aria-label": `${filterLabel}: ${selectedOption()?.label ?? filter}`,
														title: `${filterLabel}: ${selectedOption()?.label ?? filter}`,
														children: ($$renderer) => {
															List_filter($$renderer, {});
														},
														$$slots: { default: true }
													});
													$$renderer.push("<!--]-->");
												} else {
													$$renderer.push("<!--[!-->");
													$$renderer.push("<!--]-->");
												}
												$$renderer.push(` `);
												if (Dropdown_menu_content) {
													$$renderer.push("<!--[-->");
													Dropdown_menu_content($$renderer, {
														align: "end",
														class: "w-56",
														children: ($$renderer) => {
															if (Dropdown_menu_label) {
																$$renderer.push("<!--[-->");
																Dropdown_menu_label($$renderer, {
																	children: ($$renderer) => {
																		$$renderer.push(`<!---->${escape_html(filterLabel)}`);
																	},
																	$$slots: { default: true }
																});
																$$renderer.push("<!--]-->");
															} else {
																$$renderer.push("<!--[!-->");
																$$renderer.push("<!--]-->");
															}
															$$renderer.push(` `);
															if (Dropdown_menu_group) {
																$$renderer.push("<!--[-->");
																Dropdown_menu_group($$renderer, {
																	children: ($$renderer) => {
																		if (Dropdown_menu_radio_group) {
																			$$renderer.push("<!--[-->");
																			Dropdown_menu_radio_group($$renderer, {
																				value: filter,
																				onValueChange: (value) => filter = value,
																				children: ($$renderer) => {
																					$$renderer.push(`<!--[-->`);
																					const each_array = ensure_array_like(options);
																					for (let $$index = 0, $$length = each_array.length; $$index < $$length; $$index++) {
																						let option = each_array[$$index];
																						if (Dropdown_menu_radio_item) {
																							$$renderer.push("<!--[-->");
																							Dropdown_menu_radio_item($$renderer, {
																								value: option.value,
																								children: ($$renderer) => {
																									$$renderer.push(`<span>${escape_html(option.label)}</span> `);
																									Badge($$renderer, {
																										variant: "secondary",
																										class: "ml-auto mr-5",
																										children: ($$renderer) => {
																											$$renderer.push(`<!---->${escape_html(option.count)}`);
																										},
																										$$slots: { default: true }
																									});
																									$$renderer.push(`<!---->`);
																								},
																								$$slots: { default: true }
																							});
																							$$renderer.push("<!--]-->");
																						} else {
																							$$renderer.push("<!--[!-->");
																							$$renderer.push("<!--]-->");
																						}
																					}
																					$$renderer.push(`<!--]-->`);
																				},
																				$$slots: { default: true }
																			});
																			$$renderer.push("<!--]-->");
																		} else {
																			$$renderer.push("<!--[!-->");
																			$$renderer.push("<!--]-->");
																		}
																	},
																	$$slots: { default: true }
																});
																$$renderer.push("<!--]-->");
															} else {
																$$renderer.push("<!--[!-->");
																$$renderer.push("<!--]-->");
															}
														},
														$$slots: { default: true }
													});
													$$renderer.push("<!--]-->");
												} else {
													$$renderer.push("<!--[!-->");
													$$renderer.push("<!--]-->");
												}
											},
											$$slots: { default: true }
										});
										$$renderer.push("<!--]-->");
									} else {
										$$renderer.push("<!--[!-->");
										$$renderer.push("<!--]-->");
									}
								},
								$$slots: { default: true }
							});
							$$renderer.push("<!--]-->");
						} else {
							$$renderer.push("<!--[!-->");
							$$renderer.push("<!--]-->");
						}
					},
					$$slots: { default: true }
				});
				$$renderer.push("<!--]-->");
			} else {
				$$renderer.push("<!--[!-->");
				$$renderer.push("<!--]-->");
			}
			$$renderer.push(`</div>`);
		}
		do {
			$$settled = true;
			$$inner_renderer = $$renderer.copy();
			$$render_inner($$inner_renderer);
		} while (!$$settled);
		$$renderer.subsume($$inner_renderer);
		bind_props($$props, {
			query,
			filter,
			inputRef
		});
	});
}
//#endregion
//#region node_modules/@lucide/svelte/dist/icons/book-open.svelte
function Book_open($$renderer, $$props) {
	let { $$slots, $$events, ...props } = $$props;
	Icon($$renderer, spread_props([
		{ name: "book-open" },
		props,
		{ iconNode: [["path", { "d": "M12 5v16" }], ["path", { "d": "M20.001 19A2 2 0 0022 17V5a2 2 0 00-1.999-2L16 3.002A5 5 0 0012 5a5 5 0 00-4-2H4a2 2 0 00-2 2v12a2 2 0 001.999 2H8a5 5 0 014 2 5 5 0 014-2z" }]] }
	]));
}
//#endregion
//#region node_modules/@lucide/svelte/dist/icons/circle-check.svelte
function Circle_check($$renderer, $$props) {
	let { $$slots, $$events, ...props } = $$props;
	Icon($$renderer, spread_props([
		{ name: "circle-check" },
		props,
		{ iconNode: [["circle", {
			"cx": "12",
			"cy": "12",
			"r": "10"
		}], ["path", { "d": "m9 12 2 2 4-4" }]] }
	]));
}
//#endregion
//#region node_modules/@lucide/svelte/dist/icons/clipboard-list.svelte
function Clipboard_list($$renderer, $$props) {
	let { $$slots, $$events, ...props } = $$props;
	Icon($$renderer, spread_props([
		{ name: "clipboard-list" },
		props,
		{ iconNode: [
			["rect", {
				"width": "8",
				"height": "4",
				"x": "8",
				"y": "2",
				"rx": "1",
				"ry": "1"
			}],
			["path", { "d": "M16 4h2a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2h2" }],
			["path", { "d": "M12 11h4" }],
			["path", { "d": "M12 16h4" }],
			["path", { "d": "M8 11h.01" }],
			["path", { "d": "M8 16h.01" }]
		] }
	]));
}
//#endregion
//#region node_modules/@lucide/svelte/dist/icons/sparkles.svelte
function Sparkles($$renderer, $$props) {
	let { $$slots, $$events, ...props } = $$props;
	Icon($$renderer, spread_props([
		{ name: "sparkles" },
		props,
		{ iconNode: [
			["path", { "d": "M11.017 2.814a1 1 0 0 1 1.966 0l1.051 5.558a2 2 0 0 0 1.594 1.594l5.558 1.051a1 1 0 0 1 0 1.966l-5.558 1.051a2 2 0 0 0-1.594 1.594l-1.051 5.558a1 1 0 0 1-1.966 0l-1.051-5.558a2 2 0 0 0-1.594-1.594l-5.558-1.051a1 1 0 0 1 0-1.966l5.558-1.051a2 2 0 0 0 1.594-1.594z" }],
			["path", { "d": "M20 2v4" }],
			["path", { "d": "M22 4h-4" }],
			["circle", {
				"cx": "4",
				"cy": "20",
				"r": "2"
			}]
		] }
	]));
}
//#endregion
//#region node_modules/@lucide/svelte/dist/icons/triangle-alert.svelte
function Triangle_alert($$renderer, $$props) {
	let { $$slots, $$events, ...props } = $$props;
	Icon($$renderer, spread_props([
		{ name: "triangle-alert" },
		props,
		{ iconNode: [
			["path", { "d": "m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3" }],
			["path", { "d": "M12 9v4" }],
			["path", { "d": "M12 17h.01" }]
		] }
	]));
}
//#endregion
//#region src/lib/components/ui/card/card-action.svelte
function Card_action($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, children, $$slots, $$events, ...restProps } = $$props;
		$$renderer.push(`<div${attributes({
			"data-slot": "card-action",
			class: clsx$1(cn("cn-card-action col-start-2 row-span-2 row-start-1 self-start justify-self-end", className)),
			...restProps
		})}>`);
		children?.($$renderer);
		$$renderer.push(`<!----></div>`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/card/card-content.svelte
function Card_content($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, children, $$slots, $$events, ...restProps } = $$props;
		$$renderer.push(`<div${attributes({
			"data-slot": "card-content",
			class: clsx$1(cn("px-(--card-spacing)", className)),
			...restProps
		})}>`);
		children?.($$renderer);
		$$renderer.push(`<!----></div>`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/card/card-description.svelte
function Card_description($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, children, $$slots, $$events, ...restProps } = $$props;
		$$renderer.push(`<p${attributes({
			"data-slot": "card-description",
			class: clsx$1(cn("text-sm text-muted-foreground", className)),
			...restProps
		})}>`);
		children?.($$renderer);
		$$renderer.push(`<!----></p>`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/card/card-footer.svelte
function Card_footer($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, children, $$slots, $$events, ...restProps } = $$props;
		$$renderer.push(`<div${attributes({
			"data-slot": "card-footer",
			class: clsx$1(cn("rounded-b-4xl px-(--card-spacing) [.border-t]:pt-(--card-spacing) flex items-center", className)),
			...restProps
		})}>`);
		children?.($$renderer);
		$$renderer.push(`<!----></div>`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/card/card-header.svelte
function Card_header($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, children, $$slots, $$events, ...restProps } = $$props;
		$$renderer.push(`<div${attributes({
			"data-slot": "card-header",
			class: clsx$1(cn("gap-1.5 rounded-t-4xl px-(--card-spacing) [.border-b]:pb-(--card-spacing) group/card-header @container/card-header grid auto-rows-min items-start has-data-[slot=card-action]:grid-cols-[1fr_auto] has-data-[slot=card-description]:grid-rows-[auto_auto]", className)),
			...restProps
		})}>`);
		children?.($$renderer);
		$$renderer.push(`<!----></div>`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/card/card-title.svelte
function Card_title($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, children, $$slots, $$events, ...restProps } = $$props;
		$$renderer.push(`<div${attributes({
			"data-slot": "card-title",
			class: clsx$1(cn("text-base font-medium", className)),
			...restProps
		})}>`);
		children?.($$renderer);
		$$renderer.push(`<!----></div>`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/card/card.svelte
function Card($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, children, size = "default", $$slots, $$events, ...restProps } = $$props;
		$$renderer.push(`<div${attributes({
			"data-slot": "card",
			"data-size": size,
			class: clsx$1(cn("gap-(--card-spacing) overflow-hidden rounded-4xl bg-card py-(--card-spacing) text-sm text-card-foreground shadow-md ring-1 ring-foreground/5 [--card-spacing:--spacing(6)] has-[>img:first-child]:pt-0 data-[size=sm]:[--card-spacing:--spacing(4)] dark:ring-foreground/10 *:[img:first-child]:rounded-t-4xl *:[img:last-child]:rounded-b-4xl group/card flex flex-col", className)),
			...restProps
		})}>`);
		children?.($$renderer);
		$$renderer.push(`<!----></div>`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/field/field-content.svelte
function Field_content($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, children, $$slots, $$events, ...restProps } = $$props;
		$$renderer.push(`<div${attributes({
			"data-slot": "field-content",
			class: clsx$1(cn("gap-1 group/field-content flex flex-1 flex-col leading-snug", className)),
			...restProps
		})}>`);
		children?.($$renderer);
		$$renderer.push(`<!----></div>`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/field/field-description.svelte
function Field_description($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, children, $$slots, $$events, ...restProps } = $$props;
		$$renderer.push(`<p${attributes({
			"data-slot": "field-description",
			class: clsx$1(cn("text-left text-sm text-muted-foreground [[data-variant=legend]+&]:-mt-1.5 leading-normal font-normal group-has-[[data-orientation=horizontal]]/field:text-balance", "last:mt-0 nth-last-2:-mt-1", "[&>a]:underline [&>a]:underline-offset-4 [&>a:hover]:text-primary", className)),
			...restProps
		})}>`);
		children?.($$renderer);
		$$renderer.push(`<!----></p>`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/field/field-error.svelte
function Field_error($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, children, errors, $$slots, $$events, ...restProps } = $$props;
		const hasContent = derived(() => {
			if (children) return true;
			if (!errors || errors.length === 0) return false;
			if (errors.length === 1 && !errors[0]?.message) return false;
			return true;
		});
		const isMultipleErrors = derived(() => errors && errors.length > 1);
		const singleErrorMessage = derived(() => errors && errors.length === 1 && errors[0]?.message);
		if (hasContent()) {
			$$renderer.push("<!--[0-->");
			$$renderer.push(`<div${attributes({
				role: "alert",
				"data-slot": "field-error",
				class: clsx$1(cn("text-sm text-destructive font-normal", className)),
				...restProps
			})}>`);
			if (children) {
				$$renderer.push("<!--[0-->");
				children($$renderer);
				$$renderer.push(`<!---->`);
			} else if (singleErrorMessage()) {
				$$renderer.push("<!--[1-->");
				$$renderer.push(`${escape_html(singleErrorMessage())}`);
			} else if (isMultipleErrors()) {
				$$renderer.push("<!--[2-->");
				$$renderer.push(`<ul class="ml-4 flex list-disc flex-col gap-1"><!--[-->`);
				const each_array = ensure_array_like(errors ?? []);
				for (let index = 0, $$length = each_array.length; index < $$length; index++) {
					let error = each_array[index];
					if (error?.message) {
						$$renderer.push("<!--[0-->");
						$$renderer.push(`<li>${escape_html(error.message)}</li>`);
					} else $$renderer.push("<!--[-1-->");
					$$renderer.push(`<!--]-->`);
				}
				$$renderer.push(`<!--]--></ul>`);
			} else $$renderer.push("<!--[-1-->");
			$$renderer.push(`<!--]--></div>`);
		} else $$renderer.push("<!--[-1-->");
		$$renderer.push(`<!--]-->`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/field/field-group.svelte
function Field_group($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, children, $$slots, $$events, ...restProps } = $$props;
		$$renderer.push(`<div${attributes({
			"data-slot": "field-group",
			class: clsx$1(cn("gap-7 data-[slot=checkbox-group]:gap-3 *:data-[slot=field-group]:gap-4 group/field-group @container/field-group flex w-full flex-col", className)),
			...restProps
		})}>`);
		children?.($$renderer);
		$$renderer.push(`<!----></div>`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/label/label.svelte
function Label($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, $$slots, $$events, ...restProps } = $$props;
		let $$settled = true;
		let $$inner_renderer;
		function $$render_inner($$renderer) {
			if (Label$1) {
				$$renderer.push("<!--[-->");
				Label$1($$renderer, spread_props([
					{
						"data-slot": "label",
						class: cn("gap-2 text-sm leading-none font-medium group-data-[disabled=true]:opacity-50 peer-disabled:opacity-50 flex items-center select-none group-data-[disabled=true]:pointer-events-none peer-disabled:cursor-not-allowed", className)
					},
					restProps,
					{
						get ref() {
							return ref;
						},
						set ref($$value) {
							ref = $$value;
							$$settled = false;
						}
					}
				]));
				$$renderer.push("<!--]-->");
			} else {
				$$renderer.push("<!--[!-->");
				$$renderer.push("<!--]-->");
			}
		}
		do {
			$$settled = true;
			$$inner_renderer = $$renderer.copy();
			$$render_inner($$inner_renderer);
		} while (!$$settled);
		$$renderer.subsume($$inner_renderer);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/field/field-label.svelte
function Field_label($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, children, $$slots, $$events, ...restProps } = $$props;
		let $$settled = true;
		let $$inner_renderer;
		function $$render_inner($$renderer) {
			Label($$renderer, spread_props([
				{
					"data-slot": "field-label",
					class: cn("gap-2 leading-snug group-data-[disabled=true]/field:opacity-50 has-data-checked:bg-input/30 has-[>[data-slot=field]]:rounded-2xl has-[>[data-slot=field]]:border *:data-[slot=field]:p-4 group/field-label peer/field-label flex w-fit leading-snug", "has-[>[data-slot=field]]:w-full has-[>[data-slot=field]]:flex-col", className)
				},
				restProps,
				{
					get ref() {
						return ref;
					},
					set ref($$value) {
						ref = $$value;
						$$settled = false;
					},
					children: ($$renderer) => {
						children?.($$renderer);
						$$renderer.push(`<!---->`);
					},
					$$slots: { default: true }
				}
			]));
		}
		do {
			$$settled = true;
			$$inner_renderer = $$renderer.copy();
			$$render_inner($$inner_renderer);
		} while (!$$settled);
		$$renderer.subsume($$inner_renderer);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/field/field-legend.svelte
function Field_legend($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, variant = "legend", children, $$slots, $$events, ...restProps } = $$props;
		$$renderer.push(`<legend${attributes({
			"data-slot": "field-legend",
			"data-variant": variant,
			class: clsx$1(cn("mb-3 font-medium data-[variant=label]:text-sm data-[variant=legend]:text-base", className)),
			...restProps
		})}>`);
		children?.($$renderer);
		$$renderer.push(`<!----></legend>`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/field/field-set.svelte
function Field_set($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, children, $$slots, $$events, ...restProps } = $$props;
		$$renderer.push(`<fieldset${attributes({
			"data-slot": "field-set",
			class: clsx$1(cn("gap-6 has-[>[data-slot=checkbox-group]]:gap-3 has-[>[data-slot=radio-group]]:gap-3 flex flex-col", className)),
			...restProps
		})}>`);
		children?.($$renderer);
		$$renderer.push(`<!----></fieldset>`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/field/field.svelte
var fieldVariants = tv({
	base: "gap-3 data-[invalid=true]:text-destructive group/field flex w-full",
	variants: { orientation: {
		vertical: "cn-field-orientation-vertical flex-col [&>*]:w-full [&>.sr-only]:w-auto",
		horizontal: "cn-field-orientation-horizontal flex-row items-center has-[>[data-slot=field-content]]:items-start [&>[data-slot=field-label]]:flex-auto has-[>[data-slot=field-content]]:[&>[role=checkbox],[role=radio]]:mt-px",
		responsive: "cn-field-orientation-responsive flex-col @md/field-group:flex-row @md/field-group:items-center @md/field-group:has-[>[data-slot=field-content]]:items-start [&>*]:w-full @md/field-group:[&>*]:w-auto [&>.sr-only]:w-auto @md/field-group:[&>[data-slot=field-label]]:flex-auto @md/field-group:has-[>[data-slot=field-content]]:[&>[role=checkbox],[role=radio]]:mt-px"
	} },
	defaultVariants: { orientation: "vertical" }
});
function Field($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, orientation = "vertical", children, $$slots, $$events, ...restProps } = $$props;
		$$renderer.push(`<div${attributes({
			role: "group",
			"data-slot": "field",
			"data-orientation": orientation,
			class: clsx$1(cn(fieldVariants({ orientation }), className)),
			...restProps
		})}>`);
		children?.($$renderer);
		$$renderer.push(`<!----></div>`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/select/select-portal.svelte
function Select_portal($$renderer, $$props) {
	let { $$slots, $$events, ...restProps } = $$props;
	if (Portal) {
		$$renderer.push("<!--[-->");
		Portal($$renderer, spread_props([restProps]));
		$$renderer.push("<!--]-->");
	} else {
		$$renderer.push("<!--[!-->");
		$$renderer.push("<!--]-->");
	}
}
//#endregion
//#region src/lib/components/ui/select/select-scroll-down-button.svelte
function Select_scroll_down_button($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, $$slots, $$events, ...restProps } = $$props;
		let $$settled = true;
		let $$inner_renderer;
		function $$render_inner($$renderer) {
			if (Select_scroll_down_button$1) {
				$$renderer.push("<!--[-->");
				Select_scroll_down_button$1($$renderer, spread_props([
					{
						"data-slot": "select-scroll-down-button",
						class: cn("z-10 flex cursor-default items-center justify-center bg-popover py-1 [&_svg:not([class*='size-'])]:size-4 bottom-0 w-full", className)
					},
					restProps,
					{
						get ref() {
							return ref;
						},
						set ref($$value) {
							ref = $$value;
							$$settled = false;
						},
						children: ($$renderer) => {
							Chevron_down($$renderer, {});
						},
						$$slots: { default: true }
					}
				]));
				$$renderer.push("<!--]-->");
			} else {
				$$renderer.push("<!--[!-->");
				$$renderer.push("<!--]-->");
			}
		}
		do {
			$$settled = true;
			$$inner_renderer = $$renderer.copy();
			$$render_inner($$inner_renderer);
		} while (!$$settled);
		$$renderer.subsume($$inner_renderer);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region node_modules/@lucide/svelte/dist/icons/chevron-up.svelte
function Chevron_up($$renderer, $$props) {
	let { $$slots, $$events, ...props } = $$props;
	Icon($$renderer, spread_props([
		{ name: "chevron-up" },
		props,
		{ iconNode: [["path", { "d": "m18 15-6-6-6 6" }]] }
	]));
}
//#endregion
//#region src/lib/components/ui/select/select-scroll-up-button.svelte
function Select_scroll_up_button($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, $$slots, $$events, ...restProps } = $$props;
		let $$settled = true;
		let $$inner_renderer;
		function $$render_inner($$renderer) {
			if (Select_scroll_up_button$1) {
				$$renderer.push("<!--[-->");
				Select_scroll_up_button$1($$renderer, spread_props([
					{
						"data-slot": "select-scroll-up-button",
						class: cn("z-10 flex cursor-default items-center justify-center bg-popover py-1 [&_svg:not([class*='size-'])]:size-4 top-0 w-full", className)
					},
					restProps,
					{
						get ref() {
							return ref;
						},
						set ref($$value) {
							ref = $$value;
							$$settled = false;
						},
						children: ($$renderer) => {
							Chevron_up($$renderer, {});
						},
						$$slots: { default: true }
					}
				]));
				$$renderer.push("<!--]-->");
			} else {
				$$renderer.push("<!--[!-->");
				$$renderer.push("<!--]-->");
			}
		}
		do {
			$$settled = true;
			$$inner_renderer = $$renderer.copy();
			$$render_inner($$inner_renderer);
		} while (!$$settled);
		$$renderer.subsume($$inner_renderer);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/select/select-content.svelte
function Select_content($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, sideOffset = 4, portalProps, children, preventScroll = true, $$slots, $$events, ...restProps } = $$props;
		let $$settled = true;
		let $$inner_renderer;
		function $$render_inner($$renderer) {
			Select_portal($$renderer, spread_props([portalProps, {
				children: ($$renderer) => {
					if (Select_content$1) {
						$$renderer.push("<!--[-->");
						Select_content$1($$renderer, spread_props([
							{
								sideOffset,
								preventScroll,
								"data-slot": "select-content",
								class: cn("min-w-36 rounded-3xl bg-popover text-popover-foreground shadow-lg ring-1 ring-foreground/5 duration-100 data-[side=bottom]:slide-in-from-top-2 data-[side=left]:slide-in-from-right-2 data-[side=right]:slide-in-from-left-2 data-[side=top]:slide-in-from-bottom-2 dark:ring-foreground/10 data-open:animate-in data-open:fade-in-0 data-open:zoom-in-95 data-closed:animate-out data-closed:fade-out-0 data-closed:zoom-out-95 data-[side=inline-end]:slide-in-from-left-2 data-[side=inline-start]:slide-in-from-right-2 relative isolate z-50 overflow-x-hidden overflow-y-auto", className)
							},
							restProps,
							{
								get ref() {
									return ref;
								},
								set ref($$value) {
									ref = $$value;
									$$settled = false;
								},
								children: ($$renderer) => {
									Select_scroll_up_button($$renderer, {});
									$$renderer.push(`<!----> `);
									if (Select_viewport) {
										$$renderer.push("<!--[-->");
										Select_viewport($$renderer, {
											class: cn("h-(--bits-select-anchor-height) w-full min-w-(--bits-select-anchor-width) scroll-my-1"),
											children: ($$renderer) => {
												children?.($$renderer);
												$$renderer.push(`<!---->`);
											},
											$$slots: { default: true }
										});
										$$renderer.push("<!--]-->");
									} else {
										$$renderer.push("<!--[!-->");
										$$renderer.push("<!--]-->");
									}
									$$renderer.push(` `);
									Select_scroll_down_button($$renderer, {});
									$$renderer.push(`<!---->`);
								},
								$$slots: { default: true }
							}
						]));
						$$renderer.push("<!--]-->");
					} else {
						$$renderer.push("<!--[!-->");
						$$renderer.push("<!--]-->");
					}
				},
				$$slots: { default: true }
			}]));
		}
		do {
			$$settled = true;
			$$inner_renderer = $$renderer.copy();
			$$render_inner($$inner_renderer);
		} while (!$$settled);
		$$renderer.subsume($$inner_renderer);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/select/select-group.svelte
function Select_group($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, $$slots, $$events, ...restProps } = $$props;
		let $$settled = true;
		let $$inner_renderer;
		function $$render_inner($$renderer) {
			if (Select_group$1) {
				$$renderer.push("<!--[-->");
				Select_group$1($$renderer, spread_props([
					{
						"data-slot": "select-group",
						class: cn("scroll-my-1.5 p-1.5", className)
					},
					restProps,
					{
						get ref() {
							return ref;
						},
						set ref($$value) {
							ref = $$value;
							$$settled = false;
						}
					}
				]));
				$$renderer.push("<!--]-->");
			} else {
				$$renderer.push("<!--[!-->");
				$$renderer.push("<!--]-->");
			}
		}
		do {
			$$settled = true;
			$$inner_renderer = $$renderer.copy();
			$$render_inner($$inner_renderer);
		} while (!$$settled);
		$$renderer.subsume($$inner_renderer);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/select/select-item.svelte
function Select_item($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, value, label, children: childrenProp, $$slots, $$events, ...restProps } = $$props;
		let $$settled = true;
		let $$inner_renderer;
		function $$render_inner($$renderer) {
			{
				function children($$renderer, { selected, highlighted }) {
					$$renderer.push(`<span class="absolute end-2 flex size-3.5 items-center justify-center">`);
					if (selected) {
						$$renderer.push("<!--[0-->");
						Check($$renderer, { class: "cn-select-item-indicator-icon" });
					} else $$renderer.push("<!--[-1-->");
					$$renderer.push(`<!--]--></span> <span class="flex flex-1 gap-2 shrink-0 whitespace-nowrap">`);
					if (childrenProp) {
						$$renderer.push("<!--[0-->");
						childrenProp($$renderer, {
							selected,
							highlighted
						});
						$$renderer.push(`<!---->`);
					} else {
						$$renderer.push("<!--[-1-->");
						$$renderer.push(`${escape_html(label || value)}`);
					}
					$$renderer.push(`<!--]--></span>`);
				}
				if (Select_item$1) {
					$$renderer.push("<!--[-->");
					Select_item$1($$renderer, spread_props([
						{
							value,
							"data-slot": "select-item",
							class: cn("gap-2.5 rounded-2xl py-2 pr-8 pl-3 text-sm font-medium focus:bg-accent focus:text-accent-foreground not-data-[variant=destructive]:focus:**:text-accent-foreground [&_svg:not([class*='size-'])]:size-4 *:[span]:last:flex *:[span]:last:items-center *:[span]:last:gap-2 relative flex w-full cursor-default items-center outline-hidden select-none focus:bg-accent focus:text-accent-foreground data-highlighted:bg-accent data-highlighted:text-accent-foreground data-[disabled]:pointer-events-none data-[disabled]:opacity-50 [&_svg]:pointer-events-none [&_svg]:shrink-0", className)
						},
						restProps,
						{
							get ref() {
								return ref;
							},
							set ref($$value) {
								ref = $$value;
								$$settled = false;
							},
							children,
							$$slots: { default: true }
						}
					]));
					$$renderer.push("<!--]-->");
				} else {
					$$renderer.push("<!--[!-->");
					$$renderer.push("<!--]-->");
				}
			}
		}
		do {
			$$settled = true;
			$$inner_renderer = $$renderer.copy();
			$$render_inner($$inner_renderer);
		} while (!$$settled);
		$$renderer.subsume($$inner_renderer);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/select/select-label.svelte
function Select_label($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, children, $$slots, $$events, ...restProps } = $$props;
		$$renderer.push(`<div${attributes({
			"data-slot": "select-label",
			class: clsx$1(cn("px-3 py-2.5 text-xs text-muted-foreground", className)),
			...restProps
		})}>`);
		children?.($$renderer);
		$$renderer.push(`<!----></div>`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/select/select-trigger.svelte
function Select_trigger($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, children, size = "default", $$slots, $$events, ...restProps } = $$props;
		let $$settled = true;
		let $$inner_renderer;
		function $$render_inner($$renderer) {
			if (Select_trigger$1) {
				$$renderer.push("<!--[-->");
				Select_trigger$1($$renderer, spread_props([
					{
						"data-slot": "select-trigger",
						"data-size": size,
						class: cn("gap-1.5 rounded-3xl border border-transparent bg-input/50 px-3 py-2 text-sm transition-[color,box-shadow,background-color] focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/30 aria-invalid:border-destructive aria-invalid:ring-3 aria-invalid:ring-destructive/20 data-placeholder:text-muted-foreground data-[size=default]:h-9 data-[size=sm]:h-8 *:data-[slot=select-value]:flex *:data-[slot=select-value]:gap-1.5 dark:aria-invalid:border-destructive/50 dark:aria-invalid:ring-destructive/40 [&_svg:not([class*='size-'])]:size-4 flex w-fit items-center justify-between whitespace-nowrap outline-none disabled:cursor-not-allowed disabled:opacity-50 *:data-[slot=select-value]:line-clamp-1 *:data-[slot=select-value]:flex *:data-[slot=select-value]:items-center [&_svg]:pointer-events-none [&_svg]:shrink-0", className)
					},
					restProps,
					{
						get ref() {
							return ref;
						},
						set ref($$value) {
							ref = $$value;
							$$settled = false;
						},
						children: ($$renderer) => {
							children?.($$renderer);
							$$renderer.push(`<!----> `);
							Chevron_down($$renderer, { class: "size-4 text-muted-foreground pointer-events-none" });
							$$renderer.push(`<!---->`);
						},
						$$slots: { default: true }
					}
				]));
				$$renderer.push("<!--]-->");
			} else {
				$$renderer.push("<!--[!-->");
				$$renderer.push("<!--]-->");
			}
		}
		do {
			$$settled = true;
			$$inner_renderer = $$renderer.copy();
			$$render_inner($$inner_renderer);
		} while (!$$settled);
		$$renderer.subsume($$inner_renderer);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/select/select.svelte
function Select($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { open = false, value = void 0, $$slots, $$events, ...restProps } = $$props;
		let $$settled = true;
		let $$inner_renderer;
		function $$render_inner($$renderer) {
			if (Select$1) {
				$$renderer.push("<!--[-->");
				Select$1($$renderer, spread_props([restProps, {
					get open() {
						return open;
					},
					set open($$value) {
						open = $$value;
						$$settled = false;
					},
					get value() {
						return value;
					},
					set value($$value) {
						value = $$value;
						$$settled = false;
					}
				}]));
				$$renderer.push("<!--]-->");
			} else {
				$$renderer.push("<!--[!-->");
				$$renderer.push("<!--]-->");
			}
		}
		do {
			$$settled = true;
			$$inner_renderer = $$renderer.copy();
			$$render_inner($$inner_renderer);
		} while (!$$settled);
		$$renderer.subsume($$inner_renderer);
		bind_props($$props, {
			open,
			value
		});
	});
}
//#endregion
//#region src/lib/ReviewWorkflow.svelte
function ReviewWorkflow($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		const states = [
			{
				value: "draft",
				label: "Draft"
			},
			{
				value: "translated",
				label: "Translated"
			},
			{
				value: "needs-review",
				label: "Needs review"
			},
			{
				value: "approved",
				label: "Approved"
			}
		];
		let { state: reviewState, dirty, message, disabled, stale, terminologyCount, qualityCount, note, qualityIssues, suggestions, onstatechange, onnotechange, onterminology, onreport, onqualityfilter, onsuggestion } = $$props;
		let stateLabel = derived(() => states.find((option) => option.value === reviewState)?.label ?? reviewState);
		let open = false;
		let $$settled = true;
		let $$inner_renderer;
		function $$render_inner($$renderer) {
			if (Collapsible) {
				$$renderer.push("<!--[-->");
				Collapsible($$renderer, {
					class: "group/workflow mx-auto mb-4 w-full max-w-[1000px]",
					get open() {
						return open;
					},
					set open($$value) {
						open = $$value;
						$$settled = false;
					},
					children: ($$renderer) => {
						if (Card) {
							$$renderer.push("<!--[-->");
							Card($$renderer, {
								size: "sm",
								class: "gap-0 py-0 shadow-none",
								children: ($$renderer) => {
									if (Card_header) {
										$$renderer.push("<!--[-->");
										Card_header($$renderer, {
											class: "p-0",
											children: ($$renderer) => {
												if (Collapsible_trigger) {
													$$renderer.push("<!--[-->");
													Collapsible_trigger($$renderer, {
														class: "flex min-h-12 w-full items-center gap-2 rounded-2xl px-4 py-2 text-left outline-none focus-visible:ring-2 focus-visible:ring-ring",
														children: ($$renderer) => {
															List_checks($$renderer, {
																class: "size-4 shrink-0",
																"aria-hidden": "true"
															});
															$$renderer.push(`<!----> <span class="font-medium">Workflow</span> `);
															Badge($$renderer, {
																variant: "secondary",
																children: ($$renderer) => {
																	$$renderer.push(`<!---->${escape_html(stateLabel())}`);
																},
																$$slots: { default: true }
															});
															$$renderer.push(`<!----> `);
															if (stale) {
																$$renderer.push("<!--[0-->");
																Badge($$renderer, {
																	variant: "destructive",
																	children: ($$renderer) => {
																		$$renderer.push(`<!---->Source changed`);
																	},
																	$$slots: { default: true }
																});
															} else $$renderer.push("<!--[-1-->");
															$$renderer.push(`<!--]--> <span class="ml-auto hidden min-w-0 truncate text-xs text-muted-foreground sm:block">${escape_html(dirty ? "Unsaved workflow changes" : message)}</span> `);
															Chevron_down($$renderer, {
																class: "size-4 shrink-0 transition-transform group-data-[state=open]/workflow:rotate-180",
																"aria-hidden": "true"
															});
															$$renderer.push(`<!---->`);
														},
														$$slots: { default: true }
													});
													$$renderer.push("<!--]-->");
												} else {
													$$renderer.push("<!--[!-->");
													$$renderer.push("<!--]-->");
												}
											},
											$$slots: { default: true }
										});
										$$renderer.push("<!--]-->");
									} else {
										$$renderer.push("<!--[!-->");
										$$renderer.push("<!--]-->");
									}
									$$renderer.push(` `);
									if (Collapsible_content) {
										$$renderer.push("<!--[-->");
										Collapsible_content($$renderer, {
											children: ($$renderer) => {
												if (Card_content) {
													$$renderer.push("<!--[-->");
													Card_content($$renderer, {
														class: "grid grid-cols-1 gap-4 border-t py-4 xl:grid-cols-[minmax(0,1.25fr)_minmax(17rem,1fr)]",
														children: ($$renderer) => {
															$$renderer.push(`<div class="grid content-start gap-4"><div class="flex flex-wrap items-end gap-2">`);
															if (Field) {
																$$renderer.push("<!--[-->");
																Field($$renderer, {
																	class: "gap-1",
																	children: ($$renderer) => {
																		if (Field_label) {
																			$$renderer.push("<!--[-->");
																			Field_label($$renderer, {
																				for: "workflow-status",
																				children: ($$renderer) => {
																					$$renderer.push(`<!---->Status`);
																				},
																				$$slots: { default: true }
																			});
																			$$renderer.push("<!--]-->");
																		} else {
																			$$renderer.push("<!--[!-->");
																			$$renderer.push("<!--]-->");
																		}
																		$$renderer.push(` `);
																		if (Select) {
																			$$renderer.push("<!--[-->");
																			Select($$renderer, {
																				type: "single",
																				value: reviewState,
																				disabled,
																				onValueChange: (value) => {
																					onstatechange(value);
																				},
																				children: ($$renderer) => {
																					if (Select_trigger) {
																						$$renderer.push("<!--[-->");
																						Select_trigger($$renderer, {
																							id: "workflow-status",
																							size: "sm",
																							class: "min-w-36",
																							children: ($$renderer) => {
																								$$renderer.push(`<!---->${escape_html(stateLabel())}`);
																							},
																							$$slots: { default: true }
																						});
																						$$renderer.push("<!--]-->");
																					} else {
																						$$renderer.push("<!--[!-->");
																						$$renderer.push("<!--]-->");
																					}
																					$$renderer.push(` `);
																					if (Select_content) {
																						$$renderer.push("<!--[-->");
																						Select_content($$renderer, {
																							children: ($$renderer) => {
																								if (Select_group) {
																									$$renderer.push("<!--[-->");
																									Select_group($$renderer, {
																										children: ($$renderer) => {
																											if (Select_label) {
																												$$renderer.push("<!--[-->");
																												Select_label($$renderer, {
																													children: ($$renderer) => {
																														$$renderer.push(`<!---->Workflow status`);
																													},
																													$$slots: { default: true }
																												});
																												$$renderer.push("<!--]-->");
																											} else {
																												$$renderer.push("<!--[!-->");
																												$$renderer.push("<!--]-->");
																											}
																											$$renderer.push(` <!--[-->`);
																											const each_array = ensure_array_like(states);
																											for (let $$index = 0, $$length = each_array.length; $$index < $$length; $$index++) {
																												let option = each_array[$$index];
																												if (Select_item) {
																													$$renderer.push("<!--[-->");
																													Select_item($$renderer, {
																														value: option.value,
																														label: option.label,
																														children: ($$renderer) => {
																															$$renderer.push(`<!---->${escape_html(option.label)}`);
																														},
																														$$slots: { default: true }
																													});
																													$$renderer.push("<!--]-->");
																												} else {
																													$$renderer.push("<!--[!-->");
																													$$renderer.push("<!--]-->");
																												}
																											}
																											$$renderer.push(`<!--]-->`);
																										},
																										$$slots: { default: true }
																									});
																									$$renderer.push("<!--]-->");
																								} else {
																									$$renderer.push("<!--[!-->");
																									$$renderer.push("<!--]-->");
																								}
																							},
																							$$slots: { default: true }
																						});
																						$$renderer.push("<!--]-->");
																					} else {
																						$$renderer.push("<!--[!-->");
																						$$renderer.push("<!--]-->");
																					}
																				},
																				$$slots: { default: true }
																			});
																			$$renderer.push("<!--]-->");
																		} else {
																			$$renderer.push("<!--[!-->");
																			$$renderer.push("<!--]-->");
																		}
																	},
																	$$slots: { default: true }
																});
																$$renderer.push("<!--]-->");
															} else {
																$$renderer.push("<!--[!-->");
																$$renderer.push("<!--]-->");
															}
															$$renderer.push(` `);
															Button($$renderer, {
																variant: "outline",
																size: "sm",
																onclick: onterminology,
																children: ($$renderer) => {
																	Book_open($$renderer, { "data-icon": "inline-start" });
																	$$renderer.push(`<!----> Terminology · ${escape_html(terminologyCount)}`);
																},
																$$slots: { default: true }
															});
															$$renderer.push(`<!----> `);
															Button($$renderer, {
																variant: "outline",
																size: "sm",
																onclick: onreport,
																children: ($$renderer) => {
																	Clipboard_list($$renderer, { "data-icon": "inline-start" });
																	$$renderer.push(`<!----> Quality report · ${escape_html(qualityCount)}`);
																},
																$$slots: { default: true }
															});
															$$renderer.push(`<!----></div> `);
															if (Field) {
																$$renderer.push("<!--[-->");
																Field($$renderer, {
																	children: ($$renderer) => {
																		if (Field_label) {
																			$$renderer.push("<!--[-->");
																			Field_label($$renderer, {
																				for: "review-note",
																				children: ($$renderer) => {
																					$$renderer.push(`<!---->Translator / reviewer note`);
																				},
																				$$slots: { default: true }
																			});
																			$$renderer.push("<!--]-->");
																		} else {
																			$$renderer.push("<!--[!-->");
																			$$renderer.push("<!--]-->");
																		}
																		$$renderer.push(` `);
																		Textarea($$renderer, {
																			id: "review-note",
																			class: "min-h-20 resize-y",
																			value: note,
																			placeholder: "Optional context for the next reviewer…",
																			oninput: (event) => onnotechange(event.currentTarget.value)
																		});
																		$$renderer.push(`<!---->`);
																	},
																	$$slots: { default: true }
																});
																$$renderer.push("<!--]-->");
															} else {
																$$renderer.push("<!--[!-->");
																$$renderer.push("<!--]-->");
															}
															$$renderer.push(`</div> <section class="flex flex-col gap-2" aria-label="Quality checks"><strong class="text-sm font-medium">Quality checks</strong> `);
															if (qualityIssues.length === 0) {
																$$renderer.push("<!--[0-->");
																$$renderer.push(`<div class="flex items-center gap-2 text-sm text-muted-foreground">`);
																Circle_check($$renderer, { class: "text-primary" });
																$$renderer.push(`<!----> No issues found</div>`);
															} else {
																$$renderer.push("<!--[-1-->");
																$$renderer.push(`<!--[-->`);
																const each_array_1 = ensure_array_like(qualityIssues);
																for (let $$index_1 = 0, $$length = each_array_1.length; $$index_1 < $$length; $$index_1++) {
																	let issue = each_array_1[$$index_1];
																	Button($$renderer, {
																		variant: "outline",
																		size: "xs",
																		class: "h-auto justify-start whitespace-normal",
																		onclick: onqualityfilter,
																		children: ($$renderer) => {
																			Triangle_alert($$renderer, {
																				class: "text-primary",
																				"data-icon": "inline-start"
																			});
																			$$renderer.push(`<!----> <span class="text-left">${escape_html(issue.message)}</span>`);
																		},
																		$$slots: { default: true }
																	});
																}
																$$renderer.push(`<!--]-->`);
															}
															$$renderer.push(`<!--]--></section>`);
														},
														$$slots: { default: true }
													});
													$$renderer.push("<!--]-->");
												} else {
													$$renderer.push("<!--[!-->");
													$$renderer.push("<!--]-->");
												}
												$$renderer.push(` `);
												if (suggestions.length > 0) {
													$$renderer.push("<!--[0-->");
													if (Card_footer) {
														$$renderer.push("<!--[-->");
														Card_footer($$renderer, {
															class: "flex-col items-stretch gap-2 border-t py-3",
															children: ($$renderer) => {
																$$renderer.push(`<strong class="flex items-center gap-2 text-sm font-medium">`);
																Sparkles($$renderer, {});
																$$renderer.push(`<!---->Local translation memory</strong> <div class="grid gap-1.5"><!--[-->`);
																const each_array_2 = ensure_array_like(suggestions);
																for (let $$index_2 = 0, $$length = each_array_2.length; $$index_2 < $$length; $$index_2++) {
																	let suggestion = each_array_2[$$index_2];
																	Button($$renderer, {
																		variant: "ghost",
																		size: "sm",
																		class: "h-auto justify-start whitespace-normal",
																		title: suggestion.source,
																		onclick: () => onsuggestion(suggestion.translation),
																		children: ($$renderer) => {
																			Badge($$renderer, {
																				variant: "secondary",
																				children: ($$renderer) => {
																					$$renderer.push(`<!---->${escape_html(Math.round(suggestion.score * 100))}%`);
																				},
																				$$slots: { default: true }
																			});
																			$$renderer.push(`<!----> <span class="text-left"><code>${escape_html(suggestion.key)}</code> · ${escape_html(suggestion.translation)}</span>`);
																		},
																		$$slots: { default: true }
																	});
																}
																$$renderer.push(`<!--]--></div>`);
															},
															$$slots: { default: true }
														});
														$$renderer.push("<!--]-->");
													} else {
														$$renderer.push("<!--[!-->");
														$$renderer.push("<!--]-->");
													}
												} else $$renderer.push("<!--[-1-->");
												$$renderer.push(`<!--]-->`);
											},
											$$slots: { default: true }
										});
										$$renderer.push("<!--]-->");
									} else {
										$$renderer.push("<!--[!-->");
										$$renderer.push("<!--]-->");
									}
								},
								$$slots: { default: true }
							});
							$$renderer.push("<!--]-->");
						} else {
							$$renderer.push("<!--[!-->");
							$$renderer.push("<!--]-->");
						}
					},
					$$slots: { default: true }
				});
				$$renderer.push("<!--]-->");
			} else {
				$$renderer.push("<!--[!-->");
				$$renderer.push("<!--]-->");
			}
		}
		do {
			$$settled = true;
			$$inner_renderer = $$renderer.copy();
			$$render_inner($$inner_renderer);
		} while (!$$settled);
		$$renderer.subsume($$inner_renderer);
	});
}
//#endregion
//#region src/lib/SidebarSectionPanels.svelte
function SidebarSectionPanels($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		const defaultShare = .5;
		const minimumShare = .2;
		let { languages, messages, languagesOpen = true, messagesOpen = true } = $$props;
		let languagesShare = defaultShare;
		let resizing = false;
		let layout = derived(() => languagesOpen && messagesOpen ? "both-open" : languagesOpen ? "languages-open" : messagesOpen ? "messages-open" : "both-closed");
		$$renderer.push(`<div${attr_class(clsx$1([
			"sidebar-section-panels",
			layout(),
			resizing
		]), "svelte-g07qdv")}${attr_style("", {
			"--languages-size": `${languagesShare}fr`,
			"--messages-size": `0.5fr`
		})}><section class="sidebar-section-panel languages-panel svelte-g07qdv" aria-label="Languages panel">`);
		languages($$renderer);
		$$renderer.push(`<!----></section> `);
		if (languagesOpen && messagesOpen) {
			$$renderer.push("<!--[0-->");
			$$renderer.push(`<div class="section-resizer svelte-g07qdv" role="slider" tabindex="0" aria-label="Resize Languages and Messages" aria-orientation="vertical"${attr("aria-valuemin", minimumShare * 100)}${attr("aria-valuemax", 80)}${attr("aria-valuenow", Math.round(languagesShare * 100))}${attr("aria-valuetext", `Languages ${Math.round(languagesShare * 100)}%, Messages ${Math.round(50)}%`)} title="Drag to resize · Double-click to reset"></div>`);
		} else $$renderer.push("<!--[-1-->");
		$$renderer.push(`<!--]--> <section class="sidebar-section-panel messages-panel svelte-g07qdv" aria-label="Messages panel">`);
		messages($$renderer);
		$$renderer.push(`<!----></section></div>`);
		bind_props($$props, {
			languagesOpen,
			messagesOpen
		});
	});
}
//#endregion
//#region node_modules/@lucide/svelte/dist/icons/wand-sparkles.svelte
function Wand_sparkles($$renderer, $$props) {
	let { $$slots, $$events, ...props } = $$props;
	Icon($$renderer, spread_props([
		{ name: "wand-sparkles" },
		props,
		{ iconNode: [
			["path", { "d": "m21.64 3.64-1.28-1.28a1.21 1.21 0 0 0-1.72 0L2.36 18.64a1.21 1.21 0 0 0 0 1.72l1.28 1.28a1.2 1.2 0 0 0 1.72 0L21.64 5.36a1.2 1.2 0 0 0 0-1.72" }],
			["path", { "d": "m14 7 3 3" }],
			["path", { "d": "M5 6v4" }],
			["path", { "d": "M19 14v4" }],
			["path", { "d": "M10 2v2" }],
			["path", { "d": "M7 8H3" }],
			["path", { "d": "M21 16h-4" }],
			["path", { "d": "M11 3H9" }]
		] }
	]));
}
//#endregion
//#region node_modules/@lucide/svelte/dist/icons/arrow-down.svelte
function Arrow_down($$renderer, $$props) {
	let { $$slots, $$events, ...props } = $$props;
	Icon($$renderer, spread_props([
		{ name: "arrow-down" },
		props,
		{ iconNode: [["path", { "d": "M12 5v14" }], ["path", { "d": "m19 12-7 7-7-7" }]] }
	]));
}
//#endregion
//#region node_modules/@lucide/svelte/dist/icons/arrow-up.svelte
function Arrow_up($$renderer, $$props) {
	let { $$slots, $$events, ...props } = $$props;
	Icon($$renderer, spread_props([
		{ name: "arrow-up" },
		props,
		{ iconNode: [["path", { "d": "m5 12 7-7 7 7" }], ["path", { "d": "M12 19V5" }]] }
	]));
}
//#endregion
//#region node_modules/@lucide/svelte/dist/icons/circle-plus.svelte
function Circle_plus($$renderer, $$props) {
	let { $$slots, $$events, ...props } = $$props;
	Icon($$renderer, spread_props([
		{ name: "circle-plus" },
		props,
		{ iconNode: [
			["circle", {
				"cx": "12",
				"cy": "12",
				"r": "10"
			}],
			["path", { "d": "M8 12h8" }],
			["path", { "d": "M12 8v8" }]
		] }
	]));
}
//#endregion
//#region node_modules/@lucide/svelte/dist/icons/code-xml.svelte
function Code_xml($$renderer, $$props) {
	let { $$slots, $$events, ...props } = $$props;
	Icon($$renderer, spread_props([
		{ name: "code-xml" },
		props,
		{ iconNode: [
			["path", { "d": "m18 16 4-4-4-4" }],
			["path", { "d": "m6 8-4 4 4 4" }],
			["path", { "d": "m14.5 4-5 16" }]
		] }
	]));
}
//#endregion
//#region src/lib/components/ui/popover/popover-portal.svelte
function Popover_portal($$renderer, $$props) {
	let { $$slots, $$events, ...restProps } = $$props;
	if (Portal) {
		$$renderer.push("<!--[-->");
		Portal($$renderer, spread_props([restProps]));
		$$renderer.push("<!--]-->");
	} else {
		$$renderer.push("<!--[!-->");
		$$renderer.push("<!--]-->");
	}
}
//#endregion
//#region src/lib/components/ui/popover/popover-content.svelte
function Popover_content($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, sideOffset = 4, align = "center", portalProps, $$slots, $$events, ...restProps } = $$props;
		let $$settled = true;
		let $$inner_renderer;
		function $$render_inner($$renderer) {
			Popover_portal($$renderer, spread_props([portalProps, {
				children: ($$renderer) => {
					if (Popover_content$1) {
						$$renderer.push("<!--[-->");
						Popover_content$1($$renderer, spread_props([
							{
								"data-slot": "popover-content",
								sideOffset,
								align,
								class: cn("flex flex-col gap-4 rounded-3xl bg-popover p-4 text-sm text-popover-foreground shadow-lg ring-1 ring-foreground/5 duration-100 data-[side=bottom]:slide-in-from-top-2 data-[side=left]:slide-in-from-right-2 data-[side=right]:slide-in-from-left-2 data-[side=top]:slide-in-from-bottom-2 dark:ring-foreground/10 data-open:animate-in data-open:fade-in-0 data-open:zoom-in-95 data-closed:animate-out data-closed:fade-out-0 data-closed:zoom-out-95 data-[side=inline-end]:slide-in-from-left-2 data-[side=inline-start]:slide-in-from-right-2 z-50 w-72 origin-(--transform-origin) outline-hidden", className)
							},
							restProps,
							{
								get ref() {
									return ref;
								},
								set ref($$value) {
									ref = $$value;
									$$settled = false;
								}
							}
						]));
						$$renderer.push("<!--]-->");
					} else {
						$$renderer.push("<!--[!-->");
						$$renderer.push("<!--]-->");
					}
				},
				$$slots: { default: true }
			}]));
		}
		do {
			$$settled = true;
			$$inner_renderer = $$renderer.copy();
			$$render_inner($$inner_renderer);
		} while (!$$settled);
		$$renderer.subsume($$inner_renderer);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/popover/popover-description.svelte
function Popover_description($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, children, $$slots, $$events, ...restProps } = $$props;
		$$renderer.push(`<div${attributes({
			"data-slot": "popover-description",
			class: clsx$1(cn("text-muted-foreground", className)),
			...restProps
		})}>`);
		children?.($$renderer);
		$$renderer.push(`<!----></div>`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/popover/popover-header.svelte
function Popover_header($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, children, $$slots, $$events, ...restProps } = $$props;
		$$renderer.push(`<div${attributes({
			"data-slot": "popover-header",
			class: clsx$1(cn("flex flex-col gap-1 text-sm", className)),
			...restProps
		})}>`);
		children?.($$renderer);
		$$renderer.push(`<!----></div>`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/popover/popover-title.svelte
function Popover_title($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, children, $$slots, $$events, ...restProps } = $$props;
		$$renderer.push(`<div${attributes({
			"data-slot": "popover-title",
			class: clsx$1(cn("text-base font-medium", className)),
			...restProps
		})}>`);
		children?.($$renderer);
		$$renderer.push(`<!----></div>`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/popover/popover-trigger.svelte
function Popover_trigger($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, $$slots, $$events, ...restProps } = $$props;
		let $$settled = true;
		let $$inner_renderer;
		function $$render_inner($$renderer) {
			if (Popover_trigger$1) {
				$$renderer.push("<!--[-->");
				Popover_trigger$1($$renderer, spread_props([
					{
						"data-slot": "popover-trigger",
						class: cn("", className)
					},
					restProps,
					{
						get ref() {
							return ref;
						},
						set ref($$value) {
							ref = $$value;
							$$settled = false;
						}
					}
				]));
				$$renderer.push("<!--]-->");
			} else {
				$$renderer.push("<!--[!-->");
				$$renderer.push("<!--]-->");
			}
		}
		do {
			$$settled = true;
			$$inner_renderer = $$renderer.copy();
			$$render_inner($$inner_renderer);
		} while (!$$settled);
		$$renderer.subsume($$inner_renderer);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/popover/popover.svelte
function Popover($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { open = false, $$slots, $$events, ...restProps } = $$props;
		let $$settled = true;
		let $$inner_renderer;
		function $$render_inner($$renderer) {
			if (Popover$1) {
				$$renderer.push("<!--[-->");
				Popover$1($$renderer, spread_props([restProps, {
					get open() {
						return open;
					},
					set open($$value) {
						open = $$value;
						$$settled = false;
					}
				}]));
				$$renderer.push("<!--]-->");
			} else {
				$$renderer.push("<!--[!-->");
				$$renderer.push("<!--]-->");
			}
		}
		do {
			$$settled = true;
			$$inner_renderer = $$renderer.copy();
			$$render_inner($$inner_renderer);
		} while (!$$settled);
		$$renderer.subsume($$inner_renderer);
		bind_props($$props, { open });
	});
}
//#endregion
//#region src/lib/PatternEditor.svelte
function PatternEditor($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { nodes, inputs, localNames, onchange } = $$props;
		let inputNames = derived(() => Object.keys(inputs));
		let formattableInputs = derived(() => inputNames().filter((name) => inputs[name].type !== "bool"));
		function kind(node) {
			if (typeof node === "string") return "text";
			if ("input" in node) return "input";
			if ("local" in node) return "local";
			if ("format" in node) return "format";
			return "markup";
		}
		function replacement(type) {
			const input = inputNames()[0] ?? "value";
			if (type === "text") return "";
			if (type === "input") return { input };
			if (type === "local") return { local: localNames[0] ?? "formattedValue" };
			if (type === "format") {
				const formattedInput = formattableInputs()[0] ?? input;
				return { format: {
					input: formattedInput,
					function: functionFor(inputs[formattedInput]?.type)
				} };
			}
			return { markup: {
				name: "strong",
				attributes: {},
				children: [""]
			} };
		}
		function functionFor(type) {
			return {
				int64: "integer",
				decimal: "number",
				date: "date",
				time: "time",
				instant: "datetime",
				uuid: "uuid"
			}[type ?? "string"] ?? "string";
		}
		function listAt(root, path) {
			let list = root;
			for (const index of path) {
				const node = list[index];
				if (typeof node === "string" || !("markup" in node)) throw new TypeError("Invalid markup path.");
				list = node.markup.children;
			}
			return list;
		}
		function mutate(path, action) {
			const next = structuredClone(nodes);
			action(listAt(next, path));
			onchange(next);
		}
		function replace(path, index, node) {
			mutate(path, (list) => list[index] = node);
		}
		function updateFormat(path, index, property, value) {
			mutate(path, (list) => {
				const node = list[index];
				if (typeof node === "string" || !("format" in node)) return;
				const format = node.format;
				if (property === "format" && value === "") delete format.format;
				else format[property] = value;
				if (property === "function" && value === "relativeTime") {
					format.unit = "day";
					format.numeric = "auto";
				} else if (property === "function") {
					delete format.unit;
					delete format.numeric;
				}
			});
		}
		function nodeList($$renderer, list, path, depth) {
			$$renderer.push(`<div${attr_class("pattern-list svelte-1jhhgd2", void 0, { "nested": depth > 0 })}><!--[-->`);
			const each_array = ensure_array_like(list);
			for (let index = 0, $$length = each_array.length; index < $$length; index++) {
				let node = each_array[index];
				$$renderer.push(`<article class="pattern-node svelte-1jhhgd2"><header class="svelte-1jhhgd2"><label class="svelte-1jhhgd2">Content type `);
				$$renderer.select({
					value: kind(node),
					onchange: (event) => replace(path, index, replacement(event.currentTarget.value)),
					class: ""
				}, ($$renderer) => {
					$$renderer.option({ value: "text" }, ($$renderer) => {
						$$renderer.push(`Text`);
					});
					$$renderer.option({
						value: "input",
						disabled: inputNames().length === 0
					}, ($$renderer) => {
						$$renderer.push(`Input chip`);
					});
					$$renderer.option({
						value: "local",
						disabled: localNames.length === 0
					}, ($$renderer) => {
						$$renderer.push(`Declaration chip`);
					});
					$$renderer.option({
						value: "format",
						disabled: formattableInputs().length === 0
					}, ($$renderer) => {
						$$renderer.push(`Inline formatter`);
					});
					$$renderer.option({ value: "markup" }, ($$renderer) => {
						$$renderer.push(`Semantic markup`);
					});
				}, "svelte-1jhhgd2");
				$$renderer.push(`</label> <div class="node-actions svelte-1jhhgd2"><button aria-label="Move content up"${attr("disabled", index === 0, true)} class="svelte-1jhhgd2">↑</button> <button aria-label="Move content down"${attr("disabled", index === list.length - 1, true)} class="svelte-1jhhgd2">↓</button> <button class="remove svelte-1jhhgd2" aria-label="Remove content">×</button></div></header> `);
				if (typeof node === "string") {
					$$renderer.push("<!--[0-->");
					$$renderer.push(`<textarea aria-label="Text content" class="svelte-1jhhgd2">`);
					const $$body = escape_html(node);
					if ($$body) $$renderer.push(`${$$body}`);
					$$renderer.push(`</textarea>`);
				} else if ("input" in node) {
					$$renderer.push("<!--[1-->");
					$$renderer.push(`<label class="chip-field svelte-1jhhgd2">Protected input `);
					$$renderer.select({
						value: node.input,
						onchange: (event) => replace(path, index, { input: event.currentTarget.value }),
						class: ""
					}, ($$renderer) => {
						$$renderer.push(`<!--[-->`);
						const each_array_1 = ensure_array_like(inputNames());
						for (let $$index = 0, $$length = each_array_1.length; $$index < $$length; $$index++) {
							let name = each_array_1[$$index];
							$$renderer.option({ value: name }, ($$renderer) => {
								$$renderer.push(`${escape_html(name)}`);
							});
						}
						$$renderer.push(`<!--]-->`);
					}, "svelte-1jhhgd2");
					$$renderer.push(`</label>`);
				} else if ("local" in node) {
					$$renderer.push("<!--[2-->");
					$$renderer.push(`<label class="chip-field local svelte-1jhhgd2">Formatted declaration `);
					$$renderer.select({
						value: node.local,
						onchange: (event) => replace(path, index, { local: event.currentTarget.value }),
						class: ""
					}, ($$renderer) => {
						$$renderer.push(`<!--[-->`);
						const each_array_2 = ensure_array_like(localNames);
						for (let $$index_1 = 0, $$length = each_array_2.length; $$index_1 < $$length; $$index_1++) {
							let name = each_array_2[$$index_1];
							$$renderer.option({ value: name }, ($$renderer) => {
								$$renderer.push(`${escape_html(name)}`);
							});
						}
						$$renderer.push(`<!--]-->`);
					}, "svelte-1jhhgd2");
					$$renderer.push(`</label>`);
				} else if ("format" in node) {
					$$renderer.push("<!--[3-->");
					$$renderer.push(`<div class="format-grid svelte-1jhhgd2"><label class="svelte-1jhhgd2">Input`);
					$$renderer.select({
						value: node.format.input,
						onchange: (event) => updateFormat(path, index, "input", event.currentTarget.value),
						class: ""
					}, ($$renderer) => {
						$$renderer.push(`<!--[-->`);
						const each_array_3 = ensure_array_like(formattableInputs());
						for (let $$index_2 = 0, $$length = each_array_3.length; $$index_2 < $$length; $$index_2++) {
							let name = each_array_3[$$index_2];
							$$renderer.option({ value: name }, ($$renderer) => {
								$$renderer.push(`${escape_html(name)}`);
							});
						}
						$$renderer.push(`<!--]-->`);
					}, "svelte-1jhhgd2");
					$$renderer.push(`</label> <label class="svelte-1jhhgd2">Formatter`);
					$$renderer.select({
						value: node.format.function,
						onchange: (event) => updateFormat(path, index, "function", event.currentTarget.value),
						class: ""
					}, ($$renderer) => {
						$$renderer.push(`<!--[-->`);
						const each_array_4 = ensure_array_like(formatFunctions);
						for (let $$index_3 = 0, $$length = each_array_4.length; $$index_3 < $$length; $$index_3++) {
							let fn = each_array_4[$$index_3];
							$$renderer.option({ value: fn }, ($$renderer) => {
								$$renderer.push(`${escape_html(fn)}`);
							});
						}
						$$renderer.push(`<!--]-->`);
					}, "svelte-1jhhgd2");
					$$renderer.push(`</label> `);
					if (node.format.function === "relativeTime") {
						$$renderer.push("<!--[0-->");
						$$renderer.push(`<label class="svelte-1jhhgd2">Unit`);
						$$renderer.select({
							value: node.format.unit ?? "day",
							onchange: (event) => updateFormat(path, index, "unit", event.currentTarget.value),
							class: ""
						}, ($$renderer) => {
							$$renderer.push(`<!--[-->`);
							const each_array_5 = ensure_array_like(relativeTimeUnits);
							for (let $$index_4 = 0, $$length = each_array_5.length; $$index_4 < $$length; $$index_4++) {
								let unit = each_array_5[$$index_4];
								$$renderer.option({ value: unit }, ($$renderer) => {
									$$renderer.push(`${escape_html(unit)}`);
								});
							}
							$$renderer.push(`<!--]-->`);
						}, "svelte-1jhhgd2");
						$$renderer.push(`</label> <label class="svelte-1jhhgd2">Numeric`);
						$$renderer.select({
							value: node.format.numeric ?? "auto",
							onchange: (event) => updateFormat(path, index, "numeric", event.currentTarget.value),
							class: ""
						}, ($$renderer) => {
							$$renderer.option({ value: "auto" }, ($$renderer) => {
								$$renderer.push(`auto`);
							});
							$$renderer.option({ value: "always" }, ($$renderer) => {
								$$renderer.push(`always`);
							});
						}, "svelte-1jhhgd2");
						$$renderer.push(`</label>`);
					} else {
						$$renderer.push("<!--[-1-->");
						$$renderer.push(`<label class="svelte-1jhhgd2">Format<input${attr("value", node.format.format ?? "")} placeholder="compiler default" class="svelte-1jhhgd2"/></label>`);
					}
					$$renderer.push(`<!--]--></div>`);
				} else {
					$$renderer.push("<!--[-1-->");
					$$renderer.push(`<div class="markup-editor svelte-1jhhgd2"><label class="svelte-1jhhgd2">Semantic name<input${attr("value", node.markup.name)} class="svelte-1jhhgd2"/></label> <div class="attributes svelte-1jhhgd2"><header class="svelte-1jhhgd2"><strong class="svelte-1jhhgd2">Attributes</strong><button class="svelte-1jhhgd2">＋ Add</button></header> <!--[-->`);
					const each_array_6 = ensure_array_like(Object.entries(node.markup.attributes ?? {}));
					for (let $$index_5 = 0, $$length = each_array_6.length; $$index_5 < $$length; $$index_5++) {
						let [name, value] = each_array_6[$$index_5];
						$$renderer.push(`<div class="svelte-1jhhgd2"><input aria-label="Attribute name"${attr("value", name)} class="svelte-1jhhgd2"/><input${attr("aria-label", `Value for ${name}`)}${attr("value", value)} class="svelte-1jhhgd2"/><button${attr("aria-label", `Remove ${name}`)} class="svelte-1jhhgd2">×</button></div>`);
					}
					$$renderer.push(`<!--]--></div> <div class="children-label svelte-1jhhgd2">Children · rendered as safe semantic data</div> `);
					nodeList($$renderer, node.markup.children, [...path, index], depth + 1);
					$$renderer.push(`<!----></div>`);
				}
				$$renderer.push(`<!--]--></article>`);
			}
			$$renderer.push(`<!--]--> <div class="add-nodes svelte-1jhhgd2" role="group" aria-label="Add content"><button class="svelte-1jhhgd2">＋ Text</button> <button${attr("disabled", inputNames().length === 0, true)} class="svelte-1jhhgd2">＋ Input</button> <button${attr("disabled", localNames.length === 0, true)} class="svelte-1jhhgd2">＋ Declaration</button> <button${attr("disabled", formattableInputs().length === 0, true)} class="svelte-1jhhgd2">＋ Formatter</button> <button class="svelte-1jhhgd2">＋ Markup</button></div></div>`);
		}
		nodeList($$renderer, nodes, [], 0);
	});
}
//#endregion
//#region node_modules/@lucide/svelte/dist/icons/arrow-left.svelte
function Arrow_left($$renderer, $$props) {
	let { $$slots, $$events, ...props } = $$props;
	Icon($$renderer, spread_props([
		{ name: "arrow-left" },
		props,
		{ iconNode: [["path", { "d": "m12 19-7-7 7-7" }], ["path", { "d": "M19 12H5" }]] }
	]));
}
//#endregion
//#region node_modules/@lucide/svelte/dist/icons/arrow-right.svelte
function Arrow_right($$renderer, $$props) {
	let { $$slots, $$events, ...props } = $$props;
	Icon($$renderer, spread_props([
		{ name: "arrow-right" },
		props,
		{ iconNode: [["path", { "d": "M5 12h14" }], ["path", { "d": "m12 5 7 7-7 7" }]] }
	]));
}
//#endregion
//#region node_modules/@lucide/svelte/dist/icons/grip-vertical.svelte
function Grip_vertical($$renderer, $$props) {
	let { $$slots, $$events, ...props } = $$props;
	Icon($$renderer, spread_props([
		{ name: "grip-vertical" },
		props,
		{ iconNode: [
			["circle", {
				"cx": "9",
				"cy": "12",
				"r": "1"
			}],
			["circle", {
				"cx": "9",
				"cy": "5",
				"r": "1"
			}],
			["circle", {
				"cx": "9",
				"cy": "19",
				"r": "1"
			}],
			["circle", {
				"cx": "15",
				"cy": "12",
				"r": "1"
			}],
			["circle", {
				"cx": "15",
				"cy": "5",
				"r": "1"
			}],
			["circle", {
				"cx": "15",
				"cy": "19",
				"r": "1"
			}]
		] }
	]));
}
//#endregion
//#region node_modules/@lucide/svelte/dist/icons/variable.svelte
function Variable($$renderer, $$props) {
	let { $$slots, $$events, ...props } = $$props;
	Icon($$renderer, spread_props([
		{ name: "variable" },
		props,
		{ iconNode: [
			["path", { "d": "M8 21s-4-3-4-9 4-9 4-9" }],
			["path", { "d": "M16 3s4 3 4 9-4 9-4 9" }],
			["line", {
				"x1": "15",
				"x2": "9",
				"y1": "9",
				"y2": "15"
			}],
			["line", {
				"x1": "9",
				"x2": "15",
				"y1": "9",
				"y2": "15"
			}]
		] }
	]));
}
//#endregion
//#region src/lib/InlineMessageEditor.svelte
function InlineMessageEditor($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { value, inputs, label, onchange, onensureinput, onupdateformat } = $$props;
		let selectedToken = void 0;
		let dropSlot = void 0;
		let dropBoundary = void 0;
		let editingSlot = void 0;
		let activeSlot = 0;
		let textareas = [];
		let slots = derived(() => parseSlots(value));
		let inputNames = derived(() => Object.keys(inputs));
		function parseSlots(source) {
			const result = [{ text: "" }];
			for (let index = 0; index < source.length;) {
				if (source.startsWith("{{", index) || source.startsWith("}}", index)) {
					result[result.length - 1].text += source.slice(index, index + 2);
					index += 2;
					continue;
				}
				if (source[index] === "{") {
					const end = source.indexOf("}", index + 1);
					const name = end < 0 ? "" : source.slice(index + 1, end);
					if (/^[A-Za-z_][A-Za-z0-9_]*$/.test(name)) {
						result[result.length - 1].token = name;
						result.push({ text: "" });
						index = end + 1;
						continue;
					}
				}
				result[result.length - 1].text += source[index];
				index += 1;
			}
			return result;
		}
		function serialize(next) {
			return next.map((slot) => `${slot.text}${slot.token === void 0 ? "" : `{${slot.token}}`}`).join("");
		}
		function slotStart(index) {
			let position = 0;
			for (let slotIndex = 0; slotIndex < index; slotIndex += 1) {
				const slot = slots()[slotIndex];
				position += slot.text.length + (slot.token === void 0 ? 0 : slot.token.length + 2);
			}
			return position;
		}
		function tokenStart(index) {
			return slotStart(index) + (slots()[index]?.text.length ?? 0);
		}
		function moveToken(sourceSlot, targetPosition) {
			const source = serialize(slots());
			const name = slots()[sourceSlot]?.token;
			if (name === void 0) return;
			const syntax = `{${name}}`;
			const sourceStart = tokenStart(sourceSlot);
			const sourceEnd = sourceStart + syntax.length;
			if (targetPosition >= sourceStart && targetPosition <= sourceEnd) return;
			const withoutToken = source.slice(0, sourceStart) + source.slice(sourceEnd);
			const adjustedTarget = targetPosition > sourceEnd ? targetPosition - syntax.length : targetPosition;
			onchange(withoutToken.slice(0, adjustedTarget) + syntax + withoutToken.slice(adjustedTarget));
			selectedToken = void 0;
		}
		function startVariableDrag(event, name, sourceSlot) {
			event.dataTransfer?.setData("application/x-runic-variable", JSON.stringify({
				name,
				sourceSlot
			}));
			event.dataTransfer?.setData("text/plain", `{${name}}`);
			if (event.dataTransfer !== null) event.dataTransfer.effectAllowed = "move";
		}
		function allowBoundaryDrop(event, boundary) {
			if (!event.dataTransfer?.types.includes("application/x-runic-variable")) return;
			event.preventDefault();
			event.dataTransfer.dropEffect = "move";
			dropBoundary = boundary;
		}
		function dropVariableAtBoundary(event, targetSlot, targetCaret) {
			const payload = event.dataTransfer?.getData("application/x-runic-variable");
			dropBoundary = void 0;
			if (payload === void 0 || payload === "") return;
			event.preventDefault();
			try {
				const { sourceSlot } = JSON.parse(payload);
				moveToken(sourceSlot, slotStart(targetSlot) + targetCaret);
			} catch {}
		}
		function moveSelected(direction) {
			if (selectedToken === void 0) return;
			const sourceSlot = selectedToken.slot;
			const sourceEnd = tokenStart(sourceSlot) + selectedToken.name.length + 2;
			if (direction === "earlier") {
				moveToken(sourceSlot, slots()[sourceSlot].text.length > 0 ? slotStart(sourceSlot) : sourceSlot > 0 ? tokenStart(sourceSlot - 1) : tokenStart(sourceSlot));
				return;
			}
			const following = slots()[sourceSlot + 1];
			moveToken(sourceSlot, following === void 0 ? sourceEnd : sourceEnd + following.text.length + (following.text.length === 0 && following.token !== void 0 ? following.token.length + 2 : 0));
		}
		function canMoveSelected(direction) {
			if (selectedToken === void 0) return false;
			if (direction === "earlier") return tokenStart(selectedToken.slot) > 0;
			return tokenStart(selectedToken.slot) + selectedToken.name.length + 2 < serialize(slots()).length;
		}
		function inspectToken(name, slot) {
			if (name !== void 0) selectedToken = {
				name,
				slot
			};
		}
		function insertVariableAt(index, position, name) {
			const next = structuredClone(slots());
			const slot = next[index] ?? next[next.length - 1];
			const insertionPosition = Math.min(position, slot.text.length);
			const trailingText = slot.text.slice(insertionPosition);
			const previousToken = slot.token;
			slot.text = slot.text.slice(0, insertionPosition);
			slot.token = name;
			next.splice(index + 1, 0, {
				text: trailingText,
				token: previousToken
			});
			onchange(serialize(next));
			selectedToken = {
				name,
				slot: index
			};
			editingSlot = void 0;
		}
		function insertNewVariableAt(index, position) {
			insertVariableAt(index, position, nextIdentifier("value", inputNames()));
		}
		function editTextAt(index, position) {
			editingSlot = index;
			focusSlot(index, position);
		}
		function removeSelectedToken() {
			if (selectedToken === void 0) return;
			const next = structuredClone(slots());
			const slot = next[selectedToken.slot];
			const following = next[selectedToken.slot + 1];
			if (slot === void 0 || slot.token === void 0) return;
			slot.token = following?.token;
			slot.text += following?.text ?? "";
			if (following !== void 0) next.splice(selectedToken.slot + 1, 1);
			onchange(serialize(next));
			selectedToken = void 0;
			focusSlot(Math.min(activeSlot, next.length - 1), slot.text.length);
		}
		async function focusSlot(index, position) {
			await tick();
			const target = textareas[index];
			if (target === void 0) return;
			target.focus();
			target.setSelectionRange(position, position);
			activeSlot = index;
		}
		function insertionPoint($$renderer, index, position, description) {
			const boundary = `${index}:${position}`;
			if (Dropdown_menu) {
				$$renderer.push("<!--[-->");
				Dropdown_menu($$renderer, {
					children: ($$renderer) => {
						{
							function child($$renderer, { props }) {
								Button($$renderer, spread_props([props, {
									variant: "ghost",
									size: "icon-xs",
									class: ["shrink-0 rounded-full text-muted-foreground hover:text-foreground", dropBoundary === boundary && "bg-primary text-primary-foreground ring-2 ring-primary/40"],
									"aria-label": `Insert ${description}`,
									title: `Insert ${description}`,
									ondragenter: (event) => allowBoundaryDrop(event, boundary),
									ondragover: (event) => allowBoundaryDrop(event, boundary),
									ondragleave: () => dropBoundary = void 0,
									ondrop: (event) => dropVariableAtBoundary(event, index, position),
									children: ($$renderer) => {
										Plus($$renderer, {});
									},
									$$slots: { default: true }
								}]));
							}
							if (Dropdown_menu_trigger) {
								$$renderer.push("<!--[-->");
								Dropdown_menu_trigger($$renderer, {
									child,
									$$slots: { child: true }
								});
								$$renderer.push("<!--]-->");
							} else {
								$$renderer.push("<!--[!-->");
								$$renderer.push("<!--]-->");
							}
						}
						$$renderer.push(` `);
						if (Dropdown_menu_content) {
							$$renderer.push("<!--[-->");
							Dropdown_menu_content($$renderer, {
								align: "start",
								class: "w-52",
								children: ($$renderer) => {
									if (Dropdown_menu_label) {
										$$renderer.push("<!--[-->");
										Dropdown_menu_label($$renderer, {
											children: ($$renderer) => {
												$$renderer.push(`<!---->Insert`);
											},
											$$slots: { default: true }
										});
										$$renderer.push("<!--]-->");
									} else {
										$$renderer.push("<!--[!-->");
										$$renderer.push("<!--]-->");
									}
									$$renderer.push(` `);
									if (Dropdown_menu_item) {
										$$renderer.push("<!--[-->");
										Dropdown_menu_item($$renderer, {
											onclick: () => editTextAt(index, position),
											children: ($$renderer) => {
												$$renderer.push(`<span class="grid size-4 place-items-center font-serif text-base" aria-hidden="true">T</span> Text`);
											},
											$$slots: { default: true }
										});
										$$renderer.push("<!--]-->");
									} else {
										$$renderer.push("<!--[!-->");
										$$renderer.push("<!--]-->");
									}
									$$renderer.push(` `);
									if (Dropdown_menu_sub) {
										$$renderer.push("<!--[-->");
										Dropdown_menu_sub($$renderer, {
											children: ($$renderer) => {
												if (Dropdown_menu_sub_trigger) {
													$$renderer.push("<!--[-->");
													Dropdown_menu_sub_trigger($$renderer, {
														children: ($$renderer) => {
															Variable($$renderer, {});
															$$renderer.push(`<!----> Variable`);
														},
														$$slots: { default: true }
													});
													$$renderer.push("<!--]-->");
												} else {
													$$renderer.push("<!--[!-->");
													$$renderer.push("<!--]-->");
												}
												$$renderer.push(` `);
												if (Dropdown_menu_sub_content) {
													$$renderer.push("<!--[-->");
													Dropdown_menu_sub_content($$renderer, {
														class: "w-52",
														children: ($$renderer) => {
															$$renderer.push(`<!--[-->`);
															const each_array = ensure_array_like(inputNames());
															for (let $$index = 0, $$length = each_array.length; $$index < $$length; $$index++) {
																let name = each_array[$$index];
																if (Dropdown_menu_item) {
																	$$renderer.push("<!--[-->");
																	Dropdown_menu_item($$renderer, {
																		onclick: () => insertVariableAt(index, position, name),
																		children: ($$renderer) => {
																			Variable($$renderer, {});
																			$$renderer.push(`<!----> <span class="font-mono">${escape_html(name)}</span>`);
																		},
																		$$slots: { default: true }
																	});
																	$$renderer.push("<!--]-->");
																} else {
																	$$renderer.push("<!--[!-->");
																	$$renderer.push("<!--]-->");
																}
															}
															$$renderer.push(`<!--]--> `);
															if (inputNames().length > 0) {
																$$renderer.push("<!--[0-->");
																if (Dropdown_menu_separator) {
																	$$renderer.push("<!--[-->");
																	Dropdown_menu_separator($$renderer, {});
																	$$renderer.push("<!--]-->");
																} else {
																	$$renderer.push("<!--[!-->");
																	$$renderer.push("<!--]-->");
																}
															} else $$renderer.push("<!--[-1-->");
															$$renderer.push(`<!--]--> `);
															if (Dropdown_menu_item) {
																$$renderer.push("<!--[-->");
																Dropdown_menu_item($$renderer, {
																	onclick: () => insertNewVariableAt(index, position),
																	children: ($$renderer) => {
																		Plus($$renderer, {});
																		$$renderer.push(`<!----> Create new variable`);
																	},
																	$$slots: { default: true }
																});
																$$renderer.push("<!--]-->");
															} else {
																$$renderer.push("<!--[!-->");
																$$renderer.push("<!--]-->");
															}
														},
														$$slots: { default: true }
													});
													$$renderer.push("<!--]-->");
												} else {
													$$renderer.push("<!--[!-->");
													$$renderer.push("<!--]-->");
												}
											},
											$$slots: { default: true }
										});
										$$renderer.push("<!--]-->");
									} else {
										$$renderer.push("<!--[!-->");
										$$renderer.push("<!--]-->");
									}
								},
								$$slots: { default: true }
							});
							$$renderer.push("<!--]-->");
						} else {
							$$renderer.push("<!--[!-->");
							$$renderer.push("<!--]-->");
						}
					},
					$$slots: { default: true }
				});
				$$renderer.push("<!--]-->");
			} else {
				$$renderer.push("<!--[!-->");
				$$renderer.push("<!--]-->");
			}
		}
		$$renderer.push(`<div class="overflow-hidden rounded-2xl border bg-card/70 shadow-inner focus-within:ring-2 focus-within:ring-ring"><div class="flex min-h-32 flex-wrap content-start items-start gap-x-1 gap-y-2 px-4 py-3" role="group"${attr("aria-label", label)}><!--[-->`);
		const each_array_1 = ensure_array_like(slots());
		for (let index = 0, $$length = each_array_1.length; index < $$length; index++) {
			let slot = each_array_1[index];
			if (index === 0) {
				$$renderer.push("<!--[0-->");
				insertionPoint($$renderer, index, 0, "at the beginning");
			} else $$renderer.push("<!--[-1-->");
			$$renderer.push(`<!--]--> `);
			if (slot.text !== "" || editingSlot === index) {
				$$renderer.push("<!--[0-->");
				$$renderer.push(`<textarea${attr_class(clsx$1([
					"field-sizing-content min-h-7 max-w-full min-w-[3ch] flex-none resize-none overflow-hidden rounded-sm bg-transparent p-0 font-sans text-base leading-7 outline-none transition-[width,background-color,box-shadow] placeholder:text-muted-foreground",
					slot.text === "" && "min-w-[8ch]",
					dropSlot === index && "bg-primary/10 ring-2 ring-primary/50"
				]))} rows="1"${attr("aria-label", `${label}, text ${index + 1}`)} placeholder="Write text…" spellcheck="true">`);
				const $$body = escape_html(slot.text);
				if ($$body) $$renderer.push(`${$$body}`);
				$$renderer.push(`</textarea> `);
				insertionPoint($$renderer, index, slot.text.length, `after text ${index + 1}`);
				$$renderer.push(`<!---->`);
			} else $$renderer.push("<!--[-1-->");
			$$renderer.push(`<!--]--> `);
			if (slot.token !== void 0) {
				$$renderer.push("<!--[0-->");
				Button($$renderer, {
					variant: "secondary",
					size: "sm",
					class: "h-7 shrink-0 rounded-full px-2 font-mono text-xs",
					"aria-label": `Variable ${slot.token}. Open settings.`,
					title: `Variable ${slot.token}. Click to inspect.`,
					draggable: "true",
					ondragstart: (event) => startVariableDrag(event, slot.token, index),
					ondragend: () => {
						dropSlot = void 0;
						dropBoundary = void 0;
					},
					onclick: () => inspectToken(slot.token, index),
					children: ($$renderer) => {
						Grip_vertical($$renderer, {
							"data-icon": "inline-start",
							"aria-hidden": "true"
						});
						$$renderer.push(`<!----> ${escape_html(slot.token)}`);
					},
					$$slots: { default: true }
				});
				$$renderer.push(`<!----> `);
				insertionPoint($$renderer, index + 1, 0, `after variable ${slot.token}`);
				$$renderer.push(`<!---->`);
			} else $$renderer.push("<!--[-1-->");
			$$renderer.push(`<!--]-->`);
		}
		$$renderer.push(`<!--]--></div> `);
		if (selectedToken !== void 0) {
			$$renderer.push("<!--[0-->");
			$$renderer.push(`<div class="grid gap-3 border-t bg-muted/30 px-3 py-3 sm:grid-cols-[minmax(0,1fr)_minmax(0,1fr)_auto_auto_auto_auto] sm:items-end">`);
			if (Field) {
				$$renderer.push("<!--[-->");
				Field($$renderer, {
					class: "gap-1",
					children: ($$renderer) => {
						if (Field_label) {
							$$renderer.push("<!--[-->");
							Field_label($$renderer, {
								for: `inline-token-type-${selectedToken.name}`,
								children: ($$renderer) => {
									$$renderer.push(`<span class="font-mono">${escape_html(selectedToken.name)}</span> type`);
								},
								$$slots: { default: true }
							});
							$$renderer.push("<!--]-->");
						} else {
							$$renderer.push("<!--[!-->");
							$$renderer.push("<!--]-->");
						}
						$$renderer.push(` `);
						if (Select) {
							$$renderer.push("<!--[-->");
							Select($$renderer, {
								type: "single",
								value: inputs[selectedToken.name]?.type ?? "string",
								onValueChange: (type) => onensureinput(selectedToken.name, type),
								children: ($$renderer) => {
									if (Select_trigger) {
										$$renderer.push("<!--[-->");
										Select_trigger($$renderer, {
											id: `inline-token-type-${selectedToken.name}`,
											class: "w-full",
											children: ($$renderer) => {
												$$renderer.push(`<!---->${escape_html(inputs[selectedToken.name]?.type ?? "string")}`);
											},
											$$slots: { default: true }
										});
										$$renderer.push("<!--]-->");
									} else {
										$$renderer.push("<!--[!-->");
										$$renderer.push("<!--]-->");
									}
									$$renderer.push(` `);
									if (Select_content) {
										$$renderer.push("<!--[-->");
										Select_content($$renderer, {
											children: ($$renderer) => {
												if (Select_group) {
													$$renderer.push("<!--[-->");
													Select_group($$renderer, {
														children: ($$renderer) => {
															$$renderer.push(`<!--[-->`);
															const each_array_2 = ensure_array_like(inputTypes);
															for (let $$index_2 = 0, $$length = each_array_2.length; $$index_2 < $$length; $$index_2++) {
																let type = each_array_2[$$index_2];
																if (Select_item) {
																	$$renderer.push("<!--[-->");
																	Select_item($$renderer, {
																		value: type,
																		label: type,
																		children: ($$renderer) => {
																			$$renderer.push(`<!---->${escape_html(type)}`);
																		},
																		$$slots: { default: true }
																	});
																	$$renderer.push("<!--]-->");
																} else {
																	$$renderer.push("<!--[!-->");
																	$$renderer.push("<!--]-->");
																}
															}
															$$renderer.push(`<!--]-->`);
														},
														$$slots: { default: true }
													});
													$$renderer.push("<!--]-->");
												} else {
													$$renderer.push("<!--[!-->");
													$$renderer.push("<!--]-->");
												}
											},
											$$slots: { default: true }
										});
										$$renderer.push("<!--]-->");
									} else {
										$$renderer.push("<!--[!-->");
										$$renderer.push("<!--]-->");
									}
								},
								$$slots: { default: true }
							});
							$$renderer.push("<!--]-->");
						} else {
							$$renderer.push("<!--[!-->");
							$$renderer.push("<!--]-->");
						}
					},
					$$slots: { default: true }
				});
				$$renderer.push("<!--]-->");
			} else {
				$$renderer.push("<!--[!-->");
				$$renderer.push("<!--]-->");
			}
			$$renderer.push(` `);
			if (Field) {
				$$renderer.push("<!--[-->");
				Field($$renderer, {
					class: "gap-1",
					children: ($$renderer) => {
						if (Field_label) {
							$$renderer.push("<!--[-->");
							Field_label($$renderer, {
								for: `inline-token-format-${selectedToken.name}`,
								children: ($$renderer) => {
									$$renderer.push(`<!---->Default format`);
								},
								$$slots: { default: true }
							});
							$$renderer.push("<!--]-->");
						} else {
							$$renderer.push("<!--[!-->");
							$$renderer.push("<!--]-->");
						}
						$$renderer.push(` `);
						Input($$renderer, {
							id: `inline-token-format-${selectedToken.name}`,
							value: inputs[selectedToken.name]?.format ?? "",
							placeholder: "Compiler default",
							oninput: (event) => onupdateformat(selectedToken.name, event.currentTarget.value)
						});
						$$renderer.push(`<!---->`);
					},
					$$slots: { default: true }
				});
				$$renderer.push("<!--]-->");
			} else {
				$$renderer.push("<!--[!-->");
				$$renderer.push("<!--]-->");
			}
			$$renderer.push(` `);
			Button($$renderer, {
				variant: "ghost",
				size: "icon",
				disabled: !canMoveSelected("earlier"),
				"aria-label": `Move ${selectedToken.name} earlier`,
				title: "Move variable earlier",
				onclick: () => moveSelected("earlier"),
				children: ($$renderer) => {
					Arrow_left($$renderer, {});
				},
				$$slots: { default: true }
			});
			$$renderer.push(`<!----> `);
			Button($$renderer, {
				variant: "ghost",
				size: "icon",
				disabled: !canMoveSelected("later"),
				"aria-label": `Move ${selectedToken.name} later`,
				title: "Move variable later",
				onclick: () => moveSelected("later"),
				children: ($$renderer) => {
					Arrow_right($$renderer, {});
				},
				$$slots: { default: true }
			});
			$$renderer.push(`<!----> `);
			Button($$renderer, {
				variant: "ghost",
				size: "icon",
				"aria-label": `Remove ${selectedToken.name} from this translation`,
				title: "Remove variable from this translation",
				onclick: removeSelectedToken,
				children: ($$renderer) => {
					Trash_2($$renderer, {});
				},
				$$slots: { default: true }
			});
			$$renderer.push(`<!----> `);
			Button($$renderer, {
				variant: "ghost",
				size: "icon",
				"aria-label": `Close ${selectedToken.name} settings`,
				title: "Close variable settings",
				onclick: () => selectedToken = void 0,
				children: ($$renderer) => {
					X($$renderer, {});
				},
				$$slots: { default: true }
			});
			$$renderer.push(`<!----></div>`);
		} else $$renderer.push("<!--[-1-->");
		$$renderer.push(`<!--]--></div>`);
	});
}
//#endregion
//#region src/lib/MessageComposer.svelte
function MessageComposer($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { value, locale, onchange } = $$props;
		let rawMode = false;
		let rawText = "";
		let rawError = void 0;
		let structureOpen = false;
		let exactCaseOpen = false;
		let exactCaseValue = "";
		let message = derived(() => toStructuredMessage(value));
		let inputNames = derived(() => Object.keys(message().inputs));
		let effectiveInputs = derived(() => {
			const names = new Set(Object.keys(message().inputs));
			for (const variant of message().variants) collectInputNames(patternNodes(variant.value), names);
			return Object.fromEntries([...names].map((name) => [name, message().inputs[name] ?? { type: "string" }]));
		});
		let declarationNames = derived(() => (message().declarations ?? []).map((item) => item.name));
		let primarySelector = derived(() => message().selectors[0]);
		let exactCaseMatch = derived(() => {
			const normalized = exactCaseValue.trim().replace(/^=/, "");
			if (normalized === "") return "";
			return primarySelector()?.function === "literal" ? normalized : `=${normalized}`;
		});
		let exactCaseDuplicate = derived(() => exactCaseMatch() !== "" && primarySelector() !== void 0 && message().variants.some((variant) => variant.match[primarySelector().name] === exactCaseMatch()));
		let availableCaseMatches = derived(() => {
			const selector = message().selectors[0];
			if (selector === void 0 || selector.function === "literal") return [];
			const used = new Set(message().variants.map((variant) => variant.match[selector.name]));
			return localePluralCategories(locale, selector.function === "ordinal").filter((category) => category !== "other" && !used.has(category));
		});
		function commit(action) {
			const next = structuredClone(message());
			action(next);
			onchange(synchronizeMatches(next));
		}
		function addInput(type = "string", preferredName = "value") {
			commit((next) => {
				const name = nextIdentifier(preferredName, Object.keys(next.inputs));
				next.inputs[name] = { type };
			});
		}
		function ensureInput(name, type) {
			commit((next) => {
				next.inputs[name] ??= { type };
				next.inputs[name].type = type;
			});
		}
		function updateInputFormat(name, format) {
			commit((next) => {
				next.inputs[name] ??= { type: "string" };
				if (format === "") delete next.inputs[name].format;
				else next.inputs[name].format = format;
			});
		}
		function removeInput(name) {
			commit((next) => {
				delete next.inputs[name];
				next.declarations = next.declarations?.filter((item) => item.input !== name);
				next.selectors = next.selectors.filter((item) => item.input !== name);
				scrubNodes(next, (node) => "input" in node && node.input === name || "format" in node && node.format.input === name);
			});
		}
		function addDeclaration() {
			commit((next) => {
				const name = nextIdentifier("formattedValue", (next.declarations ?? []).map((item) => item.name));
				const input = Object.keys(next.inputs).find((candidate) => next.inputs[candidate].type !== "bool") ?? "value";
				next.declarations ??= [];
				next.declarations.push({
					name,
					input,
					function: functionFor(next.inputs[input]?.type)
				});
			});
		}
		function functionFor(type) {
			return {
				int64: "integer",
				decimal: "number",
				date: "date",
				time: "time",
				instant: "datetime",
				uuid: "uuid"
			}[type ?? "string"] ?? "string";
		}
		function updateDeclaration(index, property, value) {
			commit((next) => {
				const declaration = next.declarations?.[index];
				if (declaration === void 0) return;
				if (property === "format" && value === "") delete declaration.format;
				else declaration[property] = value;
				if (property === "function" && value === "relativeTime") {
					declaration.unit = "day";
					declaration.numeric = "auto";
					delete declaration.format;
				} else if (property === "function") {
					delete declaration.unit;
					delete declaration.numeric;
					declaration.format ??= "plain";
				}
			});
		}
		function addSelector() {
			commit((next) => {
				const name = nextIdentifier("choice", next.selectors.map((item) => item.name));
				next.selectors.push({
					name,
					input: Object.keys(next.inputs)[0] ?? "value",
					function: "literal"
				});
			});
		}
		function enablePluralForms() {
			commit((next) => {
				let input = Object.keys(next.inputs).find((name) => next.inputs[name].type === "int64" || next.inputs[name].type === "decimal");
				if (input === void 0) {
					input = nextIdentifier("count", Object.keys(next.inputs));
					next.inputs[input] = { type: "int64" };
				}
				const name = nextIdentifier("quantity", next.selectors.map((item) => item.name));
				next.selectors.push({
					name,
					input,
					function: "plural"
				});
				const original = structuredClone(next.variants[0]?.value ?? "");
				next.variants = [{
					match: { [name]: "one" },
					value: original
				}, {
					match: { [name]: "*" },
					value: structuredClone(original)
				}];
			});
		}
		function addVariant(primaryMatch) {
			commit((next) => {
				const matches = Object.fromEntries(next.selectors.map((selector) => [selector.name, "*"]));
				const primarySelector = next.selectors[0];
				if (primarySelector !== void 0) matches[primarySelector.name] = primaryMatch;
				next.variants.splice(Math.max(0, next.variants.length - 1), 0, {
					match: matches,
					value: ""
				});
			});
		}
		function addExactCase() {
			if (exactCaseMatch() === "" || exactCaseDuplicate()) return;
			addVariant(exactCaseMatch());
			exactCaseValue = "";
			exactCaseOpen = false;
		}
		function updateMatch(variantIndex, selectorName, match) {
			commit((next) => next.variants[variantIndex].match[selectorName] = match || "*");
		}
		function editableText(value) {
			return typeof value === "string" ? value : patternText(value);
		}
		function openRaw() {
			rawText = JSON.stringify(message(), null, 2);
			rawError = void 0;
			rawMode = true;
		}
		function applyRaw() {
			try {
				const next = toStructuredMessage(JSON.parse(rawText));
				onchange(synchronizeMatches(next));
				rawMode = false;
				rawError = void 0;
			} catch (error) {
				rawError = error instanceof Error ? error.message : String(error);
			}
		}
		function variantTitle(index) {
			if (message().selectors.length === 0) return "Default translation";
			return message().selectors.map((selector) => matchLabel(selector, message().variants[index].match[selector.name] ?? "*")).join(" + ");
		}
		function variantActionLabel(index) {
			const title = variantTitle(index);
			return title.toLocaleLowerCase().endsWith("translation") ? title : `${title} translation`;
		}
		function matchLabel(selector, match) {
			if (match === "*") return selector.function === "literal" ? "Fallback" : "Other";
			if (match.startsWith("=")) return `Exactly ${match.slice(1)}`;
			return match.charAt(0).toLocaleUpperCase() + match.slice(1);
		}
		function conditionDescription(selector, match) {
			if (match === "*") return `Used for every ${selector.input} value without a more specific translation`;
			if (match.startsWith("=")) return `When ${selector.input} equals ${match.slice(1)}`;
			if (selector.function === "plural") return match === "one" ? `Used for the language’s singular form of ${selector.input}` : `Used for the language’s “${match}” number form of ${selector.input}`;
			if (selector.function === "ordinal") return `Used for the language’s “${match}” ordinal form of ${selector.input}`;
			return `When ${selector.input} is “${match}”`;
		}
		function variantDescription(index) {
			if (message().selectors.length === 0) return "Shown whenever this message is used.";
			return message().selectors.map((selector) => conditionDescription(selector, message().variants[index].match[selector.name] ?? "*")).join(" · ");
		}
		function updateVariantText(index, text) {
			if (value === void 0 || typeof value === "string") {
				onchange(text);
				return;
			}
			commit((next) => {
				next.variants[index].value = text;
				for (const node of patternNodes(text)) if (typeof node !== "string" && "input" in node) next.inputs[node.input] ??= { type: "string" };
			});
		}
		function collectInputNames(nodes, names) {
			for (const node of nodes) {
				if (typeof node === "string") continue;
				if ("input" in node) names.add(node.input);
				else if ("format" in node) names.add(node.format.input);
				else if ("markup" in node) collectInputNames(node.markup.children, names);
			}
		}
		function isFallback(index) {
			return message().selectors.length > 0 && message().selectors.every((selector) => message().variants[index].match[selector.name] === "*");
		}
		function localePluralCategories(targetLocale, ordinal) {
			try {
				return new Intl.PluralRules(targetLocale, ordinal ? { type: "ordinal" } : void 0).resolvedOptions().pluralCategories;
			} catch {
				return ["one", "other"];
			}
		}
		function selectorMatches(selector) {
			if (selector.function === "literal") return ["*"];
			return ["*", ...localePluralCategories(locale, selector.function === "ordinal")];
		}
		function scrubNodes(next, predicate) {
			const scrub = (nodes) => {
				const result = [];
				for (const node of nodes) if (typeof node === "string") result.push(node);
				else if (!predicate(node)) {
					if ("markup" in node) node.markup.children = scrub(node.markup.children);
					result.push(node);
				}
				return result;
			};
			for (const variant of next.variants) if (Array.isArray(variant.value)) variant.value = scrub(variant.value);
		}
		let $$settled = true;
		let $$inner_renderer;
		function $$render_inner($$renderer) {
			$$renderer.push(`<div class="grid gap-4"><header class="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between"><div class="grid gap-1"><div class="flex flex-wrap items-center gap-2"><h3 class="text-sm font-semibold">Translate the message</h3> `);
			if (message().selectors.some((selector) => selector.function === "plural")) {
				$$renderer.push("<!--[0-->");
				Badge($$renderer, {
					variant: "secondary",
					children: ($$renderer) => {
						$$renderer.push(`<!---->Plural message`);
					},
					$$slots: { default: true }
				});
			} else if (message().selectors.length > 0) {
				$$renderer.push("<!--[1-->");
				Badge($$renderer, {
					variant: "secondary",
					children: ($$renderer) => {
						$$renderer.push(`<!---->${escape_html(message().variants.length)} cases`);
					},
					$$slots: { default: true }
				});
			} else $$renderer.push("<!--[-1-->");
			$$renderer.push(`<!--]--></div> <p class="text-xs leading-relaxed text-muted-foreground">Write naturally. Variables such as <code class="svelte-1jz3g1o">{count}</code> become protected, inspectable chips in the sentence.</p></div> `);
			Button($$renderer, {
				variant: "outline",
				size: "sm",
				onclick: openRaw,
				children: ($$renderer) => {
					Code_xml($$renderer, { "data-icon": "inline-start" });
					$$renderer.push(`<!----> Message source`);
				},
				$$slots: { default: true }
			});
			$$renderer.push(`<!----></header> `);
			if (message().selectors.length === 0) {
				$$renderer.push("<!--[0-->");
				if (Card) {
					$$renderer.push("<!--[-->");
					Card($$renderer, {
						size: "sm",
						children: ($$renderer) => {
							if (Card_header) {
								$$renderer.push("<!--[-->");
								Card_header($$renderer, {
									children: ($$renderer) => {
										if (Card_title) {
											$$renderer.push("<!--[-->");
											Card_title($$renderer, {
												children: ($$renderer) => {
													$$renderer.push(`<!---->One translation is used in every situation`);
												},
												$$slots: { default: true }
											});
											$$renderer.push("<!--]-->");
										} else {
											$$renderer.push("<!--[!-->");
											$$renderer.push("<!--]-->");
										}
										$$renderer.push(` `);
										if (Card_description) {
											$$renderer.push("<!--[-->");
											Card_description($$renderer, {
												children: ($$renderer) => {
													$$renderer.push(`<!---->If wording changes with a number, add plural forms and translate each case separately.`);
												},
												$$slots: { default: true }
											});
											$$renderer.push("<!--]-->");
										} else {
											$$renderer.push("<!--[!-->");
											$$renderer.push("<!--]-->");
										}
										$$renderer.push(` `);
										if (Card_action) {
											$$renderer.push("<!--[-->");
											Card_action($$renderer, {
												children: ($$renderer) => {
													Button($$renderer, {
														variant: "outline",
														size: "sm",
														onclick: enablePluralForms,
														children: ($$renderer) => {
															Circle_plus($$renderer, { "data-icon": "inline-start" });
															$$renderer.push(`<!----> Add plural forms`);
														},
														$$slots: { default: true }
													});
												},
												$$slots: { default: true }
											});
											$$renderer.push("<!--]-->");
										} else {
											$$renderer.push("<!--[!-->");
											$$renderer.push("<!--]-->");
										}
									},
									$$slots: { default: true }
								});
								$$renderer.push("<!--]-->");
							} else {
								$$renderer.push("<!--[!-->");
								$$renderer.push("<!--]-->");
							}
						},
						$$slots: { default: true }
					});
					$$renderer.push("<!--]-->");
				} else {
					$$renderer.push("<!--[!-->");
					$$renderer.push("<!--]-->");
				}
			} else $$renderer.push("<!--[-1-->");
			$$renderer.push(`<!--]--> <div class="grid gap-3"><!--[-->`);
			const each_array = ensure_array_like(message().variants);
			for (let variantIndex = 0, $$length = each_array.length; variantIndex < $$length; variantIndex++) {
				let variant = each_array[variantIndex];
				if (Card) {
					$$renderer.push("<!--[-->");
					Card($$renderer, {
						size: "sm",
						children: ($$renderer) => {
							if (Card_header) {
								$$renderer.push("<!--[-->");
								Card_header($$renderer, {
									class: "gap-2",
									children: ($$renderer) => {
										$$renderer.push(`<div class="flex min-w-0 flex-wrap items-center gap-2">`);
										if (Card_title) {
											$$renderer.push("<!--[-->");
											Card_title($$renderer, {
												class: "font-serif text-lg",
												children: ($$renderer) => {
													$$renderer.push(`<!---->${escape_html(variantTitle(variantIndex))}`);
												},
												$$slots: { default: true }
											});
											$$renderer.push("<!--]-->");
										} else {
											$$renderer.push("<!--[!-->");
											$$renderer.push("<!--]-->");
										}
										$$renderer.push(` `);
										if (isFallback(variantIndex)) {
											$$renderer.push("<!--[0-->");
											Badge($$renderer, {
												variant: "secondary",
												children: ($$renderer) => {
													$$renderer.push(`<!---->Required fallback`);
												},
												$$slots: { default: true }
											});
										} else $$renderer.push("<!--[-1-->");
										$$renderer.push(`<!--]--> <!--[-->`);
										const each_array_1 = ensure_array_like(message().selectors);
										for (let $$index_1 = 0, $$length = each_array_1.length; $$index_1 < $$length; $$index_1++) {
											let selector = each_array_1[$$index_1];
											if (Popover) {
												$$renderer.push("<!--[-->");
												Popover($$renderer, {
													children: ($$renderer) => {
														if (Popover_trigger) {
															$$renderer.push("<!--[-->");
															Popover_trigger($$renderer, {
																class: buttonVariants({
																	variant: "outline",
																	size: "sm",
																	class: "h-7 rounded-full px-2 text-xs"
																}),
																children: ($$renderer) => {
																	$$renderer.push(`<!---->${escape_html(selector.input)}: ${escape_html(message().variants[variantIndex].match[selector.name] ?? "*")}`);
																},
																$$slots: { default: true }
															});
															$$renderer.push("<!--]-->");
														} else {
															$$renderer.push("<!--[!-->");
															$$renderer.push("<!--]-->");
														}
														$$renderer.push(` `);
														if (Popover_content) {
															$$renderer.push("<!--[-->");
															Popover_content($$renderer, {
																align: "start",
																class: "w-[calc(100vw-2rem)] max-w-80",
																children: ($$renderer) => {
																	if (Popover_header) {
																		$$renderer.push("<!--[-->");
																		Popover_header($$renderer, {
																			children: ($$renderer) => {
																				if (Popover_title) {
																					$$renderer.push("<!--[-->");
																					Popover_title($$renderer, {
																						children: ($$renderer) => {
																							$$renderer.push(`<!---->When is this translation used?`);
																						},
																						$$slots: { default: true }
																					});
																					$$renderer.push("<!--]-->");
																				} else {
																					$$renderer.push("<!--[!-->");
																					$$renderer.push("<!--]-->");
																				}
																				$$renderer.push(` `);
																				if (Popover_description) {
																					$$renderer.push("<!--[-->");
																					Popover_description($$renderer, {
																						children: ($$renderer) => {
																							$$renderer.push(`<!---->${escape_html(conditionDescription(selector, message().variants[variantIndex].match[selector.name] ?? "*"))}`);
																						},
																						$$slots: { default: true }
																					});
																					$$renderer.push("<!--]-->");
																				} else {
																					$$renderer.push("<!--[!-->");
																					$$renderer.push("<!--]-->");
																				}
																			},
																			$$slots: { default: true }
																		});
																		$$renderer.push("<!--]-->");
																	} else {
																		$$renderer.push("<!--[!-->");
																		$$renderer.push("<!--]-->");
																	}
																	$$renderer.push(` `);
																	if (isFallback(variantIndex)) {
																		$$renderer.push("<!--[0-->");
																		$$renderer.push(`<p class="text-sm text-muted-foreground">Every structured message needs this final fallback, so its matching rule cannot be changed or removed.</p>`);
																	} else {
																		$$renderer.push("<!--[-1-->");
																		if (selector.function === "plural" || selector.function === "ordinal") {
																			$$renderer.push("<!--[0-->");
																			if (Field) {
																				$$renderer.push("<!--[-->");
																				Field($$renderer, {
																					children: ($$renderer) => {
																						if (Field_label) {
																							$$renderer.push("<!--[-->");
																							Field_label($$renderer, {
																								for: `match-${variantIndex}-${selector.name}`,
																								children: ($$renderer) => {
																									$$renderer.push(`<!---->Number form`);
																								},
																								$$slots: { default: true }
																							});
																							$$renderer.push("<!--]-->");
																						} else {
																							$$renderer.push("<!--[!-->");
																							$$renderer.push("<!--]-->");
																						}
																						$$renderer.push(` `);
																						if (Select) {
																							$$renderer.push("<!--[-->");
																							Select($$renderer, {
																								type: "single",
																								value: message().variants[variantIndex].match[selector.name] ?? "*",
																								onValueChange: (match) => updateMatch(variantIndex, selector.name, match),
																								children: ($$renderer) => {
																									if (Select_trigger) {
																										$$renderer.push("<!--[-->");
																										Select_trigger($$renderer, {
																											id: `match-${variantIndex}-${selector.name}`,
																											class: "w-full",
																											children: ($$renderer) => {
																												$$renderer.push(`<!---->${escape_html(matchLabel(selector, message().variants[variantIndex].match[selector.name] ?? "*"))}`);
																											},
																											$$slots: { default: true }
																										});
																										$$renderer.push("<!--]-->");
																									} else {
																										$$renderer.push("<!--[!-->");
																										$$renderer.push("<!--]-->");
																									}
																									$$renderer.push(` `);
																									if (Select_content) {
																										$$renderer.push("<!--[-->");
																										Select_content($$renderer, {
																											children: ($$renderer) => {
																												if (Select_group) {
																													$$renderer.push("<!--[-->");
																													Select_group($$renderer, {
																														children: ($$renderer) => {
																															$$renderer.push(`<!--[-->`);
																															const each_array_2 = ensure_array_like(selectorMatches(selector).filter((match) => match !== "*"));
																															for (let $$index = 0, $$length = each_array_2.length; $$index < $$length; $$index++) {
																																let match = each_array_2[$$index];
																																if (Select_item) {
																																	$$renderer.push("<!--[-->");
																																	Select_item($$renderer, {
																																		value: match,
																																		label: match,
																																		children: ($$renderer) => {
																																			$$renderer.push(`<!---->${escape_html(match)}`);
																																		},
																																		$$slots: { default: true }
																																	});
																																	$$renderer.push("<!--]-->");
																																} else {
																																	$$renderer.push("<!--[!-->");
																																	$$renderer.push("<!--]-->");
																																}
																															}
																															$$renderer.push(`<!--]-->`);
																														},
																														$$slots: { default: true }
																													});
																													$$renderer.push("<!--]-->");
																												} else {
																													$$renderer.push("<!--[!-->");
																													$$renderer.push("<!--]-->");
																												}
																											},
																											$$slots: { default: true }
																										});
																										$$renderer.push("<!--]-->");
																									} else {
																										$$renderer.push("<!--[!-->");
																										$$renderer.push("<!--]-->");
																									}
																								},
																								$$slots: { default: true }
																							});
																							$$renderer.push("<!--]-->");
																						} else {
																							$$renderer.push("<!--[!-->");
																							$$renderer.push("<!--]-->");
																						}
																					},
																					$$slots: { default: true }
																				});
																				$$renderer.push("<!--]-->");
																			} else {
																				$$renderer.push("<!--[!-->");
																				$$renderer.push("<!--]-->");
																			}
																		} else $$renderer.push("<!--[-1-->");
																		$$renderer.push(`<!--]--> `);
																		if (Field) {
																			$$renderer.push("<!--[-->");
																			Field($$renderer, {
																				children: ($$renderer) => {
																					if (Field_label) {
																						$$renderer.push("<!--[-->");
																						Field_label($$renderer, {
																							for: `custom-match-${variantIndex}-${selector.name}`,
																							children: ($$renderer) => {
																								$$renderer.push(`<!---->Exact or custom match`);
																							},
																							$$slots: { default: true }
																						});
																						$$renderer.push("<!--]-->");
																					} else {
																						$$renderer.push("<!--[!-->");
																						$$renderer.push("<!--]-->");
																					}
																					$$renderer.push(` `);
																					Input($$renderer, {
																						id: `custom-match-${variantIndex}-${selector.name}`,
																						value: message().variants[variantIndex].match[selector.name] ?? "*",
																						placeholder: selector.function === "literal" ? "premium" : "=0",
																						onblur: (event) => updateMatch(variantIndex, selector.name, event.currentTarget.value)
																					});
																					$$renderer.push(`<!----> `);
																					if (Field_description) {
																						$$renderer.push("<!--[-->");
																						Field_description($$renderer, {
																							children: ($$renderer) => {
																								$$renderer.push(`<!---->Use <code class="svelte-1jz3g1o">=0</code> for an exact number.`);
																							},
																							$$slots: { default: true }
																						});
																						$$renderer.push("<!--]-->");
																					} else {
																						$$renderer.push("<!--[!-->");
																						$$renderer.push("<!--]-->");
																					}
																				},
																				$$slots: { default: true }
																			});
																			$$renderer.push("<!--]-->");
																		} else {
																			$$renderer.push("<!--[!-->");
																			$$renderer.push("<!--]-->");
																		}
																	}
																	$$renderer.push(`<!--]-->`);
																},
																$$slots: { default: true }
															});
															$$renderer.push("<!--]-->");
														} else {
															$$renderer.push("<!--[!-->");
															$$renderer.push("<!--]-->");
														}
													},
													$$slots: { default: true }
												});
												$$renderer.push("<!--]-->");
											} else {
												$$renderer.push("<!--[!-->");
												$$renderer.push("<!--]-->");
											}
										}
										$$renderer.push(`<!--]--></div> `);
										if (Card_description) {
											$$renderer.push("<!--[-->");
											Card_description($$renderer, {
												children: ($$renderer) => {
													$$renderer.push(`<!---->${escape_html(variantDescription(variantIndex))}`);
												},
												$$slots: { default: true }
											});
											$$renderer.push("<!--]-->");
										} else {
											$$renderer.push("<!--[!-->");
											$$renderer.push("<!--]-->");
										}
										$$renderer.push(` `);
										if (Card_action) {
											$$renderer.push("<!--[-->");
											Card_action($$renderer, {
												class: "flex gap-1",
												children: ($$renderer) => {
													Button($$renderer, {
														variant: "ghost",
														size: "icon-sm",
														"aria-label": `Move ${variantActionLabel(variantIndex)} up`,
														title: `Move ${variantTitle(variantIndex)} up`,
														disabled: variantIndex === 0 || isFallback(variantIndex),
														onclick: () => commit((next) => next.variants.splice(variantIndex - 1, 0, next.variants.splice(variantIndex, 1)[0])),
														children: ($$renderer) => {
															Arrow_up($$renderer, {});
														},
														$$slots: { default: true }
													});
													$$renderer.push(`<!----> `);
													Button($$renderer, {
														variant: "ghost",
														size: "icon-sm",
														"aria-label": `Move ${variantActionLabel(variantIndex)} down`,
														title: `Move ${variantTitle(variantIndex)} down`,
														disabled: variantIndex === message().variants.length - 1 || isFallback(variantIndex) || isFallback(variantIndex + 1),
														onclick: () => commit((next) => next.variants.splice(variantIndex + 1, 0, next.variants.splice(variantIndex, 1)[0])),
														children: ($$renderer) => {
															Arrow_down($$renderer, {});
														},
														$$slots: { default: true }
													});
													$$renderer.push(`<!----> `);
													Button($$renderer, {
														variant: "ghost",
														size: "icon-sm",
														"aria-label": `Remove ${variantActionLabel(variantIndex)}`,
														title: isFallback(variantIndex) ? "The fallback translation is required" : `Remove ${variantTitle(variantIndex)}`,
														disabled: message().variants.length === 1 || isFallback(variantIndex),
														onclick: () => commit((next) => next.variants.splice(variantIndex, 1)),
														children: ($$renderer) => {
															Trash_2($$renderer, {});
														},
														$$slots: { default: true }
													});
													$$renderer.push(`<!---->`);
												},
												$$slots: { default: true }
											});
											$$renderer.push("<!--]-->");
										} else {
											$$renderer.push("<!--[!-->");
											$$renderer.push("<!--]-->");
										}
									},
									$$slots: { default: true }
								});
								$$renderer.push("<!--]-->");
							} else {
								$$renderer.push("<!--[!-->");
								$$renderer.push("<!--]-->");
							}
							$$renderer.push(` `);
							if (Card_content) {
								$$renderer.push("<!--[-->");
								Card_content($$renderer, {
									class: "grid gap-3",
									children: ($$renderer) => {
										if (editableText(variant.value) !== void 0) {
											$$renderer.push("<!--[0-->");
											InlineMessageEditor($$renderer, {
												value: editableText(variant.value) ?? "",
												inputs: effectiveInputs(),
												label: `Translation for ${variantTitle(variantIndex)}`,
												onchange: (text) => updateVariantText(variantIndex, text),
												onensureinput: ensureInput,
												onupdateformat: updateInputFormat
											});
										} else {
											$$renderer.push("<!--[-1-->");
											$$renderer.push(`<p class="text-xs text-muted-foreground">This case contains formatting or semantic markup. Edit its content blocks below.</p> `);
											PatternEditor($$renderer, {
												nodes: variant.value,
												inputs: message().inputs,
												localNames: declarationNames(),
												onchange: (nodes) => commit((next) => next.variants[variantIndex].value = nodes)
											});
											$$renderer.push(`<!---->`);
										}
										$$renderer.push(`<!--]-->`);
									},
									$$slots: { default: true }
								});
								$$renderer.push("<!--]-->");
							} else {
								$$renderer.push("<!--[!-->");
								$$renderer.push("<!--]-->");
							}
						},
						$$slots: { default: true }
					});
					$$renderer.push("<!--]-->");
				} else {
					$$renderer.push("<!--[!-->");
					$$renderer.push("<!--]-->");
				}
			}
			$$renderer.push(`<!--]--></div> `);
			if (message().selectors.length > 0) {
				$$renderer.push("<!--[0-->");
				if (Dropdown_menu) {
					$$renderer.push("<!--[-->");
					Dropdown_menu($$renderer, {
						children: ($$renderer) => {
							{
								function child($$renderer, { props }) {
									Button($$renderer, spread_props([props, {
										variant: "outline",
										class: "justify-self-start",
										children: ($$renderer) => {
											Circle_plus($$renderer, { "data-icon": "inline-start" });
											$$renderer.push(`<!----> Add translation case`);
										},
										$$slots: { default: true }
									}]));
								}
								if (Dropdown_menu_trigger) {
									$$renderer.push("<!--[-->");
									Dropdown_menu_trigger($$renderer, {
										child,
										$$slots: { child: true }
									});
									$$renderer.push("<!--]-->");
								} else {
									$$renderer.push("<!--[!-->");
									$$renderer.push("<!--]-->");
								}
							}
							$$renderer.push(` `);
							if (Dropdown_menu_content) {
								$$renderer.push("<!--[-->");
								Dropdown_menu_content($$renderer, {
									align: "start",
									class: "w-64",
									children: ($$renderer) => {
										if (Dropdown_menu_label) {
											$$renderer.push("<!--[-->");
											Dropdown_menu_label($$renderer, {
												children: ($$renderer) => {
													$$renderer.push(`<!---->Choose when this translation is used`);
												},
												$$slots: { default: true }
											});
											$$renderer.push("<!--]-->");
										} else {
											$$renderer.push("<!--[!-->");
											$$renderer.push("<!--]-->");
										}
										$$renderer.push(` <!--[-->`);
										const each_array_3 = ensure_array_like(availableCaseMatches());
										for (let $$index_3 = 0, $$length = each_array_3.length; $$index_3 < $$length; $$index_3++) {
											let match = each_array_3[$$index_3];
											if (Dropdown_menu_item) {
												$$renderer.push("<!--[-->");
												Dropdown_menu_item($$renderer, {
													onclick: () => addVariant(match),
													children: ($$renderer) => {
														$$renderer.push(`<!---->${escape_html(match.charAt(0).toLocaleUpperCase() + match.slice(1))} ${escape_html(primarySelector()?.function === "ordinal" ? "ordinal" : "plural")} form`);
													},
													$$slots: { default: true }
												});
												$$renderer.push("<!--]-->");
											} else {
												$$renderer.push("<!--[!-->");
												$$renderer.push("<!--]-->");
											}
										}
										$$renderer.push(`<!--]--> `);
										if (Dropdown_menu_item) {
											$$renderer.push("<!--[-->");
											Dropdown_menu_item($$renderer, {
												onclick: () => exactCaseOpen = true,
												children: ($$renderer) => {
													$$renderer.push(`<!---->${escape_html(primarySelector()?.function === "literal" ? "Custom value…" : "Exact number…")}`);
												},
												$$slots: { default: true }
											});
											$$renderer.push("<!--]-->");
										} else {
											$$renderer.push("<!--[!-->");
											$$renderer.push("<!--]-->");
										}
									},
									$$slots: { default: true }
								});
								$$renderer.push("<!--]-->");
							} else {
								$$renderer.push("<!--[!-->");
								$$renderer.push("<!--]-->");
							}
						},
						$$slots: { default: true }
					});
					$$renderer.push("<!--]-->");
				} else {
					$$renderer.push("<!--[!-->");
					$$renderer.push("<!--]-->");
				}
			} else $$renderer.push("<!--[-1-->");
			$$renderer.push(`<!--]--> `);
			Separator($$renderer, {});
			$$renderer.push(`<!----> `);
			if (Collapsible) {
				$$renderer.push("<!--[-->");
				Collapsible($$renderer, {
					class: "group/structure rounded-3xl bg-card shadow-sm ring-1 ring-foreground/5 dark:ring-foreground/10",
					get open() {
						return structureOpen;
					},
					set open($$value) {
						structureOpen = $$value;
						$$settled = false;
					},
					children: ($$renderer) => {
						$$renderer.push(`<div class="flex items-center justify-between gap-3 px-4 py-3"><div class="grid gap-0.5"><strong class="text-sm">Advanced structure</strong> <span class="text-xs text-muted-foreground">Inputs, formatters, and selection rules used by the translations above.</span></div> `);
						if (Collapsible_trigger) {
							$$renderer.push("<!--[-->");
							Collapsible_trigger($$renderer, {
								class: buttonVariants({
									variant: "ghost",
									size: "icon-sm"
								}),
								"aria-label": "Toggle advanced structure",
								children: ($$renderer) => {
									Chevron_down($$renderer, { class: "transition-transform group-data-[state=open]/structure:rotate-180" });
								},
								$$slots: { default: true }
							});
							$$renderer.push("<!--]-->");
						} else {
							$$renderer.push("<!--[!-->");
							$$renderer.push("<!--]-->");
						}
						$$renderer.push(`</div> `);
						if (Collapsible_content) {
							$$renderer.push("<!--[-->");
							Collapsible_content($$renderer, {
								children: ($$renderer) => {
									Separator($$renderer, {});
									$$renderer.push(`<!----> <div class="grid gap-6 px-4 py-5">`);
									if (Field_set) {
										$$renderer.push("<!--[-->");
										Field_set($$renderer, {
											children: ($$renderer) => {
												if (Field_legend) {
													$$renderer.push("<!--[-->");
													Field_legend($$renderer, {
														variant: "label",
														children: ($$renderer) => {
															$$renderer.push(`<!---->Inputs`);
														},
														$$slots: { default: true }
													});
													$$renderer.push("<!--]-->");
												} else {
													$$renderer.push("<!--[!-->");
													$$renderer.push("<!--]-->");
												}
												$$renderer.push(` `);
												if (Field_description) {
													$$renderer.push("<!--[-->");
													Field_description($$renderer, {
														children: ($$renderer) => {
															$$renderer.push(`<!---->Values supplied by application code. Translators insert them by typing <code class="svelte-1jz3g1o">{name}</code>.`);
														},
														$$slots: { default: true }
													});
													$$renderer.push("<!--]-->");
												} else {
													$$renderer.push("<!--[!-->");
													$$renderer.push("<!--]-->");
												}
												$$renderer.push(` `);
												if (Field_group) {
													$$renderer.push("<!--[-->");
													Field_group($$renderer, {
														class: "gap-3",
														children: ($$renderer) => {
															$$renderer.push(`<!--[-->`);
															const each_array_4 = ensure_array_like(Object.entries(message().inputs));
															for (let $$index_5 = 0, $$length = each_array_4.length; $$index_5 < $$length; $$index_5++) {
																let [name, descriptor] = each_array_4[$$index_5];
																$$renderer.push(`<div class="grid gap-3 rounded-2xl bg-muted/50 p-3 sm:grid-cols-[minmax(0,1fr)_minmax(0,1fr)_minmax(0,1.25fr)_auto] sm:items-end">`);
																if (Field) {
																	$$renderer.push("<!--[-->");
																	Field($$renderer, {
																		children: ($$renderer) => {
																			if (Field_label) {
																				$$renderer.push("<!--[-->");
																				Field_label($$renderer, {
																					for: `input-name-${name}`,
																					children: ($$renderer) => {
																						$$renderer.push(`<!---->Name`);
																					},
																					$$slots: { default: true }
																				});
																				$$renderer.push("<!--]-->");
																			} else {
																				$$renderer.push("<!--[!-->");
																				$$renderer.push("<!--]-->");
																			}
																			$$renderer.push(` `);
																			Input($$renderer, {
																				id: `input-name-${name}`,
																				pattern: "[A-Za-z_][A-Za-z0-9_]*",
																				value: name,
																				onblur: (event) => onchange(renameInput(message(), name, event.currentTarget.value))
																			});
																			$$renderer.push(`<!---->`);
																		},
																		$$slots: { default: true }
																	});
																	$$renderer.push("<!--]-->");
																} else {
																	$$renderer.push("<!--[!-->");
																	$$renderer.push("<!--]-->");
																}
																$$renderer.push(` `);
																if (Field) {
																	$$renderer.push("<!--[-->");
																	Field($$renderer, {
																		children: ($$renderer) => {
																			if (Field_label) {
																				$$renderer.push("<!--[-->");
																				Field_label($$renderer, {
																					for: `input-type-${name}`,
																					children: ($$renderer) => {
																						$$renderer.push(`<!---->Type`);
																					},
																					$$slots: { default: true }
																				});
																				$$renderer.push("<!--]-->");
																			} else {
																				$$renderer.push("<!--[!-->");
																				$$renderer.push("<!--]-->");
																			}
																			$$renderer.push(` `);
																			if (Select) {
																				$$renderer.push("<!--[-->");
																				Select($$renderer, {
																					type: "single",
																					value: descriptor.type,
																					onValueChange: (type) => ensureInput(name, type),
																					children: ($$renderer) => {
																						if (Select_trigger) {
																							$$renderer.push("<!--[-->");
																							Select_trigger($$renderer, {
																								id: `input-type-${name}`,
																								class: "w-full",
																								children: ($$renderer) => {
																									$$renderer.push(`<!---->${escape_html(descriptor.type)}`);
																								},
																								$$slots: { default: true }
																							});
																							$$renderer.push("<!--]-->");
																						} else {
																							$$renderer.push("<!--[!-->");
																							$$renderer.push("<!--]-->");
																						}
																						$$renderer.push(` `);
																						if (Select_content) {
																							$$renderer.push("<!--[-->");
																							Select_content($$renderer, {
																								children: ($$renderer) => {
																									if (Select_group) {
																										$$renderer.push("<!--[-->");
																										Select_group($$renderer, {
																											children: ($$renderer) => {
																												$$renderer.push(`<!--[-->`);
																												const each_array_5 = ensure_array_like(inputTypes);
																												for (let $$index_4 = 0, $$length = each_array_5.length; $$index_4 < $$length; $$index_4++) {
																													let type = each_array_5[$$index_4];
																													if (Select_item) {
																														$$renderer.push("<!--[-->");
																														Select_item($$renderer, {
																															value: type,
																															label: type,
																															children: ($$renderer) => {
																																$$renderer.push(`<!---->${escape_html(type)}`);
																															},
																															$$slots: { default: true }
																														});
																														$$renderer.push("<!--]-->");
																													} else {
																														$$renderer.push("<!--[!-->");
																														$$renderer.push("<!--]-->");
																													}
																												}
																												$$renderer.push(`<!--]-->`);
																											},
																											$$slots: { default: true }
																										});
																										$$renderer.push("<!--]-->");
																									} else {
																										$$renderer.push("<!--[!-->");
																										$$renderer.push("<!--]-->");
																									}
																								},
																								$$slots: { default: true }
																							});
																							$$renderer.push("<!--]-->");
																						} else {
																							$$renderer.push("<!--[!-->");
																							$$renderer.push("<!--]-->");
																						}
																					},
																					$$slots: { default: true }
																				});
																				$$renderer.push("<!--]-->");
																			} else {
																				$$renderer.push("<!--[!-->");
																				$$renderer.push("<!--]-->");
																			}
																		},
																		$$slots: { default: true }
																	});
																	$$renderer.push("<!--]-->");
																} else {
																	$$renderer.push("<!--[!-->");
																	$$renderer.push("<!--]-->");
																}
																$$renderer.push(` `);
																if (Field) {
																	$$renderer.push("<!--[-->");
																	Field($$renderer, {
																		children: ($$renderer) => {
																			if (Field_label) {
																				$$renderer.push("<!--[-->");
																				Field_label($$renderer, {
																					for: `input-format-${name}`,
																					children: ($$renderer) => {
																						$$renderer.push(`<!---->Default format`);
																					},
																					$$slots: { default: true }
																				});
																				$$renderer.push("<!--]-->");
																			} else {
																				$$renderer.push("<!--[!-->");
																				$$renderer.push("<!--]-->");
																			}
																			$$renderer.push(` `);
																			Input($$renderer, {
																				id: `input-format-${name}`,
																				value: descriptor.format ?? "",
																				placeholder: "Compiler default",
																				oninput: (event) => updateInputFormat(name, event.currentTarget.value)
																			});
																			$$renderer.push(`<!---->`);
																		},
																		$$slots: { default: true }
																	});
																	$$renderer.push("<!--]-->");
																} else {
																	$$renderer.push("<!--[!-->");
																	$$renderer.push("<!--]-->");
																}
																$$renderer.push(` `);
																Button($$renderer, {
																	variant: "ghost",
																	size: "icon",
																	"aria-label": `Remove input ${name}`,
																	onclick: () => removeInput(name),
																	children: ($$renderer) => {
																		Trash_2($$renderer, {});
																	},
																	$$slots: { default: true }
																});
																$$renderer.push(`<!----></div>`);
															}
															$$renderer.push(`<!--]-->`);
														},
														$$slots: { default: true }
													});
													$$renderer.push("<!--]-->");
												} else {
													$$renderer.push("<!--[!-->");
													$$renderer.push("<!--]-->");
												}
												$$renderer.push(` `);
												Button($$renderer, {
													variant: "outline",
													class: "justify-self-start",
													onclick: () => addInput(),
													children: ($$renderer) => {
														Circle_plus($$renderer, { "data-icon": "inline-start" });
														$$renderer.push(`<!---->Add input`);
													},
													$$slots: { default: true }
												});
												$$renderer.push(`<!---->`);
											},
											$$slots: { default: true }
										});
										$$renderer.push("<!--]-->");
									} else {
										$$renderer.push("<!--[!-->");
										$$renderer.push("<!--]-->");
									}
									$$renderer.push(` `);
									if (Field_set) {
										$$renderer.push("<!--[-->");
										Field_set($$renderer, {
											children: ($$renderer) => {
												if (Field_legend) {
													$$renderer.push("<!--[-->");
													Field_legend($$renderer, {
														variant: "label",
														children: ($$renderer) => {
															$$renderer.push(`<!---->Selection rules`);
														},
														$$slots: { default: true }
													});
													$$renderer.push("<!--]-->");
												} else {
													$$renderer.push("<!--[!-->");
													$$renderer.push("<!--]-->");
												}
												$$renderer.push(` `);
												if (Field_description) {
													$$renderer.push("<!--[-->");
													Field_description($$renderer, {
														children: ($$renderer) => {
															$$renderer.push(`<!---->Rules decide which translation case is used for a given input.`);
														},
														$$slots: { default: true }
													});
													$$renderer.push("<!--]-->");
												} else {
													$$renderer.push("<!--[!-->");
													$$renderer.push("<!--]-->");
												}
												$$renderer.push(` `);
												if (Field_group) {
													$$renderer.push("<!--[-->");
													Field_group($$renderer, {
														class: "gap-3",
														children: ($$renderer) => {
															$$renderer.push(`<!--[-->`);
															const each_array_6 = ensure_array_like(message().selectors);
															for (let index = 0, $$length = each_array_6.length; index < $$length; index++) {
																let selector = each_array_6[index];
																$$renderer.push(`<div class="grid gap-3 rounded-2xl bg-muted/50 p-3 sm:grid-cols-3 sm:items-end">`);
																if (Field) {
																	$$renderer.push("<!--[-->");
																	Field($$renderer, {
																		children: ($$renderer) => {
																			if (Field_label) {
																				$$renderer.push("<!--[-->");
																				Field_label($$renderer, {
																					for: `selector-name-${selector.name}`,
																					children: ($$renderer) => {
																						$$renderer.push(`<!---->Rule name`);
																					},
																					$$slots: { default: true }
																				});
																				$$renderer.push("<!--]-->");
																			} else {
																				$$renderer.push("<!--[!-->");
																				$$renderer.push("<!--]-->");
																			}
																			Input($$renderer, {
																				id: `selector-name-${selector.name}`,
																				value: selector.name,
																				onblur: (event) => onchange(renameSelector(message(), selector.name, event.currentTarget.value))
																			});
																			$$renderer.push(`<!---->`);
																		},
																		$$slots: { default: true }
																	});
																	$$renderer.push("<!--]-->");
																} else {
																	$$renderer.push("<!--[!-->");
																	$$renderer.push("<!--]-->");
																}
																$$renderer.push(` `);
																if (Field) {
																	$$renderer.push("<!--[-->");
																	Field($$renderer, {
																		children: ($$renderer) => {
																			if (Field_label) {
																				$$renderer.push("<!--[-->");
																				Field_label($$renderer, {
																					for: `selector-input-${selector.name}`,
																					children: ($$renderer) => {
																						$$renderer.push(`<!---->Uses input`);
																					},
																					$$slots: { default: true }
																				});
																				$$renderer.push("<!--]-->");
																			} else {
																				$$renderer.push("<!--[!-->");
																				$$renderer.push("<!--]-->");
																			}
																			$$renderer.push(` `);
																			if (Select) {
																				$$renderer.push("<!--[-->");
																				Select($$renderer, {
																					type: "single",
																					value: selector.input,
																					onValueChange: (input) => commit((next) => next.selectors[index].input = input),
																					children: ($$renderer) => {
																						if (Select_trigger) {
																							$$renderer.push("<!--[-->");
																							Select_trigger($$renderer, {
																								id: `selector-input-${selector.name}`,
																								class: "w-full",
																								children: ($$renderer) => {
																									$$renderer.push(`<!---->${escape_html(selector.input)}`);
																								},
																								$$slots: { default: true }
																							});
																							$$renderer.push("<!--]-->");
																						} else {
																							$$renderer.push("<!--[!-->");
																							$$renderer.push("<!--]-->");
																						}
																						$$renderer.push(` `);
																						if (Select_content) {
																							$$renderer.push("<!--[-->");
																							Select_content($$renderer, {
																								children: ($$renderer) => {
																									if (Select_group) {
																										$$renderer.push("<!--[-->");
																										Select_group($$renderer, {
																											children: ($$renderer) => {
																												$$renderer.push(`<!--[-->`);
																												const each_array_7 = ensure_array_like(inputNames());
																												for (let $$index_6 = 0, $$length = each_array_7.length; $$index_6 < $$length; $$index_6++) {
																													let name = each_array_7[$$index_6];
																													if (Select_item) {
																														$$renderer.push("<!--[-->");
																														Select_item($$renderer, {
																															value: name,
																															label: name,
																															children: ($$renderer) => {
																																$$renderer.push(`<!---->${escape_html(name)}`);
																															},
																															$$slots: { default: true }
																														});
																														$$renderer.push("<!--]-->");
																													} else {
																														$$renderer.push("<!--[!-->");
																														$$renderer.push("<!--]-->");
																													}
																												}
																												$$renderer.push(`<!--]-->`);
																											},
																											$$slots: { default: true }
																										});
																										$$renderer.push("<!--]-->");
																									} else {
																										$$renderer.push("<!--[!-->");
																										$$renderer.push("<!--]-->");
																									}
																								},
																								$$slots: { default: true }
																							});
																							$$renderer.push("<!--]-->");
																						} else {
																							$$renderer.push("<!--[!-->");
																							$$renderer.push("<!--]-->");
																						}
																					},
																					$$slots: { default: true }
																				});
																				$$renderer.push("<!--]-->");
																			} else {
																				$$renderer.push("<!--[!-->");
																				$$renderer.push("<!--]-->");
																			}
																		},
																		$$slots: { default: true }
																	});
																	$$renderer.push("<!--]-->");
																} else {
																	$$renderer.push("<!--[!-->");
																	$$renderer.push("<!--]-->");
																}
																$$renderer.push(` <div class="flex items-end gap-2">`);
																if (Field) {
																	$$renderer.push("<!--[-->");
																	Field($$renderer, {
																		children: ($$renderer) => {
																			if (Field_label) {
																				$$renderer.push("<!--[-->");
																				Field_label($$renderer, {
																					for: `selector-function-${selector.name}`,
																					children: ($$renderer) => {
																						$$renderer.push(`<!---->Chooses by`);
																					},
																					$$slots: { default: true }
																				});
																				$$renderer.push("<!--]-->");
																			} else {
																				$$renderer.push("<!--[!-->");
																				$$renderer.push("<!--]-->");
																			}
																			$$renderer.push(` `);
																			if (Select) {
																				$$renderer.push("<!--[-->");
																				Select($$renderer, {
																					type: "single",
																					value: selector.function,
																					onValueChange: (fn) => commit((next) => next.selectors[index].function = fn),
																					children: ($$renderer) => {
																						if (Select_trigger) {
																							$$renderer.push("<!--[-->");
																							Select_trigger($$renderer, {
																								id: `selector-function-${selector.name}`,
																								class: "w-full",
																								children: ($$renderer) => {
																									$$renderer.push(`<!---->${escape_html(selector.function)}`);
																								},
																								$$slots: { default: true }
																							});
																							$$renderer.push("<!--]-->");
																						} else {
																							$$renderer.push("<!--[!-->");
																							$$renderer.push("<!--]-->");
																						}
																						$$renderer.push(` `);
																						if (Select_content) {
																							$$renderer.push("<!--[-->");
																							Select_content($$renderer, {
																								children: ($$renderer) => {
																									if (Select_group) {
																										$$renderer.push("<!--[-->");
																										Select_group($$renderer, {
																											children: ($$renderer) => {
																												$$renderer.push(`<!--[-->`);
																												const each_array_8 = ensure_array_like(selectorFunctions);
																												for (let $$index_7 = 0, $$length = each_array_8.length; $$index_7 < $$length; $$index_7++) {
																													let fn = each_array_8[$$index_7];
																													if (Select_item) {
																														$$renderer.push("<!--[-->");
																														Select_item($$renderer, {
																															value: fn,
																															label: fn,
																															children: ($$renderer) => {
																																$$renderer.push(`<!---->${escape_html(fn)}`);
																															},
																															$$slots: { default: true }
																														});
																														$$renderer.push("<!--]-->");
																													} else {
																														$$renderer.push("<!--[!-->");
																														$$renderer.push("<!--]-->");
																													}
																												}
																												$$renderer.push(`<!--]-->`);
																											},
																											$$slots: { default: true }
																										});
																										$$renderer.push("<!--]-->");
																									} else {
																										$$renderer.push("<!--[!-->");
																										$$renderer.push("<!--]-->");
																									}
																								},
																								$$slots: { default: true }
																							});
																							$$renderer.push("<!--]-->");
																						} else {
																							$$renderer.push("<!--[!-->");
																							$$renderer.push("<!--]-->");
																						}
																					},
																					$$slots: { default: true }
																				});
																				$$renderer.push("<!--]-->");
																			} else {
																				$$renderer.push("<!--[!-->");
																				$$renderer.push("<!--]-->");
																			}
																		},
																		$$slots: { default: true }
																	});
																	$$renderer.push("<!--]-->");
																} else {
																	$$renderer.push("<!--[!-->");
																	$$renderer.push("<!--]-->");
																}
																$$renderer.push(` `);
																Button($$renderer, {
																	variant: "ghost",
																	size: "icon",
																	"aria-label": `Remove selector ${selector.name}`,
																	onclick: () => commit((next) => next.selectors.splice(index, 1)),
																	children: ($$renderer) => {
																		Trash_2($$renderer, {});
																	},
																	$$slots: { default: true }
																});
																$$renderer.push(`<!----></div></div>`);
															}
															$$renderer.push(`<!--]-->`);
														},
														$$slots: { default: true }
													});
													$$renderer.push("<!--]-->");
												} else {
													$$renderer.push("<!--[!-->");
													$$renderer.push("<!--]-->");
												}
												$$renderer.push(` `);
												Button($$renderer, {
													variant: "outline",
													class: "justify-self-start",
													disabled: inputNames().length === 0,
													onclick: addSelector,
													children: ($$renderer) => {
														Circle_plus($$renderer, { "data-icon": "inline-start" });
														$$renderer.push(`<!---->Add selection rule`);
													},
													$$slots: { default: true }
												});
												$$renderer.push(`<!---->`);
											},
											$$slots: { default: true }
										});
										$$renderer.push("<!--]-->");
									} else {
										$$renderer.push("<!--[!-->");
										$$renderer.push("<!--]-->");
									}
									$$renderer.push(` `);
									if (Field_set) {
										$$renderer.push("<!--[-->");
										Field_set($$renderer, {
											children: ($$renderer) => {
												if (Field_legend) {
													$$renderer.push("<!--[-->");
													Field_legend($$renderer, {
														variant: "label",
														children: ($$renderer) => {
															$$renderer.push(`<!---->Reusable formatters`);
														},
														$$slots: { default: true }
													});
													$$renderer.push("<!--]-->");
												} else {
													$$renderer.push("<!--[!-->");
													$$renderer.push("<!--]-->");
												}
												$$renderer.push(` `);
												if (Field_description) {
													$$renderer.push("<!--[-->");
													Field_description($$renderer, {
														children: ($$renderer) => {
															$$renderer.push(`<!---->Optional named formats for dates, numbers, relative time, and other typed values.`);
														},
														$$slots: { default: true }
													});
													$$renderer.push("<!--]-->");
												} else {
													$$renderer.push("<!--[!-->");
													$$renderer.push("<!--]-->");
												}
												$$renderer.push(` `);
												if (Field_group) {
													$$renderer.push("<!--[-->");
													Field_group($$renderer, {
														class: "gap-3",
														children: ($$renderer) => {
															$$renderer.push(`<!--[-->`);
															const each_array_9 = ensure_array_like(message().declarations ?? []);
															for (let index = 0, $$length = each_array_9.length; index < $$length; index++) {
																let declaration = each_array_9[index];
																$$renderer.push(`<div class="grid gap-3 rounded-2xl bg-muted/50 p-3 sm:grid-cols-2 lg:grid-cols-4">`);
																if (Field) {
																	$$renderer.push("<!--[-->");
																	Field($$renderer, {
																		children: ($$renderer) => {
																			if (Field_label) {
																				$$renderer.push("<!--[-->");
																				Field_label($$renderer, {
																					for: `declaration-name-${declaration.name}`,
																					children: ($$renderer) => {
																						$$renderer.push(`<!---->Name`);
																					},
																					$$slots: { default: true }
																				});
																				$$renderer.push("<!--]-->");
																			} else {
																				$$renderer.push("<!--[!-->");
																				$$renderer.push("<!--]-->");
																			}
																			Input($$renderer, {
																				id: `declaration-name-${declaration.name}`,
																				value: declaration.name,
																				onblur: (event) => onchange(renameDeclaration(message(), declaration.name, event.currentTarget.value))
																			});
																			$$renderer.push(`<!---->`);
																		},
																		$$slots: { default: true }
																	});
																	$$renderer.push("<!--]-->");
																} else {
																	$$renderer.push("<!--[!-->");
																	$$renderer.push("<!--]-->");
																}
																$$renderer.push(` `);
																if (Field) {
																	$$renderer.push("<!--[-->");
																	Field($$renderer, {
																		children: ($$renderer) => {
																			if (Field_label) {
																				$$renderer.push("<!--[-->");
																				Field_label($$renderer, {
																					for: `declaration-input-${declaration.name}`,
																					children: ($$renderer) => {
																						$$renderer.push(`<!---->Input`);
																					},
																					$$slots: { default: true }
																				});
																				$$renderer.push("<!--]-->");
																			} else {
																				$$renderer.push("<!--[!-->");
																				$$renderer.push("<!--]-->");
																			}
																			$$renderer.push(` `);
																			if (Select) {
																				$$renderer.push("<!--[-->");
																				Select($$renderer, {
																					type: "single",
																					value: declaration.input,
																					onValueChange: (input) => updateDeclaration(index, "input", input),
																					children: ($$renderer) => {
																						if (Select_trigger) {
																							$$renderer.push("<!--[-->");
																							Select_trigger($$renderer, {
																								id: `declaration-input-${declaration.name}`,
																								class: "w-full",
																								children: ($$renderer) => {
																									$$renderer.push(`<!---->${escape_html(declaration.input)}`);
																								},
																								$$slots: { default: true }
																							});
																							$$renderer.push("<!--]-->");
																						} else {
																							$$renderer.push("<!--[!-->");
																							$$renderer.push("<!--]-->");
																						}
																						if (Select_content) {
																							$$renderer.push("<!--[-->");
																							Select_content($$renderer, {
																								children: ($$renderer) => {
																									if (Select_group) {
																										$$renderer.push("<!--[-->");
																										Select_group($$renderer, {
																											children: ($$renderer) => {
																												$$renderer.push(`<!--[-->`);
																												const each_array_10 = ensure_array_like(inputNames());
																												for (let $$index_9 = 0, $$length = each_array_10.length; $$index_9 < $$length; $$index_9++) {
																													let name = each_array_10[$$index_9];
																													if (Select_item) {
																														$$renderer.push("<!--[-->");
																														Select_item($$renderer, {
																															value: name,
																															label: name,
																															children: ($$renderer) => {
																																$$renderer.push(`<!---->${escape_html(name)}`);
																															},
																															$$slots: { default: true }
																														});
																														$$renderer.push("<!--]-->");
																													} else {
																														$$renderer.push("<!--[!-->");
																														$$renderer.push("<!--]-->");
																													}
																												}
																												$$renderer.push(`<!--]-->`);
																											},
																											$$slots: { default: true }
																										});
																										$$renderer.push("<!--]-->");
																									} else {
																										$$renderer.push("<!--[!-->");
																										$$renderer.push("<!--]-->");
																									}
																								},
																								$$slots: { default: true }
																							});
																							$$renderer.push("<!--]-->");
																						} else {
																							$$renderer.push("<!--[!-->");
																							$$renderer.push("<!--]-->");
																						}
																					},
																					$$slots: { default: true }
																				});
																				$$renderer.push("<!--]-->");
																			} else {
																				$$renderer.push("<!--[!-->");
																				$$renderer.push("<!--]-->");
																			}
																		},
																		$$slots: { default: true }
																	});
																	$$renderer.push("<!--]-->");
																} else {
																	$$renderer.push("<!--[!-->");
																	$$renderer.push("<!--]-->");
																}
																$$renderer.push(` `);
																if (Field) {
																	$$renderer.push("<!--[-->");
																	Field($$renderer, {
																		children: ($$renderer) => {
																			if (Field_label) {
																				$$renderer.push("<!--[-->");
																				Field_label($$renderer, {
																					for: `declaration-function-${declaration.name}`,
																					children: ($$renderer) => {
																						$$renderer.push(`<!---->Formatter`);
																					},
																					$$slots: { default: true }
																				});
																				$$renderer.push("<!--]-->");
																			} else {
																				$$renderer.push("<!--[!-->");
																				$$renderer.push("<!--]-->");
																			}
																			$$renderer.push(` `);
																			if (Select) {
																				$$renderer.push("<!--[-->");
																				Select($$renderer, {
																					type: "single",
																					value: declaration.function,
																					onValueChange: (fn) => updateDeclaration(index, "function", fn),
																					children: ($$renderer) => {
																						if (Select_trigger) {
																							$$renderer.push("<!--[-->");
																							Select_trigger($$renderer, {
																								id: `declaration-function-${declaration.name}`,
																								class: "w-full",
																								children: ($$renderer) => {
																									$$renderer.push(`<!---->${escape_html(declaration.function)}`);
																								},
																								$$slots: { default: true }
																							});
																							$$renderer.push("<!--]-->");
																						} else {
																							$$renderer.push("<!--[!-->");
																							$$renderer.push("<!--]-->");
																						}
																						if (Select_content) {
																							$$renderer.push("<!--[-->");
																							Select_content($$renderer, {
																								children: ($$renderer) => {
																									if (Select_group) {
																										$$renderer.push("<!--[-->");
																										Select_group($$renderer, {
																											children: ($$renderer) => {
																												$$renderer.push(`<!--[-->`);
																												const each_array_11 = ensure_array_like(formatFunctions);
																												for (let $$index_10 = 0, $$length = each_array_11.length; $$index_10 < $$length; $$index_10++) {
																													let fn = each_array_11[$$index_10];
																													if (Select_item) {
																														$$renderer.push("<!--[-->");
																														Select_item($$renderer, {
																															value: fn,
																															label: fn,
																															children: ($$renderer) => {
																																$$renderer.push(`<!---->${escape_html(fn)}`);
																															},
																															$$slots: { default: true }
																														});
																														$$renderer.push("<!--]-->");
																													} else {
																														$$renderer.push("<!--[!-->");
																														$$renderer.push("<!--]-->");
																													}
																												}
																												$$renderer.push(`<!--]-->`);
																											},
																											$$slots: { default: true }
																										});
																										$$renderer.push("<!--]-->");
																									} else {
																										$$renderer.push("<!--[!-->");
																										$$renderer.push("<!--]-->");
																									}
																								},
																								$$slots: { default: true }
																							});
																							$$renderer.push("<!--]-->");
																						} else {
																							$$renderer.push("<!--[!-->");
																							$$renderer.push("<!--]-->");
																						}
																					},
																					$$slots: { default: true }
																				});
																				$$renderer.push("<!--]-->");
																			} else {
																				$$renderer.push("<!--[!-->");
																				$$renderer.push("<!--]-->");
																			}
																		},
																		$$slots: { default: true }
																	});
																	$$renderer.push("<!--]-->");
																} else {
																	$$renderer.push("<!--[!-->");
																	$$renderer.push("<!--]-->");
																}
																$$renderer.push(` <div class="flex items-end gap-2">`);
																if (declaration.function === "relativeTime") {
																	$$renderer.push("<!--[0-->");
																	if (Field) {
																		$$renderer.push("<!--[-->");
																		Field($$renderer, {
																			children: ($$renderer) => {
																				if (Field_label) {
																					$$renderer.push("<!--[-->");
																					Field_label($$renderer, {
																						for: `declaration-unit-${declaration.name}`,
																						children: ($$renderer) => {
																							$$renderer.push(`<!---->Unit`);
																						},
																						$$slots: { default: true }
																					});
																					$$renderer.push("<!--]-->");
																				} else {
																					$$renderer.push("<!--[!-->");
																					$$renderer.push("<!--]-->");
																				}
																				$$renderer.push(` `);
																				if (Select) {
																					$$renderer.push("<!--[-->");
																					Select($$renderer, {
																						type: "single",
																						value: declaration.unit ?? "day",
																						onValueChange: (unit) => updateDeclaration(index, "unit", unit),
																						children: ($$renderer) => {
																							if (Select_trigger) {
																								$$renderer.push("<!--[-->");
																								Select_trigger($$renderer, {
																									id: `declaration-unit-${declaration.name}`,
																									class: "w-full",
																									children: ($$renderer) => {
																										$$renderer.push(`<!---->${escape_html(declaration.unit ?? "day")}`);
																									},
																									$$slots: { default: true }
																								});
																								$$renderer.push("<!--]-->");
																							} else {
																								$$renderer.push("<!--[!-->");
																								$$renderer.push("<!--]-->");
																							}
																							if (Select_content) {
																								$$renderer.push("<!--[-->");
																								Select_content($$renderer, {
																									children: ($$renderer) => {
																										if (Select_group) {
																											$$renderer.push("<!--[-->");
																											Select_group($$renderer, {
																												children: ($$renderer) => {
																													$$renderer.push(`<!--[-->`);
																													const each_array_12 = ensure_array_like(relativeTimeUnits);
																													for (let $$index_11 = 0, $$length = each_array_12.length; $$index_11 < $$length; $$index_11++) {
																														let unit = each_array_12[$$index_11];
																														if (Select_item) {
																															$$renderer.push("<!--[-->");
																															Select_item($$renderer, {
																																value: unit,
																																label: unit,
																																children: ($$renderer) => {
																																	$$renderer.push(`<!---->${escape_html(unit)}`);
																																},
																																$$slots: { default: true }
																															});
																															$$renderer.push("<!--]-->");
																														} else {
																															$$renderer.push("<!--[!-->");
																															$$renderer.push("<!--]-->");
																														}
																													}
																													$$renderer.push(`<!--]-->`);
																												},
																												$$slots: { default: true }
																											});
																											$$renderer.push("<!--]-->");
																										} else {
																											$$renderer.push("<!--[!-->");
																											$$renderer.push("<!--]-->");
																										}
																									},
																									$$slots: { default: true }
																								});
																								$$renderer.push("<!--]-->");
																							} else {
																								$$renderer.push("<!--[!-->");
																								$$renderer.push("<!--]-->");
																							}
																						},
																						$$slots: { default: true }
																					});
																					$$renderer.push("<!--]-->");
																				} else {
																					$$renderer.push("<!--[!-->");
																					$$renderer.push("<!--]-->");
																				}
																			},
																			$$slots: { default: true }
																		});
																		$$renderer.push("<!--]-->");
																	} else {
																		$$renderer.push("<!--[!-->");
																		$$renderer.push("<!--]-->");
																	}
																	$$renderer.push(` `);
																	if (Field) {
																		$$renderer.push("<!--[-->");
																		Field($$renderer, {
																			children: ($$renderer) => {
																				if (Field_label) {
																					$$renderer.push("<!--[-->");
																					Field_label($$renderer, {
																						for: `declaration-numeric-${declaration.name}`,
																						children: ($$renderer) => {
																							$$renderer.push(`<!---->Numeric`);
																						},
																						$$slots: { default: true }
																					});
																					$$renderer.push("<!--]-->");
																				} else {
																					$$renderer.push("<!--[!-->");
																					$$renderer.push("<!--]-->");
																				}
																				$$renderer.push(` `);
																				if (Select) {
																					$$renderer.push("<!--[-->");
																					Select($$renderer, {
																						type: "single",
																						value: declaration.numeric ?? "auto",
																						onValueChange: (numeric) => updateDeclaration(index, "numeric", numeric),
																						children: ($$renderer) => {
																							if (Select_trigger) {
																								$$renderer.push("<!--[-->");
																								Select_trigger($$renderer, {
																									id: `declaration-numeric-${declaration.name}`,
																									class: "w-full",
																									children: ($$renderer) => {
																										$$renderer.push(`<!---->${escape_html(declaration.numeric ?? "auto")}`);
																									},
																									$$slots: { default: true }
																								});
																								$$renderer.push("<!--]-->");
																							} else {
																								$$renderer.push("<!--[!-->");
																								$$renderer.push("<!--]-->");
																							}
																							if (Select_content) {
																								$$renderer.push("<!--[-->");
																								Select_content($$renderer, {
																									children: ($$renderer) => {
																										if (Select_group) {
																											$$renderer.push("<!--[-->");
																											Select_group($$renderer, {
																												children: ($$renderer) => {
																													if (Select_item) {
																														$$renderer.push("<!--[-->");
																														Select_item($$renderer, {
																															value: "auto",
																															label: "auto",
																															children: ($$renderer) => {
																																$$renderer.push(`<!---->auto`);
																															},
																															$$slots: { default: true }
																														});
																														$$renderer.push("<!--]-->");
																													} else {
																														$$renderer.push("<!--[!-->");
																														$$renderer.push("<!--]-->");
																													}
																													if (Select_item) {
																														$$renderer.push("<!--[-->");
																														Select_item($$renderer, {
																															value: "always",
																															label: "always",
																															children: ($$renderer) => {
																																$$renderer.push(`<!---->always`);
																															},
																															$$slots: { default: true }
																														});
																														$$renderer.push("<!--]-->");
																													} else {
																														$$renderer.push("<!--[!-->");
																														$$renderer.push("<!--]-->");
																													}
																												},
																												$$slots: { default: true }
																											});
																											$$renderer.push("<!--]-->");
																										} else {
																											$$renderer.push("<!--[!-->");
																											$$renderer.push("<!--]-->");
																										}
																									},
																									$$slots: { default: true }
																								});
																								$$renderer.push("<!--]-->");
																							} else {
																								$$renderer.push("<!--[!-->");
																								$$renderer.push("<!--]-->");
																							}
																						},
																						$$slots: { default: true }
																					});
																					$$renderer.push("<!--]-->");
																				} else {
																					$$renderer.push("<!--[!-->");
																					$$renderer.push("<!--]-->");
																				}
																			},
																			$$slots: { default: true }
																		});
																		$$renderer.push("<!--]-->");
																	} else {
																		$$renderer.push("<!--[!-->");
																		$$renderer.push("<!--]-->");
																	}
																} else {
																	$$renderer.push("<!--[-1-->");
																	if (Field) {
																		$$renderer.push("<!--[-->");
																		Field($$renderer, {
																			children: ($$renderer) => {
																				if (Field_label) {
																					$$renderer.push("<!--[-->");
																					Field_label($$renderer, {
																						for: `declaration-format-${declaration.name}`,
																						children: ($$renderer) => {
																							$$renderer.push(`<!---->Format`);
																						},
																						$$slots: { default: true }
																					});
																					$$renderer.push("<!--]-->");
																				} else {
																					$$renderer.push("<!--[!-->");
																					$$renderer.push("<!--]-->");
																				}
																				Input($$renderer, {
																					id: `declaration-format-${declaration.name}`,
																					value: declaration.format ?? "",
																					placeholder: "Compiler default",
																					oninput: (event) => updateDeclaration(index, "format", event.currentTarget.value)
																				});
																				$$renderer.push(`<!---->`);
																			},
																			$$slots: { default: true }
																		});
																		$$renderer.push("<!--]-->");
																	} else {
																		$$renderer.push("<!--[!-->");
																		$$renderer.push("<!--]-->");
																	}
																}
																$$renderer.push(`<!--]--> `);
																Button($$renderer, {
																	variant: "ghost",
																	size: "icon",
																	"aria-label": `Remove formatter ${declaration.name}`,
																	onclick: () => commit((next) => {
																		next.declarations?.splice(index, 1);
																		scrubNodes(next, (node) => "local" in node && node.local === declaration.name);
																	}),
																	children: ($$renderer) => {
																		Trash_2($$renderer, {});
																	},
																	$$slots: { default: true }
																});
																$$renderer.push(`<!----></div></div>`);
															}
															$$renderer.push(`<!--]-->`);
														},
														$$slots: { default: true }
													});
													$$renderer.push("<!--]-->");
												} else {
													$$renderer.push("<!--[!-->");
													$$renderer.push("<!--]-->");
												}
												$$renderer.push(` `);
												Button($$renderer, {
													variant: "outline",
													class: "justify-self-start",
													disabled: !inputNames().some((name) => message().inputs[name].type !== "bool"),
													onclick: addDeclaration,
													children: ($$renderer) => {
														Circle_plus($$renderer, { "data-icon": "inline-start" });
														$$renderer.push(`<!---->Add formatter`);
													},
													$$slots: { default: true }
												});
												$$renderer.push(`<!---->`);
											},
											$$slots: { default: true }
										});
										$$renderer.push("<!--]-->");
									} else {
										$$renderer.push("<!--[!-->");
										$$renderer.push("<!--]-->");
									}
									$$renderer.push(`</div>`);
								},
								$$slots: { default: true }
							});
							$$renderer.push("<!--]-->");
						} else {
							$$renderer.push("<!--[!-->");
							$$renderer.push("<!--]-->");
						}
					},
					$$slots: { default: true }
				});
				$$renderer.push("<!--]-->");
			} else {
				$$renderer.push("<!--[!-->");
				$$renderer.push("<!--]-->");
			}
			$$renderer.push(`</div> `);
			{
				function footer($$renderer) {
					Button($$renderer, {
						variant: "outline",
						onclick: () => exactCaseOpen = false,
						children: ($$renderer) => {
							$$renderer.push(`<!---->Cancel`);
						},
						$$slots: { default: true }
					});
					$$renderer.push(`<!----> `);
					Button($$renderer, {
						disabled: exactCaseMatch() === "" || exactCaseDuplicate(),
						onclick: addExactCase,
						children: ($$renderer) => {
							$$renderer.push(`<!---->Add case`);
						},
						$$slots: { default: true }
					});
					$$renderer.push(`<!---->`);
				}
				AppDialog($$renderer, {
					open: exactCaseOpen,
					title: primarySelector()?.function === "literal" ? "Add a custom case" : "Add an exact-number case",
					description: primarySelector()?.function === "literal" ? "Enter the exact value that should select this translation." : "Enter the number that should select this translation instead of the locale’s normal plural form.",
					class: "sm:max-w-md",
					bodyClass: "grid gap-3",
					onopenchange: (open) => exactCaseOpen = open,
					footer,
					children: ($$renderer) => {
						if (Field) {
							$$renderer.push("<!--[-->");
							Field($$renderer, {
								children: ($$renderer) => {
									if (Field_label) {
										$$renderer.push("<!--[-->");
										Field_label($$renderer, {
											for: "exact-case-value",
											children: ($$renderer) => {
												$$renderer.push(`<!---->${escape_html(primarySelector()?.function === "literal" ? "Value" : "Exact number")}`);
											},
											$$slots: { default: true }
										});
										$$renderer.push("<!--]-->");
									} else {
										$$renderer.push("<!--[!-->");
										$$renderer.push("<!--]-->");
									}
									$$renderer.push(` `);
									Input($$renderer, {
										id: "exact-case-value",
										type: primarySelector()?.function === "literal" ? "text" : "number",
										placeholder: primarySelector()?.function === "literal" ? "premium" : "0",
										onkeydown: (event) => {
											if (event.key === "Enter") addExactCase();
										},
										get value() {
											return exactCaseValue;
										},
										set value($$value) {
											exactCaseValue = $$value;
											$$settled = false;
										}
									});
									$$renderer.push(`<!----> `);
									if (exactCaseDuplicate()) {
										$$renderer.push("<!--[0-->");
										if (Field_error) {
											$$renderer.push("<!--[-->");
											Field_error($$renderer, {
												children: ($$renderer) => {
													$$renderer.push(`<!---->This case already exists.`);
												},
												$$slots: { default: true }
											});
											$$renderer.push("<!--]-->");
										} else {
											$$renderer.push("<!--[!-->");
											$$renderer.push("<!--]-->");
										}
									} else $$renderer.push("<!--[-1-->");
									$$renderer.push(`<!--]-->`);
								},
								$$slots: { default: true }
							});
							$$renderer.push("<!--]-->");
						} else {
							$$renderer.push("<!--[!-->");
							$$renderer.push("<!--]-->");
						}
					},
					$$slots: {
						footer: true,
						default: true
					}
				});
			}
			$$renderer.push(`<!----> `);
			{
				function footer($$renderer) {
					Button($$renderer, {
						variant: "outline",
						onclick: () => rawMode = false,
						children: ($$renderer) => {
							$$renderer.push(`<!---->Cancel`);
						},
						$$slots: { default: true }
					});
					$$renderer.push(`<!----> `);
					Button($$renderer, {
						onclick: applyRaw,
						children: ($$renderer) => {
							Settings_2($$renderer, { "data-icon": "inline-start" });
							$$renderer.push(`<!---->Apply source`);
						},
						$$slots: { default: true }
					});
					$$renderer.push(`<!---->`);
				}
				AppDialog($$renderer, {
					open: rawMode,
					title: "Structured message source",
					description: "An escape hatch for exact schema-v2 source editing.",
					class: "sm:max-w-3xl",
					bodyClass: "grid gap-3",
					onopenchange: (open) => rawMode = open,
					footer,
					children: ($$renderer) => {
						Textarea($$renderer, {
							class: "field-sizing-fixed min-h-[55svh] resize-none font-mono text-xs leading-relaxed",
							spellcheck: false,
							"aria-label": "Structured message source",
							get value() {
								return rawText;
							},
							set value($$value) {
								rawText = $$value;
								$$settled = false;
							}
						});
						$$renderer.push(`<!----> `);
						if (rawError) {
							$$renderer.push("<!--[0-->");
							$$renderer.push(`<p class="text-sm text-destructive" aria-live="polite">${escape_html(rawError)}</p>`);
						} else $$renderer.push("<!--[-1-->");
						$$renderer.push(`<!--]-->`);
					},
					$$slots: {
						footer: true,
						default: true
					}
				});
			}
			$$renderer.push(`<!---->`);
		}
		do {
			$$settled = true;
			$$inner_renderer = $$renderer.copy();
			$$render_inner($$inner_renderer);
		} while (!$$settled);
		$$renderer.subsume($$inner_renderer);
	});
}
//#endregion
//#region src/lib/TranslationEditor.svelte
function TranslationEditor($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { mode, locale, label, value, resourceValue, missing, invalid, onresourcechange, onrawchange, onformatraw } = $$props;
		$$renderer.push(`<section class="mx-auto mt-4 w-full max-w-[1000px]">`);
		if (Field) {
			$$renderer.push("<!--[-->");
			Field($$renderer, {
				"data-invalid": invalid,
				class: "gap-2",
				children: ($$renderer) => {
					$$renderer.push(`<div class="flex min-w-0 items-center justify-between gap-4">`);
					if (Field_label) {
						$$renderer.push("<!--[-->");
						Field_label($$renderer, {
							for: mode === "raw" ? "translation-value" : void 0,
							class: "min-w-0 truncate text-xs font-semibold text-foreground/80",
							children: ($$renderer) => {
								$$renderer.push(`<!---->${escape_html(label)}`);
							},
							$$slots: { default: true }
						});
						$$renderer.push("<!--]-->");
					} else {
						$$renderer.push("<!--[!-->");
						$$renderer.push("<!--]-->");
					}
					$$renderer.push(` <span class="shrink-0 text-[0.65rem] tabular-nums text-muted-foreground">${escape_html(value.length.toLocaleString())} characters</span></div> `);
					if (mode === "translation") {
						$$renderer.push("<!--[0-->");
						MessageComposer($$renderer, {
							value: resourceValue,
							locale,
							onchange: onresourcechange
						});
					} else {
						$$renderer.push("<!--[-1-->");
						Textarea($$renderer, {
							id: "translation-value",
							class: "field-sizing-fixed min-h-96 resize-y bg-card/70 px-5 py-4 font-mono text-xs leading-7 shadow-inner",
							value,
							placeholder: missing ? "Add this translation…" : void 0,
							spellcheck: false,
							"aria-invalid": invalid,
							oninput: (event) => onrawchange(event.currentTarget.value)
						});
						$$renderer.push(`<!----> <div class="flex flex-wrap items-center justify-between gap-x-4 gap-y-2 px-1">`);
						if (Field_description) {
							$$renderer.push("<!--[-->");
							Field_description($$renderer, {
								class: "text-xs",
								children: ($$renderer) => {
									$$renderer.push(`<!---->Changes here affect the complete resource document.`);
								},
								$$slots: { default: true }
							});
							$$renderer.push("<!--]-->");
						} else {
							$$renderer.push("<!--[!-->");
							$$renderer.push("<!--]-->");
						}
						$$renderer.push(` `);
						Button($$renderer, {
							variant: "ghost",
							size: "xs",
							onclick: onformatraw,
							children: ($$renderer) => {
								Wand_sparkles($$renderer, { "data-icon": "inline-start" });
								$$renderer.push(`<!----> Format JSON`);
							},
							$$slots: { default: true }
						});
						$$renderer.push(`<!----></div>`);
					}
					$$renderer.push(`<!--]-->`);
				},
				$$slots: { default: true }
			});
			$$renderer.push("<!--]-->");
		} else {
			$$renderer.push("<!--[!-->");
			$$renderer.push("<!--]-->");
		}
		$$renderer.push(`</section>`);
	});
}
//#endregion
//#region node_modules/@lucide/svelte/dist/icons/circle-check-big.svelte
function Circle_check_big($$renderer, $$props) {
	let { $$slots, $$events, ...props } = $$props;
	Icon($$renderer, spread_props([
		{ name: "circle-check-big" },
		props,
		{ iconNode: [["path", { "d": "M21.801 10A10 10 0 1 1 17 3.335" }], ["path", { "d": "m9 11 3 3L22 4" }]] }
	]));
}
//#endregion
//#region src/lib/components/ui/alert/alert-action.svelte
function Alert_action($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, children, $$slots, $$events, ...restProps } = $$props;
		$$renderer.push(`<div${attributes({
			"data-slot": "alert-action",
			class: clsx$1(cn("absolute top-2.5 right-3", className)),
			...restProps
		})}>`);
		children?.($$renderer);
		$$renderer.push(`<!----></div>`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/alert/alert-description.svelte
function Alert_description($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, children, $$slots, $$events, ...restProps } = $$props;
		$$renderer.push(`<div${attributes({
			"data-slot": "alert-description",
			class: clsx$1(cn("text-sm text-balance text-muted-foreground md:text-pretty [&_p:not(:last-child)]:mb-4 [&_a]:underline [&_a]:underline-offset-3 [&_a]:hover:text-foreground", className)),
			...restProps
		})}>`);
		children?.($$renderer);
		$$renderer.push(`<!----></div>`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/alert/alert-title.svelte
function Alert_title($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, children, $$slots, $$events, ...restProps } = $$props;
		$$renderer.push(`<div${attributes({
			"data-slot": "alert-title",
			class: clsx$1(cn("font-medium group-has-[>svg]/alert:col-start-2 [&_a]:underline [&_a]:underline-offset-3 [&_a]:hover:text-foreground", className)),
			...restProps
		})}>`);
		children?.($$renderer);
		$$renderer.push(`<!----></div>`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/components/ui/alert/alert.svelte
var alertVariants = tv({
	base: "grid gap-0.5 rounded-2xl border px-4 py-3 text-left text-sm has-data-[slot=alert-action]:relative has-data-[slot=alert-action]:pr-18 has-[>svg]:grid-cols-[auto_1fr] has-[>svg]:gap-x-2.5 *:[svg]:row-span-2 *:[svg]:translate-y-0.5 *:[svg]:text-current *:[svg:not([class*='size-'])]:size-4 group/alert relative w-full",
	variants: { variant: {
		default: "bg-card text-card-foreground",
		destructive: "bg-card text-destructive *:data-[slot=alert-description]:text-destructive/90 *:[svg]:text-current"
	} },
	defaultVariants: { variant: "default" }
});
function Alert($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, class: className, variant = "default", children, $$slots, $$events, ...restProps } = $$props;
		$$renderer.push(`<div${attributes({
			"data-slot": "alert",
			role: "alert",
			class: clsx$1(cn(alertVariants({ variant }), className)),
			...restProps
		})}>`);
		children?.($$renderer);
		$$renderer.push(`<!----></div>`);
		bind_props($$props, { ref });
	});
}
//#endregion
//#region src/lib/ValidationPanel.svelte
function ValidationPanel($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { busy, diagnostics, clientError, errorCount, warningCount, validLabel, invalidLabel, diagnosticsLabel, schemaVersion, onselect } = $$props;
		let invalid = derived(() => errorCount > 0 || clientError !== void 0);
		if (Alert) {
			$$renderer.push("<!--[-->");
			Alert($$renderer, {
				variant: invalid() ? "destructive" : "default",
				class: "mx-auto mt-7 max-w-[1000px] gap-0 overflow-hidden p-0",
				"aria-live": "polite",
				children: ($$renderer) => {
					$$renderer.push(`<header class="flex flex-wrap items-center justify-between gap-3 px-4 py-3"><div class="flex min-w-0 items-center gap-3">`);
					if (busy) {
						$$renderer.push("<!--[0-->");
						Spinner($$renderer, {
							class: "size-5 shrink-0 text-primary",
							"aria-label": "Validating"
						});
					} else if (invalid()) {
						$$renderer.push("<!--[1-->");
						Circle_alert($$renderer, {
							class: "size-5 shrink-0",
							"aria-hidden": "true"
						});
					} else {
						$$renderer.push("<!--[-1-->");
						Circle_check_big($$renderer, {
							class: "size-5 shrink-0 text-primary",
							"aria-hidden": "true"
						});
					}
					$$renderer.push(`<!--]--> <div class="min-w-0">`);
					if (Alert_title) {
						$$renderer.push("<!--[-->");
						Alert_title($$renderer, {
							class: "text-xs font-semibold",
							children: ($$renderer) => {
								$$renderer.push(`<!---->${escape_html(busy ? "Validating with the Runic compiler…" : invalid() ? invalidLabel : validLabel)}`);
							},
							$$slots: { default: true }
						});
						$$renderer.push("<!--]-->");
					} else {
						$$renderer.push("<!--[!-->");
						$$renderer.push("<!--]-->");
					}
					$$renderer.push(` `);
					if (Alert_description) {
						$$renderer.push("<!--[-->");
						Alert_description($$renderer, {
							class: "text-xs",
							children: ($$renderer) => {
								$$renderer.push(`<!---->${escape_html(diagnosticsLabel)} · ${escape_html(errorCount)} errors · ${escape_html(warningCount)} warnings`);
							},
							$$slots: { default: true }
						});
						$$renderer.push("<!--]-->");
					} else {
						$$renderer.push("<!--[!-->");
						$$renderer.push("<!--]-->");
					}
					$$renderer.push(`</div></div> `);
					Badge($$renderer, {
						variant: "outline",
						class: "font-mono text-[0.65rem]",
						children: ($$renderer) => {
							$$renderer.push(`<!---->compiler · schema v${escape_html(schemaVersion)}`);
						},
						$$slots: { default: true }
					});
					$$renderer.push(`<!----></header> `);
					if (clientError) {
						$$renderer.push("<!--[0-->");
						$$renderer.push(`<div class="border-t border-destructive/30 bg-destructive/10 px-4 py-3 text-xs text-destructive">${escape_html(clientError)}</div>`);
					} else $$renderer.push("<!--[-1-->");
					$$renderer.push(`<!--]--> `);
					if (diagnostics.length > 0) {
						$$renderer.push("<!--[0-->");
						$$renderer.push(`<div class="divide-y border-t"><!--[-->`);
						const each_array = ensure_array_like(diagnostics);
						for (let $$index = 0, $$length = each_array.length; $$index < $$length; $$index++) {
							let diagnostic = each_array[$$index];
							Button($$renderer, {
								variant: "ghost",
								class: "grid h-auto w-full grid-cols-[auto_minmax(0,1fr)] items-start justify-start gap-3 px-4 py-3 text-left whitespace-normal md:grid-cols-[auto_minmax(0,1fr)_auto]",
								onclick: () => onselect(diagnostic),
								children: ($$renderer) => {
									if (diagnostic.severity === "error") {
										$$renderer.push("<!--[0-->");
										Circle_alert($$renderer, {
											class: "mt-0.5 size-4 text-destructive",
											"aria-hidden": "true"
										});
									} else {
										$$renderer.push("<!--[-1-->");
										Triangle_alert($$renderer, {
											class: "mt-0.5 size-4 text-primary",
											"aria-hidden": "true"
										});
									}
									$$renderer.push(`<!--]--> <span class="min-w-0 text-xs leading-5 text-muted-foreground"><strong class="mr-2 font-mono text-foreground">${escape_html(diagnostic.id)}</strong>${escape_html(diagnostic.message)}</span> <code class="hidden whitespace-nowrap text-[0.65rem] text-muted-foreground md:block">${escape_html(diagnostic.path)}:${escape_html(diagnostic.line)}:${escape_html(diagnostic.column)}</code>`);
								},
								$$slots: { default: true }
							});
						}
						$$renderer.push(`<!--]--></div>`);
					} else $$renderer.push("<!--[-1-->");
					$$renderer.push(`<!--]-->`);
				},
				$$slots: { default: true }
			});
			$$renderer.push("<!--]-->");
		} else {
			$$renderer.push("<!--[!-->");
			$$renderer.push("<!--]-->");
		}
	});
}
//#endregion
//#region node_modules/@lucide/svelte/dist/icons/wrench.svelte
function Wrench($$renderer, $$props) {
	let { $$slots, $$events, ...props } = $$props;
	Icon($$renderer, spread_props([
		{ name: "wrench" },
		props,
		{ iconNode: [["path", { "d": "M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.106-3.105c.32-.322.863-.22.983.218a6 6 0 0 1-8.259 7.057l-7.91 7.91a1 1 0 0 1-2.999-3l7.91-7.91a6 6 0 0 1 7.057-8.259c.438.12.54.662.219.984z" }]] }
	]));
}
//#endregion
//#region src/lib/WorkspacePanel.svelte
function WorkspacePanel($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { malformedDocuments, reviewError, onrepair } = $$props;
		if (malformedDocuments.length > 0 || reviewError) {
			$$renderer.push("<!--[0-->");
			if (Sidebar_group) {
				$$renderer.push("<!--[-->");
				Sidebar_group($$renderer, {
					"aria-label": "Workspace issues",
					class: "py-1",
					children: ($$renderer) => {
						if (Sidebar_group_label) {
							$$renderer.push("<!--[-->");
							Sidebar_group_label($$renderer, {
								children: ($$renderer) => {
									$$renderer.push(`<!---->Workspace issues`);
								},
								$$slots: { default: true }
							});
							$$renderer.push("<!--]-->");
						} else {
							$$renderer.push("<!--[!-->");
							$$renderer.push("<!--]-->");
						}
						$$renderer.push(` `);
						if (Sidebar_group_content) {
							$$renderer.push("<!--[-->");
							Sidebar_group_content($$renderer, {
								class: "grid gap-2 px-2",
								children: ($$renderer) => {
									if (malformedDocuments.length > 0) {
										$$renderer.push("<!--[0-->");
										if (Alert) {
											$$renderer.push("<!--[-->");
											Alert($$renderer, {
												variant: "destructive",
												class: "gap-y-1 px-2.5 py-2",
												children: ($$renderer) => {
													Wrench($$renderer, {});
													$$renderer.push(`<!----> `);
													if (Alert_title) {
														$$renderer.push("<!--[-->");
														Alert_title($$renderer, {
															class: "text-xs",
															children: ($$renderer) => {
																$$renderer.push(`<!---->${escape_html(malformedDocuments.length)} malformed ${escape_html(malformedDocuments.length === 1 ? "file" : "files")}`);
															},
															$$slots: { default: true }
														});
														$$renderer.push("<!--]-->");
													} else {
														$$renderer.push("<!--[!-->");
														$$renderer.push("<!--]-->");
													}
													$$renderer.push(` `);
													if (Alert_description) {
														$$renderer.push("<!--[-->");
														Alert_description($$renderer, {
															class: "grid min-w-0 gap-1",
															children: ($$renderer) => {
																$$renderer.push(`<!--[-->`);
																const each_array = ensure_array_like(malformedDocuments);
																for (let $$index = 0, $$length = each_array.length; $$index < $$length; $$index++) {
																	let document = each_array[$$index];
																	$$renderer.push(`<button type="button" class="truncate text-left text-xs underline-offset-4 hover:underline">Repair ${escape_html(document.path)}</button>`);
																}
																$$renderer.push(`<!--]-->`);
															},
															$$slots: { default: true }
														});
														$$renderer.push("<!--]-->");
													} else {
														$$renderer.push("<!--[!-->");
														$$renderer.push("<!--]-->");
													}
												},
												$$slots: { default: true }
											});
											$$renderer.push("<!--]-->");
										} else {
											$$renderer.push("<!--[!-->");
											$$renderer.push("<!--]-->");
										}
									} else $$renderer.push("<!--[-1-->");
									$$renderer.push(`<!--]--> `);
									if (reviewError) {
										$$renderer.push("<!--[0-->");
										if (Alert) {
											$$renderer.push("<!--[-->");
											Alert($$renderer, {
												variant: "destructive",
												class: "px-2.5 py-2",
												children: ($$renderer) => {
													Circle_alert($$renderer, {});
													$$renderer.push(`<!----> `);
													if (Alert_title) {
														$$renderer.push("<!--[-->");
														Alert_title($$renderer, {
															class: "text-xs",
															children: ($$renderer) => {
																$$renderer.push(`<!---->Review notes unavailable`);
															},
															$$slots: { default: true }
														});
														$$renderer.push("<!--]-->");
													} else {
														$$renderer.push("<!--[!-->");
														$$renderer.push("<!--]-->");
													}
													$$renderer.push(` `);
													if (Alert_description) {
														$$renderer.push("<!--[-->");
														Alert_description($$renderer, {
															class: "text-xs",
															children: ($$renderer) => {
																$$renderer.push(`<!---->${escape_html(reviewError)}`);
															},
															$$slots: { default: true }
														});
														$$renderer.push("<!--]-->");
													} else {
														$$renderer.push("<!--[!-->");
														$$renderer.push("<!--]-->");
													}
												},
												$$slots: { default: true }
											});
											$$renderer.push("<!--]-->");
										} else {
											$$renderer.push("<!--[!-->");
											$$renderer.push("<!--]-->");
										}
									} else $$renderer.push("<!--[-1-->");
									$$renderer.push(`<!--]-->`);
								},
								$$slots: { default: true }
							});
							$$renderer.push("<!--]-->");
						} else {
							$$renderer.push("<!--[!-->");
							$$renderer.push("<!--]-->");
						}
					},
					$$slots: { default: true }
				});
				$$renderer.push("<!--]-->");
			} else {
				$$renderer.push("<!--[!-->");
				$$renderer.push("<!--]-->");
			}
		} else $$renderer.push("<!--[-1-->");
		$$renderer.push(`<!--]-->`);
	});
}
//#endregion
//#region node_modules/@lucide/svelte/dist/icons/message-square-text.svelte
function Message_square_text($$renderer, $$props) {
	let { $$slots, $$events, ...props } = $$props;
	Icon($$renderer, spread_props([
		{ name: "message-square-text" },
		props,
		{ iconNode: [
			["path", { "d": "M22 17a2 2 0 0 1-2 2H6.828a2 2 0 0 0-1.414.586l-2.202 2.202A.71.71 0 0 1 2 21.286V5a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2z" }],
			["path", { "d": "M7 11h10" }],
			["path", { "d": "M7 15h6" }],
			["path", { "d": "M7 7h8" }]
		] }
	]));
}
//#endregion
//#region src/lib/components/ui/checkbox/checkbox.svelte
function Checkbox($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		let { ref = null, checked = false, indeterminate = false, class: className, $$slots, $$events, ...restProps } = $$props;
		let $$settled = true;
		let $$inner_renderer;
		function $$render_inner($$renderer) {
			{
				function children($$renderer, { checked, indeterminate }) {
					$$renderer.push(`<div data-slot="checkbox-indicator" class="[&amp;>svg]:size-3.5 grid place-content-center text-current transition-none">`);
					if (checked) {
						$$renderer.push("<!--[0-->");
						Check($$renderer, {});
					} else if (indeterminate) {
						$$renderer.push("<!--[1-->");
						Minus($$renderer, {});
					} else $$renderer.push("<!--[-1-->");
					$$renderer.push(`<!--]--></div>`);
				}
				if (Checkbox$1) {
					$$renderer.push("<!--[-->");
					Checkbox$1($$renderer, spread_props([
						{
							"data-slot": "checkbox",
							class: cn("flex size-4 items-center justify-center rounded-[5px] border border-transparent bg-input/90 transition-shadow group-has-disabled/field:opacity-50 focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/30 aria-invalid:border-destructive aria-invalid:ring-3 aria-invalid:ring-destructive/20 aria-invalid:aria-checked:border-primary dark:aria-invalid:border-destructive/50 dark:aria-invalid:ring-destructive/40 data-checked:border-primary data-checked:bg-primary data-checked:text-primary-foreground dark:data-checked:bg-primary peer relative shrink-0 outline-none after:absolute after:-inset-x-3 after:-inset-y-2 disabled:cursor-not-allowed disabled:opacity-50", className)
						},
						restProps,
						{
							get ref() {
								return ref;
							},
							set ref($$value) {
								ref = $$value;
								$$settled = false;
							},
							get checked() {
								return checked;
							},
							set checked($$value) {
								checked = $$value;
								$$settled = false;
							},
							get indeterminate() {
								return indeterminate;
							},
							set indeterminate($$value) {
								indeterminate = $$value;
								$$settled = false;
							},
							children,
							$$slots: { default: true }
						}
					]));
					$$renderer.push("<!--]-->");
				} else {
					$$renderer.push("<!--[!-->");
					$$renderer.push("<!--]-->");
				}
			}
		}
		do {
			$$settled = true;
			$$inner_renderer = $$renderer.copy();
			$$render_inner($$inner_renderer);
		} while (!$$settled);
		$$renderer.subsume($$inner_renderer);
		bind_props($$props, {
			ref,
			checked,
			indeterminate
		});
	});
}
//#endregion
//#region src/lib/message-preview.js
/**
* Executes the compiler-normalized locale AST used by the generated ESM dynamic runtime.
* The result is semantic data only. Callers must never turn markup names into HTML.
* @param {import("./message-composer").MessageArtifact} ast
* @param {string} locale
* @param {Record<string, string>} samples
* @returns {{ kind: "text", value: string } | { kind: "content", nodes: PreviewNode[] }}
*/
function executeMessagePreview(ast, locale, samples) {
	/** @type {Record<string, unknown>} */
	const inputs = {};
	for (const [name, descriptor] of Object.entries(ast.inputs)) {
		if (!(name in samples)) throw new TypeError(`Enter a sample value for '${name}'.`);
		inputs[name] = parseSample(name, descriptor.type, samples[name]);
	}
	const selected = ast.selectors.map((selector) => {
		const value = inputs[selector.input];
		if (selector.function === "plural") return new Intl.PluralRules(locale, { type: "cardinal" }).select(Number(value));
		if (selector.function === "ordinal") return new Intl.PluralRules(locale, { type: "ordinal" }).select(Number(value));
		return String(value);
	});
	const variant = ast.variants.find((candidate) => ast.selectors.every((selector, index) => candidate.matches[selector.name] === "*" || candidate.matches[selector.name] === selected[index]));
	if (variant === void 0) throw new RangeError("No variant matches these sample values.");
	const nodes = contentNodes(variant.nodes, ast.inputs, inputs, locale);
	return hasMarkup(nodes) ? {
		kind: "content",
		nodes
	} : {
		kind: "text",
		value: flattenPreview(nodes)
	};
}
/** @param {PreviewNode[]} nodes @returns {string} */
function flattenPreview(nodes) {
	return nodes.map((node) => node.kind === "text" ? node.value : flattenPreview(node.children)).join("");
}
/**
* @param {import("./message-composer").ArtifactNode[]} nodes
* @param {Record<string, import("./message-composer").ArtifactInput>} descriptors
* @param {Record<string, unknown>} inputs
* @param {string} locale
* @returns {PreviewNode[]}
*/
function contentNodes(nodes, descriptors, inputs, locale) {
	return nodes.map((node) => {
		if (node.kind === "markup") return {
			kind: "element",
			name: node.name,
			attributes: { ...node.attributes },
			children: contentNodes(node.children, descriptors, inputs, locale)
		};
		return {
			kind: "text",
			value: node.kind === "text" ? node.value : node.kind === "input" ? formatInput(inputs, node.input, descriptors[node.input], locale) : node.function === "relativeTime" ? formatRelativeTime(inputs[node.input], node.unit ?? "day", node.numeric ?? "auto", locale, node.input) : formatInput(inputs, node.input, {
				...descriptors[node.input],
				format: node.format
			}, locale)
		};
	});
}
/** @param {PreviewNode[]} nodes */
function hasMarkup(nodes) {
	return nodes.some((node) => node.kind === "element");
}
/** @param {string} name @param {string} type @param {string} value */
function parseSample(name, type, value) {
	if (type === "int") try {
		return BigInt(value);
	} catch {
		throw new TypeError(`Sample '${name}' must be an integer.`);
	}
	if (type === "number") {
		const parsed = Number(value);
		if (!Number.isFinite(parsed)) throw new TypeError(`Sample '${name}' must be a finite number.`);
		return parsed;
	}
	if (type === "bool") {
		if (value === "true") return true;
		if (value === "false") return false;
		throw new TypeError(`Sample '${name}' must be true or false.`);
	}
	return value;
}
/** @param {Record<string, unknown>} inputs @param {string} name @param {import("./message-composer").ArtifactInput} descriptor @param {string} locale */
function formatInput(inputs, name, descriptor, locale) {
	const value = inputs[name];
	const format = descriptor.format;
	switch (descriptor.type) {
		case "string":
			if (typeof value !== "string") invalid(name, "a string");
			return value;
		case "bool":
			if (typeof value !== "boolean") invalid(name, "true or false");
			return value ? "true" : "false";
		case "int": return formatInteger(value, format, locale, name);
		case "number": return formatNumber(value, format, locale, name);
		case "date": return formatDate(value, format, locale, name);
		case "time": return formatTime(value, format, locale, name);
		case "datetime": return formatDateTime(value, format, locale, name);
		case "guid": return formatGuid(value, format, name);
	}
}
/** @param {unknown} value @param {string} format @param {string} locale @param {string} name */
function formatInteger(value, format, locale, name) {
	if (typeof value !== "bigint") invalid(name, "an integer");
	if (format === "plain") return value.toString();
	if (format === "grouped") return new Intl.NumberFormat(locale, { maximumFractionDigits: 0 }).format(value);
	throw new TypeError(`Unsupported integer format '${format}'.`);
}
/** @param {unknown} value @param {string} format @param {string} locale @param {string} name */
function formatNumber(value, format, locale, name) {
	if (typeof value !== "number" || !Number.isFinite(value)) invalid(name, "a finite number");
	if (format === "plain") return expandExponent(String(value));
	if (format === "grouped") return new Intl.NumberFormat(locale, { maximumFractionDigits: 20 }).format(value);
	const fixed = /^fixed([0-6])$/.exec(format);
	if (fixed !== null) return new Intl.NumberFormat(locale, {
		minimumFractionDigits: Number(fixed[1]),
		maximumFractionDigits: Number(fixed[1]),
		useGrouping: false
	}).format(value);
	const percent = /^percent([0-4])$/.exec(format);
	if (percent !== null) return new Intl.NumberFormat(locale, {
		style: "percent",
		minimumFractionDigits: Number(percent[1]),
		maximumFractionDigits: Number(percent[1])
	}).format(value);
	throw new TypeError(`Unsupported number format '${format}'.`);
}
/** @param {unknown} value @param {string} format @param {string} locale @param {string} name */
function formatDate(value, format, locale, name) {
	if (typeof value !== "string" || !/^\d{4}-\d{2}-\d{2}$/.test(value)) invalid(name, "an ISO date");
	if (format === "iso") return value;
	const date = /* @__PURE__ */ new Date(`${value}T00:00:00Z`);
	if (Number.isNaN(date.valueOf()) || date.toISOString().slice(0, 10) !== value) invalid(name, "an ISO date");
	if (![
		"short",
		"medium",
		"long"
	].includes(format)) throw new TypeError(`Unsupported date format '${format}'.`);
	return new Intl.DateTimeFormat(locale, {
		dateStyle: format,
		timeZone: "UTC"
	}).format(date);
}
/** @param {unknown} value @param {string} format @param {string} locale @param {string} name */
function formatTime(value, format, locale, name) {
	if (typeof value !== "string" || !/^\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?$/.test(value)) invalid(name, "an ISO time");
	if (format === "iso") return value;
	const date = /* @__PURE__ */ new Date(`1970-01-01T${value}Z`);
	if (Number.isNaN(date.valueOf())) invalid(name, "an ISO time");
	if (!["short", "medium"].includes(format)) throw new TypeError(`Unsupported time format '${format}'.`);
	return new Intl.DateTimeFormat(locale, {
		timeStyle: format,
		timeZone: "UTC"
	}).format(date);
}
/** @param {unknown} value @param {string} format @param {string} locale @param {string} name */
function formatDateTime(value, format, locale, name) {
	if (typeof value !== "string") invalid(name, "an ISO instant");
	const date = new Date(value);
	if (Number.isNaN(date.valueOf())) invalid(name, "an ISO instant");
	if (format === "iso") return date.toISOString().replace(/\.(\d{3})Z$/, (_, digits) => `.${digits}0000Z`);
	if (![
		"short",
		"medium",
		"long"
	].includes(format)) throw new TypeError(`Unsupported datetime format '${format}'.`);
	const style = format;
	return new Intl.DateTimeFormat(locale, {
		dateStyle: style,
		timeStyle: style,
		timeZone: "UTC"
	}).format(date);
}
/** @param {unknown} value @param {string} format @param {string} name */
function formatGuid(value, format, name) {
	if (typeof value !== "string" || !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value)) invalid(name, "a canonical UUID");
	const canonical = value.toLowerCase();
	if (format.toLowerCase() === "d") return canonical;
	if (format.toLowerCase() === "n") return canonical.replaceAll("-", "");
	throw new TypeError(`Unsupported UUID format '${format}'.`);
}
/** @param {unknown} value @param {string} unit @param {string} numeric @param {string} locale @param {string} name */
function formatRelativeTime(value, unit, numeric, locale, name) {
	const number = typeof value === "bigint" ? Number(value) : value;
	if (typeof number !== "number" || !Number.isFinite(number)) invalid(name, "a number");
	return new Intl.RelativeTimeFormat(locale, { numeric }).format(number, unit);
}
/** @param {string} value */
function expandExponent(value) {
	if (!/[eE]/.test(value)) return value;
	const [coefficient, exponentText] = value.toLowerCase().split("e");
	const exponent = Number(exponentText);
	const negative = coefficient.startsWith("-");
	const unsigned = negative ? coefficient.slice(1) : coefficient;
	const point = unsigned.indexOf(".");
	const digits = unsigned.replace(".", "");
	const decimal = (point < 0 ? unsigned.length : point) + exponent;
	const result = decimal <= 0 ? `0.${"0".repeat(-decimal)}${digits}` : decimal >= digits.length ? digits + "0".repeat(decimal - digits.length) : `${digits.slice(0, decimal)}.${digits.slice(decimal)}`;
	return negative ? `-${result}` : result;
}
/** @param {string} name @param {string} expected @returns {never} */
function invalid(name, expected) {
	throw new TypeError(`Input '${name}' must be ${expected}.`);
}
/** @typedef {{ kind: "text", value: string } | { kind: "element", name: string, attributes: Record<string, string>, children: PreviewNode[] }} PreviewNode */
//#endregion
//#region src/lib/resource-model.ts
function buildRows(snapshot, drafts) {
	if (snapshot?.catalog === void 0) return [];
	const documents = snapshot.documents.filter((document) => !document.isManifest);
	const byLocale = /* @__PURE__ */ new Map();
	for (const document of documents) {
		if (document.locale === void 0) continue;
		const group = byLocale.get(document.locale) ?? [];
		group.push(document);
		byLocale.set(document.locale, group);
	}
	for (const group of byLocale.values()) group.sort((left, right) => layerPriority(snapshot, right.layer) - layerPriority(snapshot, left.layer));
	const entriesByLocale = /* @__PURE__ */ new Map();
	const keys = /* @__PURE__ */ new Set();
	for (const locale of snapshot.catalog.locales) {
		const entries = /* @__PURE__ */ new Map();
		for (const document of [...byLocale.get(locale.tag) ?? []].reverse()) {
			const content = drafts[document.path] ?? document.content;
			for (const entry of flattenDocument(content)) entries.set(entry.key, entry);
		}
		entriesByLocale.set(locale.tag, entries);
		for (const key of entries.keys()) keys.add(key);
	}
	const sourceEntries = entriesByLocale.get(snapshot.catalog.defaultLocale) ?? /* @__PURE__ */ new Map();
	return [...keys].sort().map((key) => {
		const source = sourceEntries.get(key);
		const cells = {};
		for (const locale of snapshot.catalog.locales) {
			const entry = entriesByLocale.get(locale.tag)?.get(key);
			cells[locale.tag] = {
				document: primaryDocument(byLocale.get(locale.tag) ?? []),
				entry,
				inheritedFrom: entry === void 0 ? fallbackWithValue(snapshot, entriesByLocale, locale.tag, key) : void 0
			};
		}
		return {
			key,
			description: source?.description,
			tags: source?.tags ?? [],
			cells,
			structured: [...snapshot.catalog.locales].some((locale) => entriesByLocale.get(locale.tag)?.get(key)?.structured)
		};
	});
}
function updateResourceValue(content, key, value, sourceTemplate) {
	const document = JSON.parse(content);
	const resources = object(document.resources, "resources");
	const segments = key.split(".");
	let group = resources;
	for (const segment of segments.slice(0, -1)) {
		const existing = group[segment];
		if (!isObject(existing) || "$value" in existing) group[segment] = {};
		group = object(group[segment], segment);
	}
	const leaf = segments.at(-1);
	const existing = group[leaf];
	if (isObject(existing) && "$value" in existing) existing.$value = value;
	else if (sourceTemplate?.placeholders !== void 0) group[leaf] = {
		$value: value,
		$placeholders: structuredClone(sourceTemplate.placeholders)
	};
	else if (typeof value === "string") group[leaf] = value;
	else group[leaf] = { $value: value };
	return `${JSON.stringify(document, null, 2)}\n`;
}
function formatJson(content) {
	return `${JSON.stringify(JSON.parse(content), null, 2)}\n`;
}
function preview(entry) {
	if (entry === void 0) return "Not translated";
	if (typeof entry.value === "string") return entry.value;
	const variants = Array.isArray(entry.value.variants) ? entry.value.variants.length : 0;
	return variants === 1 ? "Structured message · 1 variant" : `Structured message · ${variants} variants`;
}
function coverage(rows, locale) {
	return {
		translated: rows.filter((row) => row.cells[locale]?.entry !== void 0).length,
		total: rows.length
	};
}
function flattenDocument(content) {
	try {
		return flattenGroup(object(JSON.parse(content).resources, "resources"), []);
	} catch {
		return [];
	}
}
function flattenGroup(group, path) {
	const entries = [];
	for (const [name, candidate] of Object.entries(group)) {
		const next = [...path, name];
		if (typeof candidate === "string") entries.push({
			key: next.join("."),
			value: candidate,
			tags: [],
			structured: false
		});
		else if (isObject(candidate) && "$value" in candidate) {
			const value = candidate.$value;
			if (typeof value === "string" || isObject(value)) entries.push({
				key: next.join("."),
				value,
				description: typeof candidate.$description === "string" ? candidate.$description : void 0,
				tags: Array.isArray(candidate.$tags) ? candidate.$tags.filter((tag) => typeof tag === "string") : [],
				placeholders: isObject(candidate.$placeholders) ? candidate.$placeholders : void 0,
				structured: typeof value !== "string"
			});
		} else if (isObject(candidate)) entries.push(...flattenGroup(candidate, next));
	}
	return entries;
}
function primaryDocument(documents) {
	return documents[0];
}
function layerPriority(snapshot, layer) {
	return snapshot.catalog?.layers.find((candidate) => candidate.name === layer)?.priority ?? 0;
}
function fallbackWithValue(snapshot, entries, locale, key) {
	const seen = /* @__PURE__ */ new Set();
	let current = snapshot.catalog?.locales.find((candidate) => candidate.tag === locale)?.fallback;
	while (current !== void 0 && !seen.has(current)) {
		if (entries.get(current)?.has(key)) return current;
		seen.add(current);
		current = snapshot.catalog?.locales.find((candidate) => candidate.tag === current)?.fallback;
	}
}
function object(value, name) {
	if (!isObject(value)) throw new TypeError(`Expected '${name}' to be a JSON object.`);
	return value;
}
function isObject(value) {
	return typeof value === "object" && value !== null && !Array.isArray(value);
}
//#endregion
//#region src/lib/review-model.ts
function sourceFingerprint(value) {
	if (value === void 0) return void 0;
	const text = stableJson(value);
	let hash = 14695981039346656037n;
	for (let index = 0; index < text.length; index += 1) {
		hash ^= BigInt(text.charCodeAt(index));
		hash = BigInt.asUintN(64, hash * 1099511628211n);
	}
	return "fnv1a64:" + hash.toString(16).padStart(16, "0");
}
function reviewIdentity(key, locale) {
	return key + "\0" + locale;
}
function reviewMap(entries) {
	return new Map(entries.map((entry) => [reviewIdentity(entry.key, entry.locale), entry]));
}
function effectiveReviewState(entry, translated) {
	return entry?.state ?? (translated ? "translated" : "draft");
}
function isStale(entry, currentSource) {
	const fingerprint = sourceFingerprint(currentSource);
	return entry?.sourceFingerprint !== void 0 && fingerprint !== void 0 && entry.sourceFingerprint !== fingerprint;
}
function qualityIssues(rows, sourceLocale, locale, reviewEntries, terminology) {
	const reviews = reviewMap(reviewEntries);
	const result = [];
	for (const row of rows) {
		const source = row.cells[sourceLocale]?.entry?.value;
		const target = row.cells[locale]?.entry?.value;
		if (target === void 0) {
			result.push({
				kind: "missing",
				key: row.key,
				locale,
				message: "Translation is missing."
			});
			continue;
		}
		if (typeof source === "string" && typeof target === "string") {
			if (locale !== sourceLocale && source.trim().length > 0 && target === source) result.push({
				kind: "identical",
				key: row.key,
				locale,
				message: "Translation is identical to the source."
			});
			if (target !== target.trim()) result.push({
				kind: "whitespace",
				key: row.key,
				locale,
				message: "Translation has leading or trailing whitespace."
			});
			for (const term of terminology) {
				if (term.locale !== void 0 && term.locale !== locale) continue;
				if (source.toLocaleLowerCase().includes(term.source.toLocaleLowerCase()) && !target.toLocaleLowerCase().includes(term.preferred.toLocaleLowerCase())) result.push({
					kind: "terminology",
					key: row.key,
					locale,
					message: "Preferred term '" + term.preferred + "' is missing."
				});
			}
		}
		if (isStale(reviews.get(reviewIdentity(row.key, locale)), source)) result.push({
			kind: "stale",
			key: row.key,
			locale,
			message: "Source changed after this review state was recorded."
		});
	}
	return result.sort((left, right) => left.key.localeCompare(right.key) || left.kind.localeCompare(right.kind));
}
function translationSuggestions(rows, sourceLocale, targetLocale, key) {
	const current = rows.find((row) => row.key === key)?.cells[sourceLocale]?.entry?.value;
	if (typeof current !== "string" || current.trim() === "") return [];
	const currentTokens = tokens(current);
	return rows.flatMap((row) => {
		if (row.key === key) return [];
		const source = row.cells[sourceLocale]?.entry?.value;
		const translation = row.cells[targetLocale]?.entry?.value;
		if (typeof source !== "string" || typeof translation !== "string") return [];
		const score = similarity(currentTokens, tokens(source));
		return score < .2 ? [] : [{
			key: row.key,
			source,
			translation,
			score
		}];
	}).sort((left, right) => right.score - left.score || left.key.localeCompare(right.key)).slice(0, 5);
}
function qualityReportCsv(issues) {
	const escape = (value) => "\"" + value.replaceAll("\"", "\"\"") + "\"";
	return [[
		"key",
		"locale",
		"kind",
		"message"
	].map(escape).join(","), ...issues.map((issue) => [
		issue.key,
		issue.locale,
		issue.kind,
		issue.message
	].map(escape).join(","))].join("\n") + "\n";
}
function stableJson(value) {
	if (Array.isArray(value)) return "[" + value.map(stableJson).join(",") + "]";
	if (typeof value === "object" && value !== null) return "{" + Object.entries(value).sort(([left], [right]) => left.localeCompare(right)).map(([name, child]) => JSON.stringify(name) + ":" + stableJson(child)).join(",") + "}";
	return JSON.stringify(value);
}
function tokens(value) {
	return new Set(value.toLocaleLowerCase().split(/[^\p{L}\p{N}]+/u).filter((item) => item.length > 1));
}
function similarity(left, right) {
	if (left.size === 0 || right.size === 0) return 0;
	let intersection = 0;
	for (const token of left) if (right.has(token)) intersection += 1;
	return intersection / (left.size + right.size - intersection);
}
//#endregion
//#region src/routes/+page.svelte
function previewNodes($$renderer, nodes) {
	$$renderer.push(`<!--[-->`);
	const each_array = ensure_array_like(nodes);
	for (let index = 0, $$length = each_array.length; index < $$length; index++) {
		let node = each_array[index];
		if (node.kind === "text") {
			$$renderer.push("<!--[0-->");
			$$renderer.push(`<span class="preview-text">${escape_html(node.value)}</span>`);
		} else {
			$$renderer.push("<!--[-1-->");
			$$renderer.push(`<span class="preview-element svelte-1uha8ag"><span class="preview-element-label svelte-1uha8ag">${escape_html(node.name)}</span> `);
			if (Object.keys(node.attributes).length > 0) {
				$$renderer.push("<!--[0-->");
				$$renderer.push(`<span class="preview-attributes svelte-1uha8ag">${escape_html(Object.entries(node.attributes).map(([name, value]) => name + "=" + value).join(" · "))}</span>`);
			} else $$renderer.push("<!--[-1-->");
			$$renderer.push(`<!--]--> <span class="preview-children svelte-1uha8ag">`);
			previewNodes($$renderer, node.children);
			$$renderer.push(`<!----></span></span>`);
		}
		$$renderer.push(`<!--]-->`);
	}
	$$renderer.push(`<!--]-->`);
}
function _page($$renderer, $$props) {
	$$renderer.component(($$renderer) => {
		const bridge = createEditorBridge();
		let snapshot = void 0;
		let drafts = {};
		let selectedKey = "";
		let selectedLocale = "";
		let selectedDocumentPath = "";
		let filter = "all";
		let query = "";
		let mode = "translation";
		let editorText = "";
		let uiLocale = "en";
		let themeMode = "dark";
		let themePalette = "runic";
		let loading = true;
		let saving = false;
		let validationBusy = false;
		let validation = void 0;
		let clientError = void 0;
		let operationMessage = void 0;
		let searchInput = null;
		let validationTimer;
		let validationEpoch = 0;
		let projectDialogOpen = false;
		let projectStep = 1;
		let projectDirectory = "";
		let projectCatalog = "product";
		let projectDefaultLocale = "en";
		let projectLocales = [];
		let projectNamespace = "Customer.Product";
		let projectClassName = "ProductText";
		let projectLayer = "base";
		let projectGenerateEsm = true;
		let projectIncludeStarter = true;
		let projectPlan = void 0;
		let projectError = void 0;
		let projectBusy = false;
		let nextProjectLocaleId = 1;
		let openDirectory = "";
		let openingWorkspace = false;
		let pickingWorkspace = false;
		let openDialogOpen = false;
		let repairDocument = void 0;
		let repairText = "";
		let repairBusy = false;
		let repairMessage = void 0;
		let externalChanges = [];
		let externalFileChanges = [];
		let comparedExternalChange = void 0;
		let mergedExternalText = "";
		let recoveredDrafts = {};
		let recentProjects = [];
		let mutationDialogOpen = false;
		let mutationKind = "add-locale";
		let mutationLocale = "";
		let mutationFallback = "";
		let mutationReplacementFallback = "";
		let mutationLayer = "base";
		let mutationCopyFrom = "";
		let mutationSourceKey = "";
		let mutationTargetKey = "";
		let mutationInitialValue = "";
		let mutationPreview = void 0;
		let mutationError = void 0;
		let mutationBusy = false;
		let recoveryBusy = false;
		let previewBusy = false;
		let previewError = void 0;
		let previewAst = void 0;
		let previewSamples = {};
		let previewResult = void 0;
		let previewTimer;
		let previewEpoch = 0;
		let reviewEntries = [];
		let terminology = [];
		let reviewRevision = void 0;
		let reviewDirty = false;
		let reviewSaving = false;
		let reviewMessage = void 0;
		let rowLimit = 300;
		let terminologyDialogOpen = false;
		let termSource = "";
		let termPreferred = "";
		let termLocale = "";
		let termNote = "";
		let reportDialogOpen = false;
		let aboutDialogOpen = false;
		let aboutInfo = void 0;
		let aboutBusy = false;
		let diagnosticBusy = false;
		let diagnosticMessage = void 0;
		let languagesOpen = true;
		let messagesOpen = true;
		let labels = derived(() => labelsFor(uiLocale));
		let rows = derived(() => buildRows(snapshot, drafts));
		let localeSummaries = derived(() => (snapshot?.catalog?.locales ?? []).map((locale) => {
			const state = coverage(rows(), locale.tag);
			return {
				tag: locale.tag,
				name: localeName(locale.tag),
				fallback: locale.fallback,
				translated: state.translated,
				total: state.total,
				percent: state.total === 0 ? 100 : Math.round(state.translated / state.total * 100),
				isSource: locale.tag === snapshot?.catalog?.defaultLocale
			};
		}));
		let reviewIndex = derived(() => reviewMap(reviewEntries));
		let localeQuality = derived(() => qualityIssues(rows(), snapshot?.catalog?.defaultLocale ?? "", selectedLocale, reviewEntries, terminology));
		let qualityKeySet = derived(() => new Set(localeQuality().map((issue) => issue.key)));
		let filterOptions = derived(() => [
			{
				value: "all",
				label: labels().all,
				count: rows().length
			},
			{
				value: "missing",
				label: labels().missing,
				count: rows().filter((row) => row.cells[selectedLocale]?.entry === void 0).length
			},
			{
				value: "structured",
				label: labels().structured,
				count: rows().filter((row) => row.structured).length
			},
			{
				value: "needs-review",
				label: "Review",
				count: rows().filter((row) => effectiveReviewState(reviewIndex().get(reviewIdentity(row.key, selectedLocale)), row.cells[selectedLocale]?.entry !== void 0) === "needs-review").length
			},
			{
				value: "stale",
				label: "Stale",
				count: rows().filter((row) => isStale(reviewIndex().get(reviewIdentity(row.key, selectedLocale)), row.cells[snapshot?.catalog?.defaultLocale ?? ""]?.entry?.value)).length
			},
			{
				value: "quality",
				label: "Quality",
				count: qualityKeySet().size
			}
		]);
		let visibleRows = derived(() => {
			const normalized = query.trim().toLocaleLowerCase();
			return rows().filter((row) => {
				const cell = row.cells[selectedLocale];
				if (filter === "missing" && cell?.entry !== void 0) return false;
				if (filter === "structured" && !row.structured) return false;
				const review = reviewIndex().get(reviewIdentity(row.key, selectedLocale));
				if (filter === "needs-review" && effectiveReviewState(review, cell?.entry !== void 0) !== "needs-review") return false;
				if (filter === "stale" && !isStale(review, row.cells[snapshot?.catalog?.defaultLocale ?? ""]?.entry?.value)) return false;
				if (filter === "quality" && !qualityKeySet().has(row.key)) return false;
				if (normalized.length === 0) return true;
				return [
					row.key,
					row.description ?? "",
					...row.tags,
					...Object.values(row.cells).map((candidate) => preview(candidate.entry))
				].join("\n").toLocaleLowerCase().includes(normalized);
			});
		});
		let renderedRows = derived(() => visibleRows().slice(0, rowLimit));
		let messageListItems = derived(() => renderedRows().map((row) => {
			const cell = row.cells[selectedLocale];
			const rowReview = reviewIndex().get(reviewIdentity(row.key, selectedLocale));
			return {
				key: row.key,
				preview: preview(cell?.entry),
				missing: cell?.entry === void 0,
				structured: row.structured,
				stale: isStale(rowReview, row.cells[snapshot?.catalog?.defaultLocale ?? ""]?.entry?.value),
				needsReview: effectiveReviewState(rowReview, cell?.entry !== void 0) === "needs-review"
			};
		}));
		let selectedRow = derived(() => rows().find((row) => row.key === selectedKey));
		let currentCell = derived(() => selectedRow()?.cells[selectedLocale]);
		let currentSourceValue = derived(() => selectedRow()?.cells[snapshot?.catalog?.defaultLocale ?? ""]?.entry?.value);
		let currentReview = derived(() => reviewIndex().get(reviewIdentity(selectedKey, selectedLocale)));
		let currentReviewState = derived(() => effectiveReviewState(currentReview(), currentCell()?.entry !== void 0));
		let currentIsStale = derived(() => isStale(currentReview(), currentSourceValue()));
		let currentQuality = derived(() => localeQuality().filter((issue) => issue.key === selectedKey));
		let memorySuggestions = derived(() => translationSuggestions(rows(), snapshot?.catalog?.defaultLocale ?? "", selectedLocale, selectedKey));
		let currentDocument = derived(() => snapshot?.documents.find((document) => document.path === selectedDocumentPath));
		let currentContent = derived(() => currentDocument() === void 0 ? void 0 : drafts[currentDocument().path] ?? currentDocument().content);
		let isDirty = derived(() => currentDocument() !== void 0 && drafts[currentDocument().path] !== void 0);
		let diagnostics = derived(() => validation?.diagnostics ?? snapshot?.diagnostics ?? []);
		let errorCount = derived(() => diagnostics().filter((item) => item.severity === "error").length);
		let warningCount = derived(() => diagnostics().filter((item) => item.severity === "warning").length);
		let malformedDocuments = derived(() => snapshot?.documents.filter((document) => document.isMalformed) ?? []);
		function changeThemeMode(mode) {
			themeMode = mode;
			saveAppearance(themeMode, themePalette);
		}
		function changeThemePalette(palette) {
			themePalette = palette;
			saveAppearance(themeMode, themePalette);
		}
		async function loadWorkspace(confirmDiscard) {
			if (confirmDiscard && Object.keys(drafts).length > 0 && !confirm("Discard all unsaved changes?")) return;
			if (confirmDiscard) clearStoredDrafts(snapshot);
			loading = true;
			operationMessage = void 0;
			clientError = void 0;
			try {
				installSnapshot(await bridge.load(), true);
				externalChanges = [];
				externalFileChanges = [];
			} catch (error) {
				clientError = errorMessage(error);
			} finally {
				loading = false;
			}
		}
		function installSnapshot(next, resetSelection) {
			snapshot = next;
			if (resetSelection) {
				drafts = {};
				recoveredDrafts = readStoredDrafts(next);
			}
			if (resetSelection || !reviewDirty) installReview(next);
			validation = void 0;
			const nextRows = buildRows(next, {});
			if (resetSelection || !nextRows.some((row) => row.key === selectedKey)) selectedKey = nextRows[0]?.key ?? "";
			if (resetSelection || !next.catalog?.locales.some((locale) => locale.tag === selectedLocale)) selectedLocale = next.catalog?.defaultLocale ?? next.catalog?.locales[0]?.tag ?? "";
			configureEditor();
			rememberProject(next);
		}
		function installReview(next) {
			reviewEntries = structuredClone(next.review?.entries ?? []);
			terminology = structuredClone(next.review?.terminology ?? []);
			reviewRevision = next.review?.revision;
			reviewDirty = false;
			reviewMessage = next.review?.error;
		}
		function selectRow(row) {
			const nextKey = row.key;
			validation = void 0;
			clientError = void 0;
			operationMessage = void 0;
			configureEditor(void 0, nextKey, selectedLocale);
			selectedKey = nextKey;
		}
		function selectLocale(locale) {
			validation = void 0;
			clientError = void 0;
			operationMessage = void 0;
			configureEditor(void 0, selectedKey, locale);
			selectedLocale = locale;
		}
		function chooseMode(nextMode) {
			mode = nextMode;
			clientError = void 0;
			configureEditor(nextMode);
		}
		function configureEditor(preferredMode, key = selectedKey, locale = selectedLocale) {
			const row = buildRows(snapshot, drafts).find((candidate) => candidate.key === key);
			const cell = row?.cells[locale];
			const document = cell?.document;
			selectedDocumentPath = document?.path ?? "";
			const sourceEntry = row?.cells[snapshot?.catalog?.defaultLocale ?? ""]?.entry;
			previewSamples = { ...reviewIndex().get(reviewIdentity(key, locale))?.samples ?? {} };
			const nextMode = preferredMode ?? "translation";
			mode = nextMode;
			if (nextMode === "raw") editorText = document === void 0 ? "" : drafts[document.path] ?? document.content;
			else {
				const resourceValue = cell?.entry?.value ?? sourceEntry?.value ?? "";
				editorText = typeof resourceValue === "string" ? resourceValue : JSON.stringify(resourceValue, null, 2);
			}
			previewAst = void 0;
			previewResult = void 0;
			previewError = void 0;
			if (nextMode === "translation" && document !== void 0) schedulePreview(document.path, drafts[document.path] ?? document.content);
		}
		function edit(value) {
			if (mode !== "raw") {
				editResourceValue(value);
				return;
			}
			editorText = value;
			clientError = void 0;
			operationMessage = void 0;
			const document = currentDocument();
			if (document === void 0) {
				clientError = "This locale has no resource document to edit.";
				return;
			}
			try {
				drafts[document.path] = value;
				persistDrafts();
				scheduleValidation(document.path, value);
			} catch (error) {
				clientError = errorMessage(error);
				validation = {
					success: false,
					diagnostics: []
				};
			}
		}
		function editResourceValue(resourceValue) {
			editorText = typeof resourceValue === "string" ? resourceValue : JSON.stringify(resourceValue, null, 2);
			clientError = void 0;
			operationMessage = void 0;
			const document = currentDocument();
			if (document === void 0) {
				clientError = "This locale has no resource document to edit.";
				return;
			}
			try {
				const sourceEntry = selectedRow()?.cells[snapshot?.catalog?.defaultLocale ?? ""]?.entry;
				const content = updateResourceValue(drafts[document.path] ?? document.content, selectedKey, resourceValue, sourceEntry);
				drafts[document.path] = content;
				persistDrafts();
				scheduleValidation(document.path, content);
				schedulePreview(document.path, content);
			} catch (error) {
				clientError = errorMessage(error);
				validation = {
					success: false,
					diagnostics: []
				};
			}
		}
		function schedulePreview(path, content) {
			if (previewTimer !== void 0) window.clearTimeout(previewTimer);
			const epoch = ++previewEpoch;
			previewBusy = true;
			previewTimer = window.setTimeout(() => {
				bridge.previewMessage(path, content, selectedLocale, selectedKey).then((result) => {
					if (epoch !== previewEpoch) return;
					if (!result.success || result.astJson === void 0 || result.locale === void 0) {
						previewAst = void 0;
						previewResult = void 0;
						previewError = result.diagnostics[0]?.message ?? "The compiler could not build a preview.";
						return;
					}
					const ast = JSON.parse(result.astJson);
					previewAst = ast;
					const samples = {};
					for (const [name, descriptor] of Object.entries(ast.inputs)) samples[name] = previewSamples[name] ?? defaultSample(descriptor.type);
					previewSamples = samples;
					previewError = void 0;
					renderPreview(result.locale);
				}).catch((error) => {
					if (epoch === previewEpoch) previewError = errorMessage(error);
				}).finally(() => {
					if (epoch === previewEpoch) previewBusy = false;
				});
			}, 450);
		}
		function updateReview(key, locale, patch) {
			const identity = reviewIdentity(key, locale);
			const index = reviewEntries.findIndex((entry) => reviewIdentity(entry.key, entry.locale) === identity);
			const row = rows().find((candidate) => candidate.key === key);
			const existing = index < 0 ? void 0 : reviewEntries[index];
			const next = {
				key,
				locale,
				state: patch.state ?? existing?.state ?? effectiveReviewState(void 0, row?.cells[locale]?.entry !== void 0),
				note: patch.note ?? existing?.note,
				sourceFingerprint: patch.sourceFingerprint ?? existing?.sourceFingerprint,
				samples: patch.samples ?? existing?.samples ?? {}
			};
			reviewEntries = index < 0 ? [...reviewEntries, next] : reviewEntries.map((entry, candidate) => candidate === index ? next : entry);
			reviewDirty = true;
			reviewMessage = void 0;
		}
		function setCurrentReviewState(state) {
			updateReview(selectedKey, selectedLocale, {
				state,
				sourceFingerprint: sourceFingerprint(currentSourceValue()),
				samples: { ...previewSamples }
			});
		}
		function setCurrentReviewNote(note) {
			updateReview(selectedKey, selectedLocale, { note });
		}
		function markVisible(state) {
			let next = [...reviewEntries];
			const byIdentity = reviewMap(next);
			for (const row of visibleRows()) {
				const identity = reviewIdentity(row.key, selectedLocale);
				const existing = byIdentity.get(identity);
				const entry = {
					key: row.key,
					locale: selectedLocale,
					state,
					note: existing?.note,
					sourceFingerprint: sourceFingerprint(row.cells[snapshot?.catalog?.defaultLocale ?? ""]?.entry?.value),
					samples: { ...existing?.samples ?? {} }
				};
				const index = next.findIndex((candidate) => reviewIdentity(candidate.key, candidate.locale) === identity);
				if (index < 0) next.push(entry);
				else next[index] = entry;
				byIdentity.set(identity, entry);
			}
			reviewEntries = next;
			reviewDirty = true;
			reviewMessage = visibleRows().length + " visible messages marked " + state + ". Save workflow changes to commit.";
		}
		async function saveReview() {
			if (!reviewDirty || reviewSaving || snapshot?.review?.error !== void 0) return;
			reviewSaving = true;
			reviewMessage = void 0;
			try {
				const result = await bridge.saveReview({
					expectedRevision: reviewRevision,
					entries: reviewEntries.map((entry) => ({
						...entry,
						samples: { ...entry.samples }
					})),
					terminology: terminology.map((term) => ({ ...term }))
				});
				if (!result.ok || result.review === void 0) {
					reviewMessage = result.message ?? "Review data could not be saved.";
					return;
				}
				reviewEntries = structuredClone(result.review.entries);
				terminology = structuredClone(result.review.terminology);
				reviewRevision = result.review.revision;
				reviewDirty = false;
				reviewMessage = "Workflow sidecar saved";
				if (snapshot !== void 0) snapshot.review = result.review;
			} catch (error) {
				reviewMessage = errorMessage(error);
			} finally {
				reviewSaving = false;
			}
		}
		function discardReview() {
			if (snapshot !== void 0) installReview(snapshot);
		}
		function addTerm() {
			if (termSource.trim() === "" || termPreferred.trim() === "") return;
			terminology = [...terminology, {
				source: termSource.trim(),
				preferred: termPreferred.trim(),
				locale: termLocale.trim() || void 0,
				note: termNote.trim() || void 0
			}];
			reviewDirty = true;
			termSource = "";
			termPreferred = "";
			termLocale = "";
			termNote = "";
		}
		function removeTerm(index) {
			terminology = terminology.filter((_, candidate) => candidate !== index);
			reviewDirty = true;
		}
		async function showAbout() {
			aboutDialogOpen = true;
			diagnosticMessage = void 0;
			if (aboutInfo !== void 0 || aboutBusy) return;
			aboutBusy = true;
			try {
				aboutInfo = await bridge.about();
			} catch (error) {
				diagnosticMessage = errorMessage(error);
			} finally {
				aboutBusy = false;
			}
		}
		async function createDiagnosticBundle() {
			if (diagnosticBusy) return;
			diagnosticBusy = true;
			diagnosticMessage = void 0;
			try {
				const result = await bridge.createDiagnosticBundle();
				diagnosticMessage = result.ok ? `Sanitized diagnostics saved to ${result.path ?? "the temporary diagnostics directory"}.` : result.message ?? "The diagnostic bundle could not be created.";
			} catch (error) {
				diagnosticMessage = errorMessage(error);
			} finally {
				diagnosticBusy = false;
			}
		}
		function applySuggestion(value) {
			mode = "translation";
			editResourceValue(value);
		}
		function renderPreview(locale) {
			if (previewAst === void 0) return;
			try {
				previewResult = executeMessagePreview(previewAst, locale, previewSamples);
				previewError = void 0;
			} catch (error) {
				previewResult = void 0;
				previewError = errorMessage(error);
			}
		}
		function defaultSample(type) {
			if (type === "int" || type === "number") return "1";
			if (type === "bool") return "true";
			if (type === "date") return "2026-08-08";
			if (type === "time") return "12:30:00";
			if (type === "datetime") return "2026-08-08T12:30:00Z";
			if (type === "guid") return "12345678-1234-1234-1234-123456789abc";
			return "Sample";
		}
		function formatRaw() {
			if (mode !== "raw") return;
			try {
				edit(formatJson(editorText));
			} catch (error) {
				clientError = errorMessage(error);
			}
		}
		function scheduleValidation(path, content) {
			if (validationTimer !== void 0) window.clearTimeout(validationTimer);
			const epoch = ++validationEpoch;
			validationBusy = true;
			validationTimer = window.setTimeout(() => {
				bridge.validate(path, content).then((result) => {
					if (epoch !== validationEpoch) return;
					validation = result;
					validationBusy = false;
				}).catch((error) => {
					if (epoch !== validationEpoch) return;
					clientError = errorMessage(error);
					validationBusy = false;
				});
			}, 350);
		}
		async function save() {
			const document = currentDocument();
			const content = currentContent();
			if (document === void 0 || content === void 0 || !isDirty() || saving) return;
			saving = true;
			operationMessage = void 0;
			clientError = void 0;
			try {
				const checked = await bridge.validate(document.path, content);
				validation = checked;
				if (!checked.success) return;
				const result = await bridge.save(document.path, content, document.revision);
				if (!result.ok || result.snapshot === void 0) {
					if (result.validation !== void 0) validation = result.validation;
					clientError = result.message ?? `Save failed (${result.kind}).`;
					return;
				}
				const key = selectedKey;
				const locale = selectedLocale;
				delete drafts[document.path];
				persistDrafts();
				installSnapshot(result.snapshot, false);
				selectedKey = key;
				selectedLocale = locale;
				configureEditor();
				operationMessage = labels().saved;
			} catch (error) {
				clientError = errorMessage(error);
			} finally {
				saving = false;
			}
		}
		function selectDiagnostic(diagnostic) {
			const document = snapshot?.documents.find((candidate) => candidate.path === diagnostic.path);
			if (document?.locale !== void 0) selectedLocale = document.locale;
			if (document !== void 0) {
				selectedDocumentPath = document.path;
				mode = "raw";
				editorText = drafts[document.path] ?? document.content;
			}
		}
		function localeName(tag) {
			try {
				return new Intl.DisplayNames([uiLocale], { type: "language" }).of(tag) ?? tag;
			} catch {
				return tag;
			}
		}
		function errorMessage(error) {
			return error instanceof Error ? error.message : String(error);
		}
		function openProjectWizard() {
			projectStep = 1;
			projectDirectory = "";
			projectCatalog = "product";
			projectDefaultLocale = "en";
			projectLocales = [];
			projectNamespace = "Customer.Product";
			projectClassName = "ProductText";
			projectLayer = "base";
			projectGenerateEsm = true;
			projectIncludeStarter = true;
			projectPlan = void 0;
			projectError = void 0;
			projectBusy = false;
			projectDialogOpen = true;
		}
		function closeProjectWizard() {
			if (!projectBusy) projectDialogOpen = false;
		}
		function addProjectLocale() {
			projectLocales.push({
				id: nextProjectLocaleId++,
				tag: "",
				fallback: ""
			});
		}
		function removeProjectLocale(id) {
			projectLocales = projectLocales.filter((locale) => locale.id !== id);
		}
		function projectRequest() {
			return {
				directory: projectDirectory.trim(),
				catalogId: projectCatalog.trim(),
				defaultLocale: projectDefaultLocale.trim(),
				additionalLocales: projectLocales.map((locale) => ({
					tag: locale.tag.trim(),
					fallback: locale.fallback.trim() || void 0
				})),
				codeNamespace: projectNamespace.trim(),
				className: projectClassName.trim(),
				layerName: projectLayer.trim(),
				generateEsm: projectGenerateEsm,
				includeStarterMessage: projectIncludeStarter
			};
		}
		function validateProjectStep() {
			if (projectStep === 1 && (projectDirectory.trim() === "" || projectCatalog.trim() === "")) {
				projectError = "Choose a new directory and enter a catalog ID.";
				return false;
			}
			if (projectStep === 2) {
				const tags = [projectDefaultLocale.trim(), ...projectLocales.map((locale) => locale.tag.trim())];
				if (tags.some((tag) => tag === "")) {
					projectError = "Every language needs a locale tag.";
					return false;
				}
				if (new Set(tags.map((tag) => tag.toLocaleLowerCase())).size !== tags.length) {
					projectError = "Each language must use a different locale tag.";
					return false;
				}
			}
			if (projectStep === 3 && [
				projectNamespace,
				projectClassName,
				projectLayer
			].some((value) => value.trim() === "")) {
				projectError = "Namespace, class name, and layer are required.";
				return false;
			}
			projectError = void 0;
			return true;
		}
		async function advanceProjectWizard() {
			if (!validateProjectStep()) return;
			if (projectStep < 3) {
				projectStep += 1;
				return;
			}
			projectBusy = true;
			try {
				const plan = await bridge.previewProject(projectRequest());
				projectPlan = plan;
				if (!plan.ok) {
					projectError = plan.message ?? "The proposed project is invalid.";
					return;
				}
				projectStep = 4;
			} catch (error) {
				projectError = errorMessage(error);
			} finally {
				projectBusy = false;
			}
		}
		async function createProject() {
			if (projectPlan?.ok !== true || projectBusy) return;
			projectBusy = true;
			projectError = void 0;
			try {
				const result = await bridge.createProject(projectRequest());
				if (!result.ok || result.snapshot === void 0) {
					projectError = result.message ?? "The project could not be created.";
					return;
				}
				installSnapshot(result.snapshot, true);
				operationMessage = "Project created";
				projectDialogOpen = false;
			} catch (error) {
				projectError = errorMessage(error);
			} finally {
				projectBusy = false;
			}
		}
		async function openWorkspace(catalogId, directoryOverride) {
			if (openingWorkspace) return;
			if (Object.keys(drafts).length > 0 && !confirm("Discard all unsaved changes?")) return;
			clearStoredDrafts(snapshot);
			const directory = directoryOverride ?? (catalogId === void 0 ? openDirectory.trim() : snapshot?.root ?? "");
			if (directory === "") {
				clientError = "Enter a workspace directory.";
				return;
			}
			openingWorkspace = true;
			clientError = void 0;
			try {
				const result = await bridge.openWorkspace({
					directory,
					catalogId
				});
				if (!result.ok || result.snapshot === void 0) {
					clientError = result.message ?? "The workspace could not be opened.";
					return;
				}
				installSnapshot(result.snapshot, true);
				externalChanges = [];
				externalFileChanges = [];
				openDirectory = result.snapshot.root;
				openDialogOpen = false;
			} catch (error) {
				clientError = errorMessage(error);
			} finally {
				openingWorkspace = false;
			}
		}
		async function pickWorkspace() {
			if (pickingWorkspace || openingWorkspace) return;
			pickingWorkspace = true;
			clientError = void 0;
			try {
				const result = await bridge.pickWorkspace();
				if (result.ok && result.directory !== void 0) openDirectory = result.directory;
				else if (!result.cancelled && result.message !== void 0) clientError = result.message;
			} catch (error) {
				clientError = errorMessage(error);
			} finally {
				pickingWorkspace = false;
			}
		}
		function showOpenWorkspaceDialog() {
			const current = snapshot;
			if (current === void 0) return;
			openDirectory = current.root;
			openDialogOpen = true;
		}
		function prepareMutation(kind) {
			const current = snapshot;
			if (current?.catalog === void 0) return false;
			if (Object.keys(drafts).length > 0 && !confirm("Structural changes require a clean workspace. Discard unsaved drafts?")) return false;
			drafts = {};
			clearStoredDrafts(current);
			mutationKind = kind;
			const firstNonDefault = current.catalog.locales.find((locale) => locale.tag !== current.catalog?.defaultLocale)?.tag ?? "";
			mutationLocale = kind === "remove-locale" || kind === "set-fallback" ? selectedLocale === current.catalog.defaultLocale ? firstNonDefault : selectedLocale : "";
			mutationFallback = current.catalog.defaultLocale;
			mutationReplacementFallback = current.catalog.defaultLocale;
			mutationLayer = current.catalog.layers[0]?.name ?? "base";
			mutationCopyFrom = current.catalog.defaultLocale;
			mutationSourceKey = selectedKey;
			mutationTargetKey = kind === "duplicate-key" ? `${selectedKey}Copy` : kind === "create-key" ? "" : selectedKey;
			mutationInitialValue = "";
			mutationPreview = void 0;
			mutationError = void 0;
			mutationBusy = false;
			mutationDialogOpen = true;
			return true;
		}
		function mutationRequest() {
			return {
				kind: mutationKind,
				locale: mutationLocale.trim() || void 0,
				fallback: mutationFallback.trim() || void 0,
				replacementFallback: mutationReplacementFallback.trim() || void 0,
				layer: mutationLayer,
				copyFromLocale: mutationCopyFrom,
				sourceKey: mutationSourceKey.trim() || void 0,
				targetKey: mutationTargetKey.trim() || void 0,
				initialValue: mutationInitialValue
			};
		}
		function invalidateMutationPreview() {
			mutationPreview = void 0;
			mutationError = void 0;
		}
		function changeMutationKind(value) {
			const next = value;
			mutationKind = next;
			const firstTarget = (snapshot?.catalog?.locales ?? []).find((locale) => locale.tag !== snapshot?.catalog?.defaultLocale)?.tag ?? "";
			mutationLocale = next === "add-locale" ? "" : firstTarget;
			mutationFallback = snapshot?.catalog?.defaultLocale ?? "";
			mutationReplacementFallback = snapshot?.catalog?.defaultLocale ?? "";
			invalidateMutationPreview();
		}
		async function previewMutation() {
			if (mutationBusy) return;
			mutationBusy = true;
			mutationError = void 0;
			try {
				const result = await bridge.previewMutation(mutationRequest());
				mutationPreview = result;
				if (!result.ok) mutationError = result.message ?? "The change is not valid.";
			} catch (error) {
				mutationError = errorMessage(error);
			} finally {
				mutationBusy = false;
			}
		}
		async function applyMutation() {
			if (mutationBusy || mutationPreview?.ok !== true) return;
			mutationBusy = true;
			mutationError = void 0;
			try {
				const result = await bridge.applyMutation(mutationRequest());
				if (!result.ok || result.snapshot === void 0) {
					mutationError = result.message ?? "The workspace change could not be committed.";
					mutationPreview = void 0;
					return;
				}
				const preferredKey = mutationKind === "rename-key" || mutationKind === "duplicate-key" ? mutationTargetKey : selectedKey;
				installSnapshot(result.snapshot, true);
				if (buildRows(result.snapshot, {}).some((row) => row.key === preferredKey)) selectedKey = preferredKey;
				mutationDialogOpen = false;
				operationMessage = "Workspace updated";
				configureEditor();
			} catch (error) {
				mutationError = errorMessage(error);
				mutationPreview = void 0;
			} finally {
				mutationBusy = false;
			}
		}
		function recoverSavedDrafts() {
			drafts = Object.fromEntries(Object.entries(recoveredDrafts).map(([path, draft]) => [path, draft.content]));
			recoveredDrafts = {};
			persistDrafts();
			configureEditor();
		}
		function reviewExternalChanges() {
			const change = externalFileChanges[0];
			if (change === void 0) return;
			comparedExternalChange = change;
			const base = snapshot?.documents.find((document) => document.path === change.path);
			mergedExternalText = drafts[change.path] ?? base?.content ?? change.content ?? "";
		}
		async function applyExternalMerge() {
			const change = comparedExternalChange;
			if (change === void 0) return;
			const retainedDrafts = {
				...drafts,
				[change.path]: mergedExternalText
			};
			loading = true;
			clientError = void 0;
			try {
				const next = await bridge.load();
				installSnapshot(next, true);
				drafts = Object.fromEntries(Object.entries(retainedDrafts).filter(([path]) => next.documents.some((document) => document.path === path)));
				if (drafts[change.path] === void 0) clientError = `The externally deleted file '${change.path}' cannot receive a merged draft.`;
				persistDrafts();
				externalChanges = [];
				externalFileChanges = [];
				comparedExternalChange = void 0;
				configureEditor();
			} catch (error) {
				clientError = errorMessage(error);
			} finally {
				loading = false;
			}
		}
		function discardSavedDrafts() {
			recoveredDrafts = {};
			clearStoredDrafts(snapshot);
		}
		function draftStorageKey(value) {
			return `runic-text-resources:drafts:1:${value.root}\n${value.catalog?.id ?? ""}`;
		}
		function persistDrafts() {
			const current = snapshot;
			if (current === void 0) return;
			if (Object.keys(drafts).length === 0) {
				clearStoredDrafts(current);
				return;
			}
			const stored = {};
			for (const [path, content] of Object.entries(drafts)) {
				const document = current.documents.find((candidate) => candidate.path === path);
				if (document !== void 0) stored[path] = {
					content,
					baseRevision: document.revision
				};
			}
			localStorage.setItem(draftStorageKey(current), JSON.stringify({
				version: 1,
				documents: stored
			}));
		}
		function readStoredDrafts(value) {
			try {
				const raw = localStorage.getItem(draftStorageKey(value));
				if (raw === null) return {};
				const parsed = JSON.parse(raw);
				if (parsed.version !== 1 || typeof parsed.documents !== "object" || parsed.documents === null) return {};
				const recovered = {};
				for (const [path, candidate] of Object.entries(parsed.documents)) {
					if (value.documents.find((item) => item.path === path) === void 0 || typeof candidate !== "object" || candidate === null) continue;
					const draft = candidate;
					if (typeof draft.content === "string" && typeof draft.baseRevision === "string") recovered[path] = draft;
				}
				return recovered;
			} catch {
				return {};
			}
		}
		function clearStoredDrafts(value) {
			if (value !== void 0) localStorage.removeItem(draftStorageKey(value));
		}
		function rememberProject(value) {
			const catalogId = value.catalog?.id;
			if (catalogId === void 0) return;
			const entry = {
				root: value.root,
				catalogId,
				openedAt: (/* @__PURE__ */ new Date()).toISOString()
			};
			recentProjects = [entry, ...recentProjects.filter((item) => item.root !== entry.root || item.catalogId !== catalogId)].slice(0, 8);
			localStorage.setItem("runic-text-resources:recent:1", JSON.stringify(recentProjects));
		}
		function beginRepair(document) {
			repairDocument = document;
			repairText = document.content;
			repairMessage = void 0;
		}
		async function saveRepair() {
			const document = repairDocument;
			if (document === void 0 || repairBusy) return;
			repairBusy = true;
			repairMessage = void 0;
			try {
				const checked = await bridge.validate(document.path, repairText);
				if (!checked.success) {
					repairMessage = checked.diagnostics[0]?.message ?? "The document is still invalid.";
					return;
				}
				const result = await bridge.save(document.path, repairText, document.revision);
				if (!result.ok || result.snapshot === void 0) {
					repairMessage = result.message ?? "The repaired document could not be saved.";
					return;
				}
				repairDocument = void 0;
				installSnapshot(result.snapshot, true);
			} catch (error) {
				repairMessage = errorMessage(error);
			} finally {
				repairBusy = false;
			}
		}
		function mutationTitle(kind) {
			return {
				"add-locale": "Add a language",
				"remove-locale": "Remove a language",
				"set-fallback": "Change fallback relationships",
				"create-key": "Add a message",
				"rename-key": "Rename or move a message",
				"duplicate-key": "Duplicate a message",
				"delete-key": "Delete a message"
			}[kind];
		}
		function labelsFor(locale) {
			const options = { locale };
			return {
				title: m$App$Title(options),
				eyebrow: m$App$Eyebrow(options),
				search: m$App$Search(options),
				all: m$App$All(options),
				missing: m$App$Missing(options),
				structured: m$App$Structured(options),
				save: m$App$Save(options),
				saving: m$App$Saving(options),
				reload: m$App$Reload(options),
				simple: m$App$Simple(options),
				advanced: m$App$Advanced(options),
				raw: m$App$Raw(options),
				noSelection: m$App$NoSelection(options),
				noResults: m$App$NoResults(options),
				valid: m$App$Valid(options),
				invalid: m$App$Invalid(options),
				unsaved: m$App$Unsaved(options),
				saved: m$App$Saved(options),
				defaultLocale: m$App$DefaultLocale(options),
				workspace: m$App$Workspace(options),
				diagnostics: m$App$Diagnostics(options)
			};
		}
		let $$settled = true;
		let $$inner_renderer;
		function $$render_inner($$renderer) {
			head("1uha8ag", $$renderer, ($$renderer) => {
				$$renderer.title(($$renderer) => {
					$$renderer.push(`<title>${escape_html(labels().title)} · Runic Artifex</title>`);
				});
				$$renderer.push(`<meta name="description" content="A focused editor for Runic Text Resources"/>`);
			});
			if (externalChanges.length > 0) {
				$$renderer.push("<!--[0-->");
				$$renderer.push(`<div class="pointer-events-none fixed inset-x-2 bottom-2 z-50 mx-auto max-w-[calc(100vw-1rem)] sm:inset-x-4 sm:bottom-4 sm:max-w-4xl">`);
				if (Alert) {
					$$renderer.push("<!--[-->");
					Alert($$renderer, {
						class: "pointer-events-auto pr-4 shadow-xl",
						"aria-live": "polite",
						children: ($$renderer) => {
							if (Alert_title) {
								$$renderer.push("<!--[-->");
								Alert_title($$renderer, {
									children: ($$renderer) => {
										$$renderer.push(`<!---->Files changed outside the editor`);
									},
									$$slots: { default: true }
								});
								$$renderer.push("<!--]-->");
							} else {
								$$renderer.push("<!--[!-->");
								$$renderer.push("<!--]-->");
							}
							$$renderer.push(` `);
							if (Alert_description) {
								$$renderer.push("<!--[-->");
								Alert_description($$renderer, {
									class: "min-w-0",
									children: ($$renderer) => {
										$$renderer.push(`<p class="truncate font-mono text-xs">${escape_html(externalChanges.join(", "))}</p> <p>${escape_html(Object.keys(drafts).length > 0 ? "Your local drafts are still intact." : "Reload to read the latest versions.")}</p>`);
									},
									$$slots: { default: true }
								});
								$$renderer.push("<!--]-->");
							} else {
								$$renderer.push("<!--[!-->");
								$$renderer.push("<!--]-->");
							}
							$$renderer.push(` `);
							if (Alert_action) {
								$$renderer.push("<!--[-->");
								Alert_action($$renderer, {
									class: "static col-span-full mt-2 flex flex-wrap justify-end gap-2",
									children: ($$renderer) => {
										Button($$renderer, {
											variant: "ghost",
											size: "xs",
											onclick: () => {
												externalChanges = [];
												externalFileChanges = [];
											},
											children: ($$renderer) => {
												$$renderer.push(`<!---->Keep current view`);
											},
											$$slots: { default: true }
										});
										$$renderer.push(`<!----> `);
										Button($$renderer, {
											variant: "outline",
											size: "xs",
											onclick: reviewExternalChanges,
											children: ($$renderer) => {
												$$renderer.push(`<!---->Compare / merge`);
											},
											$$slots: { default: true }
										});
										$$renderer.push(`<!----> `);
										Button($$renderer, {
											size: "xs",
											onclick: () => void loadWorkspace(true),
											children: ($$renderer) => {
												$$renderer.push(`<!---->Reload files`);
											},
											$$slots: { default: true }
										});
										$$renderer.push(`<!---->`);
									},
									$$slots: { default: true }
								});
								$$renderer.push("<!--]-->");
							} else {
								$$renderer.push("<!--[!-->");
								$$renderer.push("<!--]-->");
							}
						},
						$$slots: { default: true }
					});
					$$renderer.push("<!--]-->");
				} else {
					$$renderer.push("<!--[!-->");
					$$renderer.push("<!--]-->");
				}
				$$renderer.push(`</div>`);
			} else $$renderer.push("<!--[-1-->");
			$$renderer.push(`<!--]--> `);
			if (Object.keys(recoveredDrafts).length > 0) {
				$$renderer.push("<!--[0-->");
				$$renderer.push(`<div class="pointer-events-none fixed inset-x-2 bottom-2 z-50 mx-auto max-w-[calc(100vw-1rem)] sm:inset-x-4 sm:bottom-4 sm:max-w-2xl">`);
				if (Alert) {
					$$renderer.push("<!--[-->");
					Alert($$renderer, {
						class: "pointer-events-auto pr-4 shadow-xl",
						"aria-live": "polite",
						children: ($$renderer) => {
							if (Alert_title) {
								$$renderer.push("<!--[-->");
								Alert_title($$renderer, {
									children: ($$renderer) => {
										$$renderer.push(`<!---->Unsaved work was recovered`);
									},
									$$slots: { default: true }
								});
								$$renderer.push("<!--]-->");
							} else {
								$$renderer.push("<!--[!-->");
								$$renderer.push("<!--]-->");
							}
							$$renderer.push(` `);
							if (Alert_description) {
								$$renderer.push("<!--[-->");
								Alert_description($$renderer, {
									children: ($$renderer) => {
										$$renderer.push(`<!---->${escape_html(Object.keys(recoveredDrafts).length === 1 ? "One document draft was found in local application storage." : `${Object.keys(recoveredDrafts).length} document drafts were found in local application storage.`)}`);
									},
									$$slots: { default: true }
								});
								$$renderer.push("<!--]-->");
							} else {
								$$renderer.push("<!--[!-->");
								$$renderer.push("<!--]-->");
							}
							$$renderer.push(` `);
							if (Alert_action) {
								$$renderer.push("<!--[-->");
								Alert_action($$renderer, {
									class: "static col-span-full mt-2 flex flex-col gap-2 min-[360px]:flex-row min-[360px]:justify-end",
									children: ($$renderer) => {
										Button($$renderer, {
											variant: "ghost",
											size: "xs",
											onclick: discardSavedDrafts,
											children: ($$renderer) => {
												$$renderer.push(`<!---->Discard`);
											},
											$$slots: { default: true }
										});
										$$renderer.push(`<!----> `);
										Button($$renderer, {
											size: "xs",
											onclick: recoverSavedDrafts,
											children: ($$renderer) => {
												$$renderer.push(`<!---->Restore drafts`);
											},
											$$slots: { default: true }
										});
										$$renderer.push(`<!---->`);
									},
									$$slots: { default: true }
								});
								$$renderer.push("<!--]-->");
							} else {
								$$renderer.push("<!--[!-->");
								$$renderer.push("<!--]-->");
							}
						},
						$$slots: { default: true }
					});
					$$renderer.push("<!--]-->");
				} else {
					$$renderer.push("<!--[!-->");
					$$renderer.push("<!--]-->");
				}
				$$renderer.push(`</div>`);
			} else $$renderer.push("<!--[-1-->");
			$$renderer.push(`<!--]--> `);
			if (comparedExternalChange !== void 0) {
				$$renderer.push("<!--[0-->");
				{
					function footer($$renderer) {
						Button($$renderer, {
							variant: "outline",
							onclick: () => comparedExternalChange = void 0,
							children: ($$renderer) => {
								$$renderer.push(`<!---->Keep current view`);
							},
							$$slots: { default: true }
						});
						$$renderer.push(`<!----> `);
						Button($$renderer, {
							onclick: () => void applyExternalMerge(),
							children: ($$renderer) => {
								$$renderer.push(`<!---->Reload base and keep merged draft`);
							},
							$$slots: { default: true }
						});
						$$renderer.push(`<!---->`);
					}
					AppDialog($$renderer, {
						open: true,
						title: comparedExternalChange.path,
						description: "Compare the editor base with the current file, then keep or merge the change.",
						class: "sm:max-w-6xl",
						bodyClass: "grid gap-4",
						onopenchange: (open) => {
							if (!open) comparedExternalChange = void 0;
						},
						footer,
						children: ($$renderer) => {
							$$renderer.push(`<div class="grid gap-4 lg:grid-cols-2">`);
							if (Field) {
								$$renderer.push("<!--[-->");
								Field($$renderer, {
									children: ($$renderer) => {
										if (Field_label) {
											$$renderer.push("<!--[-->");
											Field_label($$renderer, {
												for: "external-editor-base",
												children: ($$renderer) => {
													$$renderer.push(`<!---->Editor base`);
												},
												$$slots: { default: true }
											});
											$$renderer.push("<!--]-->");
										} else {
											$$renderer.push("<!--[!-->");
											$$renderer.push("<!--]-->");
										}
										$$renderer.push(` `);
										Textarea($$renderer, {
											id: "external-editor-base",
											class: "min-h-64 font-mono text-xs",
											readonly: true,
											value: snapshot?.documents.find((document) => document.path === comparedExternalChange?.path)?.content ?? "File was not previously loaded."
										});
										$$renderer.push(`<!---->`);
									},
									$$slots: { default: true }
								});
								$$renderer.push("<!--]-->");
							} else {
								$$renderer.push("<!--[!-->");
								$$renderer.push("<!--]-->");
							}
							$$renderer.push(` `);
							if (Field) {
								$$renderer.push("<!--[-->");
								Field($$renderer, {
									children: ($$renderer) => {
										if (Field_label) {
											$$renderer.push("<!--[-->");
											Field_label($$renderer, {
												for: "external-current-disk",
												children: ($$renderer) => {
													$$renderer.push(`<!---->Current disk`);
												},
												$$slots: { default: true }
											});
											$$renderer.push("<!--]-->");
										} else {
											$$renderer.push("<!--[!-->");
											$$renderer.push("<!--]-->");
										}
										$$renderer.push(` `);
										Textarea($$renderer, {
											id: "external-current-disk",
											class: "min-h-64 font-mono text-xs",
											readonly: true,
											value: comparedExternalChange.content ?? "File was deleted externally."
										});
										$$renderer.push(`<!---->`);
									},
									$$slots: { default: true }
								});
								$$renderer.push("<!--]-->");
							} else {
								$$renderer.push("<!--[!-->");
								$$renderer.push("<!--]-->");
							}
							$$renderer.push(`</div> `);
							if (Field) {
								$$renderer.push("<!--[-->");
								Field($$renderer, {
									children: ($$renderer) => {
										if (Field_label) {
											$$renderer.push("<!--[-->");
											Field_label($$renderer, {
												for: "external-merged-draft",
												children: ($$renderer) => {
													$$renderer.push(`<!---->Merged draft`);
												},
												$$slots: { default: true }
											});
											$$renderer.push("<!--]-->");
										} else {
											$$renderer.push("<!--[!-->");
											$$renderer.push("<!--]-->");
										}
										$$renderer.push(` `);
										Textarea($$renderer, {
											id: "external-merged-draft",
											class: "min-h-64 font-mono text-xs",
											spellcheck: false,
											get value() {
												return mergedExternalText;
											},
											set value($$value) {
												mergedExternalText = $$value;
												$$settled = false;
											}
										});
										$$renderer.push(`<!---->`);
									},
									$$slots: { default: true }
								});
								$$renderer.push("<!--]-->");
							} else {
								$$renderer.push("<!--[!-->");
								$$renderer.push("<!--]-->");
							}
						},
						$$slots: {
							footer: true,
							default: true
						}
					});
				}
			} else $$renderer.push("<!--[-1-->");
			$$renderer.push(`<!--]--> `);
			if (loading) {
				$$renderer.push("<!--[0-->");
				$$renderer.push(`<main class="loading-shell svelte-1uha8ag" aria-live="polite"><div class="mark svelte-1uha8ag" aria-hidden="true"><span class="svelte-1uha8ag"></span></div> <p class="svelte-1uha8ag">${escape_html(labels().eyebrow)}</p> <div class="loading-line svelte-1uha8ag"></div></main>`);
			} else if (snapshot === void 0) {
				$$renderer.push("<!--[1-->");
				$$renderer.push(`<main class="fatal-shell svelte-1uha8ag"><div class="mark svelte-1uha8ag" aria-hidden="true"><span class="svelte-1uha8ag"></span></div> <p class="eyebrow svelte-1uha8ag">${escape_html(labels().eyebrow)}</p> <h1 class="svelte-1uha8ag">Could not open this translation workspace</h1> <p class="svelte-1uha8ag">${escape_html(clientError ?? "No Runic Text Resources catalog was found.")}</p> <button class="primary svelte-1uha8ag">${escape_html(labels().reload)}</button></main>`);
			} else if (snapshot.pendingTransaction !== void 0) {
				$$renderer.push("<!--[2-->");
				$$renderer.push(`<main class="recovery-shell svelte-1uha8ag"><div class="mark svelte-1uha8ag" aria-hidden="true"><span class="svelte-1uha8ag"></span></div> <p class="eyebrow svelte-1uha8ag">Workspace recovery</p> <h1 class="svelte-1uha8ag">An interrupted change needs your decision</h1> <p class="svelte-1uha8ag">The recovery journal for <strong>${escape_html(snapshot.pendingTransaction.catalogId)}</strong> lists ${escape_html(snapshot.pendingTransaction.paths.length)} affected ${escape_html(snapshot.pendingTransaction.paths.length === 1 ? "file" : "files")}. No further editing is allowed until it is resolved.</p> <div class="recovery-paths svelte-1uha8ag"><!--[-->`);
				const each_array_1 = ensure_array_like(snapshot.pendingTransaction.paths);
				for (let $$index_1 = 0, $$length = each_array_1.length; $$index_1 < $$length; $$index_1++) {
					let path = each_array_1[$$index_1];
					$$renderer.push(`<code class="svelte-1uha8ag">${escape_html(path)}</code>`);
				}
				$$renderer.push(`<!--]--></div> `);
				if (clientError) {
					$$renderer.push("<!--[0-->");
					$$renderer.push(`<p class="project-error svelte-1uha8ag" aria-live="polite">${escape_html(clientError)}</p>`);
				} else $$renderer.push("<!--[-1-->");
				$$renderer.push(`<!--]--> <div class="recovery-actions svelte-1uha8ag"><button class="secondary svelte-1uha8ag"${attr("disabled", recoveryBusy, true)}>Restore files from before the change</button> <button class="primary svelte-1uha8ag"${attr("disabled", recoveryBusy, true)}>${escape_html("Complete the planned change")}</button></div> <small class="svelte-1uha8ag">Both choices use the bounded local journal. The journal is removed only after recovery succeeds.</small></main>`);
			} else if (snapshot.catalog === void 0) {
				$$renderer.push("<!--[3-->");
				$$renderer.push(`<main class="welcome-shell svelte-1uha8ag"><header class="welcome-brand svelte-1uha8ag"><div class="mark small svelte-1uha8ag" aria-hidden="true"><span class="svelte-1uha8ag"></span></div> <div><p class="eyebrow svelte-1uha8ag">${escape_html(labels().eyebrow)}</p><h1 class="svelte-1uha8ag">${escape_html(labels().title)}</h1></div> `);
				$$renderer.select({
					"aria-label": "Interface language",
					value: uiLocale,
					onchange: (event) => uiLocale = event.currentTarget.value,
					class: ""
				}, ($$renderer) => {
					$$renderer.option({ value: "en" }, ($$renderer) => {
						$$renderer.push(`EN`);
					});
					$$renderer.option({ value: "de" }, ($$renderer) => {
						$$renderer.push(`DE`);
					});
				}, "svelte-1uha8ag");
				$$renderer.push(`</header> <section class="welcome-content svelte-1uha8ag"><div class="welcome-heading svelte-1uha8ag"><p class="eyebrow svelte-1uha8ag">Workspace onboarding</p> <h2 class="svelte-1uha8ag">${escape_html(snapshot.catalogs.length > 1 ? "Choose a translation catalog" : "Open a translation project")}</h2> <p class="svelte-1uha8ag">${escape_html(snapshot.catalogs.length > 1 ? `We found ${snapshot.catalogs.length} catalogs below this workspace boundary.` : "Open an existing workspace or create a compiler-valid project from scratch.")}</p></div> `);
				if (snapshot.catalogs.length > 0) {
					$$renderer.push("<!--[0-->");
					$$renderer.push(`<div class="catalog-choices svelte-1uha8ag"><!--[-->`);
					const each_array_2 = ensure_array_like(snapshot.catalogs);
					for (let $$index_2 = 0, $$length = each_array_2.length; $$index_2 < $$length; $$index_2++) {
						let catalog = each_array_2[$$index_2];
						$$renderer.push(`<button class="catalog-choice svelte-1uha8ag"${attr("disabled", openingWorkspace, true)}><span${attr_class(clsx$1({
							"status-dot": true,
							warning: !catalog.success
						}), "svelte-1uha8ag")}></span> <span class="svelte-1uha8ag"><strong class="svelte-1uha8ag">${escape_html(catalog.id)}</strong><small class="svelte-1uha8ag">${escape_html(catalog.manifestPaths.join(", "))}</small></span> <span class="catalog-metrics svelte-1uha8ag">${escape_html(catalog.localeCount)} locales<br/>${escape_html(catalog.messageCount)} messages</span> <span${attr_class(clsx$1(catalog.errorCount > 0 ? "health error" : "health"), "svelte-1uha8ag")}>${escape_html(catalog.errorCount > 0 ? `${catalog.errorCount} errors` : "Healthy")}</span></button>`);
					}
					$$renderer.push(`<!--]--></div>`);
				} else $$renderer.push("<!--[-1-->");
				$$renderer.push(`<!--]--> <div class="open-workspace-card svelte-1uha8ag"><label for="open-directory" class="svelte-1uha8ag">Workspace directory</label> <div class="svelte-1uha8ag"><input id="open-directory"${attr("value", openDirectory)} placeholder="/projects/customer-app" autocomplete="off" class="svelte-1uha8ag"/> <button class="secondary svelte-1uha8ag"${attr("disabled", pickingWorkspace || openingWorkspace, true)}>${escape_html(pickingWorkspace ? "Choosing…" : "Browse…")}</button> <button class="primary svelte-1uha8ag"${attr("disabled", openingWorkspace, true)}>${escape_html(openingWorkspace ? "Opening…" : "Open")}</button></div> <small class="svelte-1uha8ag">Traversal stays inside this boundary and ignores links, dependencies, and generated output.</small></div> <div class="welcome-actions svelte-1uha8ag"><button class="secondary svelte-1uha8ag">＋ Create new project</button> <button class="secondary svelte-1uha8ag">↻ Scan ${escape_html(snapshot.root)}</button></div> `);
				if (recentProjects.length > 0) {
					$$renderer.push("<!--[0-->");
					$$renderer.push(`<section class="recent-projects svelte-1uha8ag"><header class="svelte-1uha8ag"><strong class="svelte-1uha8ag">Recent projects</strong><span class="svelte-1uha8ag">Stored only in your local application profile</span></header> <!--[-->`);
					const each_array_3 = ensure_array_like(recentProjects);
					for (let $$index_3 = 0, $$length = each_array_3.length; $$index_3 < $$length; $$index_3++) {
						let project = each_array_3[$$index_3];
						$$renderer.push(`<button${attr("disabled", openingWorkspace, true)} class="svelte-1uha8ag"><span class="svelte-1uha8ag"><strong>${escape_html(project.catalogId)}</strong><code class="svelte-1uha8ag">${escape_html(project.root)}</code></span> <small class="svelte-1uha8ag">${escape_html(new Date(project.openedAt).toLocaleDateString(uiLocale))}</small></button>`);
					}
					$$renderer.push(`<!--]--></section>`);
				} else $$renderer.push("<!--[-1-->");
				$$renderer.push(`<!--]--> `);
				if (malformedDocuments().length > 0) {
					$$renderer.push("<!--[0-->");
					$$renderer.push(`<section class="repair-list svelte-1uha8ag"><header class="svelte-1uha8ag"><div class="svelte-1uha8ag"><strong class="svelte-1uha8ag">Repair malformed JSON</strong><span class="svelte-1uha8ag">${escape_html(malformedDocuments().length)} files need attention</span></div></header> <!--[-->`);
					const each_array_4 = ensure_array_like(malformedDocuments());
					for (let $$index_4 = 0, $$length = each_array_4.length; $$index_4 < $$length; $$index_4++) {
						let document = each_array_4[$$index_4];
						$$renderer.push(`<button class="svelte-1uha8ag"><span class="svelte-1uha8ag">!</span><code class="svelte-1uha8ag">${escape_html(document.path)}</code><small class="svelte-1uha8ag">Open repair editor →</small></button>`);
					}
					$$renderer.push(`<!--]--></section>`);
				} else $$renderer.push("<!--[-1-->");
				$$renderer.push(`<!--]--> `);
				if (clientError) {
					$$renderer.push("<!--[0-->");
					$$renderer.push(`<p class="project-error svelte-1uha8ag" aria-live="polite">${escape_html(clientError)}</p>`);
				} else $$renderer.push("<!--[-1-->");
				$$renderer.push(`<!--]--></section></main>`);
			} else {
				$$renderer.push("<!--[-1-->");
				if (Sidebar_provider) {
					$$renderer.push("<!--[-->");
					Sidebar_provider($$renderer, {
						style: "--sidebar-width: 21rem; --sidebar-width-mobile: min(20rem, calc(100vw - 1rem));",
						class: "h-svh min-h-0 overflow-hidden",
						children: ($$renderer) => {
							if (Sidebar) {
								$$renderer.push("<!--[-->");
								Sidebar($$renderer, {
									collapsible: "offcanvas",
									children: ($$renderer) => {
										EditorSidebarHeader($$renderer, {
											catalogId: snapshot.catalog.id,
											localeCount: snapshot.catalog.locales.length,
											schemaVersion: snapshot.catalog.schemaVersion,
											root: snapshot.root,
											success: snapshot.success,
											reloadLabel: labels().reload,
											recentProjects,
											onreload: () => void loadWorkspace(true),
											onopenworkspace: showOpenWorkspaceDialog,
											onnewproject: openProjectWizard,
											onopenrecent: (project) => void openWorkspace(project.catalogId, project.root)
										});
										$$renderer.push(`<!----> `);
										if (Sidebar_content) {
											$$renderer.push("<!--[-->");
											Sidebar_content($$renderer, {
												class: "gap-0 overflow-hidden",
												children: ($$renderer) => {
													WorkspacePanel($$renderer, {
														malformedDocuments: malformedDocuments(),
														reviewError: snapshot.review?.error,
														onrepair: beginRepair
													});
													$$renderer.push(`<!----> `);
													{
														function languages($$renderer) {
															LocaleSwitcher($$renderer, {
																locales: localeSummaries(),
																selectedLocale,
																onselect: selectLocale,
																onmanage: () => prepareMutation("add-locale"),
																get open() {
																	return languagesOpen;
																},
																set open($$value) {
																	languagesOpen = $$value;
																	$$settled = false;
																}
															});
														}
														function messages($$renderer) {
															{
																function toolbar($$renderer) {
																	MessageToolbar($$renderer, {
																		placeholder: labels().search,
																		options: filterOptions(),
																		filterLabel: "Message filters",
																		get query() {
																			return query;
																		},
																		set query($$value) {
																			query = $$value;
																			$$settled = false;
																		},
																		get filter() {
																			return filter;
																		},
																		set filter($$value) {
																			filter = $$value;
																			$$settled = false;
																		},
																		get inputRef() {
																			return searchInput;
																		},
																		set inputRef($$value) {
																			searchInput = $$value;
																			$$settled = false;
																		}
																	});
																}
																MessageList($$renderer, {
																	items: messageListItems(),
																	selectedKey,
																	visibleCount: visibleRows().length,
																	remainingCount: visibleRows().length - renderedRows().length,
																	noResultsLabel: labels().noResults,
																	onselect: (key) => {
																		const row = renderedRows().find((candidate) => candidate.key === key);
																		if (row !== void 0) selectRow(row);
																	},
																	onadd: () => prepareMutation("create-key"),
																	onmarkreview: () => markVisible("needs-review"),
																	onapprove: () => markVisible("approved"),
																	onloadmore: () => rowLimit += 300,
																	get open() {
																		return messagesOpen;
																	},
																	set open($$value) {
																		messagesOpen = $$value;
																		$$settled = false;
																	},
																	toolbar,
																	$$slots: { toolbar: true }
																});
															}
														}
														SidebarSectionPanels($$renderer, {
															get languagesOpen() {
																return languagesOpen;
															},
															set languagesOpen($$value) {
																languagesOpen = $$value;
																$$settled = false;
															},
															get messagesOpen() {
																return messagesOpen;
															},
															set messagesOpen($$value) {
																messagesOpen = $$value;
																$$settled = false;
															},
															languages,
															messages,
															$$slots: {
																languages: true,
																messages: true
															}
														});
													}
													$$renderer.push(`<!---->`);
												},
												$$slots: { default: true }
											});
											$$renderer.push("<!--]-->");
										} else {
											$$renderer.push("<!--[!-->");
											$$renderer.push("<!--]-->");
										}
										$$renderer.push(` `);
										EditorSettingsFooter($$renderer, {
											locale: uiLocale,
											themeMode,
											themePalette,
											onlocalechange: (locale) => uiLocale = locale,
											onthememodechange: changeThemeMode,
											onthemepalettechange: changeThemePalette,
											onabout: () => void showAbout()
										});
										$$renderer.push(`<!----> `);
										if (Sidebar_rail) {
											$$renderer.push("<!--[-->");
											Sidebar_rail($$renderer, {});
											$$renderer.push("<!--]-->");
										} else {
											$$renderer.push("<!--[!-->");
											$$renderer.push("<!--]-->");
										}
									},
									$$slots: { default: true }
								});
								$$renderer.push("<!--]-->");
							} else {
								$$renderer.push("<!--[!-->");
								$$renderer.push("<!--]-->");
							}
							$$renderer.push(` `);
							if (Sidebar_inset) {
								$$renderer.push("<!--[-->");
								Sidebar_inset($$renderer, {
									class: "editor-shell min-h-0 min-w-0 overflow-hidden",
									children: ($$renderer) => {
										EditorToolbar($$renderer, {
											reviewDirty,
											reviewSaving,
											reviewDisabled: snapshot.review?.error !== void 0,
											saveDisabled: !isDirty() || saving || validationBusy || validation?.success === false || clientError !== void 0,
											saving,
											saveLabel: labels().save,
											savingLabel: labels().saving,
											saveState: isDirty() ? labels().unsaved : operationMessage ?? labels().saved,
											isDirty: isDirty(),
											ondiscardreview: discardReview,
											onsavereview: () => void saveReview(),
											onsave: () => void save()
										});
										$$renderer.push(`<!----> `);
										if (selectedRow() === void 0) {
											$$renderer.push("<!--[0-->");
											if (Empty) {
												$$renderer.push("<!--[-->");
												Empty($$renderer, {
													children: ($$renderer) => {
														if (Empty_header) {
															$$renderer.push("<!--[-->");
															Empty_header($$renderer, {
																children: ($$renderer) => {
																	if (Empty_media) {
																		$$renderer.push("<!--[-->");
																		Empty_media($$renderer, {
																			variant: "icon",
																			class: "text-primary",
																			children: ($$renderer) => {
																				Message_square_text($$renderer, { "aria-hidden": "true" });
																			},
																			$$slots: { default: true }
																		});
																		$$renderer.push("<!--]-->");
																	} else {
																		$$renderer.push("<!--[!-->");
																		$$renderer.push("<!--]-->");
																	}
																	$$renderer.push(` `);
																	if (Empty_title) {
																		$$renderer.push("<!--[-->");
																		Empty_title($$renderer, {
																			class: "font-serif font-medium",
																			children: ($$renderer) => {
																				$$renderer.push(`<!---->${escape_html(labels().noSelection)}`);
																			},
																			$$slots: { default: true }
																		});
																		$$renderer.push("<!--]-->");
																	} else {
																		$$renderer.push("<!--[!-->");
																		$$renderer.push("<!--]-->");
																	}
																	$$renderer.push(` `);
																	if (Empty_description) {
																		$$renderer.push("<!--[-->");
																		Empty_description($$renderer, {
																			children: ($$renderer) => {
																				$$renderer.push(`<!---->Choose a message from the sidebar to review or edit its translation.`);
																			},
																			$$slots: { default: true }
																		});
																		$$renderer.push("<!--]-->");
																	} else {
																		$$renderer.push("<!--[!-->");
																		$$renderer.push("<!--]-->");
																	}
																},
																$$slots: { default: true }
															});
															$$renderer.push("<!--]-->");
														} else {
															$$renderer.push("<!--[!-->");
															$$renderer.push("<!--]-->");
														}
													},
													$$slots: { default: true }
												});
												$$renderer.push("<!--]-->");
											} else {
												$$renderer.push("<!--[!-->");
												$$renderer.push("<!--]-->");
											}
										} else {
											$$renderer.push("<!--[-1-->");
											$$renderer.push(`<div class="editor-content svelte-1uha8ag">`);
											MessageHeading($$renderer, {
												messageKey: selectedRow().key,
												description: selectedRow().description,
												tags: selectedRow().tags,
												locale: selectedLocale,
												layer: currentDocument()?.layer ?? "no document",
												inheritedFrom: currentCell()?.inheritedFrom,
												onrename: () => prepareMutation("rename-key"),
												onduplicate: () => prepareMutation("duplicate-key"),
												ondelete: () => prepareMutation("delete-key")
											});
											$$renderer.push(`<!----> `);
											ReviewWorkflow($$renderer, {
												state: currentReviewState(),
												dirty: reviewDirty,
												message: reviewMessage ?? "Project notes",
												disabled: snapshot.review?.error !== void 0,
												stale: currentIsStale(),
												terminologyCount: terminology.length,
												qualityCount: localeQuality().length,
												note: currentReview()?.note ?? "",
												qualityIssues: currentQuality(),
												suggestions: memorySuggestions(),
												onstatechange: setCurrentReviewState,
												onnotechange: setCurrentReviewNote,
												onterminology: () => terminologyDialogOpen = true,
												onreport: () => reportDialogOpen = true,
												onqualityfilter: () => filter = "quality",
												onsuggestion: applySuggestion
											});
											$$renderer.push(`<!----> `);
											EditorModeSwitcher($$renderer, {
												mode,
												simpleLabel: labels().simple,
												rawLabel: labels().raw,
												onchange: chooseMode
											});
											$$renderer.push(`<!----> `);
											TranslationEditor($$renderer, {
												mode,
												locale: selectedLocale,
												label: mode === "translation" ? localeName(selectedLocale) : currentDocument()?.path ?? "Resource document",
												value: editorText,
												resourceValue: currentCell()?.entry?.value ?? selectedRow().cells[snapshot.catalog.defaultLocale]?.entry?.value,
												missing: currentCell()?.entry === void 0,
												invalid: clientError !== void 0 || validation?.success === false,
												onresourcechange: editResourceValue,
												onrawchange: edit,
												onformatraw: formatRaw
											});
											$$renderer.push(`<!----> `);
											if (mode === "translation") {
												$$renderer.push("<!--[0-->");
												$$renderer.push(`<section class="message-preview svelte-1uha8ag" aria-live="polite"><header class="svelte-1uha8ag"><div class="svelte-1uha8ag"><strong class="svelte-1uha8ag">Preview</strong><span class="svelte-1uha8ag">Uses the same rules as the generated application message</span></div> <span class="preview-state svelte-1uha8ag">${escape_html(previewBusy ? "Compiling…" : previewAst === void 0 ? "Unavailable" : selectedLocale)}</span></header> `);
												if (previewAst !== void 0 && Object.keys(previewAst.inputs).length > 0) {
													$$renderer.push("<!--[0-->");
													$$renderer.push(`<div class="sample-inputs svelte-1uha8ag"><!--[-->`);
													const each_array_5 = ensure_array_like(Object.entries(previewAst.inputs));
													for (let $$index_5 = 0, $$length = each_array_5.length; $$index_5 < $$length; $$index_5++) {
														let [name, descriptor] = each_array_5[$$index_5];
														$$renderer.push(`<label class="svelte-1uha8ag"><span class="svelte-1uha8ag">${escape_html(name)}<small class="svelte-1uha8ag">${escape_html(descriptor.type)}</small></span><input${attr("value", previewSamples[name] ?? "")} class="svelte-1uha8ag"/></label>`);
													}
													$$renderer.push(`<!--]--></div>`);
												} else $$renderer.push("<!--[-1-->");
												$$renderer.push(`<!--]--> <div class="preview-canvas svelte-1uha8ag">`);
												if (previewBusy) {
													$$renderer.push("<!--[0-->");
													$$renderer.push(`<span class="preview-placeholder svelte-1uha8ag">Compiling the current draft…</span>`);
												} else if (previewError) {
													$$renderer.push("<!--[1-->");
													$$renderer.push(`<span class="preview-error svelte-1uha8ag">${escape_html(previewError)}</span>`);
												} else if (previewResult?.kind === "text") {
													$$renderer.push("<!--[2-->");
													$$renderer.push(`<p class="svelte-1uha8ag">${escape_html(previewResult.value)}</p>`);
												} else if (previewResult?.kind === "content") {
													$$renderer.push("<!--[3-->");
													$$renderer.push(`<div class="safe-content svelte-1uha8ag">`);
													previewNodes($$renderer, previewResult.nodes);
													$$renderer.push(`<!----></div>`);
												} else {
													$$renderer.push("<!--[-1-->");
													$$renderer.push(`<span class="preview-placeholder svelte-1uha8ag">Edit the message to build a preview.</span>`);
												}
												$$renderer.push(`<!--]--></div> <p class="safe-note svelte-1uha8ag">Semantic markup is displayed as a data tree. Names and attributes are never interpreted as trusted HTML.</p></section>`);
											} else $$renderer.push("<!--[-1-->");
											$$renderer.push(`<!--]--> `);
											ValidationPanel($$renderer, {
												busy: validationBusy,
												diagnostics: diagnostics(),
												clientError,
												errorCount: errorCount(),
												warningCount: warningCount(),
												validLabel: labels().valid,
												invalidLabel: labels().invalid,
												diagnosticsLabel: labels().diagnostics,
												schemaVersion: snapshot.catalog.schemaVersion,
												onselect: selectDiagnostic
											});
											$$renderer.push(`<!----></div>`);
										}
										$$renderer.push(`<!--]-->`);
									},
									$$slots: { default: true }
								});
								$$renderer.push("<!--]-->");
							} else {
								$$renderer.push("<!--[!-->");
								$$renderer.push("<!--]-->");
							}
						},
						$$slots: { default: true }
					});
					$$renderer.push("<!--]-->");
				} else {
					$$renderer.push("<!--[!-->");
					$$renderer.push("<!--]-->");
				}
			}
			$$renderer.push(`<!--]--> `);
			if (aboutDialogOpen) {
				$$renderer.push("<!--[0-->");
				{
					function footer($$renderer) {
						Button($$renderer, {
							variant: "outline",
							onclick: () => aboutDialogOpen = false,
							children: ($$renderer) => {
								$$renderer.push(`<!---->Close`);
							},
							$$slots: { default: true }
						});
						$$renderer.push(`<!----> `);
						Button($$renderer, {
							disabled: diagnosticBusy || aboutBusy,
							onclick: () => void createDiagnosticBundle(),
							children: ($$renderer) => {
								if (diagnosticBusy) {
									$$renderer.push("<!--[0-->");
									Spinner($$renderer, { "data-icon": "inline-start" });
								} else $$renderer.push("<!--[-1-->");
								$$renderer.push(`<!--]--> ${escape_html(diagnosticBusy ? "Creating bundle…" : "Create diagnostic bundle")}`);
							},
							$$slots: { default: true }
						});
						$$renderer.push(`<!---->`);
					}
					AppDialog($$renderer, {
						open: true,
						title: aboutInfo?.product ?? "Runic Translations Editor",
						description: "Application information and privacy-safe diagnostics.",
						onopenchange: (open) => aboutDialogOpen = open,
						footer,
						children: ($$renderer) => {
							$$renderer.push(`<div class="grid gap-4">`);
							if (aboutBusy) {
								$$renderer.push("<!--[0-->");
								$$renderer.push(`<div class="flex items-center gap-2 text-muted-foreground">`);
								Spinner($$renderer, {});
								$$renderer.push(`<!---->Reading application information…</div>`);
							} else if (aboutInfo !== void 0) {
								$$renderer.push("<!--[1-->");
								$$renderer.push(`<dl class="grid overflow-hidden rounded-xl border"><!--[-->`);
								const each_array_6 = ensure_array_like([
									["Version", aboutInfo.version],
									["Update channel", aboutInfo.updateChannel],
									["Source revision", aboutInfo.commit ?? "development build"],
									["Runtime", aboutInfo.runtime],
									["Runtime identifier", aboutInfo.runtimeIdentifier],
									["System", `${aboutInfo.operatingSystem} · ${aboutInfo.architecture}`]
								]);
								for (let $$index_6 = 0, $$length = each_array_6.length; $$index_6 < $$length; $$index_6++) {
									let item = each_array_6[$$index_6];
									$$renderer.push(`<div class="grid gap-1 border-b px-4 py-3 last:border-b-0 sm:grid-cols-[9rem_1fr] sm:gap-4"><dt class="text-muted-foreground">${escape_html(item[0])}</dt><dd class="m-0 overflow-wrap-anywhere font-mono text-xs">${escape_html(item[1])}</dd></div>`);
								}
								$$renderer.push(`<!--]--></dl>`);
							} else $$renderer.push("<!--[-1-->");
							$$renderer.push(`<!--]--> `);
							if (Alert) {
								$$renderer.push("<!--[-->");
								Alert($$renderer, {
									children: ($$renderer) => {
										if (Alert_title) {
											$$renderer.push("<!--[-->");
											Alert_title($$renderer, {
												children: ($$renderer) => {
													$$renderer.push(`<!---->Sanitized diagnostic bundle`);
												},
												$$slots: { default: true }
											});
											$$renderer.push("<!--]-->");
										} else {
											$$renderer.push("<!--[!-->");
											$$renderer.push("<!--]-->");
										}
										$$renderer.push(` `);
										if (Alert_description) {
											$$renderer.push("<!--[-->");
											Alert_description($$renderer, {
												children: ($$renderer) => {
													$$renderer.push(`<!---->The zip contains version/runtime information, catalog counts, and grouped diagnostic IDs. It excludes workspace paths, file names, messages, source JSON, and translations.`);
												},
												$$slots: { default: true }
											});
											$$renderer.push("<!--]-->");
										} else {
											$$renderer.push("<!--[!-->");
											$$renderer.push("<!--]-->");
										}
										$$renderer.push(` `);
										if (diagnosticMessage) {
											$$renderer.push("<!--[0-->");
											$$renderer.push(`<p class="text-sm text-primary" aria-live="polite">${escape_html(diagnosticMessage)}</p>`);
										} else $$renderer.push("<!--[-1-->");
										$$renderer.push(`<!--]-->`);
									},
									$$slots: { default: true }
								});
								$$renderer.push("<!--]-->");
							} else {
								$$renderer.push("<!--[!-->");
								$$renderer.push("<!--]-->");
							}
							$$renderer.push(` <p class="text-sm text-muted-foreground">Runic Text Resources is MIT licensed. The packaged application includes <code>LICENSE.txt</code> and <code>THIRD-PARTY-NOTICES.md</code>.</p></div>`);
						},
						$$slots: {
							footer: true,
							default: true
						}
					});
				}
			} else $$renderer.push("<!--[-1-->");
			$$renderer.push(`<!--]--> `);
			if (terminologyDialogOpen) {
				$$renderer.push("<!--[0-->");
				{
					function footer($$renderer) {
						Button($$renderer, {
							variant: "outline",
							onclick: () => terminologyDialogOpen = false,
							children: ($$renderer) => {
								$$renderer.push(`<!---->Done`);
							},
							$$slots: { default: true }
						});
						$$renderer.push(`<!----> `);
						Button($$renderer, {
							disabled: !reviewDirty || reviewSaving,
							onclick: () => void saveReview(),
							children: ($$renderer) => {
								if (reviewSaving) {
									$$renderer.push("<!--[0-->");
									Spinner($$renderer, { "data-icon": "inline-start" });
								} else $$renderer.push("<!--[-1-->");
								$$renderer.push(`<!--]-->Save workflow`);
							},
							$$slots: { default: true }
						});
						$$renderer.push(`<!---->`);
					}
					AppDialog($$renderer, {
						open: true,
						title: "Project terminology",
						description: "Terms stay in the optional versioned sidecar and are checked locally. Nothing is sent to a service.",
						class: "sm:max-w-4xl",
						onopenchange: (open) => terminologyDialogOpen = open,
						footer,
						children: ($$renderer) => {
							if (Field_group) {
								$$renderer.push("<!--[-->");
								Field_group($$renderer, {
									class: "grid gap-3 sm:grid-cols-2",
									children: ($$renderer) => {
										if (Field) {
											$$renderer.push("<!--[-->");
											Field($$renderer, {
												children: ($$renderer) => {
													if (Field_label) {
														$$renderer.push("<!--[-->");
														Field_label($$renderer, {
															for: "term-source",
															children: ($$renderer) => {
																$$renderer.push(`<!---->Source term`);
															},
															$$slots: { default: true }
														});
														$$renderer.push("<!--]-->");
													} else {
														$$renderer.push("<!--[!-->");
														$$renderer.push("<!--]-->");
													}
													Input($$renderer, {
														id: "term-source",
														placeholder: "Save",
														get value() {
															return termSource;
														},
														set value($$value) {
															termSource = $$value;
															$$settled = false;
														}
													});
													$$renderer.push(`<!---->`);
												},
												$$slots: { default: true }
											});
											$$renderer.push("<!--]-->");
										} else {
											$$renderer.push("<!--[!-->");
											$$renderer.push("<!--]-->");
										}
										$$renderer.push(` `);
										if (Field) {
											$$renderer.push("<!--[-->");
											Field($$renderer, {
												children: ($$renderer) => {
													if (Field_label) {
														$$renderer.push("<!--[-->");
														Field_label($$renderer, {
															for: "term-preferred",
															children: ($$renderer) => {
																$$renderer.push(`<!---->Preferred translation`);
															},
															$$slots: { default: true }
														});
														$$renderer.push("<!--]-->");
													} else {
														$$renderer.push("<!--[!-->");
														$$renderer.push("<!--]-->");
													}
													Input($$renderer, {
														id: "term-preferred",
														placeholder: "Speichern",
														get value() {
															return termPreferred;
														},
														set value($$value) {
															termPreferred = $$value;
															$$settled = false;
														}
													});
													$$renderer.push(`<!---->`);
												},
												$$slots: { default: true }
											});
											$$renderer.push("<!--]-->");
										} else {
											$$renderer.push("<!--[!-->");
											$$renderer.push("<!--]-->");
										}
										$$renderer.push(` `);
										if (Field) {
											$$renderer.push("<!--[-->");
											Field($$renderer, {
												children: ($$renderer) => {
													if (Field_label) {
														$$renderer.push("<!--[-->");
														Field_label($$renderer, {
															for: "term-locale",
															children: ($$renderer) => {
																$$renderer.push(`<!---->Locale`);
															},
															$$slots: { default: true }
														});
														$$renderer.push("<!--]-->");
													} else {
														$$renderer.push("<!--[!-->");
														$$renderer.push("<!--]-->");
													}
													Input($$renderer, {
														id: "term-locale",
														placeholder: "Optional, e.g. de",
														get value() {
															return termLocale;
														},
														set value($$value) {
															termLocale = $$value;
															$$settled = false;
														}
													});
													$$renderer.push(`<!---->`);
												},
												$$slots: { default: true }
											});
											$$renderer.push("<!--]-->");
										} else {
											$$renderer.push("<!--[!-->");
											$$renderer.push("<!--]-->");
										}
										$$renderer.push(` `);
										if (Field) {
											$$renderer.push("<!--[-->");
											Field($$renderer, {
												children: ($$renderer) => {
													if (Field_label) {
														$$renderer.push("<!--[-->");
														Field_label($$renderer, {
															for: "term-note",
															children: ($$renderer) => {
																$$renderer.push(`<!---->Note`);
															},
															$$slots: { default: true }
														});
														$$renderer.push("<!--]-->");
													} else {
														$$renderer.push("<!--[!-->");
														$$renderer.push("<!--]-->");
													}
													Input($$renderer, {
														id: "term-note",
														placeholder: "Optional usage guidance",
														get value() {
															return termNote;
														},
														set value($$value) {
															termNote = $$value;
															$$settled = false;
														}
													});
													$$renderer.push(`<!---->`);
												},
												$$slots: { default: true }
											});
											$$renderer.push("<!--]-->");
										} else {
											$$renderer.push("<!--[!-->");
											$$renderer.push("<!--]-->");
										}
										$$renderer.push(` `);
										Button($$renderer, {
											class: "justify-self-start sm:col-span-2",
											variant: "outline",
											disabled: termSource.trim() === "" || termPreferred.trim() === "",
											onclick: addTerm,
											children: ($$renderer) => {
												Plus($$renderer, { "data-icon": "inline-start" });
												$$renderer.push(`<!---->Add term`);
											},
											$$slots: { default: true }
										});
										$$renderer.push(`<!---->`);
									},
									$$slots: { default: true }
								});
								$$renderer.push("<!--]-->");
							} else {
								$$renderer.push("<!--[!-->");
								$$renderer.push("<!--]-->");
							}
							$$renderer.push(` <div class="mt-5 grid overflow-hidden rounded-xl border">`);
							const each_array_7 = ensure_array_like(terminology);
							if (each_array_7.length !== 0) {
								$$renderer.push("<!--[-->");
								for (let index = 0, $$length = each_array_7.length; index < $$length; index++) {
									let term = each_array_7[index];
									$$renderer.push(`<div class="grid grid-cols-[minmax(0,1fr)_auto] items-center gap-3 border-b px-4 py-3 last:border-b-0"><div class="min-w-0"><div class="flex min-w-0 flex-wrap items-center gap-2"><strong>${escape_html(term.source)}</strong><span class="text-muted-foreground">→</span><strong>${escape_html(term.preferred)}</strong>`);
									if (term.locale) {
										$$renderer.push("<!--[0-->");
										Badge($$renderer, {
											variant: "outline",
											children: ($$renderer) => {
												$$renderer.push(`<!---->${escape_html(term.locale)}`);
											},
											$$slots: { default: true }
										});
									} else $$renderer.push("<!--[-1-->");
									$$renderer.push(`<!--]--></div> <p class="truncate text-xs text-muted-foreground">${escape_html(term.note ?? "No note")}</p></div> `);
									Button($$renderer, {
										variant: "ghost",
										size: "icon-xs",
										"aria-label": "Remove term " + term.source,
										onclick: () => removeTerm(index),
										children: ($$renderer) => {
											Trash_2($$renderer, {});
										},
										$$slots: { default: true }
									});
									$$renderer.push(`<!----></div>`);
								}
							} else {
								$$renderer.push("<!--[!-->");
								$$renderer.push(`<p class="p-6 text-center text-sm text-muted-foreground">No terminology entries yet.</p>`);
							}
							$$renderer.push(`<!--]--></div>`);
						},
						$$slots: {
							footer: true,
							default: true
						}
					});
				}
			} else $$renderer.push("<!--[-1-->");
			$$renderer.push(`<!--]--> `);
			if (reportDialogOpen) {
				$$renderer.push("<!--[0-->");
				{
					function footer($$renderer) {
						Button($$renderer, {
							variant: "outline",
							onclick: () => reportDialogOpen = false,
							children: ($$renderer) => {
								$$renderer.push(`<!---->Close`);
							},
							$$slots: { default: true }
						});
					}
					AppDialog($$renderer, {
						open: true,
						title: `${selectedLocale} quality report`,
						description: `${localeQuality().length} findings across ${qualityKeySet().size} messages. CSV is ordered by key and finding kind.`,
						class: "sm:max-w-4xl",
						onopenchange: (open) => reportDialogOpen = open,
						footer,
						children: ($$renderer) => {
							Textarea($$renderer, {
								class: "min-h-[26rem] font-mono text-xs",
								"aria-label": "Quality report CSV",
								readonly: true,
								value: qualityReportCsv(localeQuality())
							});
						},
						$$slots: {
							footer: true,
							default: true
						}
					});
				}
			} else $$renderer.push("<!--[-1-->");
			$$renderer.push(`<!--]--> `);
			if (repairDocument !== void 0) {
				$$renderer.push("<!--[0-->");
				{
					function footer($$renderer) {
						Button($$renderer, {
							variant: "outline",
							disabled: repairBusy,
							onclick: () => repairDocument = void 0,
							children: ($$renderer) => {
								$$renderer.push(`<!---->Cancel`);
							},
							$$slots: { default: true }
						});
						$$renderer.push(`<!----> `);
						Button($$renderer, {
							disabled: repairBusy,
							onclick: () => void saveRepair(),
							children: ($$renderer) => {
								if (repairBusy) {
									$$renderer.push("<!--[0-->");
									Spinner($$renderer, { "data-icon": "inline-start" });
								} else $$renderer.push("<!--[-1-->");
								$$renderer.push(`<!--]-->${escape_html(repairBusy ? "Validating…" : "Validate and save")}`);
							},
							$$slots: { default: true }
						});
						$$renderer.push(`<!---->`);
					}
					AppDialog($$renderer, {
						open: true,
						title: repairDocument.path,
						description: "Edit the raw JSON below. The canonical compiler must accept it before it can replace the file.",
						class: "sm:max-w-4xl",
						showCloseButton: !repairBusy,
						onopenchange: (open) => {
							if (!open && !repairBusy) repairDocument = void 0;
						},
						footer,
						children: ($$renderer) => {
							Textarea($$renderer, {
								class: "min-h-[26rem] font-mono text-xs",
								"aria-label": "Malformed JSON document",
								spellcheck: false,
								get value() {
									return repairText;
								},
								set value($$value) {
									repairText = $$value;
									$$settled = false;
								}
							});
							$$renderer.push(`<!----> `);
							if (repairMessage) {
								$$renderer.push("<!--[0-->");
								if (Alert) {
									$$renderer.push("<!--[-->");
									Alert($$renderer, {
										variant: "destructive",
										class: "mt-4",
										children: ($$renderer) => {
											if (Alert_title) {
												$$renderer.push("<!--[-->");
												Alert_title($$renderer, {
													children: ($$renderer) => {
														$$renderer.push(`<!---->Repair failed`);
													},
													$$slots: { default: true }
												});
												$$renderer.push("<!--]-->");
											} else {
												$$renderer.push("<!--[!-->");
												$$renderer.push("<!--]-->");
											}
											if (Alert_description) {
												$$renderer.push("<!--[-->");
												Alert_description($$renderer, {
													children: ($$renderer) => {
														$$renderer.push(`<!---->${escape_html(repairMessage)}`);
													},
													$$slots: { default: true }
												});
												$$renderer.push("<!--]-->");
											} else {
												$$renderer.push("<!--[!-->");
												$$renderer.push("<!--]-->");
											}
										},
										$$slots: { default: true }
									});
									$$renderer.push("<!--]-->");
								} else {
									$$renderer.push("<!--[!-->");
									$$renderer.push("<!--]-->");
								}
							} else $$renderer.push("<!--[-1-->");
							$$renderer.push(`<!--]-->`);
						},
						$$slots: {
							footer: true,
							default: true
						}
					});
				}
			} else $$renderer.push("<!--[-1-->");
			$$renderer.push(`<!--]--> `);
			if (openDialogOpen) {
				$$renderer.push("<!--[0-->");
				{
					function footer($$renderer) {
						Button($$renderer, {
							variant: "outline",
							disabled: openingWorkspace,
							onclick: () => openDialogOpen = false,
							children: ($$renderer) => {
								$$renderer.push(`<!---->Cancel`);
							},
							$$slots: { default: true }
						});
						$$renderer.push(`<!----> `);
						Button($$renderer, {
							disabled: openingWorkspace || openDirectory.trim() === "",
							onclick: () => void openWorkspace(),
							children: ($$renderer) => {
								if (openingWorkspace) {
									$$renderer.push("<!--[0-->");
									Spinner($$renderer, { "data-icon": "inline-start" });
								} else $$renderer.push("<!--[-1-->");
								$$renderer.push(`<!--]-->${escape_html(openingWorkspace ? "Opening…" : "Open workspace")}`);
							},
							$$slots: { default: true }
						});
						$$renderer.push(`<!---->`);
					}
					AppDialog($$renderer, {
						open: true,
						title: "Open translation project",
						description: "Catalogs are discovered below this workspace boundary. You will choose one if several are found.",
						showCloseButton: !openingWorkspace && !pickingWorkspace,
						onopenchange: (open) => {
							if (!openingWorkspace && !pickingWorkspace) openDialogOpen = open;
						},
						footer,
						children: ($$renderer) => {
							if (Field) {
								$$renderer.push("<!--[-->");
								Field($$renderer, {
									children: ($$renderer) => {
										if (Field_label) {
											$$renderer.push("<!--[-->");
											Field_label($$renderer, {
												for: "dialog-open-directory",
												children: ($$renderer) => {
													$$renderer.push(`<!---->Workspace directory`);
												},
												$$slots: { default: true }
											});
											$$renderer.push("<!--]-->");
										} else {
											$$renderer.push("<!--[!-->");
											$$renderer.push("<!--]-->");
										}
										$$renderer.push(` <div class="flex flex-col gap-2 sm:flex-row">`);
										Input($$renderer, {
											id: "dialog-open-directory",
											class: "min-w-0 flex-1",
											autocomplete: "off",
											get value() {
												return openDirectory;
											},
											set value($$value) {
												openDirectory = $$value;
												$$settled = false;
											}
										});
										$$renderer.push(`<!----> `);
										Button($$renderer, {
											variant: "outline",
											disabled: pickingWorkspace || openingWorkspace,
											onclick: () => void pickWorkspace(),
											children: ($$renderer) => {
												$$renderer.push(`<!---->${escape_html(pickingWorkspace ? "Choosing…" : "Browse…")}`);
											},
											$$slots: { default: true }
										});
										$$renderer.push(`<!----></div>`);
									},
									$$slots: { default: true }
								});
								$$renderer.push("<!--]-->");
							} else {
								$$renderer.push("<!--[!-->");
								$$renderer.push("<!--]-->");
							}
							$$renderer.push(` `);
							if (clientError) {
								$$renderer.push("<!--[0-->");
								if (Alert) {
									$$renderer.push("<!--[-->");
									Alert($$renderer, {
										variant: "destructive",
										class: "mt-4",
										children: ($$renderer) => {
											if (Alert_title) {
												$$renderer.push("<!--[-->");
												Alert_title($$renderer, {
													children: ($$renderer) => {
														$$renderer.push(`<!---->Could not open workspace`);
													},
													$$slots: { default: true }
												});
												$$renderer.push("<!--]-->");
											} else {
												$$renderer.push("<!--[!-->");
												$$renderer.push("<!--]-->");
											}
											if (Alert_description) {
												$$renderer.push("<!--[-->");
												Alert_description($$renderer, {
													children: ($$renderer) => {
														$$renderer.push(`<!---->${escape_html(clientError)}`);
													},
													$$slots: { default: true }
												});
												$$renderer.push("<!--]-->");
											} else {
												$$renderer.push("<!--[!-->");
												$$renderer.push("<!--]-->");
											}
										},
										$$slots: { default: true }
									});
									$$renderer.push("<!--]-->");
								} else {
									$$renderer.push("<!--[!-->");
									$$renderer.push("<!--]-->");
								}
							} else $$renderer.push("<!--[-1-->");
							$$renderer.push(`<!--]-->`);
						},
						$$slots: {
							footer: true,
							default: true
						}
					});
				}
			} else $$renderer.push("<!--[-1-->");
			$$renderer.push(`<!--]--> `);
			if (mutationDialogOpen && snapshot?.catalog !== void 0) {
				$$renderer.push("<!--[0-->");
				{
					function footer($$renderer) {
						Button($$renderer, {
							variant: "outline",
							disabled: mutationBusy,
							onclick: () => mutationDialogOpen = false,
							children: ($$renderer) => {
								$$renderer.push(`<!---->Cancel`);
							},
							$$slots: { default: true }
						});
						$$renderer.push(`<!----> `);
						if (mutationPreview?.ok) {
							$$renderer.push("<!--[0-->");
							Button($$renderer, {
								variant: mutationKind === "remove-locale" || mutationKind === "delete-key" ? "destructive" : "default",
								disabled: mutationBusy,
								onclick: () => void applyMutation(),
								children: ($$renderer) => {
									if (mutationBusy) {
										$$renderer.push("<!--[0-->");
										Spinner($$renderer, { "data-icon": "inline-start" });
									} else $$renderer.push("<!--[-1-->");
									$$renderer.push(`<!--]-->${escape_html(mutationBusy ? "Committing…" : "Commit change")}`);
								},
								$$slots: { default: true }
							});
						} else {
							$$renderer.push("<!--[-1-->");
							Button($$renderer, {
								disabled: mutationBusy,
								onclick: () => void previewMutation(),
								children: ($$renderer) => {
									if (mutationBusy) {
										$$renderer.push("<!--[0-->");
										Spinner($$renderer, { "data-icon": "inline-start" });
									} else $$renderer.push("<!--[-1-->");
									$$renderer.push(`<!--]-->${escape_html(mutationBusy ? "Checking…" : "Preview change")}`);
								},
								$$slots: { default: true }
							});
						}
						$$renderer.push(`<!--]-->`);
					}
					AppDialog($$renderer, {
						open: true,
						title: mutationTitle(mutationKind),
						description: "Compiler-backed workspace change. Review the affected files before committing.",
						class: "sm:max-w-3xl",
						showCloseButton: !mutationBusy,
						onopenchange: (open) => {
							if (!mutationBusy) mutationDialogOpen = open;
						},
						footer,
						children: ($$renderer) => {
							if (Field_group) {
								$$renderer.push("<!--[-->");
								Field_group($$renderer, {
									class: "gap-4",
									children: ($$renderer) => {
										if (mutationKind === "add-locale" || mutationKind === "remove-locale" || mutationKind === "set-fallback") {
											$$renderer.push("<!--[0-->");
											if (Field) {
												$$renderer.push("<!--[-->");
												Field($$renderer, {
													children: ($$renderer) => {
														if (Field_label) {
															$$renderer.push("<!--[-->");
															Field_label($$renderer, {
																for: "language-operation",
																children: ($$renderer) => {
																	$$renderer.push(`<!---->Language operation`);
																},
																$$slots: { default: true }
															});
															$$renderer.push("<!--]-->");
														} else {
															$$renderer.push("<!--[!-->");
															$$renderer.push("<!--]-->");
														}
														$$renderer.push(` `);
														if (Select) {
															$$renderer.push("<!--[-->");
															Select($$renderer, {
																type: "single",
																value: mutationKind,
																onValueChange: changeMutationKind,
																children: ($$renderer) => {
																	if (Select_trigger) {
																		$$renderer.push("<!--[-->");
																		Select_trigger($$renderer, {
																			id: "language-operation",
																			class: "w-full",
																			children: ($$renderer) => {
																				$$renderer.push(`<!---->${escape_html(mutationTitle(mutationKind))}`);
																			},
																			$$slots: { default: true }
																		});
																		$$renderer.push("<!--]-->");
																	} else {
																		$$renderer.push("<!--[!-->");
																		$$renderer.push("<!--]-->");
																	}
																	$$renderer.push(` `);
																	if (Select_content) {
																		$$renderer.push("<!--[-->");
																		Select_content($$renderer, {
																			children: ($$renderer) => {
																				if (Select_group) {
																					$$renderer.push("<!--[-->");
																					Select_group($$renderer, {
																						children: ($$renderer) => {
																							if (Select_label) {
																								$$renderer.push("<!--[-->");
																								Select_label($$renderer, {
																									children: ($$renderer) => {
																										$$renderer.push(`<!---->Language operation`);
																									},
																									$$slots: { default: true }
																								});
																								$$renderer.push("<!--]-->");
																							} else {
																								$$renderer.push("<!--[!-->");
																								$$renderer.push("<!--]-->");
																							}
																							$$renderer.push(` `);
																							if (Select_item) {
																								$$renderer.push("<!--[-->");
																								Select_item($$renderer, {
																									value: "add-locale",
																									label: "Add a language",
																									children: ($$renderer) => {
																										$$renderer.push(`<!---->Add a language`);
																									},
																									$$slots: { default: true }
																								});
																								$$renderer.push("<!--]-->");
																							} else {
																								$$renderer.push("<!--[!-->");
																								$$renderer.push("<!--]-->");
																							}
																							$$renderer.push(` `);
																							if (Select_item) {
																								$$renderer.push("<!--[-->");
																								Select_item($$renderer, {
																									value: "remove-locale",
																									label: "Remove a language",
																									children: ($$renderer) => {
																										$$renderer.push(`<!---->Remove a language`);
																									},
																									$$slots: { default: true }
																								});
																								$$renderer.push("<!--]-->");
																							} else {
																								$$renderer.push("<!--[!-->");
																								$$renderer.push("<!--]-->");
																							}
																							$$renderer.push(` `);
																							if (Select_item) {
																								$$renderer.push("<!--[-->");
																								Select_item($$renderer, {
																									value: "set-fallback",
																									label: "Change a fallback",
																									children: ($$renderer) => {
																										$$renderer.push(`<!---->Change a fallback`);
																									},
																									$$slots: { default: true }
																								});
																								$$renderer.push("<!--]-->");
																							} else {
																								$$renderer.push("<!--[!-->");
																								$$renderer.push("<!--]-->");
																							}
																						},
																						$$slots: { default: true }
																					});
																					$$renderer.push("<!--]-->");
																				} else {
																					$$renderer.push("<!--[!-->");
																					$$renderer.push("<!--]-->");
																				}
																			},
																			$$slots: { default: true }
																		});
																		$$renderer.push("<!--]-->");
																	} else {
																		$$renderer.push("<!--[!-->");
																		$$renderer.push("<!--]-->");
																	}
																},
																$$slots: { default: true }
															});
															$$renderer.push("<!--]-->");
														} else {
															$$renderer.push("<!--[!-->");
															$$renderer.push("<!--]-->");
														}
													},
													$$slots: { default: true }
												});
												$$renderer.push("<!--]-->");
											} else {
												$$renderer.push("<!--[!-->");
												$$renderer.push("<!--]-->");
											}
										} else $$renderer.push("<!--[-1-->");
										$$renderer.push(`<!--]--> `);
										if (mutationKind === "add-locale") {
											$$renderer.push("<!--[0-->");
											$$renderer.push(`<div class="grid gap-4 sm:grid-cols-2">`);
											if (Field) {
												$$renderer.push("<!--[-->");
												Field($$renderer, {
													children: ($$renderer) => {
														if (Field_label) {
															$$renderer.push("<!--[-->");
															Field_label($$renderer, {
																for: "mutation-locale",
																children: ($$renderer) => {
																	$$renderer.push(`<!---->New locale tag`);
																},
																$$slots: { default: true }
															});
															$$renderer.push("<!--]-->");
														} else {
															$$renderer.push("<!--[!-->");
															$$renderer.push("<!--]-->");
														}
														Input($$renderer, {
															id: "mutation-locale",
															oninput: invalidateMutationPreview,
															placeholder: "fr-FR",
															autocomplete: "off",
															get value() {
																return mutationLocale;
															},
															set value($$value) {
																mutationLocale = $$value;
																$$settled = false;
															}
														});
														$$renderer.push(`<!---->`);
													},
													$$slots: { default: true }
												});
												$$renderer.push("<!--]-->");
											} else {
												$$renderer.push("<!--[!-->");
												$$renderer.push("<!--]-->");
											}
											$$renderer.push(` `);
											if (Field) {
												$$renderer.push("<!--[-->");
												Field($$renderer, {
													children: ($$renderer) => {
														if (Field_label) {
															$$renderer.push("<!--[-->");
															Field_label($$renderer, {
																for: "mutation-fallback",
																children: ($$renderer) => {
																	$$renderer.push(`<!---->Fallback`);
																},
																$$slots: { default: true }
															});
															$$renderer.push("<!--]-->");
														} else {
															$$renderer.push("<!--[!-->");
															$$renderer.push("<!--]-->");
														}
														$$renderer.push(` `);
														if (Select) {
															$$renderer.push("<!--[-->");
															Select($$renderer, {
																type: "single",
																value: mutationFallback,
																onValueChange: (value) => {
																	mutationFallback = value;
																	invalidateMutationPreview();
																},
																children: ($$renderer) => {
																	if (Select_trigger) {
																		$$renderer.push("<!--[-->");
																		Select_trigger($$renderer, {
																			id: "mutation-fallback",
																			class: "w-full",
																			children: ($$renderer) => {
																				$$renderer.push(`<!---->${escape_html(mutationFallback)} · ${escape_html(localeName(mutationFallback))}`);
																			},
																			$$slots: { default: true }
																		});
																		$$renderer.push("<!--]-->");
																	} else {
																		$$renderer.push("<!--[!-->");
																		$$renderer.push("<!--]-->");
																	}
																	$$renderer.push(` `);
																	if (Select_content) {
																		$$renderer.push("<!--[-->");
																		Select_content($$renderer, {
																			children: ($$renderer) => {
																				if (Select_group) {
																					$$renderer.push("<!--[-->");
																					Select_group($$renderer, {
																						children: ($$renderer) => {
																							$$renderer.push(`<!--[-->`);
																							const each_array_8 = ensure_array_like(snapshot.catalog.locales);
																							for (let $$index_8 = 0, $$length = each_array_8.length; $$index_8 < $$length; $$index_8++) {
																								let locale = each_array_8[$$index_8];
																								if (Select_item) {
																									$$renderer.push("<!--[-->");
																									Select_item($$renderer, {
																										value: locale.tag,
																										label: `${locale.tag} · ${localeName(locale.tag)}`,
																										children: ($$renderer) => {
																											$$renderer.push(`<!---->${escape_html(locale.tag)} · ${escape_html(localeName(locale.tag))}`);
																										},
																										$$slots: { default: true }
																									});
																									$$renderer.push("<!--]-->");
																								} else {
																									$$renderer.push("<!--[!-->");
																									$$renderer.push("<!--]-->");
																								}
																							}
																							$$renderer.push(`<!--]-->`);
																						},
																						$$slots: { default: true }
																					});
																					$$renderer.push("<!--]-->");
																				} else {
																					$$renderer.push("<!--[!-->");
																					$$renderer.push("<!--]-->");
																				}
																			},
																			$$slots: { default: true }
																		});
																		$$renderer.push("<!--]-->");
																	} else {
																		$$renderer.push("<!--[!-->");
																		$$renderer.push("<!--]-->");
																	}
																},
																$$slots: { default: true }
															});
															$$renderer.push("<!--]-->");
														} else {
															$$renderer.push("<!--[!-->");
															$$renderer.push("<!--]-->");
														}
													},
													$$slots: { default: true }
												});
												$$renderer.push("<!--]-->");
											} else {
												$$renderer.push("<!--[!-->");
												$$renderer.push("<!--]-->");
											}
											$$renderer.push(` `);
											if (Field) {
												$$renderer.push("<!--[-->");
												Field($$renderer, {
													children: ($$renderer) => {
														if (Field_label) {
															$$renderer.push("<!--[-->");
															Field_label($$renderer, {
																for: "mutation-copy-from",
																children: ($$renderer) => {
																	$$renderer.push(`<!---->Copy starter values from`);
																},
																$$slots: { default: true }
															});
															$$renderer.push("<!--]-->");
														} else {
															$$renderer.push("<!--[!-->");
															$$renderer.push("<!--]-->");
														}
														$$renderer.push(` `);
														if (Select) {
															$$renderer.push("<!--[-->");
															Select($$renderer, {
																type: "single",
																value: mutationCopyFrom,
																onValueChange: (value) => {
																	mutationCopyFrom = value;
																	invalidateMutationPreview();
																},
																children: ($$renderer) => {
																	if (Select_trigger) {
																		$$renderer.push("<!--[-->");
																		Select_trigger($$renderer, {
																			id: "mutation-copy-from",
																			class: "w-full",
																			children: ($$renderer) => {
																				$$renderer.push(`<!---->${escape_html(mutationCopyFrom)} · ${escape_html(localeName(mutationCopyFrom))}`);
																			},
																			$$slots: { default: true }
																		});
																		$$renderer.push("<!--]-->");
																	} else {
																		$$renderer.push("<!--[!-->");
																		$$renderer.push("<!--]-->");
																	}
																	$$renderer.push(` `);
																	if (Select_content) {
																		$$renderer.push("<!--[-->");
																		Select_content($$renderer, {
																			children: ($$renderer) => {
																				if (Select_group) {
																					$$renderer.push("<!--[-->");
																					Select_group($$renderer, {
																						children: ($$renderer) => {
																							$$renderer.push(`<!--[-->`);
																							const each_array_9 = ensure_array_like(snapshot.catalog.locales);
																							for (let $$index_9 = 0, $$length = each_array_9.length; $$index_9 < $$length; $$index_9++) {
																								let locale = each_array_9[$$index_9];
																								if (Select_item) {
																									$$renderer.push("<!--[-->");
																									Select_item($$renderer, {
																										value: locale.tag,
																										label: `${locale.tag} · ${localeName(locale.tag)}`,
																										children: ($$renderer) => {
																											$$renderer.push(`<!---->${escape_html(locale.tag)} · ${escape_html(localeName(locale.tag))}`);
																										},
																										$$slots: { default: true }
																									});
																									$$renderer.push("<!--]-->");
																								} else {
																									$$renderer.push("<!--[!-->");
																									$$renderer.push("<!--]-->");
																								}
																							}
																							$$renderer.push(`<!--]-->`);
																						},
																						$$slots: { default: true }
																					});
																					$$renderer.push("<!--]-->");
																				} else {
																					$$renderer.push("<!--[!-->");
																					$$renderer.push("<!--]-->");
																				}
																			},
																			$$slots: { default: true }
																		});
																		$$renderer.push("<!--]-->");
																	} else {
																		$$renderer.push("<!--[!-->");
																		$$renderer.push("<!--]-->");
																	}
																},
																$$slots: { default: true }
															});
															$$renderer.push("<!--]-->");
														} else {
															$$renderer.push("<!--[!-->");
															$$renderer.push("<!--]-->");
														}
														$$renderer.push(` `);
														if (Field_description) {
															$$renderer.push("<!--[-->");
															Field_description($$renderer, {
																children: ($$renderer) => {
																	$$renderer.push(`<!---->Copied text keeps the new catalog compiler-valid and can then be translated.`);
																},
																$$slots: { default: true }
															});
															$$renderer.push("<!--]-->");
														} else {
															$$renderer.push("<!--[!-->");
															$$renderer.push("<!--]-->");
														}
													},
													$$slots: { default: true }
												});
												$$renderer.push("<!--]-->");
											} else {
												$$renderer.push("<!--[!-->");
												$$renderer.push("<!--]-->");
											}
											$$renderer.push(` `);
											if (Field) {
												$$renderer.push("<!--[-->");
												Field($$renderer, {
													children: ($$renderer) => {
														if (Field_label) {
															$$renderer.push("<!--[-->");
															Field_label($$renderer, {
																for: "mutation-layer",
																children: ($$renderer) => {
																	$$renderer.push(`<!---->Layer`);
																},
																$$slots: { default: true }
															});
															$$renderer.push("<!--]-->");
														} else {
															$$renderer.push("<!--[!-->");
															$$renderer.push("<!--]-->");
														}
														$$renderer.push(` `);
														if (Select) {
															$$renderer.push("<!--[-->");
															Select($$renderer, {
																type: "single",
																value: mutationLayer,
																onValueChange: (value) => {
																	mutationLayer = value;
																	invalidateMutationPreview();
																},
																children: ($$renderer) => {
																	if (Select_trigger) {
																		$$renderer.push("<!--[-->");
																		Select_trigger($$renderer, {
																			id: "mutation-layer",
																			class: "w-full",
																			children: ($$renderer) => {
																				$$renderer.push(`<!---->${escape_html(mutationLayer)}`);
																			},
																			$$slots: { default: true }
																		});
																		$$renderer.push("<!--]-->");
																	} else {
																		$$renderer.push("<!--[!-->");
																		$$renderer.push("<!--]-->");
																	}
																	$$renderer.push(` `);
																	if (Select_content) {
																		$$renderer.push("<!--[-->");
																		Select_content($$renderer, {
																			children: ($$renderer) => {
																				if (Select_group) {
																					$$renderer.push("<!--[-->");
																					Select_group($$renderer, {
																						children: ($$renderer) => {
																							$$renderer.push(`<!--[-->`);
																							const each_array_10 = ensure_array_like(snapshot.catalog.layers);
																							for (let $$index_10 = 0, $$length = each_array_10.length; $$index_10 < $$length; $$index_10++) {
																								let layer = each_array_10[$$index_10];
																								if (Select_item) {
																									$$renderer.push("<!--[-->");
																									Select_item($$renderer, {
																										value: layer.name,
																										label: layer.name,
																										children: ($$renderer) => {
																											$$renderer.push(`<!---->${escape_html(layer.name)}`);
																										},
																										$$slots: { default: true }
																									});
																									$$renderer.push("<!--]-->");
																								} else {
																									$$renderer.push("<!--[!-->");
																									$$renderer.push("<!--]-->");
																								}
																							}
																							$$renderer.push(`<!--]-->`);
																						},
																						$$slots: { default: true }
																					});
																					$$renderer.push("<!--]-->");
																				} else {
																					$$renderer.push("<!--[!-->");
																					$$renderer.push("<!--]-->");
																				}
																			},
																			$$slots: { default: true }
																		});
																		$$renderer.push("<!--]-->");
																	} else {
																		$$renderer.push("<!--[!-->");
																		$$renderer.push("<!--]-->");
																	}
																},
																$$slots: { default: true }
															});
															$$renderer.push("<!--]-->");
														} else {
															$$renderer.push("<!--[!-->");
															$$renderer.push("<!--]-->");
														}
													},
													$$slots: { default: true }
												});
												$$renderer.push("<!--]-->");
											} else {
												$$renderer.push("<!--[!-->");
												$$renderer.push("<!--]-->");
											}
											$$renderer.push(`</div>`);
										} else if (mutationKind === "remove-locale") {
											$$renderer.push("<!--[1-->");
											$$renderer.push(`<div class="grid gap-4 sm:grid-cols-2">`);
											if (Field) {
												$$renderer.push("<!--[-->");
												Field($$renderer, {
													children: ($$renderer) => {
														if (Field_label) {
															$$renderer.push("<!--[-->");
															Field_label($$renderer, {
																for: "remove-locale",
																children: ($$renderer) => {
																	$$renderer.push(`<!---->Language to remove`);
																},
																$$slots: { default: true }
															});
															$$renderer.push("<!--]-->");
														} else {
															$$renderer.push("<!--[!-->");
															$$renderer.push("<!--]-->");
														}
														$$renderer.push(` `);
														if (Select) {
															$$renderer.push("<!--[-->");
															Select($$renderer, {
																type: "single",
																value: mutationLocale,
																onValueChange: (value) => {
																	mutationLocale = value;
																	invalidateMutationPreview();
																},
																children: ($$renderer) => {
																	if (Select_trigger) {
																		$$renderer.push("<!--[-->");
																		Select_trigger($$renderer, {
																			id: "remove-locale",
																			class: "w-full",
																			children: ($$renderer) => {
																				$$renderer.push(`<!---->${escape_html(mutationLocale)} · ${escape_html(localeName(mutationLocale))}`);
																			},
																			$$slots: { default: true }
																		});
																		$$renderer.push("<!--]-->");
																	} else {
																		$$renderer.push("<!--[!-->");
																		$$renderer.push("<!--]-->");
																	}
																	$$renderer.push(` `);
																	if (Select_content) {
																		$$renderer.push("<!--[-->");
																		Select_content($$renderer, {
																			children: ($$renderer) => {
																				if (Select_group) {
																					$$renderer.push("<!--[-->");
																					Select_group($$renderer, {
																						children: ($$renderer) => {
																							$$renderer.push(`<!--[-->`);
																							const each_array_11 = ensure_array_like(snapshot.catalog.locales.filter((locale) => locale.tag !== snapshot?.catalog?.defaultLocale));
																							for (let $$index_11 = 0, $$length = each_array_11.length; $$index_11 < $$length; $$index_11++) {
																								let locale = each_array_11[$$index_11];
																								if (Select_item) {
																									$$renderer.push("<!--[-->");
																									Select_item($$renderer, {
																										value: locale.tag,
																										label: `${locale.tag} · ${localeName(locale.tag)}`,
																										children: ($$renderer) => {
																											$$renderer.push(`<!---->${escape_html(locale.tag)} · ${escape_html(localeName(locale.tag))}`);
																										},
																										$$slots: { default: true }
																									});
																									$$renderer.push("<!--]-->");
																								} else {
																									$$renderer.push("<!--[!-->");
																									$$renderer.push("<!--]-->");
																								}
																							}
																							$$renderer.push(`<!--]-->`);
																						},
																						$$slots: { default: true }
																					});
																					$$renderer.push("<!--]-->");
																				} else {
																					$$renderer.push("<!--[!-->");
																					$$renderer.push("<!--]-->");
																				}
																			},
																			$$slots: { default: true }
																		});
																		$$renderer.push("<!--]-->");
																	} else {
																		$$renderer.push("<!--[!-->");
																		$$renderer.push("<!--]-->");
																	}
																},
																$$slots: { default: true }
															});
															$$renderer.push("<!--]-->");
														} else {
															$$renderer.push("<!--[!-->");
															$$renderer.push("<!--]-->");
														}
													},
													$$slots: { default: true }
												});
												$$renderer.push("<!--]-->");
											} else {
												$$renderer.push("<!--[!-->");
												$$renderer.push("<!--]-->");
											}
											$$renderer.push(` `);
											if (Field) {
												$$renderer.push("<!--[-->");
												Field($$renderer, {
													children: ($$renderer) => {
														if (Field_label) {
															$$renderer.push("<!--[-->");
															Field_label($$renderer, {
																for: "replacement-fallback",
																children: ($$renderer) => {
																	$$renderer.push(`<!---->Redirect dependent fallbacks to`);
																},
																$$slots: { default: true }
															});
															$$renderer.push("<!--]-->");
														} else {
															$$renderer.push("<!--[!-->");
															$$renderer.push("<!--]-->");
														}
														$$renderer.push(` `);
														if (Select) {
															$$renderer.push("<!--[-->");
															Select($$renderer, {
																type: "single",
																value: mutationReplacementFallback,
																onValueChange: (value) => {
																	mutationReplacementFallback = value;
																	invalidateMutationPreview();
																},
																children: ($$renderer) => {
																	if (Select_trigger) {
																		$$renderer.push("<!--[-->");
																		Select_trigger($$renderer, {
																			id: "replacement-fallback",
																			class: "w-full",
																			children: ($$renderer) => {
																				$$renderer.push(`<!---->${escape_html(mutationReplacementFallback)}`);
																			},
																			$$slots: { default: true }
																		});
																		$$renderer.push("<!--]-->");
																	} else {
																		$$renderer.push("<!--[!-->");
																		$$renderer.push("<!--]-->");
																	}
																	$$renderer.push(` `);
																	if (Select_content) {
																		$$renderer.push("<!--[-->");
																		Select_content($$renderer, {
																			children: ($$renderer) => {
																				if (Select_group) {
																					$$renderer.push("<!--[-->");
																					Select_group($$renderer, {
																						children: ($$renderer) => {
																							$$renderer.push(`<!--[-->`);
																							const each_array_12 = ensure_array_like(snapshot.catalog.locales.filter((locale) => locale.tag !== mutationLocale));
																							for (let $$index_12 = 0, $$length = each_array_12.length; $$index_12 < $$length; $$index_12++) {
																								let locale = each_array_12[$$index_12];
																								if (Select_item) {
																									$$renderer.push("<!--[-->");
																									Select_item($$renderer, {
																										value: locale.tag,
																										label: locale.tag,
																										children: ($$renderer) => {
																											$$renderer.push(`<!---->${escape_html(locale.tag)}`);
																										},
																										$$slots: { default: true }
																									});
																									$$renderer.push("<!--]-->");
																								} else {
																									$$renderer.push("<!--[!-->");
																									$$renderer.push("<!--]-->");
																								}
																							}
																							$$renderer.push(`<!--]-->`);
																						},
																						$$slots: { default: true }
																					});
																					$$renderer.push("<!--]-->");
																				} else {
																					$$renderer.push("<!--[!-->");
																					$$renderer.push("<!--]-->");
																				}
																			},
																			$$slots: { default: true }
																		});
																		$$renderer.push("<!--]-->");
																	} else {
																		$$renderer.push("<!--[!-->");
																		$$renderer.push("<!--]-->");
																	}
																},
																$$slots: { default: true }
															});
															$$renderer.push("<!--]-->");
														} else {
															$$renderer.push("<!--[!-->");
															$$renderer.push("<!--]-->");
														}
													},
													$$slots: { default: true }
												});
												$$renderer.push("<!--]-->");
											} else {
												$$renderer.push("<!--[!-->");
												$$renderer.push("<!--]-->");
											}
											$$renderer.push(`</div> `);
											if (Alert) {
												$$renderer.push("<!--[-->");
												Alert($$renderer, {
													variant: "destructive",
													children: ($$renderer) => {
														if (Alert_title) {
															$$renderer.push("<!--[-->");
															Alert_title($$renderer, {
																children: ($$renderer) => {
																	$$renderer.push(`<!---->Files will be deleted`);
																},
																$$slots: { default: true }
															});
															$$renderer.push("<!--]-->");
														} else {
															$$renderer.push("<!--[!-->");
															$$renderer.push("<!--]-->");
														}
														if (Alert_description) {
															$$renderer.push("<!--[-->");
															Alert_description($$renderer, {
																children: ($$renderer) => {
																	$$renderer.push(`<!---->All resource documents for this locale will be deleted after the preview is confirmed.`);
																},
																$$slots: { default: true }
															});
															$$renderer.push("<!--]-->");
														} else {
															$$renderer.push("<!--[!-->");
															$$renderer.push("<!--]-->");
														}
													},
													$$slots: { default: true }
												});
												$$renderer.push("<!--]-->");
											} else {
												$$renderer.push("<!--[!-->");
												$$renderer.push("<!--]-->");
											}
										} else if (mutationKind === "set-fallback") {
											$$renderer.push("<!--[2-->");
											$$renderer.push(`<div class="grid gap-4 sm:grid-cols-2">`);
											if (Field) {
												$$renderer.push("<!--[-->");
												Field($$renderer, {
													children: ($$renderer) => {
														if (Field_label) {
															$$renderer.push("<!--[-->");
															Field_label($$renderer, {
																for: "fallback-locale",
																children: ($$renderer) => {
																	$$renderer.push(`<!---->Language`);
																},
																$$slots: { default: true }
															});
															$$renderer.push("<!--]-->");
														} else {
															$$renderer.push("<!--[!-->");
															$$renderer.push("<!--]-->");
														}
														$$renderer.push(` `);
														if (Select) {
															$$renderer.push("<!--[-->");
															Select($$renderer, {
																type: "single",
																value: mutationLocale,
																onValueChange: (value) => {
																	mutationLocale = value;
																	invalidateMutationPreview();
																},
																children: ($$renderer) => {
																	if (Select_trigger) {
																		$$renderer.push("<!--[-->");
																		Select_trigger($$renderer, {
																			id: "fallback-locale",
																			class: "w-full",
																			children: ($$renderer) => {
																				$$renderer.push(`<!---->${escape_html(mutationLocale)} · ${escape_html(localeName(mutationLocale))}`);
																			},
																			$$slots: { default: true }
																		});
																		$$renderer.push("<!--]-->");
																	} else {
																		$$renderer.push("<!--[!-->");
																		$$renderer.push("<!--]-->");
																	}
																	$$renderer.push(` `);
																	if (Select_content) {
																		$$renderer.push("<!--[-->");
																		Select_content($$renderer, {
																			children: ($$renderer) => {
																				if (Select_group) {
																					$$renderer.push("<!--[-->");
																					Select_group($$renderer, {
																						children: ($$renderer) => {
																							$$renderer.push(`<!--[-->`);
																							const each_array_13 = ensure_array_like(snapshot.catalog.locales.filter((locale) => locale.tag !== snapshot?.catalog?.defaultLocale));
																							for (let $$index_13 = 0, $$length = each_array_13.length; $$index_13 < $$length; $$index_13++) {
																								let locale = each_array_13[$$index_13];
																								if (Select_item) {
																									$$renderer.push("<!--[-->");
																									Select_item($$renderer, {
																										value: locale.tag,
																										label: `${locale.tag} · ${localeName(locale.tag)}`,
																										children: ($$renderer) => {
																											$$renderer.push(`<!---->${escape_html(locale.tag)} · ${escape_html(localeName(locale.tag))}`);
																										},
																										$$slots: { default: true }
																									});
																									$$renderer.push("<!--]-->");
																								} else {
																									$$renderer.push("<!--[!-->");
																									$$renderer.push("<!--]-->");
																								}
																							}
																							$$renderer.push(`<!--]-->`);
																						},
																						$$slots: { default: true }
																					});
																					$$renderer.push("<!--]-->");
																				} else {
																					$$renderer.push("<!--[!-->");
																					$$renderer.push("<!--]-->");
																				}
																			},
																			$$slots: { default: true }
																		});
																		$$renderer.push("<!--]-->");
																	} else {
																		$$renderer.push("<!--[!-->");
																		$$renderer.push("<!--]-->");
																	}
																},
																$$slots: { default: true }
															});
															$$renderer.push("<!--]-->");
														} else {
															$$renderer.push("<!--[!-->");
															$$renderer.push("<!--]-->");
														}
													},
													$$slots: { default: true }
												});
												$$renderer.push("<!--]-->");
											} else {
												$$renderer.push("<!--[!-->");
												$$renderer.push("<!--]-->");
											}
											$$renderer.push(` `);
											if (Field) {
												$$renderer.push("<!--[-->");
												Field($$renderer, {
													children: ($$renderer) => {
														if (Field_label) {
															$$renderer.push("<!--[-->");
															Field_label($$renderer, {
																for: "fallback-target",
																children: ($$renderer) => {
																	$$renderer.push(`<!---->Fallback`);
																},
																$$slots: { default: true }
															});
															$$renderer.push("<!--]-->");
														} else {
															$$renderer.push("<!--[!-->");
															$$renderer.push("<!--]-->");
														}
														$$renderer.push(` `);
														if (Select) {
															$$renderer.push("<!--[-->");
															Select($$renderer, {
																type: "single",
																value: mutationFallback,
																onValueChange: (value) => {
																	mutationFallback = value;
																	invalidateMutationPreview();
																},
																children: ($$renderer) => {
																	if (Select_trigger) {
																		$$renderer.push("<!--[-->");
																		Select_trigger($$renderer, {
																			id: "fallback-target",
																			class: "w-full",
																			children: ($$renderer) => {
																				$$renderer.push(`<!---->${escape_html(mutationFallback)} · ${escape_html(localeName(mutationFallback))}`);
																			},
																			$$slots: { default: true }
																		});
																		$$renderer.push("<!--]-->");
																	} else {
																		$$renderer.push("<!--[!-->");
																		$$renderer.push("<!--]-->");
																	}
																	$$renderer.push(` `);
																	if (Select_content) {
																		$$renderer.push("<!--[-->");
																		Select_content($$renderer, {
																			children: ($$renderer) => {
																				if (Select_group) {
																					$$renderer.push("<!--[-->");
																					Select_group($$renderer, {
																						children: ($$renderer) => {
																							$$renderer.push(`<!--[-->`);
																							const each_array_14 = ensure_array_like(snapshot.catalog.locales.filter((locale) => locale.tag !== mutationLocale));
																							for (let $$index_14 = 0, $$length = each_array_14.length; $$index_14 < $$length; $$index_14++) {
																								let locale = each_array_14[$$index_14];
																								if (Select_item) {
																									$$renderer.push("<!--[-->");
																									Select_item($$renderer, {
																										value: locale.tag,
																										label: `${locale.tag} · ${localeName(locale.tag)}`,
																										children: ($$renderer) => {
																											$$renderer.push(`<!---->${escape_html(locale.tag)} · ${escape_html(localeName(locale.tag))}`);
																										},
																										$$slots: { default: true }
																									});
																									$$renderer.push("<!--]-->");
																								} else {
																									$$renderer.push("<!--[!-->");
																									$$renderer.push("<!--]-->");
																								}
																							}
																							$$renderer.push(`<!--]-->`);
																						},
																						$$slots: { default: true }
																					});
																					$$renderer.push("<!--]-->");
																				} else {
																					$$renderer.push("<!--[!-->");
																					$$renderer.push("<!--]-->");
																				}
																			},
																			$$slots: { default: true }
																		});
																		$$renderer.push("<!--]-->");
																	} else {
																		$$renderer.push("<!--[!-->");
																		$$renderer.push("<!--]-->");
																	}
																},
																$$slots: { default: true }
															});
															$$renderer.push("<!--]-->");
														} else {
															$$renderer.push("<!--[!-->");
															$$renderer.push("<!--]-->");
														}
													},
													$$slots: { default: true }
												});
												$$renderer.push("<!--]-->");
											} else {
												$$renderer.push("<!--[!-->");
												$$renderer.push("<!--]-->");
											}
											$$renderer.push(`</div> <div class="flex flex-wrap gap-2"><!--[-->`);
											const each_array_15 = ensure_array_like(snapshot.catalog.locales);
											for (let $$index_15 = 0, $$length = each_array_15.length; $$index_15 < $$length; $$index_15++) {
												let locale = each_array_15[$$index_15];
												Badge($$renderer, {
													variant: "outline",
													children: ($$renderer) => {
														$$renderer.push(`<strong>${escape_html(locale.tag)}</strong>${escape_html(locale.tag === mutationLocale ? ` → ${mutationFallback}` : locale.fallback ? ` → ${locale.fallback}` : " · source")}`);
													},
													$$slots: { default: true }
												});
											}
											$$renderer.push(`<!--]--></div>`);
										} else if (mutationKind === "create-key") {
											$$renderer.push("<!--[3-->");
											if (Field) {
												$$renderer.push("<!--[-->");
												Field($$renderer, {
													children: ($$renderer) => {
														if (Field_label) {
															$$renderer.push("<!--[-->");
															Field_label($$renderer, {
																for: "mutation-target-key",
																children: ($$renderer) => {
																	$$renderer.push(`<!---->Message key`);
																},
																$$slots: { default: true }
															});
															$$renderer.push("<!--]-->");
														} else {
															$$renderer.push("<!--[!-->");
															$$renderer.push("<!--]-->");
														}
														Input($$renderer, {
															id: "mutation-target-key",
															oninput: invalidateMutationPreview,
															placeholder: "Checkout.Actions.Pay",
															autocomplete: "off",
															get value() {
																return mutationTargetKey;
															},
															set value($$value) {
																mutationTargetKey = $$value;
																$$settled = false;
															}
														});
														$$renderer.push(`<!---->`);
														if (Field_description) {
															$$renderer.push("<!--[-->");
															Field_description($$renderer, {
																children: ($$renderer) => {
																	$$renderer.push(`<!---->Use dots to organize messages into groups.`);
																},
																$$slots: { default: true }
															});
															$$renderer.push("<!--]-->");
														} else {
															$$renderer.push("<!--[!-->");
															$$renderer.push("<!--]-->");
														}
													},
													$$slots: { default: true }
												});
												$$renderer.push("<!--]-->");
											} else {
												$$renderer.push("<!--[!-->");
												$$renderer.push("<!--]-->");
											}
											$$renderer.push(` `);
											if (Field) {
												$$renderer.push("<!--[-->");
												Field($$renderer, {
													children: ($$renderer) => {
														if (Field_label) {
															$$renderer.push("<!--[-->");
															Field_label($$renderer, {
																for: "mutation-initial-value",
																children: ($$renderer) => {
																	$$renderer.push(`<!---->Initial text`);
																},
																$$slots: { default: true }
															});
															$$renderer.push("<!--]-->");
														} else {
															$$renderer.push("<!--[!-->");
															$$renderer.push("<!--]-->");
														}
														Textarea($$renderer, {
															id: "mutation-initial-value",
															class: "min-h-28",
															oninput: invalidateMutationPreview,
															placeholder: "Pay now",
															get value() {
																return mutationInitialValue;
															},
															set value($$value) {
																mutationInitialValue = $$value;
																$$settled = false;
															}
														});
														$$renderer.push(`<!---->`);
														if (Field_description) {
															$$renderer.push("<!--[-->");
															Field_description($$renderer, {
																children: ($$renderer) => {
																	$$renderer.push(`<!---->The initial value is added to every language so strict projects stay valid.`);
																},
																$$slots: { default: true }
															});
															$$renderer.push("<!--]-->");
														} else {
															$$renderer.push("<!--[!-->");
															$$renderer.push("<!--]-->");
														}
													},
													$$slots: { default: true }
												});
												$$renderer.push("<!--]-->");
											} else {
												$$renderer.push("<!--[!-->");
												$$renderer.push("<!--]-->");
											}
											$$renderer.push(` `);
											if (Field) {
												$$renderer.push("<!--[-->");
												Field($$renderer, {
													children: ($$renderer) => {
														if (Field_label) {
															$$renderer.push("<!--[-->");
															Field_label($$renderer, {
																for: "message-layer",
																children: ($$renderer) => {
																	$$renderer.push(`<!---->Layer`);
																},
																$$slots: { default: true }
															});
															$$renderer.push("<!--]-->");
														} else {
															$$renderer.push("<!--[!-->");
															$$renderer.push("<!--]-->");
														}
														$$renderer.push(` `);
														if (Select) {
															$$renderer.push("<!--[-->");
															Select($$renderer, {
																type: "single",
																value: mutationLayer,
																onValueChange: (value) => {
																	mutationLayer = value;
																	invalidateMutationPreview();
																},
																children: ($$renderer) => {
																	if (Select_trigger) {
																		$$renderer.push("<!--[-->");
																		Select_trigger($$renderer, {
																			id: "message-layer",
																			class: "w-full",
																			children: ($$renderer) => {
																				$$renderer.push(`<!---->${escape_html(mutationLayer)}`);
																			},
																			$$slots: { default: true }
																		});
																		$$renderer.push("<!--]-->");
																	} else {
																		$$renderer.push("<!--[!-->");
																		$$renderer.push("<!--]-->");
																	}
																	if (Select_content) {
																		$$renderer.push("<!--[-->");
																		Select_content($$renderer, {
																			children: ($$renderer) => {
																				if (Select_group) {
																					$$renderer.push("<!--[-->");
																					Select_group($$renderer, {
																						children: ($$renderer) => {
																							$$renderer.push(`<!--[-->`);
																							const each_array_16 = ensure_array_like(snapshot.catalog.layers);
																							for (let $$index_16 = 0, $$length = each_array_16.length; $$index_16 < $$length; $$index_16++) {
																								let layer = each_array_16[$$index_16];
																								if (Select_item) {
																									$$renderer.push("<!--[-->");
																									Select_item($$renderer, {
																										value: layer.name,
																										label: layer.name,
																										children: ($$renderer) => {
																											$$renderer.push(`<!---->${escape_html(layer.name)}`);
																										},
																										$$slots: { default: true }
																									});
																									$$renderer.push("<!--]-->");
																								} else {
																									$$renderer.push("<!--[!-->");
																									$$renderer.push("<!--]-->");
																								}
																							}
																							$$renderer.push(`<!--]-->`);
																						},
																						$$slots: { default: true }
																					});
																					$$renderer.push("<!--]-->");
																				} else {
																					$$renderer.push("<!--[!-->");
																					$$renderer.push("<!--]-->");
																				}
																			},
																			$$slots: { default: true }
																		});
																		$$renderer.push("<!--]-->");
																	} else {
																		$$renderer.push("<!--[!-->");
																		$$renderer.push("<!--]-->");
																	}
																},
																$$slots: { default: true }
															});
															$$renderer.push("<!--]-->");
														} else {
															$$renderer.push("<!--[!-->");
															$$renderer.push("<!--]-->");
														}
													},
													$$slots: { default: true }
												});
												$$renderer.push("<!--]-->");
											} else {
												$$renderer.push("<!--[!-->");
												$$renderer.push("<!--]-->");
											}
										} else if (mutationKind === "rename-key" || mutationKind === "duplicate-key") {
											$$renderer.push("<!--[4-->");
											if (Field) {
												$$renderer.push("<!--[-->");
												Field($$renderer, {
													children: ($$renderer) => {
														if (Field_label) {
															$$renderer.push("<!--[-->");
															Field_label($$renderer, {
																for: "mutation-source-key",
																children: ($$renderer) => {
																	$$renderer.push(`<!---->Existing key`);
																},
																$$slots: { default: true }
															});
															$$renderer.push("<!--]-->");
														} else {
															$$renderer.push("<!--[!-->");
															$$renderer.push("<!--]-->");
														}
														Input($$renderer, {
															id: "mutation-source-key",
															value: mutationSourceKey,
															readonly: true
														});
														$$renderer.push(`<!---->`);
													},
													$$slots: { default: true }
												});
												$$renderer.push("<!--]-->");
											} else {
												$$renderer.push("<!--[!-->");
												$$renderer.push("<!--]-->");
											}
											$$renderer.push(` `);
											if (Field) {
												$$renderer.push("<!--[-->");
												Field($$renderer, {
													children: ($$renderer) => {
														if (Field_label) {
															$$renderer.push("<!--[-->");
															Field_label($$renderer, {
																for: "mutation-new-key",
																children: ($$renderer) => {
																	$$renderer.push(`<!---->${escape_html(mutationKind === "rename-key" ? "New key or group path" : "Duplicate key")}`);
																},
																$$slots: { default: true }
															});
															$$renderer.push("<!--]-->");
														} else {
															$$renderer.push("<!--[!-->");
															$$renderer.push("<!--]-->");
														}
														Input($$renderer, {
															id: "mutation-new-key",
															oninput: invalidateMutationPreview,
															autocomplete: "off",
															get value() {
																return mutationTargetKey;
															},
															set value($$value) {
																mutationTargetKey = $$value;
																$$settled = false;
															}
														});
														$$renderer.push(`<!---->`);
														if (Field_description) {
															$$renderer.push("<!--[-->");
															Field_description($$renderer, {
																children: ($$renderer) => {
																	$$renderer.push(`<!---->The change is applied across every locale and layer where the source message exists.`);
																},
																$$slots: { default: true }
															});
															$$renderer.push("<!--]-->");
														} else {
															$$renderer.push("<!--[!-->");
															$$renderer.push("<!--]-->");
														}
													},
													$$slots: { default: true }
												});
												$$renderer.push("<!--]-->");
											} else {
												$$renderer.push("<!--[!-->");
												$$renderer.push("<!--]-->");
											}
										} else {
											$$renderer.push("<!--[-1-->");
											if (Alert) {
												$$renderer.push("<!--[-->");
												Alert($$renderer, {
													variant: "destructive",
													children: ($$renderer) => {
														if (Alert_title) {
															$$renderer.push("<!--[-->");
															Alert_title($$renderer, {
																children: ($$renderer) => {
																	$$renderer.push(`<!---->Delete ${escape_html(mutationSourceKey)}?`);
																},
																$$slots: { default: true }
															});
															$$renderer.push("<!--]-->");
														} else {
															$$renderer.push("<!--[!-->");
															$$renderer.push("<!--]-->");
														}
														if (Alert_description) {
															$$renderer.push("<!--[-->");
															Alert_description($$renderer, {
																children: ($$renderer) => {
																	$$renderer.push(`<!---->The message will be removed from every locale and layer. The preview below lists every file that will change.`);
																},
																$$slots: { default: true }
															});
															$$renderer.push("<!--]-->");
														} else {
															$$renderer.push("<!--[!-->");
															$$renderer.push("<!--]-->");
														}
													},
													$$slots: { default: true }
												});
												$$renderer.push("<!--]-->");
											} else {
												$$renderer.push("<!--[!-->");
												$$renderer.push("<!--]-->");
											}
										}
										$$renderer.push(`<!--]-->`);
									},
									$$slots: { default: true }
								});
								$$renderer.push("<!--]-->");
							} else {
								$$renderer.push("<!--[!-->");
								$$renderer.push("<!--]-->");
							}
							$$renderer.push(` `);
							if (mutationError) {
								$$renderer.push("<!--[0-->");
								if (Alert) {
									$$renderer.push("<!--[-->");
									Alert($$renderer, {
										variant: "destructive",
										class: "mt-4",
										"aria-live": "polite",
										children: ($$renderer) => {
											if (Alert_title) {
												$$renderer.push("<!--[-->");
												Alert_title($$renderer, {
													children: ($$renderer) => {
														$$renderer.push(`<!---->Change is not valid`);
													},
													$$slots: { default: true }
												});
												$$renderer.push("<!--]-->");
											} else {
												$$renderer.push("<!--[!-->");
												$$renderer.push("<!--]-->");
											}
											if (Alert_description) {
												$$renderer.push("<!--[-->");
												Alert_description($$renderer, {
													children: ($$renderer) => {
														$$renderer.push(`<!---->${escape_html(mutationError)}`);
													},
													$$slots: { default: true }
												});
												$$renderer.push("<!--]-->");
											} else {
												$$renderer.push("<!--[!-->");
												$$renderer.push("<!--]-->");
											}
										},
										$$slots: { default: true }
									});
									$$renderer.push("<!--]-->");
								} else {
									$$renderer.push("<!--[!-->");
									$$renderer.push("<!--]-->");
								}
							} else $$renderer.push("<!--[-1-->");
							$$renderer.push(`<!--]--> `);
							if (mutationPreview?.ok) {
								$$renderer.push("<!--[0-->");
								$$renderer.push(`<section class="mt-5 overflow-hidden rounded-xl border" aria-label="Operation preview"><header class="flex items-center justify-between gap-3 border-b px-4 py-3"><strong>Operation preview</strong>`);
								Badge($$renderer, {
									variant: "secondary",
									children: ($$renderer) => {
										$$renderer.push(`<!---->${escape_html(mutationPreview.files.length)} affected ${escape_html(mutationPreview.files.length === 1 ? "file" : "files")}`);
									},
									$$slots: { default: true }
								});
								$$renderer.push(`<!----></header> <!--[-->`);
								const each_array_17 = ensure_array_like(mutationPreview.files);
								for (let $$index_17 = 0, $$length = each_array_17.length; $$index_17 < $$length; $$index_17++) {
									let file = each_array_17[$$index_17];
									$$renderer.push(`<div class="grid grid-cols-[auto_minmax(0,1fr)] items-center gap-3 border-b px-4 py-3 last:border-b-0 sm:grid-cols-[auto_minmax(0,1fr)_auto]">`);
									Badge($$renderer, {
										variant: file.kind === "delete" ? "destructive" : file.kind === "create" ? "default" : "secondary",
										children: ($$renderer) => {
											$$renderer.push(`<!---->${escape_html(file.kind)}`);
										},
										$$slots: { default: true }
									});
									$$renderer.push(`<!----><code class="truncate text-xs">${escape_html(file.path)}</code><small class="col-start-2 text-muted-foreground sm:col-start-auto">${escape_html(file.beforeBytes.toLocaleString())} → ${escape_html(file.afterBytes.toLocaleString())} bytes</small></div>`);
								}
								$$renderer.push(`<!--]--></section>`);
							} else $$renderer.push("<!--[-1-->");
							$$renderer.push(`<!--]-->`);
						},
						$$slots: {
							footer: true,
							default: true
						}
					});
				}
			} else $$renderer.push("<!--[-1-->");
			$$renderer.push(`<!--]--> `);
			if (projectDialogOpen) {
				$$renderer.push("<!--[0-->");
				{
					function footer($$renderer) {
						Button($$renderer, {
							variant: "outline",
							disabled: projectBusy,
							onclick: closeProjectWizard,
							children: ($$renderer) => {
								$$renderer.push(`<!---->Cancel`);
							},
							$$slots: { default: true }
						});
						$$renderer.push(`<!----> `);
						if (projectStep > 1) {
							$$renderer.push("<!--[0-->");
							Button($$renderer, {
								variant: "ghost",
								disabled: projectBusy,
								onclick: () => {
									projectStep -= 1;
									projectError = void 0;
								},
								children: ($$renderer) => {
									$$renderer.push(`<!---->Back`);
								},
								$$slots: { default: true }
							});
						} else $$renderer.push("<!--[-1-->");
						$$renderer.push(`<!--]--> `);
						if (projectStep < 4) {
							$$renderer.push("<!--[0-->");
							Button($$renderer, {
								disabled: projectBusy,
								onclick: () => void advanceProjectWizard(),
								children: ($$renderer) => {
									if (projectBusy) {
										$$renderer.push("<!--[0-->");
										Spinner($$renderer, { "data-icon": "inline-start" });
									} else $$renderer.push("<!--[-1-->");
									$$renderer.push(`<!--]-->${escape_html(projectBusy ? "Validating…" : "Continue")}`);
								},
								$$slots: { default: true }
							});
						} else {
							$$renderer.push("<!--[-1-->");
							Button($$renderer, {
								disabled: projectBusy || projectPlan?.ok !== true,
								onclick: () => void createProject(),
								children: ($$renderer) => {
									if (projectBusy) {
										$$renderer.push("<!--[0-->");
										Spinner($$renderer, { "data-icon": "inline-start" });
									} else $$renderer.push("<!--[-1-->");
									$$renderer.push(`<!--]-->${escape_html(projectBusy ? "Creating…" : "Create project")}`);
								},
								$$slots: { default: true }
							});
						}
						$$renderer.push(`<!--]-->`);
					}
					AppDialog($$renderer, {
						open: true,
						title: "New translation project",
						description: "Create compiler-valid text resources without overwriting an existing directory.",
						class: "sm:max-w-3xl",
						showCloseButton: !projectBusy,
						onopenchange: (open) => {
							if (!open && !projectBusy) closeProjectWizard();
						},
						footer,
						children: ($$renderer) => {
							$$renderer.push(`<ol class="mb-6 grid grid-cols-2 gap-2 sm:grid-cols-4" aria-label="Project creation steps"><!--[-->`);
							const each_array_18 = ensure_array_like([
								"Project",
								"Languages",
								"Settings",
								"Review"
							]);
							for (let index = 0, $$length = each_array_18.length; index < $$length; index++) {
								let title = each_array_18[index];
								$$renderer.push(`<li class="flex items-center gap-2 text-sm"${attr("aria-current", projectStep === index + 1 ? "step" : void 0)}>`);
								Badge($$renderer, {
									variant: projectStep === index + 1 ? "default" : projectStep > index + 1 ? "secondary" : "outline",
									children: ($$renderer) => {
										$$renderer.push(`<!---->${escape_html(projectStep > index + 1 ? "✓" : index + 1)}`);
									},
									$$slots: { default: true }
								});
								$$renderer.push(`<!----> <span${attr_class(clsx$1(projectStep === index + 1 ? "font-medium" : "text-muted-foreground"))}>${escape_html(title)}</span></li>`);
							}
							$$renderer.push(`<!--]--></ol> `);
							if (projectStep === 1) {
								$$renderer.push("<!--[0-->");
								$$renderer.push(`<div class="mb-5"><h3 class="font-medium">Where should the translations live?</h3><p class="text-sm text-muted-foreground">The editor creates a new directory and never overwrites an existing one.</p></div> `);
								if (Field_group) {
									$$renderer.push("<!--[-->");
									Field_group($$renderer, {
										children: ($$renderer) => {
											if (Field) {
												$$renderer.push("<!--[-->");
												Field($$renderer, {
													children: ($$renderer) => {
														if (Field_label) {
															$$renderer.push("<!--[-->");
															Field_label($$renderer, {
																for: "project-directory",
																children: ($$renderer) => {
																	$$renderer.push(`<!---->New project directory`);
																},
																$$slots: { default: true }
															});
															$$renderer.push("<!--]-->");
														} else {
															$$renderer.push("<!--[!-->");
															$$renderer.push("<!--]-->");
														}
														Input($$renderer, {
															id: "project-directory",
															placeholder: "/projects/customer-app/Resources",
															autocomplete: "off",
															get value() {
																return projectDirectory;
															},
															set value($$value) {
																projectDirectory = $$value;
																$$settled = false;
															}
														});
														$$renderer.push(`<!---->`);
														if (Field_description) {
															$$renderer.push("<!--[-->");
															Field_description($$renderer, {
																children: ($$renderer) => {
																	$$renderer.push(`<!---->Enter an absolute path or a path relative to the editor process.`);
																},
																$$slots: { default: true }
															});
															$$renderer.push("<!--]-->");
														} else {
															$$renderer.push("<!--[!-->");
															$$renderer.push("<!--]-->");
														}
													},
													$$slots: { default: true }
												});
												$$renderer.push("<!--]-->");
											} else {
												$$renderer.push("<!--[!-->");
												$$renderer.push("<!--]-->");
											}
											$$renderer.push(` `);
											if (Field) {
												$$renderer.push("<!--[-->");
												Field($$renderer, {
													children: ($$renderer) => {
														if (Field_label) {
															$$renderer.push("<!--[-->");
															Field_label($$renderer, {
																for: "project-catalog",
																children: ($$renderer) => {
																	$$renderer.push(`<!---->Catalog ID`);
																},
																$$slots: { default: true }
															});
															$$renderer.push("<!--]-->");
														} else {
															$$renderer.push("<!--[!-->");
															$$renderer.push("<!--]-->");
														}
														Input($$renderer, {
															id: "project-catalog",
															placeholder: "product",
															autocomplete: "off",
															get value() {
																return projectCatalog;
															},
															set value($$value) {
																projectCatalog = $$value;
																$$settled = false;
															}
														});
														$$renderer.push(`<!---->`);
														if (Field_description) {
															$$renderer.push("<!--[-->");
															Field_description($$renderer, {
																children: ($$renderer) => {
																	$$renderer.push(`<!---->Lowercase letters, numbers, dots, and hyphens.`);
																},
																$$slots: { default: true }
															});
															$$renderer.push("<!--]-->");
														} else {
															$$renderer.push("<!--[!-->");
															$$renderer.push("<!--]-->");
														}
													},
													$$slots: { default: true }
												});
												$$renderer.push("<!--]-->");
											} else {
												$$renderer.push("<!--[!-->");
												$$renderer.push("<!--]-->");
											}
										},
										$$slots: { default: true }
									});
									$$renderer.push("<!--]-->");
								} else {
									$$renderer.push("<!--[!-->");
									$$renderer.push("<!--]-->");
								}
							} else if (projectStep === 2) {
								$$renderer.push("<!--[1-->");
								$$renderer.push(`<div class="mb-5"><h3 class="font-medium">Which languages does this project use?</h3><p class="text-sm text-muted-foreground">One language is fully supported. Add translations now or later.</p></div> <div class="grid gap-3"><div class="grid items-end gap-3 rounded-xl border p-4 sm:grid-cols-[1fr_auto]">`);
								if (Field) {
									$$renderer.push("<!--[-->");
									Field($$renderer, {
										children: ($$renderer) => {
											if (Field_label) {
												$$renderer.push("<!--[-->");
												Field_label($$renderer, {
													for: "project-default-locale",
													children: ($$renderer) => {
														$$renderer.push(`<!---->Source/default language`);
													},
													$$slots: { default: true }
												});
												$$renderer.push("<!--]-->");
											} else {
												$$renderer.push("<!--[!-->");
												$$renderer.push("<!--]-->");
											}
											Input($$renderer, {
												id: "project-default-locale",
												placeholder: "de",
												autocomplete: "off",
												get value() {
													return projectDefaultLocale;
												},
												set value($$value) {
													projectDefaultLocale = $$value;
													$$settled = false;
												}
											});
											$$renderer.push(`<!---->`);
										},
										$$slots: { default: true }
									});
									$$renderer.push("<!--]-->");
								} else {
									$$renderer.push("<!--[!-->");
									$$renderer.push("<!--]-->");
								}
								$$renderer.push(` `);
								Badge($$renderer, {
									variant: "secondary",
									class: "mb-2",
									children: ($$renderer) => {
										$$renderer.push(`<!---->Canonical source`);
									},
									$$slots: { default: true }
								});
								$$renderer.push(`<!----></div> <!--[-->`);
								const each_array_19 = ensure_array_like(projectLocales);
								for (let $$index_20 = 0, $$length = each_array_19.length; $$index_20 < $$length; $$index_20++) {
									let locale = each_array_19[$$index_20];
									$$renderer.push(`<div class="grid items-end gap-3 rounded-xl border p-4 sm:grid-cols-[1fr_1fr_auto]">`);
									if (Field) {
										$$renderer.push("<!--[-->");
										Field($$renderer, {
											children: ($$renderer) => {
												if (Field_label) {
													$$renderer.push("<!--[-->");
													Field_label($$renderer, {
														for: `project-locale-${locale.id}`,
														children: ($$renderer) => {
															$$renderer.push(`<!---->Additional language`);
														},
														$$slots: { default: true }
													});
													$$renderer.push("<!--]-->");
												} else {
													$$renderer.push("<!--[!-->");
													$$renderer.push("<!--]-->");
												}
												Input($$renderer, {
													id: `project-locale-${locale.id}`,
													placeholder: "en",
													autocomplete: "off",
													get value() {
														return locale.tag;
													},
													set value($$value) {
														locale.tag = $$value;
														$$settled = false;
													}
												});
												$$renderer.push(`<!---->`);
											},
											$$slots: { default: true }
										});
										$$renderer.push("<!--]-->");
									} else {
										$$renderer.push("<!--[!-->");
										$$renderer.push("<!--]-->");
									}
									$$renderer.push(` `);
									if (Field) {
										$$renderer.push("<!--[-->");
										Field($$renderer, {
											children: ($$renderer) => {
												if (Field_label) {
													$$renderer.push("<!--[-->");
													Field_label($$renderer, {
														for: `project-fallback-${locale.id}`,
														children: ($$renderer) => {
															$$renderer.push(`<!---->Fallback`);
														},
														$$slots: { default: true }
													});
													$$renderer.push("<!--]-->");
												} else {
													$$renderer.push("<!--[!-->");
													$$renderer.push("<!--]-->");
												}
												$$renderer.push(` `);
												if (Select) {
													$$renderer.push("<!--[-->");
													Select($$renderer, {
														type: "single",
														value: locale.fallback,
														onValueChange: (value) => locale.fallback = value,
														children: ($$renderer) => {
															if (Select_trigger) {
																$$renderer.push("<!--[-->");
																Select_trigger($$renderer, {
																	id: `project-fallback-${locale.id}`,
																	class: "w-full",
																	children: ($$renderer) => {
																		$$renderer.push(`<!---->${escape_html(locale.fallback || `Default (${projectDefaultLocale || "source"})`)}`);
																	},
																	$$slots: { default: true }
																});
																$$renderer.push("<!--]-->");
															} else {
																$$renderer.push("<!--[!-->");
																$$renderer.push("<!--]-->");
															}
															$$renderer.push(` `);
															if (Select_content) {
																$$renderer.push("<!--[-->");
																Select_content($$renderer, {
																	children: ($$renderer) => {
																		if (Select_group) {
																			$$renderer.push("<!--[-->");
																			Select_group($$renderer, {
																				children: ($$renderer) => {
																					if (Select_item) {
																						$$renderer.push("<!--[-->");
																						Select_item($$renderer, {
																							value: "",
																							label: `Default (${projectDefaultLocale || "source"})`,
																							children: ($$renderer) => {
																								$$renderer.push(`<!---->Default (${escape_html(projectDefaultLocale || "source")})`);
																							},
																							$$slots: { default: true }
																						});
																						$$renderer.push("<!--]-->");
																					} else {
																						$$renderer.push("<!--[!-->");
																						$$renderer.push("<!--]-->");
																					}
																					$$renderer.push(`<!--[-->`);
																					const each_array_20 = ensure_array_like(projectLocales.filter((candidate) => candidate.id !== locale.id && candidate.tag.trim() !== ""));
																					for (let $$index_19 = 0, $$length = each_array_20.length; $$index_19 < $$length; $$index_19++) {
																						let candidate = each_array_20[$$index_19];
																						if (Select_item) {
																							$$renderer.push("<!--[-->");
																							Select_item($$renderer, {
																								value: candidate.tag,
																								label: candidate.tag,
																								children: ($$renderer) => {
																									$$renderer.push(`<!---->${escape_html(candidate.tag)}`);
																								},
																								$$slots: { default: true }
																							});
																							$$renderer.push("<!--]-->");
																						} else {
																							$$renderer.push("<!--[!-->");
																							$$renderer.push("<!--]-->");
																						}
																					}
																					$$renderer.push(`<!--]-->`);
																				},
																				$$slots: { default: true }
																			});
																			$$renderer.push("<!--]-->");
																		} else {
																			$$renderer.push("<!--[!-->");
																			$$renderer.push("<!--]-->");
																		}
																	},
																	$$slots: { default: true }
																});
																$$renderer.push("<!--]-->");
															} else {
																$$renderer.push("<!--[!-->");
																$$renderer.push("<!--]-->");
															}
														},
														$$slots: { default: true }
													});
													$$renderer.push("<!--]-->");
												} else {
													$$renderer.push("<!--[!-->");
													$$renderer.push("<!--]-->");
												}
											},
											$$slots: { default: true }
										});
										$$renderer.push("<!--]-->");
									} else {
										$$renderer.push("<!--[!-->");
										$$renderer.push("<!--]-->");
									}
									$$renderer.push(` `);
									Button($$renderer, {
										variant: "ghost",
										size: "icon-sm",
										"aria-label": `Remove locale ${locale.tag || "row"}`,
										onclick: () => removeProjectLocale(locale.id),
										children: ($$renderer) => {
											Trash_2($$renderer, {});
										},
										$$slots: { default: true }
									});
									$$renderer.push(`<!----></div>`);
								}
								$$renderer.push(`<!--]--> `);
								Button($$renderer, {
									variant: "outline",
									class: "justify-self-start",
									onclick: addProjectLocale,
									children: ($$renderer) => {
										Plus($$renderer, { "data-icon": "inline-start" });
										$$renderer.push(`<!---->Add another language`);
									},
									$$slots: { default: true }
								});
								$$renderer.push(`<!----></div>`);
							} else if (projectStep === 3) {
								$$renderer.push("<!--[2-->");
								$$renderer.push(`<div class="mb-5"><h3 class="font-medium">Generated API and output</h3><p class="text-sm text-muted-foreground">These defaults work for most .NET and ESM consumers.</p></div> `);
								if (Field_group) {
									$$renderer.push("<!--[-->");
									Field_group($$renderer, {
										children: ($$renderer) => {
											$$renderer.push(`<div class="grid gap-4 sm:grid-cols-2">`);
											if (Field) {
												$$renderer.push("<!--[-->");
												Field($$renderer, {
													children: ($$renderer) => {
														if (Field_label) {
															$$renderer.push("<!--[-->");
															Field_label($$renderer, {
																for: "project-namespace",
																children: ($$renderer) => {
																	$$renderer.push(`<!---->Code namespace`);
																},
																$$slots: { default: true }
															});
															$$renderer.push("<!--]-->");
														} else {
															$$renderer.push("<!--[!-->");
															$$renderer.push("<!--]-->");
														}
														Input($$renderer, {
															id: "project-namespace",
															autocomplete: "off",
															get value() {
																return projectNamespace;
															},
															set value($$value) {
																projectNamespace = $$value;
																$$settled = false;
															}
														});
														$$renderer.push(`<!---->`);
													},
													$$slots: { default: true }
												});
												$$renderer.push("<!--]-->");
											} else {
												$$renderer.push("<!--[!-->");
												$$renderer.push("<!--]-->");
											}
											$$renderer.push(` `);
											if (Field) {
												$$renderer.push("<!--[-->");
												Field($$renderer, {
													children: ($$renderer) => {
														if (Field_label) {
															$$renderer.push("<!--[-->");
															Field_label($$renderer, {
																for: "project-class",
																children: ($$renderer) => {
																	$$renderer.push(`<!---->Generated class`);
																},
																$$slots: { default: true }
															});
															$$renderer.push("<!--]-->");
														} else {
															$$renderer.push("<!--[!-->");
															$$renderer.push("<!--]-->");
														}
														Input($$renderer, {
															id: "project-class",
															autocomplete: "off",
															get value() {
																return projectClassName;
															},
															set value($$value) {
																projectClassName = $$value;
																$$settled = false;
															}
														});
														$$renderer.push(`<!---->`);
													},
													$$slots: { default: true }
												});
												$$renderer.push("<!--]-->");
											} else {
												$$renderer.push("<!--[!-->");
												$$renderer.push("<!--]-->");
											}
											$$renderer.push(` `);
											if (Field) {
												$$renderer.push("<!--[-->");
												Field($$renderer, {
													children: ($$renderer) => {
														if (Field_label) {
															$$renderer.push("<!--[-->");
															Field_label($$renderer, {
																for: "project-layer",
																children: ($$renderer) => {
																	$$renderer.push(`<!---->Initial layer`);
																},
																$$slots: { default: true }
															});
															$$renderer.push("<!--]-->");
														} else {
															$$renderer.push("<!--[!-->");
															$$renderer.push("<!--]-->");
														}
														Input($$renderer, {
															id: "project-layer",
															autocomplete: "off",
															get value() {
																return projectLayer;
															},
															set value($$value) {
																projectLayer = $$value;
																$$settled = false;
															}
														});
														$$renderer.push(`<!---->`);
													},
													$$slots: { default: true }
												});
												$$renderer.push("<!--]-->");
											} else {
												$$renderer.push("<!--[!-->");
												$$renderer.push("<!--]-->");
											}
											$$renderer.push(`</div> `);
											if (Field) {
												$$renderer.push("<!--[-->");
												Field($$renderer, {
													orientation: "horizontal",
													children: ($$renderer) => {
														Checkbox($$renderer, {
															id: "project-esm",
															get checked() {
																return projectGenerateEsm;
															},
															set checked($$value) {
																projectGenerateEsm = $$value;
																$$settled = false;
															}
														});
														$$renderer.push(`<!---->`);
														if (Field_content) {
															$$renderer.push("<!--[-->");
															Field_content($$renderer, {
																children: ($$renderer) => {
																	if (Field_label) {
																		$$renderer.push("<!--[-->");
																		Field_label($$renderer, {
																			for: "project-esm",
																			children: ($$renderer) => {
																				$$renderer.push(`<!---->Enable ESM output`);
																			},
																			$$slots: { default: true }
																		});
																		$$renderer.push("<!--]-->");
																	} else {
																		$$renderer.push("<!--[!-->");
																		$$renderer.push("<!--]-->");
																	}
																	if (Field_description) {
																		$$renderer.push("<!--[-->");
																		Field_description($$renderer, {
																			children: ($$renderer) => {
																				$$renderer.push(`<!---->Generate tree-shakeable modules for TypeScript and browser applications.`);
																			},
																			$$slots: { default: true }
																		});
																		$$renderer.push("<!--]-->");
																	} else {
																		$$renderer.push("<!--[!-->");
																		$$renderer.push("<!--]-->");
																	}
																},
																$$slots: { default: true }
															});
															$$renderer.push("<!--]-->");
														} else {
															$$renderer.push("<!--[!-->");
															$$renderer.push("<!--]-->");
														}
													},
													$$slots: { default: true }
												});
												$$renderer.push("<!--]-->");
											} else {
												$$renderer.push("<!--[!-->");
												$$renderer.push("<!--]-->");
											}
											$$renderer.push(` `);
											if (Field) {
												$$renderer.push("<!--[-->");
												Field($$renderer, {
													orientation: "horizontal",
													children: ($$renderer) => {
														Checkbox($$renderer, {
															id: "project-starter",
															get checked() {
																return projectIncludeStarter;
															},
															set checked($$value) {
																projectIncludeStarter = $$value;
																$$settled = false;
															}
														});
														$$renderer.push(`<!---->`);
														if (Field_content) {
															$$renderer.push("<!--[-->");
															Field_content($$renderer, {
																children: ($$renderer) => {
																	if (Field_label) {
																		$$renderer.push("<!--[-->");
																		Field_label($$renderer, {
																			for: "project-starter",
																			children: ($$renderer) => {
																				$$renderer.push(`<!---->Add a starter message`);
																			},
																			$$slots: { default: true }
																		});
																		$$renderer.push("<!--]-->");
																	} else {
																		$$renderer.push("<!--[!-->");
																		$$renderer.push("<!--]-->");
																	}
																	if (Field_description) {
																		$$renderer.push("<!--[-->");
																		Field_description($$renderer, {
																			children: ($$renderer) => {
																				$$renderer.push(`<!---->Create <code>Application.Name</code> in every language.`);
																			},
																			$$slots: { default: true }
																		});
																		$$renderer.push("<!--]-->");
																	} else {
																		$$renderer.push("<!--[!-->");
																		$$renderer.push("<!--]-->");
																	}
																},
																$$slots: { default: true }
															});
															$$renderer.push("<!--]-->");
														} else {
															$$renderer.push("<!--[!-->");
															$$renderer.push("<!--]-->");
														}
													},
													$$slots: { default: true }
												});
												$$renderer.push("<!--]-->");
											} else {
												$$renderer.push("<!--[!-->");
												$$renderer.push("<!--]-->");
											}
										},
										$$slots: { default: true }
									});
									$$renderer.push("<!--]-->");
								} else {
									$$renderer.push("<!--[!-->");
									$$renderer.push("<!--]-->");
								}
							} else if (projectStep === 4 && projectPlan !== void 0) {
								$$renderer.push("<!--[3-->");
								if (Alert) {
									$$renderer.push("<!--[-->");
									Alert($$renderer, {
										children: ($$renderer) => {
											if (Alert_title) {
												$$renderer.push("<!--[-->");
												Alert_title($$renderer, {
													children: ($$renderer) => {
														$$renderer.push(`<!---->Ready to create ${escape_html(projectPlan.catalogId)}`);
													},
													$$slots: { default: true }
												});
												$$renderer.push("<!--]-->");
											} else {
												$$renderer.push("<!--[!-->");
												$$renderer.push("<!--]-->");
											}
											if (Alert_description) {
												$$renderer.push("<!--[-->");
												Alert_description($$renderer, {
													children: ($$renderer) => {
														$$renderer.push(`<!---->${escape_html(projectPlan.locales.length)} ${escape_html(projectPlan.locales.length === 1 ? "language" : "languages")} · ${escape_html(projectPlan.files.length)} files · compiler validated`);
													},
													$$slots: { default: true }
												});
												$$renderer.push("<!--]-->");
											} else {
												$$renderer.push("<!--[!-->");
												$$renderer.push("<!--]-->");
											}
										},
										$$slots: { default: true }
									});
									$$renderer.push("<!--]-->");
								} else {
									$$renderer.push("<!--[!-->");
									$$renderer.push("<!--]-->");
								}
								$$renderer.push(` <dl class="mt-4 grid gap-3 rounded-xl border p-4"><div class="grid gap-1 sm:grid-cols-[7rem_1fr]"><dt class="text-muted-foreground">Directory</dt><dd class="m-0 truncate font-mono text-xs">${escape_html(projectPlan.directory)}</dd></div><div class="grid gap-1 sm:grid-cols-[7rem_1fr]"><dt class="text-muted-foreground">Languages</dt><dd class="m-0 font-medium">${escape_html(projectPlan.locales.map((locale) => locale.tag).join(", "))}</dd></div></dl> <section class="mt-4 overflow-hidden rounded-xl border" aria-label="Files to create"><h4 class="border-b px-4 py-3 font-medium">Files to create</h4><!--[-->`);
								const each_array_21 = ensure_array_like(projectPlan.files);
								for (let $$index_21 = 0, $$length = each_array_21.length; $$index_21 < $$length; $$index_21++) {
									let file = each_array_21[$$index_21];
									$$renderer.push(`<div class="border-b px-4 py-3 last:border-b-0"><code class="text-xs">${escape_html(file)}</code></div>`);
								}
								$$renderer.push(`<!--]--></section>`);
							} else $$renderer.push("<!--[-1-->");
							$$renderer.push(`<!--]--> `);
							if (projectError) {
								$$renderer.push("<!--[0-->");
								if (Alert) {
									$$renderer.push("<!--[-->");
									Alert($$renderer, {
										variant: "destructive",
										class: "mt-4",
										"aria-live": "polite",
										children: ($$renderer) => {
											if (Alert_title) {
												$$renderer.push("<!--[-->");
												Alert_title($$renderer, {
													children: ($$renderer) => {
														$$renderer.push(`<!---->Project is not valid`);
													},
													$$slots: { default: true }
												});
												$$renderer.push("<!--]-->");
											} else {
												$$renderer.push("<!--[!-->");
												$$renderer.push("<!--]-->");
											}
											if (Alert_description) {
												$$renderer.push("<!--[-->");
												Alert_description($$renderer, {
													children: ($$renderer) => {
														$$renderer.push(`<!---->${escape_html(projectError)}`);
													},
													$$slots: { default: true }
												});
												$$renderer.push("<!--]-->");
											} else {
												$$renderer.push("<!--[!-->");
												$$renderer.push("<!--]-->");
											}
										},
										$$slots: { default: true }
									});
									$$renderer.push("<!--]-->");
								} else {
									$$renderer.push("<!--[!-->");
									$$renderer.push("<!--]-->");
								}
							} else $$renderer.push("<!--[-1-->");
							$$renderer.push(`<!--]-->`);
						},
						$$slots: {
							footer: true,
							default: true
						}
					});
				}
			} else $$renderer.push("<!--[-1-->");
			$$renderer.push(`<!--]-->`);
		}
		do {
			$$settled = true;
			$$inner_renderer = $$renderer.copy();
			$$render_inner($$inner_renderer);
		} while (!$$settled);
		$$renderer.subsume($$inner_renderer);
	});
}
//#endregion
export { _page as default };
