The tactile palette is built and rendered: 37 WAVs plus a README in `F:\fishing\.previews\audio\tactile\`. It is not a literal drop-in for FightSynth/FightCues, because the click layer needs a new grain voice on the audio thread. I have not listened to any of it; every judgement below comes from measurements. No repo files were changed.

## Files
**Previews:** `F:\fishing\.previews\audio\tactile\`
- **Situations, rendered through the real runtime logic (48 kHz):**
  - `01_reel_idle`, `02_reel_load`, `03_reel_gaining`, `04_reel_pump_start_stop`
  - `05_drag_slow`, `06_drag_fast`, `07_line_tension`, `08_rod_creak`
- **One-shots (22050 Hz):**
  - `09_hookset`, `10_tele_run`, `11_tele_dive`, `12_tele_surge`, `13_tele_jerk`, `14_tele_jump`
  - `15_splash_small`, `16_splash_big`, `17_splash_jump`, `18_line_thrum_shock`, `19_fish_flop`
  - `20_chime_small`, `21_chime`, `22_chime_big`, `23_line_snap`, `24_rod_crack`, `25_escape`
  - `26_cast_whoosh`, `27_float_plop`, `28_bite_bob`
- **Ambience:** `30_ambience_morning_64s` (seamless loop, exactly 64 s).
- **Every grain variant in a row:** `40_grains_detent_16`, `41_grains_drag_16bright_16dark`, `42_grains_strain_12`, `43_grains_rodcreak_4soft_4hard`.
- **Fight demos:** `90_fight_perch300_HoldReel` (lands after 18.7 s) and `91_fight_bream2kg_Technique` (first 21 s, still in progress). Each also has a `_reel_only` version.
- `README.md` (in Russian).

**Code:** `C:\Users\user\AppData\Local\Temp\claude\0047d7a0030b4a57\f76dbde7-8203-47cf-a932-3701622edb9c\scratchpad\harness\Audio_tactile\`
- `TactileSynth.cs`: the recipes.
- `TactileDsp.cs`: filters, struck modes, bubble, small baked reverb.
- `TactileGrains.cs`: the grain engine, written to run inside OnAudioFilterRead.
- `TactileCues.cs`: what plays when, driven by FightFrame.
- `TactileMixer.cs`: software model of the 6 AudioSources.
- `Program.cs`, `Analysis.cs`: rendering and measurement.
- All numbers are in `out.txt`. Per-frame logs are in `bin\Release\net8.0\*_frames.csv`.

## Key recipes
All sounds are struck modes with a raised-cosine onset, so there are no single-sample edges. Damping tanφ is 0.038–0.11 on everything that repeats, which reads as wood or plastic rather than metal (metal is below 0.005).

| Sound | Recipe | Variants |
|---|---|---|
| Handle detent (90 ms) | Body 300/620/1040 Hz (τ 8/13/8 ms). Click 1560/2450/3400 Hz (τ 3.4/1.6/0.8 ms, 0.25 ms attack). A small second "settle" click 2.2–3.8 ms later at −12 dB. High-pass 110 Hz, low-pass 3.8 kHz. | 16; frequency ±3%, τ ±12%, amplitude ±10% |
| Rotor swish (75 ms) | Noise through band-passes at 620 Hz (Q 1.1) and 1150 Hz (Q 2, −10 dB). 6 ms attack, τ 14 ms. Low-pass 2.2 kHz, 4th order. | 12 |
| Drag tick (45 ms) | Modes 360/560/1250/1870/2700/3600 Hz (τ 7/5/3.8/2.3/1.1/0.5 ms). Bright set low-passed at 4.2 kHz. Dark set: frequencies ×0.92, top modes halved, low-pass 2.2 kHz. | 16 bright + 16 dark |
| Strain (60–120 ms) | Stick-slip pulses with an irregular 6.5–13 ms gap, through resonators at 300/470/690/980/1350 Hz (Q 3). | 12 |
| Rod creak (0.25–0.5 s) | Stick-slip pulse rate gliding between 55–75 and 90–130 Hz, through resonators at 180–1100 Hz (Q 2–4). | 4 soft + 4 hard |
| Loops | Spool purr: 3 s noise, band-pass 620/1200 Hz, low-pass 1.7 kHz 4th order. Line hum: 4 s, double band-pass at 440/880/1320 Hz. Both filtered circularly, with slow change at one cycle per loop. | – |
| Water | Bubble model, pitch 3000/r[mm] Hz, rising pitch, τ = 6 + 4r ms. Splashes are a noise burst plus a bubble cloud (radius distribution ∝ r^−2.5); big splashes add 90/180/270 Hz. A small Freeverb-style reverb at −16 to −20 dB is baked in. | – |
| Reward | A soft latch click, then a marimba-like chime on G-major pentatonic notes (partials ×1 / ×2 / ×3.93), 75 ms apart. Three tiers by weight: under 250 g, under 1000 g, 1000 g and up. | – |

## Runtime mapping (from FightFrame and state)
| Layer | Driven by |
|---|---|
| Handle detents | Rate = 12/s × (1 − ReelLoad) while reeling. Every 8th detent (one handle turn) is accented +1 dB. Pitch × (1 − 0.1·ReelLoad). Gain × (1 + 0.35·ReelLoad), so slower clicks are heavier. |
| Rotor swish | Runs at 0.65 × detent rate. Only on a tight line; gain rises with LoadShare from 0.08 to 0.43. |
| Strain | ReelLoad 0.45→1 maps to 2→14 events/s at irregular intervals. ×0.35 while the drag is slipping. |
| Drag ticks | Rate = SlipCmPerSec / 2 cm while slipping. Breakaway fires an immediate tick. First 3 ticks of a run +1.5/+1/+0.5 dB. Above 14/s: gain × √(14/rate) and dark variants with probability (rate − 14)/40. |
| Spool purr (loop) | Volume ∝ max(handle speed, slip/60)^0.8. Pitch 0.88–1.17. On release the pitch is held and only the volume fades, so no pitch dive. |
| Line hum (loop) | Tight line only: smoothstep of LoadShare 0.72→0.97; pitch up to ×1.36. |
| Rod creak | RodLoadShare 0.78→1 maps to 0.9→2.6 creaks/s, getting louder and harder. |
| Ambience (loop) | Volume 0.2 out of the fight, 0.08 in it. |
| Telegraphs | A separate clip per NextPhase (Run, Dive, Surge, Jump, Jerk). |
| Shock | Plays a line "tung" when Peak/Weakest > 0.3. |
| Landing, snap, crack, escape | Landing plays fish flop (swung) or splash (netted), then a chime tier by WeightG. Line break, empty spool, rod break and escape each have their own clip. |

**Sources (6):**
1. Grain voice: OnAudioFilterRead calling `TactileGrains.Render`, up to 16 voices. Grains are pre-rendered at the output sample rate (about 1.7 MB at 48 kHz).
2. Purr loop.
3. Hum loop.
4. Ambience loop.
5. Dry one-shots.
6. Water one-shots, with an AudioLowPassFilter (Q 1) whose cutoff runs 6 kHz → 1.8 kHz with distance (`WaterCutoff`).

No echo or reverb components are needed because the reverb is baked into the clips. Loops restart at a hashed offset so no two starts sound identical. Vibration is unchanged; the FightCues haptics stay as they are.

## Measurements
| Check | Result |
|---|---|
| Determinism | A rebuild is bit-identical. |
| Clipping | No sample reaches ±1.0 in any raw mix. |
| DC | Mixes ≤ 0.0002; grains ≤ 0.0011. |
| 2–5 kHz share in fights (flat / A-weighted / phone) | New: 0.9–1.6% / 1.7–3.3% / 3.1–4.6%. Current game: 3.5–4.7% / 10–15.6% / 18.9–21.4%. |
| Energy above 5 kHz | New: 0.0%. Current: 1.4–2.1%. |
| Grain centroids | 1.0–1.45 kHz. |
| Loop seams | The jump at the wrap point is ordinary for each loop's own samples, no outlier: purr 57th percentile, hum 23rd, ambience 5th. |
| Volume flutter on loops | 2–8 Hz: purr 2.7%, hum 8.4%. 20–150 Hz: purr 5.0%, hum 6.7%. All under the 10% rule. |
| Variation | Detents use 16 variants and drag 32, never the same one twice in a row. Detent volume spread 0.6 dB. |
| Rhythm steadiness | Interval-to-interval change is 2.2% (median) for detents and 2.7% for drag. Playing grains at frame time instead (PlayOneShot fallback at 60 fps) would be 13% for detents and 40% at the 90th percentile for drag — audible limping. |
| Voices | At most 5 of 16 in use, so a grain is never cut off. |
| Ambience level | 17 dB under the reel during fights. |
| Loudness (peaks −3 dBFS) | Perch demo −23.8 LUFS, bream −25.3. That is about the level of the current previews. |
| Generation time (desktop) | 0.9 s for all 22050 Hz clips, of which the ambience is 0.6–0.8 s. Grain bank 30 ms. |

## Weaknesses
- **Unheard.** The detent may sound too soft (only 0.9% of its energy is in 2–5 kHz). If it is too dull, the fix is +3 dB on its 2450 and 3400 Hz modes. The strain grains are rough by nature and may read as crackle.
- **Demo loudness misses the −20 LUFS target.** The demos sit at −24 to −25 LUFS. Clicks peak about 21 dB above their average loudness, so reaching −16 to −20 LUFS on a phone would need a limiter on the master, which is not implemented.
- **The demos only cover part of the palette:**
  - The bream fight is 91% drag ticks at 10–30/s. That is the most likely thing to tire players over a long session.
  - Under heavy load the detents slow to 1.2/s, which may feel stuck.
  - Rotor swish is almost inaudible on a light fish.
  - Line hum and rod creak never fire in either demo; they are only heard in 07 and 08.
  - The bream fight never lands, so no reward sound plays in that demo.
- **Ambience repeats.** The cuckoo comes back every 64 s. It could be scheduled as a separate one-shot. On a phone the 64 s ambience could take several seconds to generate, so it needs a background thread or a shorter loop.
- **Integration cost.** It needs new audio-thread code (the grain voice) and a new sound enum. The chime tiers use fixed weight cutoffs, not the weight percentile or a personal best.