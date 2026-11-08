"""Stage a release for the Steam Workshop and write the steamcmd item file.

    python tools/workshop/prepare.py --preview build/workshop/preview.png --changenote "First release"

Builds the release (python build.py), checks it (no capabilities, no development switches or strings, no
development code), copies it to build/workshop/content/ and writes build/workshop/item.vdf. Upload yourself:

    steamcmd +login <account> +workshop_build_item "<stage>/item.vdf" +quit

steamcmd writes the new item id back into item.vdf; copy it into tools/workshop/published_id.txt so later
uploads update the same item. The first upload is hidden (visibility 2) until --public is given. The preview
image is not part of the repository.
"""
import argparse
import os
import re
import shutil
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
HERE = os.path.dirname(os.path.abspath(__file__))
DIST = os.path.join(ROOT, 'dist', 'ModePuzzle')
APP_ID = '4348910'
TITLE = '超科技模组优化器 Hypertech Module Optimizer'
MAX_PREVIEW = 1024 * 1024


def fail(message):
    sys.exit('Not ready: ' + message)


def check_release():
    manifest = open(os.path.join(DIST, 'manifest.xml'), encoding='utf-8').read()
    if not re.search(r'<Capabilities\s*/>', manifest):
        fail('the manifest declares capabilities (a development build?)')
    for name in os.listdir(os.path.join(DIST, 'Localization')):
        text = open(os.path.join(DIST, 'Localization', name), encoding='utf-8').read()
        if re.search(r'^(cfg_dev_|debug_)', text, re.M):
            fail(f'{name} contains development strings')
    dll = open(os.path.join(DIST, 'ModePuzzle.dll'), 'rb').read()
    for marker in (b'HotPanel', b'DevPaths', b'dev_spawn_on_load'):
        if marker in dll:
            fail(f'ModePuzzle.dll contains development code ({marker.decode()})')
    extra = [f for f in os.listdir(DIST) if f not in ('manifest.xml', 'ModePuzzle.dll', 'Localization')]
    if extra:
        fail('unexpected files in the package: ' + ', '.join(extra))
    version = re.search(r'<ModVersion>(.*?)</ModVersion>', manifest).group(1)
    return version


def vdf_string(text):
    return text.replace('\\', '\\\\').replace('"', '\\"')


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--preview', required=True, help='preview image (png or jpg, under 1 MB)')
    parser.add_argument('--changenote', required=True, help='what changed in this upload')
    parser.add_argument('--public', action='store_true', help='make the item public (default: hidden)')
    parser.add_argument('--stage', default=os.path.join(ROOT, 'build', 'workshop'),
                        help='where to put the upload (steamcmd may not cope with non-ASCII paths: use e.g. C:/ModePuzzleWorkshop)')
    args = parser.parse_args()
    STAGE = os.path.abspath(args.stage)
    os.makedirs(STAGE, exist_ok=True)
    if not STAGE.isascii():
        print('Warning: the stage path is not plain ASCII; steamcmd may fail to read it. Consider --stage C:/ModePuzzleWorkshop')

    preview = os.path.abspath(args.preview)
    if not os.path.isfile(preview):
        fail('preview not found: ' + preview)
    if os.path.getsize(preview) >= MAX_PREVIEW:
        fail('preview is 1 MB or larger')

    subprocess.run([sys.executable, os.path.join(ROOT, 'build.py')], check=True)
    version = check_release()

    content = os.path.join(STAGE, 'content')
    if os.path.exists(content):
        shutil.rmtree(content)
    shutil.copytree(DIST, content)
    staged_preview = os.path.join(STAGE, 'preview' + os.path.splitext(preview)[1].lower())
    if os.path.abspath(staged_preview) != preview:
        shutil.copy2(preview, staged_preview)

    id_file = os.path.join(HERE, 'published_id.txt')
    published = open(id_file).read().strip() if os.path.exists(id_file) else '0'
    description = open(os.path.join(HERE, 'description.txt'), encoding='utf-8').read().strip()
    fields = [
        ('appid', APP_ID),
        ('publishedfileid', published),
        ('contentfolder', content),
        ('previewfile', staged_preview),
        ('visibility', '0' if args.public else '2'),
        ('title', TITLE),
        ('description', description),
        ('changenote', f'{version}: {args.changenote}'),
    ]
    body = ''.join(f'\t"{k}"\t\t"{vdf_string(v)}"\n' for k, v in fields)
    vdf = os.path.join(STAGE, 'item.vdf')
    with open(vdf, 'w', encoding='utf-8') as f:
        f.write('"workshopitem"\n{\n' + body + '}\n')

    print(f'Ready: version {version}, item {"new" if published == "0" else published}, '
          f'{"public" if args.public else "hidden"}.')
    print(f'  steamcmd +login <account> +workshop_build_item "{vdf}" +quit')


if __name__ == '__main__':
    main()
