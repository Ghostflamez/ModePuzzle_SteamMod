"""Capability check: run the game's own ModScanner on dist/ModePuzzle (read-only).

    python tools/scan/scan_mod.py

Prints the capabilities the scanner detects; they must match <Capabilities> in manifest.xml.
"unlisted-reference" hard flags are expected here: the game passes its own assembly allow-list, this tool does not.
"""
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, ROOT)
import build  # noqa: E402


def main():
    data, game = build.unity_data_dir(), build.game_dir()
    managed = os.path.join(game, 'Probably Stolen_Data', 'Managed')
    out = os.path.join(ROOT, 'build')
    os.makedirs(out, exist_ok=True)
    exe = os.path.join(out, 'ScanMod.exe')
    subprocess.run([os.path.join(data, 'NetCoreRuntime', 'dotnet.exe'), os.path.join(data, 'DotNetSdkRoslyn', 'csc.dll'),
                    '-nologo', '-noconfig', '-nostdlib', '-langversion:9.0', '-utf8output', f'-out:{exe}',
                    *[f'-r:{os.path.join(managed, r)}' for r in ('mscorlib.dll', 'System.dll', 'System.Core.dll')],
                    os.path.join(HERE, 'ScanMod.cs')], check=True)
    dist = os.path.join(ROOT, 'dist', 'ModePuzzle')
    mono = os.path.join(data, 'MonoBleedingEdge', 'bin', 'mono.exe')
    sys.exit(subprocess.run([mono, exe, os.path.join(dist, build.DLL), dist]).returncode)


if __name__ == '__main__':
    main()
