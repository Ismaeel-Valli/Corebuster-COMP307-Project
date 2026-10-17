using System.Collections.Generic;
using Unity.FPS.Game;
using UnityEngine;

namespace Unity.FPS.Gameplay
{
    /// <summary>
    /// Something in the keycard loop the player can use with Interact: a room terminal, an access machine or
    /// the Master terminal. The player's KeycardInteractor picks the one in reach and on the crosshair.
    /// </summary>
    public abstract class KeycardInteractable : MonoBehaviour
    {
        static readonly List<KeycardInteractable> s_All = new List<KeycardInteractable>();
        public static IReadOnlyList<KeycardInteractable> All => s_All;

        [Tooltip("Where the player looks to use it; defaults to this transform.")]
        public Transform FocusPoint;
        [Tooltip("Furthest the focus point can be from the camera.")]
        public float Range = 2.6f;

        [Header("Sound")]
        public AudioClip DeniedSfx;
        [Range(0f, 1f)] public float SfxVolume = 1f;

        public Vector3 FocusPosition => FocusPoint ? FocusPoint.position : transform.position;

        /// <summary>Whether it currently offers anything at all (no prompt when false).</summary>
        public virtual bool IsAvailable => true;

        /// <summary>Seconds Interact must be held to use it; 0 uses it on the press.</summary>
        public virtual float HoldDuration => 0f;

        /// <summary>The prompt's main line, after the key: "ACCESS TERMINAL".</summary>
        public abstract string PromptAction(KeycardInteractor user);

        /// <summary>The prompt's second line.</summary>
        public virtual string PromptDetail(KeycardInteractor user) => string.Empty;

        public abstract void Interact(KeycardInteractor user);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_All.Clear();

        protected virtual void OnEnable() => s_All.Add(this);
        protected virtual void OnDisable() => s_All.Remove(this);

        protected void PlayAt(AudioClip clip, Vector3 position, float volume = 1f)
        {
            if (!clip) return;
            // Not AudioUtility.CreateSFX: that needs the scene's AudioManager, which test scenes lack.
            var go = new GameObject("SFX " + clip.name);
            go.transform.position = position;
            var source = go.AddComponent<AudioSource>();
            source.clip = clip;
            source.volume = SfxVolume * volume;
            source.spatialBlend = .85f;
            source.minDistance = 3f;
            source.maxDistance = 40f;
            source.rolloffMode = AudioRolloffMode.Linear;
            if (FindAnyObjectByType<AudioManager>())
                source.outputAudioMixerGroup = AudioUtility.GetAudioGroup(AudioUtility.AudioGroups.HUDObjective);
            source.Play();
            Destroy(go, clip.length + .1f);
        }

        protected static void ShowMessage(string message, float delay = 0f)
        {
            DisplayMessageEvent evt = Events.DisplayMessageEvent;
            evt.Message = message;
            evt.DelayBeforeDisplay = delay;
            EventManager.Broadcast(evt);
        }
    }
}
