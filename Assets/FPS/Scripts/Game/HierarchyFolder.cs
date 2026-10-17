using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.FPS.Game
{
    /// <summary>
    /// Marks an empty GameObject at the world origin whose only job is to group level pieces in the hierarchy (by room,
    /// then by type; see Tools/CoreBuster/Organize Main Scene Hierarchy). Tools that identify a kit piece by its
    /// top-level object look through folders with <see cref="PieceRoot"/> and <see cref="TopLevel"/> instead of
    /// <c>Transform.root</c> and <c>Scene.GetRootGameObjects</c>.
    /// </summary>
    [DisallowMultipleComponent]
    public class HierarchyFolder : MonoBehaviour
    {
        /// <summary>The piece <paramref name="t"/> belongs to: its topmost ancestor that is not inside a folder.</summary>
        public static Transform PieceRoot(Transform t)
        {
            if (!t) return null;
            while (t.parent && !t.parent.GetComponent<HierarchyFolder>()) t = t.parent;
            return t;
        }

        /// <summary>The scene's root objects, with every folder replaced by the pieces filed in it.</summary>
        public static IEnumerable<GameObject> TopLevel(Scene scene)
        {
            var found = new List<GameObject>();
            foreach (var root in scene.GetRootGameObjects()) Collect(root, found);
            return found;
        }

        static void Collect(GameObject go, List<GameObject> found)
        {
            if (!go.GetComponent<HierarchyFolder>())
            {
                found.Add(go);
                return;
            }
            foreach (Transform child in go.transform) Collect(child.gameObject, found);
        }
    }
}
