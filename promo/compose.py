"""Assemble the promo: card and game segments back to back, narration, click sounds, BGM and subtitles.

    python promo/compose.py --bgm <a.wav> [--bgm <b.wav> ...] --click <click.wav> [--out ...] [--crf 18]

Inputs: promo/build/cards/<id>.mp4 (cards.py), promo/build/game/<id>.mp4 + .json (record.py) and
promo/build/tts/*.wav + durations.json (tts.py). The game recordings start `lead` seconds before the take; that
is trimmed. The Steam FPS counter in the top right corner of the game footage is masked. BGM tracks play in order
with a crossfade and are ducked under the narration.
"""
import argparse
import json
import os
import shutil
import subprocess
import sys

import numpy as np
import soundfile as sf

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from storyboard import GAP, LEAD_IN, SEGMENTS, TAIL  # noqa: E402

BUILD = os.path.join(HERE, 'build')
FFMPEG = r'E:\ffmpeg\ffmpeg-9.0.2-essentials_build\bin\ffmpeg.exe'
FPS, SR = 30, 48000
FPS_MASK = 'drawbox=x=1850:y=0:w=70:h=20:color=0x1a1418:t=fill'   # the Steam FPS counter in the game footage (1080p)


def load(path):
    """Any audio file -> float32 stereo at SR (via ffmpeg)."""
    raw = subprocess.run([FFMPEG, '-v', 'error', '-i', path, '-f', 'f32le', '-ac', '2', '-ar', str(SR), '-'],
                         capture_output=True, check=True).stdout
    return np.frombuffer(raw, dtype=np.float32).reshape(-1, 2).copy()


def ramp_gain(n, intervals, low, high, ramp):
    """Gain curve: `low` while the narration speaks, `high` elsewhere, with smooth ramps."""
    step = 0.01
    m = int(n / SR / step) + 2
    mask = np.zeros(m, np.float32)
    for s, e in intervals:
        mask[int(s / step):int(e / step) + 1] = 1
    k = max(1, int(ramp / step))
    mask = np.convolve(np.pad(mask, k, mode='edge'), np.ones(2 * k + 1) / (2 * k + 1), mode='same')[k:-k]
    grid = np.arange(m) * step
    curve = high - (high - low) * np.clip(mask * 2, 0, 1)
    return np.interp(np.arange(n) / SR, grid, curve).astype(np.float32)


def ass_time(t):
    h, rem = divmod(max(0.0, t), 3600)
    m, s = divmod(rem, 60)
    return f'{int(h)}:{int(m):02d}:{s:05.2f}'


def wrap(text, limit=24):
    if len(text) <= limit:
        return text
    mid = len(text) / 2
    cuts = [i + 1 for i, ch in enumerate(text[:-1]) if ch in '，、：；。！？']
    if not cuts:
        return text
    cut = min(cuts, key=lambda i: abs(i - mid))
    return text[:cut] + r'\N' + text[cut:]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--bgm', action='append', required=True)
    ap.add_argument('--click', required=True)
    ap.add_argument('--out', default=os.path.join(BUILD, 'ModePuzzle-mod-promo.mp4'))
    ap.add_argument('--crf', type=int, default=18)
    ap.add_argument('--music', type=float, default=0.42, help='BGM gain without narration')
    ap.add_argument('--duck', type=float, default=0.14, help='BGM gain under narration')
    a = ap.parse_args()
    durations = json.load(open(os.path.join(BUILD, 'tts', 'durations.json'), encoding='utf-8'))

    # ---- timeline ----
    parts = []
    out_t = 0.0
    for seg in SEGMENTS:
        cues, t = [], LEAD_IN
        for i in range(len(seg['lines'])):
            cues.append(t)
            t += durations[f"{seg['id']}_{i}"]['seconds'] + GAP
        length = t - GAP + TAIL
        if seg.get('game'):
            meta = json.load(open(os.path.join(BUILD, 'game', seg['id'] + '.json'), encoding='utf-8'))
            src, lead, events = os.path.join(BUILD, 'game', seg['id'] + '.mp4'), meta['lead'], meta['events']
        else:
            src, lead, events = os.path.join(BUILD, 'cards', seg['id'] + '.mp4'), 0.0, []
        parts.append({'seg': seg, 'src': src, 'lead': lead, 'length': length, 'out': out_t, 'cues': cues,
                      'events': events, 'game': bool(seg.get('game'))})
        out_t += length
    total = out_t
    print(f'{len(parts)} segments, {total:.1f}s ({int(total // 60)}:{total % 60:04.1f})')

    # ---- audio ----
    n = int(total * SR) + SR
    voice = np.zeros((n, 2), np.float32)
    spoken, subs = [], []
    for p in parts:
        for i, text in enumerate(p['seg']['lines']):
            clip = load(os.path.join(BUILD, 'tts', f"{p['seg']['id']}_{i}.wav"))
            rms = float(np.sqrt(np.mean(clip ** 2))) or 1.0
            clip *= 0.11 / rms
            s = p['out'] + p['cues'][i]
            i0 = int(s * SR)
            m = min(len(clip), n - i0)
            voice[i0:i0 + m] += clip[:m]
            e = s + len(clip) / SR
            spoken.append((s, e))
            subs.append((s - 0.05, e + 0.15, text))

    sfx = np.zeros_like(voice)
    click = load(a.click)
    click *= 0.35 / (float(np.max(np.abs(click))) or 1.0)
    for p in parts:
        for ev in p['events']:
            if ev['type'] != 'click' or ev['t'] > p['length']:
                continue
            i0 = int((p['out'] + ev['t']) * SR)
            m = min(len(click), n - i0)
            sfx[i0:i0 + m] += click[:m]

    music = np.zeros_like(voice)
    pos, xfade = 0, int(3.0 * SR)
    for k, path in enumerate(a.bgm * 4):
        if pos >= n:
            break
        track = load(path)
        if k > 0:
            fade = np.linspace(0, 1, xfade, dtype=np.float32)[:, None]
            pos = max(0, pos - xfade)
            track[:xfade] *= fade
            music[pos:pos + xfade] *= (1 - fade)[:min(xfade, n - pos)]
        m = min(len(track), n - pos)
        music[pos:pos + m] += track[:m]
        pos += m
    music /= float(np.max(np.abs(music))) or 1.0
    music *= ramp_gain(n, spoken, a.duck, a.music, 0.35)[:, None]
    fi, fo = int(1.5 * SR), int(4.0 * SR)
    music[:fi] *= np.linspace(0, 1, fi, dtype=np.float32)[:, None]
    end = int(total * SR)
    music[end - fo:end] *= np.linspace(1, 0, fo, dtype=np.float32)[:, None]

    mix = (voice + sfx + music)[:end]
    peak = float(np.max(np.abs(mix)))
    if peak > 0.97:
        mix *= 0.97 / peak
    sf.write(os.path.join(BUILD, 'mix.wav'), mix, SR, subtype='FLOAT')

    # ---- subtitles ----
    fonts = os.path.join(BUILD, 'fonts')
    os.makedirs(fonts, exist_ok=True)
    for f in ('msyhbd.ttc', 'msyh.ttc'):
        src = os.path.join(os.environ.get('WINDIR', r'C:\Windows'), 'Fonts', f)
        if os.path.exists(src) and not os.path.exists(os.path.join(fonts, f)):
            shutil.copy(src, fonts)
    head = """[Script Info]
ScriptType: v4.00+
PlayResX: 1920
PlayResY: 1080
WrapStyle: 2

[V4+ Styles]
Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding
Style: Sub,Microsoft YaHei UI,50,&H00D6F3FF,&H00FFFFFF,&H28181418,&H00000000,1,0,0,0,100,100,1,0,3,14,0,2,80,80,46,1

[Events]
Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
"""
    body = []
    for k, (s, e, text) in enumerate(subs):
        if k + 1 < len(subs):
            e = min(e, subs[k + 1][0] - 0.02)
        body.append(f'Dialogue: 0,{ass_time(s)},{ass_time(e)},Sub,,0,0,0,,{wrap(text)}')
    with open(os.path.join(BUILD, 'subs.ass'), 'w', encoding='utf-8-sig') as fh:
        fh.write(head + '\n'.join(body) + '\n')
    with open(os.path.join(BUILD, 'subs.srt'), 'w', encoding='utf-8') as fh:
        for k, (s, e, text) in enumerate(subs, 1):
            f = lambda t: ass_time(t).replace('.', ',') + '0'  # noqa: E731
            fh.write(f'{k}\n0{f(s)} --> 0{f(e)}\n{text}\n\n')

    # ---- video: trim every part, normalise, concatenate, subtitles ----
    inputs, chains = [], []
    for k, p in enumerate(parts):
        inputs += ['-ss', f"{p['lead']:.3f}", '-t', f"{p['length']:.3f}", '-i', p['src']]
        vf = f'fps={FPS},scale=1920:1080:flags=lanczos,setsar=1,format=yuv420p'
        if p['game']:
            vf = FPS_MASK + ',' + vf
        # hold the last frame if a recording is a little short
        vf += f",tpad=stop_mode=clone:stop_duration=2,trim=duration={p['length']:.3f},setpts=PTS-STARTPTS"
        chains.append(f'[{k}:v]{vf}[v{k}]')
    concat = ''.join(f'[v{k}]' for k in range(len(parts))) + f'concat=n={len(parts)}:v=1:a=0[cat]'
    graph = ';'.join(chains + [concat, '[cat]subtitles=subs.ass:fontsdir=fonts[out]'])
    out = os.path.abspath(a.out)
    cmd = [FFMPEG, '-y', '-v', 'error', '-stats', *inputs, '-i', 'mix.wav', '-filter_complex', graph,
           '-map', '[out]', '-map', f'{len(parts)}:a', '-c:v', 'libx264', '-preset', 'slow', '-crf', str(a.crf),
           '-pix_fmt', 'yuv420p', '-c:a', 'aac', '-b:a', '192k', '-shortest', '-movflags', '+faststart', out]
    subprocess.run(cmd, cwd=BUILD, check=True)
    print('wrote', out)


if __name__ == '__main__':
    main()
