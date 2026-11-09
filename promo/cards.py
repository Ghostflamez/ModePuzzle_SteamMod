"""Render the card scenes (scenes/card.html) frame by frame with headless Chromium, timed by the narration.

    python promo/cards.py [--only title,outro] [--fps 30]

Writes promo/build/cards/<scene>.mp4 (1920x1080). Each frame sets the animation time explicitly and is
captured as a PNG, so the result does not depend on how fast the browser renders.
"""
import argparse
import asyncio
import json
import os
import subprocess
import sys

from playwright.async_api import async_playwright

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from storyboard import GAP, LEAD_IN, SEGMENTS, TAIL  # noqa: E402

FFMPEG = r'E:\ffmpeg\ffmpeg-9.0.2-essentials_build\bin\ffmpeg.exe'
OUT = os.path.join(HERE, 'build', 'cards')


def timing(seg, durations):
    offs, t = [], LEAD_IN
    for i in range(len(seg['lines'])):
        offs.append(round(t, 3))
        t += durations[f"{seg['id']}_{i}"]['seconds'] + GAP
    return offs, t - GAP + TAIL


async def render(only, fps):
    durations = json.load(open(os.path.join(HERE, 'build', 'tts', 'durations.json'), encoding='utf-8'))
    os.makedirs(OUT, exist_ok=True)
    url = 'file:///' + os.path.join(HERE, 'scenes', 'card.html').replace('\\', '/')
    async with async_playwright() as p:
        browser = await p.chromium.launch(channel='msedge')
        page = await browser.new_page(viewport={'width': 1920, 'height': 1080}, device_scale_factor=1)
        await page.goto(url)
        await page.evaluate('window.ready')
        for seg in SEGMENTS:
            if 'card' not in seg or (only and seg['id'] not in only):
                continue
            cues, length = timing(seg, durations)
            frames = int(round(length * fps))
            path = os.path.join(OUT, seg['id'] + '.mp4')
            enc = subprocess.Popen([FFMPEG, '-hide_banner', '-loglevel', 'error', '-y', '-f', 'image2pipe', '-framerate', str(fps),
                                    '-i', '-', '-c:v', 'libx264', '-preset', 'medium', '-crf', '16', '-pix_fmt', 'yuv420p', path],
                                   stdin=subprocess.PIPE)
            for k in range(frames):
                await page.evaluate('([s, t, c]) => window.render(s, t, c)', [seg['card'], k / fps, cues])
                enc.stdin.write(await page.screenshot(type='png'))
            enc.stdin.close()
            enc.wait()
            print(f"{seg['id']}: {length:.1f}s, {frames} frames", flush=True)
        await browser.close()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--only', default='')
    ap.add_argument('--fps', type=int, default=30)
    a = ap.parse_args()
    asyncio.run(render(set(filter(None, a.only.split(','))), a.fps))


if __name__ == '__main__':
    main()
