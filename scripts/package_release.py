"""Build a Thunderstore zip from a compiled plugin and the project's runtime assets."""
import hashlib
import json
from pathlib import Path
import re
import zipfile

ROOT = Path(__file__).resolve().parents[1]
manifest = json.loads((ROOT / 'packaging/manifest.json').read_text(encoding='utf-8'))
version = re.search(r'Version = "([^"]+)"', (ROOT / 'src/BuckshotRoulette.Plugin/Core/ModInfo.cs').read_text()).group(1)
if version != manifest['version_number']:
    raise SystemExit('Manifest and plugin versions differ')
dll = ROOT / 'src/BuckshotRoulette.Plugin/bin/Release/netstandard2.1/BuckshotRoulette.dll'
if not dll.is_file():
    raise SystemExit('Build the plugin in Release configuration first')
files = {name: ROOT / 'packaging' / name for name in ['manifest.json', 'README.md', 'CHANGELOG.md', 'icon.png']}
files['plugins/BuckshotRoulette/BuckshotRoulette.dll'] = dll
for source, target, suffixes in [('assets/models', 'Models', {'.json', '.png'}), ('assets/audio', 'Audio', {'.wav', '.ogg'})]:
    for path in sorted((ROOT / source).iterdir()):
        if path.suffix in suffixes:
            files[f'plugins/BuckshotRoulette/{target}/{path.name}'] = path
if any(not path.is_file() for path in files.values()):
    raise SystemExit('Missing package file')
out = ROOT / 'releases' / f'Avariiiprime-{manifest["name"]}-{version}.zip'
out.parent.mkdir(exist_ok=True)
with zipfile.ZipFile(out, 'w', zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
    for name, path in files.items():
        archive.write(path, name)
with zipfile.ZipFile(out) as archive:
    if archive.testzip():
        raise SystemExit('Corrupt archive')
print(f'{out.name}: {len(files)} files, {out.stat().st_size} bytes, SHA256 {hashlib.sha256(out.read_bytes()).hexdigest()}')
