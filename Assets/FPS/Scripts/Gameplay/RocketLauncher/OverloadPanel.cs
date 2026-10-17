using System.Collections;
using Unity.FPS.Game;
using UnityEngine;

namespace Unity.FPS.Gameplay
{
    /// <summary>
    /// The power panel of a <see cref="WeaponCradle"/>'s containment beams, overloaded exactly as the reactor consoles
    /// are: with a fully charged Disassembler the player slots their gun into the panel's port and injects the cores
    /// (<see cref="CoreExecutioner.TryInject"/>). The panel then arcs with electricity for OverloadDuration seconds,
    /// hurting anyone standing at it, while the beams surge; when it is spent the beams cut out and release the
    /// weapon. It works once.
    /// </summary>
    public class OverloadPanel : KeycardInteractable
    {
        public enum State { Waiting, Injecting, Overloading, Spent }

        const string ElectrocutionResource = "CoreBuster/VFX_Electrocution";

        public WeaponCradle Cradle;
        [Tooltip("The gun port; its forward points out of the panel toward the player.")]
        public Transform Slot;
        [Tooltip("Used for the cores run down the gun into the port.")]
        public Material CoreOrbMaterial;

        [Header("Status")]
        public Renderer Body;
        public Renderer[] Lamps;
        [Tooltip("Points the overload's arcs jump between and down to the floor from.")]
        public Transform[] ArcAnchors;
        public Color IdleColor = new Color(1f, .55f, .1f);
        public Color ReadyColor = new Color(.2f, 1f, .45f);
        public Color ChargingColor = new Color(.35f, .7f, 1f);
        public Color OverloadColor = new Color(.85f, .55f, 1f);
        public Color SpentColor = new Color(.6f, .08f, .05f);

        [Header("Overload")]
        [Min(.5f)] public float OverloadDuration = 3.5f;
        [Tooltip("Seconds of warning before standing at the panel starts to hurt.")]
        [Min(0f)] public float WarningTime = .8f;
        [Min(.5f)] public float OverloadRadius = 2.6f;
        [Min(0f)] public float OverloadDamagePerSecond = 18f;

        [Header("Sound")]
        public AudioClip CoreInjectedSfx;
        public AudioClip OverloadSfx;
        public AudioClip SpentSfx;

        public State Current { get; private set; }
        public int CoresInjected { get; private set; }

        Disassembler m_Disassembler;
        CoreExecutioner m_Executioner;
        Health m_PlayerHealth;
        Transform m_Player;
        ElectrocutionVfx m_Vfx;

        public override bool IsAvailable => Current == State.Waiting;

        public override string PromptAction(KeycardInteractor user) => Charged(user) ? "INJECT CORES" : "CONTAINMENT POWER PANEL";

        public override string PromptDetail(KeycardInteractor user)
        {
            if (Charged(user)) return "OVERLOAD THE BEAMS TO RELEASE THE ROCKET LAUNCHER - THEN GET CLEAR";
            var disassembler = DisassemblerOf(user);
            int have = disassembler ? disassembler.Charge : 0, need = disassembler ? disassembler.ExecutionsForFullCharge : 3;
            return $"NEEDS A FULL DISASSEMBLER ({have}/{need} CORES) - EXECUTE ENEMIES";
        }

        void Awake()
        {
            if (!FocusPoint) FocusPoint = Slot;
        }

        void Start()
        {
            var player = FindAnyObjectByType<PlayerCharacterController>();
            if (!player) return;
            m_Player = player.transform;
            m_PlayerHealth = player.GetComponent<Health>();
            m_Disassembler = player.GetComponent<Disassembler>();
            m_Executioner = player.GetComponent<CoreExecutioner>();
        }

        Disassembler DisassemblerOf(KeycardInteractor user) => user ? user.GetComponent<Disassembler>() : m_Disassembler;

        bool Charged(KeycardInteractor user)
        {
            var disassembler = DisassemblerOf(user);
            return disassembler && disassembler.IsCharged;
        }

        public override void Interact(KeycardInteractor user)
        {
            var disassembler = DisassemblerOf(user);
            var executioner = user ? user.GetComponent<CoreExecutioner>() : m_Executioner;
            if (!disassembler || !disassembler.IsCharged)
            {
                PlayAt(DeniedSfx, FocusPosition);
                ShowMessage("DISASSEMBLER NOT FULLY CHARGED - EXECUTE ENEMIES FOR CORES");
                return;
            }
            if (!executioner) return;
            m_Disassembler = disassembler;
            m_Executioner = executioner;
            CoresInjected = 0;
            bool started = executioner.TryInject(Slot, disassembler.Charge, CoreOrbMaterial, OnCoreInjected,
                () => StartCoroutine(Overload()));
            if (started) Current = State.Injecting;
        }

        void OnCoreInjected(int index)
        {
            if (m_Disassembler) m_Disassembler.SpendCharge(1);
            CoresInjected = index + 1;
            PlayAt(CoreInjectedSfx, FocusPosition, .8f);
        }

        IEnumerator Overload()
        {
            Current = State.Overloading;
            float start = Time.time;
            ShowMessage("CONTAINMENT OVERLOAD - CLEAR THE PANEL");
            PlayAt(OverloadSfx, transform.position);
            var prefab = Resources.Load<ElectrocutionVfx>(ElectrocutionResource);
            if (prefab)
            {
                m_Vfx = Instantiate(prefab, transform.position, Quaternion.identity, transform);
                m_Vfx.Play(transform, ArcAnchors, Body);
            }
            if (Cradle) Cradle.Surge();

            while (Time.time - start < OverloadDuration)
            {
                if (Time.time - start >= WarningTime) HurtNearby();
                yield return null;
            }
            if (m_Vfx) m_Vfx.Stop();
            m_Vfx = null;
            Current = State.Spent;
            PlayAt(SpentSfx, transform.position);
            if (Cradle) Cradle.Release();
        }

        void HurtNearby()
        {
            if (!m_Player || !m_PlayerHealth || m_PlayerHealth.CurrentHealth <= 0f) return;
            Vector3 offset = m_Player.position - transform.position;
            offset.y = 0f;
            if (offset.magnitude > OverloadRadius) return;
            m_PlayerHealth.TakeDamage(OverloadDamagePerSecond * Time.deltaTime, gameObject);
        }

        void Update()
        {
            // An injection cut short (the player died mid-way) gives the panel back.
            if (Current == State.Injecting && m_Executioner && !m_Executioner.IsExecuting) Current = State.Waiting;

            bool charged = m_Disassembler && m_Disassembler.IsCharged;
            Color color;
            float glow;
            switch (Current)
            {
                case State.Injecting:
                    color = ChargingColor;
                    glow = 3f + 3f * CoresInjected + StatusLamps.Pulse(14f) * 2f;
                    break;
                case State.Overloading:
                    color = Color.Lerp(OverloadColor, Color.white, Random.value * .5f);
                    glow = 6f + Random.value * 10f;
                    break;
                case State.Spent:
                    color = SpentColor;
                    glow = .6f;
                    break;
                default:
                    color = charged ? ReadyColor : IdleColor;
                    glow = charged ? 3f * StatusLamps.Pulse(4f) + 1f : 1.4f;
                    break;
            }
            StatusLamps.Set(Lamps, null, color, glow);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(.8f, .5f, 1f, .4f);
            Gizmos.DrawWireSphere(transform.position, OverloadRadius);
        }
    }
}
