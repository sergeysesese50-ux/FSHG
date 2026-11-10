namespace Fishing.Unity
{
    // In the project LakeState lives in FishingController.cs (a MonoBehaviour) — copied here for the harness.
    public enum LakeState { Idle, Casting, Waiting, Bite, Fight, Caught, Lost }

    /// <summary>FightFrame → CozyFrame (the only engine-touching bit: FightFrame carries Vector3 fields).</summary>
    internal static class CozyFrameAdapter
    {
        public static CozyFrame Of(FightFrame f) => new CozyFrame
        {
            ReelInCmPerSec = f.ReelInCmPerSec,
            ReelLoad = f.ReelLoad,
            IsDragSlipping = f.IsDragSlipping,
            IsCrankingIntoSlip = f.IsCrankingIntoSlip,
            SlipCmPerSec = f.SlipCmPerSec,
            LoadShare = f.LoadShare,
            RodLoadShare = f.RodLoadShare,
            SlackCm = f.SlackCm,
            Phase = f.Phase,
            NextPhase = f.NextPhase,
            Events = f.Events,
            EndedThisFrame = f.EndedThisFrame,
            Outcome = f.Outcome,
            WeightG = f.WeightG,
            FishDistanceCm = f.FishDistanceCm,
            Tick = f.Tick,
            Alpha = f.Alpha,
        };
    }
}
