"""Last frame of every card scene, side by side in promo/build/check/cards_sheet.png (for review)."""
import asyncio
import json
import os
import sys

from PIL import Image
from playwright.async_api import async_playwright

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import cards  # noqa: E402
from storyboard import SEGMENTS  # noqa: E402


async def main():
    durations = json.load(open(os.path.join(HERE, 'build', 'tts', 'durations.json'), encoding='utf-8'))
    check = os.path.join(HERE, 'build', 'check')
    os.makedirs(check, exist_ok=True)
    ids = []
    async with async_playwright() as p:
        browser = await p.chromium.launch(channel='msedge')
        page = await browser.new_page(viewport={'width': 1920, 'height': 1080})
        await page.goto('file:///' + os.path.join(HERE, 'scenes', 'card.html').replace(os.sep, '/'))
        await page.evaluate('window.ready')
        for seg in SEGMENTS:
            if 'card' not in seg:
                continue
            cues, length = cards.timing(seg, durations)
            await page.evaluate('([s, t, c]) => window.render(s, t, c)', [seg['card'], length - 0.1, cues])
            await page.screenshot(path=os.path.join(check, f"card_{seg['id']}.png"))
            ids.append(seg['id'])
        await browser.close()
    sheet = Image.new('RGB', (1920, 540 * ((len(ids) + 1) // 2)))
    for k, sid in enumerate(ids):
        sheet.paste(Image.open(os.path.join(check, f'card_{sid}.png')).resize((960, 540)), ((k % 2) * 960, (k // 2) * 540))
    sheet.save(os.path.join(check, 'cards_sheet.png'))
    print(ids)


if __name__ == '__main__':
    asyncio.run(main())
