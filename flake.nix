{
  description = "Runic Translations SDK development environment";
  inputs.nixpkgs.url = "github:NixOS/nixpkgs/nixpkgs-unstable";
  outputs = { self, nixpkgs }:
    let systems = [ "x86_64-linux" "aarch64-linux" ]; in {
      devShells = nixpkgs.lib.genAttrs systems (system:
        let
          pkgs = import nixpkgs { inherit system; };
          inherit (pkgs) lib;
          baseShell = {
            packages = [ pkgs.git pkgs.dotnetCorePackages.sdk_10_0 pkgs.nodejs_24 pkgs.bun pkgs.python3 pkgs.clang pkgs.pkg-config pkgs.zlib ];
            DOTNET_CLI_TELEMETRY_OPTOUT = "1";
            DOTNET_NOLOGO = "1";
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
