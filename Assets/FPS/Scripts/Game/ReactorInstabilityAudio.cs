using UnityEngine;

namespace Unity.FPS.Game
{
    /// <summary>Matches the Centre Room reactor's spatial hum to its existing visual surge.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public sealed class ReactorInstabilityAudio : MonoBehaviour
    {
        [SerializeField] Material field;
        [SerializeField, Range(0f, 1f)] float baseVolume = .7f;

        AudioSource source;

        public Material Field => field;

        public void Configure(Material containmentField, float volume)
        {
            field = containmentField;
            baseVolume = Mathf.Clamp01(volume);
        }

        void Awake() => source = GetComponent<AudioSource>();

        void Update()
        {
            if (Time.deltaTime <= 0f) return;
            float surge = ReactorInstabilityLight.Surge(Time.time, ReactorInstabilityLight.InstabilityOf(field));
            source.volume = baseVolume * Mathf.Clamp(.72f + .28f * (surge - 1f), .48f, 1f);
            source.pitch = Mathf.Clamp(1f + .035f * (surge - 1f), .96f, 1.05f);
        }
    }
}
