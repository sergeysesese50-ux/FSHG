The cozy palette is built and rendered, but I have not listened to any of it. Everything below comes from measurements and spectrograms, so the files need an ear check, first on a phone speaker and then on headphones.

The measured result: the old buzz is gone. While cranking in the perch fight, the reel's spectral centroid is now 931 Hz (A-weighted 1125 Hz), against 3866 Hz for the old reel-whir loop (Whir). Its energy share at 2–5 kHz through the phone model is 3.2%, against 24%. There are 0 clipped samples and DC is ≤0.00012. Loop seams are invisible, and no grain variant plays twice in a row.

## Files
**F:\fishing\.previews\audio\cozy\** — 31 WAVs, README.md (Russian) and parts\ (10 WAVs: raw grain sets, and the three fight loops tiled twice so the seam sits mid-file).
- **01–04 reel** (8–12 s, 48 kHz, −20 LUFS): no load, labouring, gaining fast, and a load sweep 0→0.95→0.
- **05–06 drag:** slow (countable ticks) and fast.
- **07 line tension:** boosted 11 dB for listening.
- **08 rod creak.**
- **09–27 one-shots** (22050 Hz, peak −3 dBFS): hookset; telegraphs for Run, Dive, Surge, Jerk and Jump; splashes small, big and jump; landed small, medium and big; line snap, rod crack, escape, cast whoosh, float plop, bite bob; all 11 bird variants.
- **28** ambience loop, 64 s, seamless. **29** one morning minute (loop plus scheduled birds), −24 LUFS.
- **30** perch 300 g, HoldReel bot, to the landing. **31** bream 2 kg, Technique bot, first 26 s of fight (seed 3; no seed lands within 60 s). Both include cast, wait and bite, and are mixed with the lake at game levels.

**Code** in C:\Users\user\AppData\Local\Temp\claude\0047d7a0030b4a57\f76dbde7-8203-47cf-a932-3701622edb9c\scratchpad\harness\Audio_cozy\:
- CozySynth.cs — the recipes; engine-free, deterministic, 22050 Hz mono.
- CozyCues.cs — the runtime mapping; engine-free; covers the audio half of FightCues.
- CozyFrameAdapter.cs — FightFrame → CozyFrame.
- CozyPlayer.cs — the clip bank plus a software model of the Unity playback.
- Program.cs, Measure.cs, Dsp.cs — render and measure.
- out.txt — all measurements.
- tools\spec.py and png\ — spectrograms.
- Run: `"$DN" run -c Release --project "$H/Audio_cozy"` (about 4 s).

**Sample rate:** I kept 22050 Hz mono. Every layer is low-passed at 3.2 kHz or lower, so 44.1 kHz would double memory and generation time for nothing.

## Key recipes
- **Gear tick** (grain, 3 weights × 12 variants):
  - Modes 300/690/1240/1980 Hz, decay τ 12/8.5/4/1.8 ms, amplitudes 0.45/1/0.45/0.12.
  - Excited by a soft 1.4 ms "mallet" (1.2 ms light, 1.8 ms heavy), plus a per-variant contact noise at 1.2 kHz, −16 dB.
  - Jitter: frequency ±3%, τ ±15%, amplitude ±20%. Low-pass 2.4 kHz, high-pass 120 Hz, small room at −17 dB.
  - Light weight: frequency ×1.08, τ ×0.8. Heavy: frequency ×0.84, τ ×1.35. Centroid 505–712 Hz.
- **Handle tock** (every 8th tick): 240/520/1150/1700 Hz, τ 16/12/5/2 ms, 6 variants. Damping tanφ ≈ 0.05, so it reads as wood rather than metal.
- **Reel bed** (6 s loop):
  - Noise bands 420 Hz (Q 0.9), 210 Hz (×0.5) and 1150 Hz (×0.18), plus random micro-ticks at about 45/s around 900 Hz (×0.25).
  - Slow volume drift at 0.17 and 0.33 Hz. Low-pass 1.8 kHz, soft tanh limit at 3× RMS.
  - It has no harmonics, so it can't buzz at any pitch.
- **Strain** (5 s loop; replaces the old load growl): resonant noise at 240/360/520/760 Hz, shaped by irregular friction grains at about 18/s (40–60 ms each, floor 0.5) — a muffled purr. Low-pass 1.5 kHz.
- **Drag tick** (16 bright + 16 dark):
  - 480/1050/1650/2400 Hz, τ 7/4.5/2.5/1 ms, 0.5 ms mallet.
  - Dark variant: frequency ×0.92, 0.9 ms mallet, low-pass 1.7 kHz instead of 3.2 kHz.
  - **Purr grain** (12 variants): 650 Hz noise puff, τ 9 ms.
- **Line tension** (7 s loop): noise bands 420 Hz (Q 6), 840 Hz (Q 5, ×0.4) and 1400 Hz (Q 1, ×0.16), slow drift. No tone and no tremolo.
- **Rod creak** (8 variants): stick-slip pulses at 30→90→30/s into resonators at 180–1100 Hz (Q 3.5), 0.25–0.5 s long.
- **Water:**
  - Bubbles: f₀ = 3 m/s ÷ radius, with a slight upward pitch glide; decay 6 ms + 4 ms per mm of radius.
  - Splashes: a noise "crown" (τ 30/55/80 ms) plus a bubble cloud (9/16/10 bubbles, sizes 2–12 mm); a 90 Hz thump on the big one; falling drops on the jump splash; far-shore reverb at −16 dB.
- **Hookset:** swish 300→900 Hz; thump 125→80 Hz with 2nd and 3rd harmonics; line "tunn" at 410/820 Hz; a small bubble.
- **Telegraphs:**

  | Cue | Sound |
  |---|---|
  | Run | Swirl 300→750→450 Hz with bubbles |
  | Dive | Downward "gloop" 600→260 Hz with big low bubbles |
  | Surge | Rush 350→1100 Hz |
  | Jerk | Heartbeat "tu-dum", two thumps at 150 Hz |
  | Jump | Airy rise 280→1500 Hz |

- **Landed:** 3/4/5 wet flops, then a marimba-like arpeggio in G pentatonic (110 ms steps); the big tier adds a low 196 Hz pad.
- **Line snap:** a muffled snap, a slack-string "tunn" at 520 Hz dropping 3% in pitch, and a falling-line whisper.
- **Lake (64 s loop, no birds inside):**
  - 24 laps whose low-pass follows the wave (250→900 Hz).
  - A water floor, a gusty breeze in the reeds at 850/1500 Hz with sparse rustle clicks, and 2 distant plips.
  - Far reverb, processed circularly so the loop has no seam.
- **Birds:** scheduled every 8–24 s, 11 variants (cuckoo 2, wood pigeon 3, blackbird 6).
- **Rooms** (baked into the clips):
  - Small room for reel and one-shots: RT60 about 0.27 s; grain tails cut at 0.16 s.
  - Far shore for water and birds: RT60 about 0.8 s.

## Runtime mapping (CozyCues; reads only FightFrame fields)
| Layer | Driven by |
|---|---|
| Gear ticks | One tick per C0/16 cm of handle travel, where handle speed = max(ReelIn, C0·(1−ReelLoad)) while cranking. At 0.72·C0 that is 11/s. Timing ±4%. Weight = 1 + h − l, with h from ReelLoad 0.2–0.8 and l from handle speed 0.8–1.1·C0. Gain 0.55 + 0.45·speed/C0, multiplied by √(14/rate) above 14/s. |
| Drag ticks | One per 2 cm of slip. Above 14/s gain × √(14/rate); dark-variant probability (rate−10)/20; purr-grain probability (rate−20)/30, capped at 0.85. Timing ±4% rising to ±12% on fast runs. First 3 ticks of a run +2 dB; Breakaway plays an immediate accented tick. |
| Bed loop | max(√(handle/C0), 0.8·(Slip/70)^0.7) × 0.45. Pitch 0.86 + 0.24·speed, or 0.8 + 0.35·Slip/90 while the spool slips. |
| Strain loop | smoothstep((ReelLoad−0.35)/0.55) × 0.6; × 0.45 while slipping. |
| Line loop | Only when tight: smoothstep((LoadShare−0.68)/0.28) × 0.3. |
| Rod creak | Above RodLoadShare 0.78: 1→3.5 per s, intervals ×0.7–1.4. |
| One-shots | Telegraph cues by NextPhase on TelegraphStart (Run, Dive and Surge each get their own sound). Splashes on PhaseStart, JumpApex and SurgeStart, scaled by weight and distance. Landed tier by WeightG: under 500 g, under 1500 g, above. Snap or crack sets CutLoops (1-frame fade). |
| Lake | Always on; × 0.6 during a fight. |

Two design points make spectators hear the same thing:
- **Event clocks run in model time, not frame time.** Distances are integrated over model time τ = (Tick−1+Alpha)·0.05 s, and each event's variant comes from a hash of its index (never the same variant twice in a row). Counters reset at every hookset.
- **Release doesn't dive in pitch.** When a loop fades out, its pitch freezes instead of dropping; each loop restart begins at a hashed offset, so onsets differ.

**Unity side (6 sources):**
- 4 loop sources: bed, strain, line, lake (volume and pitch only).
- 1 clip-less source with an OnAudioFilterRead grain mixer: 24 voices; each grain stamped at dspTime − (1−At)·dt + 40 ms and passed through a ring buffer.
- 1 fx source using PlayOneShot.
- No Unity filter components are needed, because the reverb is baked.
- Clip generation is pure functions, so it can run on a worker thread.
- On WebGL (no OnAudioFilterRead) the fallback is PlayOneShot, with the timing jitter shown below.

## Measurements
- **Bank generation:** 0.7–0.8 s on this PC including JIT; the 64 s ambience is 0.4 s of that. Total 182 s of audio, 15.3 MB as float.
- **Loops:**
  - Seam: the jump at the wrap point is at the 48th–89th percentile of the loop's own sample steps (a visible discontinuity would be ≥99).
  - Largest periodic volume modulation in any single component: 2–8 Hz 2.2–6.1%, 20–150 Hz 1.5–8.5%. The exception is the strain purr at 8–20 Hz, 15%, which is irregular by design.
  - For comparison, the old whir had 19% at 4 Hz and the old growl 33% at 11 Hz.
- **Game levels at master 0.8:**

  | Situation | Loudness |
  |---|---|
  | Free reel | −22.5 LUFS |
  | Reeling fast | −21.9 LUFS |
  | Labouring | −23.3 LUFS |
  | Fast drag | −24.7 LUFS |
  | Slow drag | −29.2 LUFS |
  | Line at its maximum | −31 LUFS |
  | Lake while waiting | −32.9 LUFS |
  | Lake during a fight | about 15 dB under the reel |

- **Demo games:** perch −23.4 LUFS (peak −3.9 dBFS), bream −24.7 LUFS (peak −3.8). Tick crest limits the files to −22.5 and −23.9 LUFS at −3 dBFS.
- **Variation:** 0 immediate repeats among more than 400 grains per demo. Peak grain voices 12 (fast drag at 62/s); 5–8 in the demos.
- **Timing jitter** (local variation of tick gaps):

  | Stream | Scheduled (sub-frame) | PlayOneShot at 60 fps | PlayOneShot at 30 fps |
  |---|---|---|---|
  | Gear ticks | 3.6–5.3% | 9.8–12.4% | 15–26% |
  | Drag ticks | 6–9% | 19–31% | 24–29% |

- **Spectator check** (same bream fight at 60 fps vs 37 fps): all 227 grains identical in order, sound and variant. Onset difference 0 ms for gear ticks, at most 6.3 ms for drag ticks.

## Weaknesses (honest)
1. **No listening.** All character judgements come from metrics.
2. **Loudness:** reaching −20 LUFS at −3 dBFS peak would need a limiter of about 3 dB; I did not apply one, so the demo files are at −22.5 and −23.9 LUFS.
3. **The bed is filtered noise.** It sits about 4 dB under the ticks; on a phone it could still read as a soft "shh".
4. **The strain purr** sits near the roughness range. If it sounds like a rattle, raise the envelope floor from 0.5 to 0.7.
5. **Drag ticks peak only about 1 dB above gear ticks**, not the researched +3 dB. They stand out by timbre (centroid 1.1 kHz vs 0.6 kHz) rather than level.
6. **The labouring bream** gets only 1.6 heavy ticks per second, so the strain purr carries that state. Some players may want more ticks there.
7. **Line tension and rod creak never trigger in either demo**: max LoadShare is 0.70 and max RodLoadShare 0.69. They can only be heard in files 07 and 08.
8. **Integration cost:** this needs a new grain-mixer component plus a new sound enum (Run, Dive and Surge separated; bird variants).
9. **Exact spectator equality holds only at ≥20 fps.** Below that, a frame can contain two model steps.
10. **Reverb is baked**, so it can't be changed at runtime. The small-room touch on an outdoor lake is a stylistic choice.
11. **Landed tiers use weight thresholds**, not weight percentile or personal best — FightFrame doesn't carry those.
12. **Blackbirds are the brightest element** (4.7% of their energy at 2–5 kHz), though distant and rare.
13. **Gear variants are deliberately similar** (cross-correlation 0.89–0.95, for a consistent mechanism). Natural variation comes from the weights, ±1.5 dB gain, ±4% timing and the contact noise.
14. **Haptics are untouched**; the haptic half of FightCues stays as it is.