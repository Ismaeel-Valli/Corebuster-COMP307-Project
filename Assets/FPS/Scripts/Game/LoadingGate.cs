using System.Collections.Generic;
using UnityEngine;

namespace Unity.FPS.Game
{
    /// <summary>
    /// Lets systems that prepare a freshly loaded scene (zone streaming, the ray-tracing acceleration structure)
    /// keep the loading screen up until they are ready. Each holder begins with a key and ends with the same key;
    /// the loading screen waits until no key is held, or until its own time limit runs out.
    /// </summary>
    public static class LoadingGate
    {
        static readonly HashSet<object> s_Holders = new HashSet<object>();

        public static bool IsBusy => s_Holders.Count > 0;

        public static void Begin(object key)
        {
            if (key != null) s_Holders.Add(key);
        }

        public static void End(object key)
        {
            if (key != null) s_Holders.Remove(key);
        }

        // The project enters play mode without a domain reload, so statics outlive each session.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_Holders.Clear();
    }
}
