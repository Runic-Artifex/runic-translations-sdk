{
  description = "WebUIToolkit development environment";

  inputs = {
    nixpkgs.url = "github:NixOS/nixpkgs/nixpkgs-unstable";
    cs-webui.url = "github:ViktorJannicke/cs-webui";
  };

  outputs =
    { nixpkgs, cs-webui, ... }:
    let
      supportedSystems = [
        "x86_64-linux"
        "aarch64-linux"
      ];
      forAllSystems = nixpkgs.lib.genAttrs supportedSystems;
    in
    {
      devShells = forAllSystems (
        system:
        let
          pkgs = import nixpkgs { inherit system; };
          inherit (pkgs) lib;
          dotnet = pkgs.dotnetCorePackages.sdk_10_0;
          csWebUiNative = cs-webui.packages.${system}.webui-native;
          nativeLibraryName =
            if pkgs.stdenv.hostPlatform.isDarwin then
              "libwebui-2.dylib"
            else
              "libwebui-2.so";
          linuxRuntimePackages = with pkgs; lib.optionals pkgs.stdenv.hostPlatform.isLinux [
            chromium
            gtk3
            webkitgtk_4_1
            xvfb
          ];
        in
        {
          default = pkgs.mkShell {
            packages = with pkgs; [
              dotnet
              nodejs_24
              powershell

              # Required by the repository's Native AOT verification.
              clang
              zlib
            ] ++ linuxRuntimePackages;

            DOTNET_CLI_TELEMETRY_OPTOUT = "1";
            DOTNET_NOLOGO = "1";
            DisableImplicitLibraryPacksFolder = "true";

            shellHook = ''
              # Keep interactive restores separate from the verification scripts,
              # which intentionally build several different packages at version 1.0.0.
              export NUGET_PACKAGES="$PWD/.direnv/nuget"
              export CSWEBUI_NATIVE_LIBRARY="${csWebUiNative}/lib/${nativeLibraryName}"
              ${lib.optionalString pkgs.stdenv.hostPlatform.isLinux ''
                export LD_LIBRARY_PATH="${lib.makeLibraryPath linuxRuntimePackages}:$LD_LIBRARY_PATH"
                export WEBUI_BROWSER_PATH="${pkgs.chromium}/bin/chromium"
              ''}

              waveGFeed="$PWD/artifacts/wave-g/packages/nuget/feed"
              if [[ -d "$waveGFeed" ]]; then
                export RestoreAdditionalProjectSources="$waveGFeed"
              fi
              unset waveGFeed
            '';
          };
        }
      );
    };
}
