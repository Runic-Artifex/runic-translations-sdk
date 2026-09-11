"""Verify and copy a VSIX produced by Microsoft's Windows VSSDK build targets."""
from pathlib import Path
import argparse
import json
import shutil
import zipfile
import xml.etree.ElementTree as ET

parser = argparse.ArgumentParser()
parser.add_argument('--configuration', default='Debug', choices=['Debug', 'Release'])
parser.add_argument('--input', type=Path, help='Native-built VSIX, optionally copied from Windows')
args = parser.parse_args()
root = Path(__file__).resolve().parent
source = args.input or root / 'bin' / args.configuration / 'net472/Runic.Translations.VisualStudio.vsix'
if not source.is_file():
    parser.error('Build with Visual Studio MSBuild on Windows first, or supply --input with that VSIX.')
ns = {'v': 'http://schemas.microsoft.com/developer/vsx-schema/2011'}
with zipfile.ZipFile(source) as archive:
    assert archive.testzip() is None, 'Corrupt VSIX'
    names = set(archive.namelist())
    for name in ('manifest.json', 'catalog.json', '[Content_Types].xml', 'LICENSE.txt', 'Runic.Translations.VisualStudio.pkgdef'):
        assert name in names, f'Missing native installer metadata: {name}'
    manifest = ET.fromstring(archive.read('extension.vsixmanifest'))
    for asset in manifest.findall('v:Assets/v:Asset', ns):
        assert asset.attrib['Path'].replace('\\', '/') in names, 'Missing declared VSIX asset'
    dlls = [name for name in names if name.lower().endswith('.dll')]
    assert dlls == ['Runic.Translations.VisualStudio.dll'], 'Do not redistribute VS-owned assemblies'
    assert archive.read(dlls[0])[:2] == b'MZ', 'Missing managed extension'
    assert 'rmf2' in json.loads(archive.read('Grammars/rmf2.tmLanguage.json'))['fileTypes']
output = root / 'artifacts/runic-translations-visualstudio.vsix'
output.parent.mkdir(exist_ok=True)
if source.resolve() != output.resolve():
    shutil.copyfile(source, output)
print(f'PASS native VSIX metadata, assets, grammar and archive integrity: {output}')
