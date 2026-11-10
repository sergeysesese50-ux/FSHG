using System.Collections.Generic;
using UnityEngine;

namespace Fishing.Unity
{
    /// <summary>
    /// Набросок звуковой части FightFeedback для «натуральной» палитры (вибрация остаётся как была —
    /// FightCues.Pulses → Haptics; здесь её нет, чтобы не дублировать). Проверен только компиляцией
    /// против UnityEngine 6000.6 в харнессе; в редакторе не запускался.
    ///
    /// Источники (6):
    /// 1. «Зёрна» — AudioSource без клипа + GrainHost.OnAudioFilterRead → GrainMixer (16 голосов, точность до сэмпла):
    ///    ротор, такт шестерни, натуга, трещотка фрикциона, треск лески, скрип удилища.
    /// 2. Укладка лески — петля 4 с (LayBed), 3. гул лески — петля 4 с (LineHum), 4. фон озера — петля 64 с.
    /// 5. Разовые — PlayOneShot (всплески, телеграфы, подсечка, поимка, обрыв…). 6. Свободен (запас).
    /// Петли: каждый кадр только громкость и высота; при старте — с хешированного места (не с 0), при
    /// отпускании «Подмотки» высота не ныряет. Фильтров-компонентов не нужно: «даль» запечена в клипы.
    /// </summary>
    public sealed class NaturalFeedback : MonoBehaviour
    {
        private const float DefaultMasterVolume = 0.8f;
        private const float MaxFrameSeconds = 0.1f;

        private FishingController _game;
        private readonly NaturalCues _cues = new NaturalCues();
        private NaturalBank _bank;
        private GrainMixer _grains;
        private GrainHost _grainHost;
        private LoopSource _lay, _hum, _ambience;
        private AudioSource _fx;
        private readonly Dictionary<float[], AudioClip> _clips = new Dictionary<float[], AudioClip>();
        private bool _wasFight;

        public float MasterVolume { get; set; } = DefaultMasterVolume;
        public bool Muted { get; set; }

        public void Init(FishingController game)
        {
            if (_game != null)
                return;
            _game = game;
            _bank = new NaturalBank(); // ~0,5–0,9 с на настольном ПК; на телефоне — разово при загрузке озера
            _grains = new GrainMixer(NaturalSynth.SampleRate);

            var host = new GameObject("NaturalAudio");
            host.transform.SetParent(transform, false);
            var grainObject = new GameObject("Grains");
            grainObject.transform.SetParent(host.transform, false);
            var grainSource = Source(grainObject, null, true);
            _grainHost = grainObject.AddComponent<GrainHost>();
            _grainHost.Mixer = _grains;
            grainSource.volume = 1f;
            grainSource.Play();

            _lay = new LoopSource(Source(host, Clip(_bank.LayBed, "Lay"), true), 0x1A70u);
            _hum = new LoopSource(Source(host, Clip(_bank.LineHum, "Hum"), true), 0x4C40u);
            _ambience = new LoopSource(Source(host, Clip(_bank.Ambience, "Lake"), true), 0xA3B0u);
            _fx = Source(host, null, false);
        }

        private void LateUpdate()
        {
            if (_game == null || _game.Error != null)
                return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, MaxFrameSeconds);
            var state = _game.State;
            var frame = _game.Frame;
            bool fight = state == LakeState.Fight;
            if (fight && !_wasFight)
                _cues.RewardTier = RewardTier(_game.Species, frame.WeightG);
            _wasFight = fight;

            var input = fight || frame.EndedThisFrame ? NaturalInput.Of(frame) : default;
            float c0 = _game.Tackle != null ? (float)_game.Tackle.ReelFightSpeedCmPerSec : 110f;
            _cues.Update(state, input, c0, _game.FloatDistanceM, AudioSettings.dspTime, dt);

            float master = Muted ? 0f : MasterVolume;
            _grainHost.Master = master;
            _fx.volume = master;
            for (int i = 0; i < _cues.Grains.Count; i++)
            {
                var g = _cues.Grains[i];
                _grains.Schedule(_bank.Grain(g.Kind, g.Variant), g.Time, g.Gain, g.Pitch);
            }
            for (int i = 0; i < _cues.Sounds.Count; i++)
            {
                var s = _cues.Sounds[i];
                var samples = _bank.Sound(s);
                if (!_clips.TryGetValue(samples, out var clip))
                    _clips[samples] = clip = Clip(samples, s.Sound.ToString());
                _fx.PlayOneShot(clip, s.Volume);
            }
            if (_cues.CutLoops)
            {
                _lay.Cut();
                _hum.Cut();
                _grains.Clear();
            }
            float release = _cues.LoopReleaseSeconds;
            _lay.Drive(_cues.LayVolume, _cues.LayPitch, release, dt, master);
            _hum.Drive(_cues.HumVolume, _cues.HumPitch, release, dt, master);
            _ambience.Drive(_cues.AmbienceVolume, 1f, release, dt, master);
        }

        /// <summary>1 — мельче типичной, 2 — от типичной, 3 — трофей (и рекорд игрока, если его знает вызывающий).</summary>
        private static int RewardTier(Fishing.Core.FishSpecies species, int weightG)
        {
            if (species == null || species.weightG == null)
                return weightG >= 1500 ? 3 : weightG >= 400 ? 2 : 1;
            return weightG >= species.weightG.trophy ? 3 : weightG >= species.weightG.typical ? 2 : 1;
        }

        private void OnDestroy()
        {
            foreach (var clip in _clips.Values)
                if (clip != null)
                    Destroy(clip);
            _lay?.Destroy();
            _hum?.Destroy();
            _ambience?.Destroy();
        }

        private static AudioClip Clip(float[] samples, string name)
        {
            var clip = AudioClip.Create("Natural" + name, samples.Length, 1, NaturalSynth.SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private static AudioSource Source(GameObject host, AudioClip clip, bool loop)
        {
            var source = host.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            source.clip = clip;
            source.spatialBlend = 0f;
            source.dopplerLevel = 0f;
            source.volume = 0f;
            source.pitch = 1f;
            return source;
        }

        /// <summary>Звуковой поток: подмешивает зёрна в буфер своего AudioSource (без клипа — буфер изначально пуст).</summary>
        private sealed class GrainHost : MonoBehaviour
        {
            public GrainMixer Mixer;
            public volatile float Master;
            private int _rate;

            private void Awake() => _rate = AudioSettings.outputSampleRate;

            private void OnAudioFilterRead(float[] data, int channels)
            {
                Mixer?.Render(data, channels, _rate, AudioSettings.dspTime, Master);
            }
        }

        /// <summary>Петля: громкость и высота подтягиваются к целям; старт с хешированного места, без «ныряния» высоты.</summary>
        private sealed class LoopSource
        {
            private const float AttackSeconds = 0.04f;
            private const float PitchSeconds = 0.08f;
            private const float Silence = 0.002f;
            private readonly AudioSource _source;
            private uint _seed;
            private float _volume;
            private float _pitch = 1f;

            public LoopSource(AudioSource source, uint seed)
            {
                _source = source;
                _seed = seed;
            }

            public void Drive(float target, float pitch, float release, float dt, float master)
            {
                if (target <= 0f && _volume < Silence)
                {
                    if (_source.isPlaying)
                        Cut();
                    return;
                }
                if (!_source.isPlaying)
                {
                    _seed = NaturalSynth.Hash(_seed + 0x9E37u);
                    _source.timeSamples = (int)(_seed % (uint)_source.clip.samples);
                    _pitch = pitch;
                    _source.pitch = pitch;
                    _source.volume = 0f;
                    _source.Play();
                }
                _volume += (target - _volume) * (1f - Mathf.Exp(-dt / (target > _volume ? AttackSeconds : Mathf.Max(release, 1e-4f))));
                _pitch += (pitch - _pitch) * (1f - Mathf.Exp(-dt / PitchSeconds));
                _source.volume = _volume * master;
                _source.pitch = _pitch;
            }

            public void Cut()
            {
                _volume = 0f;
                _source.volume = 0f;
                _source.Stop();
            }

            public void Destroy()
            {
                if (_source != null && _source.clip != null)
                    Object.Destroy(_source.clip);
            }
        }
    }
}
