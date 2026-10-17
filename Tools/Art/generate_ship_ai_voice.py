"""Generate the ship AI's voiced lines for the tutorial wing's wake-up (TutorialDirector.WakeUpLines).

Each line is spoken by the Windows text-to-speech engine (System.Speech, "Microsoft Zira Desktop", which ships with
Windows 10/11), then turned into the ship's AI by the DSP chain below, written here from first principles:
an intercom band-pass with a presence lift, a short metallic comb resonance, a faint ring modulation, soft
saturation and a synthetic corridor reverb. Run from the Unity project root on Windows:

    python Tools/Art/generate_ship_ai_voice.py

The subtitles live in TutorialDirector.WakeUpLines; keep LINES in step with them.
"""
import subprocess
import tempfile
import wave
from pathlib import Path

import numpy as np

OUT = Path("Assets/FPS/Audio/Voice/ShipAI")
RATE = 44100
VOICE = "Microsoft Zira Desktop"
SPEECH_RATE = 0    # SAPI -10..10.
LEAD_IN = .38      # Silence before the voice, so the transmission chirp plays first.
TAIL = .35

LINES = [
    ("WakeUp_01", "Emergency officer, revival complete."),
    ("WakeUp_02", "Warning. Hostile units detected aboard the ship."),
    ("WakeUp_03", "Life support systems are stable."),
    ("WakeUp_04", "Main power is offline. Backup generators are running."),
    ("WakeUp_05", "Your vitals are weak. Stretch it out, and get the blood moving."),
]


def speak(text, path):
    quoted = text.replace("'", "''")
    script = (
        "Add-Type -AssemblyName System.Speech;"
        "$s = New-Object System.Speech.Synthesis.SpeechSynthesizer;"
        f"$s.SelectVoice('{VOICE}');"
        f"$s.Rate = {SPEECH_RATE};"
        "$f = New-Object System.Speech.AudioFormat.SpeechAudioFormatInfo("
        f"{RATE}, [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen, [System.Speech.AudioFormat.AudioChannel]::Mono);"
        f"$s.SetOutputToWaveFile('{path}', $f);"
        f"$s.Speak('{quoted}');"
        "$s.Dispose();"
    )
    subprocess.run(["powershell", "-NoProfile", "-Command", script], check=True)


def read_wav(path):
    with wave.open(str(path), "rb") as wav:
        assert wav.getnchannels() == 1 and wav.getsampwidth() == 2 and wav.getframerate() == RATE
        data = np.frombuffer(wav.readframes(wav.getnframes()), dtype="<i2")
    return data.astype(np.float64) / 32768.0


def write_wav(path, x):
    path.parent.mkdir(parents=True, exist_ok=True)
    with wave.open(str(path), "wb") as wav:
        wav.setnchannels(1)
        wav.setsampwidth(2)
        wav.setframerate(RATE)
        wav.writeframes((np.clip(x, -1, 1) * 32767).round().astype("<i2").tobytes())


def trim(x, threshold=.01):
    loud = np.flatnonzero(np.abs(x) > threshold)
    if loud.size == 0:
        return x
    pad = int(.02 * RATE)
    return x[max(0, loud[0] - pad):loud[-1] + pad]


def convolve(x, h):
    n = len(x) + len(h) - 1
    size = 1 << (n - 1).bit_length()
    return np.fft.irfft(np.fft.rfft(x, size) * np.fft.rfft(h, size), size)[:n]


def intercom_eq(x):
    """Band-pass 220 Hz..6.5 kHz (2nd-order slopes) with a +4 dB lift around 2.4 kHz, applied in the spectrum."""
    size = 1 << (len(x) - 1).bit_length()
    f = np.fft.rfftfreq(size, 1 / RATE)
    f[0] = 1e-3
    high_pass = (f / 220) ** 2 / np.sqrt(1 + (f / 220) ** 4)
    low_pass = 1 / np.sqrt(1 + (f / 6500) ** 4)
    presence = 1 + .58 * np.exp(-0.5 * (np.log2(f / 2400) / .6) ** 2)
    return np.fft.irfft(np.fft.rfft(x, size) * high_pass * low_pass * presence, size)[:len(x)]


def comb(x, delay_ms=4.3, feedback=.32, mix=.35):
    """A short feedback comb: the hollow ring of a voice through a metal grille."""
    d = int(RATE * delay_ms / 1000)
    y = x.copy()
    for start in range(d, len(y), d):  # block-wise recursion: y[n] += feedback * y[n - d]
        end = min(start + d, len(y))
        y[start:end] += feedback * y[start - d:end - d]
    return (1 - mix) * x + mix * y


def ring_mod(x, hz=62, mix=.1):
    t = np.arange(len(x)) / RATE
    return (1 - mix) * x + mix * x * np.sin(2 * np.pi * hz * t)


def saturate(x, drive=1.8):
    return np.tanh(drive * x) / np.tanh(drive)


def corridor_reverb(x, seconds=.55, wet=.16, seed=307):
    """A synthetic impulse response: a few discrete early reflections, then a decaying noise tail."""
    rng = np.random.default_rng(seed)
    n = int(seconds * RATE)
    t = np.arange(n) / RATE
    h = rng.standard_normal(n) * np.exp(-t * 6.9 / seconds) * .05
    h[:int(.012 * RATE)] = 0
    for ms, gain in ((11, .45), (17, .32), (23, .27), (31, .2), (43, .14)):
        h[int(ms / 1000 * RATE)] += gain
    # Darken the tail a little so it reads as a room, not hiss.
    h = convolve(h, np.ones(6) / 6)[:n]
    tail = convolve(x, h)
    dry = np.concatenate([x, np.zeros(len(tail) - len(x))])
    return (1 - wet) * dry + wet * tail / np.max(np.abs(tail)) * np.max(np.abs(x))


def ship_ai(x):
    x = trim(x)
    x = intercom_eq(x)
    x = comb(x)
    x = ring_mod(x)
    x = x / np.max(np.abs(x)) * .8
    x = saturate(x)
    x = corridor_reverb(x)
    fade = int(.15 * RATE)
    x[-fade:] *= np.linspace(1, 0, fade)
    x = np.concatenate([np.zeros(int(LEAD_IN * RATE)), x, np.zeros(int(TAIL * RATE))])
    return x / np.max(np.abs(x)) * .89  # -1 dBFS peak


def main():
    with tempfile.TemporaryDirectory() as tmp:
        for name, text in LINES:
            raw = Path(tmp) / f"{name}.wav"
            speak(text, raw)
            out = OUT / f"VO_ShipAI_{name}.wav"
            y = ship_ai(read_wav(raw))
            write_wav(out, y)
            print(f"{out}: {len(y) / RATE:.2f} s  \"{text}\"")


if __name__ == "__main__":
    main()
