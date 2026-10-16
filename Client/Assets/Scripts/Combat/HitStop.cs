using UnityEngine;

namespace Game.Client.Combat
{
    /// <summary>
    /// "Hit stop": congela o jogo por alguns milissegundos no impacto, para o golpe ter peso.
    /// Uma pausa nova só estende a atual se for mais longa.
    /// </summary>
    public sealed class HitStop : MonoBehaviour
    {
        private const float FrozenTimeScale = 0.02f;

        private static HitStop _instance;
        private float _resumeAt;

        /// <summary>Pausa por <paramref name="seconds"/> segundos reais.</summary>
        public static void Trigger(float seconds)
        {
            if (seconds <= 0) return;
            if (_instance == null)
            {
                var host = new GameObject("HitStop") { hideFlags = HideFlags.HideAndDontSave };
                _instance = host.AddComponent<HitStop>();
            }
            _instance._resumeAt = Mathf.Max(_instance._resumeAt, Time.unscaledTime + seconds);
            Time.timeScale = FrozenTimeScale;
        }

        private void Update()
        {
            if (Time.timeScale < 1f && Time.unscaledTime >= _resumeAt) Time.timeScale = 1f;
        }

        private void OnDestroy()
        {
            Time.timeScale = 1f;
            if (_instance == this) _instance = null;
        }
    }
}
