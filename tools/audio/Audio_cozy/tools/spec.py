# Spectrogram + waveform envelope PNG for a 16-bit mono WAV (numpy only). Usage: spec.py in.wav out.png [fmax] [seconds]
import sys, struct, zlib, wave
import numpy as np

src, dst = sys.argv[1], sys.argv[2]
fmax = float(sys.argv[3]) if len(sys.argv) > 3 else 6000
secs = float(sys.argv[4]) if len(sys.argv) > 4 else 0
with wave.open(src, 'rb') as w:
    fs = w.getframerate()
    x = np.frombuffer(w.readframes(w.getnframes()), dtype=np.int16).astype(np.float64) / 32768
if secs > 0:
    x = x[: int(secs * fs)]
nfft = 2048 if fs > 30000 else 1024
hop = nfft // 8
win = np.hanning(nfft)
frames = []
for s in range(0, max(1, len(x) - nfft), hop):
    seg = x[s:s + nfft] * win
    frames.append(np.abs(np.fft.rfft(seg)) ** 2)
S = np.array(frames).T  # freq x time
freqs = np.fft.rfftfreq(nfft, 1 / fs)
# log-frequency rows 60 Hz .. fmax
H, W = 300, min(1600, S.shape[1])
rows = np.geomspace(60, fmax, H)[::-1]
idx = np.clip(np.searchsorted(freqs, rows), 0, len(freqs) - 1)
cols = np.linspace(0, S.shape[1] - 1, W).astype(int)
img = S[idx][:, cols]
db = 10 * np.log10(img + 1e-14)
top = np.percentile(db, 99.7)
db = np.clip((db - (top - 70)) / 70, 0, 1)
# colormap: black -> purple -> orange -> yellow
def cmap(v):
    r = np.clip(1.6 * v, 0, 1)
    g = np.clip(1.6 * v - 0.6, 0, 1)
    b = np.clip(0.8 * np.sin(np.pi * v * 1.2), 0, 1) * (v < 0.83) + (v >= 0.83) * np.clip((v - 0.83) * 4, 0, 1)
    return np.stack([r, g, b], -1)
rgb = (cmap(db) * 255).astype(np.uint8)
# envelope strip (peak per column) underneath, 80 px
E = 80
env = np.zeros((E, W, 3), np.uint8)
edges = np.linspace(0, len(x), W + 1).astype(int)
for c in range(W):
    seg = x[edges[c]:max(edges[c] + 1, edges[c + 1])]
    pk = np.max(np.abs(seg)) if len(seg) else 0
    rms = np.sqrt(np.mean(seg ** 2)) if len(seg) else 0
    hp = int(min(1, pk) * (E - 1)); hr = int(min(1, rms) * (E - 1))
    env[E - 1 - hp:E, c] = (90, 90, 140)
    env[E - 1 - hr:E, c] = (230, 230, 255)
# frequency gridlines at 250, 500, 1k, 2k, 4k (dim)
for f in (250, 500, 1000, 2000, 4000):
    if f < fmax:
        r = int(np.argmin(np.abs(rows - f)))
        rgb[r, ::4] = (60, 200, 60)
# second ticks
for t in range(int(len(x) / fs) + 1):
    c = int(t * fs / len(x) * W)
    if 0 <= c < W:
        rgb[-6:, c] = (255, 255, 255)
        env[:6, c] = (255, 255, 255)
out = np.concatenate([rgb, env], 0)
h, w, _ = out.shape
raw = b''.join(b'\x00' + out[i].tobytes() for i in range(h))
def chunk(t, d):
    c = struct.pack('>I', len(d)) + t + d
    return c + struct.pack('>I', zlib.crc32(t + d) & 0xffffffff)
png = b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, 2, 0, 0, 0)) + chunk(b'IDAT', zlib.compress(raw, 9)) + chunk(b'IEND', b'')
open(dst, 'wb').write(png)
print(dst, w, h, 'grid 250/500/1k/2k/4k green; bottom strip: peak (dim) and RMS (bright) per column')
