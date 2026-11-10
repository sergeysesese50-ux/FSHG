namespace Fishing.Core.Audio
{
    /// <summary>Состояние озера для звука (то же, что LakeState в Fishing.Unity, — сборке логики оно недоступно).</summary>
    public enum LakeSoundState
    {
        Idle,
        Casting,
        Waiting,
        Bite,
        Fight,
        Caught,
        Lost,
    }

    /// <summary>Повторяющиеся короткие звуки — их ставит FightGrainScheduler с точностью до сэмпла.</summary>
    public enum FightGrain
    {
        Rotor,
        Tock,
        Strain,
        Drag,
        Crackle,
        Creak,
    }

    /// <summary>Разовые звуки (играются целиком, высота 1).</summary>
    public enum FightOneShot
    {
        Cast,
        FloatPlop,
        BiteBloop,
        Hookset,
        TeleRun,
        TeleDive,
        TeleSurge,
        TeleJerk,
        TeleJump,
        SplashSmall,
        SplashBig,
        SplashJump,
        Landed,
        LineSnap,
        RodCrack,
        Escape,

        /// <summary>«Дзинь» лески: удар (Shock) близко к пределу — обрывы случаются на ударах.</summary>
        LineZing,
    }

    /// <summary>Что главный поток сообщает звуковому кроме снимка параметров.</summary>
    public enum FightAudioEventKind
    {
        /// <summary>Сыграть разовый звук Sound (вариант Variant, «даль» Far, громкость Volume).</summary>
        OneShot,

        /// <summary>Подсечка: счётчики лески, пробуксовки и срывов — с нуля (зритель с теми же кадрами слышит то же).</summary>
        FightStart,

        /// <summary>Обрыв или поломка: зёрна и петли катушки замолкают сразу.</summary>
        Cut,

        /// <summary>Фрикцион тронулся: щелчок сразу, следующий — через полные 1,2 см.</summary>
        DragBreakaway,
    }

    /// <summary>Событие для звукового потока (кладётся в очередь без блокировок).</summary>
    public struct FightAudioEvent
    {
        public FightAudioEventKind Kind;
        public FightOneShot Sound;
        public int Variant;
        public bool Far;
        public float Volume;

        /// <summary>FightAudioMixer.RenderedFrames в момент отправки: разовые звуки старше 0,3 с не играются
        /// (звук стоял — приложение было свёрнуто, — и не надо выдавать всё накопленное разом).</summary>
        public long PostedAtFrame;
    }

    /// <summary>
    /// Снимок для звукового потока: всё, что FightAudioMixer читает раз в буфер. Только значения — без ссылок, —
    /// поэтому снимок копируется целиком через тройной буфер без блокировок. Заполняет FightAudioDirector (главный поток).
    /// </summary>
    public struct FightAudioParams
    {
        /// <summary>Частота вывода, Гц (AudioSettings.outputSampleRate). 0 — ещё не известна: тишина.</summary>
        public int OutputRate;

        /// <summary>Общая громкость 0…1 (0 — звук выключен). Ставится до лимитера.</summary>
        public float Master;

        /// <summary>Идёт вываживание: зёрна катушки, фрикциона, лески и удилища разрешены.</summary>
        public bool Fight;

        /// <summary>Скорость ротора, см лески в секунду (0 — стоит). Зерно — каждые 14 см, «ток» — каждые 85 см.</summary>
        public float RotorCmPerSec;

        /// <summary>Скорость подмотки катушки без нагрузки C0, см/с.</summary>
        public float C0;

        /// <summary>Нагрузка катушки 0…1.</summary>
        public float ReelLoad;

        /// <summary>Фрикцион пробуксовывает; щелчок — каждые 1,2 см SlipCmPerSec.</summary>
        public bool Slipping;

        public float SlipCmPerSec;

        /// <summary>Натяжение лески / предел слабого звена (треск лески выше 0,88).</summary>
        public float LoadShare;

        /// <summary>Леска натянута (слабина до 30 см).</summary>
        public bool Tight;

        /// <summary>Нагрузка удилища / предел (скрип выше 0,75).</summary>
        public float RodLoadShare;

        /// <summary>Укладка лески: громкость (без волны раскладчика — её накладывает микшер) и высота.</summary>
        public float LayVolume;

        public float LayPitch;

        /// <summary>Гул лески у предела: громкость и высота.</summary>
        public float HumVolume;

        public float HumPitch;

        /// <summary>За сколько секунд петли катушки затухают (в вываживании быстро, после — плавно).</summary>
        public float LoopReleaseSeconds;

        /// <summary>Громкость фона озера (уже с приглушением на время вываживания).</summary>
        public float AmbienceVolume;

        /// <summary>Ночь 0…1 (LakeClock.Night): вода мягче и тише, птицы молчат, поют сверчки.</summary>
        public float Night;

        /// <summary>Сила ветра 0…1 (0 — штиль, 1 — 8 м/с): по ней звучит слой ветра; в штиль его нет.</summary>
        public float Wind;

        /// <summary>Самая высокая частота вывода, которую принимает микшер (выше — считается ошибкой: тишина).</summary>
        public const int MaxOutputRate = 768000;

        /// <summary>Самая низкая частота вывода (у Unity не бывает ниже 8 кГц).</summary>
        public const int MinOutputRate = 8000;

        /// <summary>Потолок скоростей в снимке, см/с (в десятки раз выше любой катушки): дальше зёрна не нужны.</summary>
        public const float MaxSpeedCmPerSec = 5000f;

        /// <summary>
        /// Все поля — в допустимых пределах, без NaN и бесконечностей (FightAudioMixer вызывает на каждом снимке):
        /// одно испорченное число не должно ни подвесить звуковой поток (бесконечная скорость — бесконечный цикл зёрен),
        /// ни «отравить» сглаженные громкости до конца сессии. NaN → нижняя граница (громкость 0, высота «как была»),
        /// +∞ → верхняя; частота вне 8–768 кГц → 0 (тишина).
        /// </summary>
        public void Sanitize()
        {
            if (OutputRate < MinOutputRate || OutputRate > MaxOutputRate)
                OutputRate = 0;
            Master = SafeMath.Clamp(Master, 0f, 1f);
            RotorCmPerSec = SafeMath.Clamp(RotorCmPerSec, 0f, MaxSpeedCmPerSec);
            C0 = SafeMath.Clamp(C0, 0f, MaxSpeedCmPerSec);
            ReelLoad = SafeMath.Clamp(ReelLoad, 0f, 1f);
            SlipCmPerSec = SafeMath.Clamp(SlipCmPerSec, 0f, MaxSpeedCmPerSec);
            LoadShare = SafeMath.Clamp(LoadShare, 0f, 4f);
            RodLoadShare = SafeMath.Clamp(RodLoadShare, 0f, 4f);
            LayVolume = SafeMath.Clamp(LayVolume, 0f, 2f);
            LayPitch = SafeMath.Clamp(LayPitch, 0f, 4f);
            HumVolume = SafeMath.Clamp(HumVolume, 0f, 2f);
            HumPitch = SafeMath.Clamp(HumPitch, 0f, 4f);
            LoopReleaseSeconds = SafeMath.Clamp(LoopReleaseSeconds, 0f, 10f);
            AmbienceVolume = SafeMath.Clamp(AmbienceVolume, 0f, 2f);
            Night = SafeMath.Clamp(Night, 0f, 1f);
            Wind = SafeMath.Clamp(Wind, 0f, 1f);
        }
    }

    /// <summary>Числа для звука без NaN и бесконечностей: звуковой поток не должен ни зависнуть, ни «отравиться».</summary>
    internal static class SafeMath
    {
        /// <summary>x в [lo, hi]: NaN и −∞ → lo, +∞ → hi.</summary>
        public static float Clamp(float x, float lo, float hi) => x > lo ? (x < hi ? x : hi) : lo;

        /// <summary>x в [lo, hi]: NaN и −∞ → lo, +∞ → hi.</summary>
        public static double Clamp(double x, double lo, double hi) => x > lo ? (x < hi ? x : hi) : lo;

        public static bool IsFinite(double x) => !double.IsNaN(x) && !double.IsInfinity(x);
    }
}
