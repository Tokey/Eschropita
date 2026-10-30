"""Build Eschropita's sound library from the sound designer's tracks.

    python build_sound_library.py --src <folder of decoded .wav tracks> --project <Unity project root>
                                  [--analysis analysis.json]

Source tracks (by Alexander, SoundCloud: "SP SOUND LIBRARY", "Mushroom 4 Part Loops", "Mushroom Game Layered")
are expected as 44.1 kHz WAVs named after their titles (non-alphanumerics -> "_"), e.g.
"Mushroom_Game_Layered.wav", "Mushroom_4_Part_Loop_017.wav". get_tracks.sh downloads and decodes them.
If you get the original lossless files, decode them to the same names and re-run: the selections stay the same.

Outputs (overwritten):
  Assets/Overhaul/Resources/Overhaul/Audio/{music,amb,phrase,stinger,sfx,ui}/...   (.ogg beds, .wav one-shots)
  Assets/Overhaul/Resources/Overhaul/Audio/manifest.json                          (cue index read by AudioCatalog)
  Tools/SoundLibrary/SOUND_LIBRARY.md                                             (what came from where)

Everything tonal comes from the designer's material (cut, pitched into F major / D minor, layered, reverbed).
Only non-tonal sounds the library lacks are synthesized (footsteps, hiss, impacts, machinery, scanner sweeps).
Deterministic: same input -> same output.
"""
import argparse, json, os, subprocess, sys
from fractions import Fraction
import numpy as np
from scipy import signal
from scipy.io import wavfile

SR = 44100
RNG = np.random.default_rng(20261030)

# ---------------------------------------------------------------------------------------------------------------
# DSP helpers (x: float32 array, shape (n,) mono or (n, 2) stereo)
# ---------------------------------------------------------------------------------------------------------------

def load(src, name):
    sr, x = wavfile.read(os.path.join(src, name + '.wav'))
    x = x.astype(np.float32) / (32768.0 if x.dtype == np.int16 else 1.0)
    if x.ndim == 1:
        x = np.stack([x, x], 1)
    if sr != SR:
        x = resample(x, SR / sr)
    return x


def load_file(path):
    sr, x = wavfile.read(path)
    x = x.astype(np.float32) / (32768.0 if x.dtype == np.int16 else 1.0)
    if x.ndim == 1:
        x = np.stack([x, x], 1)
    if sr != SR:
        x = resample(x, SR / sr)
    return x


def resample(x, ratio):
    """Change sample count by ratio (ratio > 1 = longer)."""
    fr = Fraction(ratio).limit_denominator(200)
    return signal.resample_poly(x, fr.numerator, fr.denominator, axis=0).astype(np.float32)


def pitch(x, semitones):
    """Sampler-style pitch shift (duration changes with pitch)."""
    if abs(semitones) < 1e-3:
        return x.copy()
    return resample(x, 2 ** (-semitones / 12.0))


def stretch_pitch_keep(x, semitones):
    """Pitch shift keeping duration (resample + overlap-add time stretch). Good enough for pads."""
    y = pitch(x, semitones)
    return time_stretch(y, len(x) / max(1, len(y)))


def time_stretch(x, factor, win=4096):
    """Simple windowed overlap-add stretch (factor > 1 = longer). For pads/drones only."""
    mono_in = x.ndim == 1
    if mono_in:
        x = x[:, None]
    hop_out = win // 4
    hop_in = hop_out / factor
    w = np.hanning(win).astype(np.float32)
    n_out = int(len(x) * factor)
    out = np.zeros((n_out + win, x.shape[1]), np.float32)
    norm = np.zeros(n_out + win, np.float32)
    pos_out = 0
    pos_in = 0.0
    while pos_out < n_out and int(pos_in) + win < len(x):
        seg = x[int(pos_in):int(pos_in) + win] * w[:, None]
        out[pos_out:pos_out + win] += seg
        norm[pos_out:pos_out + win] += w
        pos_out += hop_out
        pos_in += hop_in
    norm[norm < 1e-3] = 1.0
    out = out / norm[:, None]
    out = out[:n_out]
    return out[:, 0] if mono_in else out


def seg(x, t0, t1):
    return x[int(t0 * SR):int(t1 * SR)].copy()


def fade(x, fin=0.008, fout=0.02):
    x = x.copy()
    n = len(x)
    a, b = min(n // 2, int(fin * SR)), min(n // 2, int(fout * SR))
    shape = (slice(None),) + ((None,) if x.ndim == 2 else ())
    if a > 0:
        x[:a] *= np.linspace(0, 1, a, dtype=np.float32)[shape]
    if b > 0:
        x[n - b:] *= (np.linspace(1, 0, b, dtype=np.float32) ** 1.5)[shape]
    return x


def trim(x, lead_db=-40.0, tail_db=-62.0, pre=0.003, post=0.06):
    """Remove leading / trailing near-silence (relative to the peak), keeping a little pre-roll and tail."""
    m = np.abs(to_mono(x))
    pk = m.max() + 1e-9
    above = np.nonzero(m > pk * 10 ** (lead_db / 20))[0]
    if len(above) == 0:
        return x
    i0 = max(0, above[0] - int(pre * SR))
    tail = np.nonzero(m > pk * 10 ** (tail_db / 20))[0]
    i1 = min(len(x), tail[-1] + int(post * SR))
    return x[i0:i1]


def to_mono(x):
    return x.mean(1) if x.ndim == 2 else x


def to_stereo(x):
    return np.stack([x, x], 1) if x.ndim == 1 else x


def butter(x, kind, f, order=2):
    if isinstance(f, (list, tuple)):
        wn = [fi / (SR / 2) for fi in f]
    else:
        wn = f / (SR / 2)
    sos = signal.butter(order, wn, btype=kind, output='sos')
    return signal.sosfilt(sos, x, axis=0).astype(np.float32)


def reverb(x, seconds=1.8, wet=0.3, pre=0.012, bright=6000):
    """Exponentially decaying decorrelated-noise convolution reverb."""
    st = to_stereo(x)
    n = int(seconds * SR)
    t = np.arange(n) / SR
    env = np.exp(-6.9 * t / seconds).astype(np.float32)
    irs = []
    for ch in range(2):
        noise = RNG.standard_normal(n).astype(np.float32) * env
        noise = butter(noise, 'low', bright)
        noise[:int(pre * SR)] = 0
        noise /= np.sqrt(np.sum(noise ** 2)) + 1e-9
        irs.append(noise)
    tail = np.zeros((len(st) + n - 1, 2), np.float32)
    for ch in range(2):
        tail[:, ch] = signal.fftconvolve(st[:, ch], irs[ch])
    out = np.zeros_like(tail)
    out[:len(st)] = st * (1 - wet)
    out += tail * wet * 1.4
    return out


def mix(*parts):
    """mix((x, offset_seconds, gain), ...) -> stereo sum."""
    length = max(int(off * SR) + len(p) for p, off, g in parts)
    out = np.zeros((length, 2), np.float32)
    for p, off, g in parts:
        p = to_stereo(p)
        i = int(off * SR)
        out[i:i + len(p)] += p * g
    return out


def rms_db(x, gate=-50):
    m = to_mono(x)
    hop = int(0.05 * SR)
    if len(m) < hop:
        return 20 * np.log10(np.sqrt(np.mean(m ** 2)) + 1e-9)
    fr = m[:len(m) // hop * hop].reshape(-1, hop)
    e = np.sqrt((fr ** 2).mean(1) + 1e-12)
    edb = 20 * np.log10(e)
    act = e[edb > gate]
    if len(act) == 0:
        act = e
    return 20 * np.log10(np.sqrt(np.mean(act ** 2)) + 1e-9)


def normalize(x, target_rms_db, peak_db=-1.0):
    g = 10 ** ((target_rms_db - rms_db(x)) / 20)
    y = x * g
    pk = np.abs(y).max() + 1e-9
    lim = 10 ** (peak_db / 20)
    if pk > lim:
        # soft-knee: compress only the overs
        y = np.where(np.abs(y) > lim * 0.7, np.sign(y) * (lim * 0.7 + (lim * 0.3) * np.tanh((np.abs(y) - lim * 0.7) / (lim * 0.3))), y)
        y *= min(1.0, lim / (np.abs(y).max() + 1e-9))
    return y.astype(np.float32)


def loopify(x, xf=2.0):
    """Seamless loop: crossfade the last xf seconds into the start (equal power). Output is xf shorter."""
    n = int(xf * SR)
    if len(x) <= 2 * n:
        return x
    head, body, tail = x[:n], x[n:len(x) - n], x[len(x) - n:]
    t = np.linspace(0, np.pi / 2, n, dtype=np.float32)
    shape = (slice(None),) + ((None,) if x.ndim == 2 else ())
    blend = tail * np.cos(t)[shape] + head * np.sin(t)[shape]
    return np.concatenate([body, blend])  # body ends at tail start; blend leads back into body start


def env_adsr(n, a=0.005, d=0.1, s=0.0, r=0.2, sus_time=0.0):
    t = np.arange(n) / SR
    e = np.zeros(n, np.float32)
    e[t < a] = t[t < a] / max(a, 1e-6)
    m = (t >= a) & (t < a + d)
    e[m] = 1 - (1 - s) * (t[m] - a) / max(d, 1e-6)
    m = (t >= a + d) & (t < a + d + sus_time)
    e[m] = s
    m = t >= a + d + sus_time
    e[m] = s * np.exp(-(t[m] - a - d - sus_time) / max(r, 1e-6) * 4) if s > 0 else 0
    return e


def sine(freq, dur, phase=0.0):
    t = np.arange(int(dur * SR)) / SR
    if callable(freq):
        f = freq(t)
        ph = 2 * np.pi * np.cumsum(f) / SR + phase
        return np.sin(ph).astype(np.float32)
    return np.sin(2 * np.pi * freq * t + phase).astype(np.float32)


def noise(dur, color='white'):
    n = int(dur * SR)
    w = RNG.standard_normal(n).astype(np.float32)
    if color == 'brown':
        w = np.cumsum(w)
        w = butter(w - np.convolve(w, np.ones(2048) / 2048, 'same'), 'high', 20)
        w /= np.abs(w).max() + 1e-9
    elif color == 'pink':
        w = butter(w, 'low', 1200, 1) * 0.6 + w * 0.15
    return w.astype(np.float32)


# ---------------------------------------------------------------------------------------------------------------
# analysis helpers
# ---------------------------------------------------------------------------------------------------------------

NOTE_F = {'F4': 349.23, 'G4': 392.0, 'A4': 440.0, 'Bb4': 466.16, 'C5': 523.25, 'D5': 587.33, 'E5': 659.26,
          'F5': 698.46, 'G5': 783.99, 'A5': 880.0, 'C6': 1046.5, 'D6': 1174.66, 'F6': 1396.91, 'C4': 261.63,
          'A3': 220.0, 'F3': 174.61, 'D4': 293.66, 'C3': 130.81, 'F2': 87.31, 'A6': 1760.0}


def dominant_freq(x, fmin=80, fmax=3000):
    m = to_mono(x)[:int(0.3 * SR)]
    if len(m) < 1024:
        return 0.0
    m = m * np.hanning(len(m))
    spec = np.abs(np.fft.rfft(m, 1 << 16))
    f = np.fft.rfftfreq(1 << 16, 1 / SR)
    band = (f > fmin) & (f < fmax)
    return float(f[band][np.argmax(spec[band])])


def semis_to(x, target_note, fmin=80, fmax=3000):
    f0 = dominant_freq(x, fmin, fmax)
    if f0 <= 0:
        return 0.0
    s = 12 * np.log2(NOTE_F[target_note] / f0)
    # shift by the nearest octave-equivalent amount when the jump is huge
    while s > 7:
        s -= 12
    while s < -14:
        s += 12
    return float(s)


def best_notes(analysis, tracks, n=8, fmin=150, fmax=2500, min_isol=12, min_decay=6, long_tail=None):
    cands = []
    for tr in tracks:
        for o in analysis[tr]['onsets']:
            if o['isol'] < min_isol or o['decay'] < min_decay or not (fmin <= o['f'] <= fmax) or o['att'] < -38:
                continue
            score = o['isol'] + 0.5 * o['decay']
            if long_tail is True:
                score = o['isol'] - abs(o['decay'] - 10)
            cands.append((score, tr, o))
    cands.sort(key=lambda c: -c[0])
    picked, used = [], []
    for sc, tr, o in cands:
        if any(tr == ut and abs(o['t'] - tt) < 1.0 for ut, tt in used):
            continue
        picked.append((tr, o))
        used.append((tr, o['t']))
        if len(picked) >= n:
            break
    return picked


def split_phrases(x, min_len=1.2, max_len=6.5):
    """Split on relative quiet: envelope below p90 - 22 dB for >= 0.2 s."""
    m = to_mono(x)
    hop = int(0.05 * SR)
    fr = m[:len(m) // hop * hop].reshape(-1, hop)
    e = 20 * np.log10(np.sqrt((fr ** 2).mean(1) + 1e-12))
    thr = np.percentile(e, 90) - 22
    quiet = e < thr
    out, i, nfr = [], 0, len(e)
    while i < nfr:
        if quiet[i]:
            i += 1
            continue
        j = i
        while j < nfr:
            if quiet[j]:
                k = j
                while k < nfr and quiet[k]:
                    k += 1
                if (k - j) * 0.05 >= 0.2:
                    break
                j = k
            else:
                j += 1
        t0, t1 = i * 0.05, min(j * 0.05 + 0.25, len(m) / SR)
        if min_len <= t1 - t0 <= max_len:
            out.append((round(max(0, t0 - 0.02), 2), round(t1, 2)))
        i = j
    return out


def phrase_features(x):
    m = to_mono(x)
    f, t, Z = signal.stft(m, SR, nperseg=2048, noverlap=1536)
    p = np.abs(Z) ** 2
    cen = (f[:, None] * p).sum(0) / (p.sum(0) + 1e-12)
    energy = p.sum(0)
    w = energy / (energy.sum() + 1e-12)
    centroid = float((cen * w).sum())
    half = len(cen) // 2
    rise = float(np.average(cen[half:], weights=w[half:] + 1e-12) - np.average(cen[:half], weights=w[:half] + 1e-12)) if half > 1 else 0.0
    low = float(p[(f < 250)].sum() / (p.sum() + 1e-12))
    flux = np.maximum(0, np.diff(np.log(np.abs(Z) + 1e-9), axis=1)).sum(0)
    flux = (flux - flux.mean()) / (flux.std() + 1e-9)
    onsets = len(signal.find_peaks(flux, height=1.5, distance=6)[0]) / (len(m) / SR)
    return {'centroid': centroid, 'rise': rise, 'low': low, 'density': onsets}


# ---------------------------------------------------------------------------------------------------------------
# output
# ---------------------------------------------------------------------------------------------------------------

class Lib:
    def __init__(self, project):
        self.root = os.path.join(project, 'Assets', 'Overhaul', 'Resources', 'Overhaul', 'Audio')
        self.cues = {}
        self.docs = []

    def write(self, rel, x, fmt):
        path = os.path.join(self.root, rel + ('.ogg' if fmt == 'ogg' else '.wav'))
        os.makedirs(os.path.dirname(path), exist_ok=True)
        x = np.clip(x, -1, 1)
        if fmt == 'wav':
            data = (x * 32767).astype(np.int16)
            wavfile.write(path, SR, data)
        else:
            tmp = path + '.tmp.wav'
            wavfile.write(tmp, SR, (x * 32767).astype(np.int16))
            subprocess.run(['ffmpeg', '-v', 'error', '-y', '-i', tmp, '-c:a', 'libvorbis', '-q:a', '5', path], check=True)
            os.remove(tmp)
        return rel

    def cue(self, cid, xs, bus, volume, source, notes='', loop=False, fmt=None, mono=None, pitch_jitter=0.0,
            max_voices=4, min_interval=0.04, min_dist=3.0, max_dist=38.0, key='', target=None):
        if not isinstance(xs, list):
            xs = [xs]
        longest = max(len(x) for x in xs) / SR
        fmt = fmt or ('ogg' if bus in ('music', 'amb', 'stinger') or longest > 2.5 else 'wav')
        if mono is None:
            mono = bus in ('sfx', 'phrase')
        tgt = target if target is not None else {'music': -18, 'amb': -22, 'phrase': -17, 'stinger': -16,
                                                 'sfx': -15, 'ui': -17}[bus]
        files = []
        for i, x in enumerate(xs):
            y = to_mono(x) if mono else to_stereo(x)
            if not loop:
                y = fade(trim(y), 0.003, 0.03)
            y = normalize(y, tgt)
            name = cid.split('.', 1)[1].replace('.', '_') + (f'_{i + 1}' if len(xs) > 1 else '')
            files.append(self.write(f"{bus if bus != 'stinger' else 'stinger'}/{name}", y, fmt))
        self.cues[cid] = {'id': cid, 'files': [f.replace('\\', '/') for f in files], 'bus': bus, 'volume': volume,
                          'loop': loop, 'pitchJitter': pitch_jitter, 'maxVoices': max_voices,
                          'minInterval': min_interval, 'minDistance': min_dist, 'maxDistance': max_dist,
                          'key': key, 'bpm': 0, 'source': source, 'notes': notes}
        dur = sum(len(x) for x in xs) / SR / len(xs)
        self.docs.append((cid, bus, len(xs), round(dur, 2), source, notes))
        print(f"  {cid:28s} {bus:7s} x{len(xs)} {dur:6.2f}s  {source[:80]}", flush=True)

    def finish(self, project, analysis_note):
        man = {'version': 1, 'cues': [self.cues[k] for k in sorted(self.cues)]}
        with open(os.path.join(self.root, 'manifest.json'), 'w', encoding='utf-8') as f:
            json.dump(man, f, indent=1)
        md = ["# Eschropita sound library", "",
              "Generated by `Tools/SoundLibrary/build_sound_library.py` from the sound designer's (Alexander's) tracks.",
              "Bus volumes are relative mix levels the AudioDirector starts from. Times are seconds in the source track.",
              "", analysis_note, "", "| cue | bus | variants | length | source | notes |", "|---|---|---|---|---|---|"]
        for cid, bus, nv, dur, src, notes in sorted(self.docs):
            md.append(f"| `{cid}` | {bus} | {nv} | {dur}s | {src} | {notes} |")
        with open(os.path.join(project, 'Tools', 'SoundLibrary', 'SOUND_LIBRARY.md'), 'w', encoding='utf-8') as f:
            f.write("\n".join(md) + "\n")


# ---------------------------------------------------------------------------------------------------------------
# the library
# ---------------------------------------------------------------------------------------------------------------

def build(src, project, analysis):
    L = Lib(project)
    T = lambda n: load(src, n)
    print("music:")
    mgl = T('Mushroom_Game_Layered')
    lead = analysis['Mushroom_Game_Layered']['lead'] if analysis else 1.0
    L.cue('music.explore', seg(mgl, lead, len(mgl) / SR), 'music', 0.8,
          'Mushroom Game Layered (whole piece)', 'main exploration theme, ~129 BPM, F major', loop=True, key='F major')
    dl = T('Distant_Layer_Delay_Mids')
    L.cue('music.layer.calm', fade(seg(dl, 0, 150), 2, 3), 'music', 0.45,
          'Distant Layer Delay Mids 0-150', 'low drone pad under exploration in quiet moments', loop=True)
    sb = T('Sub_Layer_Delay_Bass')
    L.cue('music.layer.tension', fade(seg(sb, 50, 230), 2, 3), 'music', 0.6,
          'Sub Layer Delay Bass 50-230', 'pulsing bass that creeps in with danger (storm, low life support)', loop=True)
    hn = T('Sub_Layer_On_High_Notes')
    L.cue('music.layer.wonder', fade(seg(hn, 0.85, 160), 2, 3), 'music', 0.5,
          'Sub Layer On High Notes 0.85-160', 'shimmering high chord bed for discovery, bunker, lore (played on its own)', loop=True)
    sp = T('Space_Distanc_Layer_Delay_HiLo')
    L.cue('music.layer.space', fade(seg(sp, 3.75, 142), 2, 3), 'music', 0.55,
          'Space Distanc Layer Delay HiLo 3.75-142', 'eerie bed for the surveillance obelisk', loop=True)
    sw = T('Space_Wobble_Distanc_Layer_Delay_HiLo')
    L.cue('music.layer.wobble', fade(seg(sw, 0.1, 166), 2, 3), 'music', 0.5,
          'Space Wobble Distanc Layer Delay HiLo', 'unstable pulsing layer for the jammer / obelisk core', loop=True)
    td = T('Tornado_Delay_Noise')
    L.cue('music.storm', fade(seg(td, 0.05, 110), 2, 3), 'music', 0.55,
          'Tornado Delay Noise', 'storm bed while a tornado is active', loop=True)
    th = T('Subtle_Delay_Noise_Thought')
    L.cue('music.thought', fade(seg(th, 0, 148), 2, 3), 'music', 0.5,
          'Subtle Delay Noise Thought', 'introspective bed for reading logs and the bunker', loop=True)
    grove = ['015', '006', '017', '001', '004', '012']
    for i, g in enumerate(grove):
        x = T(f'Mushroom_4_Part_Loop_{g}')
        a = analysis.get(f'Mushroom_4_Part_Loop_{g}', {}) if analysis else {}
        lead = a.get('lead', 0.0)
        L.cue(f'music.grove.{i + 1}', fade(seg(x, lead, len(x) / SR), 0.5, 2), 'music', 0.7,
              f'Mushroom 4 Part Loop {g}', f"grove playlist ({a.get('key', '?')}, F/Dm fit {a.get('fit_Fmaj_Dmin', '?')})", loop=True)
    x = T('Mushroom_4_Part_Loop_016')
    L.cue('music.report', fade(seg(x, 0, len(x) / SR), 0.3, 3), 'music', 0.75,
          'Mushroom 4 Part Loop 016', 'warm F major closing loop for the report card and ending', loop=True, key='F major')
    x = T('Mushroom_4_Part_Loop_005')
    L.cue('music.bunker', fade(seg(x, 0, len(x) / SR), 0.3, 3), 'music', 0.75,
          'Mushroom 4 Part Loop 005', 'the "something new" the Mycari play at the bunker', loop=True)

    print("ambience:")
    wind = load_file(os.path.join(project, 'Assets', 'Mars Landscape 3D', 'Resources', 'Sounds', 'Wind.wav'))
    L.cue('amb.wind', loopify(butter(wind, 'high', 60), 3.0), 'amb', 0.45, 'Mars Landscape 3D Wind.wav (project)',
          'constant thin Mars wind', loop=True)
    wl = load_file(os.path.join(project, 'Assets', 'EzTornado', 'Audio', 'windloop.wav'))
    storm = mix((butter(wl, 'low', 2600), 0, 1.0), (butter(seg(td, 20, 40.5), 'low', 900), 0, 0.5))
    L.cue('amb.storm', loopify(storm, 2.0), 'amb', 0.6, 'EzTornado windloop.wav + Tornado Delay Noise 20-40 (low-passed)',
          'heavy dust-storm wind', loop=True)
    x11 = T('Mushroom_4_Part_Loop_011')
    gro = reverb(butter(seg(x11, 10, 52), 'low', 3500), 3.0, 0.45)
    L.cue('amb.grove', loopify(gro[:int(40 * SR)], 4.0), 'amb', 0.5, 'Mushroom 4 Part Loop 011 10-52 (low-passed, long reverb)',
          'soft fungal drone around the grove (3D)', loop=True, min_dist=4, max_dist=30)
    x10 = T('Mushroom_4_Part_Loop_010')
    trip = mix((seg(x10, 19.5, 35.5), 0, 1.0), (seg(x10, 56.0, 72.0), 7.0, 0.8))
    trip = reverb(time_stretch(butter(trip, 'low', 7000), 1.6), 3.5, 0.55)
    L.cue('amb.trip', loopify(trip[:int(34 * SR)], 5.0), 'amb', 0.6,
          'Mushroom 4 Part Loop 010 19.5-35.5 + 56-72 (glide textures, 1.6x slower, long reverb)',
          'shimmering spore-trip pad that rises near Mycari and in the grove', loop=True)
    # life support: mains hum + pump rhythm (synth) with a low drone from Distant Layer for character
    t8 = 8.0
    hum = sum(sine(60 * k, t8) * (0.5 / k) for k in (1, 2, 3, 5))
    pump_env = np.zeros(int(t8 * SR), np.float32)
    for k in range(8):
        i0 = int((k * 1.0 + 0.05) * SR)
        e = env_adsr(int(0.7 * SR), 0.08, 0.25, 0.3, 0.25, 0.1)
        pump_env[i0:i0 + len(e)] += e[:max(0, min(len(e), len(pump_env) - i0))]
    pump = butter(noise(t8), 'band', (90, 700)) * pump_env * 0.8
    ls = to_stereo(hum * 0.35 + pump) + butter(seg(dl, 40, 48), 'low', 300) * 0.4
    L.cue('amb.lifesupport', ls, 'amb', 0.5, 'synth hum + pump rhythm, Distant Layer Delay Mids 40-48 (low-passed)',
          'machinery near the life-support plant (3D)', loop=True, min_dist=3, max_dist=24)
    # turbine: blade-pass whoosh (synth)
    t6 = 6.0
    tt = np.arange(int(t6 * SR)) / SR
    bp = 0.5 + 0.5 * np.cos(2 * np.pi * 1.5 * tt)
    whoosh = butter(noise(t6), 'band', (180, 1400)) * (0.25 + 0.75 * bp ** 3)
    L.cue('amb.turbine', to_stereo(whoosh.astype(np.float32)), 'amb', 0.5, 'synth (band-passed noise, blade-pass rhythm)',
          'rotor whoosh; the director pitches it with rotor speed', loop=True, min_dist=4, max_dist=34)
    sp_bright = butter(seg(sp, 115, 139), 'high', 1500)
    buzz = sum(sine(50 * k, 24.0) * (0.3 / k) for k in (1, 2, 3, 4, 6))
    L.cue('amb.obelisk', loopify(to_stereo(buzz) * 0.5 + sp_bright * 0.8, 3.0), 'amb', 0.45,
          'Space Distanc Layer Delay HiLo 115-139 (high-passed) + synth 50 Hz buzz', 'electrical air around the obelisk (3D)',
          loop=True, min_dist=4, max_dist=30)
    room = butter(noise(20.0, 'brown'), 'low', 400) * 0.5
    drips = np.zeros(int(20 * SR), np.float32)
    for k in range(9):
        i0 = int(RNG.uniform(0.5, 19.0) * SR)
        f0 = RNG.uniform(900, 1700)
        d = sine(lambda t: f0 * (1 + 0.6 * np.exp(-t * 40)), 0.25) * env_adsr(int(0.25 * SR), 0.001, 0.05, 0.0, 0.2)
        drips[i0:i0 + len(d)] += d[:len(drips) - i0] * RNG.uniform(0.15, 0.35)
    L.cue('amb.bunker', loopify(reverb(room + drips, 2.2, 0.4)[:int(20 * SR)], 2.0), 'amb', 0.5,
          'synth room tone + drips', 'damp underground room', loop=True)

    print("phrases:")
    pool = []
    for tr in ['Mushroom_4_Part_Loop_017', 'Mushroom_4_Part_Loop_001', 'Mushroom_4_Part_Loop_006',
               'Mushroom_4_Part_Loop_002', 'Mushroom_4_Part_Loop_005', 'Mushroom_4_Part_Loop_015']:
        x = T(tr)
        for t0, t1 in split_phrases(x):
            p = seg(x, t0, t1)
            ft = phrase_features(p)
            pool.append((tr, t0, t1, p, ft))
    if pool:
        cen = np.array([p[4]['centroid'] for p in pool])
        cz = (cen - cen.mean()) / (cen.std() + 1e-9)
        rise = np.array([p[4]['rise'] for p in pool]); rz = (rise - rise.mean()) / (rise.std() + 1e-9)
        low = np.array([p[4]['low'] for p in pool]); lz = (low - low.mean()) / (low.std() + 1e-9)
        den = np.array([p[4]['density'] for p in pool]); dz = (den - den.mean()) / (den.std() + 1e-9)
        used = set()

        def take(score, k=3):
            order = np.argsort(-score)
            out = []
            for i in order:
                tr, t0 = pool[i][0], pool[i][1]
                if (tr, t0) in used:
                    continue
                used.add((tr, t0))
                out.append(pool[i])
                if len(out) == k:
                    break
            return out
        fixer = take(cz + rz - 0.3 * lz)
        breaker = take(lz + dz - 0.5 * cz)
        wanderer = take(-dz + 0.3 * cz - 0.2 * lz)
        for name, picks, note in (('phrase.fixer', fixer, 'bright, rising — a blue Fixer answers'),
                                  ('phrase.breaker', breaker, 'low, punchy — a red Breaker answers'),
                                  ('phrase.wanderer', wanderer, 'sparse, floaty — a green Wanderer answers')):
            srcs = '; '.join(f"{p[0].replace('Mushroom_4_Part_Loop_', 'Loop ')} {p[1]:.2f}-{p[2]:.2f}" for p in picks)
            L.cue(name, [fade(p[3], 0.01, 0.25) for p in picks], 'phrase', 0.75, srcs, note, mono=True,
                  min_dist=3, max_dist=30, max_voices=2, min_interval=0.4)

    print("stingers:")
    L.cue('stinger.solved', fade(seg(mgl, 87.6, 95.0), 0.05, 2.5), 'stinger', 0.85,
          'Mushroom Game Layered 87.6-95.0', 'the theme blooming after its breakdown — the grove lights up')
    x2 = T('Mushroom_4_Part_Loop_002')
    ph2 = split_phrases(x2, 2.0, 8.0)
    pick2 = ph2[0] if ph2 else (0.0, 4.0)
    L.cue('stinger.repaired', fade(reverb(seg(x2, *pick2), 2.0, 0.25), 0.01, 1.0), 'stinger', 0.8,
          f'Mushroom 4 Part Loop 002 {pick2[0]}-{pick2[1]} (+ reverb)', 'warm resolve when a site is fully repaired')
    L.cue('stinger.discovery', fade(seg(hn, 0.85, 7.5), 0.6, 2.0), 'stinger', 0.7,
          'Sub Layer On High Notes 0.85-7.5', 'something new found (lore log, bunker, obelisk)')
    danger = mix((fade(seg(td, 0.05, 5.0), 1.2, 1.5), 0, 0.9), (butter(seg(sb, 60.0, 65.0), 'low', 220), 0.3, 1.0))
    L.cue('stinger.danger', danger, 'stinger', 0.75, 'Tornado Delay Noise 0-5 + Sub Layer Delay Bass 60-65 (low-passed)',
          'storm forming / life support low')
    x8 = T('Mushroom_4_Part_Loop_008')
    L.cue('stinger.shield', fade(reverb(seg(x8, 0.0, 3.2), 1.6, 0.3), 0.01, 0.8), 'stinger', 0.75,
          'Mushroom 4 Part Loop 008 0-3.2 (+ reverb)', 'shard shield clicks onto the turbine')
    L.cue('stinger.obelisk_down', fade(seg(sw, 32.5, 40.0), 0.3, 2.5), 'stinger', 0.85,
          'Space Wobble Distanc Layer Delay HiLo 32.5-40', 'the obelisk falls — release and awe')
    L.cue('stinger.paths', fade(reverb(seg(x11, 3.0, 9.5), 2.5, 0.35), 1.0, 2.0), 'stinger', 0.75,
          'Mushroom 4 Part Loop 011 3.0-9.5 (+ reverb)', 'light paths spread across the ground')
    x16 = T('Mushroom_4_Part_Loop_016')
    L.cue('stinger.ending', fade(seg(x16, len(x16) / SR - 9.0, len(x16) / SR), 0.2, 3.0), 'stinger', 0.85,
          'Mushroom 4 Part Loop 016 (last 9 s)', 'report card shown')

    print("tonal one-shots:")
    # Three source notes chosen by onset analysis (isolated, tonally pure, high register; see notes.py):
    PICKS = {'bell': ('Mushroom_4_Part_Loop_001', 35.352, 987.2, 1.4),    # B5, long ringing tail
             'pluck': ('Mushroom_4_Part_Loop_016', 70.298, 1397.0, 0.6),  # F6, crisp fast decay
             'soft': ('Mushroom_4_Part_Loop_005', 11.645, 656.1, 1.0)}    # E5, soft mallet
    F0 = {}

    def pick(kind, fin, fout):
        tr, t0, f0, length = PICKS[kind]
        raw = to_mono(seg(T(tr), max(0.0, t0 - 0.03), t0 + length))
        x = fade(trim(raw, -30.0, -90.0, 0.002, 0.0), fin, fout)
        F0[id(x)] = f0
        name = ['C', 'C#', 'D', 'D#', 'E', 'F', 'F#', 'G', 'G#', 'A', 'A#', 'B']
        m = int(round(69 + 12 * np.log2(f0 / 440)))
        return x, f"{tr.replace('Mushroom_4_Part_Loop_', 'Loop ')} @{t0:.2f}s ({name[m % 12]}{m // 12 - 1})"

    bell, bell_src = pick('bell', 0.002, 0.4)
    pluck, pluck_src = pick('pluck', 0.002, 0.2)
    soft, soft_src = pick('soft', 0.002, 0.3)

    def at(x, note, fmin=80, fmax=3000):
        f0 = F0.get(id(x)) or dominant_freq(x, fmin, fmax)
        return pitch(x, 12 * np.log2(NOTE_F[note] / f0)) if f0 > 0 else x.copy()

    def arp(x, notes_, gap):
        return mix(*[(at(x, n), i * gap, 1.0 - 0.12 * i) for i, n in enumerate(notes_)])

    L.cue('ui.click', fade(at(pluck, 'C6')[:int(0.14 * SR)], 0.001, 0.05), 'ui', 0.55, pluck_src + ' -> C6', 'button press')
    L.cue('ui.hover', butter(fade(at(pluck, 'F5')[:int(0.1 * SR)], 0.001, 0.05), 'low', 3500), 'ui', 0.3, pluck_src + ' -> F5 (soft)', 'hover')
    L.cue('ui.open', arp(pluck, ['C5', 'G5'], 0.07), 'ui', 0.55, pluck_src + ' -> C5, G5', 'panel opens')
    L.cue('ui.close', arp(pluck, ['G5', 'C5'], 0.07), 'ui', 0.5, pluck_src + ' -> G5, C5', 'panel closes')
    L.cue('ui.toast', reverb(at(bell, 'A5'), 1.0, 0.2), 'ui', 0.5, bell_src + ' -> A5 (+ reverb)', 'notification')
    L.cue('ui.objective', reverb(arp(bell, ['C5', 'F5'], 0.12), 1.2, 0.2), 'ui', 0.6, bell_src + ' -> C5, F5', 'new objective')
    L.cue('ui.checkpoint', reverb(mix((at(soft, 'F4'), 0, 0.8), (at(soft, 'A4'), 0.04, 0.7), (at(soft, 'C5'), 0.08, 0.7)), 1.5, 0.25),
          'ui', 0.55, soft_src + ' -> F4 A4 C5 chord', 'checkpoint saved')
    L.cue('ui.gameover', reverb(mix((at(soft, 'F3'), 0, 1.0), (at(soft, 'C3'), 0.35, 0.9)), 2.5, 0.35),
          'ui', 0.7, soft_src + ' -> F3, C3 (+ reverb)', 'life support depleted / rollback')
    L.cue('ui.blip.daffodil', [fade(at(pluck, n)[:int(0.05 * SR)], 0.001, 0.02) for n in ('C6', 'D6', 'F6', 'A6')], 'ui', 0.28,
          pluck_src + ' -> C6 D6 F6 A6 (50 ms)', 'chirpy digital text blips (Daffodil)', max_voices=2, min_interval=0.03)
    L.cue('ui.blip.eschropita', [fade(at(soft, n)[:int(0.075 * SR)], 0.002, 0.03) for n in ('F5', 'G5', 'A5')], 'ui', 0.28,
          soft_src + ' -> F5 G5 A5 (75 ms)', 'warm text blips (Eschropita)', max_voices=2, min_interval=0.03)
    L.cue('ui.blip.mycari', [fade(at(bell, n)[:int(0.09 * SR)], 0.002, 0.04) for n in ('D5', 'F5', 'C5')], 'ui', 0.28,
          bell_src + ' -> D5 F5 C5 (90 ms)', 'mushroom-tone text blips (Mycari / others)', max_voices=2, min_interval=0.03)
    L.cue('sfx.recruit', reverb(arp(pluck, ['F5', 'A5', 'C6'], 0.06), 1.0, 0.2), 'sfx', 0.7, pluck_src + ' -> F5 A5 C6', 'a Mycari agrees to follow')
    L.cue('sfx.decline', arp(soft, ['A4', 'F4'], 0.12), 'sfx', 0.6, soft_src + ' -> A4, F4', 'a Mycari declines')
    L.cue('sfx.dismiss', at(soft, 'C4'), 'sfx', 0.5, soft_src + ' -> C4', 'follower dismissed')
    L.cue('sfx.command.repair', reverb(arp(bell, ['C5', 'G5'], 0.1), 1.2, 0.2), 'sfx', 0.7, bell_src + ' -> C5, G5', 'repair command confirmed')
    thump = sine(lambda t: 110 * (1 + 1.5 * np.exp(-t * 30)), 0.35) * env_adsr(int(0.35 * SR), 0.002, 0.08, 0.0, 0.25)
    L.cue('sfx.command.dismantle', mix((at(soft, 'D4'), 0, 0.8), (thump, 0, 0.9)), 'sfx', 0.7, soft_src + ' -> D4 + synth thump', 'dismantle command confirmed')
    L.cue('sfx.repair.tick', [fade(at(pluck, n)[:int(0.18 * SR)], 0.001, 0.08) for n in ('A5', 'C6', 'F6')], 'sfx', 0.35,
          pluck_src + ' -> A5 C6 F6', 'sparkles while repair progresses', pitch_jitter=0.03, max_voices=3, min_interval=0.08)
    L.cue('sfx.shard.pickup', [reverb(at(bell, n), 1.2, 0.25) for n in ('F5', 'A5', 'C6')], 'sfx', 0.75,
          bell_src + ' -> F5 A5 C6', 'crystalline chime; the director climbs the triad on consecutive pickups', max_voices=3)
    rev = fade(bell[::-1].copy(), 0.3, 0.01)
    F0[id(rev)] = F0[id(bell)]
    L.cue('sfx.attune', reverb(mix((at(rev, 'F5'), 0, 0.8), (at(bell, 'C6'), len(rev) / SR - 0.02, 1.0)), 2.0, 0.35), 'sfx', 0.75,
          bell_src + ' reversed -> F5, then C6', 'spore attunement: a Mycari reveals its role')
    L.cue('sfx.mycari.leave', reverb(mix((at(rev, 'C6'), 0, 0.7), (at(bell, 'F6'), len(rev) / SR, 0.6)), 2.5, 0.45), 'sfx', 0.7,
          bell_src + ' reversed -> C6, then F6 (+ long reverb)', 'a Mycari leaves through the dome door', min_dist=4, max_dist=40)
    L.cue('sfx.lore.open', reverb(arp(pluck, ['F5', 'C6', 'F6'], 0.05), 1.0, 0.25), 'sfx', 0.6, pluck_src + ' -> F5 C6 F6', 'log terminal opens')
    L.cue('sfx.lore.close', arp(pluck, ['C6', 'F5'], 0.06), 'sfx', 0.5, pluck_src + ' -> C6, F5', 'log terminal closes')
    L.cue('sfx.pad.ripple', butter(reverb(at(rev, 'F3'), 1.5, 0.4), 'low', 1200), 'sfx', 0.35, bell_src + ' reversed -> F3 (low-passed)',
          'soft ground swell under a mushroom note', min_dist=3, max_dist=22)
    L.cue('sfx.ls.restored', reverb(arp(bell, ['F4', 'A4', 'C5', 'F5'], 0.08), 1.6, 0.3), 'sfx', 0.65, bell_src + ' -> F4 A4 C5 F5', 'life support stabilised')
    a1, a2 = at(soft, 'A4')[:int(0.32 * SR)], at(soft, 'F4')[:int(0.32 * SR)]
    alarm = np.zeros(int(1.6 * SR), np.float32)
    for k, cl in enumerate((a1, a2, a1, a2)):
        i0 = int(k * 0.4 * SR)
        c = fade(cl, 0.005, 0.1)
        alarm[i0:i0 + len(c)] += c[:len(alarm) - i0]
    L.cue('sfx.ls.alarm', alarm, 'sfx', 0.45, soft_src + ' -> A4/F4 alternating', 'soft pulsing life-support alarm (loop)', loop=True, mono=False)

    print("synthesized / processed one-shots:")
    steps = []
    for k in range(6):
        d = 0.11
        n = butter(noise(d), 'band', (RNG.uniform(400, 700), RNG.uniform(2500, 4500)))
        grains = (RNG.random(int(d * SR)) < 0.004).astype(np.float32) * RNG.uniform(0.5, 1.0)
        e = env_adsr(int(d * SR), 0.004, 0.07, 0.0, 0.05)
        low = sine(lambda t: 85 * (1 + np.exp(-t * 60)), d) * env_adsr(int(d * SR), 0.002, 0.04, 0.0, 0.03)
        steps.append((n * 0.7 + butter(grains, 'high', 2000) * 0.5) * e + low * 0.5)
    L.cue('sfx.footstep', steps, 'sfx', 0.32, 'synth (filtered noise crunch + grain clicks + thump)', 'soft regolith footsteps',
          pitch_jitter=0.08, max_voices=2, min_interval=0.12, min_dist=2, max_dist=18)
    tick = mix((butter(noise(0.03), 'high', 2500) * env_adsr(int(0.03 * SR), 0.001, 0.02, 0, 0.01), 0, 0.6),
               (fade(at(pluck, 'C6')[:int(0.06 * SR)], 0.001, 0.03), 0, 0.5))
    L.cue('sfx.interact', tick, 'sfx', 0.45, pluck_src + ' (60 ms) + noise tick', 'generic interact')
    hits = []
    for k, f0 in enumerate((95, 110, 82)):
        d = 0.9
        body = sine(lambda t, f0=f0: f0 * (1 + 0.8 * np.exp(-t * 25)), d) * env_adsr(int(d * SR), 0.002, 0.15, 0.0, 0.3)
        ring = sum(sine(f0 * r, d) * (0.25 / (j + 1)) for j, r in enumerate((5.3, 8.9, 12.7))) * env_adsr(int(d * SR), 0.001, 0.4, 0.0, 0.5)
        crack = butter(noise(0.08), 'high', 1500) * env_adsr(int(0.08 * SR), 0.001, 0.05, 0, 0.02)
        hits.append(mix((body, 0, 1.0), (ring, 0, 0.5), (crack, 0, 0.6)))
    L.cue('sfx.hit', hits, 'sfx', 0.7, 'synth (thud + metallic partials + crack)', 'Breaker strikes a structure', pitch_jitter=0.05, max_voices=3, min_interval=0.08)
    d = 1.6
    whine = sine(lambda t: 900 * np.exp(-t * 1.3) + 110, d) * env_adsr(int(d * SR), 0.01, 0.2, 0.6, 0.4, 0.9)
    clunk = sine(lambda t: 70 * (1 + np.exp(-t * 30)), 0.4) * env_adsr(int(0.4 * SR), 0.001, 0.1, 0, 0.25)
    crackle = butter(noise(1.2), 'high', 3000) * (RNG.random(int(1.2 * SR)) < 0.01) * 3
    L.cue('sfx.site.down', mix((whine, 0, 0.5), (clunk, 1.2, 1.0), (crackle, 0.1, 0.4)), 'sfx', 0.75, 'synth (falling whine + clunk + crackle)',
          'power failure', min_dist=5, max_dist=45)
    d = 2.6
    spin = butter(noise(d), 'band', (300, 2500)) * env_adsr(int(d * SR), 0.3, 1.5, 0.6, 0.6, 0.3) * 0.5
    spin_t = sine(lambda t: 120 + 700 * (1 - np.exp(-t * 1.2)), d) * env_adsr(int(d * SR), 0.2, 1.2, 0.5, 0.8, 0.4) * 0.35
    L.cue('sfx.turbine.spinup', mix((spin, 0, 1.0), (spin_t, 0, 1.0), (at(bell, 'F5'), 1.6, 0.4)), 'sfx', 0.7,
          'synth sweep + ' + bell_src + ' -> F5', 'turbine spinning back up', min_dist=5, max_dist=45)
    clicks = np.zeros(int(1.0 * SR), np.float32)
    for k in range(10):
        i0 = int((k * 0.06 + RNG.uniform(0, 0.02)) * SR)
        c = at(pluck, ['C6', 'F6', 'A5', 'D6'][k % 4])[:int(0.12 * SR)]
        c = fade(c, 0.001, 0.05) * (0.4 + 0.06 * k)
        clicks[i0:i0 + len(c)] += c[:len(clicks) - i0]
    L.cue('sfx.shield.build', reverb(clicks, 1.4, 0.3), 'sfx', 0.75, pluck_src + ' cascade (C6 F6 A5 D6)', 'shield plates clip on')
    d = 1.4
    shatter = np.zeros(int(d * SR), np.float32)
    for k in range(60):
        i0 = int(RNG.exponential(0.12) * SR)
        if i0 >= len(shatter) - 400:
            continue
        f0 = RNG.uniform(2500, 7000)
        g = sine(f0, 0.03) * env_adsr(int(0.03 * SR), 0.0005, 0.02, 0, 0.01) * RNG.uniform(0.2, 0.7)
        shatter[i0:i0 + len(g)] += g
    shatter += butter(noise(d), 'high', 2000) * env_adsr(int(d * SR), 0.001, 0.3, 0.0, 0.4) * 0.5
    L.cue('sfx.shield.break', reverb(shatter, 1.2, 0.25), 'sfx', 0.75, 'synth (glass tinkles + noise burst)', 'shield shatters')
    d = 0.4
    L.cue('sfx.scan.start', sine(lambda t: 400 + 1300 * (t / 0.4) ** 1.5, d) * env_adsr(int(d * SR), 0.01, 0.3, 0.3, 0.1) * 0.6,
          'sfx', 0.5, 'synth (rising sweep)', 'scanner on')
    ping = sine(1400, 1.0) * env_adsr(int(1.0 * SR), 0.002, 0.25, 0.0, 0.3) * 0.5 + sine(2100, 1.0) * env_adsr(int(1.0 * SR), 0.002, 0.1, 0, 0.15) * 0.2
    L.cue('sfx.scan.loop', ping, 'sfx', 0.3, 'synth (radar ping, 1 s)', 'scanner active (loop)', loop=True, mono=False)
    L.cue('sfx.scan.stop', sine(lambda t: 1500 - 1100 * (t / 0.35), 0.35) * env_adsr(int(0.35 * SR), 0.005, 0.25, 0.2, 0.08) * 0.6,
          'sfx', 0.45, 'synth (falling sweep)', 'scanner off')
    d = 1.3
    down = sine(lambda t: 800 * np.exp(-t * 2.5) + 60, d) * (0.6 + 0.4 * np.sign(np.sin(2 * np.pi * 14 * np.arange(int(d * SR)) / SR))) * env_adsr(int(d * SR), 0.005, 0.2, 0.7, 0.3, 0.8)
    L.cue('sfx.daffodil.shutdown', mix((down * 0.5, 0, 1.0), (tick, 1.25, 0.8)), 'sfx', 0.6, 'synth (tremolo power-down + click)', 'Daffodil shuts down')
    L.cue('sfx.daffodil.boot', mix((sine(lambda t: 200 + 900 * (t / 0.6), 0.6) * env_adsr(int(0.6 * SR), 0.05, 0.4, 0.3, 0.1) * 0.4, 0, 1.0),
                                   (arp(pluck, ['C5', 'F5', 'A5', 'C6'], 0.07), 0.45, 0.8)), 'sfx', 0.6,
          'synth rising sweep + ' + pluck_src + ' -> C5 F5 A5 C6', 'Daffodil boots up')
    gusts = []
    for k, (t0, t1) in enumerate(((10, 12.8), (45, 48.0), (78, 80.6))):
        g = seg(td, t0, t1)
        g = butter(g, 'band', (250, 3500)) * to_stereo(env_adsr(len(g), 0.8, 0.6, 0.6, 0.6, 0.3))[:, :1]
        gusts.append(to_mono(g))
    L.cue('sfx.tornado.gust', gusts, 'sfx', 0.6, 'Tornado Delay Noise 10-12.8, 45-48, 78-80.6 (band-passed, swelled)', 'wind gusts near a tornado',
          max_voices=2, min_interval=0.5, min_dist=6, max_dist=50)
    boom = sine(lambda t: 55 * (1 + np.exp(-t * 8)), 1.5) * env_adsr(int(1.5 * SR), 0.005, 0.3, 0.0, 1.0)
    debris = butter(noise(1.2), 'band', (300, 5000)) * env_adsr(int(1.2 * SR), 0.01, 0.6, 0.0, 0.4) * 0.6
    L.cue('sfx.tornado.hit', mix((boom, 0, 1.0), (debris, 0.02, 0.8), (butter(seg(td, 30, 32), 'low', 1500), 0, 0.6)), 'sfx', 0.8,
          'synth boom + debris + Tornado Delay Noise 30-32', 'tornado strikes a structure', min_dist=6, max_dist=55)
    d = 1.3
    hiss = butter(noise(d), 'high', 3000) * env_adsr(int(d * SR), 0.02, 0.4, 0.5, 0.5, 0.4) * 0.5
    L.cue('sfx.door', mix((clunk, 0, 0.8), (hiss, 0.08, 1.0)), 'sfx', 0.7, 'synth (clunk + airlock hiss)', 'dome door / airlock', min_dist=4, max_dist=35)
    d = 0.6
    puff = butter(noise(d), 'band', (800, 6000)) * env_adsr(int(d * SR), 0.03, 0.35, 0.0, 0.2) * 0.5
    L.cue('sfx.spores', mix((puff, 0, 1.0), (fade(at(pluck, 'F6')[:int(0.15 * SR)], 0.001, 0.08), 0.05, 0.25)), 'sfx', 0.45,
          'synth puff + ' + pluck_src + ' -> F6', 'airy spore puff', pitch_jitter=0.08, max_voices=3, min_interval=0.1)
    x7 = T('Mushroom_4_Part_Loop_007')
    knit = butter(seg(x7, 46.0, 52.0), 'band', (300, 6000))
    knit = loopify(reverb(knit, 1.2, 0.3)[:int(5.5 * SR)], 1.5)
    L.cue('sfx.repair.loop', to_mono(knit), 'sfx', 0.35, 'Mushroom 4 Part Loop 007 46-52 (band-passed, reverb, looped)',
          'fungal "knitting" while Fixers repair (3D loop)', loop=True, min_dist=3, max_dist=26)
    fail = split_phrases(T('Mushroom_4_Part_Loop_005'), 0.8, 3.0)
    f0, f1 = fail[0] if fail else (5.0, 7.0)
    L.cue('stinger.fail', fade(butter(seg(T('Mushroom_4_Part_Loop_005'), f0, f1), 'low', 3000), 0.01, 0.5), 'stinger', 0.5,
          f'Mushroom 4 Part Loop 005 {f0}-{f1} (low-passed)', 'gentle "listen again" after a wrong note')

    L.finish(project, "Analysis: tempo ~129 BPM (layers at 64.5 = half time), mushroom loops mostly F major / D minor / C major.")
    print(f"wrote {len(L.cues)} cues")


if __name__ == '__main__':
    ap = argparse.ArgumentParser()
    ap.add_argument('--src', required=True)
    ap.add_argument('--project', required=True)
    ap.add_argument('--analysis', default=None)
    a = ap.parse_args()
    an = json.load(open(a.analysis)) if a.analysis and os.path.exists(a.analysis) else {}
    build(a.src, a.project, an)
