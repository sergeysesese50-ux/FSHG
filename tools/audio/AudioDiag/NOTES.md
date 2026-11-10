I built the diagnosis harness and listened through it by measurement: every FightSynth clip, plus three real fights stepped through FightFrame, FightCues and a software model of FightFeedback. The main cause is the Whir clip itself: it is a buzzer-style pulse wave, and on a phone speaker its low body disappears, so only rasp and hiss are left. Under load the Growl clip makes it worse, because it comes out of a phone as static rather than rumble.

**How it was measured.** Project: `C:\Users\user\AppData\Local\Temp\claude\0047d7a0030b4a57\f76dbde7-8203-47cf-a932-3701622edb9c\scratchpad\harness\AudioDiag`. It has three files: `Program.cs`, `Dsp.cs` and `Mixer.cs`. To re-run: `"$DN" run -c Release --project "$H/AudioDiag"`, which takes about 2 s. Full numbers are in `...\harness\AudioDiag\out.txt`.
- The mixer copies FightFeedback: same clips and seeds, master 0.8, attack 40 ms, release from FightCues, pitch smoothing 50 ms, loops restart from sample 0.
- It plays the 22050 Hz clips to a 48 kHz output with linear interpolation, which is how FMOD resamples.
- The fights were stepped at 60 fps with 0.05 s ticks, like FishingController.
- "Phone" below means A-weighted after a 700 Hz, 12 dB/octave high-pass, as a rough phone loudspeaker.
- Demo outcomes:
  - Perch 300 g with the HoldReel bot: landed after 18.7 s.
  - Bream 2 kg with the Technique bot: first 30 s.
  - Pike 4 kg on 0.20 line with the Technique bot: first 30 s.

## Why reeling sounds unpleasant, most important first

**1. Whir is a narrow pulse wave, the classic buzzer sound** (FightSynth.cs:27-31).
- **Shape:** an unsmoothed pulse train, on 20% of each cycle, at 220 Hz × pitch. In play the pitch puts it at 132–308 Hz.
- **Bright harmonics:** they fall only 6.8 dB per octave. The 7th harmonic is only 13 dB below the fundamental. A soft tone falls at least 12 dB per octave.
  - The only smoothing is a single 6 dB/octave low-pass at 3.5 kHz (line 30, LowPass at :354).
  - In third-octave bands the clip stays flat at −16 to −19 dB from 1.3 kHz all the way to 10 kHz. Its spectral centroid is 4.7 kHz.
- **Hard edges:** each rising edge jumps 0.45, which is 56% of the peak, in a single sample.
- **On a phone:** the fundamental is gone (0.2–0.7% of energy below 300 Hz).
  - The loudest component becomes the 3rd harmonic (660–924 Hz) or the 7th (924–1232 Hz).
  - 21–33% of what you hear is in the 2–5 kHz band.
  - Above about 1 kHz the harmonics are only 132–150 Hz apart while reeling a loaded fish. That is narrower than the ear's frequency resolution there (about 133 Hz at 1 kHz, 241 Hz at 2 kHz). So they beat against each other, which is what makes it sound like an electric razor or kazoo.
- **Hiss:** line 39 adds 15% unfiltered white noise, and 38% of that noise is above 5 kHz.
- **Exposure:** Whir plays during 100% of cranking. In the perch fight it is 73% of everything heard (70% on a phone).

**2. Growl is meant as rumble but comes out of a phone as static** (FightSynth.cs:46-57).
- **Filter too gentle:** white noise goes through a single 6 dB/octave low-pass at 150 Hz. Relative to the peak it is still only 8 dB down at 1 kHz, 10 dB at 2 kHz and 13 dB at 4 kHz.
- **Without speakers:** 71% of its energy is below 300 Hz, so on headphones it does rumble.
- **On a phone:** 0.2% is below 300 Hz, 62% is at 300–2000 Hz and 29% at 2–5 kHz. The loudest band is 1–1.6 kHz, which is hiss.
- **Flutter:** line 54 adds a fixed 11 Hz triangle tremolo with 33% depth. That reads as a rattle.
- **Repeats:** the noise is the same one-second loop over and over.
- **Too loud under load:** GrowlLevel is 0.5 against WhirLevel 0.28 (FightCues.cs:71-72). For a 2 kg bream, ReelLoad sits around 0.90, so Growl plays at 0.47 against Whir's 0.18, about 4–5 dB louder. While cranking:
  - Growl plays 100% of the time for bream and 91% for pike.
  - Whir sits at its lowest pitch, 0.60–0.68 (132–150 Hz).
  - So straining against a fish sounds like low buzz plus static at an 11 Hz flutter.

**3. The sound is static and mechanical, with no natural variation.**
- 93.4% of Whir's energy is in perfectly regular harmonics.
- The 4 Hz "gear" pulse (FightSynth.cs:38) has 19% depth and runs at 4 Hz × pitch. In the perch fight that is 4.6 Hz, close to the rate where the ear notices volume changes most.
- Loops repeat every 435–735 ms (Whir) and about 1 s (Growl), identical every time.
- In the perch fight the reel sound measures −25 or −26 dBFS in every second for 18 s.
- Nothing in it reflects progress, such as line coming in. It only follows reel speed, and only within about ±10%.

**4. Starting and stopping make a "pew".**
- When the player releases Reel, the pitch target drops to 0.6 straight away (FightCues.cs:483-484).
- The volume takes 120 ms to fade (FightCues.cs:76), but the pitch moves with a 50 ms time constant (FightFeedback.cs:255).
- So every release dives as much as about 15 semitones while still audible. In the pike fight, pumping the rod (lift, then a short reel) caused 26 dives of this kind in 30 s.
- Each restart plays the clip from sample 0 (FightFeedback.cs:276-282), so the onset is the same every time: 13–19 starts per minute.
- There is also a small staircase in pitch because reel speed updates 20 times a second. In steady reeling the steps are 1–3 cents, about 20 cents at the 90th percentile; this is minor.

**5. Level balance and silence.**
- The reel sound is as loud as the events. In the perch fight Whir is at −25 dBFS and the splashes and hookset at −21 to −29. A continuous bed should sit about 10–15 dB below events.
- There is no ambient bed. In the bream fight several stretches drop to −45 dBFS or silence, so every loop onset stands out.

**6. Other sounds heard in the same moments.**
- **Drag click** (FightSynth.cs:96-111):
  - Each click starts with a full-scale impulse (line 106) and is high-passed at 2 kHz.
  - 65–77% of its energy is above 5 kHz, with a centroid around 6.3 kHz. It sounds like a digital tick rather than a ratchet clack.
  - Only three variants play in a fixed rotation (FightFeedback.cs:150-151), which gives the "machine-gun" effect. The bream fight had 70 clicks in 30 s.
- **Click loop** (FightSynth.cs:60-75):
  - Clicks are exactly 551 or 552 samples apart, a strict 40 Hz rate, with 70% volume pulsing at 40 Hz, the range the ear hears as roughest.
  - 62% of its energy is above 5 kHz.
  - Its pitch can reach 3× (FightCues.cs:493), which means 120 clicks a second with content up to 33 kHz, beyond what the output can reproduce cleanly.
- **Line sing** (FightSynth.cs:78-91): a pure 1.06–1.14 kHz tone with its octave at 2.1–2.3 kHz. It plays 11–21% of the time with bream and pike and is often the only sound during a run. That gives a ringing-ears effect, though it is quiet at about −38 dBFS.
- **Rod creak** (FightSynth.cs:114-131): an unsmoothed 70 Hz sawtooth that sounds like buzz on a phone. It never triggered in these demos.

## Worth keeping
- **Architecture:** FightCues is driven purely by FightFrame, so a spectator hears the same thing. It uses 6 sources, changes only pitch and volume, and the clips are deterministic.
- **Loop seams are clean.** At each wrap point the sample jump matches the clip's own regular edges:

  | Loop | Jump at the seam | Same jump inside the clip |
  |---|---|---|
  | Whir | 0.449 | 0.446 (median pulse edge) |
  | ClickLoop | 0.735 | 0.30–0.85 (click edges) |
  | Growl | 19th percentile | normal |
  | LineSing | 48th percentile (curvature) | normal |

- **Levels are healthy:**
  - Clip peaks are −0.4 to −3.1 dBFS.
  - The only meaningful DC offset is 0.02 in JerkThunk.
  - The mix peaks at −3.8 dBFS with no clipping.
  - No loop is ever cut off while audible.
  - Resampling artifacts above 11 kHz are only 0.2–0.4% of energy.
- **The meaning of the reel sounds is right:** reeling, straining and slipping each sound different, and pitch follows reel speed. When the perch jerks, the pitch dips from 1.12 to 0.96 as the reel slows, then recovers. That is good feedback; it just needs a better sound.
- **Good one-shots:**
  - LandedChime: a C-E-G sine chord, 99% of energy at 300–2000 Hz.
  - TelegraphSwirl: 87% at 300–2000 Hz.
  - SplashHeavy and Splash.
  - The Hookset thump, which has harmonics so it survives a phone speaker.
  - Splash volume scales with distance and fish weight.

## What this means for the fix
- Replace Whir with something soft and grainy:
  - A low-mid purr with randomized gear ticks.
  - Harmonics falling at least 12 dB per octave, and no single-sample edges.
  - Loops longer than 2 s, or several out of sync.
- Rebuild Growl with a steeper filter and real mid-range content of its own, so it still reads as strain on a phone. Replace the fixed 11 Hz tremolo with random variation, and make it quieter.
- Let pitch glide down on release instead of diving.
- Lower the bed relative to events, and add a quiet ambient layer under everything.

## WAV files
Folder: `F:\fishing\.previews\audio\current\`. Loops are tiled to about 4 s. The fight files are 48 kHz mixes; each also has a reel-only version (Whir + Growl + LineSing + ClickLoop) and separate Whir and Growl versions.

**Loops**
- clip_Whir_loop.wav
- clip_Growl_loop.wav
- clip_ClickLoop_loop.wav
- clip_LineSing_loop.wav

**One-shots**
- clip_DragClick0.wav, clip_DragClick1.wav, clip_DragClick2.wav
- clip_RodCreak0.wav, clip_RodCreak1.wav, clip_RodCreak2.wav
- clip_Splash.wav, clip_SplashHeavy.wav
- clip_TelegraphSwirl.wav, clip_JerkThunk.wav, clip_JumpWhoosh.wav
- clip_Hookset.wav, clip_LineSnap.wav, clip_RodCrack.wav
- clip_EscapePlip.wav, clip_LandedChime.wav

**As heard in game**
- clip_DragClick_rotation_12ps.wav (the three click variants rotating at 12 per second)
- clip_RodCreak_rotation.wav

**Fights**
- fightdemo_perch300_HoldReel.wav, fightdemo_perch300_HoldReel_reelbed.wav, fightdemo_perch300_HoldReel_whir.wav, fightdemo_perch300_HoldReel_growl.wav
- fightdemo_bream2kg_Technique.wav, fightdemo_bream2kg_Technique_reelbed.wav, fightdemo_bream2kg_Technique_whir.wav, fightdemo_bream2kg_Technique_growl.wav
- fightdemo_pike4kg_Technique.wav, fightdemo_pike4kg_Technique_reelbed.wav, fightdemo_pike4kg_Technique_whir.wav, fightdemo_pike4kg_Technique_growl.wav

Frame-by-frame logs of the demos are in `C:\Users\user\AppData\Local\Temp\claude\0047d7a0030b4a57\f76dbde7-8203-47cf-a932-3701622edb9c\scratchpad\harness\AudioDiag\bin\Release\net8.0\`: perch300_HoldReel_frames.csv, bream2kg_Technique_frames.csv and pike4kg_Technique_frames.csv.