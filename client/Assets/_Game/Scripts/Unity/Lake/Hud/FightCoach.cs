using Fishing.Core;
using UnityEngine;

namespace Fishing.Unity
{
    /// <summary>
    /// Тренер (docs/specs/fight-v2.md, §5): одна строка подсказки в постоянном месте под дорожкой рыбы.
    ///
    /// Каждый кадр вываживания проверяются условия всех строк; показывается одна — самая важная:
    /// опасность (вот-вот порвёт или сойдёт) → ошибка (игрок делает не то) → возможность (сейчас самое время).
    /// Более важная строка перебивает текущую сразу; менее важная ждёт, пока текущая провисит не меньше
    /// MinShowSeconds и её условие кончится. Строка после конца условия держится ещё LingerSeconds и гаснет.
    ///
    /// Обучение: если, пока строка на экране, игрок сделал то, что она советует, — это верный ответ (один за
    /// показ). После трёх верных ответов строка больше не показывается (счётчики — в PlayerPrefs). Проигрыш
    /// с причиной, к которой строка относится, возвращает её. Тренера можно выключить (тоже PlayerPrefs).
    ///
    /// Читает только FightFrame и ввод HUD. Создаётся в LakeHud.Init (PlayerPrefs нельзя читать в конструкторе
    /// MonoBehaviour). Прототип: строки прямо в коде; в игре они уйдут в таблицы Localization.
    /// </summary>
    internal sealed class FightCoach
    {
        public enum Kind
        {
            Danger,
            Mistake,
            Opportunity,
        }

        /// <summary>Строки тренера в порядке приоритета: чем меньше номер, тем важнее.</summary>
        private enum Line
        {
            RodDanger,
            LineDanger,
            Slack,
            JumpTelegraph,
            RunTelegraph,
            SlipCrank,
            ReelStall,
            RodTop,
            NetReady,
            Swing,
            RestLift,
            NetNotReady,
        }

        private const int LineCount = 12;
        private const int RetireAfter = 3;

        private const float TightSlackCm = 30f;
        private const float DangerShare = 0.85f;
        private const float RelievedShare = 0.7f;
        private const float LabourReelLoad = 0.65f;
        private const float RestLiftMaxAngle = 0.9f;
        private const float NetHoldAngle = 0.6f;
        private const float SlipCrankSeconds = 0.4f;
        private const float ReelStallSeconds = 1.5f;
        private const float RestNoLiftSeconds = 1f;
        private const float RodTopSeconds = 1.5f;

        private const float MinShowSeconds = 1.2f;
        private const float LingerSeconds = 0.8f;
        private const float FadePerSecond = 6f;

        private const string PrefEnabled = "fishing.coach.enabled";

        private readonly int[] _correct = new int[LineCount];
        private readonly bool[] _active = new bool[LineCount];

        private int _current = -1;
        private int _shown = -1;
        private float _shownSeconds;
        private float _lingerSeconds;
        private bool _credited;
        private float _angleAtShow;

        private float _slipCrank;
        private float _reelStall;
        private float _restNoLift;
        private float _rodTop;

        public FightCoach()
        {
            Enabled = PlayerPrefs.GetInt(PrefEnabled, 1) != 0;
            for (int i = 0; i < LineCount; i++)
                _correct[i] = PlayerPrefs.GetInt(Key((Line)i), 0);
        }

        /// <summary>Тренер включён (настройка игрока).</summary>
        public bool Enabled { get; private set; }

        /// <summary>Строка на экране (держится и во время угасания) или null.</summary>
        public string Text => _shown >= 0 ? TextOf((Line)_shown) : null;

        /// <summary>Вид строки на экране — от него цвет плашки.</summary>
        public Kind CurrentKind => _shown >= 0 ? KindOf((Line)_shown) : Kind.Opportunity;

        /// <summary>Прозрачность строки 0…1 (появление и угасание).</summary>
        public float Alpha { get; private set; }

        public void SetEnabled(bool enabled)
        {
            Enabled = enabled;
            PlayerPrefs.SetInt(PrefEnabled, enabled ? 1 : 0);
            PlayerPrefs.Save();
            if (!enabled)
                Clear();
        }

        /// <summary>Все строки снова новые (для проверки обучения).</summary>
        public void ResetProgress()
        {
            for (int i = 0; i < LineCount; i++)
            {
                _correct[i] = 0;
                PlayerPrefs.SetInt(Key((Line)i), 0);
            }
            PlayerPrefs.Save();
        }

        /// <summary>Новое вываживание: таймеры условий и строка — с нуля.</summary>
        public void BeginFight()
        {
            _slipCrank = 0f;
            _reelStall = 0f;
            _restNoLift = 0f;
            _rodTop = 0f;
            Clear();
        }

        /// <summary>
        /// Конец вываживания. Поимка махом или подсачеком засчитывает строку «выудить»/«подсачек», если она
        /// висела (рыба поймана в том же кадре, что нажата кнопка, и Update этого кадра уже не было).
        /// Проигрыш возвращает строки, которые учат против его причины. Сохраняет счётчики.
        /// </summary>
        public void EndFight(FightOutcome outcome, FightCause cause)
        {
            if (outcome == FightOutcome.Landed && _current >= 0 && !_credited
                && (_current == (int)Line.Swing || _current == (int)Line.NetReady))
                Credit(_current);

            switch (cause)
            {
                case FightCause.WornByCranking:
                    Return(Line.SlipCrank);
                    break;
                case FightCause.DragTooTight:
                    Return(Line.LineDanger);
                    break;
                case FightCause.ShockRodLow:
                    Return(Line.RunTelegraph);
                    Return(Line.LineDanger);
                    break;
                case FightCause.RodHighTightDrag:
                    Return(Line.RodDanger);
                    break;
                case FightCause.Overload:
                    Return(outcome == FightOutcome.RodBroke ? Line.RodDanger : Line.LineDanger);
                    break;
                case FightCause.SlackShaking:
                    Return(Line.Slack);
                    break;
                case FightCause.JumpOnSlack:
                    Return(Line.JumpTelegraph);
                    break;
                case FightCause.NetBump:
                    Return(Line.NetNotReady);
                    Return(Line.NetReady);
                    break;
                case FightCause.SwingFresh:
                    Return(Line.NetNotReady);
                    Return(Line.Swing);
                    break;
                case FightCause.Idle:
                    Return(Line.RestLift);
                    Return(Line.RodTop);
                    break;
                // SpoolEmptyDragLoose: строки «затяни фрикцион» у тренера нет; None — нечего возвращать.
            }

            Clear();
            PlayerPrefs.Save();
        }

        /// <summary>
        /// Кадр вываживания (State == Fight). reel и lift — что реально уходит в модель («Подтянуть» подсечки,
        /// которое ещё не отпустили, — не lift); net — нажат «Подсачек»; dragDelta — шаг фрикциона в этом кадре, г.
        /// </summary>
        public void Update(FightFrame f, bool reel, bool lift, bool net, int dragDelta, float dt)
        {
            if (!Enabled)
            {
                Clear();
                return;
            }

            bool tight = f.SlackCm <= TightSlackCm;
            bool telegraph = f.TelegraphProgress > 0f;
            _slipCrank = f.IsCrankingIntoSlip ? _slipCrank + dt : 0f;
            _reelStall = f.ReelLoad > LabourReelLoad ? _reelStall + dt : 0f;
            // «Подтягивай» не к месту, когда пауза кончается (идёт телеграф другой фазы) и когда катушка уже
            // упирается (_reelStall идёт): тогда через миг его перебила бы более важная строка — мигание.
            bool restAhead = f.Phase == FishPhase.Rest && (!telegraph || f.NextPhase == FishPhase.Rest);
            bool restCue = restAhead && !lift && tight && f.RodAngle < RestLiftMaxAngle && _reelStall <= 0f;
            _restNoLift = restCue ? _restNoLift + dt : 0f;
            // Рыба вот-вот уйдёт и заберёт леску (уход, вглубь, бросок от берега): подмотку надо отпустить.
            bool runAhead = telegraph
                            && (f.NextPhase == FishPhase.Run || f.NextPhase == FishPhase.Dive || f.NextPhase == FishPhase.Surge);
            // «Подтянуть» держат, а удилище уже в самом верху: подъёма нет, для модели это простой (10 с — и крючок
            // слабеет). Не на пробуксовке (там простой не копится, а крутить вредно), не перед уходом рыбы и не в
            // зоне подсачека (там удилище и надо держать высоко) — иначе совет спорил бы со строками «не крути»,
            // «отпусти подмотку» и «держи удилище выше».
            bool rodTopCue = lift && !reel && f.RodAtTop && !f.IsDragSlipping && !runAhead && !f.NetShown;
            _rodTop = rodTopCue ? _rodTop + dt : 0f;

            _active[(int)Line.SlipCrank] = _slipCrank >= SlipCrankSeconds;
            _active[(int)Line.ReelStall] = _reelStall >= ReelStallSeconds;
            _active[(int)Line.RodTop] = _rodTop >= RodTopSeconds;
            _active[(int)Line.RestLift] = _restNoLift >= RestNoLiftSeconds;
            _active[(int)Line.RunTelegraph] = runAhead && reel;
            // Свечка: телеграф и сам прыжок (совет тот же — держать леску натянутой).
            _active[(int)Line.JumpTelegraph] = (telegraph && f.NextPhase == FishPhase.Jump) || f.Phase == FishPhase.Jump;
            _active[(int)Line.Slack] = !tight;
            _active[(int)Line.RodDanger] = f.LoadShare > DangerShare && f.WeakestIsRod;
            _active[(int)Line.LineDanger] = f.LoadShare > DangerShare && !f.WeakestIsRod;
            _active[(int)Line.Swing] = f.CanSwing;
            // Строки про подсачек — только когда на экране есть кнопка «Подсачек» (то же правило, что у HUD):
            // лёгкую рыбу берут махом, и у неё CanNet бывает вместе с CanSwing — тогда нужна строка «выудить»,
            // а «подсачек!» звал бы к кнопке, которой нет, и засчитывал бы урок подсачека за мах.
            // У берега, но взять подсачеком ещё нельзя (рыба не устала или удилище низко).
            _active[(int)Line.NetNotReady] = f.NetShown && !f.CanNet;
            _active[(int)Line.NetReady] = f.NetShown && f.CanNet;

            Choose(f, dt);

            if (_current >= 0 && !_credited && Responded((Line)_current, f, reel, lift, net, dragDelta))
                Credit(_current);

            Alpha = Mathf.MoveTowards(Alpha, _current >= 0 ? 1f : 0f, dt * FadePerSecond);
            if (_current < 0 && Alpha <= 0f)
                _shown = -1;
        }

        private void Choose(FightFrame f, float dt)
        {
            int best = -1;
            for (int i = 0; i < LineCount; i++)
            {
                if (_active[i] && _correct[i] < RetireAfter)
                {
                    best = i;
                    break;
                }
            }

            if (_current < 0)
            {
                if (best >= 0)
                    Show(best, f);
                return;
            }

            _shownSeconds += dt;
            bool still = _active[_current];
            _lingerSeconds = still ? 0f : _lingerSeconds + dt;
            bool settled = _shownSeconds >= MinShowSeconds;

            if (best >= 0 && best != _current && (best < _current || (!still && settled)))
                Show(best, f);
            else if (!still && settled && _lingerSeconds >= LingerSeconds)
                _current = -1;
        }

        private void Show(int line, FightFrame f)
        {
            _current = line;
            _shown = line;
            _shownSeconds = 0f;
            _lingerSeconds = 0f;
            _credited = false;
            _angleAtShow = f.RodAngle;
        }

        private void Clear()
        {
            _current = -1;
            _shown = -1;
            Alpha = 0f;
            _credited = false;
        }

        /// <summary>Сделал ли игрок то, что советует строка.</summary>
        private bool Responded(Line line, FightFrame f, bool reel, bool lift, bool net, int dragDelta)
        {
            switch (line)
            {
                case Line.SlipCrank: return !reel;
                case Line.ReelStall: return lift;
                case Line.RodTop: return reel;
                case Line.RestLift: return lift && f.Phase == FishPhase.Rest;
                case Line.RunTelegraph: return !reel;
                case Line.JumpTelegraph: return reel;
                case Line.Slack: return reel || lift;
                case Line.RodDanger: return !lift && (f.RodAngle < _angleAtShow - 0.1f || f.LoadShare < RelievedShare);
                case Line.LineDanger: return dragDelta < 0 || (!reel && f.LoadShare < RelievedShare);
                case Line.Swing: return lift;
                case Line.NetNotReady: return f.RodAngle >= NetHoldAngle;
                case Line.NetReady: return net;
                default: return false;
            }
        }

        private void Credit(int line)
        {
            _credited = true;
            if (_correct[line] >= RetireAfter)
                return;
            _correct[line]++;
            PlayerPrefs.SetInt(Key((Line)line), _correct[line]);
        }

        private void Return(Line line)
        {
            _correct[(int)line] = 0;
            PlayerPrefs.SetInt(Key(line), 0);
        }

        private static Kind KindOf(Line line)
        {
            switch (line)
            {
                case Line.RodDanger:
                case Line.LineDanger:
                case Line.Slack:
                case Line.JumpTelegraph:
                case Line.RunTelegraph:
                    return Kind.Danger;
                case Line.SlipCrank:
                case Line.ReelStall:
                case Line.RodTop:
                    return Kind.Mistake;
                default:
                    return Kind.Opportunity;
            }
        }

        /// <summary>Строки — дословно из таблицы §5.</summary>
        private static string TextOf(Line line)
        {
            switch (line)
            {
                case Line.SlipCrank: return "Фрикцион отдаёт — не крути";
                case Line.ReelStall: return "Катушка не тянет — качай: Подтянуть, потом Подмотка";
                case Line.RodTop: return "Удилище вверху — опускай Подмоткой";
                case Line.RestLift: return "Рыба встала — подтягивай!";
                case Line.RunTelegraph: return "Сейчас уйдёт — отпусти подмотку";
                case Line.JumpTelegraph: return "Свечка! Подматывай";
                case Line.Slack: return "Слабина! Подматывай";
                case Line.RodDanger: return "Опусти удилище — сломаешь";
                case Line.LineDanger: return "Опасно! Ослабь фрикцион";
                case Line.Swing: return "Подтянуть — выудить";
                case Line.NetNotReady: return "Держи удилище выше, дай устать";
                case Line.NetReady: return "Голова над водой — подсачек!";
                default: return null;
            }
        }

        private static string Key(Line line)
        {
            switch (line)
            {
                case Line.RodDanger: return "fishing.coach.rodDanger";
                case Line.LineDanger: return "fishing.coach.lineDanger";
                case Line.Slack: return "fishing.coach.slack";
                case Line.JumpTelegraph: return "fishing.coach.jumpTelegraph";
                case Line.RunTelegraph: return "fishing.coach.runTelegraph";
                case Line.SlipCrank: return "fishing.coach.slipCrank";
                case Line.ReelStall: return "fishing.coach.reelStall";
                case Line.RodTop: return "fishing.coach.rodTop";
                case Line.NetReady: return "fishing.coach.netReady";
                case Line.Swing: return "fishing.coach.swing";
                case Line.RestLift: return "fishing.coach.restLift";
                case Line.NetNotReady: return "fishing.coach.netNotReady";
                default: return "fishing.coach.unknown";
            }
        }
    }
}
