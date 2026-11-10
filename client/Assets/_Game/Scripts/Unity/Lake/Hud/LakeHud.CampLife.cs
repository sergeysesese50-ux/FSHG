using UnityEngine;

namespace Fishing.Unity
{
    // Жизнь турбазы поверх картинки: облака плывут, вода бликует, лодка качается, из трубы идёт дым, пролетают птицы,
    // на причале сидит рыбак с поплавком, у рынка продавец показывает рыбу. Всё рисуется теми же примитивами, что
    // и остальной интерфейс, в координатах сцены (см. LakeHud.Camp.cs); лодка — отдельная картинка
    // Resources/Art/CampBoat.png. Прохожих на тропинке нет — убраны по решению автора игры.
    public sealed partial class LakeHud
    {
        private const string CampBoatPath = "Art/CampBoat";
        private bool _campLifeReady;
        private float _campTime;
        private Texture2D _campBoat;
        private bool _campBoatMissing;

        // ---------- Шаг ----------

        private void TickCamp(float dt)
        {
            if (!_campOpen)
                return;
            _campLifeReady = true;
            _campTime += dt;
        }

        // ---------- Рисование ----------

        /// <summary>Точка сцены → точка экрана.</summary>
        private Vector2 CampPoint(float x, float y) => new Vector2(_campOrigin.x + x * _campUnit, _campOrigin.y + y * _campUnit);

        private void CampLife()
        {
            if (!_drawing || !_campLifeReady)
                return;
            float t = _campTime;
            float u = _campUnit;

            // Облака плывут слева направо и возвращаются.
            for (int i = 0; i < 6; i++)
            {
                float size = 0.7f + 0.12f * ((i * 7) % 4);
                float span = 760f;
                float x = Mathf.Repeat(i * 131f + t * (2.2f + 0.5f * (i % 3)), span) - 190f;
                float y = -120f + i * 58f + 10f * Mathf.Sin(i * 2.1f);
                Cloud(CampPoint(x, y), size * u);
            }

            // Блики на воде.
            for (int i = 0; i < 9; i++)
            {
                float x = -60f + i * 62f + 12f * Mathf.Sin(t * 0.3f + i);
                float y = 290f + (i * 37 % 86);
                float alpha = 0.25f + 0.45f * (0.5f + 0.5f * Mathf.Sin(t * (1.1f + 0.17f * i) + i * 1.9f));
                float half = (20f + 14f * (i % 3)) * u;
                var c = CampPoint(x, y);
                Round(new Rect(c.x - half, c.y - 1.3f * u, 2 * half, 2.6f * u), new Color(1f, 1f, 1f, alpha), 1.3f * u);
            }

            Boat(t, u);
            Smoke(t, u);
            Birds(t, u);
            Angler(t, u);
            Seller(t, u);
        }

        private void Cloud(Vector2 c, float k)
        {
            var white = new Color(1f, 1f, 1f, 0.96f);
            Round(new Rect(c.x - 46 * k, c.y - 12 * k, 92 * k, 26 * k), white, 13 * k);
            Round(new Rect(c.x - 2 * k, c.y - 26 * k, 64 * k, 32 * k), white, 16 * k);
            Round(new Rect(c.x - 56 * k, c.y - 16 * k, 50 * k, 22 * k), white, 11 * k);
        }

        private void Boat(float t, float u)
        {
            float x = 300f + 7f * Mathf.Sin(t * 0.11f);
            float y = 316f + 1.6f * Mathf.Sin(t * 1.3f);
            var c = CampPoint(x, y);
            Round(new Rect(c.x - 32 * u, c.y - 1 * u, 64 * u, 7 * u), new Color(0.05f, 0.3f, 0.45f, 0.35f), 3.5f * u);
            if (_campBoat == null && !_campBoatMissing)
            {
                _campBoat = Resources.Load<Texture2D>(CampBoatPath);
                _campBoatMissing = _campBoat == null;
            }

            if (_campBoat == null)
                return;
            var matrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(2.5f * Mathf.Sin(t * 0.9f), c);
            // Картинка лодки 64×44 единицы сцены, точка опоры — середина днища (32, 38).
            GUI.DrawTexture(new Rect(c.x - 32 * u, c.y - 38 * u, 64 * u, 44 * u), _campBoat, ScaleMode.StretchToFill, true);
            GUI.matrix = matrix;
        }

        /// <summary>Дым из трубы домика: клубы поднимаются, растут и тают.</summary>
        private void Smoke(float t, float u)
        {
            for (int i = 0; i < 5; i++)
            {
                float age = Mathf.Repeat(t * 0.16f + i / 5f, 1f);
                // Труба печки — на левом скате крыши домика (tools/art/camp_day.py, C.rect(56, 14, …)).
                float x = 261f + 9f * age * Mathf.Sin(t * 0.4f + i * 1.3f) + 7f * age;
                float y = 681f - 52f * age;
                Disc(CampPoint(x, y), (4.5f + 9f * age) * u, new Color(1f, 1f, 1f, 0.55f * (1f - age) * Mathf.Clamp01(age * 6f)));
            }
        }

        /// <summary>Две птицы пролетают над лесом, взмахивая крыльями.</summary>
        private void Birds(float t, float u)
        {
            var ink = new Color(0.10f, 0.22f, 0.32f, 0.85f);
            for (int i = 0; i < 2; i++)
            {
                float x = Mathf.Repeat(t * (16f + 5f * i) + i * 320f, 820f) - 200f;
                float y = 70f + 60f * i + 9f * Mathf.Sin(t * 0.7f + i * 2f);
                float flap = 0.35f + 0.5f * Mathf.Sin(t * 9f + i * 1.7f);
                var c = CampPoint(x, y);
                float wing = 7f * u;
                Bar(c, c + new Vector2(-wing, -wing * flap), 1.6f * u, ink);
                Bar(c, c + new Vector2(wing, -wing * flap), 1.6f * u, ink);
            }
        }

        /// <summary>Рыбак на конце причала: сидит с удочкой, поплавок покачивается и иногда ныряет.</summary>
        private void Angler(float t, float u)
        {
            var seat = CampPoint(204f, 333f);
            float k = 0.8f * u;
            float twitch = Mathf.Max(0f, Mathf.Sin(t * 0.5f) - 0.94f) * 16f;
            var hand = seat + new Vector2(-5 * k, -13 * k);
            var tip = seat + new Vector2(-36 * k, -30 * k - twitch * 3 * k);
            var floatAt = CampPoint(168f, 356f + 0.8f * Mathf.Sin(t * 2.1f) + twitch * 2.5f);
            Bar(tip, floatAt, 0.8f * u, new Color(1f, 1f, 1f, 0.7f));
            Bar(hand, tip, 1.4f * u, new Color(0.25f, 0.16f, 0.10f));
            Disc(floatAt, 2.2f * u, new Color(0.92f, 0.26f, 0.18f));
            Disc(floatAt + new Vector2(0f, -1.6f * u), 1.3f * u, Color.white);
            // Сидит: туловище и голова, ноги свешены с настила.
            var skin = new Color(0.96f, 0.80f, 0.66f);
            Bar(seat + new Vector2(-2 * k, 0), seat + new Vector2(-6 * k, 8 * k), 3.2f * k, new Color(0.16f, 0.22f, 0.35f));
            Round(new Rect(seat.x - 5 * k, seat.y - 15 * k, 10 * k, 15 * k), new Color(0.20f, 0.55f, 0.35f), 4 * k);
            Bar(seat + new Vector2(-3 * k, -11 * k), hand, 2.6f * k, new Color(0.20f, 0.55f, 0.35f));
            Disc(seat + new Vector2(0, -20 * k), 4.4f * k, skin);
            Round(new Rect(seat.x - 6.5f * k, seat.y - 25.5f * k, 13 * k, 4.2f * k), new Color(0.85f, 0.75f, 0.45f), 2 * k);
            Round(new Rect(seat.x - 4.2f * k, seat.y - 29 * k, 8.4f * k, 6 * k), new Color(0.85f, 0.75f, 0.45f), 3 * k);
        }

        /// <summary>Продавец у рынка: стоит слева от прилавка (справа проходит тропинка) и время от времени поднимает рыбу.</summary>
        private void Seller(float t, float u)
        {
            float raise = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(Mathf.Sin(t * 0.9f) * 2f));
            var feet = CampPoint(30f, 608f);
            Person(feet, 1.2f * u, new Color(1f, 1f, 1f), new Color(0.20f, 0.26f, 0.36f), new Color(0.30f, 0.20f, 0.12f),
                t * 1.5f, 0f, 1f, raise, true);
        }

        /// <summary>
        /// Человечек. feet — точка под ногами на экране, k — пикселей на единицу (рост ≈ 31 единица), walk — доля
        /// размаха шага (0 стоит), raise — насколько поднята правая рука, withFish — в ней рыба.
        /// </summary>
        private void Person(Vector2 feet, float k, Color shirt, Color pants, Color hair, float phase, float walk, float alpha,
            float raise, bool withFish)
        {
            var skin = WithAlpha(new Color(0.96f, 0.80f, 0.66f), alpha);
            shirt = WithAlpha(shirt, alpha);
            pants = WithAlpha(pants, alpha);
            hair = WithAlpha(hair, alpha);
            float swing = Mathf.Sin(phase) * walk;
            float bob = Mathf.Abs(Mathf.Cos(phase)) * walk * 0.8f * k;

            Round(new Rect(feet.x - 8 * k, feet.y - 2 * k, 16 * k, 4.5f * k), new Color(0.05f, 0.2f, 0.1f, 0.28f * alpha), 2.2f * k);
            var hip = feet + new Vector2(0, -12 * k - bob);
            Bar(hip + new Vector2(-2 * k, 0), feet + new Vector2(-2 * k + swing * 4.5f * k, -1 * k), 3.4f * k, pants);
            Bar(hip + new Vector2(2 * k, 0), feet + new Vector2(2 * k - swing * 4.5f * k, -1 * k), 3.4f * k, pants);
            Round(new Rect(feet.x - 5.5f * k, feet.y - 24.5f * k - bob, 11 * k, 14 * k), shirt, 4 * k);

            var leftShoulder = feet + new Vector2(-5 * k, -22 * k - bob);
            var rightShoulder = feet + new Vector2(5 * k, -22 * k - bob);
            Bar(leftShoulder, leftShoulder + new Vector2(-1.5f * k - swing * 3 * k, 9 * k), 2.6f * k, shirt);
            var rightHand = rightShoulder + Vector2.Lerp(new Vector2(1.5f * k + swing * 3 * k, 9 * k), new Vector2(7 * k, -6 * k), raise);
            Bar(rightShoulder, rightHand, 2.6f * k, shirt);
            if (withFish)
            {
                // Рыба в руке: серебристое тельце и хвост; чуть покачивается.
                float wag = Mathf.Sin(phase * 2f) * 1.2f * k;
                var body = new Rect(rightHand.x - 2 * k, rightHand.y - 3 * k + wag * 0.3f, 12 * k, 5.4f * k);
                Round(body, WithAlpha(new Color(0.80f, 0.88f, 0.92f), alpha), 2.7f * k);
                var tail = new Vector2(body.xMax, body.center.y);
                Bar(tail, tail + new Vector2(3.6f * k, -2.6f * k + wag), 1.6f * k, WithAlpha(new Color(0.62f, 0.74f, 0.80f), alpha));
                Bar(tail, tail + new Vector2(3.6f * k, 2.6f * k + wag), 1.6f * k, WithAlpha(new Color(0.62f, 0.74f, 0.80f), alpha));
                Disc(new Vector2(body.x + 2.6f * k, body.center.y - 0.6f * k), 0.7f * k, WithAlpha(new Color(0.1f, 0.15f, 0.2f), alpha));
            }

            var head = feet + new Vector2(0, -28.5f * k - bob);
            Disc(head, 4.4f * k, skin);
            Round(new Rect(head.x - 4.6f * k, head.y - 5 * k, 9.2f * k, 5.2f * k), hair, 2.6f * k);
        }
    }
}
