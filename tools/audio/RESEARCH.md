**Fishing reel and fight audio: 20 procedural recipes for an ASMR feel**

I measured the current clips and checked them against bot fights before writing the recipes. I also built a small prototype of some of the new recipes, rendered from the same fights, and measured it. I have not listened to any of these renders, so the A/B files below still need a listen.

## Why the current retrieve sound is unpleasant (measured)
- **The reel sound is a constant buzz.** While cranking, reel/C0 sits at a median of 0.92–0.99 (Roach, Perch, Bream, Pike; 3 seeds each). So WhirPitch stays at about 1.37, and the 220 Hz pulse train (20% duty, no anti-aliasing) becomes a steady ~300 Hz buzz. That buzz runs for 34–64% of every fight, with a 4 Hz "gear" wobble at 20% depth on top.
- **Brightness is not the problem.** The old reel mix has only about 3% of its energy in 2–5 kHz, and its spectral centroid is 440–490 Hz. The character is wrong: a static, synthetic buzz with low wobble. EQ alone will not fix it.
- **Growl is mostly inaudible rumble.** 51% of its energy is below 150 Hz, which phone speakers can't play, and it has an 11 Hz scrape modulation. It is audible during 15–83% of cranking time.
- **Drag clicks are harsh.** The click has a centroid of 6.7 kHz, with 93% of its energy above 2 kHz. Above 25 clicks/s it switches to a loop that gets pitched up as far as 3×.
- **Line sing is a tonal whine.** It is a pure tone at 1.07–1.6 kHz, with a harmonic reaching up to 3.2 kHz and a 6 Hz tremolo. It is only audible in 3–13% of cranking.
- **Rod creak can't be heard on phones.** 82% of its energy is below 150 Hz.
- **Slip speeds in bot fights:** median 26–30 cm/s, 90th percentile 44–54 cm/s, maximum 106 cm/s.

## Recipes and rules
1. **Drive the retrieve with discrete grains, not a pitch-shifted loop.**
   - Fire one "rotor pass" grain every 14 cm reeled. A real reel takes in about 85 cm of line per handle turn, and the rotor turns 5.6–6.2 times per handle turn.
   - Fire one handle "tock" every 85 cm, offset 40 cm from the rotor grid.
   - That gives 7.9 grains/s at C0 = 110 cm/s and 18 grains/s at C0 = 250 cm/s.
   - Timing jitter ±4% of the period; gain jitter ±1.5 dB.
   - Sources: https://www.saltstrong.com/?p=37500 ; https://www.mcvuk.com/development/how-to-make-racing-car-engines-roar-using-audiomotors-fmod (grains timed per engine cycle instead of pitch-shifted loops). In https://ar5iv.arxiv.org/html/2504.00621, stronger cyclic patterns and smoothly spread spectra increased the effect, and the irregularly timed ("stochastically regulated") clip was rated most natural.

2. **Rotor grain recipe.**
   - 75 ms of white noise through a 2-pole band-pass at 620 Hz ±10%, Q 1.1.
   - Envelope: raised-cosine attack of 6 ms, then exponential decay with τ = 14 ms ±15%.
   - Add a quiet line-roller tick at −13 dB: modes at 1150 and 1720 Hz (±4%), τ = 5 and 2.5 ms. Low-pass at 3.2 kHz, high-pass at 90 Hz.
   - Gain = 0.55 + 0.45 × min(1, ReelIn/C0).
   - Under a continuous bed of the same band-passed noise (2 s seamless loop), set the bed at −12 dB relative to grain peaks and scale its volume with speed. The rhythm then sits at about 50% modulation depth instead of having gaps.
   - Under load: band-pass centre × (1 − 0.15·ReelLoad), τ × (1 + 0.5·ReelLoad).
   - Measured: centroid 890 Hz, 6.6% of energy in 2–5 kHz. Source: engineering judgement.

3. **Handle tock (wood/plastic, not metal).**
   - Three modes: 420, 980 and 1650 Hz, with τ = 18, 9 and 4 ms and amplitudes 1, 0.55 and 0.22.
   - Excite with a 0.6 ms raised-cosine noise burst; low-pass at 2.8 kHz; play it 10 dB under the rotor grains.
   - Decay follows tanφ = 1/(π·f·τ), which gives about 0.03–0.05 here. Damping is what separates steel and glass from wood and plastic, so keep tanφ above 0.005 on anything that repeats; lower values ring like metal.
   - Source: https://brungio.github.io/BLG_SMC_2006_JASA.pdf

4. **Replace Growl with stick-slip strain.**
   - No events while ReelLoad is below 0.4. Map ReelLoad 0.4→1 to a slip rate of 3→60 per second, with irregular timing (each interval scaled by 0.5–2×).
   - Each event's amplitude is √(force built up since the last slip), with decay = 10 ms × √amplitude and a 1 ms attack.
   - Pass the events through a resonator bank at 250, 395, 560, 790 and 1120 Hz with Q 2–3. These are the door-creak formants (62.5–790 Hz) shifted up, partly so phones can play them.
   - Bake three seamless 1.2 s loops at force 0.4, 0.7 and 1.0, and crossfade between them by volume.
   - Sources: https://en.wikibooks.org/wiki/Designing_Sound_in_SuperCollider/Creaking_door ; https://en.wikipedia.org/wiki/Missing_fundamental (phones play almost nothing below about 300 Hz).

5. **Drag tick recipe (pawl striking a toothed disc).**
   - A body mode at 380 Hz (τ 7 ms, amplitude 0.25), plus modes at 1250 Hz (5 ms, 1.0), 2050 Hz (3 ms, 0.7) and 3100 Hz (1.2 ms, 0.35).
   - Jitter: frequency ±4%, τ ±15%, amplitude ±12%. Exciter: 0.3 ms noise burst at 0.12.
   - Low-pass at 4.5 kHz for bright variants and 2.5 kHz for dark ones. Make 16 variants.
   - Measured: centroid 1.27 kHz, 15% of energy in 2–5 kHz, and that energy is only in the first few ms.
   - Avoid the STK guiro/wrench ratchet resonances (2.5–8 kHz) for anything heard over and over.
   - Sources: https://patents.google.com/patent/US6854677 (mechanism) ; https://sources.debian.org/src/stk/5.0.1%2Bdfsg-1/src/Shakers.cpp

6. **Drag tick scheduling.**
   - One tick per 1.2 cm slipped. That is roughly 12 teeth on a spool of about 14 cm circumference, an engineering estimate.
   - In bot fights this gives 22 ticks/s at median slip, 42/s at the 90th percentile and 85/s at maximum.
   - Play every tick as a grain and drop the switch to a loop.
   - Above 15 ticks/s, multiply each tick's gain by √(15/rate) and use dark variants with probability (rate − 15)/45.
   - Timing jitter ±5% at most, because a ratchet is regular. Accent the first 3 ticks of each run by +2 dB.
   - Below about 12/s the ticks are distinct and countable, which is the most ASMR-like range. 20–150 Hz is where repetition is heard as roughness (peak at 70 Hz), so a fast run's "zzz" should be darker and quieter.
   - Source: https://metricgate.com/docs/roughness-asper/

7. **Replace LineSing with line hum.**
   - Band-passed noise partials at 320 Hz (Q 14), 643 Hz (Q 10, −7 dB) and 1290 Hz (Q 6, −18 dB), built as a 2 s circular loop.
   - Slow "breathing" at 0.5 Hz, ±15% (one cycle per loop, so it stays seamless).
   - Pitch = 1 + 0.9 × clamp(x − 0.55, 0, 0.75). Volume = 0.16 × clamp((x − 0.55)/0.4, 0, 1).
   - Measured: centroid 505 Hz, 1.7% of energy in 2–5 kHz. No tremolo between 2 and 8 Hz.
   - Source: https://metricgate.com/docs/fluctuation-strength-vacil/ (fluctuation strength peaks at 4 Hz).

8. **Rod creak.**
   - Use the stick-slip model from recipe 4 with force = (RodLoadShare − 0.75)/0.25, a rate of 1–80 per second, and a resonator bank at 180, 320, 520, 760 and 1100 Hz with Q 2–4. High-pass at 125 Hz.
   - Each creak lasts 0.25–0.5 s; make 6 variants at 2 loudness levels.
   - Source: Wikibooks creaking-door page (recipe 4).

9. **Bubble primitive for all water sounds.**
   - f₀·r ≈ 3 m/s, with p(t) = A₀·sin(2π·f(t)·t)·e^(−βt) and f(t) = f₀(1 + ξβt), ξ = 0.1, A₀ ∝ r.
   - Radius gives pitch: 2 mm → 1.5 kHz, 4 mm → 750 Hz, 8 mm → 375 Hz.
   - Decay τ = 6 ms + 4 ms × r in mm (engineering).
   - Escape plip: one bubble with r = 3.3 mm (900 Hz). Lure plop: r = 6–9 mm plus a 15 ms noise burst.
   - Source: https://gamma.cs.unc.edu/SoundingLiquids/soundingliquids.pdf

10. **Splash.**
    - Initial noise burst: 2 ms attack, decay τ = 35 ms (60 ms heavy), band 250–2400 Hz (1600 Hz low-pass for heavy), mixed at 0.55.
    - Then a bubble cloud: 11 bubbles (18 heavy), onset times spread exponentially with τ = 120 ms, radii drawn from p(r) ∝ r^−2.5 over 1.6–8 mm (3–14 mm heavy), gain ±30%.
    - Heavy splashes add a 90 Hz thump with 180 and 270 Hz harmonics.
    - With distance: low-pass from 2.4 kHz down to 900 Hz, and −6 dB per doubling beyond 5 m.
    - Measured: light splash centroid 1.1 kHz with 1.9% in 2–5 kHz; heavy splash centroid 420 Hz.
    - Sources: Sounding Liquids (recipe 9); STK water drops (450/600/750 Hz).

11. **Lake ambience.**
    - Lapping: noise low-passed at 500 Hz with slow amplitude swells at 0.15–0.3 Hz, 40% depth.
    - Random bubbles 0.5–2 per second, r = 3–10 mm, at −30 dB.
    - The whole bed sits at least 14 dB under the reel. Build it as a circular loop of 8–12 s or schedule it as events.
    - Leave room for silence; the reel is the main voice.
    - Source: https://www.vice.com/en/article/cast-n-chill-is-an-idle-and-active-fishing-game-that-has-become-my-new-go-to-when-i-want-to-relax-review/ (the reviewer praises silence broken only by the line).

12. **Variation and determinism.**
    - Pre-render 12–16 variants per repeated sound, and add ±1.5 dB gain per play.
    - Pitch variation ±0.3 semitone at most for mechanical sounds. General SFX advice is ±2 semitones and at least 3–5 variants, but a ratchet has to sound consistent.
    - Pick variants random-exclusive (never the same twice in a row), with variant = hash(event index). Event indices come from distances integrated from FightFrame, so a spectating friend hears identical sounds.
    - Sources: https://sfxengine.com/blog/common-game-audio-mistakes ; https://www.microsoft.com/en-us/research/?p=186041

13. **Spectral budget.**
    - Repeated and continuous layers: centroid 400–1300 Hz, at most 10% of energy in 2–5 kHz (hearing is most sensitive there), nothing above 5 kHz except the first milliseconds of transients.
    - High-pass everything at 90–120 Hz. Give "weight" through harmonics 2–4 in the 180–600 Hz range.
    - Keep brightness for rare one-shots such as the line snap.
    - Sources: https://en.wikipedia.org/wiki/Equal-loudness_contour ; Missing fundamental page ; https://www.johndcook.com/blog/2016/07/28/quantifying-how-annoying-a-sound-is/ (Zwicker annoyance: sharpness counts above 1.75 acum; fluctuation and roughness are weighted 0.4/0.6).

14. **Modulation rules.**
    - No periodic amplitude modulation deeper than 10% at 2–8 Hz or 20–150 Hz on sustained layers. Today's whir (4 Hz), growl (11 Hz) and sing (6 Hz) all break this.
    - Use drift below 0.5 Hz, or rhythm made of discrete events.
    - Sources: the two metricgate pages (recipes 6 and 7).

15. **Clip hygiene.**
    - No naive pulse or saw waveforms: use additive synthesis, or polyBLEP around each edge (−(1−x)² just after the jump, (x+1)² just before it).
    - Keep clip content below 0.45 × 22050 / maximum pitch.
    - Use loops only for noise beds: at least 2 s, with filter state and event tails wrapped around the loop, or a 30–50 ms equal-power crossfade.
    - Fade in over 0.2–0.6 ms with a raised cosine and fade out over 3–20 ms. Exciters should be raised-cosine bursts of at least 0.3 ms.
    - Source: https://paulbatchelor.github.io/sndkit/blep

16. **Delivery in Unity.**
    - Use one of the 6 AudioSources with no clip and an OnAudioFilterRead grain mixer, holding up to 16 voices. Pre-render the grains at AudioSettings.outputSampleRate.
    - On the main thread, turn each FightFrame into events with times interpolated inside the frame, stamp them dspTime + about 35 ms, and push them to a lock-free ring buffer.
    - The reason: at 60 fps a frame lasts 16.7 ms, which is up to 30% of the rotor period at 18 grains/s. Playing grains at frame time would sound like limping.
    - OnAudioFilterRead runs on the audio thread and is not supported on the Web platform.
    - Source: https://docs.unity.com/en-us/engine/6000.3/script-reference/unityengine/monobehaviour/onaudiofilterread.md

17. **Space and closeness.**
    - Reel layers stay dry and close, with a +2–3 dB low shelf at 250 Hz. Water and fish sounds are distant.
    - Put a far-shore echo only on the one-shot source: AudioEchoFilter with delay 180–260 ms, decayRatio 0.2–0.3, wetMix 0.10–0.15 (engineering).
    - Or bake a short reverb tail into the splash clips: 4 combs of 558, 594, 638 and 678 samples, feedback 0.84, damping 0.2, plus allpasses of 278 and 220 samples at g = 0.5, wet at −18 dB. These are the Freeverb defaults halved for 22.05 kHz.
    - Sources: https://github.com/sinshu/freeverb/blob/main/Components/tuning.h ; https://discovermagazine.com/mind/relaxing-asmr-sounds-are-harder-to-re-create-than-youd-think (close microphone placement creates presence).

18. **Loudness and mix.**
    - About −16 LUFS integrated on the device, true peak ≤ −1 dBTP, loudness range 5–10 LU.
    - Drag ticks peak about +3 dB over the retrieve.
    - Put a cubic soft clip (x − x³/3) on the listener as a safety, with normal peaks kept below 0.5.
    - Sources: https://auphonic.com/blog/2013/01/07/loudness-targets-mobile-audio-podcasts-radio-tv/ ; https://designingsound.org/2013/02/20/different-loudness-ranges-for-console-and-mobile-games ; https://ccrma.stanford.edu/~jos/pasp/Cubic_Soft_Clipper.html

19. **Reward contrast.**
    - Keep the middle of the fight quiet and effortful, then make the landing warm: bubble splash, a few wet flops at 6–9 Hz, then a chime whose length scales with the fish's weight percentile. A personal best gets an extra tier.
    - Don't celebrate a fish that gets away.
    - Source: engineering judgement.

20. **Later option: real CC0 recordings.**
    - The VSCO ratchet sample (Freesound #375708) is CC0. It could be analysed with the Microsoft modal method to tune the tick modes.
    - The Duke Sonic Dictionary reel recording is CC BY-NC, so it can't be used in a commercial game.
    - Sources: https://freesound.org/people/Samulis/sounds/375708/ ; https://sonicdictionary.duke.edu/tags/reels.html

## Gaps
- **No primary audio sources for the named games.** I found no first-party audio write-ups for Dredge, Sea of Thieves, Stardew Valley, Animal Crossing: New Horizons, RF4 or Fishing Planet, so the recipes make no claims about how those games do it.
- **No ASMR study of fishing reels.** The ASMR evidence is general: cyclic patterns, smoothly spread spectra, and close microphone placement.
- **The prototype is partial and unheard.** It covers recipes 2, 3, 5, 6, 7, 9 and 10, without the bed from recipe 2. Measured: the new mix has a centroid of about 1.0 kHz with 7.6–8% of its energy in 2–5 kHz, against 440–490 Hz and about 3% for the old mix.

Files are in C:\Users\user\AppData\Local\Temp\claude\0047d7a0030b4a57\f76dbde7-8203-47cf-a932-3701622edb9c\scratchpad\harness:
- `AsmrProto\Dsp.cs` — prototype recipes
- `AsmrProto\Program.cs` — fight-driven grain scheduling
- `AsmrProto\Old.cs` — current-sound re-render for comparison
- `AsmrProto\out\ab_bream_old_then_new.wav` — level-matched A/B: 20 s old, 1 s silence, 20 s new
- `AsmrProto\out\ab_pike_old_then_new.wav` — same for the pike fight
- `AsmrProto\out\splash.wav`
- `AsmrProto\out\splashheavy.wav`
- `AsmrStats\Program.cs` — bot-fight statistics
- `AsmrResearch\analyze.py` and `AsmrProto\analyze2.py` — spectral and modulation analysis