"""Build the mod into dist/ModePuzzle/ with the Roslyn compiler that ships with a Unity editor.

    python build.py              compile and stage dist/ModePuzzle/
    python build.py --install    also copy it into the game's Mods folder (run this yourself)

The game's assemblies are only referenced at compile time, from the local install; they are never
copied into the repo or the build output.

Paths can be overridden with the environment variables UNITY_EDITOR_DATA (the editor's Data folder)
and PROBABLY_STOLEN_DIR (the game folder that holds Probably Stolen.exe).
"""
import argparse
import json
import os
import re
import shutil
import subprocess
import sys

ROOT = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(ROOT, 'dist', 'ModePuzzle')
DLL = 'ModePuzzle.dll'
GAME_EXE = 'Probably Stolen.exe'
REFERENCES = [
    'mscorlib.dll', 'System.dll', 'System.Core.dll', 'netstandard.dll',
    'UnityEngine.dll', 'UnityEngine.CoreModule.dll', 'UnityEngine.TextRenderingModule.dll', 'UnityEngine.UI.dll',
    'Unity.TextMeshPro.dll',
    'Assembly-CSharp.dll', '0Harmony.dll',
]
# Locale files the game may ask for; the first one is the source copied to the others.
LOCALE_ALIASES = {'zh-Hans.csv': ['zh.csv']}


def unity_data_dir():
    env = os.environ.get('UNITY_EDITOR_DATA')
    if env:
        return env
    hub = os.path.join(os.environ.get('APPDATA', ''), 'UnityHub', 'editors-v2.json')
    if os.path.exists(hub):
        with open(hub, encoding='utf-8') as f:
            editors = json.load(f).get('data', [])
        for editor in sorted(editors, key=lambda e: e.get('version', ''), reverse=True):
            for exe in editor.get('location', []):
                data = os.path.join(os.path.dirname(exe), 'Data')
                if os.path.exists(os.path.join(data, 'DotNetSdkRoslyn', 'csc.dll')):
                    return data
    sys.exit('Unity editor not found: set UNITY_EDITOR_DATA to the editor\'s Data folder.')


def steam_root():
    try:
        import winreg
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, r'Software\Valve\Steam') as key:
            return winreg.QueryValueEx(key, 'SteamPath')[0]
    except OSError:
        return None


def steam_libraries():
    roots = [r for r in (steam_root(), r'C:\Program Files (x86)\Steam', r'C:\Program Files\Steam') if r]
    libs = []
    for root in roots:
        vdf = os.path.join(root, 'steamapps', 'libraryfolders.vdf')
        if os.path.exists(vdf):
            libs.append(root)
            with open(vdf, encoding='utf-8', errors='replace') as f:
                libs += [p.replace('\\\\', '\\') for p in re.findall(r'"path"\s+"([^"]+)"', f.read())]
    return libs


def game_dir():
    env = os.environ.get('PROBABLY_STOLEN_DIR')
    candidates = [env] if env else [os.path.join(lib, 'steamapps', 'common', 'Probably Stolen') for lib in steam_libraries()]
    for path in candidates:
        if path and os.path.exists(os.path.join(path, GAME_EXE)):
            return path
    sys.exit('Game not found: set PROBABLY_STOLEN_DIR to the folder that holds Probably Stolen.exe.')


def compile_dll(data, managed):
    dotnet = os.path.join(data, 'NetCoreRuntime', 'dotnet.exe')
    csc = os.path.join(data, 'DotNetSdkRoslyn', 'csc.dll')
    sources = sorted(os.path.join(d, f) for d, _, files in os.walk(os.path.join(ROOT, 'src')) for f in files if f.endswith('.cs'))
    refs = [f'-r:{os.path.join(managed, r)}' for r in REFERENCES]
    cmd = [dotnet, csc, '-nologo', '-utf8output', '-noconfig', '-nostdlib', '-target:library', '-optimize+', '-deterministic',
           '-langversion:9.0', '-warnaserror+', f'-out:{os.path.join(OUT, DLL)}', *refs, *sources]
    result = subprocess.run(cmd, capture_output=True, text=True, encoding='utf-8', errors='replace')
    out = result.stdout + result.stderr
    sys.stdout.write(out.encode(sys.stdout.encoding or 'utf-8', errors='replace').decode(sys.stdout.encoding or 'utf-8'))
    if result.returncode != 0:
        sys.exit('Compilation failed.')


def stage():
    shutil.copy2(os.path.join(ROOT, 'manifest.xml'), OUT)
    loc_src = os.path.join(ROOT, 'Localization')
    loc_out = os.path.join(OUT, 'Localization')
    os.makedirs(loc_out, exist_ok=True)
    for name in os.listdir(loc_src):
        if name.endswith('.csv'):
            shutil.copy2(os.path.join(loc_src, name), loc_out)
            for alias in LOCALE_ALIASES.get(name, []):
                shutil.copy2(os.path.join(loc_src, name), os.path.join(loc_out, alias))


def game_running():
    try:
        out = subprocess.run(['tasklist', '/FI', f'IMAGENAME eq {GAME_EXE}', '/NH'], capture_output=True, text=True,
                             encoding='utf-8', errors='replace').stdout
    except OSError:
        return False
    return GAME_EXE.lower() in out.lower()


def install(game):
    if game_running():
        sys.exit('The game is running: close it, then install again (its loaded mod files must not change underneath it).')
    target = os.path.join(game, 'Mods', 'ModePuzzle')
    if os.path.exists(target):
        shutil.rmtree(target)
    shutil.copytree(OUT, target)
    print(f'Installed to {target}')


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--install', action='store_true', help="copy the build into the game's Mods folder")
    args = parser.parse_args()
    data, game = unity_data_dir(), game_dir()
    if os.path.exists(OUT):
        shutil.rmtree(OUT)
    os.makedirs(OUT)
    compile_dll(data, os.path.join(game, 'Probably Stolen_Data', 'Managed'))
    stage()
    print(f'Built {OUT}')
    if args.install:
        install(game)


if __name__ == '__main__':
    main()
