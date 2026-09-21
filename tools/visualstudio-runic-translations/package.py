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
parser.add_argument('--check-source', action='store_true', help='Validate the source manifest and support range without a native VSIX')
args = parser.parse_args()
root = Path(__file__).resolve().parent
ns = {'v': 'http://schemas.microsoft.com/developer/vsx-schema/2011'}
source_manifest = root / 'source.extension.vsixmanifest'

def manifest_contract(manifest):
    identity = manifest.find('v:Metadata/v:Identity', ns)
    target = manifest.find('v:Installation/v:InstallationTarget', ns)
    prerequisite = manifest.find('v:Prerequisites/v:Prerequisite', ns)
    assert identity is not None, 'VSIX identity is missing'
    assert target is not None, 'VSIX installation target is missing'
    assert prerequisite is not None, 'VSIX core-editor prerequisite is missing'
    return {
        'identity': tuple(sorted(identity.attrib.items())),
        'license': manifest.findtext('v:Metadata/v:License', namespaces=ns),
        'target': (tuple(sorted(target.attrib.items())), target.findtext('v:ProductArchitecture', namespaces=ns)),
        'prerequisite': tuple(sorted(prerequisite.attrib.items())),
    }

source_contract = manifest_contract(ET.parse(source_manifest).getroot())

if args.check_source:
    identity = dict(source_contract['identity'])
    target = dict(source_contract['target'][0])
    prerequisite = dict(source_contract['prerequisite'])
    assert identity == {'Id': 'Runic.Artifex.Translations.Rmf2', 'Language': 'en-US', 'Publisher': 'Runic Artifex', 'Version': '0.0.1'}, 'Unexpected source VSIX identity'
    assert target == {'Id': 'Microsoft.VisualStudio.Community', 'Version': '[17.14,19.0)'}, 'Manifest must declare the 17.14 API floor through the 18.x host line'
    assert prerequisite['Id'] == 'Microsoft.VisualStudio.Component.CoreEditor' and prerequisite['Version'] == '[17.14,19.0)', 'Core editor prerequisite must match the declared installation range'
    assert source_contract['target'][1] == 'amd64', 'VSIX must declare the amd64 product architecture'
    assert source_contract['license'] == 'LICENSE.txt', 'VSIX must carry the packaged license name'
    print('PASS source VSIX manifest, host range and prerequisite contract')
    raise SystemExit(0)

source = args.input or root / 'bin' / args.configuration / 'net472/Runic.Translations.VisualStudio.vsix'
if not source.is_file():
    parser.error('Build with Visual Studio MSBuild on Windows first, or supply --input with that VSIX.')
with zipfile.ZipFile(source) as archive:
    assert archive.testzip() is None, 'Corrupt VSIX'
    names = set(archive.namelist())
    for name in ('manifest.json', 'catalog.json', '[Content_Types].xml', 'LICENSE.txt', 'Runic.Translations.VisualStudio.pkgdef'):
        assert name in names, f'Missing native installer metadata: {name}'
    manifest = ET.fromstring(archive.read('extension.vsixmanifest'))
    assert manifest_contract(manifest) == source_contract, 'Embedded VSIX manifest identity/license/host contract differs from source.extension.vsixmanifest'
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
