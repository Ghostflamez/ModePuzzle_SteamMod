"""Run production ComposeBay against a minimal host; no game assets. Outputs only build/."""
import os
from pathlib import Path
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
    managed = Path(build.game_dir()) / 'Probably Stolen_Data/Managed'
    env = dict(os.environ, TEMP=str(out), TMP=str(out), DOTNET_CLI_HOME=str(out))
    exe = out / 'BayPreview.exe'
    sources = ['tools/BayPreview.cs', 'src/UI/ModuleArt.cs', 'src/UI/BayNumbers.cs',
               'src/UI/PanelStyle.cs', 'src/UI/TraitIcons.cs', 'src/IconForge.cs', 'src/Model/Snapshot.cs']
    subprocess.run([str(data / 'NetCoreRuntime/dotnet.exe'), str(data / 'DotNetSdkRoslyn/csc.dll'),
                    '-nologo', '-noconfig', '-nostdlib', '-langversion:9.0', '-warnaserror+', f'-out:{exe}',
                    *[f'-r:{managed / r}' for r in ['mscorlib.dll', 'System.dll', 'System.Core.dll']],
                    *[str(ROOT / p) for p in sources]], check=True, env=env)
    subprocess.run([str(data / 'MonoBleedingEdge/bin/mono.exe'), str(exe), str(out)], check=True, env=env)
    sheet = Image.new('RGB', (960, 390), '#46423c')
    draw = ImageDraw.Draw(sheet)
    for i, name in enumerate(['bay_empty', 'bay_numbered']):
        draw.text((16 + i * 480, 12), 'Empty bay' if i == 0 else 'Synthetic pieces + badges + numbers 1-12', fill='#fff3d6')
        bay = Image.frombytes('RGBA', (114, 82), (out / (name + '.rgba')).read_bytes())
        sheet.paste(bay.resize((456, 328), Image.Resampling.NEAREST), (16 + i * 480, 40))
    sheet.save(out / 'bay_preview.png')
    print('Wrote', out / 'bay_preview.png')

if __name__ == '__main__':
    main()
