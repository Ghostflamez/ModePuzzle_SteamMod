"""Preview the generated item icon without starting the game (development only).

    python tools/icon_preview.py

Reads the two source sprites from the local game install (read-only, needs UnityPy and Pillow), runs the
same IconForge code the mod uses, and writes build/icon_preview.png. Nothing read from the game is
written anywhere except build/, which is git-ignored and never shipped.
"""
import os
import subprocess
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import build  # noqa: E402

from PIL import Image  # noqa: E402

ROOT = build.ROOT
OUT = os.path.join(ROOT, 'build')
SPRITES = ('genetic_scanner', 'module_black_4')
SCALE = 8


def read_sprites(game):
    import UnityPy
    data = os.path.join(game, 'Probably Stolen_Data')
    found = {}
    for name in ('resources.assets', 'sharedassets0.assets', 'sharedassets1.assets'):
        env = UnityPy.load(os.path.join(data, name))
        for obj in env.objects:
            if obj.type.name != 'Sprite':
                continue
            sprite = obj.read()
            if sprite.m_Name in SPRITES and sprite.m_Name not in found:
                found[sprite.m_Name] = sprite.image.convert('RGBA')
        if len(found) == len(SPRITES):
            return found
    sys.exit(f'Sprites not found: {set(SPRITES) - set(found)}')


def main():
    data, game = build.unity_data_dir(), build.game_dir()
    os.makedirs(OUT, exist_ok=True)
    sprites = read_sprites(game)
    raw = {}
    for name, img in sprites.items():
        raw[name] = os.path.join(OUT, name + '.raw')
        with open(raw[name], 'wb') as f:
            f.write(img.tobytes())

    exe = os.path.join(OUT, 'IconPreview.exe')
    managed = os.path.join(game, 'Probably Stolen_Data', 'Managed')
    subprocess.run([os.path.join(data, 'NetCoreRuntime', 'dotnet.exe'), os.path.join(data, 'DotNetSdkRoslyn', 'csc.dll'),
                    '-nologo', '-noconfig', '-nostdlib', '-langversion:9.0', f'-out:{exe}',
                    *[f'-r:{os.path.join(managed, r)}' for r in ('mscorlib.dll', 'System.dll', 'System.Core.dll')],
                    os.path.join(ROOT, 'tools', 'IconPreview.cs'), os.path.join(ROOT, 'src', 'IconForge.cs')], check=True)

    scanner = sprites['genetic_scanner']
    out_raw = os.path.join(OUT, 'icon.raw')
    mono = os.path.join(data, 'MonoBleedingEdge', 'bin', 'mono.exe')
    subprocess.run([mono, exe, raw['genetic_scanner'], str(scanner.width), str(scanner.height),
                    raw['module_black_4'], out_raw], check=True)

    icon = Image.frombytes('RGBA', scanner.size, open(out_raw, 'rb').read())
    icon.save(os.path.join(OUT, 'icon.png'))
    w, h = scanner.size
    sheet = Image.new('RGBA', (w * SCALE * 2 + SCALE * 4, h * SCALE), (58, 58, 68, 255))
    for i, img in enumerate((scanner, icon)):
        big = img.resize((w * SCALE, h * SCALE), Image.NEAREST)
        sheet.alpha_composite(big, (i * (w * SCALE + SCALE * 4), 0))
    sheet.save(os.path.join(OUT, 'icon_preview.png'))
    print('Wrote', os.path.join(OUT, 'icon_preview.png'))


if __name__ == '__main__':
    main()
