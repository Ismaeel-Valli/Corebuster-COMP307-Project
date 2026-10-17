using UnityEngine;

namespace Unity.FPS.Game
{
    /// <summary>
    /// Something the Disassembler's bolts can electrocute. The AI assembly supplies the enemy implementation
    /// (DisassemblerShock) and registers Factory so a shot can add it to any enemy, without the gameplay code
    /// that fires the bolts depending on the AI code.
    /// </summary>
    public abstract class Electrocutable : MonoBehaviour
    {
        /// <summary>Adds the enemy implementation to an enemy that doesn't have one yet.</summary>
        public static System.Func<GameObject, Electrocutable> Factory;

        public abstract bool IsElectrocuted { get; }

        /// <summary>Freezes and electrocutes for the duration (refreshing it if already shocked). hitPoint is where the bolt struck.</summary>
        public abstract bool Electrocute(float duration, Vector3 hitPoint, GameObject source);

        /// <summary>The electrocutable enemy a collider belongs to, adding the component if needed. Null for anything else.</summary>
        public static Electrocutable Find(Collider collider)
        {
            if (!collider) return null;
            var existing = collider.GetComponentInParent<Electrocutable>();
            if (existing) return existing;
            var actor = collider.GetComponentInParent<Actor>();
            if (!actor || Factory == null) return null;
            return Factory(actor.gameObject);
        }
    }
}
