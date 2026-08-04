{
  description = "RunicTextResources development environment";

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
        in
        {
          default = pkgs.mkShell {
            packages = with pkgs; [
              dotnet
              powershell
              clang
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
