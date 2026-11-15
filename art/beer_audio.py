"""Generate original PCM Foley approximations for the beer valuable; no external recordings."""
import math
import random
import struct
import wave
from pathlib import Path

RATE = 44100
OUT = Path(__file__).resolve().parents[1] / 'assets/audio'


def save(name, samples):
    peak = max(abs(v) for v in samples)
    gain = min(1.0, 0.88 / max(peak, 0.001))
    pcm = b''.join(struct.pack('<h', round(max(-1, min(1, v * gain)) * 32767)) for v in samples)
    with wave.open(str(OUT / name), 'wb') as target:
        target.setparams((1, 2, RATE, 0, 'NONE', 'not compressed'))
        target.writeframes(pcm)
    print(name, 'seconds', len(samples) / RATE, 'peak', round(peak * gain, 3))


def aluminum(seed):
    rng = random.Random(seed)
    duration = 0.32
    samples = []
    modes = [(670, 28, .24), (1183, 22, .20), (2367, 35, .13), (4297, 55, .065)]
    phases = [rng.uniform(0, math.pi) for _ in modes]
    rumble = 0.0
    for i in range(round(RATE * duration)):
        t = i / RATE
        noise = rng.uniform(-1, 1)
        rumble += (noise - rumble) * .10
        ring = sum(math.sin(2 * math.pi * f * t + phase) * math.exp(-decay * t) * level
                   for (f, decay, level), phase in zip(modes, phases))
        crumple = rumble * math.exp(-t * 30) * (.3 + .7 * abs(math.sin(t * 260)))
        hit = noise * math.exp(-t * 390) * .42
        fade = min(1, t / .0007) * min(1, (duration - t) / .03)
        samples.append((ring + crumple + hit) * fade)
    return samples


def spill():
    rng = random.Random(807)
    duration = 1.2
    droplets = [(rng.uniform(.03, .95), rng.uniform(550, 1900), rng.uniform(.025, .10), rng.uniform(.025, .08)) for _ in range(34)]
    low = 0.0
    samples = []
    for i in range(round(RATE * duration)):
        t = i / RATE
        noise = rng.uniform(-1, 1)
        low += (noise - low) * .075
        splash = (noise * .12 + low * .8) * math.exp(-t * 6.5)
        pour = low * .48 * math.exp(-t * 3.4) * (.6 + .4 * math.sin(t * 43)**2)
        bubbles = 0.0
        for start, frequency, length, volume in droplets:
            local = t - start
            if 0 <= local < length:
                # Descending bubble pitch and very short broadband droplets.
                phase = 2 * math.pi * frequency * (local - .28 * local * local / length)
                bubbles += math.sin(phase) * math.exp(-local * 8 / length) * volume
        fade = min(1, t / .002) * min(1, (duration - t) / .15)
        samples.append((splash + pour + bubbles) * fade)
    return samples


if __name__ == '__main__':
    OUT.mkdir(parents=True, exist_ok=True)
    save('beer_hit_1.wav', aluminum(17))
    save('beer_hit_2.wav', aluminum(31))
    save('beer_spill.wav', spill())
