"""Synthesise the narration with Fun-CosyVoice3-0.5B (local, GPU). One WAV per sentence.

Run with CosyVoice's own environment:
    E:/CosyVoice/.venv/Scripts/python.exe promo/tts.py --cosyvoice E:/CosyVoice
        [--prompt-wav <reference.wav> --prompt-text "<what it says>"] [--rl] [--only seg1,seg2] [--force]
        [--sample "<one sentence>"]
Writes promo/build/tts/<segment>_<i>.wav and promo/build/tts/durations.json. --sample writes only
promo/build/tts/sample[_rl].wav, for choosing a voice.
"""
import argparse
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, 'build', 'tts')
SYSTEM = 'You are a helpful assistant.<|endofprompt|>'


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--cosyvoice', required=True, help='the CosyVoice checkout')
    ap.add_argument('--model', default='pretrained_models/Fun-CosyVoice3-0.5B')
    ap.add_argument('--prompt-wav', default=None, help='reference voice (default: the repository sample)')
    ap.add_argument('--prompt-text', default='希望你以后能够做的比我还好呦。')
    ap.add_argument('--rl', action='store_true', help='use the RL-tuned LLM weights (llm.rl.pt)')
    ap.add_argument('--speed', type=float, default=1.0)
    ap.add_argument('--only', default='')
    ap.add_argument('--force', action='store_true')
    ap.add_argument('--sample', default=None)
    a = ap.parse_args()

    sys.path.insert(0, a.cosyvoice)
    sys.path.insert(0, os.path.join(a.cosyvoice, 'third_party', 'Matcha-TTS'))
    sys.path.insert(0, HERE)
    os.chdir(a.cosyvoice)
    import torch
    import torchaudio
    from cosyvoice.cli.cosyvoice import AutoModel
    from storyboard import SEGMENTS

    model_dir = os.path.join(a.cosyvoice, a.model)
    model = AutoModel(model_dir=model_dir)
    if a.rl:
        state = torch.load(os.path.join(model_dir, 'llm.rl.pt'), map_location='cpu')
        model.model.llm.load_state_dict(state, strict=True)
    prompt_wav = a.prompt_wav or os.path.join(a.cosyvoice, 'asset', 'zero_shot_prompt.wav')
    os.makedirs(OUT, exist_ok=True)

    def say(text, path):
        chunks = [j['tts_speech'] for j in model.inference_zero_shot(text, SYSTEM + a.prompt_text, prompt_wav,
                                                                     stream=False, speed=a.speed)]
        speech = torch.cat(chunks, dim=1)
        torchaudio.save(path, speech, model.sample_rate)
        return speech.shape[1] / model.sample_rate

    if a.sample:
        path = os.path.join(OUT, 'sample_rl.wav' if a.rl else 'sample.wav')
        print(path, round(say(a.sample, path), 2))
        return

    meta_path = os.path.join(OUT, 'durations.json')
    old = json.load(open(meta_path, encoding='utf-8')) if os.path.exists(meta_path) else {}
    durations = dict(old)
    only = set(filter(None, a.only.split(',')))
    voice = f'{os.path.basename(prompt_wav)}|{a.prompt_text}|rl={a.rl}|{a.speed}'
    for seg in SEGMENTS:
        if only and seg['id'] not in only:
            continue
        for i, text in enumerate(seg['lines']):
            name = f"{seg['id']}_{i}"
            path = os.path.join(OUT, name + '.wav')
            key = f'{voice}|{text}'
            if not a.force and os.path.exists(path) and old.get(name, {}).get('key') == key:
                continue
            durations[name] = {'key': key, 'seconds': round(say(text, path), 3)}
            print(name, durations[name]['seconds'], flush=True)
            json.dump(durations, open(meta_path, 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
    total = sum(d['seconds'] for d in durations.values())
    print(f'{len(durations)} lines, {total:.1f}s of speech')


if __name__ == '__main__':
    main()
