"""Verify and copy a VSIX produced by Microsoft's Windows VSSDK build targets."""
from pathlib import Path
import argparse
import json
import re
import shutil
import zipfile
import xml.etree.ElementTree as ET

parser = argparse.ArgumentParser()
parser.add_argument('--configuration', default='Debug', choices=['Debug', 'Release'])
parser.add_argument('--input', type=Path, help='Native-built VSIX, optionally copied from Windows')
parser.add_argument('--check-source', action='store_true', help='Validate the source manifest and support range without a native VSIX')
parser.add_argument('--release-version', help='Release version to expect (default: eng/workspace.json)')
parser.add_argument('--print-version', action='store_true', help='Print the VSIX version of the release version and exit')
args = parser.parse_args()
root = Path(__file__).resolve().parent
ns = {'v': 'http://schemas.microsoft.com/developer/vsx-schema/2011'}
source_manifest = root / 'source.extension.vsixmanifest'
# The build stamps the Identity version through this VSSDK token (GetRunicVsixVersion in the project).
VERSION_TOKEN = '|%CurrentProject%;GetRunicVsixVersion|'


def vsix_version(release):
    """0.6.0-preview.N is 0.6.0.N and the final 0.6.0 is 0.6.0.1000, as in eng/release/ide-versions.mjs."""
    match = re.fullmatch(r'(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-preview\.([1-9]\d{0,2}))?', release)
    assert match, f'Release version {release} cannot be mapped to a VSIX version; use x.y.z or x.y.z-preview.N (1 <= N <= 999)'
    # Visual Studio parses each component as a 16-bit System.Version part.
    assert all(int(part) <= 65534 for part in match.group(1, 2, 3)), f'Release version {release} has a component above 65534'
    return '.'.join(match.group(1, 2, 3)) + '.' + (match.group(4) or '1000')


release_version = args.release_version or json.loads((root / '../../eng/workspace.json').read_text(encoding='utf-8'))['version']
expected_version = vsix_version(release_version)
if args.print_version:
    print(expected_version)
    raise SystemExit(0)

def manifest_contract(manifest):
    identity = manifest.find('v:Metadata/v:Identity', ns)
    target = manifest.find('v:Installation/v:InstallationTarget', ns)
    prerequisite = manifest.find('v:Prerequisites/v:Prerequisite', ns)
    assert identity is not None, 'VSIX identity is missing'
    assert target is not None, 'VSIX installation target is missing'
    assert prerequisite is not None, 'VSIX core-editor prerequisite is missing'
    assets = tuple(sorted(
        tuple(sorted((name, value.replace('\\', '/') if name == 'Path' else value) for name, value in asset.attrib.items()))
        for asset in manifest.findall('v:Assets/v:Asset', ns)
    ))
    return {
        'identity': tuple(sorted((name, value) for name, value in identity.attrib.items() if name != 'Version')),
        'version': identity.get('Version'),
        'license': manifest.findtext('v:Metadata/v:License', namespaces=ns),
        'target': (tuple(sorted(target.attrib.items())), target.findtext('v:ProductArchitecture', namespaces=ns)),
        'prerequisite': tuple(sorted(prerequisite.attrib.items())),
        'assets': assets,
    }

source_contract = manifest_contract(ET.parse(source_manifest).getroot())
assert source_contract['version'] == VERSION_TOKEN, f'The source manifest must take its version from the build: Version="{VERSION_TOKEN}"'

if args.check_source:
    identity = dict(source_contract['identity'])
    target = dict(source_contract['target'][0])
    prerequisite = dict(source_contract['prerequisite'])
    assert identity == {'Id': 'Runic.Artifex.Translations.Rmf2', 'Language': 'en-US', 'Publisher': 'Runic Artifex'}, 'Unexpected source VSIX identity'
    assert target == {'Id': 'Microsoft.VisualStudio.Community', 'Version': '[17.14,19.0)'}, 'Manifest must declare the 17.14 API floor through the 18.x host line'
    assert prerequisite['Id'] == 'Microsoft.VisualStudio.Component.CoreEditor' and prerequisite['Version'] == '[17.14,19.0)', 'Core editor prerequisite must match the declared installation range'
    assert source_contract['target'][1] == 'amd64', 'VSIX must declare the amd64 product architecture'
    assert source_contract['license'] == 'LICENSE.txt', 'VSIX must carry the packaged license name'
    assert {(dict(asset)['Type'], dict(asset)['Path']) for asset in source_contract['assets']} == {
        ('Microsoft.VisualStudio.MefComponent', 'Runic.Translations.VisualStudio.dll'),
        ('Microsoft.VisualStudio.VsPackage', 'Runic.pkgdef'),
        ('Microsoft.VisualStudio.VsPackage', 'Runic.Translations.VisualStudio.pkgdef'),
    }, 'Unexpected source VSIX asset contract'
    print(f'PASS source VSIX manifest, host range and prerequisite contract; {release_version} stamps version {expected_version}')
    raise SystemExit(0)

source = args.input or root / 'bin' / args.configuration / 'net472/Runic.Translations.VisualStudio.vsix'
if not source.is_file():
    parser.error('Build with Visual Studio MSBuild on Windows first, or supply --input with that VSIX.')
with zipfile.ZipFile(source) as archive:
    assert archive.testzip() is None, 'Corrupt VSIX'
    names = set(archive.namelist())
    for name in ('manifest.json', 'catalog.json', '[Content_Types].xml', 'LICENSE.txt', 'Runic.pkgdef', 'Runic.Translations.VisualStudio.pkgdef'):
        assert name in names, f'Missing native installer metadata: {name}'
    manifest = ET.fromstring(archive.read('extension.vsixmanifest'))
    embedded = manifest_contract(manifest)
    assert embedded['version'] == expected_version, f"VSIX version {embedded['version']} is not {expected_version}, the stamped version of {release_version}"
    assert embedded | {'version': VERSION_TOKEN} == source_contract, 'Embedded VSIX manifest identity/license/host contract differs from source.extension.vsixmanifest'
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
print(f'PASS native VSIX {expected_version} (release {release_version}) metadata, assets, grammar and archive integrity: {output}')
