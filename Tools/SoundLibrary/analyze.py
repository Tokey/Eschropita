"""Analyse the sound designer's tracks for cue selection.

usage: python -I analyze.py <wav_dir> <out_json>
Per track: loudness, silence, gap-separated parts, chroma/key fit, centroid, tempo, onsets with pitch + isolation.
"""
import glob, json, os, sys
import numpy as np
from scipy import signal
from scipy.io import wavfile

WAV, OUT = sys.argv[1], sys.argv[2]
SR = 44100
NOTES = ['C', 'C#', 'D', 'D#', 'E', 'F', 'F#', 'G', 'G#', 'A', 'A#', 'B']
MAJ = np.array([6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88])
MIN = np.array([6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17])


def db(x):
    return 20 * np.log10(np.maximum(x, 1e-9))


def note_name(f):
    if f <= 0:
        return '-'
    m = 69 + 12 * np.log2(f / 440.0)
    mi = int(round(m))
    return f"{NOTES[mi % 12]}{mi // 12 - 1}{'+' if m - mi > 0.25 else ('-' if m - mi < -0.25 else '')}"


def chroma_of(mag, f):
    sel = (f > 55) & (f < 4000)
    midi = 69 + 12 * np.log2(f[sel] / 440.0)
    pc = np.round(midi).astype(int) % 12
    p = (mag[sel] ** 2)
    ch = np.array([p[pc == c].sum() for c in range(12)])
    return ch / (ch.sum() + 1e-12)


def key_fit(ch):
    res = {}
    for i in range(12):
        res[NOTES[i] + ' major'] = float(np.corrcoef(np.roll(MAJ, i), ch)[0, 1])
        res[NOTES[i] + ' minor'] = float(np.corrcoef(np.roll(MIN, i), ch)[0, 1])
    best = max(res, key=res.get)
    return best, res


out = {}
for path in sorted(glob.glob(os.path.join(WAV, '*.wav'))):
    name = os.path.basename(path)[:-4]
    sr, x = wavfile.read(path)
    x = x.astype(np.float32) / 32768.0
    mono = x.mean(1)
    n = len(mono)
    dur = n / SR
    hop = int(SR * 0.05)
    frames = n // hop
    env = np.sqrt(np.array([np.mean(mono[i * hop:(i + 1) * hop] ** 2) for i in range(frames)]) + 1e-12)
    env_db = db(env)
    loud = env_db > -48
    lead = (np.argmax(loud) * 0.05) if loud.any() else 0.0
    trail = (np.argmax(loud[::-1]) * 0.05) if loud.any() else 0.0

    # gap-separated parts (gap = >= 0.35 s below -46 dB)
    quiet = env_db < -46
    parts = []
    i = 0
    while i < frames:
        if not quiet[i]:
            j = i
            while j < frames:
                if quiet[j]:
                    k = j
                    while k < frames and quiet[k]:
                        k += 1
                    if (k - j) * 0.05 >= 0.35 or k >= frames:
                        break
                    j = k
                else:
                    j += 1
            parts.append([round(i * 0.05, 2), round(j * 0.05, 2)])
            i = j
        else:
            i += 1
    parts = [p for p in parts if p[1] - p[0] >= 0.25]

    nfft = 4096
    f, t, Z = signal.stft(mono, SR, nperseg=nfft, noverlap=nfft - 1024, boundary=None)
    mag = np.abs(Z) + 1e-12
    power = mag ** 2
    tot = power.sum(0) + 1e-12
    centroid = float(np.median((f[:, None] * power).sum(0) / tot))
    ch = chroma_of(mag, f)
    key, fits = key_fit(ch)
    fit_F = max(fits['F major'], fits['D minor'])
    fit_C = max(fits['C major'], fits['A minor'])

    # onset envelope + tempo
    flux = np.maximum(0, np.diff(np.log(mag), axis=1)).sum(0)
    flux = (flux - flux.mean()) / (flux.std() + 1e-9)
    fps = SR / 1024.0
    ac = np.correlate(flux, flux, 'full')[len(flux) - 1:]
    ac /= ac[0] + 1e-9
    lo, hi = int(fps * 60 / 180), int(fps * 60 / 50)
    k = lo + int(np.argmax(ac[lo:hi]))
    if 1 <= k < len(ac) - 1:
        a, b, c = ac[k - 1], ac[k], ac[k + 1]
        off = 0.5 * (a - c) / (a - 2 * b + c + 1e-12)
    else:
        off = 0
    bpm = 60 * fps / (k + off)
    beat_r = float(ac[k])

    # onsets with pitch and isolation (quiet before, decays after)
    pk, _ = signal.find_peaks(flux, height=1.8, distance=int(fps * 0.12))
    ons = []
    for p in pk:
        ts = p / fps
        i0 = int(ts * SR)
        pre = mono[max(0, i0 - int(0.25 * SR)):i0]
        post = mono[i0:i0 + int(0.6 * SR)]
        if len(post) < int(0.3 * SR):
            continue
        pre_db = float(db(np.sqrt(np.mean(pre ** 2) + 1e-12))) if len(pre) else -90.0
        att_db = float(db(np.sqrt(np.mean(post[:int(0.08 * SR)] ** 2) + 1e-12)))
        tail_db = float(db(np.sqrt(np.mean(post[int(0.4 * SR):] ** 2) + 1e-12)))
        seg = post[:int(0.25 * SR)] * np.hanning(int(0.25 * SR))
        spec = np.abs(np.fft.rfft(seg, 1 << 15))
        ff = np.fft.rfftfreq(1 << 15, 1 / SR)
        band = (ff > 80) & (ff < 3000)
        fpk = float(ff[band][np.argmax(spec[band])])
        ons.append({'t': round(ts, 3), 'note': note_name(fpk), 'f': round(fpk, 1),
                    'isol': round(att_db - pre_db, 1), 'att': round(att_db, 1), 'decay': round(att_db - tail_db, 1)})

    out[name] = {
        'dur': round(dur, 2), 'rms_db': round(float(db(np.sqrt(np.mean(mono ** 2)))), 1),
        'peak_db': round(float(db(np.abs(x).max())), 1), 'lead': round(lead, 2), 'trail': round(trail, 2),
        'parts': parts, 'centroid': round(centroid), 'key': key, 'fit_Fmaj_Dmin': round(fit_F, 2),
        'fit_Cmaj_Amin': round(fit_C, 2), 'chroma_top': [NOTES[i] for i in np.argsort(ch)[::-1][:5]],
        'bpm': round(float(bpm), 1), 'beat_r': round(beat_r, 2), 'onsets': ons,
        'env_db_1s': [round(float(v), 1) for v in env_db[::20]],
    }
    o = out[name]
    print(f"{name[:38]:38s} {o['dur']:6.1f}s rms{o['rms_db']:6.1f} lead{o['lead']:5.2f} parts{len(parts):3d} "
          f"cen{o['centroid']:5d} key {key:9s} F/Dm{o['fit_Fmaj_Dmin']:5.2f} C/Am{o['fit_Cmaj_Amin']:5.2f} "
          f"top {''.join(n.ljust(3) for n in o['chroma_top'])} bpm{o['bpm']:6.1f}(r{o['beat_r']:.2f}) ons{len(ons)}", flush=True)

json.dump(out, open(OUT, 'w'), indent=1)
