using System;
using System.Collections.Generic;
using System.Linq;
using Fishing.Core;
using Fishing.Core.Audio;
using NUnit.Framework;

namespace Fishing.Tests
{
    /// <summary>
    /// Звук озера без движка (Fishing.Core/Audio): расписание зёрен с точностью до сэмпла, лимитер, швы петель,
    /// различимость вариантов и микшер целиком; защита звукового потока от NaN и бесконечностей, память банка,
    /// петли без периода, слышимость предупреждения у предела и приглушение катушки под разовые звуки.
    /// Числа рецептов — в FightGrainScheduler и FightSynth.
    /// </summary>
    public class FightAudioTests
    {
        private const int Rate = 48000;

        private struct Grain
        {
            public FightGrain Kind;
            public int Variant;
            public long Frame;
            public float Gain;
            public float Pitch;
        }

        /// <summary>Гонит расписание seconds секунд блоками block; параметры — от времени начала блока.</summary>
        private static List<Grain> Run(FightGrainScheduler scheduler, Func<double, FightAudioParams> at, double seconds, int block = 480)
        {
            var list = new List<Grain>();
            var starts = new GrainStart[256];
            long frame = 0;
            while (frame < seconds * Rate)
            {
                int count = 0;
                scheduler.Advance(at((double)frame / Rate), block, Rate, starts, ref count);
                for (int i = 0; i < count; i++)
                    list.Add(new Grain { Kind = starts[i].Kind, Variant = starts[i].Variant, Frame = frame + starts[i].Offset, Gain = starts[i].Gain, Pitch = starts[i].Pitch });
                frame += block;
            }
            return list;
        }

        private static FightAudioParams Reeling(float cmPerSec, float load = 0.1f) => new FightAudioParams
        {
            OutputRate = Rate, Fight = true, RotorCmPerSec = cmPerSec, C0 = 110, ReelLoad = load, LoadShare = 0.3f, Tight = true, RodLoadShare = 0.3f,
        };

        private static FightAudioParams Slipping(float cmPerSec) => new FightAudioParams
        {
            OutputRate = Rate, Fight = true, Slipping = true, SlipCmPerSec = cmPerSec, C0 = 110, LoadShare = 0.6f, Tight = true, RodLoadShare = 0.5f,
        };

        // ---------- Расписание зёрен ----------

        [TestCase(110f, 10.0)]
        [TestCase(250f, 6.0)]
        [TestCase(40f, 12.0)]
        public void RotorGrainsFollowReeledDistance(float cmPerSec, double seconds)
        {
            var grains = Run(new FightGrainScheduler(1), _ => Reeling(cmPerSec), seconds);

            // Скорость сглажена экспонентой 15 мс: на старте «теряется» v·0,015 см.
            double cm = cmPerSec * (seconds - 0.015);
            int rotor = grains.Count(g => g.Kind == FightGrain.Rotor);
            int tock = grains.Count(g => g.Kind == FightGrain.Tock);
            Assert.That(rotor, Is.InRange((int)Math.Floor(cm / FightGrainScheduler.RotorCm) - 1, (int)Math.Floor(cm / FightGrainScheduler.RotorCm) + 1));
            int tocks = (int)Math.Floor((cm - FightGrainScheduler.HandleOffsetCm) / FightGrainScheduler.HandleCm);
            Assert.That(tock, Is.InRange(tocks - 1, tocks + 1));
        }

        [TestCase(9f, 8.0)]
        [TestCase(30f, 5.0)]
        [TestCase(95f, 3.0)]
        public void DragTicksFollowSlippedDistance(float cmPerSec, double seconds)
        {
            var grains = Run(new FightGrainScheduler(2), _ => Slipping(cmPerSec), seconds);

            double cm = cmPerSec * (seconds - 0.015);
            int expected = (int)Math.Floor(cm / FightGrainScheduler.DragCm);
            Assert.That(grains.Count(g => g.Kind == FightGrain.Drag), Is.InRange(expected - 1, expected + 1));
        }

        [Test]
        public void GrainIntervalsJitterWithinTheRecipe()
        {
            const float cmPerSec = 110f;
            var rotor = Run(new FightGrainScheduler(3), _ => Reeling(cmPerSec), 12).Where(g => g.Kind == FightGrain.Rotor)
                .Select(g => g.Frame).OrderBy(f => f).Skip(1).ToList();
            double period = FightGrainScheduler.RotorCm / cmPerSec * Rate;
            var ioi = rotor.Zip(rotor.Skip(1), (a, b) => (double)(b - a)).ToList();

            // Разброс ротора — сдвиг 0…8% периода: соседние интервалы в пределах ±8% и ±1 сэмпл.
            Assert.That(ioi.Min(), Is.GreaterThanOrEqualTo(period * 0.92 - 1));
            Assert.That(ioi.Max(), Is.LessThanOrEqualTo(period * 1.08 + 1));
            Assert.That(ioi.Average(), Is.EqualTo(period).Within(period * 0.01));
            double sd = Math.Sqrt(ioi.Sum(v => (v - period) * (v - period)) / ioi.Count);
            Assert.That(sd / period, Is.GreaterThan(0.01), "ритм не должен быть метрономом");

            var drag = Run(new FightGrainScheduler(3), _ => Slipping(14f), 8).Where(g => g.Kind == FightGrain.Drag)
                .Select(g => g.Frame).OrderBy(f => f).Skip(1).ToList();
            double dragPeriod = FightGrainScheduler.DragCm / 14.0 * Rate;
            var dioi = drag.Zip(drag.Skip(1), (a, b) => (double)(b - a)).ToList();
            Assert.That(dioi.Min(), Is.GreaterThanOrEqualTo(dragPeriod * 0.9 - 1));
            Assert.That(dioi.Max(), Is.LessThanOrEqualTo(dragPeriod * 1.1 + 1));
        }

        [Test]
        public void SameSeedGivesSameGrainsAndAnotherSeedDoesNot()
        {
            // Всё сразу: подмотка под нагрузкой (натуга), пробуксовка, треск лески и скрип удилища.
            Func<double, FightAudioParams> at = t => new FightAudioParams
            {
                OutputRate = Rate, Fight = true, C0 = 110,
                RotorCmPerSec = t < 3 ? 90 : 25, ReelLoad = t < 3 ? 0.2f : 0.9f,
                Slipping = t > 4, SlipCmPerSec = t > 4 ? 40 : 0,
                LoadShare = t > 5 ? 0.95f : 0.5f, Tight = true, RodLoadShare = t > 5 ? 0.95f : 0.4f,
            };
            var a = Run(new FightGrainScheduler(7), at, 8);
            var b = Run(new FightGrainScheduler(7), at, 8);
            var c = Run(new FightGrainScheduler(8), at, 8);

            Assert.That(a.Count, Is.GreaterThan(200));
            foreach (FightGrain kind in Enum.GetValues(typeof(FightGrain)))
                Assert.That(a.Any(g => g.Kind == kind), kind.ToString());
            Assert.AreEqual(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.AreEqual(a[i].Kind, b[i].Kind);
                Assert.AreEqual(a[i].Variant, b[i].Variant);
                Assert.AreEqual(a[i].Frame, b[i].Frame);
                Assert.AreEqual(a[i].Gain, b[i].Gain);
                Assert.AreEqual(a[i].Pitch, b[i].Pitch);
            }
            bool differs = a.Count != c.Count || a.Zip(c, (x, y) => x.Variant != y.Variant || x.Frame != y.Frame).Any(d => d);
            Assert.That(differs, "другое зерно расписания — другие варианты и разброс");
        }

        [Test]
        public void NoGrainsWhenIdle()
        {
            // Вне вываживания — даже с ненулевыми скоростями из старого снимка.
            var outside = Reeling(110);
            outside.Fight = false;
            outside.Slipping = true;
            outside.SlipCmPerSec = 50;
            outside.LoadShare = 1f;
            outside.RodLoadShare = 1f;
            Assert.AreEqual(0, Run(new FightGrainScheduler(4), _ => outside, 5).Count);

            // В вываживании, но катушка стоит, фрикцион держит, нагрузки малы.
            var still = new FightAudioParams { OutputRate = Rate, Fight = true, C0 = 110, LoadShare = 0.5f, Tight = true, RodLoadShare = 0.5f };
            Assert.AreEqual(0, Run(new FightGrainScheduler(4), _ => still, 5).Count);

            // Подмотку отпустили — через 50 мс зёрен больше нет.
            var after = Run(new FightGrainScheduler(4), t => t < 2 ? Reeling(110) : still, 6);
            Assert.That(after.Count(g => g.Frame > 2.05 * Rate + 0.06 * Rate), Is.EqualTo(0));
        }

        [Test]
        public void BreakawayTicksAtOnceAndThenAfterAFullStep()
        {
            var scheduler = new FightGrainScheduler(5);
            var starts = new GrainStart[16];
            int count = 0;
            scheduler.Breakaway(starts, ref count);
            Assert.AreEqual(1, count);
            Assert.AreEqual(FightGrain.Drag, starts[0].Kind);
            Assert.AreEqual(0, starts[0].Offset);

            // Следующий щелчок — не раньше чем через 1,2 см при 12 см/с (100 мс), с поправкой на сглаживание.
            var next = Run(scheduler, _ => Slipping(12f), 1).Where(g => g.Kind == FightGrain.Drag).Select(g => g.Frame).ToList();
            Assert.That(next.Count, Is.GreaterThan(0));
            Assert.That(next.Min(), Is.GreaterThanOrEqualTo((long)(0.09 * Rate)));
        }

        // ---------- Лимитер ----------

        [TestCase(22050)]
        [TestCase(44100)]
        [TestCase(48000)]
        [TestCase(96000)]
        public void LimiterNeverExceedsZeroDbfs(int rate)
        {
            var limiter = new SoftLimiter();
            limiter.SetRate(rate);
            var random = new Random(rate);
            var buffer = new float[1024];
            double peak = 0;
            long t = 0;
            for (int block = 0; block < 400; block++)
            {
                int n = 1 + random.Next(buffer.Length);
                for (int i = 0; i < n; i++, t++)
                {
                    double seconds = (double)t / rate;
                    int part = (int)(seconds * 2) % 5;
                    double x = part switch
                    {
                        0 => 10 * (random.NextDouble() * 2 - 1),                         // шум +20 дБ
                        1 => t % 500 == 0 ? 50 : 0.01 * (random.NextDouble() - 0.5),      // одиночные импульсы +34 дБ
                        2 => Math.Sign(Math.Sin(2 * Math.PI * 330 * seconds)) * 4.0,      // меандр +12 дБ
                        3 => 3 * Math.Sin(2 * Math.PI * (40 + 4000 * (seconds % 0.5)) * seconds), // свип
                        _ => random.NextDouble() < 0.5 ? 0.95 : -0.95,                     // у самой границы
                    };
                    buffer[i] = (float)x;
                }
                limiter.Process(buffer, n);
                for (int i = 0; i < n; i++)
                    peak = Math.Max(peak, Math.Abs(buffer[i]));
            }
            Assert.That(peak, Is.LessThan(1.0));
            Assert.That(peak, Is.LessThanOrEqualTo(0.99));
        }

        [Test]
        public void LimiterHoldsTheCeilingAndPassesQuietMaterial()
        {
            var limiter = new SoftLimiter();
            limiter.SetRate(Rate);
            var loud = new float[Rate];
            for (int i = 0; i < loud.Length; i++)
                loud[i] = (float)(2.5 * Math.Sin(2 * Math.PI * 440 * i / Rate));
            limiter.Process(loud, loud.Length);
            Assert.That(loud.Skip(1000).Max(v => Math.Abs(v)), Is.LessThanOrEqualTo(SoftLimiter.Ceiling + 1e-3));

            // Тихое (−6 дБFS) — без изменений, только задержка на окно заглядывания.
            var quietLimiter = new SoftLimiter();
            quietLimiter.SetRate(Rate);
            var source = new float[4096];
            for (int i = 0; i < source.Length; i++)
                source[i] = (float)(0.5 * Math.Sin(2 * Math.PI * 220 * i / Rate));
            var quiet = (float[])source.Clone();
            quietLimiter.Process(quiet, quiet.Length);
            int delay = (int)Math.Round(0.0015 * Rate) - 1;
            for (int i = delay; i < quiet.Length; i++)
                Assert.That(quiet[i], Is.EqualTo(source[i - delay]).Within(1e-6));
            Assert.AreEqual(1f, quietLimiter.MinGain, 1e-6);
        }

        // ---------- Петли ----------

        [Test]
        public void LakeBedLoopHasNoSeam()
        {
            AssertSeamless(LakeSynth.LakeBed(FightSoundBank.DefaultAmbienceSeconds, out _), "фон озера");
        }

        [Test]
        public void ReelLoopsHaveNoSeam()
        {
            AssertSeamless(FightSynth.LayBed(), "укладка лески");
            AssertSeamless(FightSynth.LineHum(), "гул лески");
        }

        /// <summary>
        /// Конец → начало: скачок сэмпла не больше 99,5-го процентиля скачков внутри петли, а громкость 20 мс до и после
        /// шва различается не больше, чем у 99% таких же пар внутри петли (и уж точно меньше 3 дБ).
        /// </summary>
        private static void AssertSeamless(float[] x, string what)
        {
            int n = x.Length;
            var steps = new double[n];
            for (int i = 0; i < n; i++)
                steps[i] = Math.Abs(x[i] - x[(i + n - 1) % n]);
            double seamStep = steps[0];
            Array.Sort(steps);
            Assert.That(seamStep, Is.LessThanOrEqualTo(steps[(int)(0.995 * (n - 1))]), what + ": скачок сэмпла на шве");

            int w = (int)Math.Round(0.02 * LakeSynth.SampleRate);
            double Jump(int at)
            {
                double a = 0, b = 0;
                for (int i = 1; i <= w; i++)
                {
                    double v = x[((at - i) % n + n) % n];
                    a += v * v;
                }
                for (int i = 0; i < w; i++)
                {
                    double v = x[(at + i) % n];
                    b += v * v;
                }
                return Math.Abs(10 * Math.Log10((b + 1e-20) / (a + 1e-20)));
            }
            var jumps = new List<double>();
            for (int at = w; at < n - w; at += LakeSynth.SampleRate / 200)
                jumps.Add(Jump(at));
            jumps.Sort();
            double seamJump = Jump(0);
            Assert.That(seamJump, Is.LessThanOrEqualTo(jumps[(int)(0.99 * (jumps.Count - 1))]), what + ": скачок громкости на шве");
            Assert.That(seamJump, Is.LessThan(3.0), what + ": скачок громкости на шве, дБ");
        }

        // ---------- Различимость вариантов (замечания AudioCritic) ----------

        [Test]
        public void TockVariantsAreAllDistinct()
        {
            var tocks = Enumerable.Range(0, FightSynth.TockVariants).Select(i => FightSynth.GearTock(i, 0x70C00000u + (uint)i * 104729u)).ToArray();
            for (int a = 0; a < tocks.Length; a++)
                for (int b = a + 1; b < tocks.Length; b++)
                    Assert.That(Similarity(tocks[a], tocks[b]), Is.LessThan(0.97), $"«ток» {a} и {b}");
        }

        [Test]
        public void QuietAndLoudCreaksAreDifferentSounds()
        {
            for (int i = 0; i < 6; i++)
            {
                var quiet = FightSynth.RodCreak(0xC4EA0000u + (uint)i * 86028121u, false);
                var loud = FightSynth.RodCreak(0xC4EA0000u + (uint)(i + 6) * 86028121u, true);
                Assert.That(Similarity(quiet, loud), Is.LessThan(0.9));
                Assert.That(Centroid(loud) - Centroid(quiet), Is.GreaterThan(100), "громкий скрип ярче тихого");
            }
        }

        [Test]
        public void RecipesAreDeterministic()
        {
            CollectionAssert.AreEqual(FightSynth.RotorGrain(42), FightSynth.RotorGrain(42));
            CollectionAssert.AreEqual(FightSynth.RodCreak(42, true), FightSynth.RodCreak(42, true));
            CollectionAssert.AreEqual(LakeSynth.CricketChirp(42), LakeSynth.CricketChirp(42));
            CollectionAssert.AreNotEqual(FightSynth.RotorGrain(42), FightSynth.RotorGrain(43));
        }

        /// <summary>Наибольшая нормированная взаимная корреляция двух клипов при сдвиге до ±0,5 мс (как в AudioCritic).</summary>
        private static double Similarity(float[] a, float[] b)
        {
            int sh = (int)(0.0005 * FightSynth.SampleRate), n = Math.Min(a.Length, b.Length) - 2 * sh;
            double best = 0;
            for (int s = -sh; s <= sh; s++)
            {
                double ab = 0, aa = 0, bb = 0;
                for (int i = sh; i < sh + n; i++)
                {
                    double x = a[i], y = b[i + s];
                    ab += x * y;
                    aa += x * x;
                    bb += y * y;
                }
                best = Math.Max(best, ab / Math.Sqrt(Math.Max(1e-30, aa * bb)));
            }
            return best;
        }

        /// <summary>Центр тяжести спектра по пересечениям нуля — грубо, но без БПФ: 0,5·частота пересечений.</summary>
        private static double Centroid(float[] x)
        {
            int crossings = 0;
            for (int i = 1; i < x.Length; i++)
                if ((x[i - 1] < 0) != (x[i] < 0))
                    crossings++;
            return 0.5 * crossings * FightSynth.SampleRate / x.Length;
        }

        // ---------- Микшер целиком ----------

        [Test]
        public void MixerNeverClipsAtFullVolumeAndMuteSilencesIt()
        {
            var mixer = new FightAudioMixer(new FightSoundBank(4.0), 11);
            var buffer = new float[2 * 1024];
            var p = new FightAudioParams
            {
                OutputRate = Rate, Master = 1f, Fight = true, C0 = 110, RotorCmPerSec = 60, ReelLoad = 0.95f, Slipping = true, SlipCmPerSec = 100,
                LoadShare = 1f, Tight = true, RodLoadShare = 1f, LayVolume = 0.4f, LayPitch = 1f, HumVolume = 0.25f, HumPitch = 1.15f,
                LoopReleaseSeconds = 0.15f, AmbienceVolume = 0.16f, Night = 0.5f,
            };
            mixer.Publish(p);
            mixer.Post(new FightAudioEvent { Kind = FightAudioEventKind.FightStart });
            double peak = 0;
            for (int k = 0; k < 150; k++)
            {
                foreach (FightOneShot sound in Enum.GetValues(typeof(FightOneShot)))
                    if (k % 10 == 0)
                        mixer.Post(new FightAudioEvent { Kind = FightAudioEventKind.OneShot, Sound = sound, Variant = 1, Volume = 1f });
                mixer.Render(buffer, 2);
                foreach (var v in buffer)
                    peak = Math.Max(peak, Math.Abs(v));
            }
            Assert.That(peak, Is.LessThan(1.0));
            Assert.That(peak, Is.GreaterThan(0.3), "звук есть");
            Assert.That(mixer.GrainCounts.Sum(), Is.GreaterThan(100));

            p.Master = 0f;
            mixer.Publish(p);
            for (int k = 0; k < 20; k++)
                mixer.Render(buffer, 2);
            Assert.That(buffer.Max(v => Math.Abs(v)), Is.LessThan(1e-4), "без звука — тишина");
        }

        [Test]
        public void MixerIsSilentUntilTheOutputRateIsKnownAndDeterministicAfter()
        {
            var bank = new FightSoundBank(4.0);
            var a = new FightAudioMixer(bank, 3);
            var b = new FightAudioMixer(bank, 3);
            var bufA = new float[2 * 512];
            var bufB = new float[2 * 512];
            for (int i = 0; i < bufA.Length; i++)
                bufA[i] = 1f;
            a.Render(bufA, 2);
            Assert.That(bufA.All(v => v == 0f), "до первого снимка — тишина");

            var p = new FightAudioParams { OutputRate = 44100, Master = 0.8f, Fight = true, C0 = 110, RotorCmPerSec = 110, LayVolume = 0.36f, LayPitch = 1f, AmbienceVolume = 0.16f };
            a.Publish(p);
            b.Publish(p);
            for (int k = 0; k < 100; k++)
            {
                a.Render(bufA, 2);
                b.Render(bufB, 2);
                CollectionAssert.AreEqual(bufA, bufB);
            }
            Assert.That(a.GrainCounts[(int)FightGrain.Rotor], Is.InRange(8, 10), "110 см/с × 1,16 с / 14 см ≈ 9");
        }

        // ---------- Защита звукового потока: NaN, бесконечности, огромные скорости ----------

        private static FightAudioParams Busy() => new FightAudioParams
        {
            OutputRate = Rate, Master = 0.8f, Fight = true, C0 = 110, RotorCmPerSec = 110, ReelLoad = 0.3f, LoadShare = 0.95f, Tight = true,
            RodLoadShare = 0.9f, LayVolume = 0.36f, LayPitch = 1f, HumVolume = 0.3f, HumPitch = 1f, LoopReleaseSeconds = 0.15f, AmbienceVolume = 0.09f,
        };

        /// <summary>Render на отдельном потоке: false — не вернулся за ms (звуковой поток завис бы).</summary>
        private static bool RenderWithin(FightAudioMixer mixer, float[] buffer, int ms, out Exception error)
        {
            Exception caught = null;
            var thread = new System.Threading.Thread(() =>
            {
                try
                {
                    mixer.Render(buffer, 2);
                }
                catch (Exception e)
                {
                    caught = e;
                }
            }) { IsBackground = true };
            thread.Start();
            bool done = thread.Join(ms);
            error = caught;
            return done;
        }

        [Test]
        public void MixerSurvivesNonFiniteAndHugeSnapshots()
        {
            // Было: +∞ скорости ротора — бесконечный цикл зёрен (Render не возвращался), 1e11 — полторы секунды на буфер;
            // один снимок с NaN в громкости, ночи или общей громкости — NaN до конца сессии, NaN в высоте — исключение.
            var bank = new FightSoundBank(4.0);
            var cases = new List<(string name, Func<FightAudioParams, FightAudioParams> spoil)>
            {
                ("RotorCmPerSec = +∞", p => { p.RotorCmPerSec = float.PositiveInfinity; return p; }),
                ("RotorCmPerSec = 1e11", p => { p.RotorCmPerSec = 1e11f; return p; }),
                ("SlipCmPerSec = +∞", p => { p.Slipping = true; p.SlipCmPerSec = float.PositiveInfinity; return p; }),
                ("LayVolume = NaN", p => { p.LayVolume = float.NaN; return p; }),
                ("LayPitch = NaN", p => { p.LayPitch = float.NaN; return p; }),
                ("HumVolume = NaN", p => { p.HumVolume = float.NaN; return p; }),
                ("HumPitch = +∞", p => { p.HumPitch = float.PositiveInfinity; return p; }),
                ("AmbienceVolume = NaN", p => { p.AmbienceVolume = float.NaN; return p; }),
                ("Night = NaN", p => { p.Night = float.NaN; return p; }),
                ("Master = NaN", p => { p.Master = float.NaN; return p; }),
                ("C0 = NaN", p => { p.C0 = float.NaN; return p; }),
                ("ReelLoad = NaN", p => { p.ReelLoad = float.NaN; return p; }),
                ("LoadShare = NaN", p => { p.LoadShare = float.NaN; return p; }),
                ("RodLoadShare = +∞", p => { p.RodLoadShare = float.PositiveInfinity; return p; }),
                ("LoopReleaseSeconds = NaN", p => { p.LoopReleaseSeconds = float.NaN; return p; }),
                ("OutputRate = int.MaxValue", p => { p.OutputRate = int.MaxValue; return p; }),
            };
            var buffer = new float[2 * 1024];
            foreach (var (name, spoil) in cases)
            {
                var mixer = new FightAudioMixer(bank, 5);
                mixer.Publish(Busy());
                for (int k = 0; k < 10; k++)
                    mixer.Render(buffer, 2);

                mixer.Publish(spoil(Busy()));
                bool returned = RenderWithin(mixer, buffer, 5000, out var error);
                Assert.That(returned, name + ": Render не вернулся — звуковой поток завис");
                Assert.IsNull(error, name + ": исключение на звуковом потоке");
                Assert.That(buffer.All(v => v >= -1f && v <= 1f), name + ": NaN или выход за ±1");
                for (int k = 0; k < 5; k++)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    mixer.Render(buffer, 2);
                    Assert.That(sw.Elapsed.TotalMilliseconds, Is.LessThan(200), name + ": буфер 21 мс считается слишком долго");
                }

                // Снова нормальный снимок — звук возвращается и чистый.
                mixer.Publish(Busy());
                double peak = 0;
                for (int k = 0; k < 100; k++)
                {
                    mixer.Render(buffer, 2);
                    Assert.That(buffer.All(v => v >= -1f && v <= 1f), name + ": NaN после возврата к нормальному снимку");
                    if (k >= 50)
                        peak = Math.Max(peak, buffer.Max(v => Math.Abs(v)));
                }
                Assert.That(peak, Is.GreaterThan(0.05), name + ": звук не вернулся");
                Assert.AreEqual(0, mixer.BadSamples, name + ": испорченные сэмплы на выходе");
                Assert.AreEqual(0, mixer.StateResets, name + ": состояние успело испортиться");
            }
        }

        [Test]
        public void SchedulerSurvivesInfiniteSpeeds()
        {
            var scheduler = new FightGrainScheduler(9);
            var starts = new GrainStart[256];
            var p = new FightAudioParams
            {
                Fight = true, C0 = float.NaN, RotorCmPerSec = float.PositiveInfinity, Slipping = true, SlipCmPerSec = float.PositiveInfinity,
                ReelLoad = float.NaN, LoadShare = float.PositiveInfinity, RodLoadShare = float.NaN, Tight = true,
            };
            int count = 0;
            var thread = new System.Threading.Thread(() =>
            {
                for (int k = 0; k < 20; k++)
                {
                    count = 0;
                    scheduler.Advance(p, 1024, Rate, starts, ref count);
                }
            }) { IsBackground = true };
            thread.Start();
            Assert.That(thread.Join(5000), "Advance не вернулся");
            Assert.That(count, Is.InRange(1, starts.Length));
            for (int i = 0; i < count; i++)
            {
                Assert.That(!float.IsNaN(starts[i].Gain) && !float.IsInfinity(starts[i].Gain) && starts[i].Gain > 0f, "громкость зерна");
                Assert.That(!float.IsNaN(starts[i].Pitch) && !float.IsInfinity(starts[i].Pitch) && starts[i].Pitch > 0f, "высота зерна");
            }
        }

        [Test]
        public void DirectorCleansNonFiniteInputAndCapsOneShots()
        {
            var director = new FightAudioDirector();
            var bad = new FightSoundInput
            {
                ReelInCmPerSec = float.PositiveInfinity, ReelLoad = float.NaN, IsDragSlipping = true, SlipCmPerSec = float.PositiveInfinity,
                LoadShare = float.NaN, RodLoadShare = float.PositiveInfinity, SlackCm = float.NaN, FishDistanceCm = float.NaN, PeakShare = float.NaN,
                WeightG = -5, Phase = FishPhase.Run, NextPhase = FishPhase.Jerk,
                Events = FightEvents.PhaseStart | FightEvents.Shock | FightEvents.TelegraphStart | FightEvents.JumpApex | FightEvents.Breakaway,
            };
            var events = new List<FightAudioEvent>();
            void Frame(LakeSoundState state, FightSoundInput f, float value)
            {
                director.Update(state, f, value, value, value, value, Rate);
                for (int i = 0; i < director.EventCount; i++)
                    events.Add(director.Event(i));
                var p = director.Params;
                foreach (var (field, v) in new[]
                {
                    ("Master", p.Master), ("RotorCmPerSec", p.RotorCmPerSec), ("C0", p.C0), ("ReelLoad", p.ReelLoad), ("SlipCmPerSec", p.SlipCmPerSec),
                    ("LoadShare", p.LoadShare), ("RodLoadShare", p.RodLoadShare), ("LayVolume", p.LayVolume), ("LayPitch", p.LayPitch),
                    ("HumVolume", p.HumVolume), ("HumPitch", p.HumPitch), ("AmbienceVolume", p.AmbienceVolume), ("Night", p.Night),
                })
                    Assert.That(!float.IsNaN(v) && !float.IsInfinity(v), field + " не число");
                Assert.That(p.Master, Is.InRange(0f, 1f));
                Assert.That(p.Night, Is.InRange(0f, 1f));
                Assert.That(p.RotorCmPerSec, Is.InRange(0f, 3 * 110f));
                Assert.That(p.SlipCmPerSec, Is.InRange(0f, 2000f));
            }
            foreach (float value in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                Frame(LakeSoundState.Fight, bad, value);
                Frame(LakeSoundState.Fight, bad, value);
                Frame(LakeSoundState.Idle, default, value);
            }

            // Разовые звуки — во всех ситуациях не громче OneShotMax: тяжёлая рыба у самых ног, все события, все исходы.
            var loud = new FightSoundInput
            {
                ReelInCmPerSec = 110, ReelLoad = 0.2f, LoadShare = 0.95f, RodLoadShare = 0.9f, SlackCm = 0, FishDistanceCm = 0, WeightG = 9000,
                PeakShare = 1.5f, Phase = FishPhase.Run, NextPhase = FishPhase.Jump,
                Events = FightEvents.PhaseStart | FightEvents.Shock | FightEvents.TelegraphStart | FightEvents.JumpApex | FightEvents.SurgeStart,
            };
            foreach (FightOutcome outcome in Enum.GetValues(typeof(FightOutcome)))
            {
                var d = new FightAudioDirector();
                foreach (var state in new[] { LakeSoundState.Idle, LakeSoundState.Casting, LakeSoundState.Waiting, LakeSoundState.Bite, LakeSoundState.Fight })
                {
                    d.Update(state, loud, 110, 0, 0, 1, Rate);
                    for (int i = 0; i < d.EventCount; i++)
                        events.Add(d.Event(i));
                }
                var end = loud;
                end.EndedThisFrame = true;
                end.Outcome = outcome;
                d.Update(LakeSoundState.Lost, end, 110, 0, 0, 1, Rate);
                for (int i = 0; i < d.EventCount; i++)
                    events.Add(d.Event(i));
            }
            var shots = events.Where(e => e.Kind == FightAudioEventKind.OneShot).ToList();
            Assert.That(shots.Count, Is.GreaterThan(20));
            Assert.That(shots.Any(e => e.Sound == FightOneShot.LineZing), "удар у предела — «дзинь» лески");
            foreach (var e in shots)
                Assert.That(e.Volume, Is.GreaterThan(0f).And.LessThanOrEqualTo(FightAudioDirector.OneShotMax), e.Sound.ToString());

            // Пик клипа на OneShotMax при общей громкости по умолчанию — ровно под порог лимитера.
            Assert.That(FightSoundBank.OneShotPeak * FightAudioDirector.OneShotMax * FightAudioMixer.Makeup * FightAudioMixer.DefaultMaster,
                Is.EqualTo(SoftLimiter.Ceiling).Within(1e-4));
        }

        // ---------- Банк: память ----------

        [Test]
        public void LakeBedBuildsWithoutFullLengthTemporaries()
        {
            // Было: семь float[n] и три double[n] одновременно — 71 МБ на петлю 64 с (5,4 МБ). Теперь итог и один черновой слой.
            LakeSynth.LakeBed(2, out _);
            long before = GC.GetAllocatedBytesForCurrentThread();
            var bed = LakeSynth.LakeBed(FightSoundBank.DefaultAmbienceSeconds, out _);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            if (allocated <= 0)
                Assert.Ignore("счётчик выделений памяти недоступен в этой среде выполнения");
            long clip = (long)bed.Length * sizeof(float);
            Assert.That(allocated, Is.LessThan(3 * clip), $"выделено {allocated / 1048576.0:0.0} МБ на клип {clip / 1048576.0:0.0} МБ");
        }

        // ---------- Различимость: натуга и треск лески ----------

        [Test]
        public void StrainAndCrackleVariantsAreDistinct()
        {
            // Было: шесть малых зёрен натуги — один звук (сходство 0,95–0,99), треск лески — 7 различимых из 8.
            var bank = new FightSoundBank(4.0);
            void Check(string what, float[][] set)
            {
                for (int a = 0; a < set.Length; a++)
                    for (int b = a + 1; b < set.Length; b++)
                        Assert.That(Similarity(set[a], set[b]), Is.LessThan(0.9), $"{what}: {a} и {b}");
            }
            Check("натуга, малые", bank.Strain.Take(FightSoundBank.StrainVariants).ToArray());
            Check("натуга, большие", bank.Strain.Skip(FightSoundBank.StrainVariants).ToArray());
            Check("треск лески", bank.Crackle);
        }

        // ---------- Петли без периода ----------

        [Test]
        public void ReelLoopsWanderInsteadOfRepeating()
        {
            // Было: укладка лески повторялась точно каждые 4 с / высоту (3,6 с при 110 см/с) — сходство волны 0,99.
            var bank = new FightSoundBank(4.0);
            const float pitch = 1.113f;
            var p = new FightAudioParams { OutputRate = Rate, Master = 0.01f, LayVolume = 0.36f, LayPitch = pitch, LoopReleaseSeconds = 0.15f };
            var x = RenderMono(new FightAudioMixer(bank, 3), p, 20);
            double period = bank.LayBed.Length * (double)Rate / FightSoundBank.SampleRate / pitch;
            foreach (int k in new[] { 1, 2 })
                Assert.That(LagCorrelation(x, 2 * Rate, 14 * Rate, k * period), Is.LessThan(0.2), $"сходство через {k} период(а) петли");

            // Перескоки с равномощной сменой не дают провалов: 20 мс RMS не ниже половины медианы.
            int w = Rate / 50;
            var rms = new List<double>();
            for (int i = 2 * Rate; i + w <= x.Length; i += w)
            {
                double s = 0;
                for (int j = i; j < i + w; j++)
                    s += x[j] * x[j];
                rms.Add(Math.Sqrt(s / w));
            }
            rms.Sort();
            Assert.That(rms[0], Is.GreaterThan(0.5 * rms[rms.Count / 2]), "провал громкости на перескоке");
        }

        /// <summary>Нормированная корреляция x[t] и x[t + lag] на [from, from + length), лучшая из сдвигов ±3 сэмпла.</summary>
        private static double LagCorrelation(double[] x, int from, int length, double lag)
        {
            double best = -1;
            for (int d = -3; d <= 3; d++)
            {
                int l = (int)Math.Round(lag) + d;
                double ab = 0, aa = 0, bb = 0;
                for (int i = from; i < from + length && i + l < x.Length; i++)
                {
                    ab += x[i] * x[i + l];
                    aa += x[i] * x[i];
                    bb += x[i + l] * x[i + l];
                }
                best = Math.Max(best, ab / Math.Sqrt(Math.Max(1e-30, aa * bb)));
            }
            return best;
        }

        // ---------- Предупреждение у предела лески и приглушение катушки под разовые ----------

        [Test]
        public void LineHumStandsOutAtTheLimitUnderAHeldReel()
        {
            // Было: гул у предела на 3,7 LU тише остального и ни в одной полосе не выше катушки.
            var bank = new FightSoundBank(4.0);
            var input = new FightSoundInput { ReelInCmPerSec = 16, ReelLoad = 0.85f, LoadShare = 0.95f, RodLoadShare = 0.6f, SlackCm = 0, FishDistanceCm = 1200, WeightG = 800, Ticks = 1 };
            var director = new FightAudioDirector();
            // Общая громкость 0,01: лимитер не работает, микс линейный — гул = (с гулом) − (без гула).
            director.Update(LakeSoundState.Fight, input, 110, 15, 0, 0.01f, Rate);
            director.Update(LakeSoundState.Fight, input, 110, 15, 0, 0.01f, Rate);
            var on = director.Params;
            Assert.That(on.HumVolume, Is.GreaterThan(0f));
            var off = on;
            off.HumVolume = 0f;
            var a = RenderMono(new FightAudioMixer(bank, 7), on, 3.0, true);
            var rest = RenderMono(new FightAudioMixer(bank, 7), off, 3.0, true);
            var hum = a.Zip(rest, (x, y) => x - y).ToArray();
            var humTail = hum.Skip(Rate).ToArray();
            var restTail = rest.Skip(Rate).ToArray();
            Assert.That(BestBandSnr(humTail, restTail), Is.GreaterThanOrEqualTo(6.0), "лучшая треть октавы 315–2500 Гц, дБ");
            Assert.That(PhoneLoudness(humTail) - PhoneLoudness(restTail), Is.GreaterThanOrEqualTo(0.0), "модель динамика телефона, LU");

            // В обычной работе (доля до 0,6) гула нет.
            input.LoadShare = 0.6f;
            director.Update(LakeSoundState.Fight, input, 110, 15, 0, 0.8f, Rate);
            Assert.AreEqual(0f, director.Params.HumVolume);
        }

        [Test]
        public void OneShotsDuckTheReelAndStayUnderTheLimiter()
        {
            var bank = new FightSoundBank(4.0);

            // 1. Любой разовый клип на OneShotMax при громкости по умолчанию — лимитер не трогает (было: до −6 дБ).
            foreach (FightOneShot sound in Enum.GetValues(typeof(FightOneShot)))
            {
                var mixer = new FightAudioMixer(bank, 3);
                mixer.Publish(new FightAudioParams { OutputRate = Rate, Master = FightAudioMixer.DefaultMaster });
                mixer.Post(new FightAudioEvent { Kind = FightAudioEventKind.OneShot, Sound = sound, Variant = sound == FightOneShot.Landed ? 3 : 0, Volume = FightAudioDirector.OneShotMax });
                var buffer = new float[2 * 1024];
                for (int k = 0; k < 120; k++)
                    mixer.Render(buffer, 2);
                Assert.That(mixer.Limiter.MinGain, Is.GreaterThan(0.966f), sound + ": лимитер срезал больше 0,3 дБ");
            }

            // 2. Пока звучит разовый, шина катушки тише на CueDuck; через 0,6 с после конца — как была.
            var reel = new FightAudioParams
            {
                OutputRate = Rate, Master = 0.01f, Fight = true, C0 = 110, RotorCmPerSec = 110, ReelLoad = 0.1f, LoadShare = 0.5f, Tight = true,
                RodLoadShare = 0.3f, LayVolume = 0.36f, LayPitch = 1.1f, LoopReleaseSeconds = 0.15f,
            };
            var cue = new FightAudioEvent { Kind = FightAudioEventKind.OneShot, Sound = FightOneShot.TeleRun, Volume = FightAudioDirector.OneShotMax };
            double body = (double)bank.BodyLength(bank.OneShot(FightOneShot.TeleRun, 0, false)) / FightSoundBank.SampleRate;
            Assert.That(body, Is.InRange(FightSoundBank.MinBodySeconds, 0.62), "тело телеграфа — без тихого хвоста");
            var with = RenderMono(new FightAudioMixer(bank, 3), reel, 3.0, true, 1.0, cue);
            var without = RenderMono(new FightAudioMixer(bank, 3), reel, 3.0, true);
            var alone = RenderMono(new FightAudioMixer(bank, 3), new FightAudioParams { OutputRate = Rate, Master = 0.01f }, 3.0, true, 1.0, cue);
            var ducked = with.Zip(alone, (x, y) => x - y).ToArray();
            double Ratio(double t0, double t1)
            {
                double a = 0, b = 0;
                for (int i = (int)(t0 * Rate); i < (int)(t1 * Rate); i++)
                {
                    a += ducked[i] * ducked[i];
                    b += without[i] * without[i];
                }
                return 10 * Math.Log10(a / b);
            }
            double duckDb = 20 * Math.Log10(FightAudioMixer.CueDuck);
            double start = 1024.0 * Math.Ceiling(Rate / 1024.0) / Rate;
            Assert.That(Ratio(start + 0.05, start + body - 0.01), Is.EqualTo(duckDb).Within(1.0), "катушка под телеграфом");
            Assert.That(Ratio(start + body + 1.0, 3.0), Is.EqualTo(0.0).Within(0.3), "катушка после телеграфа");
        }

        // ---------- Кирпичики замеров ----------

        /// <summary>Канал 0 микшера за seconds секунд (буферы 1024 × 2), снимок p; событие e — на секунде eventAt.</summary>
        private static double[] RenderMono(FightAudioMixer mixer, FightAudioParams p, double seconds, bool fightStart = false, double eventAt = -1, FightAudioEvent e = default)
        {
            mixer.Publish(p);
            if (fightStart)
                mixer.Post(new FightAudioEvent { Kind = FightAudioEventKind.FightStart });
            var buffer = new float[2 * 1024];
            var x = new List<double>();
            bool posted = eventAt < 0;
            while (x.Count < seconds * Rate)
            {
                if (!posted && x.Count >= eventAt * Rate)
                {
                    mixer.Post(e);
                    posted = true;
                }
                mixer.Render(buffer, 2);
                for (int i = 0; i < buffer.Length; i += 2)
                    x.Add(buffer[i]);
            }
            return x.ToArray();
        }

        /// <summary>Лучшая треть октавы 315–2500 Гц: энергия sig над rest, дБ (Баттерворт 4-го порядка с обеих сторон).</summary>
        private static double BestBandSnr(double[] sig, double[] rest)
        {
            double best = double.NegativeInfinity;
            foreach (double fc in new[] { 315.0, 400, 500, 630, 800, 1000, 1250, 1600, 2000, 2500 })
            {
                double lo = fc / Math.Pow(2, 1 / 6.0), hi = fc * Math.Pow(2, 1 / 6.0);
                Biquad[] Band() => new[] { Biquad.HighPass(lo, 0.5412), Biquad.HighPass(lo, 1.3066), Biquad.LowPass(hi, 0.5412), Biquad.LowPass(hi, 1.3066) };
                best = Math.Max(best, 10 * Math.Log10(Energy(sig, Band()) / Math.Max(1e-30, Energy(rest, Band()))));
            }
            return best;
        }

        /// <summary>Громкость в модели динамика телефона: срез 700 Гц (2-й порядок) и K-взвешивание BS.1770, без стробирования.</summary>
        private static double PhoneLoudness(double[] x) =>
            -0.691 + 10 * Math.Log10(Math.Max(1e-30, Energy(x, new[] { Biquad.HighPass(700, 0.7071), Biquad.KShelf(), Biquad.KHighPass() }) / x.Length));

        private static double Energy(double[] x, Biquad[] chain)
        {
            double e = 0;
            foreach (double v in x)
            {
                double y = v;
                for (int k = 0; k < chain.Length; k++)
                    y = chain[k].Process(y);
                e += y * y;
            }
            return e;
        }

        /// <summary>Биквад (прямая форма I) на частоте Rate.</summary>
        private sealed class Biquad
        {
            private double _b0, _b1, _b2, _a1, _a2, _x1, _x2, _y1, _y2;

            public double Process(double x)
            {
                double y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
                _x2 = _x1;
                _x1 = x;
                _y2 = _y1;
                _y1 = y;
                return y;
            }

            private static Biquad Make(double b0, double b1, double b2, double a0, double a1, double a2) =>
                new Biquad { _b0 = b0 / a0, _b1 = b1 / a0, _b2 = b2 / a0, _a1 = a1 / a0, _a2 = a2 / a0 };

            public static Biquad LowPass(double hz, double q)
            {
                double w = 2 * Math.PI * hz / Rate, c = Math.Cos(w), alpha = Math.Sin(w) / (2 * q);
                return Make((1 - c) / 2, 1 - c, (1 - c) / 2, 1 + alpha, -2 * c, 1 - alpha);
            }

            public static Biquad HighPass(double hz, double q)
            {
                double w = 2 * Math.PI * hz / Rate, c = Math.Cos(w), alpha = Math.Sin(w) / (2 * q);
                return Make((1 + c) / 2, -(1 + c), (1 + c) / 2, 1 + alpha, -2 * c, 1 - alpha);
            }

            /// <summary>K-взвешивание BS.1770, ступень 1 (полка +4 дБ выше ~1,7 кГц).</summary>
            public static Biquad KShelf()
            {
                double f0 = 1681.974450955533, g = 3.999843853973347, q = 0.7071752369554196;
                double k = Math.Tan(Math.PI * f0 / Rate), vh = Math.Pow(10, g / 20), vb = Math.Pow(vh, 0.4996667741545416);
                return Make(vh + vb * k / q + k * k, 2 * (k * k - vh), vh - vb * k / q + k * k, 1 + k / q + k * k, 2 * (k * k - 1), 1 - k / q + k * k);
            }

            /// <summary>K-взвешивание BS.1770, ступень 2 (срез RLB 38 Гц).</summary>
            public static Biquad KHighPass()
            {
                double f0 = 38.13547087602444, q = 0.5003270373238773, k = Math.Tan(Math.PI * f0 / Rate);
                return Make(1, -2, 1, 1 + k / q + k * k, 2 * (k * k - 1), 1 - k / q + k * k);
            }
        }
    }
}
