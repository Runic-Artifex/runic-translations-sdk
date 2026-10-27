# Node.js from the official nodejs.org binary archives, for the development shell while nixpkgs' nodejs_24 is older
# than `.node-version`. The hashes are the archives' SHA-256 values from
# https://nodejs.org/dist/v24.22.1/SHASUMS256.txt (in SRI form).
{
  lib,
  stdenv,
  fetchurl,
  autoPatchelfHook,
}:

let
  version = "24.22.1";
  archives = {
    x86_64-linux = {
      platform = "linux-x64";
      hash = "sha256-bBcqmrZxj3uAH7WnvUhq8u5eMnWAhqBLhPzOKqdwleY=";
    };
    aarch64-linux = {
      platform = "linux-arm64";
      hash = "sha256-26v3ocv5R+YFn/rSwnuABB25feoeVTtir97n8T/KM24=";
    };
  };
  archive =
    archives.${stdenv.hostPlatform.system}
      or (throw "nix/toolchain/nodejs-24.nix has no Node ${version} archive for ${stdenv.hostPlatform.system}");
in
stdenv.mkDerivation {
  pname = "nodejs";
  inherit version;

  src = fetchurl {
    url = "https://nodejs.org/dist/v${version}/node-v${version}-${archive.platform}.tar.xz";
    inherit (archive) hash;
  };

  nativeBuildInputs = [ autoPatchelfHook ];
  # libstdc++ and libgcc_s; glibc comes from stdenv.
  buildInputs = [ stdenv.cc.cc.lib ];

  dontConfigure = true;
  dontBuild = true;
  dontStrip = true;

  installPhase = ''
    runHook preInstall
    mkdir -p "$out"
    cp -R bin include lib share "$out"/
    runHook postInstall
  '';

  # npm, npx and corepack are `#!/usr/bin/env node` scripts; point them at this Node.
  preFixup = ''
    PATH="$out/bin:$PATH" patchShebangs "$out/lib/node_modules"
  '';

  meta = {
    description = "Node.js ${version} (official binary distribution)";
    homepage = "https://nodejs.org";
    license = lib.licenses.mit;
    mainProgram = "node";
    platforms = builtins.attrNames archives;
    sourceProvenance = [ lib.sourceTypes.binaryNativeCode ];
  };
}
