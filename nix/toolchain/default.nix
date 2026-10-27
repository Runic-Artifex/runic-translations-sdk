# The repository's pinned .NET SDK and Node for the development shell (global.json disables SDK roll-forward and
# CI installs `.node-version`). Copied from runic-sdk's nix/toolchain (D050).
#
# The locked nixpkgs still has .NET SDK 10.0.401 (runtime 10.0.12) and Node 24.21.0, older than the October 2026
# security releases the repository pins (D050). Until it catches up, both come from the official binaries: the SDK
# through nixpkgs' own binary SDK builder (`dotnetCorePackages.buildDotnetSdk`, the `sdk_10_0-bin` machinery, so
# `.packages`, `.runtime`, `.aspnetcore` and `share/dotnet` behave as for nixpkgs' SDK) with ./dotnet-10.0.nix, and
# Node from the nodejs.org archive (./nodejs-24.nix). Evaluation warns once nixpkgs provides the pins; then delete
# this directory's overrides and use `pkgs.dotnetCorePackages.sdk_10_0` and `pkgs.nodejs_24` again.
{ pkgs }:
let
  inherit (pkgs) lib;
  dotnetPin = (lib.importJSON ../../global.json).sdk.version;
  nodePin = lib.fileContents ../../.node-version;
  # Fail at evaluation when the provided version differs from the repository pin.
  pinned =
    pkg: expected: label:
    assert lib.assertMsg (
      pkg.version == expected
    ) "The Runic SDK toolchain provides ${label} ${pkg.version}, but the repository pins ${expected}";
    pkg;
  untilNixpkgsHas =
    nixpkgsPkg: expected: label:
    lib.warnIf (lib.versionAtLeast nixpkgsPkg.version expected)
      "nixpkgs now provides ${label} ${nixpkgsPkg.version}; remove the official-binary override in nix/toolchain";
in
{
  dotnet-sdk = pinned (untilNixpkgsHas pkgs.dotnetCorePackages.sdk_10_0 dotnetPin ".NET SDK"
    (pkgs.dotnetCorePackages.buildDotnetSdk ./dotnet-10.0.nix).sdk_10_0
  ) dotnetPin ".NET SDK";
  nodejs = pinned (untilNixpkgsHas pkgs.nodejs_24 nodePin "Node" (
    pkgs.callPackage ./nodejs-24.nix { }
  )) nodePin "Node";
}
