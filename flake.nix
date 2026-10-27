{
  description = "Runic Translations SDK development environment";
  inputs.nixpkgs.url = "github:NixOS/nixpkgs/nixpkgs-unstable";
  outputs = { self, nixpkgs }:
    let systems = [ "x86_64-linux" "aarch64-linux" ]; in {
      devShells = nixpkgs.lib.genAttrs systems (system:
        let
          pkgs = import nixpkgs { inherit system; };
          inherit (pkgs) lib;
          # global.json disables SDK roll-forward and CI installs `.node-version`; nix/toolchain provides exactly
          # those versions (from the official binaries while nixpkgs is older) and fails evaluation otherwise.
          toolchain = import ./nix/toolchain { inherit pkgs; };
          dotnet = toolchain.dotnet-sdk;
          inherit (toolchain) nodejs;
          # Bun from the official release archive; the hashes match the release's SHASUMS256.txt.
          bunArchive =
            if system == "x86_64-linux" then
              {
                platform = "linux-x64";
                hash = "sha256-SQxqWbkwuEx42tLBeD8BdpDH1bxUasS1yOkkDULCz3U=";
              }
            else
              {
                platform = "linux-aarch64";
                hash = "sha256-NhWc7T3/G+CA3TE/q/vQR5oOPfBLPgZ6Exdqa1CjD5w=";
              };
          bunSource = pkgs.fetchzip {
            url = "https://github.com/oven-sh/bun/releases/download/bun-v1.4.4/bun-${bunArchive.platform}.zip";
            inherit (bunArchive) hash;
          };
          bun_1_4_4 = pkgs.stdenvNoCC.mkDerivation {
            pname = "bun";
            version = "1.4.4";
            src = bunSource;
            nativeBuildInputs = [ pkgs.autoPatchelfHook ];
            buildInputs = [ pkgs.stdenv.cc.cc.lib ];
            dontBuild = true;
            installPhase = ''
              mkdir -p "$out/bin"
              cp bun "$out/bin/bun"
              chmod +x "$out/bin/bun"
            '';
          };
          baseShell = {
            packages = [ pkgs.git dotnet nodejs bun_1_4_4 pkgs.python3 pkgs.clang pkgs.pkg-config pkgs.zlib ];
            DOTNET_CLI_TELEMETRY_OPTOUT = "1";
            DOTNET_NOLOGO = "1";
            DOTNET_ROOT = "${dotnet}/share/dotnet";
            shellHook = ''export NUGET_PACKAGES="$PWD/.cache/nuget"'';
          };
          # Optional CS-WebUI browser/WebView runtime from this flake's lock.
          # Desktop D-Bus and portal services remain owned by the user session.
          editorRuntime = with pkgs; [
            chromium gtk3 webkitgtk_4_1 glib glib-networking
            gsettings-desktop-schemas dbus xdg-desktop-portal xdg-desktop-portal-gtk
          ] ++ (with pkgs.gst_all_1; [
            gstreamer gst-plugins-base gst-plugins-good gst-plugins-bad gst-libav
          ]);
        in {
          default = pkgs.mkShell baseShell;
          editor = pkgs.mkShell (baseShell // {
            packages = baseShell.packages ++ editorRuntime;
            shellHook = baseShell.shellHook + ''

              export WEBUI_BROWSER_PATH="${pkgs.chromium}/bin/chromium"
              # CS-WebUI selects the browser for `edit` from CSWEBUI_BROWSER_PATH (no fallback).
              export CSWEBUI_BROWSER_PATH="$WEBUI_BROWSER_PATH"
              export PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH="$WEBUI_BROWSER_PATH"
              export LD_LIBRARY_PATH="${lib.makeLibraryPath editorRuntime}''${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
              export GIO_EXTRA_MODULES="${pkgs.glib-networking}/lib/gio/modules''${GIO_EXTRA_MODULES:+:$GIO_EXTRA_MODULES}"
              export GST_PLUGIN_SYSTEM_PATH_1_0="${lib.makeSearchPath "lib/gstreamer-1.0" (with pkgs.gst_all_1; [ gstreamer gst-plugins-base gst-plugins-good gst-plugins-bad gst-libav ])}''${GST_PLUGIN_SYSTEM_PATH_1_0:+:$GST_PLUGIN_SYSTEM_PATH_1_0}"
              export XDG_DATA_DIRS="${lib.concatMapStringsSep ":" (package: "${package}/share/gsettings-schemas/${package.name}") [ pkgs.gtk3 pkgs.gsettings-desktop-schemas ]}''${XDG_DATA_DIRS:+:$XDG_DATA_DIRS}"
            '';
          });
        });
    };
}
