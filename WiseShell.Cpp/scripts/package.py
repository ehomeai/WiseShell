"""Bundle a Release build on its host OS; no installer, signing, or publishing."""
import argparse
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys


def run(*args):
    return subprocess.check_output([str(x) for x in args], text=True).strip()


parser = argparse.ArgumentParser()
parser.add_argument('--build', required=True, type=Path)
parser.add_argument('--qt-bin', required=True, type=Path)
parser.add_argument('--output', required=True, type=Path)
parser.add_argument('--license-file', type=Path,
                    help='Project license file (defaults to the repository LICENSE)')
parser.add_argument('--runtime-dir', action='append', default=[], type=Path,
                    help='Windows OpenSSL/zlib DLL directory')
args = parser.parse_args()
build = args.build.resolve()
source = Path(__file__).resolve().parents[1]
system = {'win32': 'windows', 'darwin': 'macos'}.get(sys.platform, 'linux')
root = args.output.resolve() / ('WiseShellCpp-' + system)
if root.exists():
    raise SystemExit(f'Output already exists; choose a fresh output directory: {root}')
root.mkdir(parents=True)
cache = (build / 'CMakeCache.txt').read_text(encoding='utf-8')
licenses = root / 'licenses'
licenses.mkdir()
shutil.copytree(source / 'licenses', licenses, dirs_exist_ok=True)
shutil.copy2(source / 'THIRD_PARTY_NOTICES.md', licenses)
shutil.copy2(args.license_file or source.parent / 'LICENSE', licenses / 'WiseShell-MIT.txt')
for dependency in ('libssh', 'qtkeychain', 'libvterm'):
    override = re.search(rf'^FETCHCONTENT_SOURCE_DIR_{dependency.upper()}:[^=]+=(.+)$', cache, re.M)
    dep = Path(override[1]) if override else build / '_deps' / (dependency + '-src')
    destination = licenses / dependency
    destination.mkdir()
    for path in dep.iterdir():
        if path.is_file() and path.name.upper().startswith(('LICENSE', 'COPYING')):
            shutil.copy2(path, destination)
qt_data = Path(run(args.qt_bin / ('qmake.exe' if system == 'windows' else 'qmake'), '-query', 'QT_INSTALL_PREFIX'))
if (qt_data / 'sbom').exists():
    shutil.copytree(qt_data / 'sbom', licenses / 'Qt-SBOM', dirs_exist_ok=True)
for candidate in (qt_data / 'licenses', qt_data / 'share' / 'licenses'):
    if candidate.is_dir():
        shutil.copytree(candidate, licenses / 'Qt', dirs_exist_ok=True)
        break
# SDKs without bundled license texts still get the source's distribution notices.
shutil.copy2(source / 'README.md', root)
shutil.copytree(source / 'docs', root / 'docs')

if system == 'windows':
    shutil.copy2(build / 'bin' / 'WiseShellCpp.exe', root)
    for name in ('ssh.dll', 'wiseshell-keychain.dll'):
        matches = list(build.rglob(name))
        if not matches:
            raise SystemExit(f'Missing runtime: {name}')
        shutil.copy2(matches[0], root)
    wanted = ('libcrypto-3-x64.dll', 'libssl-3-x64.dll', 'zlib1.dll')
    for name in wanted:
        for directory in args.runtime_dir:
            if (directory / name).exists():
                shutil.copy2(directory / name, root)
                break
        else:
            raise SystemExit(f'Missing {name}; specify --runtime-dir')
    run(args.qt_bin / 'windeployqt.exe', '--release', '--no-compiler-runtime', '--no-translations', root / 'WiseShellCpp.exe')
    # Also support automated headless smoke tests of the shipped package.
    shutil.copy2(qt_data / 'plugins/platforms/qoffscreen.dll', root / 'platforms/qoffscreen.dll')
    vswhere = Path(os.environ.get('ProgramFiles(x86)', r'C:\Program Files (x86)')) / 'Microsoft Visual Studio/Installer/vswhere.exe'
    installation = Path(run(vswhere, '-latest', '-products', '*', '-requires',
                            'Microsoft.VisualStudio.Component.VC.Tools.x86.x64', '-property', 'installationPath'))
    crt_directories = sorted((installation / 'VC/Redist/MSVC').glob('*/x64/Microsoft.VC*.CRT'))
    if not crt_directories:
        raise SystemExit('MSVC redistributable DLL directory not found')
    for dll in crt_directories[-1].glob('*.dll'):
        shutil.copy2(dll, root)
    shutil.make_archive(str(root), 'zip', root.parent, root.name)
elif system == 'macos':
    app = root / 'WiseShellCpp.app'
    shutil.copytree(build / 'bin' / 'WiseShellCpp.app', app, symlinks=True)
    run(args.qt_bin / 'macdeployqt', app, '-always-overwrite', '-no-strip')
    shutil.make_archive(str(root), 'zip', root.parent, root.name)
else:
    executable = root / 'WiseShellCpp'
    shutil.copy2(build / 'bin' / 'WiseShellCpp', executable)
    lib = root / 'lib'
    lib.mkdir()
    plugins = root / 'plugins'
    qt_plugins = Path(run(args.qt_bin / 'qmake', '-query', 'QT_INSTALL_PLUGINS'))
    for name in ('platforms', 'platforminputcontexts', 'imageformats', 'xcbglintegrations'):
        if (qt_plugins / name).exists():
            shutil.copytree(qt_plugins / name, plugins / name)
    # Bundle all resolved non-glibc libraries, including plugin dependencies.
    skip = re.compile(r'^(ld-linux|libc\.|libm\.|libpthread\.|libdl\.|librt\.|libresolv\.)')
    queue = [build / 'bin' / 'WiseShellCpp', *plugins.rglob('*.so')]
    copied = set()
    while queue:
        binary = queue.pop()
        output = run('ldd', binary)
        if 'not found' in output:
            raise SystemExit(f'Unresolved runtime dependency for {binary}:\n{output}')
        for filename in re.findall(r'=> (/[^\n]+?) \(', output):
            path = Path(filename)
            if skip.match(path.name) or path.name in copied:
                continue
            copied.add(path.name)
            shutil.copy2(path, lib / path.name)
            queue.append(path)
    launcher = root / 'AppRun'
    launcher.write_text('#!/bin/sh\nset -eu\nAPPDIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)\n'
                        'export LD_LIBRARY_PATH="$APPDIR/lib${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"\n'
                        'export QT_PLUGIN_PATH="$APPDIR/plugins"\nexec "$APPDIR/WiseShellCpp" "$@"\n')
    launcher.chmod(0o755)
    (root / 'qt.conf').write_text('[Paths]\nPlugins=plugins\nLibraries=lib\n')
    shutil.make_archive(str(root), 'gztar', root.parent, root.name)
print(root)
