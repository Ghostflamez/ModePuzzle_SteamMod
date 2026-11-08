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
    'UnityEngine.dll', 'UnityEngine.CoreModule.dll', 'UnityEngine.TextRenderingModule.dll', 'UnityEngine.UI.dll', 'UnityEngine.UIModule.dll', 'UnityEngine.ScreenCaptureModule.dll', 'UnityEngine.InputLegacyModule.dll',
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


PANEL_DIR = os.path.join(ROOT, 'src', 'Panel')
DEV_DIR = os.path.join(ROOT, 'build', 'dev')
PANEL_DLL = 'ModePuzzle.Panel.dll'
# Panel files that exist only for the development host.
DEV_ONLY_PANEL = ('PanelEntry.cs', 'OptimizerDev.cs')


def all_sources(folder):
    return sorted(os.path.join(d, f) for d, _, files in os.walk(folder) for f in files if f.endswith('.cs'))


def run_csc(data, managed, out, sources, extra=()):
    dotnet = os.path.join(data, 'NetCoreRuntime', 'dotnet.exe')
    csc = os.path.join(data, 'DotNetSdkRoslyn', 'csc.dll')
    refs = [f'-r:{os.path.join(managed, r)}' for r in REFERENCES]
    cmd = [dotnet, csc, '-nologo', '-utf8output', '-noconfig', '-nostdlib', '-target:library', '-optimize+', '-deterministic',
           '-langversion:9.0', '-warnaserror+', f'-out:{out}', *refs, *extra, *sources]
    result = subprocess.run(cmd, capture_output=True, text=True, encoding='utf-8', errors='replace')
    text = result.stdout + result.stderr
    sys.stdout.write(text.encode(sys.stdout.encoding or 'utf-8', errors='replace').decode(sys.stdout.encoding or 'utf-8'))
    if result.returncode != 0:
        sys.exit('Compilation failed.')


def compile_dll(data, managed, dev=False):
    sources = all_sources(os.path.join(ROOT, 'src'))
    if dev:
        # The panel is loaded at run time from build/dev (src/Dev/HotPanel.cs); the main assembly knows the repo path.
        os.makedirs(DEV_DIR, exist_ok=True)
        paths = os.path.join(DEV_DIR, 'DevPaths.cs')
        with open(paths, 'w', encoding='utf-8') as f:
            f.write('namespace ModePuzzle.Dev { static class DevPaths { public const string Root = @"%s"; } }\n' % ROOT.replace('"', '""'))
        sources = [s for s in sources if not s.startswith(PANEL_DIR + os.sep)] + [paths]
        run_csc(data, managed, os.path.join(OUT, DLL), sources, ['-define:DEV'])
        shutil.copy2(os.path.join(OUT, DLL), os.path.join(DEV_DIR, DLL))   # what the panel compiles against
        compile_panel(data, managed)
    else:
        sources = [s for s in sources if os.path.basename(s) not in DEV_ONLY_PANEL]
        run_csc(data, managed, os.path.join(OUT, DLL), sources)


def compile_panel(data, managed):
    """The hot-reloadable panel: a new assembly version each time, written atomically for the running game."""
    main = os.path.join(DEV_DIR, DLL)
    if not os.path.exists(main):
        sys.exit('No development build yet: run build.py --dev first.')
    counter = os.path.join(DEV_DIR, 'panel_version.txt')
    n = int(open(counter).read().strip()) + 1 if os.path.exists(counter) else 1
    with open(counter, 'w') as f:
        f.write(str(n))
    version = os.path.join(DEV_DIR, 'PanelVersion.cs')
    with open(version, 'w', encoding='utf-8') as f:
        f.write('[assembly: System.Reflection.AssemblyVersion("1.0.%d.%d")]\n' % (n // 60000, n % 60000))
    # The file name sets the assembly name, which must stay ModePuzzle.Panel (InternalsVisibleTo in the main assembly).
    os.makedirs(os.path.join(DEV_DIR, 'tmp'), exist_ok=True)
    temp = os.path.join(DEV_DIR, 'tmp', PANEL_DLL)
    run_csc(data, managed, temp, all_sources(PANEL_DIR) + [version], [f'-r:{main}', '-define:DEV'])
    os.replace(temp, os.path.join(DEV_DIR, PANEL_DLL))
    print(f'Panel {n} -> {os.path.join(DEV_DIR, PANEL_DLL)}')


def stage(dev=False):
    shutil.copy2(os.path.join(ROOT, 'manifest.xml'), OUT)
    if dev:
        # The development host reads the panel and commands from the repository, writes screenshots there and
        # calls the loaded panel through reflection.
        path = os.path.join(OUT, 'manifest.xml')
        text = open(path, encoding='utf-8').read()
        for capability in ('files', 'reflection-external'):
            if f'<Capability>{capability}</Capability>' not in text:
                text = text.replace('</Capabilities>', f'  <Capability>{capability}</Capability>\n  </Capabilities>')
        open(path, 'w', encoding='utf-8').write(text)
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
    parser.add_argument('--dev', action='store_true', help='development build: hot-reloadable panel from build/dev')
    parser.add_argument('--panel', action='store_true', help='rebuild only the hot-reloadable panel (game may be running)')
    args = parser.parse_args()
    data, game = unity_data_dir(), game_dir()
    managed = os.path.join(game, 'Probably Stolen_Data', 'Managed')
    if args.panel:
        compile_panel(data, managed)
        return
    if os.path.exists(OUT):
        shutil.rmtree(OUT)
    os.makedirs(OUT)
    compile_dll(data, managed, args.dev)
    stage(args.dev)
    print(f'Built {OUT}' + (' (development)' if args.dev else ''))
    if args.install:
        install(game)


if __name__ == '__main__':
    main()
