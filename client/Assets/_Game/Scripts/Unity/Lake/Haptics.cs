using System;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace Fishing.Unity
{
    /// <summary>
    /// Вибрация телефона вместо Handheld.Vibrate (docs/specs/fight-v2.md, §6).
    /// Android API 26+ — VibrationEffect.createOneShot(ms, amplitude), старее — vibrate(ms), оба через
    /// AndroidJavaObject; редактор и iOS — ничего (плагин для iOS — вне объёма, §8).
    ///
    /// Два входа:
    /// - Device — служба вибрации, найденная один раз: для частых импульсов (вываживание, FightFeedback).
    ///   Состояние (объекты Java) живёт в экземпляре: изменяемых static-полей в проекте нет — в Unity 6.6 домен
    ///   при входе в Play Mode не перезагружается.
    /// - Pulse — разовый импульс без кэша (каждый раз заново ищет службу): для редких вызовов вне вываживания.
    /// Ни один из них не ограничивает частоту и не ведёт расписаний — это делает вызывающий (FightFeedback).
    ///
    /// Разрешение android.permission.VIBRATE: Unity дописывает его в манифест, только если в скриптах есть
    /// вызов Handheld.Vibrate. Такой вызов есть в Device.Pulse (запасной путь, когда служба вибрации
    /// не нашлась или Java-вызов упал) — он достижим, поэтому не вырезается при стрипинге. Если когда-нибудь
    /// его убрать, разрешение нужно прописать вручную в Assets/Plugins/Android/AndroidManifest.xml:
    /// &lt;uses-permission android:name="android.permission.VIBRATE" /&gt;.
    /// </summary>
    public static class Haptics
    {
        /// <summary>Наибольшая сила импульса (как в VibrationEffect).</summary>
        public const int MaxAmplitude = 255;

        /// <summary>Один импульс вибрации без кэша. В вываживании — через <see cref="Device"/>.</summary>
        /// <param name="ms">Длительность, мс.</param>
        /// <param name="amplitude">Сила 1–255 (как в VibrationEffect); там, где силы нет, — только длительность.</param>
        public static void Pulse(int ms, int amplitude)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var device = new Device())
                device.Pulse(ms, amplitude);
#endif
        }

        /// <summary>
        /// Служба вибрации телефона: Java-объекты находятся один раз в конструкторе и держатся до Dispose.
        /// Вызывать из главного потока. Любая ошибка Java ловится: после неё Device переходит на запасной
        /// Handheld.Vibrate для длинных импульсов, а короткие молча пропускает.
        /// </summary>
        public sealed class Device : IDisposable
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            /// <summary>Android 8.0: появился VibrationEffect и сила импульса.</summary>
            private const int EffectsApi = 26;

            /// <summary>VibrationEffect.DEFAULT_AMPLITUDE — для моторов без управления силой.</summary>
            private const int DefaultAmplitude = -1;

            /// <summary>Handheld.Vibrate длинный (~0,5 с) и без силы — запасной путь только для таких импульсов.</summary>
            private const int FallbackMinMs = 50;

            private AndroidJavaObject _vibrator;
            private AndroidJavaClass _effects;
            private bool _amplitudeControl;
            private bool _fallback;
#endif

            /// <summary>На телефоне есть вибромотор, и до него удалось достучаться.</summary>
            public bool IsAvailable
            {
                get
                {
#if UNITY_ANDROID && !UNITY_EDITOR
                    return _vibrator != null;
#else
                    return false;
#endif
                }
            }

            public Device()
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                try
                {
                    int sdk;
                    using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                        sdk = version.GetStatic<int>("SDK_INT");

                    // Context.VIBRATOR_SERVICE. На Android 12+ он помечен устаревшим (есть VibratorManager),
                    // но по-прежнему отдаёт вибратор по умолчанию.
                    var activity = AndroidApplication.currentActivity;
                    _vibrator = activity?.Call<AndroidJavaObject>("getSystemService", "vibrator");
                    if (_vibrator == null)
                    {
                        _fallback = true;
                        return;
                    }

                    if (!_vibrator.Call<bool>("hasVibrator"))
                    {
                        // Планшет без мотора: вибрировать нечем, запасной путь тоже ничего не даст.
                        Release();
                        return;
                    }

                    if (sdk >= EffectsApi)
                    {
                        _effects = new AndroidJavaClass("android.os.VibrationEffect");
                        _amplitudeControl = _vibrator.Call<bool>("hasAmplitudeControl");
                    }
                }
                catch (Exception e)
                {
                    Fail(e);
                }
#endif
            }

            /// <summary>Один импульс. Новый импульс обрывает текущий — так устроен Vibrator в Android.</summary>
            /// <param name="ms">Длительность, мс.</param>
            /// <param name="amplitude">Сила 1–255; без управления силой — сила мотора по умолчанию.</param>
            public void Pulse(int ms, int amplitude)
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                if (ms <= 0)
                    return;
                if (_vibrator == null)
                {
                    if (_fallback && ms >= FallbackMinMs)
                        Handheld.Vibrate();
                    return;
                }

                try
                {
                    if (_effects != null)
                    {
                        int amp = _amplitudeControl ? Mathf.Clamp(amplitude, 1, MaxAmplitude) : DefaultAmplitude;
                        using (var effect = _effects.CallStatic<AndroidJavaObject>("createOneShot", (long)ms, amp))
                            _vibrator.Call("vibrate", effect);
                    }
                    else
                    {
                        _vibrator.Call("vibrate", (long)ms);
                    }
                }
                catch (Exception e)
                {
                    Fail(e);
                }
#endif
            }

            public void Dispose()
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                Release();
#endif
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            private void Fail(Exception e)
            {
                Debug.LogWarning("Haptics: вибрация через Vibrator недоступна, дальше только Handheld.Vibrate — " + e.Message);
                Release();
                _fallback = true;
            }

            private void Release()
            {
                _effects?.Dispose();
                _effects = null;
                _vibrator?.Dispose();
                _vibrator = null;
            }
#endif
        }
    }
}
