using System.Threading;
using Fishing.Core.Audio;
using UnityEngine;

namespace Fishing.Unity
{
    /// <summary>
    /// Выход звука озера: стоит на одном GameObject с AudioSource (тихая петля только держит источник «играющим»)
    /// и в OnAudioFilterRead — на звуковом потоке Unity — отдаёт буфер FightAudioMixer. Здесь нет ни вызовов Unity API,
    /// ни блокировок, ни выделений памяти: микшер подключается ссылкой (Volatile), всё остальное он берёт сам из
    /// снимка и очереди событий. Создаёт и подключает FightFeedback. На WebGL OnAudioFilterRead нет — там тишина.
    /// </summary>
    public sealed class FightAudioOutput : MonoBehaviour
    {
        private FightAudioMixer _mixer;

        /// <summary>Главный поток: подключить микшер (null — отключить, выход молчит).</summary>
        public void Attach(FightAudioMixer mixer) => Volatile.Write(ref _mixer, mixer);

        private void OnAudioFilterRead(float[] data, int channels)
        {
            var mixer = Volatile.Read(ref _mixer);
            if (mixer != null)
                mixer.Render(data, channels);
        }
    }
}
