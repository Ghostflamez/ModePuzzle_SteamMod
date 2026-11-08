"""Compile actual TraitIcons.cs with Unity Roslyn/Mono; write only repo build/.

Run: python -B tools/trait_icons_preview.py
No game assets are needed: bays use clearly labelled synthetic textured pieces.
"""
import os
from pathlib import Path
import re
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
import build
from PIL import Image, ImageDraw


def main():
    out = ROOT / 'build'
    out.mkdir(exist_ok=True)
    data = Path(build.unity_data_dir())
    managed = Path(build.game_dir()) / 'Probably Stolen_Data' / 'Managed'
    exe = out / 'TraitIconsPreview.exe'
    # Prevent runtime/compiler temporary files from going outside the repository.
    env = dict(os.environ, TEMP=str(out), TMP=str(out), DOTNET_CLI_HOME=str(out),
               DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1', DOTNET_CLI_TELEMETRY_OPTOUT='1')
    subprocess.run([str(data / 'NetCoreRuntime/dotnet.exe'), str(data / 'DotNetSdkRoslyn/csc.dll'),
                    '-nologo', '-noconfig', '-nostdlib', '-langversion:9.0', '-warnaserror+', f'-out:{exe}',
                    *[f'-r:{managed / r}' for r in ('mscorlib.dll', 'System.dll', 'System.Core.dll')],
                    *[str(ROOT / p) for p in ('tools/TraitIconsPreview.cs', 'src/UI/TraitIcons.cs',
                                              'src/IconForge.cs', 'src/Model/Snapshot.cs')]], check=True, env=env)
    subprocess.run([str(data / 'MonoBleedingEdge/bin/mono.exe'), str(exe), str(out)], check=True, env=env)
    names = ['SideMount', 'TopMount', 'NegativeFeedback', 'Receiver', 'Premium', 'Inferior',
             'Overvolted', 'Degrading', 'Learning', 'SupportNode']
    # If sibling reference exists, verify every original RGBA pixel against its JS definition.
    reference = ROOT.parent / 'ModePuzzle/src/config/ui.js'
    if reference.exists():
        source = reference.read_text(encoding='utf-8')
        for name in names:
            key = name[0].lower() + name[1:]
            rows = re.findall(r"'([.#+]{9})'", re.search(r'\b' + key + r': \[(.*?)\]', source, re.S)[1])
            palette = {'.': bytes(4), '#': bytes.fromhex('ffe6b0ff'), '+': bytes.fromhex('ffb347ff')}
            assert (out / f'trait_{name}.rgba').read_bytes() == b''.join(palette[c] for row in rows for c in row), name
        print('All ten original bitmaps match the read-only web reference pixel for pixel.')
    sheet = Image.new('RGBA', (820, 1000), '#25222d')
    draw = ImageDraw.Draw(sheet)
    draw.text((16, 10), 'TRAIT ICONS | original 9px | nearest-neighbour 1x / 2x / 3x', fill='white')
    for i, name in enumerate(names):
        x, y = 16 + i % 2 * 400, 42 + i // 2 * 48
        draw.text((x, y + 8), name, fill='white')
        icon = Image.frombytes('RGBA', (9, 9), (out / f'trait_{name}.rgba').read_bytes())
        for scale in (1, 2, 3):
            sheet.alpha_composite(icon.resize((9 * scale, 9 * scale), Image.Resampling.NEAREST), (x + 170 + (scale - 1) * 65, y))
    y = 295
    draw.text((16, y), 'COMPACT fallback: all variants; two effects + optional node in one cell', fill='white')
    strip = Image.frombytes('RGBA', (160, 16), (out / 'trait_compact.rgba').read_bytes())
    for scale in (1, 2, 3):
        sheet.alpha_composite(strip.resize((160 * scale, 16 * scale), Image.Resampling.NEAREST), (16, y + 20))
        y += 16 * scale + 24
    for scene, label in enumerate(('L pieces / rotations 0,1,2,3', 'Flipped L pieces / rotations 0,1,2,3', 'Single-cell pairs / last cell also node')):
        bay = Image.frombytes('RGBA', (128, 64), (out / f'trait_bay{scene}.rgba').read_bytes())
        draw.text((16, y + 4), 'Synthetic bay: ' + label, fill='white')
        scale = scene + 1
        sheet.alpha_composite(bay.resize((128 * scale, 64 * scale), Image.Resampling.NEAREST), (400, y))
        y += 64 * scale + 20
    sheet = sheet.crop((0, 0, 820, y))
    sheet.save(out / 'trait_icons_preview.png')
    print('Wrote', out / 'trait_icons_preview.png')


if __name__ == '__main__':
    main()
