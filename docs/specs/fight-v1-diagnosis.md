**Fight diagnosis: why "hold Reel and nothing happens" and why outcomes feel random**

How I checked: I read every file in the list and changed nothing in the repo. I compiled the real `FightModel.cs`, `SeededRandom.cs` and the data classes into a small console harness in the scratchpad. It ran 400 seeds per cell on the float tackle (rod reach 240 cm, line 1800 g, reel 55 cm/s, drag 900 g) at 5, 15 and 24 m. All numbers below are from those runs or from the formulas in the code.
- Harness: `C:\Users\user\AppData\Local\Temp\claude\0047d7a0030b4a57\f76dbde7-8203-47cf-a932-3701622edb9c\scratchpad\fightsim\Program.cs`
- Results: `C:\Users\user\AppData\Local\Temp\claude\0047d7a0030b4a57\f76dbde7-8203-47cf-a932-3701622edb9c\scratchpad\fightsim\batch.txt`
- Screen numbers assume a 2400×1080 landscape phone.

## Root causes, ranked by impact

**1. Beyond 6 m nothing on screen shows the fight.**
- **Float is hidden.** It sinks 0.25 m with a 35° tilt once the fight starts (TackleViews.cs:109-114, FishingController.cs:317). The antenna tip ends about 1 cm below the surface, and the water is opaque (Water.shader:16).
- **No ripples.** The fish and all its ripples are hidden beyond 6 m because `UpdateFight` returns before spawning them (FishView.cs:15, 55-59).
- **Line is invisible at range.** It is 6 mm wide (TackleViews.cs:64), which is 0.25-0.4 px at 15-24 m.
- **Motion is radial only.** Fish position is `_fightDirection * FishDistanceCm` (FishingController.cs:237, 259). FightModel has no lateral coordinate at all.
- **Reeling barely moves anything on screen.** The camera sits at 1.6 m height, 9° pitch, 55° field of view (TestLake.cs:38-39). Reeling at 0.55 m/s moves the fish point 1.5 px/s at 24 m, 3.6 px/s at 15 m and 7.5 px/s at 10 m. A full 2.4 m Lift moves it only 18 px at 15 m. For comparison, 1 m of sideways movement at 15 m would be 64 px.
- **The HUD covers the spot.** At 15-24 m the fish sits at 41-44% of screen height, directly behind the phase pill (35-46% of height, ±20% of width; LakeHud.cs:274, 293) and inside the tension gauge arc (LakeHud.cs:320-323).
- The only things that react to Reel are the amber button highlight and a gauge that hardly moves (cause 4).

**2. For common catches the fight is a fixed-speed conveyor. Phases, skill and drag do nothing.**
- **The drag never slips.** Fish force = weight × (0.5 + 0.1 × strength) (FightModel.cs:136). Reel multiplies it by 1.6 (70, 233-235). With drag at 900 g (FishingController.cs:113):

  | Fish | Max force | With Reel, Run phase | With Reel, Jerk phase |
  |---|---|---|---|
  | Roach 120 g | 84 g | 134 g | — |
  | Perch 300 g | 270 g | 432 g | 648 g |
  | Crucian 500 g | 400 g | 640 g | 960 g (rare, only in Jerk) |

  A fish only takes line once its Run force with Reel exceeds 900 g, which needs about 560-800 g depending on species. The typical weights in `fish.json` (roach 120, perch 150, crucian 250) never get there.
- **The fish is glued to the line end.** When the drag holds, `FishDistanceCm = LineOutCm - angle × reach` (FightModel.cs:248-249). A "running" fish moves exactly 0.
- **Time to land is fixed at (distance − 2 m) / 0.55 m/s.** That is 5.5, 23.6 and 40 s for 5, 15 and 24 m, with zero spread across seeds (10th and 90th percentiles equal the median).
- **The phase text contradicts the motion.** During "Рыба уходит / рывок! / тянет вглубь" the fish is actually getting closer 100% of the time.
- **Doing nothing freezes the fish forever.** With no buttons held, roach, perch and crucian sit motionless for 600 s: no run, no slack, no escape.
- **Skill barely matters.** The skilled "Careful" strategy from the tests is only about 10% faster (perch at 15 m: 21 s vs 24 s), with equal risk.

**3. For a big fish (bream 2 kg), Reel does nothing while the fish takes line, and the outcome is a coin flip.**
- **The drag slips in every active phase.** Run force is 2000 g and Dive 2500 g, well over 900, even with no buttons.
- **While slipping, the fish swims away at full speed.** It moves at its phase speed (90 cm/s Run, 54 cm/s Dive) and the line pays out to match (FightModel.cs:236-244, 218, 324-326). Reeling gains nothing and wears the line 0.02/s (89, 240-241). The break check uses 1800 × (1 − wear) (265), so at 900 g drag the line snaps after 25 s of reeling into a slipping drag.
- **Same input, random result.** Holding Reel gives 53% broken line (median 42 s) and 48% landed (73-81 s) at every distance. Whether the fish tires before wear reaches 0.5 depends only on the seeded phase sequence. Wear at the end averages 0.49.
- **The fish drifts out while you reel.** It goes from 15 m to 21.9 m median (24 m at the 90th percentile). It never gets closer during an active phase (0% of active ticks); all gains come in Rest.
- **The exhaustion clock ignores the player.** Tension is pinned at the drag, and stamina drains in proportion to tension (253). The fish tires at 41.3 s with hold-Reel, 41.3 s with pump and 40.4 s with Careful. Only the drag setting changes it: 500 g gives 63 s, 1300 g gives 32 s.
- **The drag setting behaves backwards.** With hold-Reel, the share of broken lines by drag is:

  | Drag | 500 | 700 | 900 | 1100 | 1300-1700 |
  |---|---|---|---|---|---|
  | Line broke | 100% | 88% | 50% | 98% | 100% (1700 breaks in 4.4 s) |

  The loss card says "Ослабь фрикцион и не тяни на рывках" (LakeHud.cs:387). That advice is wrong here: a bream ("heavy" style) has no Jerk phase (FightModel.cs:341-345), and line wear is what kills the line.
- Typical-weight fish behave normally: bream 800 g and perch 1000 g land 100% in 37-39 s with 15% slipping.

**4. The gauge and rod cannot show what is happening.**
- **Danger zones are unreachable.** The gauge is tension / (1800 × (1 − wear)) (LakeHud.cs:322-323), and tension never exceeds the drag (FightModel.cs:236-239). The jerk shock adds at most 0.3 × drag (258). At default drag the gauge maxes out at 50-65%, while orange starts at 60% and red at 85% (LakeHud.cs:500-504). Red is reachable only through hidden line wear.
- **Common fish barely light the gauge** (average / peak, out of 22 segments):
  - roach: 2% / 7%
  - perch 300 g: 7% / 26%
  - crucian 500 g: 13% / 36%
- **A slipping bream always reads "0,9 кг".** The arc then creeps from 50% to 100% purely through wear while the number stays the same. It turns orange after about 8 s of reeling into a slipping drag and red after about 20 s, with no explanation.
- **Tension is flat within each phase.** Force × phase factor × stamina (217) only steps every 1.5-5 s. There are no head-shakes or oscillation for the player to read and react to.
- **The jerk spike lasts one tick.** The shock exists for 50 ms (255-259). For a perch it adds 43 g, about 2% of the gauge.
- **Rod bend is barely visible.** Bend = 0.04 + 0.45 × load (TackleViews.cs:38), with load = RodLoad / 2500 (FishingController.cs:300).
  - Roach and perch average 1-5% load, so the tip moves about 5-15 px.
  - During the bite the code forces load 0.12, so on hookset of a small fish the rod straightens.
  - The rod-load bar averages 1-15% for common fish (LakeHud.cs:338-342).

**5. Almost every event has no audio or haptic signal.**
- **No sound exists anywhere in the project.** There is no AudioSource or AudioClip. The design doc promises reel sound and drag clicking (game-design-v0.md:122-124).
- **Haptics are one fixed-length `Handheld.Vibrate()`.** It fires on bite, hookset, entering Jerk, and loss (FishingController.cs:211, 240, 253-254, 270, 343-348).
- **A bream fight gets zero vibrations** in 77 s, because the "heavy" style has no Jerk. A perch at 15 m gets 2.6.
- **These events are text-only or have no signal at all:**
  - Run, Dive and Rest starts: text only.
  - Drag slipping: one amber text line (LakeHud.cs:300-301), hidden whenever slack shows (298).
  - Slack: red text with a seconds counter, but not the species limit of 1.5-3.5 s (FightModel.cs:336-360).
  - Stamina: nothing until below 0.15 ("Рыба сдаётся", LakeHud.cs:292).
  - Line wear: only the shrinking "из X кг".
  - The Rest window, which is the moment to pump: nothing beyond the word "отдыхает".
- **No distance readout before 6 m** (LakeHud.cs:302-303). The only accidental cue is the depth pill in the top bar.

**6. Pumping doesn't pay off, can't be seen, and the natural rhythm loses the fish.**
- **Lift is the strongest motion but brings no gain overall.** Lifting pulls the fish 240 cm in 0.8 s, which is 3 m/s (FightModel.cs:56, 75).
  - Lowering with Reel then takes 240 / (1.1 × 55) = 3.97 s (79, 206-208).
  - During the lowering the fish creeps back 5.5 cm/s (−55 + 60.5).
  - Net pump rate is 0.46-0.50 m/s, below the 0.55 m/s of simply holding Reel. Perch at 15 m: 25 s with pump vs 23.7 s holding Reel; at 24 m, 44 s vs 40 s.
- **Pumping is safer for a bream, but invisibly.** Lift-then-lower-with-Reel lands 99% of bream in 78-97 s, versus 48% for hold-Reel. The only reason is less reeling into a slipping drag. The HUD shows the same "0,9 кг" either way.
- **Lift and Reel together slips the drag even in Rest.** Raw tension becomes force × 2.1: 500 × 2.1 = 1050 g > 900 on a fresh bream. Lift alone gives 750 g and holds.
- **The beginner thumb rhythm guarantees slack.** Lift, release both, then Reel drops the rod in 0.6 s (77). That creates 240 cm of slack, past the 30 cm tolerance in about 75 ms (81, 225).
  - Reeling at 55 cm/s needs about 3.8 s to recover. The escape limit is 2.0 s for jerky fish and 3.0 s for steady fish (271-272).
  - Result: perch 300 g escapes 100% of the time at 15-24 m, median 6.2 s.
  - Roach and crucian survive but spend 70%+ of the fight on slack.
  - A bream never lands this way. It runs 78 m out, because stamina recovers on slack (229).
  - Whether the fish survives depends on whether it happened to be running and took up the slack, so the escape feels random.

**7. The fish is visible near shore only briefly, and almost always already exhausted.**
- **The window is at most 7.3 s.** The fish is visible below 6 m and auto-landed at 2 m (FishView.cs:15; FightModel.cs:80, 280-281), which is 4 m at 0.55 m/s.
- **Exhaustion is permanent.** Once stamina drops below 0.15 the phase is always Rest (288-291). Stamina only recovers on slack (229) and drains under any tension (253), so a tired fish can never surge back to the deep near shore.
- **Share of visible time the fish is already exhausted (hold Reel):**

  | Fish | 15 m cast | 24 m cast |
  |---|---|---|
  | Roach | 100% | 100% |
  | Perch 300 g | 72% | 100% |
  | Crucian 500 g | 53% | 100% |
  | Bream 2 kg | 100% | 100% |

  The "still fighting near shore" look the user asked for appears only on 5 m casts, where the fish is fresh from the first tick.
- **There is no landing step.** The fish is landed automatically at 2 m.

**8. The strike flows straight into the fight without feedback.**
- **Every strike hooks.** Any Lift press within 1.3 s hooks with certainty (FishingController.cs:26, 218-221).
- **The fight starts with no slack and the fish in Run** (FightModel.cs:144; FishingController.cs:236).
- **The strike Lift is wasted.** The thumb is still on Lift, so the model pulls the fish up to 2.4 m at 3 m/s. On screen that is 18 px at 15 m.
- **A tap-strike causes slack.** If the player treats the strike as a tap and releases, the rod drops and makes slack. The sim shows 1-16% of ticks on slack, and bream line breaks still at 38%.
- **The hookset gives no distinct signal.** It uses the same vibration as the bite, and the centre text switches straight to "Рыба уходит". The rod visibly un-bends for small fish (cause 4).

**9. Smaller issues**
- The HUD hardcodes the 30 cm slack threshold (LakeHud.cs:298) instead of using `SlackToleranceCm`.
- Line sag is SlackCm / 150 (FishingController.cs:310-312), so at the 30 cm danger point sag is only 0.2 on a sub-pixel line.
- The model has no inertia: speeds switch instantly between 0, 0.55 and 3 m/s, so the fish never feels heavy.
- The design doc's swipe-up strike (game-design-v0.md:50) is implemented as a Lift press instead.

## Perch 300 g and bream 2 kg at 15 m, as the player experiences them

| Fight and strategy | Time and result | What the player sees |
|---|---|---|
| Perch, hold Reel | 23.7 s every seed, landed | Pill cycles "уходит / отдыхает / рывок!"; gauge 4-26%; second line empty for 69% of the fight; fish appears at t = 16.4 s already nearly spent (stamina 0.20), on its side for 72% of the visible time |
| Perch, pump | Lift then lower with Reel: 25 s. Lift+Reel then lower: 20 s. Careful: 21 s | Same flat HUD as above |
| Perch, lift / release / reel | 100% escaped, median 6.2 s | Red slack timer that reeling can't fix |
| Bream, hold Reel | 53% line broke at ~42 s; 48% landed at 77 s | Amber "Фрикцион трещит" for 46% of the fight; gauge fixed at 0,9 кг with the arc creeping to red through wear; no vibration; nothing visible until ~70 s, then an exhausted fish |
| Bream, pump (Lift then lower with Reel) | 99% landed, 87 s | Same HUD, so the player can't tell why it worked |
| Bream, pump (Lift+Reel together) | 55% line broke | Same as hold Reel |
| Bream, Careful | 100% landed, 58 s | Same HUD |

## What already works and should be kept

- **The model's foundations.** FightModel is deterministic and engine-free, with a seeded RNG, a documented RNG call order, a fixed 0.05 s tick, and a same-seed replay test. The controller's fixed-step accumulator (FishingController.cs:247-257) fits server replay and spectating.
- **The physics vocabulary.** Drag caps tension and slips; the raised rod is loaded harder than the line (lever factor); reeling into a slipping drag wears the line; slack escape timers differ by fight style; a jump on slack can shake the hook; stamina drains under tension and recovers on slack; each species has its own phase table. The failure modes (line break, rod break, empty spool, escape) are all covered by tests.
- **Lift as a fast 2.4 m pull.** It is the right core gesture; it just needs to pay off and be visible.
- **Skill matters for big fish.** The Careful strategy lands a 2 kg bream 100% versus 50% for holding Reel; that difference only needs to be visible.
- **FishView near-shore poses.** On its side when exhausted, upright when tired, thrashing with ripples every 0.18 s when active, a hop on jumps, all driven by model state (FishView.cs:61-88). This matches the co-op rule; only the timing window is wrong.
- **The HUD and input pieces.** Gauge with a drag tick and a wear-adjusted "из X кг"; rod-load bar; loss cards that name the cause (the bream hint needs fixing); multitouch two-thumb input with keyboard fallback; drag ± buttons; rod angle and bend and line sag driven by model state, so a spectator sees the same thing.
- **FightSandbox** (`Scripts/Unity/Sandbox/FightSandbox.cs`) is a ready tool for re-tuning the numbers.