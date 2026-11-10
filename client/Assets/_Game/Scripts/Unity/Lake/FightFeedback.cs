using System.Threading.Tasks;
using Fishing.Core;
using Fishing.Core.Audio;
using UnityEngine;

namespace Fishing.Unity
{
    /// <summary>
    /// Звук и вибрация рыбалки (docs/specs/fight-v2.md, §6). Звук — палитра «Натуральная» (Fishing.Core/Audio):
    /// что и когда звучит, решает FightAudioDirector, сводит FightAudioMixer; вибрацию по-прежнему решает FightCues.
    /// Здесь — движковая часть.
    ///
    /// Потоки:
    /// - фоновый: банк клипов FightSoundBank строится один раз при Init (Task.Run; на ПК ~0,5 с, на телефоне — секунды),
    ///   пока он не готов, озеро молчит;
    /// - главный (LateUpdate, после FishingController.Update): директор читает State, Frame, Tackle, LakeClock.Night,
    ///   общую громкость и публикует снимок и события в микшер (тройной буфер и очередь без блокировок);
    /// - звуковой: единственный AudioSource (тихая петля, приоритет 0) с FightAudioOutput — OnAudioFilterRead отдаёт
    ///   буфер микшеру. Других источников звука нет: зёрна, петли, разовые звуки и фон — внутри микшера, общий лимитер.
    ///
    /// Общая громкость MasterVolume, выключение звука — Muted (переключает LakeHud: клавиша M и «Звук: вкл/выкл»);
    /// оба запоминаются в PlayerPrefs и попадают в микшер со следующим кадром. Вибрация — отдельно, HapticsEnabled.
    /// Частота вывода — AudioSettings.outputSampleRate (перечитывается при смене устройства вывода).
    /// Если в сцене нет AudioListener, добавляет его на главную камеру.
    /// </summary>
    public sealed class FightFeedback : MonoBehaviour
    {
        private const string MutedPrefsKey = "fishing.audio.muted";
        private const string VolumePrefsKey = "fishing.audio.volume";
        private const float DefaultMasterVolume = FightAudioMixer.DefaultMaster;
        private const float MaxFrameSeconds = 0.1f;
        private const float DefaultC0 = 110f;

        private FishingController _game;
        private LakeClock _clock;
        private readonly FightCues _cues = new FightCues();
        private readonly FightAudioDirector _director = new FightAudioDirector();
        private Haptics.Device _vibrator;

        private Task<FightSoundBank> _bankTask;
        private FightAudioMixer _mixer;
        private FightAudioOutput _output;
        private AudioSource _source;
        private AudioClip _silence;
        private int _outputRate;
        private bool _wasFight;

        private bool _muted;
        private float _masterVolume = DefaultMasterVolume;

        // Фон турбазы: отдельная петля (CampSynth) на своём источнике; фон озера на это время гаснет.
        private const float CampLevel = 0.9f;
        private const float CampFadeSeconds = 0.8f;
        private Task<float[]> _campTask;
        private AudioSource _campSource;
        private AudioClip _campClip;
        private bool _camp;
        private float _campMix;

        // Звуки интерфейса: «в садок», «бульк» и монеты при продаже (UiSynth) на своём источнике.
        private const float UiLevel = 0.9f;
        private const int UiKeep = 0, UiRelease = 1, UiCoin = 2, UiCoins = 3;
        private AudioSource _uiSource;
        private AudioClip _keepClip;
        private AudioClip _releaseClip;
        private AudioClip _coinClip;
        private AudioClip _coinsClip;

        /// <summary>Рыба положена в садок: мягкий шлепок и два тихих «динь».</summary>
        public void PlayKeep() => PlayUi(ref _keepClip, UiKeep);

        /// <summary>Рыба отпущена: «бульк».</summary>
        public void PlayRelease() => PlayUi(ref _releaseClip, UiRelease);

        /// <summary>Рыба продана: звон монеты; если продано несколько рыб — монеты сыплются одна за другой.</summary>
        public void PlaySell(int fishCount)
        {
            if (fishCount <= 0)
                return;
            if (fishCount == 1)
                PlayUi(ref _coinClip, UiCoin);
            else
                PlayUi(ref _coinsClip, UiCoins);
        }

        private static float[] UiSamples(int kind)
        {
            switch (kind)
            {
                case UiKeep: return UiSynth.Keep();
                case UiRelease: return UiSynth.Release();
                case UiCoin: return UiSynth.Coin();
                default: return UiSynth.Coin(4);
            }
        }

        private void PlayUi(ref AudioClip clip, int kind)
        {
            if (_muted || _masterVolume <= 0f)
                return;
            if (_uiSource == null)
            {
                _uiSource = gameObject.AddComponent<AudioSource>();
                _uiSource.playOnAwake = false;
                _uiSource.spatialBlend = 0f;
            }

            if (clip == null)
            {
                var samples = UiSamples(kind);
                clip = AudioClip.Create("Ui" + kind, samples.Length, 1, UiSynth.SampleRate, false);
                clip.SetData(samples, 0);
            }

            _uiSource.PlayOneShot(clip, _masterVolume * UiLevel);
        }

        // Щелчок шага настройки: свой источник (у него меняется высота) и лёгкий толчок вибрации.
        private const float TickLevel = 0.5f;
        private const float TickMinInterval = 0.045f;
        private AudioSource _tickSource;
        private AudioClip _tickClip;
        private float _lastTick = -1f;

        /// <summary>
        /// Шаг настройки (спуск поплавка сдвинулся на деление): тихий щелчок и короткий толчок вибрации.
        /// share 0…1 — положение на шкале: чем глубже, тем ниже щелчок. Не чаще ~20 раз в секунду.
        /// </summary>
        public void PlayTick(float share)
        {
            float now = Time.unscaledTime;
            if (now - _lastTick < TickMinInterval)
                return;
            _lastTick = now;
            if (HapticsEnabled)
                _vibrator?.Pulse(8, 70);
            if (_muted || _masterVolume <= 0f)
                return;
            if (_tickSource == null)
            {
                _tickSource = gameObject.AddComponent<AudioSource>();
                _tickSource.playOnAwake = false;
                _tickSource.spatialBlend = 0f;
                var samples = UiSynth.Tick();
                _tickClip = AudioClip.Create("UiTick", samples.Length, 1, UiSynth.SampleRate, false);
                _tickClip.SetData(samples, 0);
            }

            _tickSource.pitch = Mathf.Lerp(1.18f, 0.84f, Mathf.Clamp01(share));
            _tickSource.PlayOneShot(_tickClip, _masterVolume * TickLevel);
        }

        /// <summary>Игрок на турбазе: вместо фона озера звучит фон турбазы (плавный переход). Сообщает интерфейс каждый кадр.</summary>
        public void SetCamp(bool camp) => _camp = camp;

        /// <summary>Общая громкость 0…1 (запоминается).</summary>
        public float MasterVolume
        {
            get => _masterVolume;
            set
            {
                _masterVolume = ValidVolume(value);
                PlayerPrefs.SetFloat(VolumePrefsKey, _masterVolume);
            }
        }

        /// <summary>Звук выключен (запоминается). Вибрацию не трогает.</summary>
        public bool Muted
        {
            get => _muted;
            set
            {
                _muted = value;
                PlayerPrefs.SetInt(MutedPrefsKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        /// <summary>Вибрация включена.</summary>
        public bool HapticsEnabled { get; set; } = true;

        /// <summary>Вызывает TestLake сразу после FishingController.Init (до LakeHud.Init — HUD получает ссылку).</summary>
        public void Init(FishingController game, LakeClock clock)
        {
            if (_game != null)
                return;
            _game = game;
            _clock = clock;
            _muted = PlayerPrefs.GetInt(MutedPrefsKey, 0) != 0;
            _masterVolume = ValidVolume(PlayerPrefs.GetFloat(VolumePrefsKey, DefaultMasterVolume));

            EnsureListener();
            _outputRate = AudioSettings.outputSampleRate;
            AudioSettings.OnAudioConfigurationChanged += OnAudioConfigurationChanged;
            CreateOutput();
            _bankTask = Task.Run(() => new FightSoundBank());
            _campTask = Task.Run(() => CampSynth.CampBed());
            _vibrator = new Haptics.Device();
        }

        public void ToggleMute() => Muted = !Muted;

        /// <summary>Громкость 0…1; NaN и бесконечность (испорченные PlayerPrefs) — громкость по умолчанию.</summary>
        private static float ValidVolume(float value) => float.IsNaN(value) || float.IsInfinity(value) ? DefaultMasterVolume : Mathf.Clamp01(value);

        private void LateUpdate()
        {
            if (_game == null || _game.Error != null)
                return;

            float dt = Mathf.Min(Time.unscaledDeltaTime, MaxFrameSeconds);
            float now = Time.unscaledTime;
            AttachWhenReady();
            DriveCamp(dt);
            DriveSound();

            _cues.Update(_game.State, _game.Frame, now, dt);
            if (_cues.Pulses.TryTake(now, out int ms, out int amplitude) && HapticsEnabled)
                _vibrator.Pulse(ms, amplitude);
        }

        /// <summary>Переход между фоном озера и фоном турбазы: один гаснет, другой нарастает.</summary>
        private void DriveCamp(float dt)
        {
            _campMix = Mathf.MoveTowards(_campMix, _camp ? 1f : 0f, dt / CampFadeSeconds);
            _director.AmbienceScale = 1f - _campMix;

            if (_campSource == null && _campTask != null && _campTask.IsCompleted)
            {
                if (_campTask.IsFaulted || _campTask.IsCanceled)
                {
                    Debug.LogError("Звук турбазы: не удалось построить фон — " + _campTask.Exception?.GetBaseException().Message);
                    _campTask = null;
                    return;
                }

                var samples = _campTask.Result;
                _campTask = null;
                _campClip = AudioClip.Create("CampBed", samples.Length, 1, CampSynth.SampleRate, false);
                _campClip.SetData(samples, 0);
                _campSource = gameObject.AddComponent<AudioSource>();
                _campSource.clip = _campClip;
                _campSource.loop = true;
                _campSource.playOnAwake = false;
                _campSource.spatialBlend = 0f;
                _campSource.volume = 0f;
            }

            if (_campSource == null)
                return;
            float volume = (_muted ? 0f : _masterVolume) * CampLevel * _campMix;
            _campSource.volume = volume;
            if (_campMix > 0f && !_campSource.isPlaying)
                _campSource.Play();
            else if (_campMix <= 0f && _campSource.isPlaying)
                _campSource.Stop();
        }

        private void DriveSound()
        {
            var state = _game.State;
            var frame = _game.Frame;
            bool fight = state == LakeState.Fight;
            if (fight && !_wasFight)
                _director.RewardTier = RewardTier(_game.Species, _game.Trophy, frame.WeightG);
            _wasFight = fight;

            var input = fight || frame.EndedThisFrame ? FightAudioInput.Of(frame) : default;
            float c0 = _game.Tackle != null && _game.Tackle.ReelFightSpeedCmPerSec > 0 ? (float)_game.Tackle.ReelFightSpeedCmPerSec : DefaultC0;
            float night = _clock != null ? _clock.Night : 0f;
            _director.Wind = _clock != null ? Mathf.Clamp01(_clock.WindSpeed / 8f) : 0.25f;
            float master = _muted ? 0f : _masterVolume;
            _director.Update(SoundState(state), input, c0, _game.FloatDistanceM, night, master, _outputRate);

            if (_mixer == null)
                return;
            _mixer.Publish(_director.Params);
            for (int i = 0; i < _director.EventCount; i++)
                _mixer.Post(_director.Event(i));
        }

        /// <summary>Банк готов — микшер и подключение к выходу (один раз). Снимок публикуется до подключения.</summary>
        private void AttachWhenReady()
        {
            if (_mixer != null || _bankTask == null || !_bankTask.IsCompleted)
                return;
            if (_bankTask.IsFaulted || _bankTask.IsCanceled)
            {
                Debug.LogError("Звук озера: не удалось построить банк клипов — " + _bankTask.Exception?.GetBaseException().Message);
                _bankTask = null;
                return;
            }
            _mixer = new FightAudioMixer(_bankTask.Result);
            _mixer.Publish(_director.Params);
            _output.Attach(_mixer);
        }

        private void OnEnable()
        {
            if (_source != null)
                _source.UnPause();
        }

        private void OnDisable()
        {
            if (_source != null)
                _source.Pause();
        }

        private void OnDestroy()
        {
            AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigurationChanged;
            if (_output != null)
                _output.Attach(null);
            if (_silence != null)
                Destroy(_silence);
            _vibrator?.Dispose();
            _vibrator = null;
        }

        /// <summary>Сменилось устройство вывода (наушники, Bluetooth): новая частота, источник — снова играть.</summary>
        private void OnAudioConfigurationChanged(bool deviceWasChanged)
        {
            _outputRate = AudioSettings.outputSampleRate;
            if (_source != null && isActiveAndEnabled && !_source.isPlaying)
                _source.Play();
        }

        // ---------- Создание ----------

        /// <summary>Слушатель на камере, если в сцене его ещё нет (второй дал бы предупреждение Unity).</summary>
        private void EnsureListener()
        {
            if (FindAnyObjectByType<AudioListener>() != null)
                return;
            var camera = Camera.main;
            (camera != null ? camera.gameObject : gameObject).AddComponent<AudioListener>();
        }

        /// <summary>Источник с тихой петлёй (держит фильтр живым на всех платформах) и FightAudioOutput после него.</summary>
        private void CreateOutput()
        {
            var host = new GameObject("LakeAudio");
            host.transform.SetParent(transform, false);
            _source = host.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.loop = true;
            _source.spatialBlend = 0f;
            _source.dopplerLevel = 0f;
            _source.priority = 0;
            _source.volume = 1f;
            _source.pitch = 1f;
            _source.bypassReverbZones = true;
            const int silenceSamples = 1024;
            _silence = AudioClip.Create("LakeAudioSilence", silenceSamples, 1, Mathf.Max(8000, _outputRate), false);
            _silence.SetData(new float[silenceSamples], 0);
            _source.clip = _silence;
            _output = host.AddComponent<FightAudioOutput>();
            _source.Play();
        }

        private static LakeSoundState SoundState(LakeState state)
        {
            switch (state)
            {
                case LakeState.Casting: return LakeSoundState.Casting;
                case LakeState.Waiting: return LakeSoundState.Waiting;
                case LakeState.Bite: return LakeSoundState.Bite;
                case LakeState.Fight: return LakeSoundState.Fight;
                case LakeState.Caught: return LakeSoundState.Caught;
                case LakeState.Lost: return LakeSoundState.Lost;
                default: return LakeSoundState.Idle;
            }
        }

        /// <summary>Ступень награды за поимку: 3 — трофей, 2 — от типичного веса вида, 1 — мельче.</summary>
        private static int RewardTier(FishSpecies species, bool trophy, int weightG)
        {
            if (trophy)
                return 3;
            if (species == null || species.weightG == null)
                return weightG >= 1500 ? 3 : weightG >= 400 ? 2 : 1;
            return weightG >= species.weightG.trophy ? 3 : weightG >= species.weightG.typical ? 2 : 1;
        }
    }
}
