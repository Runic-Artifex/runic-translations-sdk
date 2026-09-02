{
  description = "Runic Translations development environment";

  inputs = {
    nixpkgs.url = "github:NixOS/nixpkgs/nixpkgs-unstable";
  };

  outputs =
    { nixpkgs, ... }:
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
          dotnet = pkgs.dotnetCorePackages.sdk_10_0;
          bunSource =
            if system == "x86_64-linux" then
              {
                url = "https://github.com/oven-sh/bun/releases/download/bun-v1.4.0/bun-linux-x64.zip";
                hash = "sha256-Poy0vf7yJ/hzk33QiQj5gnshI5Q7dfbaMD7xgwiyDKw=";
              }
            else
              {
                url = "https://github.com/oven-sh/bun/releases/download/bun-v1.4.0/bun-linux-aarch64.zip";
                hash = "sha256-rIfaywTWWN3ELVH9DtPfrkuAGjrwi7DJYUeKPS1Zd04=";
              };
          bun = pkgs.stdenvNoCC.mkDerivation {
            pname = "bun";
            version = "1.4.0";
            src = pkgs.fetchzip bunSource;
            dontBuild = true;
            installPhase = ''
              install -Dm755 "$src/bun" "$out/bin/bun"
            '';
          };
        in
        {
          default = pkgs.mkShell {
            packages = with pkgs; [
              dotnet
              powershell
              clang
              nodejs_24
              bun
              p7zip
              zlib
            ];

            DOTNET_CLI_TELEMETRY_OPTOUT = "1";
            DOTNET_NOLOGO = "1";
            DOTNET_ROOT = "${dotnet}/share/dotnet";
            DisableImplicitLibraryPacksFolder = "true";

            shellHook = ''
              # Keep interactive restores separate from the verification scripts,
              # which intentionally build local packages at a fixed test version.
              export NUGET_PACKAGES="$PWD/.direnv/nuget"
            '';
          };
        }
      );
    };
}
