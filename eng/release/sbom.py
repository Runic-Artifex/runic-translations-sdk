#!/usr/bin/env python3
"""Write a CycloneDX 1.6 SBOM describing the files of one release.

sbom.py --repository OWNER/NAME --version VERSION --source SHA --epoch SECONDS --output FILE
        [--artifact-version FILE_NAME=VERSION]... ARTIFACT...

Reads archive metadata with the standard library only: nothing is extracted or
executed and nothing is installed, so it runs in the read-only candidate job. The
output is deterministic: the same artifacts, version, source and commit time
(--epoch, from `git show -s --format=%ct`) always give the same bytes, so a rerun
describes a published release identically.

Every artifact must declare the release --version, except an artifact named by
--artifact-version, which must declare the version given there (for example a
VSIX, whose format cannot express a SemVer prerelease, with its mapped version).

Every artifact (.nupkg, npm .tgz, .vsix) is a component with its SHA-256. Its
dependencies are
- declared: nuspec dependency groups and package.json dependencies,
  optionalDependencies and peerDependencies, as external components with the
  requested range (consumers resolve them);
- bundled: libraries listed in the *.deps.json files a package ships (exact
  version and NuGet SHA-512), and the dependencies a VS Code extension bundles.
A declared dependency on another artifact of this release links to that artifact.
"""
import argparse
import base64
import hashlib
import json
import re
import tarfile
import uuid
import zipfile
import xml.etree.ElementTree as ElementTree
from datetime import datetime, timezone
from pathlib import Path
from urllib.parse import quote

MAX_ENTRY = 16 * 1024 * 1024  # largest metadata entry read from an archive
TOOL = 'runic-release-sbom'
TOOL_VERSION = '1'
EXACT = re.compile(r'^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?(\+[0-9A-Za-z.-]+)?$')


def zip_entry(archive, name):
    """One archive entry, refused before reading when it declares more than MAX_ENTRY bytes."""
    info = archive.getinfo(name)
    if info.file_size > MAX_ENTRY:
        raise ValueError(f'{name} is larger than {MAX_ENTRY} bytes')
    with archive.open(info) as entry:
        data = entry.read(MAX_ENTRY + 1)
    if len(data) > MAX_ENTRY:
        raise ValueError(f'{name} is larger than {MAX_ENTRY} bytes')
    return data


def tar_entry(archive, name):
    member = archive.getmember(name)
    if not member.isfile():
        raise ValueError(f'{name} must be a regular file')
    if member.size > MAX_ENTRY:
        raise ValueError(f'{name} is larger than {MAX_ENTRY} bytes')
    return archive.extractfile(member).read(MAX_ENTRY + 1)


def xml(data, name):
    """Parse package XML; a document type declaration (entities, external references) is refused."""
    if b'<!DOCTYPE' in data or b'<!ENTITY' in data:
        raise ValueError(f'{name} must not declare a document type or entities')
    return ElementTree.fromstring(data)


def local(tag):
    return tag.rsplit('}', 1)[-1]


def child(element, name):
    return next((item for item in element if local(item.tag) == name), None) if element is not None else None


def descendants(element, name):
    return [item for item in element.iter() if local(item.tag) == name]


def purl(kind, name, version=None):
    namespace, _, base = name.rpartition('/') if kind == 'npm' else ('', '', name)
    path = (quote(namespace, safe='') + '/' if namespace else '') + quote(base, safe='')
    return f'pkg:{kind}/{path}' + (f'@{quote(version, safe="+")}' if version else '')


def npm_name(name):
    scope, _, base = name.rpartition('/')
    return ({'group': scope} if scope else {}) | {'name': base}


def license_of(value):
    return [{'expression': value}] if isinstance(value, str) and value.strip() else []


def property_list(values):
    return sorted(({'name': name, 'value': value} for name, value in values.items() if value), key=lambda item: item['name'])


class Graph:
    def __init__(self):
        self.components, self.edges = {}, {}

    def add(self, component):
        ref = component['bom-ref']
        self.components.setdefault(ref, component)
        self.edges.setdefault(ref, set())
        return ref

    def depend(self, ref, target):
        if ref != target:
            self.edges.setdefault(ref, set()).add(target)


def declared(graph, owner, kind, name, requested, shipped):
    """Link owner to a declared dependency: the shipped artifact it pins, or an external range.

    A dependency without a range (a nuspec <dependency> without version) has no range property.
    """
    pinned = requested[1:-1] if requested.startswith('[') and requested.endswith(']') else requested
    if requested and purl(kind, name, pinned) in shipped:
        return graph.depend(owner, purl(kind, name, pinned))
    identity = npm_name(name) if kind == 'npm' else {'name': name}
    graph.depend(owner, graph.add({'type': 'library', 'bom-ref': f'{purl(kind, name)}#{requested}' if requested else purl(kind, name), **identity,
                                   'purl': purl(kind, name),
                                   **({'properties': property_list({'runic:requested-range': requested})} if requested else {})}))


def bundled_npm(graph, owner, name, version):
    identity = npm_name(name)
    graph.depend(owner, graph.add({'type': 'library', 'bom-ref': purl('npm', name, version), **identity, 'version': version,
                                   'purl': purl('npm', name, version)}))


def deps_json(graph, owner, document, shipped):
    """Bundled .NET libraries of one *.deps.json, with the dependency edges between them."""
    refs = {}
    for key, library in sorted(document.get('libraries', {}).items()):
        name, _, version = key.rpartition('/')
        kind = library.get('type')
        if kind == 'package':
            component = {'type': 'library', 'bom-ref': purl('nuget', name, version), 'name': name, 'version': version,
                         'purl': purl('nuget', name, version)}
            digest = library.get('sha512', '')
            if digest.startswith('sha512-'):
                component['hashes'] = [{'alg': 'SHA-512', 'content': base64.b64decode(digest[7:]).hex()}]
        elif kind == 'project':
            # A project built into this package; it links to the artifact when it is one.
            shipped_ref = purl('nuget', name, version)
            if shipped_ref in shipped:
                refs[key] = shipped_ref
                continue
            component = {'type': 'library', 'bom-ref': f'project:{name}@{version}', 'name': name, 'version': version,
                         'properties': property_list({'runic:deps-type': 'project'})}
        else:
            continue  # framework references and runtime packs come from the installed runtime
        refs[key] = graph.add(component)
    for ref in refs.values():
        graph.depend(owner, ref)
    target = document.get('targets', {}).get(document.get('runtimeTarget', {}).get('name', ''), {})
    for key, entry in target.items():
        for name, version in (entry.get('dependencies') or {}).items():
            if key in refs and f'{name}/{version}' in refs:
                graph.depend(refs[key], refs[f'{name}/{version}'])


def nuget(path):
    with zipfile.ZipFile(path) as archive:
        specs = [name for name in archive.namelist() if name.endswith('.nuspec') and '/' not in name]
        if len(specs) != 1:
            raise ValueError(f'{path.name} must contain one root .nuspec')
        metadata = child(xml(zip_entry(archive, specs[0]), specs[0]), 'metadata')
        text = lambda name: (getattr(child(metadata, name), 'text', None) or '').strip()
        license_element = child(metadata, 'license')
        repository = child(metadata, 'repository')
        tool = any(item.get('name') == 'DotnetTool' for item in descendants(metadata, 'packageType'))
        name, version = text('id'), text('version')
        component = {'type': 'application' if tool else 'library', 'bom-ref': purl('nuget', name, version), 'name': name,
                     'version': version, 'purl': purl('nuget', name, version),
                     'licenses': license_of(license_element.text if license_element is not None and license_element.get('type') == 'expression' else None),
                     'externalReferences': [{'type': 'distribution', 'url': f'https://www.nuget.org/packages/{name}/{version}'}],
                     'properties': {'runic:source-commit': repository.get('commit') if repository is not None else None}}
        requested = sorted({(item.get('id'), (item.get('version') or '').strip()) for item in descendants(metadata, 'dependency')})
        documents = [json.loads(zip_entry(archive, entry).decode('utf-8-sig')) for entry in sorted(archive.namelist()) if entry.endswith('.deps.json')]
    return component, [('nuget', *item) for item in requested], lambda graph, ref, shipped: [deps_json(graph, ref, document, shipped) for document in documents]


def npm(path):
    with tarfile.open(path, 'r:gz') as archive:
        manifest = json.loads(tar_entry(archive, 'package/package.json'))
    name, version = manifest['name'], manifest['version']
    component = {'type': 'library', 'bom-ref': purl('npm', name, version), **npm_name(name), 'version': version,
                 'purl': purl('npm', name, version), 'licenses': license_of(manifest.get('license')),
                 'externalReferences': [{'type': 'distribution', 'url': f'https://www.npmjs.com/package/{name}/v/{version}'}],
                 'properties': {'runic:source-commit': manifest.get('gitHead')}}
    requested = sorted({('npm', key, value) for field in ('dependencies', 'optionalDependencies', 'peerDependencies')
                        for key, value in (manifest.get(field) or {}).items()})
    return component, requested, None


def vsix(path):
    with zipfile.ZipFile(path) as archive:
        identity = child(child(xml(zip_entry(archive, 'extension.vsixmanifest'), 'extension.vsixmanifest'), 'Metadata'), 'Identity')
        manifest = json.loads(zip_entry(archive, 'extension/package.json')) if 'extension/package.json' in archive.namelist() else {}
    name, version, publisher = identity.get('Id'), identity.get('Version'), identity.get('Publisher')
    component = {'type': 'application', 'bom-ref': f'vsix:{publisher}/{name}@{version}', 'name': name, 'version': version,
                 'publisher': publisher, 'licenses': license_of(manifest.get('license')), 'properties': {}}
    # A VS Code extension packed with --no-dependencies bundles its runtime dependencies.
    dependencies = sorted((manifest.get('dependencies') or {}).items())

    def bundle(graph, ref, shipped):
        for key, value in dependencies:
            if EXACT.match(value):
                bundled_npm(graph, ref, key, value)
            else:
                declared(graph, ref, 'npm', key, value, shipped)
    return component, [], bundle


READERS = {'.nupkg': nuget, '.tgz': npm, '.vsix': vsix}


def build(repository, version, source, epoch, paths, artifact_versions=None):
    artifact_versions = artifact_versions or {}
    owner, _, project = repository.partition('/')
    if not owner or not project or not re.fullmatch(r'[0-9a-f]{40}', source):
        raise ValueError('Expected OWNER/NAME and a full source commit')
    if len({path.name for path in paths}) != len(paths) or not paths:
        raise ValueError('Expected uniquely named artifacts')
    if set(artifact_versions) - {path.name for path in paths}:
        raise ValueError(f'--artifact-version names no artifact: {", ".join(sorted(set(artifact_versions) - {path.name for path in paths}))}')
    graph, artifacts = Graph(), []
    for path in sorted(paths, key=lambda item: item.name):
        reader = READERS.get(path.suffix)
        if reader is None:
            raise ValueError(f'Unsupported artifact {path.name}')
        data = path.read_bytes()
        component, requested, extra = reader(path)
        expected = artifact_versions.get(path.name, version)
        if component['version'] != expected:
            raise ValueError(f"{path.name} is version {component['version']}, not {expected}")
        component['hashes'] = [{'alg': 'SHA-256', 'content': hashlib.sha256(data).hexdigest()}]
        component['externalReferences'] = [*component.get('externalReferences', []), {'type': 'vcs', 'url': f'https://github.com/{repository}'}]
        component['properties'] = property_list({**component['properties'], 'runic:file': path.name})
        if not component.get('licenses'):
            component.pop('licenses', None)
        if component['bom-ref'] in graph.components:
            raise ValueError(f"{path.name} repeats {component['bom-ref']}")
        artifacts.append((graph.add(component), requested, extra))
    shipped = {ref for ref, _, _ in artifacts}
    for ref, requested, extra in artifacts:
        for kind, name, value in requested:
            declared(graph, ref, kind, name, value, shipped)
        if extra:
            extra(graph, ref, shipped)
    release = {'type': 'library', 'bom-ref': 'release', 'name': project, 'version': version,
               'purl': f'pkg:github/{owner.lower()}/{project.lower()}@v{quote(version, safe="+")}',
               'externalReferences': [{'type': 'vcs', 'url': f'https://github.com/{repository}'},
                                      {'type': 'distribution', 'url': f'https://github.com/{repository}/releases/tag/v{version}'}],
               'properties': property_list({'runic:source-commit': source})}
    identity = hashlib.sha256(json.dumps([repository, version, source, epoch, sorted(graph.components)]).encode()).digest()
    return {
        '$schema': 'http://cyclonedx.org/schema/bom-1.6.schema.json',
        'bomFormat': 'CycloneDX',
        'specVersion': '1.6',
        'serialNumber': f'urn:uuid:{uuid.UUID(bytes=identity[:16], version=5)}',
        'version': 1,
        'metadata': {
            'timestamp': datetime.fromtimestamp(epoch, timezone.utc).strftime('%Y-%m-%dT%H:%M:%SZ'),
            'lifecycles': [{'phase': 'build'}],
            'tools': {'components': [{'type': 'application', 'name': TOOL, 'version': TOOL_VERSION,
                                      'externalReferences': [{'type': 'vcs', 'url': f'https://github.com/{repository}/blob/{source}/eng/release/sbom.py'}]}]},
            'component': release,
        },
        'components': [graph.components[ref] for ref in sorted(graph.components)],
        'dependencies': [{'ref': 'release', 'dependsOn': sorted(shipped)}]
        + [{'ref': ref, 'dependsOn': sorted(graph.edges[ref])} for ref in sorted(graph.edges)],
    }


def main(argv=None):
    parser = argparse.ArgumentParser(description='Write a CycloneDX 1.6 SBOM for release artifacts.')
    parser.add_argument('--repository', required=True)
    parser.add_argument('--version', required=True)
    parser.add_argument('--source', required=True)
    parser.add_argument('--epoch', required=True, type=int, help='Commit time in seconds since the epoch')
    parser.add_argument('--output', required=True, type=Path)
    parser.add_argument('--artifact-version', action='append', default=[], metavar='FILE_NAME=VERSION',
                        help='Version the named artifact must declare instead of --version')
    parser.add_argument('artifacts', nargs='+', type=Path)
    args = parser.parse_args(argv)
    artifact_versions = {}
    for item in args.artifact_version:
        name, separator, value = item.partition('=')
        if not separator or not name or not value or name in artifact_versions:
            parser.error(f'--artifact-version expects one FILE_NAME=VERSION per artifact: {item}')
        artifact_versions[name] = value
    bom = build(args.repository, args.version, args.source, args.epoch, args.artifacts, artifact_versions)
    args.output.write_text(json.dumps(bom, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
    print(f"Described {len(bom['dependencies'][0]['dependsOn'])} artifacts and {len(bom['components'])} components in {args.output}")


if __name__ == '__main__':
    main()
