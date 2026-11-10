I've built the complete "natural" palette and rendered every sound, a 64 s lake ambience and two fight demos from real FightModel runs. Nothing has been listened to; every judgement below comes from measurement plus a visual check of spectrograms.

All the targets are met except demo loudness. The demos sit at −22 and −23 LUFS rather than −20, because transients cap the peak at −3 dBFS. The 2–5 kHz share is ≤5.3% on every repeated sound and ~2% in the demo mixes. Loops have no seams, DC is ≤5e-5 and nothing clips. No repo files were modified.

**Why this should fix the "unpleasant reeling" complaint.** Measured on the same perch fight (seed 3), against the current audio:

| | Current | Natural |
|---|---|---|
| Spectral centroid | 3517 Hz | 1400 Hz |
| A-weighted energy in 2–5 kHz | 10.0% | 3.7% |
| 2–5 kHz on a phone-speaker model | 18.9% | 6.0% |

The reel is no longer a pitch-shifted loop. It is separate grains placed by line actually reeled, over a soft line-lay noise bed.

**Files**
- **Previews:** 38 WAVs plus `README.md` (Russian, 19 lines) in `F:\fishing\.previews\audio\natural\`.
  - 01–07 and 27: reel, drag, line and rod states through the same scheduler as the game (48 kHz, game levels, +1.4 dB common trim).
    - 01 reel with no load, 02 labouring under load, 03 gaining fast, 04 slow drag slip, 05 fast drag run, 06 line tension near the limit, 07 rod creak, 27 cranking into a slipping drag.
  - 08 hookset; 09–13 telegraphs (run, dive, surge, jerk, jump).
  - 14–17 splashes (small, big, jump, far); 18–20 landed reward in three tiers.
  - 21 line snap, 22 rod crack, 23 escape, 24 cast, 25 float plop, 26 bite.
  - 30 ambience (64 s loop); 31 seam check (last 3 s followed by the first 3 s).
  - 40 perch 300 g HoldReel demo and 41 bream 2 kg Technique demo, both with ambience; 42 and 43 are the same fights with reel and drag only.
  - 50–54: every grain variant in a row.
- **Code** in `C:\Users\user\AppData\Local\Temp\claude\0047d7a0030b4a57\f76dbde7-8203-47cf-a932-3701622edb9c\scratchpad\harness\Audio_natural\`:
  - `Candidate\NaturalSynth.cs`: all clip recipes (engine-free, 22050 Hz mono, deterministic).
  - `Candidate\NaturalCues.cs`: what plays when, from FightFrame fields only.
  - `Candidate\GrainMixer.cs`: lock-free mixer that starts each grain on its exact sample; it renders the previews and is meant for Unity's audio thread.
  - `Candidate\NaturalBank.cs`: builds and holds the clip set.
  - `Candidate\NaturalFeedback.cs`: a Unity integration sketch, compile-checked against the Unity 6.6 DLLs only.
  - `Harness\*.cs` drives the real fights; the full measurements are in `out.txt`.
  - Re-run (about 3 s): `"$DN" run -c Release --project "$H/Audio_natural" -- F:/fishing/.previews/audio/natural`

**Key recipes**

*Reel*
- **Rotor grain** (16 variants, one per 14 cm of rotor travel):
  - Darkened noise (one-pole low-pass at 1 kHz) through a band-pass at 760 Hz ±10%, Q 0.85.
  - 12 ms raised-cosine attack, decay τ 20 ms ±15%.
  - A bail "thup" (290/585 Hz) at −13 dB and a line-roller tick (1180/1760 Hz, 3–8 ms late) at −16 dB.
  - Filtered to 110 Hz–1.9 kHz.
- **Gear tock** (8 variants, one per 85 cm, offset 40 cm): modes 410/960/1580 Hz, τ 16/8/3.5 ms, 0.8 ms soft mallet.
- **Lay bed** (4 s loop): pink noise band-passed at 950 Hz, 220 Hz–2 kHz, plus "coil grit" at 30/s and slow drift at 0.25/0.75/1.75 Hz.
- **Strain** (replaces Growl; 6 small and 6 big): stick-slip pulses through resonators at 250/395/560/790/1120 Hz, Q 3.5–5.

*Drag, line and rod*
- **Drag tick** (8 bright, 8 dark):
  - Modes 420/1150/1720/2600 Hz, τ 8/5/3.5/2.5 ms, amplitudes 0.4/1/0.8/0.45.
  - Variant jitter: frequency ±7%, τ ±15%, amplitude ±25%.
  - Bright variants add a 2950 Hz spool "zing"; low-pass at 4 kHz bright, 2.2 kHz dark.
- **Line hum** (4 s loop): noise partials at 330 Hz (Q 14), 662 Hz (Q 10) and 1320 Hz (Q 6), with slow 0.25/0.5 Hz "breathing".
- **Line crackle**: tiny ticks at 880/1450/2250 Hz.
- **Rod creak** (6 quiet, 6 loud):
  - A stick-slip train in an arch, 18→60→18 per second (loud: 25→90).
  - Through resonators at 180/320/520/760/1100 Hz, Q 2.5–4, high-passed at 125 Hz.

*Water and one-shots*
- **Water** uses Minnaert bubbles: f₀ = 3 m/s ÷ radius, pitch rising 10% per τ, τ = 4 ms + 3 ms per mm of radius.
  - Splashes: a noise "crown" plus 10 bubbles of 2.2–7 mm (small fish) or 18 of 3–14 mm (big), and falling droplets.
  - Heavy splashes add an 85 Hz thump with harmonics.
  - Far variants are baked darker (1.1 kHz) with a short reverb tail.
- **Hookset**: rod swish (350→1150→600 Hz) followed by a "line goes taut" thump at 160/322/488/980 Hz.
- **Telegraphs**:
  - Run: a rising water swirl plus bubbles.
  - Dive: a deep "gloop" of 6 and 7.5 mm bubbles.
  - Surge: a thickening boil.
  - Jump: a rising surface bulge ending in a "fwoop".
  - Jerk: two knocks on the rod blank, 85 ms apart.
- **Landed**: bubbles, 2/3/5 wet flops 110–160 ms apart, then wooden marimba notes in three weight tiers.
- **Escape**: falling sounds only, no reward.

*Ambience* (64 s loop):
- Water lapping with random slow swells, plus shore sloshes every 1.6–4.5 s.
- Breeze in reeds with gusts and leaf-rustle grains.
- Distant golden oriole (three calls), a cuckoo series, two chirps, and a far fish splash.

**Runtime mapping** (only FightFrame fields, reel speed C0, float distance, state and a reward tier)
- **Rotor speed** v = ReelIn, or C0 × (1 − ReelLoad) when cranking into slip; s = v / C0.
- **Rotor grains** (7.9/s at 110 cm/s, 18/s at 250 cm/s):
  - Gain 0.48 × (0.6 + 0.4s).
  - Pitch (0.97 + 0.08s) × (1 − 0.15 × ReelLoad).
  - Jitter: ±4% time, ±1.5 dB gain, ±1.2% pitch.
- **Tocks**: 0.14.
- **Strain**: when ReelLoad > 0.4, k = (load − 0.4)/0.6, rate 3 + 37k per second, irregular intervals (×0.5–2).
- **Drag tick** every 1.2 cm of SlipCmThisFrame:
  - Breakaway plays a tick immediately.
  - Gain 0.48, scaled by √(15/rate) above 15 ticks/s; first 3 ticks of a run +2 dB.
  - Dark-variant probability (rate − 15)/45.
- **Crackle**: when LoadShare > 0.88, 2→14 per second.
- **Rod creak**: when RodLoadShare > 0.75, every 0.9→0.35 s.
- **Lay loop**:
  - Volume 0.36 × max(s^0.8, 0.35 × load), with a ±10% "spool stroke" wave every 520 cm, plus a peel component of 0.22 × (slip/60)^0.7 while slipping.
  - Pitch 0.82–1.2 by speed; the pitch freezes on release instead of diving.
  - Loops restart at a hashed position, not sample 0.
- **Hum loop**: volume 0.25 × k^1.5 with k = (LoadShare − 0.7)/0.3, pitch 1 + 0.5 × (LoadShare − 0.7).
- **Ambience**: constant 0.09.
- **One-shots**: the same events as FightCues, with per-phase telegraphs, a far variant beyond 12 m, and the landed tier from Species weight range.
- **Timing**: each model tick's grains are laid across its 50 ms window on the audio clock, 40 ms ahead. Variant = hash of the event index, never the same twice in a row, so a spectator with the same model steps hears the same.
- **Unity**: six sources — grains (AudioSource with no clip, OnAudioFilterRead, 16 voices), lay loop, hum loop, ambience loop, one-shots, one spare. No filter components needed.

**Measurements**
- **Build**: the whole bank builds in 0.46–0.9 s on desktop and rebuilds bit-identically; 1.77 M samples in total.
- **2–5 kHz share per clip** (flat / A-weighted):

  | Clip | 2–5 kHz |
  |---|---|
  | Rotor | 1.4 / 2.6% (worst variant 2.8%) |
  | Drag bright | 3.1 / 4.2% (worst 5.3%) |
  | Drag dark | 1.1% |
  | Strain | 0.1% |
  | Creak | 0.3–0.4% |
  | Lay bed | 4.8 / 7.6% |
  | Hum | 0.4% |
  | Ambience | 0.5% |

  Above 5 kHz is ≤0.1% everywhere.
- **Loop seams**: the jump at the wrap point sits at the 80th/34th/30th percentile of each loop's own sample steps (lay/hum/ambience), i.e. ordinary. Periodic amplitude modulation is below 10% at 2–8 Hz and 20–150 Hz.
- **Ambience repetition**: self-similarity peaks at r = 0.27 over 1–30 s lags (1.0 = exact repeat).
- **Rhythm**: rotor interval 126.9 ms against 127.3 expected, variation 3.2%.
  - At 18 grains/s the variation is 4.6% sample-accurate, against 14.5% if grains were played with PlayOneShot at 60 fps.
  - In a fast drag run, 34 ticks would land in the same frame.
- **Variation**: in both fights no grain variant played twice in a row; gain spread 1.0–2.1 dB; 5 of 16 voices used at most, none stolen.
- **Game-level demos** (master 0.8):
  - Perch −25.5 LUFS; reel while cranking −26 dBFS RMS with 51% rhythmic modulation; ambience 14.8 dB under the reel.
  - Bream −26.5 LUFS; ambience 12.7 dB under; drag peaks +2.7 dB over the reel.
  - Preview files trimmed +3.4 dB: perch −22.0 LUFS (LRA 2.2 LU, true peak −3.0), bream −23.0 LUFS (LRA 6.0 LU).
- **Phone speaker**: most cues keep all but 0.7–3 dB of their A-weighted energy. Jerk telegraph −7.9 dB, gear tock −7.0, dive −5.7, landed −6 to −7.

**Weaknesses**
- **Not heard.** Whether the rotor sounds like a reel or like brushing paper, and whether strain reads as creak or crackling fire, needs ears.
- **Demo loudness is short of −20 LUFS.** Rare coincident peaks at fight start set the limit (99.9th percentile is 4 dB below the maximum). Reaching −20 LUFS needs about 2 dB of limiting, which I didn't add.
- **Fast drag runs** still carry a ratchet colour about 11 dB at 1.15/1.72 kHz, now wavering rather than steady. The bream slips 39% of its fight, so fatigue needs checking.
- **Bream under heavy load** (ReelLoad median 0.90): the rotor barely turns, only 3 rotor grains in 26 s. The reel there is strain grains plus a quiet bed, and it is gappy (71% modulation).
- **Grain timing depends on OnAudioFilterRead**, which doesn't exist on WebGL. Using a clip-less AudioSource with AudioSettings.dspTime on the audio thread is unverified in this project. The PlayOneShot fallback works but measurably limps at 18 grains/s.
- **NaturalFeedback.cs** is compile-checked only and contains no haptics. Keep FightCues.Pulses for vibration. The personal-best reward tier is not wired.
- **Spectator parity is approximate.** Reeled distance uses ReelIn × 0.05 × TicksThisFrame, which is exact only with one tick per frame. Adding a `ReelCmThisFrame` field to FightFrame, like SlipCmThisFrame, would make it exact.
- **Ambience** costs 5.6 MB as float. Half its energy is lapping below 300 Hz, so on a phone you mostly hear breeze, sloshes and birds. The synthetic birds may sound electronic.