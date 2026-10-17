"""Generate an original eight-second, seamless unstable reactor ambience loop."""
import math
import random
import struct
import wave
from pathlib import Path

RATE = 22050
SECONDS = 8
COUNT = RATE * SECONDS
OUT = Path("Assets/FPS/Audio/SFX/CentreRoom/Reactor_Unstable_Loop.wav")
rng = random.Random(307)

# All oscillators complete whole cycles per loop. The noise bed is a sum of
# random-phase harmonics and therefore also wraps without a discontinuity.
partials = [(rng.randint(240, 16000), rng.random() * math.tau, rng.uniform(.002, .009)) for _ in range(80)]
events = [(0.72, .72), (2.17, .48), (3.46, .95), (5.08, .55), (6.61, .8), (7.34, .43)]
samples = []
for i in range(COUNT):
    t = i / RATE
    hum = (.32 * math.sin(math.tau * 55 * t)
           + .11 * math.sin(math.tau * 110 * t + .4)
           + .055 * math.sin(math.tau * 165 * t + 1.1))
    slow = .85 + .15 * math.sin(math.tau * t / SECONDS * 3 + .5)
    noise = sum(a * math.sin(math.tau * k * t / SECONDS + p) for k, p, a in partials)
    crackle = sum(strength * math.exp(-((t - centre) / .045) ** 2) for centre, strength in events)
    samples.append(hum * slow + noise * (.28 + crackle * 1.9))

rms = math.sqrt(sum(x * x for x in samples) / COUNT)
gain = .1 / rms  # -20 dBFS RMS, matching the workshop ambience assets.
OUT.parent.mkdir(parents=True, exist_ok=True)
with wave.open(str(OUT), "wb") as wav:
    wav.setnchannels(1)
    wav.setsampwidth(2)
    wav.setframerate(RATE)
    wav.writeframes(b"".join(struct.pack("<h", max(-32768, min(32767, round(x * gain * 32767)))) for x in samples))
print(f"{OUT}: {COUNT} samples, peak={max(abs(x * gain) for x in samples):.3f}, rms={rms * gain:.3f}")
