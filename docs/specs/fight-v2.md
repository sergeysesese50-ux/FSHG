# Fight v2: implementation spec

**Base and grafts.** This spec is built on "Living Line": a force balance, Lift that pulls with a force, and a rod that holds its angle. It adds:
- from "Honest winch": drag stiction (breakaway), a rod that gives way only under load, the line-out counter, RNG used only for fish behaviour, the events bitmask and the bot bands;
- from «Тянет — отдай»: `FightReport` causes, button-state cues, −/+ pills and invariant tests.

**Numbers are first guesses.** Every number here is a hand-computed default. Step 1 is not done until the balance bands in §3.7 pass.

## 1. Pitch and first-time loop

Every Reel press visibly and audibly does one of three things:
- it **winds**: the counter goes green ↓ and the whir is high;
- it **labours**: the handle slows, a growl plays and the counter reads «= держишь»;
- it **slips**: the spool turns backward, the drag ratchets and the counter goes amber ↑.

Every move the fish makes is announced about 0.5 s ahead, on the rod tip, on the float and in the hand. You win by giving the right answer: let the drag work on runs, pump in rests, keep the line tight through jumps, and net a fish whose head is up.

**First fish (roach or perch on the float rod):**
1. Strike: the rod hoops, a thunk plays, a haptic fires and «Чётко!» appears.
2. Hold Reel: «15,0 м ↓0,9 м/с» ticks down, the float skates in with a wake and the rod tip throbs.
3. On a Rest telegraph, the Lift button pulses green with «Рыба встала — подтягивай!». Lifting jerks the float 1–2 m closer.
4. The fish appears at 8 m, splashing.
5. At about 2.9 m, Lift reads «Выудить» and pressing it swings the fish out.

This takes about 15 s and the fish practically cannot be lost.

**First fish heavier than the drag (bream 2 kg):** Reel makes the spool spin backward with a ratchet. The button turns amber with «вхолостую», and the coach shows «Фрикцион отдаёт — не крути». A loss ends on a card that names the cause in numbers and gives one fix.

## 2. Controls

**Left «Подтянуть»** (hold; key F). This is the lift motor.
- The rod rises as fast as the fish allows and never slips the drag.
- **When released, the rod holds its angle.** It moves toward the fish only under load.
- During the bite it is the strike.
- When `CanSwing` is true it is labelled «Выудить».

**Right «Подмотка»** (hold; key J). This is the reel motor.
- With the rod up and Lift not held, it lowers the rod. This is the pump return: the fish is held and the line counter falls.
- With the rod down, it winds the fish in. It slows under load but never drops below 10% speed while the drag holds.
- While the drag slips, it gains nothing and twists the line.

**Both buttons:** lift and wind together.

**Drag:**
- −/+ pills sit directly above Reel. Each step is 50 g, and a long press repeats at 8 steps/s (keys Q/E).
- On hookset the drag is set automatically: `AutoDragG = floor50(0.6·min(Line, RodMax/1.3))`. That gives 1050 g on the float rod, 2400 g on spinning with 0.20 line and 2750 g on spinning with 0.30 line.

**«Подсачек»** (key Space).
- It appears when `InNetReach` is true and the fish is above the swing limit.
- It glows green exactly when `CanNet` is true.
- A tap sends `FightInput.Net` as a one-tick edge.

**Strike quality:**
- Lift within 0.8 s of the bite: hook hold 1.0, «Чётко!».
- Lift at 0.8–1.3 s: hook hold 0.75, «Поздно».

## 3. FightModel (Fishing.Core, pure C#)

### 3.1 Data and inputs

- `FightInput` gains `bool Net`.
- `FightTackle` gains `ReelFightSpeedCmPerSec` (C0), `ReelPowerG` (Tr), and `RodPowerG = 0.6·RodMaxLoadG`.
- `tackle.json` reels gain `fightSpeedCmPerSec` and `powerG`:
  - reel_1000: 130 / 600;
  - reel_2500: 150 / 2000.
- **Do not touch `retrieveCmPerSec`.** The waiting-state reel-in uses it.
- New constructor:

  ```
  (species, weightG, tackle, startDistanceCm, IRandom, settings, initialSlackCm, double hookHold = 1, uint visualSeed = 0)
  ```

  The fight starts with `RodAngle` 0.5 and `LineOut = start + 0.5·reach + slack`.

### 3.2 Fish

- `F = W(0.5+0.1·str)` and `V = 30+12·str` are unchanged.
- Body drag: `b = 0.7·F/V`, in g per cm/s.
- Target pull:

  ```
  P* = F·PF·(0.3+0.7S)·(1 + A·tri(f·Tick·dt)),   tri(x) = 4|x−floor(x)−0.5| − 1
  ```

  `Tick` is an integer counter. Do not use Sin, Exp or Pow anywhere.
- Telegraph pre-load: PF blends toward the next phase's PF by `0.3·TelegraphProgress`.
- Pull eases toward the target: `P += (P*−P)·min(1, dt/τ)`. Initialise P to P* at construction.

| Phase | PF | τ (s) | Duration (s) | Lead before entering (s) |
|---|---|---|---|---|
| Rest | 0.15 | 0.4 | 1.2–2.8 | 0.3 |
| Run | 1.0 | 0.3 | 1.2–3.0 | 0.5 |
| Jerk | 1.6 | 0.05 | 0.3–0.5 | 0.5 |
| Dive | 1.25 | 0.4 | 1.5–3.5 | 0.5 |
| Jump | −0.5 (toward the angler) | 0.1 | 0.7–1.0 | 0.6 |
| Surge (new) | 1.4 | 0.2 | 1.5 fixed | 0.6 |

Head-shake frequency and amplitude (f Hz / A) by style:

| Style | f / A |
|---|---|
| steady | 2.5 / 0.15 |
| jerky | 3 / 0.3 |
| heavy | 0.8 / 0.1 |
| deep | 1.2 / 0.2 |
| jumps | 1.8 / 0.4 |

The style phase weights stay as they are.

### 3.3 Tension solve (tight line, `SlackCm` ≤ 30)

**Motors.**
- Reel: `c(T) = C0·max(0.1, 1−T/Tr)` while Reel is held.
- Lift: `l(T) = L0·max(0, 1−T/Ta)` while Lift is held and the angle is below 1.
- `L0 = reach/0.8 s`; `Ta = min(0.9·drag, RodPowerG)`.

**Mode (a): rod at 0 with Reel, or Lift held.**
- The fish comes in at `s = c + l`, with `T = P + b·s`.
- Closed form over the active motors: `T = (P + b(C0+L0)) / (1 + b(C0/Tr + L0/Ta))`.
- If a motor hits its clamp, fix its speed at the clamp and re-solve once (the equation is linear).
- Per tick: `LineOut −= c·dt`, `RodAngle += l·dt/reach`, `FishDistance −= s·dt`.
- With Lift only and the rod at 1, the line simply holds: `T = max(P,0)`.

**Mode (b): pump return (Reel, no Lift, angle above 0).**
- `T = max(P,0)`.
- `RodAngle −= c(T)·dt/reach` and `LineOut −= c·dt`.
- The fish is held.

**Mode (c): no buttons.**
- `T = max(P,0)` and the line is locked.
- The rod gives way: `angle −= 0.35·min(1, T/(0.25·drag))·(Rest ? 0.3 : 1)·dt`. The fish moves out by the same reach.
- This mode never creates slack.

**Negative tension.** If the solved T is below 0 (a jump), set T = 0. The fish moves toward the angler at −P/b, and slack follows from the geometry.

**Drag stiction.**
- `breakaway = wasSlipping ? drag : 1.2·drag`.
- If T exceeds breakaway, set `IsDragSlipping`:
  - `T = drag`;
  - fish speed `u = (drag−P)/b`, where negative means it takes line;
  - `LineOut += −u·dt`;
  - the reel takes in nothing and the rod stays where it is.
- On the tick the drag starts slipping, queue a shock of 0.2·drag and raise `Breakaway`.
- Lift alone can never slip the drag.

**Slack (more than 30 cm).**
- `T = 0` and the fish swims free at −P/b.
- Reel takes in line at C0.
- Lift raises the rod at L0/reach per second. This is the fastest way to take up slack.

**Twist.**
- `LineWear += 0.02/s` while Reel is held and `IsDragSlipping` is true.
- It is capped at 0.4. Nothing else wears the line.

**Distance clamp.** `FishDistance` never goes below 80 cm.

### 3.4 Shocks and breaks

**Shock sizes.** Shocks happen at Jerk start (k = 0.4), Dive start (0.25), Jump apex on a tight line (0.35) and Surge start (0.3):

```
hit = k·F·(0.3+0.7S)·(1 − flex·(0.5+0.5·angle))·(slipping ? 0.5 : 0.8)·(1 + 0.6·clamp01((800−LineOut)/600))
```

**Spreading.** Each shock, including the breakaway shock, is applied over 3 ticks at ×1.0, ×0.6 and ×0.3. `PeakT = T + shock`.

**Break checks** run every tick, in the same form as today:
- `PeakT > Line(1−wear)` → LineBroke;
- `PeakT(1+0.6·angle) > RodMax` → RodBroke;
- `LineOut > spool` → SpoolEmpty.

**Weakest link:** `WeakestLinkG = min(Line(1−wear), RodMax/(1+0.6·angle))`, plus a `WeakestIsRod` flag.

### 3.5 Stamina, hook hold, phases, landing

**Stamina.**
- Drain: `dS/dt = −min(1.2, T/F)·(0.5+0.5·min(1,|u|/V))/St`, with `St = 3 + 1.5·stamina + 1.5·kg`.
- Recovery: +0.03/s while `T < 0.25F`, and +0.05/s on slack.

**HookHold (H).** This replaces the slack timer and the 25% jump roll.
- It starts at the strike quality.
- **Slack:** `H −= k·min(1,(slack−30)/60)·dt`. k by style: steady 0.35, jerky 0.5, heavy 0.3, deep 0.4, jumps 0.6.
- **Jerk starting on slack:** −0.25.
- **Jump apex** (the middle tick of a Jump) with slack above 30 cm or `T < 0.05F`: −0.45.
- **Idle:** no Reel or Lift, drag not slipping, for more than 10 s: −0.05/s.
- **Net bump:** −0.1.
- **Recovery:** +0.1/s while the line is tight, up to the starting value.
- H at or below 0 → Escaped.

**Phases and RNG order.** Document this in the class comment and add `ReplayVersion = 2`.

1. **Constructor:** no draws. The fight opens in Rest for a fixed 0.8 s (head-shake amplitude ×2), with NextPhase = Run.
2. **At each phase start:**
   - one draw for `Duration(phase)` (none for Surge);
   - if the phase is Rest, one draw to pick NextPhase; otherwise NextPhase = Rest.
3. **First tick with `phaseLeft` ≤ 0.6:** apply deterministic overrides with no draws:
   - if `S < 0.2`, NextPhase = Rest;
   - if the current phase is Rest, no shore surge has happened yet, `FishDistance < max(450, 2·reach)` and `S ≥ 0.2`, NextPhase = Surge.

   NextPhase is locked after this.
4. **Telegraph:** `TelegraphProgress` runs from 0 to 1 once `phaseLeft` ≤ the lead of NextPhase.
5. **Net bump:** forces a Surge immediately, with no lead.
6. The jump-escape draw is removed.

**Lateral movement.** This is cosmetic and is never read by the physics.
- `side = hash(visualSeed, PhaseIndex)&1 ? +1 : −1`, using an integer hash and no IRandom.
- In Run, Dive and Surge: `LateralCm += side·0.35·|u|·dt`.
- In Rest it decays toward 0 at 10 cm/s.
- `|LateralCm|` is clamped to at most `0.3·FishDistance`.

**Landing.** This replaces the automatic landing at 200 cm.
- **Swing.** `CanSwing` = `W ≤ min(450, 0.25·Line(1−wear))`, `FishDistance ≤ reach+50` and the line is tight.
  - Lift held while `CanSwing` → Landed (`Swung`), but only if S < 0.6.
  - Otherwise H drops by 0.4, the fish moves 50 cm farther out, and there is a 1 s cooldown.
- **Net.** `InNetReach` = `FishDistance ≤ reach+50`.
  - `CanNet` = `InNetReach`, Rest, S ≤ 0.3, angle ≥ 0.4 and the line tight.
  - Net while `CanNet` → Landed.
  - Net otherwise → a bump: immediate Surge, H −0.1, S +0.05, and a 2 s cooldown.

### 3.6 Outputs (read-only)

| Group | Fields |
|---|---|
| Phase | `Phase`, `NextPhase`, `NextPhaseInSeconds`, `TelegraphProgress`, `PhaseIndex`, `Tick` |
| Tension and load | `TensionG`, `PeakTensionG`, `RodLoadG`, `WeakestLinkG`, `WeakestIsRod` |
| Reel and drag | `IsDragSlipping`, `SlipCmPerSec`, `ReelInCmPerSec`, `ReelLoad = 1−c/C0`, `IsCrankingIntoSlip` |
| Positions | `FishSpeedCmPerSec` (+ means toward the angler), `FishDistanceCm`, `LineOutCm`, `LateralCm`, `RodAngle`, `SlackCm` |
| Fish and line | `Stamina`, `HookHold`, `HookHoldStart`, `LineWear` |
| Landing | `CanSwing`, `InNetReach`, `CanNet`, `HeadUp` |
| Events this tick | `[Flags] Events`: HookSet, PhaseStart, TelegraphStart, Breakaway, Shock, JumpApex, HookLoss, SurgeStart, NetBump, Swung, Landed, Broke |

`HeadUp` = `CanNet`, or Rest with angle ≥ 0.6, S < 0.3 and the fish within 8 m.

**FightReport.**
- **Cause:** WornByCranking, DragTooTight, ShockRodLow, RodHighTightDrag, Overload, SpoolEmptyDragLoose, SlackShaking, JumpOnSlack, NetBump, SwingFresh, Idle.
- **Numbers:** PeakG, LimitG, Wear, SecondsCrankedOnSlip, Phase, RodAngle, RestsTotal, RestsLifted.
- **History:** a 200-tick ring buffer of (T, Peak, drag, limit).

**Choosing the cause.**
- For a line break, check in order:
  1. breakaway tick and drag > 0.75·limit → DragTooTight;
  2. wear ≥ 0.15 → WornByCranking;
  3. shock with angle < 0.3 → ShockRodLow;
  4. otherwise → Overload.
- For an escape, use the last thing that reduced H.

### 3.7 Tests (`client/Assets/_Game/Tests/EditMode/FightModelTests.cs`)

**Test tackles** gain C0 / Tr / RodPower: float 130 / 600 / 1500, spinning 150 / 2000 / 3600.

**Bots** (shared helper):
- **HoldReel:** always Reel. Lift when `CanSwing`. Inside net reach, Lift until the angle is at least 0.6, then Net when `CanNet`.
- **StopOnClicks:** Reel unless the drag is slipping.
- **Technique:**
  - on a Run, Dive or Surge telegraph, or while slipping: release Reel;
  - in Rest: Lift while the angle is below 0.9, otherwise Reel;
  - on a Jump telegraph or during a Jump: Reel and Lift;
  - on a Jerk telegraph: Lift;
  - swing or net when allowed.
- **TechniqueLate:** Technique acting on state that is 5 ticks old.
- **LiftReleaseReel:** 1 s Lift, 0.3 s nothing, 3 s Reel, repeated.

**Existing tests to change.**

| Test | Change |
|---|---|
| SmallRoachLandsByJustReeling | Use the HoldReel bot. Must land in under 30 s. |
| HugePike, LockedDragAndLiftingBreaksRod | Same expected outcome. Add a cause assertion (Overload / RodHighTightDrag). |
| CarefulPumpingLandsFourKiloPike | Use Technique with 0.20 line and drag 2400. Seeds 1–5 must land. |
| ReelingIntoSlippingDrag… | HoldReel on a 2 kg bream at drag 1050 → LineBroke with cause WornByCranking and wear > 0.15. Technique → Landed. |
| SlackLineLetsFishEscape | Escaped within 2.5–3.5 s, cause SlackShaking. |
| DragCapsTensionWithoutJerks | Becomes "TensionG ≤ 1.2·drag on every tick, and equals drag while slipping". |
| SameSeedGivesSameFight | Also compare Report and Tick. |
| ZeroDrag, RaisedRod, FishTires | Keep. |

**New tests.**
- Every PhaseStart, except one caused by a net bump, had NextPhase exposed at least lead − dt earlier.
- Seeded random input on roach, perch and bream: on any tick with no buttons held, outside a Jump or its telegraph, `SlackCm` never rises.
- On the exposed static solver, as P rises from 0 to 2·drag, `ReelIn` never increases, and it stays above 0 while the drag does not slip.
- A slack jump costs exactly 0.45 at `JumpApex`.
- Net rule: a ready fish → Landed; a fish that is not ready → Surge and H −0.1.
- A different `visualSeed` gives an identical outcome.
- DoNothing on a roach → Escaped(Idle) within 60 s.

**Balance bands** (`[Category("Balance")]`, 200 seeds each):

| Case | Expected |
|---|---|
| Roach 150 g, float rod, 15 m, HoldReel | 100% landed, median 12–17 s |
| Perch 300 g, 15 m | HoldReel: 100%, 18–26 s. LiftReleaseReel: ≥95% |
| Bream 2 kg, float, drag 1050 | HoldReel: ≤40% landed. StopOnClicks: ≥80%. Technique: ≥95%, 55–100 s |
| Pike 4 kg, 0.20 line, drag 2400, 30 m | HoldReel: ≤10%. StopOnClicks: ≥85%. Technique: ≥95%, 60–120 s |
| Pike 7 kg, 0.20 line | Technique: 20–40%. TechniqueLate: ≤35%. HoldReel: 0% |
| Pike 7 kg, 0.30 line | Technique: ≥90% |
| Pike 12 kg, float rod | 0% landed |

**Tuning knobs, in this order:** the 0.7 body-drag factor, the St coefficients, C0/Tr, PF, then the shock k values.

## 4. World feedback (FishingController, FishView, TackleViews)

**Controller.**
- Interpolate the previous and current snapshots (FishDistance, LineOut, Lateral, RodAngle, PeakT) by accumulator/tick.
- Combine `Events` and add up `SlipCm` across all sub-steps of a frame, for audio and haptics.
- Fish and float position = `dir·FishDistance + perp·LateralCm`.

**Float.**
- It stays on the surface. Remove the 0.25 m sink and the 35° tilt.
- It tilts up to 20° toward the rod, in proportion to T/F.
- It is pulled 0.1 m under during Dive and Jerk, and pops back up in Rest.
- It is never smaller than 10 px on screen.
- **Wake:** `RippleField` spawns every `max(0.1, 0.5−|speed|/250)` s, with radius `0.2+|speed|/150` m, as two rings ±0.3 m behind the float to form a V.
- An extra burst of rings appears when `FishSpeed < −40` (the fish is taking line).

**Line.**
- At least 1.5 px wide on screen. Set the width curve from `2d·tan(fov/2)/Screen.height` at the tip and at the end.
- It brightens with PeakT/Weakest.
- Sag = `clamp01(SlackCm/60)`.

**Rod.**
- Bend = `0.05 + 0.75·sqrt(RodLoad/RodMax)`, smoothed over 0.08 s. The throb comes from the head-shake wave in P.
- Telegraph motions are driven by `TelegraphProgress`:
  - Run, Dive or Surge: two 4° dips;
  - Jerk: one 6° knock at progress 0.6;
  - Jump: the tip rises and the bend drops by 50%.
- Hoop on hookset. Remove the 0.12 load override that applies during the bite.

**Fish.**
- `VisibleDistanceM` becomes 8.
- During a Jump the fish is visible at any range: leap height `0.45+0.3·L` m, drawn at least 40 px long.
- Poses:
  - S < 0.15: on its side;
  - `HeadUp`: nose up 20°, skating;
  - S 0.15–0.45: upright and slow;
  - S > 0.45 and active: thrashing, with ripples every `0.18/(0.4+S)` s;
  - Surge: turns away and bolts, with a 1.5 m ring.
- Splash rings at any range for: Run-start boil, Jump apex, Surge start and Jerk, sized by weight.

**Camera** (driven only by model state, with a setting to turn it off):
- FOV goes from 55° to 38° while the fish is beyond 12 m, and back to 50° at 5 m or closer, damped over 0.8 s.
- Yaw follows `atan(Lat/Dist)`, up to ±8°, damped over 0.6 s.
- At 5 m or closer, the camera pitches 6° down.

## 5. HUD (LakeHud)

**Line counter** above Reel, always shown:
- «14,2 м» = LineOut, with a small «до рыбы 12,0 м» line under it.
- The rate is the change in LineOut, smoothed over 0.3 s:
  - green «↓0,8 м/с» when gaining;
  - amber «↑0,4 м/с» when losing;
  - white «= держишь» when it moves less than 3 cm/s on a tight line.

**Track** at top centre:
- A net icon, then a fish marker placed at FishDistance/start.
- The marker is tinted in three stamina steps: «свежая / устаёт / выдохлась».
- A hook ring appears when H is below its start value or the line is slack. It flashes on `HookLoss`.
- Beginner HUD only: a telegraph word fills above the marker: «уйдёт / рывок / вглубь / свечка / встала / бросок от берега» (Surge takes line away from the angler, so the answer is to release Reel).
- Remove the phase pill from the centre of the screen.

**Tension arc:**
- Needle = PeakT/WeakestLink, critically damped over 0.12 s.
- A drag notch with its value.
- A shock leaves a ghost tick that fades over 0.6 s.
- Zones: green below 55%, amber below 85%, red above.
- The label reads «леска» or «удилище» depending on which is weaker.
- Twist is drawn as a hatched end segment.

**Buttons:**
- **Reel:** the handle glyph spins at `ReelInCmPerSec`.
  - When `ReelLoad > 0.65`, it slows, jitters and shows «= держишь».
  - When the drag slips, the spool ring turns backward in red.
  - When `IsCrankingIntoSlip`, it gets an amber ring and «вхолостую».
- **Lift:** an arc shows `RodAngle`, tinted by rod load. It pulses green in Rest when the line is tight and the angle is below 0.9. It reads «Выудить» when `CanSwing`. Held with the rod at the top (angle ≥ 0.99), away from the net zone and not while the drag slips, its sub-label reads «вверху · мотай»: the model counts that hold as idle.
- **Net:** glows exactly when `CanNet`.

**Coach line.**
- One fixed slot under the track, showing one line at a time. Priority: danger, then mistake, then opportunity.
- Each line retires after 3 correct responses (stored in PlayerPrefs). It returns after a loss with the matching cause. A setting turns the coach off.

| Trigger | Line |
|---|---|
| Cranking into slip for 0.4 s | «Фрикцион отдаёт — не крути» |
| `ReelLoad > 0.65` for 1.5 s | «Катушка не тянет — качай: Подтянуть, потом Подмотка» |
| Lift held without Reel, rod at the top (≥ 0.99), for 1.5 s; not while slipping, before a Run/Dive/Surge or in the net zone | «Удилище вверху — опускай Подмоткой» |
| Rest, no Lift for 1 s | «Рыба встала — подтягивай!» |
| Run, Dive or Surge telegraph while reeling | «Сейчас уйдёт — отпусти подмотку» |
| Jump telegraph | «Свечка! Подматывай» |
| Slack | «Слабина! Подматывай» |
| Load above 85%, weakest is the rod | «Опусти удилище — сломаешь» |
| Load above 85%, weakest is the line | «Опасно! Ослабь фрикцион» |
| `CanSwing` | «Подтянуть — выудить» |
| In net reach but not ready (only when the net button is shown) | «Держи удилище выше, дай устать» |
| `CanNet` (only when the net button is shown; a light fish gets the `CanSwing` line) | «Голова над водой — подсачек!» |

On the first fish only, this rule is shown under the gauge: «Тянет — не крути. Встала — подтягивай.»

**Loss card** (built from the Report):
- the cause, with numbers;
- one «Как надо» tip;
- a 10 s sparkline of tension, drag and limit, drawn with GL lines.

Example: «Обрыв: 1,26 кг при прочности 1,22 — 21 с крутил вхолостую (−32%)».

**Win card:** time, peak load in %, and «подтянул 7 из 9 пауз · 0 с вхолостую».

## 6. Procedural audio and haptics

**Audio.** One FightAudio component. Clips are generated once with `AudioClip.Create` at 22050 Hz mono. Use at most 6 sources, and change only pitch and volume each frame.

| Sound | Recipe | Driven by |
|---|---|---|
| Whir | 0.5 s loop: pulse train at 220 Hz plus 15% noise | Pitch `0.6+0.8·ReelIn/C0` |
| Growl | Noise through a one-pole low-pass at about 150 Hz | Volume `(ReelLoad−0.4)/0.5` |
| Drag click | 6 ms noise burst, 1 ms exponential decay, high-pass at 2 kHz | One click per 1.5 cm of accumulated slip. Above 25 clicks/s, switch to a 40 Hz click loop pitched by `SlipCmPerSec/60` |
| Line sing | Sine 600 Hz plus 0.3 of the second harmonic | Pitch `1+1.3x`, volume `(x−0.6)/0.4`, where x = Peak/Weakest |
| Rod creak | 0.3 s sawtooth at 70 Hz with amplitude jitter | Above 75% rod load, repeating every 0.6 s down to 0.2 s |
| Splash | 0.35 s noise, 80 ms decay, low-pass at 1.5 kHz | Scaled by weight and distance |

One-shots:
- telegraph cues: a filtered-noise swirl before Run, Dive and Surge; an 80 Hz, 60 ms thunk before a Jerk; a rising whoosh before a Jump;
- hookset thunk;
- line snap: a click plus a 2 kHz sine decaying over 150 ms;
- rod crack;
- escape plip: 900 Hz, 40 ms.

**Haptics.** Replace `Handheld.Vibrate` with a `Haptics` class.
- Android API 26+: `VibrationEffect.createOneShot(ms, amp)` through AndroidJavaObject. Older devices: `vibrate(ms)`. Editor and iOS: do nothing.
- Cap all haptics at 15 pulses/s.

| Event | Pulse |
|---|---|
| Drag tick | 8 ms at amplitude 70, one per 10 cm of slip, at most 12/s |
| Telegraph | Two 8 ms pulses 60 ms apart. Jerk: one 15 ms pulse at amplitude 180 |
| Shock | 30 ms, amplitude 100–255 in proportion to Peak/Weakest |
| Danger above 85% | 10 ms ticks, from 3 Hz up to 10 Hz |
| HookLoss | 20 ms every 0.4 s |
| Hookset | 25 ms + 15 ms |
| Landed | 3 × 20 ms |
| Break | 70 ms |

No haptics while the line is tight and gaining.

## 7. Pacing targets

| Fish and tackle | Target |
|---|---|
| Roach 150 g, float, 15 m | 12–17 s, cannot really be lost |
| Perch 300 g | 18–26 s, jerks felt as knocks |
| Bream 2 kg, float | 55–100 s with pumping. HoldReel mostly breaks by twist, with the cause shown |
| Pike 4 kg, spinning, 0.20 line, 30 m | 60–120 s |

**Why a 7 kg pike on 0.20 line is nearly impossible:**
- F is about 9.1 kg. Its runs take line at about 1.3 m/s.
- A fresh jerk with the rod down peaks at about 3.9 kg against a 4.0 kg line.
- Near the shore, the short-line factor of ×1.6 snaps the line even with the rod up, unless the fish is fully tired.
- Any cranking into the slip quickly costs up to 40% of line strength.
- So the angler must raise the rod on every jerk, never crank into slip, keep every jump tight, and tire the fish before 8 m. On 0.30 line the same fish is fair.

## 8. Out of scope for now

- the friend's net and spectator netcode (the model already has the Net input and `NetReach`);
- a two-stage nibble before the take;
- lip tearing;
- swipe-up strike;
- adjusting drag by sliding on the Reel button;
- camera shake;
- stereo pan;
- an iOS haptics plugin;
- moving the HUD to uGUI or UI Toolkit;
- porting the model to the Go/TS server.

The model's arithmetic is kept to `+ − × ÷ floor min max` so it can be ported to the Go/TS server later.