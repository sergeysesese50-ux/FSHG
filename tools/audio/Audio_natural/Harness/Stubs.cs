using Fishing.Core;
using UnityEngine;

namespace Fishing.Unity
{
    // Compile-only stand-in for the project's FishingController (a MonoBehaviour that cannot be compiled outside Unity).
    // Only the members NaturalFeedback reads; never instantiated by the harness.
    public sealed class FishingController : MonoBehaviour
    {
        public LakeState State { get; private set; }
        public FightFrame Frame { get; private set; }
        public FightTackle Tackle { get; private set; }
        public float FloatDistanceM { get; private set; }
        public FishSpecies Species { get; private set; }
        public string Error { get; private set; }
    }
}
