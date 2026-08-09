
// this file is generated — do not edit it


/// <reference types="@sveltejs/kit" />

/**
 * This module provides access to environment variables that are injected _statically_ into your bundle at build time and are limited to _private_ access.
 * 
 * |         | Runtime                                                                    | Build time                                                               |
 * | ------- | -------------------------------------------------------------------------- | ------------------------------------------------------------------------ |
 * | Private | [`$env/dynamic/private`](https://svelte.dev/docs/kit/$env-dynamic-private) | [`$env/static/private`](https://svelte.dev/docs/kit/$env-static-private) |
 * | Public  | [`$env/dynamic/public`](https://svelte.dev/docs/kit/$env-dynamic-public)   | [`$env/static/public`](https://svelte.dev/docs/kit/$env-static-public)   |
 * 
 * Static environment variables are [loaded by Vite](https://vitejs.dev/guide/env-and-mode.html#env-files) from `.env` files and `process.env` at build time and then statically injected into your bundle at build time, enabling optimisations like dead code elimination.
 * 
 * **_Private_ access:**
 * 
 * - This module cannot be imported into client-side code
 * - This module only includes variables that _do not_ begin with [`config.kit.env.publicPrefix`](https://svelte.dev/docs/kit/configuration#env) _and do_ start with [`config.kit.env.privatePrefix`](https://svelte.dev/docs/kit/configuration#env) (if configured)
 * 
 * For example, given the following build time environment:
 * 
 * ```env
 * ENVIRONMENT=production
 * PUBLIC_BASE_URL=http://site.com
 * ```
 * 
 * With the default `publicPrefix` and `privatePrefix`:
 * 
 * ```ts
 * import { ENVIRONMENT, PUBLIC_BASE_URL } from '$env/static/private';
 * 
 * console.log(ENVIRONMENT); // => "production"
 * console.log(PUBLIC_BASE_URL); // => throws error during build
 * ```
 * 
 * The above values will be the same _even if_ different values for `ENVIRONMENT` or `PUBLIC_BASE_URL` are set at runtime, as they are statically replaced in your code with their build time values.
 */
declare module '$env/static/private' {
	export const NODE_ENV: string;
	export const CODEX_LINUX_FEATURES_DIR: string;
	export const depsHostHostPropagated: string;
	export const name: string;
	export const NIX_LDFLAGS: string;
	export const INVOCATION_ID: string;
	export const LC_IDENTIFICATION: string;
	export const MSBUILDUSESERVER: string;
	export const DOTNET_ROOT: string;
	export const MANAGERPIDFDID: string;
	export const XDG_SEAT_PATH: string;
	export const NIX_STORE: string;
	export const XDG_SEAT: string;
	export const _: string;
	export const cmakeFlags: string;
	export const NIXOS_OZONE_WL: string;
	export const TZDIR: string;
	export const ZSH_TMUX_AUTOSTART: string;
	export const LS_COLORS: string;
	export const doInstallCheck: string;
	export const LC_PAPER: string;
	export const WAYLAND_DISPLAY: string;
	export const LD: string;
	export const HOME: string;
	export const KDE_SESSION_UID: string;
	export const propagatedNativeBuildInputs: string;
	export const CODEX_LINUX_APP_DISPLAY_NAME: string;
	export const XKB_DEFAULT_MODEL: string;
	export const shell: string;
	export const npm_command: string;
	export const SYSTEMD_XKB_DIRECTORY: string;
	export const XKB_DEFAULT_VARIANT: string;
	export const depsBuildBuildPropagated: string;
	export const ELECTRON_RENDERER_URL: string;
	export const NIX_CC: string;
	export const TEMPDIR: string;
	export const LC_MEASUREMENT: string;
	export const SYSTEMD_EXEC_PID: string;
	export const npm_config_init_module: string;
	export const GTK2_RC_FILES: string;
	export const CUPS_DATADIR: string;
	export const ELECTRON_OZONE_PLATFORM_HINT: string;
	export const NIX_LD: string;
	export const CODEX_CLI_PATH: string;
	export const CODEX_BROWSER_USE_NODE_PATH: string;
	export const XDG_SESSION_TYPE: string;
	export const npm_config_userconfig: string;
	export const XDG_SESSION_DESKTOP: string;
	export const NIX_SSL_CERT_FILE: string;
	export const MSBuildSDKsPath: string;
	export const LOCALE_ARCHIVE: string;
	export const doCheck: string;
	export const depsTargetTarget: string;
	export const CONFIG_SHELL: string;
	export const CODEX_CLI_SOURCE_PATH: string;
	export const NIX_PROFILES: string;
	export const phases: string;
	export const NIX_CFLAGS_COMPILE: string;
	export const outputs: string;
	export const __HM_SESS_VARS_SOURCED: string;
	export const EDITOR: string;
	export const LC_TELEPHONE: string;
	export const NIX_BINTOOLS: string;
	export const NIX_HARDENING_ENABLE: string;
	export const GPG_TTY: string;
	export const buildPhase: string;
	export const MANAGERPID: string;
	export const npm_config_prefix: string;
	export const XCURSOR_SIZE: string;
	export const RUNIC_TEXT_MANIFEST: string;
	export const MSBuildLoadMicrosoftTargetsReadOnly: string;
	export const NODE: string;
	export const npm_config_cache: string;
	export const XKB_DEFAULT_LAYOUT: string;
	export const TERM: string;
	export const patches: string;
	export const HOST_PATH: string;
	export const XDG_CONFIG_DIRS: string;
	export const ICEAUTHORITY: string;
	export const LC_TIME: string;
	export const DOTNET_CLI_HOME: string;
	export const CODEX_INTERNAL_ORIGINATOR_OVERRIDE: string;
	export const USER: string;
	export const COLORTERM: string;
	export const shellHook: string;
	export const QML2_IMPORT_PATH: string;
	export const ZSH_TMUX_AUTOSTARTED: string;
	export const PWD: string;
	export const LC_ALL: string;
	export const NIX_BUILD_CORES: string;
	export const builder: string;
	export const COLOR: string;
	export const NO_AT_BRIDGE: string;
	export const configureFlags: string;
	export const CODEX_DESKTOP_LAUNCH_ACTION_SOCKET: string;
	export const npm_package_version: string;
	export const SSH_ASKPASS: string;
	export const INFOPATH: string;
	export const LC_ADDRESS: string;
	export const LOCALE_ARCHIVE_2_27: string;
	export const ALSA_PLUGIN_DIR: string;
	export const SHELL: string;
	export const NIX_ENFORCE_NO_NATIVE: string;
	export const LOGNAME: string;
	export const MEMORY_PRESSURE_WATCH: string;
	export const _MSBUILDTLENABLED: string;
	export const CODEX_ELECTRON_RESOURCES_PATH: string;
	export const XCURSOR_THEME: string;
	export const depsHostHost: string;
	export const MEMORY_PRESSURE_WRITE: string;
	export const POWERSHELL_TELEMETRY_OPTOUT: string;
	export const QTWEBKIT_PLUGIN_PATH: string;
	export const MSBUILDALWAYSOVERWRITEREADONLYFILES: string;
	export const MSBUILDTERMINALLOGGER: string;
	export const LOG_FORMAT: string;
	export const CODEX_LINUX_USER_PATH: string;
	export const CODEX_EXECUTABLE_PATH: string;
	export const CODEX_LINUX_APP_STATE_DIR: string;
	export const NODE_REPL_TRUSTED_BROWSER_CLIENT_SHA256S: string;
	export const GDK_BACKEND: string;
	export const OBJDUMP: string;
	export const system: string;
	export const GDK_PIXBUF_MODULE_FILE: string;
	export const DOTNET_SKIP_WORKLOAD_INTEGRITY_CHECK: string;
	export const DEFAULT_WALLPAPER_SET: string;
	export const SANE_CONFIG_DIR: string;
	export const CODEX_LINUX_WEBVIEW_PORT: string;
	export const XDG_SESSION_PATH: string;
	export const npm_execpath: string;
	export const DEFAULT_WALLPAPERS_DIR: string;
	export const depsTargetTargetPropagated: string;
	export const out: string;
	export const XCURSOR_PATH: string;
	export const NIX_PATH: string;
	export const XAUTHORITY: string;
	export const DisableImplicitLibraryPacksFolder: string;
	export const GH_PAGER: string;
	export const GTK_RC_FILES: string;
	export const SIZE: string;
	export const LC_NUMERIC: string;
	export const stdenv: string;
	export const GIO_EXTRA_MODULES: string;
	export const CODEX_MANAGED_NODE_RUNTIME_DIR: string;
	export const __ETC_PROFILE_DONE: string;
	export const npm_package_name: string;
	export const NIX_CC_WRAPPER_TARGET_HOST_x86_64_unknown_linux_gnu: string;
	export const MSBUILDFAILONDRIVEENUMERATINGWILDCARD: string;
	export const NIXPKGS_CONFIG: string;
	export const JOURNAL_STREAM: string;
	export const mesonFlags: string;
	export const DESKTOP_SESSION: string;
	export const DOTNET_SKIP_FIRST_TIME_EXPERIENCE: string;
	export const GTK_PATH: string;
	export const LIBEXEC_PATH: string;
	export const DISABLE_AUTO_UPDATE: string;
	export const TMPDIR: string;
	export const SSH_AUTH_SOCK: string;
	export const BAMF_DESKTOP_FILE_HINT: string;
	export const NIXPKGS_QT6_QML_IMPORT_PATH: string;
	export const INIT_CWD: string;
	export const TEMP: string;
	export const AS: string;
	export const CHROME_DESKTOP: string;
	export const GNUPGHOME: string;
	export const npm_config_global_prefix: string;
	export const NIXOS_XDG_OPEN_USE_PORTAL: string;
	export const CODEX_HOME: string;
	export const STRINGS: string;
	export const GIT_PAGER: string;
	export const NIX_BUILD_TOP: string;
	export const CODEX_LINUX_SETTINGS_FILE: string;
	export const NIX_GCROOT: string;
	export const KDE_APPLICATIONS_AS_SCOPE: string;
	export const NIX_BINTOOLS_WRAPPER_TARGET_HOST_x86_64_unknown_linux_gnu: string;
	export const NIX_XDG_DESKTOP_PORTAL_DIR: string;
	export const XDG_MENU_PREFIX: string;
	export const NODE_REPL_NODE_PATH: string;
	export const CODEX_LINUX_APP_ID: string;
	export const preferLocalBuild: string;
	export const CODEX_SHELL: string;
	export const dontAddDisableDepTrack: string;
	export const XDG_SESSION_CLASS: string;
	export const CODEX_THREAD_ID: string;
	export const _JAVA_AWT_WM_NONREPARENTING: string;
	export const npm_config_globalconfig: string;
	export const strictDeps: string;
	export const READELF: string;
	export const DOTNET_HOST_PATH: string;
	export const PAM_KWALLET5_LOGIN: string;
	export const npm_config_npm_version: string;
	export const PSModulePath: string;
	export const FC_FONTATIONS: string;
	export const CODEX_LINUX_APP_DIR: string;
	export const NODE_REPL_TRUSTED_CODE_PATHS: string;
	export const LANG: string;
	export const AR: string;
	export const LC_MONETARY: string;
	export const NIX_USER_PROFILE_DIR: string;
	export const KDE_SESSION_VERSION: string;
	export const CAROOT: string;
	export const QT_WAYLAND_RECONNECT: string;
	export const npm_lifecycle_event: string;
	export const IN_NIX_SHELL: string;
	export const LC_NAME: string;
	export const STRIP: string;
	export const SHLVL: string;
	export const nativeBuildInputs: string;
	export const NM: string;
	export const DISPLAY: string;
	export const __NIXOS_SET_ENVIRONMENT_DONE: string;
	export const SVELTEKIT_FORK: string;
	export const XDG_VTNR: string;
	export const DOTNET_NOLOGO: string;
	export const depsBuildTarget: string;
	export const CODEX_NODE_REPL_PATH: string;
	export const WALLPAPERS_DIR: string;
	export const npm_lifecycle_script: string;
	export const PAGER: string;
	export const QT_PLUGIN_PATH: string;
	export const NIX_LD_LIBRARY_PATH: string;
	export const buildInputs: string;
	export const SOURCE_DATE_EPOCH: string;
	export const npm_package_json: string;
	export const XDG_SESSION_ID: string;
	export const CODEX_REMOTE_CONTROL_DAEMON_AUTOSTART_DISABLED: string;
	export const npm_config_local_prefix: string;
	export const LESSKEYIN_SYSTEM: string;
	export const MSBuildExtensionsPath: string;
	export const npm_config_user_agent: string;
	export const NUGET_PACKAGES: string;
	export const depsBuildBuild: string;
	export const propagatedBuildInputs: string;
	export const TERMINFO_DIRS: string;
	export const LD_LIBRARY_PATH: string;
	export const DISABLE_AUTOUPDATER: string;
	export const LC_CTYPE: string;
	export const XDG_RUNTIME_DIR: string;
	export const CODEX_CI: string;
	export const KDE_FULL_SESSION: string;
	export const OBJCOPY: string;
	export const SESSION_MANAGER: string;
	export const DOTNET_CLI_TELEMETRY_OPTOUT: string;
	export const CODEX_LINUX_LAUNCHER_CMD: string;
	export const XDG_DATA_DIRS: string;
	export const TMP: string;
	export const npm_config_noproxy: string;
	export const PATH: string;
	export const npm_config_node_gyp: string;
	export const NO_COLOR: string;
	export const CC: string;
	export const DBUS_SESSION_BUS_ADDRESS: string;
	export const depsBuildTargetPropagated: string;
	export const RUST_LOG: string;
	export const KPACKAGE_DEP_RESOLVERS_PATH: string;
	export const CXX: string;
	export const ICON_THEME: string;
	export const npm_config_allow_scripts: string;
	export const BROWSER_USE_AVAILABLE_BACKENDS: string;
	export const XKB_DEFAULT_OPTIONS: string;
	export const __structuredAttrs: string;
	export const XDG_CURRENT_DESKTOP: string;
	export const npm_node_execpath: string;
	export const CODEX_PERMISSION_PROFILE: string;
	export const RANLIB: string;
}

/**
 * This module provides access to environment variables that are injected _statically_ into your bundle at build time and are _publicly_ accessible.
 * 
 * |         | Runtime                                                                    | Build time                                                               |
 * | ------- | -------------------------------------------------------------------------- | ------------------------------------------------------------------------ |
 * | Private | [`$env/dynamic/private`](https://svelte.dev/docs/kit/$env-dynamic-private) | [`$env/static/private`](https://svelte.dev/docs/kit/$env-static-private) |
 * | Public  | [`$env/dynamic/public`](https://svelte.dev/docs/kit/$env-dynamic-public)   | [`$env/static/public`](https://svelte.dev/docs/kit/$env-static-public)   |
 * 
 * Static environment variables are [loaded by Vite](https://vitejs.dev/guide/env-and-mode.html#env-files) from `.env` files and `process.env` at build time and then statically injected into your bundle at build time, enabling optimisations like dead code elimination.
 * 
 * **_Public_ access:**
 * 
 * - This module _can_ be imported into client-side code
 * - **Only** variables that begin with [`config.kit.env.publicPrefix`](https://svelte.dev/docs/kit/configuration#env) (which defaults to `PUBLIC_`) are included
 * 
 * For example, given the following build time environment:
 * 
 * ```env
 * ENVIRONMENT=production
 * PUBLIC_BASE_URL=http://site.com
 * ```
 * 
 * With the default `publicPrefix` and `privatePrefix`:
 * 
 * ```ts
 * import { ENVIRONMENT, PUBLIC_BASE_URL } from '$env/static/public';
 * 
 * console.log(ENVIRONMENT); // => throws error during build
 * console.log(PUBLIC_BASE_URL); // => "http://site.com"
 * ```
 * 
 * The above values will be the same _even if_ different values for `ENVIRONMENT` or `PUBLIC_BASE_URL` are set at runtime, as they are statically replaced in your code with their build time values.
 */
declare module '$env/static/public' {
	
}

/**
 * This module provides access to environment variables set _dynamically_ at runtime and that are limited to _private_ access.
 * 
 * |         | Runtime                                                                    | Build time                                                               |
 * | ------- | -------------------------------------------------------------------------- | ------------------------------------------------------------------------ |
 * | Private | [`$env/dynamic/private`](https://svelte.dev/docs/kit/$env-dynamic-private) | [`$env/static/private`](https://svelte.dev/docs/kit/$env-static-private) |
 * | Public  | [`$env/dynamic/public`](https://svelte.dev/docs/kit/$env-dynamic-public)   | [`$env/static/public`](https://svelte.dev/docs/kit/$env-static-public)   |
 * 
 * Dynamic environment variables are defined by the platform you're running on. For example if you're using [`adapter-node`](https://github.com/sveltejs/kit/tree/main/packages/adapter-node) (or running [`vite preview`](https://svelte.dev/docs/kit/cli)), this is equivalent to `process.env`.
 * 
 * **_Private_ access:**
 * 
 * - This module cannot be imported into client-side code
 * - This module includes variables that _do not_ begin with [`config.kit.env.publicPrefix`](https://svelte.dev/docs/kit/configuration#env) _and do_ start with [`config.kit.env.privatePrefix`](https://svelte.dev/docs/kit/configuration#env) (if configured)
 * 
 * > [!NOTE] In `dev`, `$env/dynamic` includes environment variables from `.env`. In `prod`, this behavior will depend on your adapter.
 * 
 * > [!NOTE] To get correct types, environment variables referenced in your code should be declared (for example in an `.env` file), even if they don't have a value until the app is deployed:
 * >
 * > ```env
 * > MY_FEATURE_FLAG=
 * > ```
 * >
 * > You can override `.env` values from the command line like so:
 * >
 * > ```sh
 * > MY_FEATURE_FLAG="enabled" npm run dev
 * > ```
 * 
 * For example, given the following runtime environment:
 * 
 * ```env
 * ENVIRONMENT=production
 * PUBLIC_BASE_URL=http://site.com
 * ```
 * 
 * With the default `publicPrefix` and `privatePrefix`:
 * 
 * ```ts
 * import { env } from '$env/dynamic/private';
 * 
 * console.log(env.ENVIRONMENT); // => "production"
 * console.log(env.PUBLIC_BASE_URL); // => undefined
 * ```
 */
declare module '$env/dynamic/private' {
	export const env: {
		NODE_ENV: string;
		CODEX_LINUX_FEATURES_DIR: string;
		depsHostHostPropagated: string;
		name: string;
		NIX_LDFLAGS: string;
		INVOCATION_ID: string;
		LC_IDENTIFICATION: string;
		MSBUILDUSESERVER: string;
		DOTNET_ROOT: string;
		MANAGERPIDFDID: string;
		XDG_SEAT_PATH: string;
		NIX_STORE: string;
		XDG_SEAT: string;
		_: string;
		cmakeFlags: string;
		NIXOS_OZONE_WL: string;
		TZDIR: string;
		ZSH_TMUX_AUTOSTART: string;
		LS_COLORS: string;
		doInstallCheck: string;
		LC_PAPER: string;
		WAYLAND_DISPLAY: string;
		LD: string;
		HOME: string;
		KDE_SESSION_UID: string;
		propagatedNativeBuildInputs: string;
		CODEX_LINUX_APP_DISPLAY_NAME: string;
		XKB_DEFAULT_MODEL: string;
		shell: string;
		npm_command: string;
		SYSTEMD_XKB_DIRECTORY: string;
		XKB_DEFAULT_VARIANT: string;
		depsBuildBuildPropagated: string;
		ELECTRON_RENDERER_URL: string;
		NIX_CC: string;
		TEMPDIR: string;
		LC_MEASUREMENT: string;
		SYSTEMD_EXEC_PID: string;
		npm_config_init_module: string;
		GTK2_RC_FILES: string;
		CUPS_DATADIR: string;
		ELECTRON_OZONE_PLATFORM_HINT: string;
		NIX_LD: string;
		CODEX_CLI_PATH: string;
		CODEX_BROWSER_USE_NODE_PATH: string;
		XDG_SESSION_TYPE: string;
		npm_config_userconfig: string;
		XDG_SESSION_DESKTOP: string;
		NIX_SSL_CERT_FILE: string;
		MSBuildSDKsPath: string;
		LOCALE_ARCHIVE: string;
		doCheck: string;
		depsTargetTarget: string;
		CONFIG_SHELL: string;
		CODEX_CLI_SOURCE_PATH: string;
		NIX_PROFILES: string;
		phases: string;
		NIX_CFLAGS_COMPILE: string;
		outputs: string;
		__HM_SESS_VARS_SOURCED: string;
		EDITOR: string;
		LC_TELEPHONE: string;
		NIX_BINTOOLS: string;
		NIX_HARDENING_ENABLE: string;
		GPG_TTY: string;
		buildPhase: string;
		MANAGERPID: string;
		npm_config_prefix: string;
		XCURSOR_SIZE: string;
		RUNIC_TEXT_MANIFEST: string;
		MSBuildLoadMicrosoftTargetsReadOnly: string;
		NODE: string;
		npm_config_cache: string;
		XKB_DEFAULT_LAYOUT: string;
		TERM: string;
		patches: string;
		HOST_PATH: string;
		XDG_CONFIG_DIRS: string;
		ICEAUTHORITY: string;
		LC_TIME: string;
		DOTNET_CLI_HOME: string;
		CODEX_INTERNAL_ORIGINATOR_OVERRIDE: string;
		USER: string;
		COLORTERM: string;
		shellHook: string;
		QML2_IMPORT_PATH: string;
		ZSH_TMUX_AUTOSTARTED: string;
		PWD: string;
		LC_ALL: string;
		NIX_BUILD_CORES: string;
		builder: string;
		COLOR: string;
		NO_AT_BRIDGE: string;
		configureFlags: string;
		CODEX_DESKTOP_LAUNCH_ACTION_SOCKET: string;
		npm_package_version: string;
		SSH_ASKPASS: string;
		INFOPATH: string;
		LC_ADDRESS: string;
		LOCALE_ARCHIVE_2_27: string;
		ALSA_PLUGIN_DIR: string;
		SHELL: string;
		NIX_ENFORCE_NO_NATIVE: string;
		LOGNAME: string;
		MEMORY_PRESSURE_WATCH: string;
		_MSBUILDTLENABLED: string;
		CODEX_ELECTRON_RESOURCES_PATH: string;
		XCURSOR_THEME: string;
		depsHostHost: string;
		MEMORY_PRESSURE_WRITE: string;
		POWERSHELL_TELEMETRY_OPTOUT: string;
		QTWEBKIT_PLUGIN_PATH: string;
		MSBUILDALWAYSOVERWRITEREADONLYFILES: string;
		MSBUILDTERMINALLOGGER: string;
		LOG_FORMAT: string;
		CODEX_LINUX_USER_PATH: string;
		CODEX_EXECUTABLE_PATH: string;
		CODEX_LINUX_APP_STATE_DIR: string;
		NODE_REPL_TRUSTED_BROWSER_CLIENT_SHA256S: string;
		GDK_BACKEND: string;
		OBJDUMP: string;
		system: string;
		GDK_PIXBUF_MODULE_FILE: string;
		DOTNET_SKIP_WORKLOAD_INTEGRITY_CHECK: string;
		DEFAULT_WALLPAPER_SET: string;
		SANE_CONFIG_DIR: string;
		CODEX_LINUX_WEBVIEW_PORT: string;
		XDG_SESSION_PATH: string;
		npm_execpath: string;
		DEFAULT_WALLPAPERS_DIR: string;
		depsTargetTargetPropagated: string;
		out: string;
		XCURSOR_PATH: string;
		NIX_PATH: string;
		XAUTHORITY: string;
		DisableImplicitLibraryPacksFolder: string;
		GH_PAGER: string;
		GTK_RC_FILES: string;
		SIZE: string;
		LC_NUMERIC: string;
		stdenv: string;
		GIO_EXTRA_MODULES: string;
		CODEX_MANAGED_NODE_RUNTIME_DIR: string;
		__ETC_PROFILE_DONE: string;
		npm_package_name: string;
		NIX_CC_WRAPPER_TARGET_HOST_x86_64_unknown_linux_gnu: string;
		MSBUILDFAILONDRIVEENUMERATINGWILDCARD: string;
		NIXPKGS_CONFIG: string;
		JOURNAL_STREAM: string;
		mesonFlags: string;
		DESKTOP_SESSION: string;
		DOTNET_SKIP_FIRST_TIME_EXPERIENCE: string;
		GTK_PATH: string;
		LIBEXEC_PATH: string;
		DISABLE_AUTO_UPDATE: string;
		TMPDIR: string;
		SSH_AUTH_SOCK: string;
		BAMF_DESKTOP_FILE_HINT: string;
		NIXPKGS_QT6_QML_IMPORT_PATH: string;
		INIT_CWD: string;
		TEMP: string;
		AS: string;
		CHROME_DESKTOP: string;
		GNUPGHOME: string;
		npm_config_global_prefix: string;
		NIXOS_XDG_OPEN_USE_PORTAL: string;
		CODEX_HOME: string;
		STRINGS: string;
		GIT_PAGER: string;
		NIX_BUILD_TOP: string;
		CODEX_LINUX_SETTINGS_FILE: string;
		NIX_GCROOT: string;
		KDE_APPLICATIONS_AS_SCOPE: string;
		NIX_BINTOOLS_WRAPPER_TARGET_HOST_x86_64_unknown_linux_gnu: string;
		NIX_XDG_DESKTOP_PORTAL_DIR: string;
		XDG_MENU_PREFIX: string;
		NODE_REPL_NODE_PATH: string;
		CODEX_LINUX_APP_ID: string;
		preferLocalBuild: string;
		CODEX_SHELL: string;
		dontAddDisableDepTrack: string;
		XDG_SESSION_CLASS: string;
		CODEX_THREAD_ID: string;
		_JAVA_AWT_WM_NONREPARENTING: string;
		npm_config_globalconfig: string;
		strictDeps: string;
		READELF: string;
		DOTNET_HOST_PATH: string;
		PAM_KWALLET5_LOGIN: string;
		npm_config_npm_version: string;
		PSModulePath: string;
		FC_FONTATIONS: string;
		CODEX_LINUX_APP_DIR: string;
		NODE_REPL_TRUSTED_CODE_PATHS: string;
		LANG: string;
		AR: string;
		LC_MONETARY: string;
		NIX_USER_PROFILE_DIR: string;
		KDE_SESSION_VERSION: string;
		CAROOT: string;
		QT_WAYLAND_RECONNECT: string;
		npm_lifecycle_event: string;
		IN_NIX_SHELL: string;
		LC_NAME: string;
		STRIP: string;
		SHLVL: string;
		nativeBuildInputs: string;
		NM: string;
		DISPLAY: string;
		__NIXOS_SET_ENVIRONMENT_DONE: string;
		SVELTEKIT_FORK: string;
		XDG_VTNR: string;
		DOTNET_NOLOGO: string;
		depsBuildTarget: string;
		CODEX_NODE_REPL_PATH: string;
		WALLPAPERS_DIR: string;
		npm_lifecycle_script: string;
		PAGER: string;
		QT_PLUGIN_PATH: string;
		NIX_LD_LIBRARY_PATH: string;
		buildInputs: string;
		SOURCE_DATE_EPOCH: string;
		npm_package_json: string;
		XDG_SESSION_ID: string;
		CODEX_REMOTE_CONTROL_DAEMON_AUTOSTART_DISABLED: string;
		npm_config_local_prefix: string;
		LESSKEYIN_SYSTEM: string;
		MSBuildExtensionsPath: string;
		npm_config_user_agent: string;
		NUGET_PACKAGES: string;
		depsBuildBuild: string;
		propagatedBuildInputs: string;
		TERMINFO_DIRS: string;
		LD_LIBRARY_PATH: string;
		DISABLE_AUTOUPDATER: string;
		LC_CTYPE: string;
		XDG_RUNTIME_DIR: string;
		CODEX_CI: string;
		KDE_FULL_SESSION: string;
		OBJCOPY: string;
		SESSION_MANAGER: string;
		DOTNET_CLI_TELEMETRY_OPTOUT: string;
		CODEX_LINUX_LAUNCHER_CMD: string;
		XDG_DATA_DIRS: string;
		TMP: string;
		npm_config_noproxy: string;
		PATH: string;
		npm_config_node_gyp: string;
		NO_COLOR: string;
		CC: string;
		DBUS_SESSION_BUS_ADDRESS: string;
		depsBuildTargetPropagated: string;
		RUST_LOG: string;
		KPACKAGE_DEP_RESOLVERS_PATH: string;
		CXX: string;
		ICON_THEME: string;
		npm_config_allow_scripts: string;
		BROWSER_USE_AVAILABLE_BACKENDS: string;
		XKB_DEFAULT_OPTIONS: string;
		__structuredAttrs: string;
		XDG_CURRENT_DESKTOP: string;
		npm_node_execpath: string;
		CODEX_PERMISSION_PROFILE: string;
		RANLIB: string;
		[key: `PUBLIC_${string}`]: undefined;
		[key: `${string}`]: string | undefined;
	}
}

/**
 * This module provides access to environment variables set _dynamically_ at runtime and that are _publicly_ accessible.
 * 
 * |         | Runtime                                                                    | Build time                                                               |
 * | ------- | -------------------------------------------------------------------------- | ------------------------------------------------------------------------ |
 * | Private | [`$env/dynamic/private`](https://svelte.dev/docs/kit/$env-dynamic-private) | [`$env/static/private`](https://svelte.dev/docs/kit/$env-static-private) |
 * | Public  | [`$env/dynamic/public`](https://svelte.dev/docs/kit/$env-dynamic-public)   | [`$env/static/public`](https://svelte.dev/docs/kit/$env-static-public)   |
 * 
 * Dynamic environment variables are defined by the platform you're running on. For example if you're using [`adapter-node`](https://github.com/sveltejs/kit/tree/main/packages/adapter-node) (or running [`vite preview`](https://svelte.dev/docs/kit/cli)), this is equivalent to `process.env`.
 * 
 * **_Public_ access:**
 * 
 * - This module _can_ be imported into client-side code
 * - **Only** variables that begin with [`config.kit.env.publicPrefix`](https://svelte.dev/docs/kit/configuration#env) (which defaults to `PUBLIC_`) are included
 * 
 * > [!NOTE] In `dev`, `$env/dynamic` includes environment variables from `.env`. In `prod`, this behavior will depend on your adapter.
 * 
 * > [!NOTE] To get correct types, environment variables referenced in your code should be declared (for example in an `.env` file), even if they don't have a value until the app is deployed:
 * >
 * > ```env
 * > MY_FEATURE_FLAG=
 * > ```
 * >
 * > You can override `.env` values from the command line like so:
 * >
 * > ```sh
 * > MY_FEATURE_FLAG="enabled" npm run dev
 * > ```
 * 
 * For example, given the following runtime environment:
 * 
 * ```env
 * ENVIRONMENT=production
 * PUBLIC_BASE_URL=http://example.com
 * ```
 * 
 * With the default `publicPrefix` and `privatePrefix`:
 * 
 * ```ts
 * import { env } from '$env/dynamic/public';
 * console.log(env.ENVIRONMENT); // => undefined, not public
 * console.log(env.PUBLIC_BASE_URL); // => "http://example.com"
 * ```
 * 
 * ```
 * 
 * ```
 */
declare module '$env/dynamic/public' {
	export const env: {
		[key: `PUBLIC_${string}`]: string | undefined;
	}
}
