#!/usr/bin/env python3
"""Build a self-contained native settings app using the installed Xcode tools."""
import argparse
import pathlib
import plistlib
import subprocess
import tempfile
import xml.etree.ElementTree as ET

root = pathlib.Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser()
parser.add_argument('--output', type=pathlib.Path, default=root / 'dist' / 'PthTouch Settings.app')
args = parser.parse_args()
app = args.output.resolve()
contents = app / 'Contents'
macos = contents / 'MacOS'
macos.mkdir(parents=True, exist_ok=True)
settings_version = root / 'settings' / 'VERSION'
version = settings_version.read_text().strip() if settings_version.exists() else ET.parse(root / 'plugin' / 'PthTouch.csproj').find('.//Version').text
sources = [root / 'settings' / name for name in ['DaemonRPC.swift', 'SettingsCore.swift', 'PthTouchSettings.swift']]
sdk = subprocess.check_output(['xcrun', '--sdk', 'macosx', '--show-sdk-path'], text=True).strip()
with tempfile.TemporaryDirectory(prefix='pth-settings-') as temporary:
    scratch = pathlib.Path(temporary)
    binaries = []
    for arch in ['arm64', 'x86_64']:
        binary = scratch / arch
        subprocess.run(['xcrun', 'swiftc', '-parse-as-library', '-swift-version', '5', '-O',
            '-target', f'{arch}-apple-macos13.0', '-sdk', sdk,
            '-module-cache-path', str(scratch / 'module-cache'),
            '-debug-prefix-map', f'{root}=/_/PthTouch',
            '-framework', 'AppKit', '-framework', 'SwiftUI',
            *map(str, sources), '-o', str(binary)], check=True)
        binaries.append(binary)
    subprocess.run(['xcrun', 'lipo', '-create', *map(str, binaries), '-output', str(macos / 'PthTouchSettings')], check=True)
with (contents / 'Info.plist').open('wb') as stream:
    plistlib.dump({'CFBundleIdentifier': 'dev.matebenyovszky.PthTouchSettings',
        'CFBundleExecutable': 'PthTouchSettings', 'CFBundleName': 'PthTouch Settings',
        'CFBundlePackageType': 'APPL', 'CFBundleShortVersionString': version,
        'CFBundleVersion': version, 'LSMinimumSystemVersion': '13.0',
        'NSHighResolutionCapable': True}, stream)
subprocess.run(['codesign', '--force', '--sign', '-', '--timestamp=none', str(app)], check=True)
subprocess.run(['codesign', '--verify', '--strict', str(app)], check=True)
print(app)
