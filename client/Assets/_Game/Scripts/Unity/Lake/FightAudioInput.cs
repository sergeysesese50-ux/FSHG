using Fishing.Core.Audio;

namespace Fishing.Unity
{
    /// <summary>
    /// FightFrame → FightSoundInput: какие поля снимка вываживания слышит звук (FightAudioDirector). Отдельным файлом без
    /// движковых вызовов, чтобы харнесс tools/audio/AudioIngame гонял ровно то же преобразование, что игра.
    /// </summary>
    internal static class FightAudioInput
    {
        public static FightSoundInput Of(FightFrame f) => new FightSoundInput
        {
            Ticks = f.TicksThisFrame,
            ReelInCmPerSec = f.ReelInCmPerSec,
            ReelLoad = f.ReelLoad,
            IsDragSlipping = f.IsDragSlipping,
            IsCrankingIntoSlip = f.IsCrankingIntoSlip,
            SlipCmPerSec = f.SlipCmPerSec,
            LoadShare = f.LoadShare,
            RodLoadShare = f.RodLoadShare,
            SlackCm = f.SlackCm,
            FishDistanceCm = f.FishDistanceCm,
            Phase = f.Phase,
            NextPhase = f.NextPhase,
            Events = f.Events,
            Outcome = f.Outcome,
            EndedThisFrame = f.EndedThisFrame,
            WeightG = f.WeightG,
            Tick = f.Tick,
            PeakShare = f.WeakestLinkG > 0f ? f.PeakTensionMaxThisFrameG / f.WeakestLinkG : 0f,
        };
    }
}
