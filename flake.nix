{
  description = "Runic Translations SDK development environment";
  inputs.nixpkgs.url = "github:NixOS/nixpkgs/nixpkgs-unstable";
  outputs = { self, nixpkgs }:
    let systems = [ "x86_64-linux" "aarch64-linux" ]; in {
      devShells = nixpkgs.lib.genAttrs systems (system:
        let pkgs = import nixpkgs { inherit system; }; in {
          default = pkgs.mkShell {
            packages = [ pkgs.git pkgs.dotnetCorePackages.sdk_10_0 pkgs.nodejs_24 pkgs.bun pkgs.python3 pkgs.clang pkgs.pkg-config pkgs.zlib ];
            DOTNET_CLI_TELEMETRY_OPTOUT = "1";
            DOTNET_NOLOGO = "1";
            shellHook = ''export NUGET_PACKAGES="$PWD/.cache/nuget"'';
          };
        });
    };
}
