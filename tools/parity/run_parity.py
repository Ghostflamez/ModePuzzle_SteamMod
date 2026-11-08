"""Engine parity check: the C# engine (src/Engine) against the external tool's JavaScript engine.

    python tools/parity/run_parity.py [--seed N] [--keep-cases] [--cases PATH] [--exact-only]

1. gen_cases.mjs (Node) runs the reference engine and writes build/parity_cases.json.
2. Parity.cs is compiled together with src/Engine/*.cs (no game or Unity code) and run under the Mono
   that ships with the Unity editor.
Needs Node and the ModePuzzle repository (MODEPUZZLE_DIR, default ../ModePuzzle).
"""
import argparse
import glob
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, ROOT)
import build  # noqa: E402

OUT = os.path.join(ROOT, 'build')


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--seed', default='20261108')
    parser.add_argument('--keep-cases', action='store_true', help='reuse the case JSON if it already exists')
    parser.add_argument('--cases', help='case JSON path (default: build/parity_cases.json)')
    parser.add_argument('--exact-only', action='store_true', help='skip heuristic discovery to exercise exact search')
    args = parser.parse_args()
    cases = os.path.abspath(args.cases) if args.cases else os.path.join(OUT, 'parity_cases.json')
    if not (args.keep_cases and os.path.exists(cases)):
        subprocess.run(['node', os.path.join(HERE, 'gen_cases.mjs'), cases, args.seed], check=True)

    data, game = build.unity_data_dir(), build.game_dir()
    managed = os.path.join(game, 'Probably Stolen_Data', 'Managed')
    exe = os.path.join(OUT, 'Parity.exe')
    sources = [os.path.join(HERE, 'Parity.cs')] + sorted(glob.glob(os.path.join(ROOT, 'src', 'Engine', '*.cs')))
    subprocess.run([os.path.join(data, 'NetCoreRuntime', 'dotnet.exe'), os.path.join(data, 'DotNetSdkRoslyn', 'csc.dll'),
                    '-nologo', '-noconfig', '-nostdlib', '-optimize+', '-langversion:9.0', '-utf8output', f'-out:{exe}',
                    *[f'-r:{os.path.join(managed, r)}' for r in ('mscorlib.dll', 'System.dll', 'System.Core.dll', 'netstandard.dll')],
                    *sources], check=True)
    mono = os.path.join(data, 'MonoBleedingEdge', 'bin', 'mono.exe')
    extra = ['--exact-only'] if args.exact_only else []
    sys.exit(subprocess.run([mono, exe, cases, *extra]).returncode)


if __name__ == '__main__':
    main()
