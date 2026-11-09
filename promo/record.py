"""Record the in-game segments of the promo: the real game, driven by the real mouse and the development build's
command channel, captured per segment with ffmpeg (ddagrab + NVENC).

    python promo/record.py [--only open,run] [--dry] [--ffmpeg E:/ffmpeg/.../ffmpeg.exe]

Needs the development build running in the game (build/dev/cmd.txt answered), the game fullscreen on the primary
monitor, and promo/build/tts/durations.json. Do not touch the mouse or keyboard while a segment records.
Writes promo/build/game/<segment>.mp4 and <segment>.json (clicks, for the sound effects).
"""
import argparse
import ctypes
import json
import os
import subprocess
import sys
import time
from ctypes import wintypes

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
sys.path.insert(0, HERE)
from storyboard import GAP, LEAD_IN, SEGMENTS, TAIL  # noqa: E402

DEV = os.path.join(ROOT, 'build', 'dev')
OUT = os.path.join(HERE, 'build', 'game')
FFMPEG = r'E:\ffmpeg\ffmpeg-9.0.2-essentials_build\bin\ffmpeg.exe'

user32 = ctypes.windll.user32
ctypes.windll.shcore.SetProcessDpiAwareness(2)

# ---------------- input ----------------
INPUT_MOUSE, INPUT_KEYBOARD = 0, 1
MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP = 0x2, 0x4
MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP = 0x8, 0x10
MOUSEEVENTF_WHEEL = 0x800
KEYEVENTF_KEYUP, KEYEVENTF_UNICODE = 0x2, 0x4


class MOUSEINPUT(ctypes.Structure):
    _fields_ = [('dx', wintypes.LONG), ('dy', wintypes.LONG), ('mouseData', wintypes.DWORD), ('dwFlags', wintypes.DWORD),
                ('time', wintypes.DWORD), ('dwExtraInfo', ctypes.POINTER(ctypes.c_ulong))]


class KEYBDINPUT(ctypes.Structure):
    _fields_ = [('wVk', wintypes.WORD), ('wScan', wintypes.WORD), ('dwFlags', wintypes.DWORD), ('time', wintypes.DWORD),
                ('dwExtraInfo', ctypes.POINTER(ctypes.c_ulong))]


class _U(ctypes.Union):
    _fields_ = [('mi', MOUSEINPUT), ('ki', KEYBDINPUT), ('pad', ctypes.c_byte * 32)]


class INPUT(ctypes.Structure):
    _fields_ = [('type', wintypes.DWORD), ('u', _U)]


def _send(*inputs):
    arr = (INPUT * len(inputs))(*inputs)
    user32.SendInput(len(inputs), arr, ctypes.sizeof(INPUT))


def _mouse(flags, data=0):
    i = INPUT(type=INPUT_MOUSE)
    i.u.mi = MOUSEINPUT(0, 0, data & 0xFFFFFFFF, flags, 0, None)
    return i


def _key(vk=0, scan=0, flags=0):
    i = INPUT(type=INPUT_KEYBOARD)
    i.u.ki = KEYBDINPUT(vk, scan, flags, 0, None)
    return i


class Mouse:
    """The real cursor, moved like a hand: eased glides, short pauses, clicks logged for sound effects."""

    def __init__(self):
        self.events = []
        self.t0 = time.perf_counter()

    def pos(self):
        p = wintypes.POINT()
        user32.GetCursorPos(ctypes.byref(p))
        return p.x, p.y

    def move(self, x, y, seconds=0.45):
        x0, y0 = self.pos()
        steps = max(8, int(seconds * 120))
        for k in range(1, steps + 1):
            t = k / steps
            e = t * t * (3 - 2 * t)   # smoothstep
            user32.SetCursorPos(int(round(x0 + (x - x0) * e)), int(round(y0 + (y - y0) * e)))
            time.sleep(seconds / steps)

    def _log(self, kind):
        self.events.append({'t': round(time.perf_counter() - self.t0, 3), 'type': kind})

    def click(self, right=False):
        down, up = (MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP) if right else (MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP)
        _send(_mouse(down)); time.sleep(0.15 if right else 0.1); _send(_mouse(up))   # a short right press is ignored
        self._log('click')

    def double(self):
        self.click(); time.sleep(0.09); self.click()

    def wheel(self, notches):
        for _ in range(abs(notches)):
            _send(_mouse(MOUSEEVENTF_WHEEL, 120 if notches > 0 else -120))
            time.sleep(0.05)

    def type(self, text, per_char=0.09):
        for ch in text:
            _send(_key(scan=ord(ch), flags=KEYEVENTF_UNICODE), _key(scan=ord(ch), flags=KEYEVENTF_UNICODE | KEYEVENTF_KEYUP))
            self._log('key')
            time.sleep(per_char)

    def key(self, vk):
        _send(_key(vk=vk), _key(vk=vk, flags=KEYEVENTF_KEYUP))


VK_RETURN, VK_ESCAPE = 0x0D, 0x1B

# ---------------- command channel ----------------
_batch = [int(time.time()) % 100000]


def cmd(*lines, timeout=20):
    """Runs development commands; answers their results in order."""
    _batch[0] += 1
    bid = f'rec{_batch[0]}'
    out = os.path.join(DEV, 'out.txt')
    with open(os.path.join(DEV, 'cmd.txt'), 'w', encoding='utf-8') as f:
        f.write(bid + '\n' + '\n'.join(lines) + '\n')
    end = time.time() + timeout
    while time.time() < end:
        try:
            got = open(out, encoding='utf-8').read().split('\n')
            if got and got[0].strip() == bid and 'done' in got:
                return [g.split(' -> ', 1)[1] if ' -> ' in g else g for g in got[1:got.index('done')]]
        except OSError:
            pass
        time.sleep(0.03)
    raise SystemExit(f'no answer to {lines}')


def rect(query, kind='where'):
    r = cmd(f'{kind} {query}')[0]
    if r == 'none':
        raise SystemExit(f'not on screen: {kind} {query}')
    x, y, w, h = map(int, r.split())
    return x, y, w, h


def center(query, kind='where', dx=0, dy=0):
    x, y, w, h = rect(query, kind)
    return x + w // 2 + dx, y + h // 2 + dy


# ---------------- recording ----------------
class Take:
    def __init__(self, seg, durations, ffmpeg, dry):
        self.seg, self.dry = seg, dry
        self.offs, t = [], LEAD_IN
        for i in range(len(seg['lines'])):
            self.offs.append(t)
            t += durations[f"{seg['id']}_{i}"]['seconds'] + GAP
        self.length = t - GAP + TAIL
        self.ffmpeg = ffmpeg
        self.path = os.path.join(OUT, seg['id'] + '.mp4')

    def __enter__(self):
        os.makedirs(OUT, exist_ok=True)
        if not self.dry:
            self.proc = subprocess.Popen(
                [self.ffmpeg, '-hide_banner', '-loglevel', 'error', '-y', '-f', 'lavfi',
                 '-i', 'ddagrab=output_idx=0:framerate=30:draw_mouse=1',
                 '-vf', 'hwdownload,format=bgra,scale=1920:1080:flags=lanczos,format=yuv420p',
                 '-c:v', 'h264_nvenc', '-preset', 'p5', '-cq', '18', '-t', f'{self.length + 1.5:.2f}', self.path],
                stdin=subprocess.PIPE)
            time.sleep(1.0)   # capture warm-up; trimmed in compose (the recorder notes where the take starts)
        self.mouse = Mouse()
        self.start = time.perf_counter()
        self.mouse.t0 = self.start
        return self

    def cue(self, i, extra=0.0):
        """Wait until sentence i (plus extra seconds) starts."""
        t = self.start + self.offs[i] + extra
        while time.perf_counter() < t:
            time.sleep(0.005)

    def __exit__(self, *exc):
        end = self.start + self.length
        while time.perf_counter() < end:
            time.sleep(0.01)
        if not self.dry:
            self.proc.wait(timeout=30)
            meta = {'lead': 1.0, 'length': round(self.length, 3), 'events': self.mouse.events}
            json.dump(meta, open(os.path.join(OUT, self.seg['id'] + '.json'), 'w', encoding='utf-8'), indent=1)
        print(f"{self.seg['id']}: {self.length:.1f}s", flush=True)


# ---------------- segments ----------------
def go(m, query, kind='where', seconds=0.45, dx=0, dy=0):
    x, y = center(query, kind, dx, dy)
    m.move(x, y, seconds)
    return x, y


def click(m, query, kind='where', **kw):
    go(m, query, kind, **kw)
    time.sleep(0.12)
    m.click()


def park(m):
    """Cursor off to the right side of the scene while nothing is pointed at."""
    m.move(3300, 1500, 0.5)


SETUP = {}
SCRIPT = {}


def setup(name):
    def deco(f):
        SETUP[name] = f
        return f
    return deco


def script(name):
    def deco(f):
        SCRIPT[name] = f
        return f
    return deco


@setup('open')
def _(m):
    cmd('close')


@script('open')
def _(t):
    m = t.mouse
    t.cue(0)
    go(m, 'modepuzzle_optimizer', 'whereitem', seconds=0.7)
    time.sleep(0.35)
    m.double()
    t.cue(1, -0.4)
    cmd('close')
    time.sleep(0.4)
    go(m, 'modepuzzle_optimizer', 'whereitem', seconds=0.4)
    time.sleep(0.3)
    # A left click selects the tool, so the double-click left it selected: the first right click drops that,
    # the second opens the context menu.
    m.click(right=True)
    time.sleep(0.35)
    m.click(right=True)
    time.sleep(0.5)
    click(m, '装备', seconds=0.35)
    time.sleep(0.4)
    go(m, 'moisture_farm', 'whereitem', seconds=0.7)
    time.sleep(0.3)
    m.click()
    t.cue(2, 0.6)
    park(m)


def hover(m, query, kind='where', hold=1.0, **kw):
    go(m, query, kind, **kw)
    time.sleep(hold)


@setup('tour')
def _(m):
    cmd('scroll 1')


@script('tour')
def _(t):
    m = t.mouse
    t.cue(0)
    x, y, w, h = rect('', 'wherebay')
    m.move(x + w // 3, y + h // 2, 0.6)
    m.move(x + 2 * w // 3, y + h // 2, 0.9)
    hover(m, '当前', hold=0.8, seconds=0.6)
    t.cue(1)
    click(m, 'b:更换机器')
    time.sleep(0.9)
    t.cue(2)
    # The picker shows each machine's picture above its name; rest on the 3D printer's picture.
    x, y, w, h = rect('3D打印机')
    m.move(x + w // 2, y - 40, 0.6)   # lower half of the picture: the lock badge sits at its top left
    time.sleep(2.2)
    m.click()
    time.sleep(0.6)
    park(m)


@setup('run')
def _(m):
    cmd('scroll 1')


@script('run')
def _(t):
    m = t.mouse
    t.cue(0)
    click(m, 'b:0:性能')
    time.sleep(0.4)
    click(m, 'b:15 秒')
    time.sleep(0.5)
    click(m, 'b:5 秒', seconds=0.35)
    t.cue(1)
    click(m, 'b:开始优化')
    time.sleep(0.6)
    hover(m, '已尝试', hold=0.5, seconds=0.5)
    t.cue(2, 0.3)
    hover(m, '优化后', hold=1.2, seconds=0.6)
    t.cue(3)
    click(m, 'b:当前布局')
    time.sleep(1.3)
    click(m, 'b:优化结果', seconds=0.35)
    time.sleep(0.5)
    park(m)


@script('result')
def _(t):
    m = t.mouse
    t.cue(0)
    x, y, w, h = rect('', 'wherebay')
    m.move(x + w, y + h + 200, 0.5)
    m.wheel(-4)
    time.sleep(0.6)
    hover(m, '新加入本机', hold=0.8, seconds=0.6)
    t.cue(1)
    click(m, 'b:优化明细')
    time.sleep(0.8)
    hover(m, '合计', hold=0.6, seconds=0.6)
    park(m)


@setup('apply')
def _(m):
    cmd('tab modules', 'scroll 1')


@script('apply')
def _(t):
    m = t.mouse
    t.cue(0)
    click(m, 'b:应用布局')
    time.sleep(0.8)
    x, y, w, h = rect('', 'wherebay')
    m.move(x + w // 2, y + h // 2, 0.6)
    t.cue(1)
    m.move(x + w // 2, y + h - 40, 0.8)
    t.cue(2)
    # Shown, not clicked: the installed modules stay for the next segment (do not save the game afterwards).
    hover(m, 'b:恢复上一布局', hold=1.2)
    park(m)


@setup('control')
def _(m):
    cmd('bay current', 'tab modules', 'scroll 0.62')


@script('control')
def _(t):
    m = t.mouse
    t.cue(0)
    hover(m, 'b:0:不锁', hold=1.1)
    hover(m, 'b:0:保留', hold=1.1, seconds=0.3)
    hover(m, 'b:0:固定', hold=1.1, seconds=0.3)
    t.cue(1)
    m.move(*center('b:0:固定', dy=-300), seconds=0.4)
    m.wheel(8)
    time.sleep(0.5)
    click(m, '排除所有退化模组')
    time.sleep(0.6)
    click(m, '排除所有退化模组', seconds=0.2)
    t.cue(2)
    # The lock button sits left of 更换机器: lock width 44 and a row gap, at the panel's scale of 2.
    x, y, w, h = rect('b:更换机器')
    m.move(x - 16 - 44, y + h // 2, 0.5)
    time.sleep(0.6)
    m.click()
    time.sleep(1.0)
    click(m, 'b:解锁')
    time.sleep(0.6)
    park(m)


@setup('custom')
def _(m):
    cmd('scroll 1')


@script('custom')
def _(t):
    m = t.mouse
    t.cue(0, 1.2)
    click(m, 'b:自定义')
    time.sleep(0.6)
    t.cue(1)
    hover(m, 'b:1:性能', hold=0.5)
    click(m, '0:不限')
    time.sleep(0.2)
    m.type('-100')
    m.key(VK_RETURN)
    time.sleep(0.5)
    t.cue(2)
    click(m, 'b:开始优化')
    time.sleep(0.6)
    park(m)


@setup('spare')
def _(m):
    cmd('tab modules', 'scroll 0.5')


@script('spare')
def _(t):
    m = t.mouse
    t.cue(0)
    click(m, 'b:空闲模组')
    time.sleep(0.5)
    click(m, '搜索名称')
    time.sleep(0.2)
    m.type('超频', per_char=0.25)
    time.sleep(0.6)
    click(m, 'b:默认顺序')
    time.sleep(0.5)
    click(m, 'b:按性能排序', seconds=0.35)
    t.cue(1)
    click(m, '0:必选')
    time.sleep(0.6)
    park(m)


@setup('badges')
def _(m):
    cmd('scroll 1')


@script('badges')
def _(t):
    m = t.mouse
    t.cue(0)
    cmd('close')
    go(m, 'hypertech_bay', 'whereitem', seconds=0.7)
    time.sleep(0.3)
    m.double()
    time.sleep(1.0)
    m.move(1700, 900, 0.8)
    m.move(2100, 1100, 0.9)
    t.cue(1)
    park(m)


def focus_game():
    """Brings the game to the front (an Alt tap lets this process take the foreground)."""
    hwnd = user32.FindWindowW(None, 'Probably Stolen')
    if not hwnd:
        raise SystemExit('game window not found')
    VK_MENU = 0x12
    _send(_key(vk=VK_MENU), _key(vk=VK_MENU, flags=KEYEVENTF_KEYUP))
    user32.SetForegroundWindow(hwnd)
    time.sleep(0.4)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--only', default='')
    ap.add_argument('--dry', action='store_true', help='drive the game without recording')
    ap.add_argument('--ffmpeg', default=FFMPEG)
    a = ap.parse_args()
    durations = json.load(open(os.path.join(HERE, 'build', 'tts', 'durations.json'), encoding='utf-8'))
    only = set(filter(None, a.only.split(',')))
    focus_game()
    for seg in SEGMENTS:
        if not seg.get('game') or (only and seg['id'] not in only) or seg['id'] not in SCRIPT:
            continue
        if seg['id'] in SETUP:
            SETUP[seg['id']](Mouse())
        time.sleep(0.5)
        with Take(seg, durations, a.ffmpeg, a.dry) as take:
            SCRIPT[seg['id']](take)


if __name__ == '__main__':
    main()
