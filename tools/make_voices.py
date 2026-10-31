"""
Makes the soldiers' voices: British, Norwegian and German men acknowledging
orders, shouting warnings and crying out when hit. The voices are spoken by
Piper, a free text-to-speech program that runs on your own computer, then
roughened to sound shouted outdoors, and written as small OGG files in
assets/voices/<nation>/. A list of them goes in assets/voices/voices.json,
which the game reads to know what each man can say.

    pip install piper-tts numpy scipy
    python3 tools/make_voices.py

The first run downloads the voice models (about 300 MB) from Piper's GitHub
releases into tools/.voice-models/, which git ignores. Needs ffmpeg.

To change what the men say, edit LINES below and run the script again.

Voice models and their licences (see assets/voices/LICENSES.md):

    British    LibriTTS (high), speakers picked for low, male voices, spoken
               with British English pronunciation. Data: CC BY 4.0, from
               LibriTTS by Zen et al., built on LibriVox recordings.
    Norwegian  talesyntese (medium), from the National Library of Norway's
               Sprakbanken. Data: CC0.
    German     Thorsten (low), by Thorsten Mueller. Data: CC0.
               Karlsson (low), from the M-AILABS Speech Dataset. Data: free
               to use, see the M-AILABS licence.
"""
import io
import json
import os
import subprocess
import tarfile
import urllib.request
import wave

import numpy as np
from scipy import signal

HERE = os.path.dirname(os.path.abspath(__file__))
MODELS = os.path.join(HERE, ".voice-models")
OUT = os.path.join(HERE, "..", "assets", "voices")
RELEASE = "https://github.com/rhasspy/piper/releases/download/v0.0.2/voice-{}.tar.gz"
RATE = 22050
rng = np.random.default_rng(1940)

# Each man has a voice model, a speaker in it, a pitch shift (1.0 = none) and
# how fast he talks (smaller is faster).
VOICES = {
    "british": [
        ("en-us-libritts-high", 42, 1.00, 0.85),
        ("en-us-libritts-high", 135, 1.00, 0.85),
        ("en-us-libritts-high", 342, 1.00, 0.85),
        ("en-us-libritts-high", 543, 1.00, 0.85),
    ],
    "norwegian": [
        ("no-talesyntese-medium", None, 1.00, 0.85),
        ("no-talesyntese-medium", None, 0.93, 0.80),
        ("no-talesyntese-medium", None, 1.07, 0.88),
    ],
    "german": [
        ("de-thorsten-low", None, 1.00, 0.85),
        ("de-karlsson-low", None, 1.00, 0.85),
        ("de-thorsten-low", None, 0.92, 0.82),
        ("de-karlsson-low", None, 1.08, 0.88),
    ],
}

# The LibriTTS model speaks American; this makes it pronounce British English.
ESPEAK = {"en-us-libritts-high": "en"}

# What each man can say, by what is happening. The game picks one at random.
#   ack_*      answering an order (move, move fast, sneak, fire, defend, hide, dig in, stop)
#   contact    first sight of the enemy
#   incoming   shells or bombs about to land nearby
#   aircraft   enemy aircraft overhead
#   pinned     under heavy fire, heads down
#   panic      breaking and running
#   wounded    just hit
#   man_down   a man in his team has been killed
#   no_ammo    out of ammunition
#   surrender  giving up
LINES = {
    "british": {
        "ack_move": ["Right, sir.", "Moving now.", "Come on, lads, follow me."],
        "ack_fast": ["At the double!", "Double time, lads, go!", "Keep moving, quick!"],
        "ack_sneak": ["Keep low, lads.", "Quietly now.", "Heads down and follow me."],
        "ack_fire": ["Fire!", "Open fire!", "Rapid fire!"],
        "ack_defend": ["Hold here, lads.", "We'll hold this.", "Watch your front."],
        "ack_hide": ["Get down and keep still.", "Down! Stay hidden.", "Into cover, lads."],
        "ack_dig": ["Dig in!", "Get your shovels out, lads.", "Start digging, quickly now."],
        "ack_stop": ["Halt!", "Right, wait here."],
        "contact": ["Enemy in front!", "Jerry, over there!", "Movement, sir!"],
        "incoming": ["Incoming!", "Take cover!", "Shells!"],
        "aircraft": ["Aircraft! Get down!", "Here comes Jerry!", "Stukas!"],
        "pinned": ["Keep your heads down!", "We're pinned down!", "Get down, get down!"],
        "panic": ["Get out of here!", "Fall back! Fall back!", "Run for it!"],
        "wounded": ["I'm hit!", "Stretcher bearer!", "Oh God, my leg!", "Help me!"],
        "man_down": ["Man down!", "He's dead, sir.", "They got him!"],
        "no_ammo": ["I'm out of ammo!", "Need more rounds!"],
        "surrender": ["Don't shoot! We surrender!", "All right, we give up!"],
    },
    "norwegian": {
        "ack_move": ["Ja vel!", "Vi går.", "Følg meg, gutter."],
        "ack_fast": ["Fort, fort!", "Spring!", "Kom igjen, fort!"],
        "ack_sneak": ["Hold dere lavt.", "Stille nå.", "Kryp lavt og følg meg."],
        "ack_fire": ["Skyt!", "Ild!", "Fritt fram, skyt!"],
        "ack_defend": ["Vi holder her.", "Hold stillingen.", "Følg med framover."],
        "ack_hide": ["Ned! Hold dere skjult.", "I dekning, gutter.", "Ligg stille."],
        "ack_dig": ["Grav dere ned!", "Fram med spadene.", "Grav, fort!"],
        "ack_stop": ["Holdt!", "Vent her."],
        "contact": ["Fienden, der borte!", "Tyskere foran oss!", "Bevegelse der!"],
        "incoming": ["Granater!", "I dekning!", "Ned, ned!"],
        "aircraft": ["Fly! Ned!", "Flyalarm!", "Tyske fly!"],
        "pinned": ["Hold hodet nede!", "Vi kommer ikke fram!", "Ligg nede!"],
        "panic": ["Trekk tilbake!", "Løp, gutter!", "Vekk herfra!"],
        "wounded": ["Jeg er truffet!", "Sanitet!", "Hjelp meg!", "Å, beinet mitt!"],
        "man_down": ["Han er falt!", "De tok ham!", "En mann er skutt!"],
        "no_ammo": ["Jeg er tom for patroner!", "Trenger mer ammunisjon!"],
        "surrender": ["Ikke skyt! Vi overgir oss!", "Ikke skyt!"],
    },
    "german": {
        "ack_move": ["Jawohl!", "Vorwärts, Männer!", "Mir nach!"],
        "ack_fast": ["Marsch, marsch!", "Los, los, schneller!", "Sprung auf, marsch, marsch!"],
        "ack_sneak": ["Leise, Männer.", "Kopf unten bleiben.", "Langsam vorgehen."],
        "ack_fire": ["Feuer!", "Feuer frei!", "Schießen!"],
        "ack_defend": ["Stellung halten!", "Wir halten hier.", "Augen nach vorne."],
        "ack_hide": ["Volle Deckung!", "In Deckung, und still!", "Hinlegen!"],
        "ack_dig": ["Eingraben!", "Spaten raus!", "Grabt euch ein, schnell!"],
        "ack_stop": ["Halt!", "Hier warten."],
        "contact": ["Feind in Sicht!", "Da drüben!", "Feind voraus!"],
        "incoming": ["Granaten!", "Deckung!", "Achtung, Granaten!"],
        "aircraft": ["Flieger! Deckung!", "Fliegeralarm!", "Flieger!"],
        "pinned": ["Köpfe runter!", "Wir sind festgenagelt!", "Runter, runter!"],
        "panic": ["Zurück! Zurück!", "Weg hier!", "Rette sich, wer kann!"],
        "wounded": ["Ich bin getroffen!", "Sanitäter!", "Hilfe!", "Mein Bein!"],
        "man_down": ["Er ist tot!", "Sie haben ihn erwischt!", "Mann getroffen!"],
        "no_ammo": ["Keine Munition mehr!", "Munition!"],
        "surrender": ["Nicht schießen! Wir ergeben uns!", "Nicht schießen!"],
    },
}

# Lines said in pain come out slower, lower and shakier.
HURT = {"wounded"}


def model_path(name):
    onnx = os.path.join(MODELS, name, f"{name}.onnx")
    if not os.path.exists(onnx):
        os.makedirs(os.path.join(MODELS, name), exist_ok=True)
        print(f"downloading {name} ...")
        with urllib.request.urlopen(RELEASE.format(name)) as r:
            data = r.read()
        with tarfile.open(fileobj=io.BytesIO(data)) as tar:
            for m in tar.getmembers():
                if m.isfile() and os.path.basename(m.name) and ".." not in m.name:
                    m.name = os.path.basename(m.name)
                    tar.extract(m, os.path.join(MODELS, name))
    return onnx


_loaded = {}


def voice(name):
    from piper import PiperVoice
    if name not in _loaded:
        v = PiperVoice.load(model_path(name))
        if name in ESPEAK:
            v.config.espeak_voice = ESPEAK[name]
        _loaded[name] = v
    return _loaded[name]


def speak(model, speaker, text, length_scale):
    from piper import SynthesisConfig
    v = voice(model)
    buf = io.BytesIO()
    with wave.open(buf, "wb") as w:
        v.synthesize_wav(text, w, syn_config=SynthesisConfig(
            speaker_id=speaker, length_scale=length_scale, noise_scale=0.75, noise_w_scale=0.9))
    buf.seek(0)
    with wave.open(buf) as w:
        x = np.frombuffer(w.readframes(w.getnframes()), np.int16) / 32768
        rate = w.getframerate()
    return signal.resample_poly(x, RATE, rate) if rate != RATE else x


def shift(x, factor):
    """Raise (above 1) or lower the pitch, which also speeds or slows him a little."""
    if abs(factor - 1) < 1e-3:
        return x
    up, down = int(round(1000 / factor)), 1000
    return signal.resample_poly(x, up, down)


def shake(x, depth=0.035, rate=6.0):
    """A tremble in the voice: wobble the pitch slightly."""
    n = len(x)
    wob = 1 + depth * np.sin(2 * np.pi * rate * np.arange(n) / RATE + rng.uniform(0, 6))
    pos = np.cumsum(wob)
    pos = pos / pos[-1] * (n - 1)
    return np.interp(pos, np.arange(n), x)


def shout(x):
    """
    Make calm speech sound like a man shouting outdoors: cut the low rumble,
    push the 1 to 4 kHz range where a strained voice sits, drive it a little
    into distortion, and add a short echo of open ground.
    """
    b, a = signal.butter(2, 180, "high", fs=RATE)
    x = signal.lfilter(b, a, x)
    b, a = signal.butter(2, [1000, 4000], "band", fs=RATE)
    x = x + signal.lfilter(b, a, x) * 1.2
    x = x / (np.max(np.abs(x)) + 1e-9)
    x = np.tanh(x * 2.2) / np.tanh(2.2)
    tail = int(0.6 * RATE)
    ir = rng.standard_normal(tail) * np.exp(-np.arange(tail) / RATE * 9)
    b, a = signal.butter(2, 1800, "low", fs=RATE)
    ir = signal.lfilter(b, a, ir)
    ir[: int(0.02 * RATE)] = 0
    wet = signal.fftconvolve(x, ir)
    wet = wet / (np.max(np.abs(wet)) + 1e-9)
    out = np.concatenate([x, np.zeros(tail - 1)]) + wet * 0.12
    return out


def trim(x, floor=0.01):
    loud = np.nonzero(np.abs(x) > np.max(np.abs(x)) * floor)[0]
    if len(loud) == 0:
        return x
    x = x[max(0, loud[0] - int(0.01 * RATE)): loud[-1] + int(0.05 * RATE)].copy()
    f = min(len(x), int(0.03 * RATE))
    x[-f:] *= np.linspace(1, 0, f)
    return x


def write_ogg(path, x):
    x = x / (np.max(np.abs(x)) + 1e-9) * 0.9
    pcm = (x * 32767).astype(np.int16).tobytes()
    subprocess.run(
        ["ffmpeg", "-y", "-loglevel", "error", "-f", "s16le", "-ar", str(RATE), "-ac", "1", "-i", "-",
         "-c:a", "libvorbis", "-q:a", "3", path],
        input=pcm, check=True)


def main():
    manifest = {}
    for nation, lines in LINES.items():
        folder = os.path.join(OUT, nation)
        os.makedirs(folder, exist_ok=True)
        for old in os.listdir(folder):
            if old.endswith(".ogg") or old.endswith(".ogg.import"):
                os.remove(os.path.join(folder, old))
        manifest[nation] = []
        for man, (model, speaker, pitch, pace) in enumerate(VOICES[nation]):
            said = {}
            for event, texts in lines.items():
                files = []
                for k, text in enumerate(texts):
                    hurt = event in HURT
                    x = speak(model, speaker, text, pace * (1.25 if hurt else 1.0))
                    x = shift(x, pitch * (0.96 if hurt else 1.0))
                    if hurt:
                        x = shake(x)
                    x = trim(shout(trim(x)))
                    name = f"{event}_{k + 1}_man{man + 1}.ogg"
                    write_ogg(os.path.join(folder, name), x)
                    files.append(f"res://assets/voices/{nation}/{name}")
                said[event] = files
            manifest[nation].append(said)
            print(f"{nation} man {man + 1}: {sum(len(v) for v in said.values())} lines")
    with open(os.path.join(OUT, "voices.json"), "w", encoding="utf-8") as f:
        json.dump({"_help": "Made by tools/make_voices.py. For each nation, a list of men; "
                            "for each man, what he can say when something happens.",
                   **manifest}, f, ensure_ascii=False, indent=1)


if __name__ == "__main__":
    main()
