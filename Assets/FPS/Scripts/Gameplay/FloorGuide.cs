using System;
using UnityEngine;

namespace Unity.FPS.Gameplay
{
    /// <summary>
    /// Glowing chevrons on the floor that lead the player to their next goal (CoreBuster/Floor Guide). Each route is a
    /// chain of arrows in its keycard's colour, lit only while it is the current objective: from the tutorial exit to
    /// the Workshop, from the Workshop to the Centre Room once the blue card is in hand, on to the North-West Room
    /// with the yellow card, to the Master terminal with the red card, and into the Boss Hall once the vault opens.
    /// A route fades in when its show condition is met and out again once its goal is reached.
    /// Built by Tools/CoreBuster/Build Floor Guide.
    /// </summary>
    public class FloorGuide : MonoBehaviour
    {
        [Serializable]
        public class Route
        {
            public string Name;
            public Renderer Arrows;

            [Header("Shown when (any)")]
            [Tooltip("This door has opened.")]
            public SecurityDoor ShowWhenOpened;
            [Tooltip("The player holds this keycard.")]
            public KeycardData ShowWhenHolding;
            [Tooltip("The player chose to fight the core and the vault is opening.")]
            public bool ShowDuringFight;

            [Header("Hidden once (any)")]
            public SecurityDoor[] HideWhenOpened;
            public bool HideWhenMasterUnlocked;
            public bool HideWhenArenaStarted;

            [NonSerialized] public float Fade;
        }

        public Route[] Routes;
        public MasterTerminal Master;
        public FinaleDirector Director;
        [Min(.1f)] public float FadeSpeed = 1.5f;

        static readonly int k_Fade = Shader.PropertyToID("_Fade");

        KeycardInventory m_Inventory;
        MaterialPropertyBlock m_Block;

        void Start()
        {
            m_Block = new MaterialPropertyBlock();
            var player = FindAnyObjectByType<PlayerCharacterController>();
            if (player) m_Inventory = player.GetComponentInChildren<KeycardInventory>();
            if (!m_Inventory) m_Inventory = FindAnyObjectByType<KeycardInventory>();
            foreach (var route in Routes)
            {
                route.Fade = 0f;
                Apply(route);
            }
        }

        void Update()
        {
            foreach (var route in Routes)
            {
                float target = IsCurrent(route) ? 1f : 0f;
                if (Mathf.Approximately(route.Fade, target)) continue;
                route.Fade = Mathf.MoveTowards(route.Fade, target, Time.deltaTime * FadeSpeed);
                Apply(route);
            }
        }

        bool IsCurrent(Route route)
        {
            bool shown = (route.ShowWhenOpened && route.ShowWhenOpened.HasBeenOpened) ||
                         (route.ShowWhenHolding && m_Inventory && m_Inventory.Has(route.ShowWhenHolding)) ||
                         (route.ShowDuringFight && Director && Director.Current == FinaleDirector.Phase.Fight);
            if (!shown) return false;
            if (route.HideWhenOpened != null)
                foreach (var door in route.HideWhenOpened)
                    if (door && door.HasBeenOpened) return false;
            if (route.HideWhenMasterUnlocked && Master && Master.IsUnlocked) return false;
            if (route.HideWhenArenaStarted && Director && Director.ArenaStarted) return false;
            return true;
        }

        void Apply(Route route)
        {
            if (!route.Arrows) return;
            route.Arrows.enabled = route.Fade > .001f;
            route.Arrows.GetPropertyBlock(m_Block);
            m_Block.SetFloat(k_Fade, route.Fade);
            route.Arrows.SetPropertyBlock(m_Block);
        }
    }
}
