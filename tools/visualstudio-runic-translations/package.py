"""Package the cross-built MEF extension without invoking Windows-only VSSDK tasks."""
from pathlib import Path
import argparse
import json
import zipfile
import xml.etree.ElementTree as ET

parser = argparse.ArgumentParser()
parser.add_argument("--configuration", default="Debug", choices=["Debug", "Release"])
args = parser.parse_args()
root = Path(__file__).resolve().parent
manifest = root / "source.extension.vsixmanifest"
ns = {"v": "http://schemas.microsoft.com/developer/vsx-schema/2011"}
files = {
    "extension.vsixmanifest": manifest.read_bytes(),
    "Runic.Translations.VisualStudio.dll": (root / "bin" / args.configuration / "net472/Runic.Translations.VisualStudio.dll").read_bytes(),
    "Runic.pkgdef": (root / "Runic.pkgdef").read_bytes(),
    "Grammars/rmf2.tmLanguage.json": (root.parent / "vscode-runic-translations/syntaxes/rmf2.tmLanguage.json").read_bytes(),
    "LICENSE": (root.parent.parent / "LICENSE").read_bytes(),
    "README.md": (root / "README.md").read_bytes(),
}
for asset in ET.fromstring(files["extension.vsixmanifest"]).findall("v:Assets/v:Asset", ns):
    assert asset.attrib["Path"] in files, "Missing declared VSIX asset"
assert files["Runic.Translations.VisualStudio.dll"][:2] == b"MZ", "Build the managed extension first"
assert "rmf2" in json.loads(files["Grammars/rmf2.tmLanguage.json"])["fileTypes"]
files["[Content_Types].xml"] = b'''<?xml version="1.0" encoding="utf-8"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
<Default Extension="vsixmanifest" ContentType="text/xml"/>
<Default Extension="dll" ContentType="application/octet-stream"/>
<Default Extension="pkgdef" ContentType="text/plain"/>
<Default Extension="json" ContentType="application/json"/>
<Default Extension="md" ContentType="text/plain"/>
<Override PartName="/LICENSE" ContentType="text/plain"/>
</Types>'''
output = root / "artifacts/runic-translations-visualstudio.vsix"
output.parent.mkdir(exist_ok=True)
with zipfile.ZipFile(output, "w", compression=zipfile.ZIP_DEFLATED) as archive:
    for name, data in sorted(files.items()):
        info = zipfile.ZipInfo(name, (2026, 1, 1, 0, 0, 0))
        info.compress_type = zipfile.ZIP_DEFLATED
        archive.writestr(info, data)
with zipfile.ZipFile(output) as archive:
    assert archive.testzip() is None
    assert len([name for name in archive.namelist() if name.endswith(".dll")]) == 1, "Do not redistribute VS-owned assemblies"
print(f"PASS VSIX assets, grammar, content types and archive integrity: {output}")
