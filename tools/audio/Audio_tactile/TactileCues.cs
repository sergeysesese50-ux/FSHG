using System;
using System.Collections.Generic;
using Fishing.Core;

namespace Fishing.Unity
{
    /// <summary>One-shot clips of the tactile palette (22050 Hz).</summary>
    internal enum TactileSound
    {
        CastWhoosh, FloatPlop, BiteBob, Hookset,
        TeleRun, TeleDive, TeleSurge, TeleJerk, TeleJump,
        SplashSmall, SplashBig, SplashJump, LineThrum, FishFlop,
        ChimeSmall, Chime, ChimeBig, LineSnap, RodCrack, Escape,
    }

    /// <summary>A one-shot of this frame. Water = play on the water source (distance low-pass LowPassHz).</summary>
    internal struct TactileCue
    {
        public TactileSound Sound;
        public float Volume;
        public bool Water;
        public float LowPassHz;
    }

    /// <summary>Plain inputs of the continuous reel layer (read from FightFrame; previews feed them directly).</summary>
    internal struct ReelInputs
    {
        public bool Fight;
        public float C0;               // tackle.ReelFightSpeedCmPerSec
        public float ReelInCmPerSec;
        public float ReelLoad;
        public bool IsCrankingIntoSlip;
        public bool IsDragSlipping;
        public float SlipCmPerSec;
        public float LoadShare;        // PeakTension / WeakestLink
        public float SlackCm;
        public float RodLoadShare;
    }

    /// <summary>
    /// «Тактильная» палитра: что звучит когда (drop-in кандидат на место FightCues, звук). Только FightFrame/State —
    /// зритель-друг слышит то же. Сам ничего не играет: цели для зерен (TactileGrains, аудиопоток), петель
    /// (шпуля, леска, озеро) и разовые звуки кадра. Вибрация не трогается (остаётся FightCues/HapticSchedule).
    ///
    /// | Слой                | Чем управляется                                                              |
    /// | щелчки ручки        | rate = 12/с × (1 − ReelLoad), пока крутят; тяжесть = ReelLoad (высота −10%, громче) |
    /// | шорох ротора        | тот же ход × 5,2/8 (проход ротора), громкость ∝ LoadShare 0,08→0,43 на натянутой — «леска ложится под нагрузкой» |
    /// | натуга механизма    | ReelLoad 0,45→1: 2→14 событий/с, неровно; при пробуксовке ×0,35               |
    /// | тики фрикциона      | rate = SlipCmPerSec / 2 см, пока IsDragSlipping; Breakaway — сразу акцент     |
    /// | шорох шпули (петля) | громкость ∝ max(ручка, slip/60)^0,8; высота 0,88 + 0,24·скорость; при отпускании высота держится |
    /// | гул лески (петля)   | только на натянутой: громкость smoothstep(LoadShare 0,72→0,97), высота 1 → 1,36 |
    /// | скрип удилища       | RodLoadShare 0,78→1: 0,9→2,6 скрипа/с, громче и «твёрже»                       |
    /// | озеро (петля 64 с)  | 0,2 вне боя, 0,08 в бою (плавно 1,5 с) — в бою ≥ 14 дБ под катушкой            |
    /// </summary>
    internal sealed class TactileCues
    {
        // ---- Levels (before master 0.8) ----
        public const float DetentsPerSecAtC0 = 12f;   // 8 detents per handle turn × 1.5 turns/s at C0
        public const float DragCmPerTick = 2f;
        private const float DetentLevel = 0.42f;
        private const float RotorLevel = 0.20f;
        private const float DragLevel = 0.55f;
        private const float StrainLevel = 0.16f;
        private const float CreakQuiet = 0.18f, CreakLoud = 0.40f;
        private const float PurrLevel = 0.08f;
        private const float HumLevel = 0.14f;
        private const float AmbIdle = 0.2f, AmbFight = 0.08f;
        private const float TightSlackCm = 30f;

        private const float CastLevel = 0.45f, PlopLevel = 0.5f, BiteLevel = 0.5f, HooksetLevel = 0.6f;
        private const float TeleRunLevel = 0.5f, TeleDiveLevel = 0.5f, TeleSurgeLevel = 0.55f, TeleJumpLevel = 0.5f, TeleJerkLevel = 0.55f;
        private const float RunBoil = 0.5f, JerkSplash = 0.35f, DiveSplash = 0.35f, ApexSplash = 0.8f, ReentrySplash = 0.85f, SurgeSplash = 0.75f;
        private const float ThrumMin = 0.12f, ThrumMax = 0.35f;
        private const float FlopLevel = 0.4f, NetSplash = 0.4f, ChimeLevel = 0.42f;
        private const float SnapLevel = 0.8f, SpoolSnapLevel = 0.55f, CrackLevel = 0.85f, EscapeLevel = 0.55f;
        private const int HeavySplashG = 1200;

        private readonly List<TactileCue> _sounds = new List<TactileCue>(8);
        private LakeState _state;
        private FishPhase _lastPhase;
        private bool _swung;

        public IReadOnlyList<TactileCue> Sounds => _sounds;
        /// <summary>Breakaway this frame: TactileGrains.Trigger(GrainKind.DragAccent).</summary>
        public bool DragAccent { get; private set; }

        // Grain targets.
        public float DetentRate, DetentHeavy, DetentGain, RotorGain, DragRate, DragGain, StrainRate, StrainGain, CreakRate, CreakGain, CreakHard;
        // Loops.
        public float PurrVolume, PurrPitch = 1f, HumVolume, HumPitch = 1f, AmbienceVolume;
        public float LoopReleaseSeconds = 0.15f;
        public bool CutLoops { get; private set; }

        public void Update(LakeState state, FightFrame frame, FightTackle tackle, float floatDistanceM, float dt)
        {
            _sounds.Clear();
            CutLoops = false;
            DragAccent = false;
            if (state != _state)
            {
                var from = _state;
                _state = state;
                StateChanged(from, state, frame, floatDistanceM);
            }

            bool fight = state == LakeState.Fight;
            if (fight || frame.EndedThisFrame)
                ReadFrame(frame);

            var x = new ReelInputs
            {
                Fight = fight,
                C0 = tackle != null && tackle.ReelFightSpeedCmPerSec > 0 ? (float)tackle.ReelFightSpeedCmPerSec : 100f,
                ReelInCmPerSec = frame.ReelInCmPerSec,
                ReelLoad = frame.ReelLoad,
                IsCrankingIntoSlip = frame.IsCrankingIntoSlip,
                IsDragSlipping = frame.IsDragSlipping,
                SlipCmPerSec = frame.SlipCmPerSec,
                LoadShare = frame.LoadShare,
                SlackCm = frame.SlackCm,
                RodLoadShare = frame.RodLoadShare,
            };
            Map(x);
            AmbienceVolume = fight ? AmbFight : AmbIdle;
            LoopReleaseSeconds = fight ? 0.15f : 0.4f;
        }

        /// <summary>Continuous layers from plain inputs (also used by the previews).</summary>
        public void Map(in ReelInputs x)
        {
            if (!x.Fight)
            {
                DetentRate = DragRate = StrainRate = CreakRate = RotorGain = 0f;
                PurrVolume = HumVolume = 0f;
                return;
            }
            bool cranking = x.ReelInCmPerSec > 0.5f || x.IsCrankingIntoSlip || x.ReelLoad > 0.001f;
            float load = Clamp01(x.ReelLoad);
            float handle = cranking ? 1f - load : 0f;               // handle (motor) speed / C0
            DetentRate = cranking ? DetentsPerSecAtC0 * handle : 0f;
            DetentHeavy = cranking ? load : 0f;
            DetentGain = DetentLevel * (0.8f + 0.2f * handle) * (1f + 0.35f * load);

            bool tightLine = x.SlackCm <= TightSlackCm;
            RotorGain = cranking && tightLine ? RotorLevel * Clamp01((x.LoadShare - 0.08f) / 0.35f) * (0.6f + 0.4f * handle) : 0f;

            float slip = x.IsDragSlipping ? Math.Max(0f, x.SlipCmPerSec) : 0f;
            DragRate = slip / DragCmPerTick;
            DragGain = DragLevel;

            float u = Clamp01((load - 0.45f) / 0.55f);
            StrainRate = cranking && u > 0f ? 2f + 12f * (float)Math.Pow(u, 1.2) : 0f;
            StrainGain = StrainLevel * (0.4f + 0.6f * u) * (x.IsDragSlipping ? 0.35f : 1f);

            float k = Clamp01((x.RodLoadShare - 0.78f) / 0.22f);
            CreakRate = x.RodLoadShare > 0.78f ? 0.9f + 1.7f * k : 0f;
            CreakGain = CreakQuiet + (CreakLoud - CreakQuiet) * k;
            CreakHard = k;

            float spool = Math.Max(handle, slip / 60f);
            PurrVolume = spool > 0.01f ? PurrLevel * (float)Math.Pow(Math.Min(1f, spool), 0.8) : 0f;
            if (spool > 0.01f) PurrPitch = 0.88f + 0.24f * Math.Min(1.2f, spool); // released: keep the pitch, only fade

            bool tight = x.SlackCm <= TightSlackCm;
            float h = Clamp01((x.LoadShare - 0.72f) / 0.25f);
            HumVolume = tight ? HumLevel * h * h * (3f - 2f * h) : 0f;
            if (HumVolume > 0f) HumPitch = 1f + 0.3f * Math.Max(0f, Math.Min(1.2f, (x.LoadShare - 0.72f) / 0.28f));
        }

        private void StateChanged(LakeState from, LakeState to, FightFrame frame, float floatDistanceM)
        {
            switch (to)
            {
                case LakeState.Casting:
                    Play(TactileSound.CastWhoosh, CastLevel);
                    break;
                case LakeState.Waiting when from == LakeState.Casting:
                    Water(TactileSound.FloatPlop, PlopLevel * DistanceFactor(floatDistanceM), floatDistanceM);
                    break;
                case LakeState.Bite:
                    Water(TactileSound.BiteBob, BiteLevel * DistanceFactor(floatDistanceM), floatDistanceM);
                    break;
                case LakeState.Fight:
                    _lastPhase = frame.Phase;
                    _swung = false;
                    Play(TactileSound.Hookset, HooksetLevel);
                    break;
            }
        }

        private void ReadFrame(FightFrame f)
        {
            var e = f.Events;
            if (Has(e, FightEvents.TelegraphStart) && !f.EndedThisFrame)
                switch (f.NextPhase)
                {
                    case FishPhase.Run: Play(TactileSound.TeleRun, TeleRunLevel); break;
                    case FishPhase.Dive: Play(TactileSound.TeleDive, TeleDiveLevel); break;
                    case FishPhase.Surge: Play(TactileSound.TeleSurge, TeleSurgeLevel); break;
                    case FishPhase.Jump: Play(TactileSound.TeleJump, TeleJumpLevel); break;
                    case FishPhase.Jerk: Play(TactileSound.TeleJerk, TeleJerkLevel); break;
                }
            if (Has(e, FightEvents.PhaseStart))
            {
                if (_lastPhase == FishPhase.Jump && f.Phase != FishPhase.Jump)
                    Splash(f, ReentrySplash, TactileSound.SplashJump);
                switch (f.Phase)
                {
                    case FishPhase.Run: Splash(f, RunBoil); break;
                    case FishPhase.Jerk: Splash(f, JerkSplash); break;
                    case FishPhase.Dive: Splash(f, DiveSplash); break;
                }
            }
            if (Has(e, FightEvents.JumpApex)) Splash(f, ApexSplash);
            if (Has(e, FightEvents.SurgeStart)) Splash(f, SurgeSplash);
            if (Has(e, FightEvents.NetBump)) Splash(f, 0.3f);
            if (Has(e, FightEvents.Breakaway)) DragAccent = true;
            if (Has(e, FightEvents.Swung)) _swung = true;
            if (Has(e, FightEvents.Shock))
            {
                float share = f.WeakestLinkG > 0f ? f.PeakTensionMaxThisFrameG / f.WeakestLinkG : 0f;
                if (share > 0.3f)
                    Play(TactileSound.LineThrum, ThrumMin + (ThrumMax - ThrumMin) * Clamp01((share - 0.3f) / 0.7f));
            }
            if (f.EndedThisFrame) Ended(f);
            _lastPhase = f.Phase;
        }

        private void Ended(FightFrame f)
        {
            switch (f.Outcome)
            {
                case FightOutcome.Landed:
                    if (_swung) Play(TactileSound.FishFlop, FlopLevel);
                    else Splash(f, NetSplash);
                    var tier = f.WeightG < 250 ? TactileSound.ChimeSmall : f.WeightG < 1000 ? TactileSound.Chime : TactileSound.ChimeBig;
                    Play(tier, ChimeLevel);
                    break;
                case FightOutcome.LineBroke: CutLoops = true; Play(TactileSound.LineSnap, SnapLevel); break;
                case FightOutcome.SpoolEmpty: CutLoops = true; Play(TactileSound.LineSnap, SpoolSnapLevel); break;
                case FightOutcome.RodBroke: CutLoops = true; Play(TactileSound.RodCrack, CrackLevel); break;
                case FightOutcome.Escaped: Play(TactileSound.Escape, EscapeLevel); break;
            }
        }

        private void Splash(FightFrame f, float strength, TactileSound? force = null)
        {
            float kg = f.WeightG / 1000f;
            float weight = Math.Max(0.3f, Math.Min(1f, 0.3f + 0.7f * (float)Math.Sqrt(kg / 3f)));
            float m = f.FishDistanceCm / 100f;
            var sound = force ?? (f.WeightG >= HeavySplashG ? TactileSound.SplashBig : TactileSound.SplashSmall);
            Water(sound, strength * weight * DistanceFactor(m), m);
        }

        /// <summary>Quieter with distance: full to 3 m, half at 15 m, a third at 27 m.</summary>
        public static float DistanceFactor(float meters) => 1f / (1f + Math.Max(0f, meters - 3f) / 12f);

        /// <summary>Water-source low-pass (AudioLowPassFilter.cutoffFrequency): 6 kHz near → 1.8 kHz at 28 m+.</summary>
        public static float WaterCutoff(float meters) => 6000f - 4200f * Clamp01((meters - 3f) / 25f);

        private void Play(TactileSound s, float v)
        {
            if (v > 0f) _sounds.Add(new TactileCue { Sound = s, Volume = Math.Min(v, 1f) });
        }

        private void Water(TactileSound s, float v, float meters)
        {
            if (v > 0f) _sounds.Add(new TactileCue { Sound = s, Volume = Math.Min(v, 1f), Water = true, LowPassHz = WaterCutoff(meters) });
        }

        private static bool Has(FightEvents e, FightEvents f) => (e & f) != 0;
        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
