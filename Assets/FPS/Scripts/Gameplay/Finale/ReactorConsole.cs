using System.Collections;
using Unity.FPS.Game;
using UnityEngine;

namespace Unity.FPS.Gameplay
{
    /// <summary>
    /// One of the three control consoles ringed around the Boss Hall reactor. With a fully charged Disassembler
    /// (three cores), the player slots their gun into the console's port and injects the cores
    /// (<see cref="CoreExecutioner.TryInject"/>, the execution played in reverse). The console then overloads:
    /// it arcs with electricity for <see cref="OverloadDuration"/> seconds and hurts anyone standing at it, while
    /// the surge runs down its cable into the reactor, overcharges the beam and blows a layer of armour off the core
    /// (<see cref="FinaleDirector.ConsoleOverloaded"/>). Each console works once.
    /// </summary>
    public class ReactorConsole : KeycardInteractable
    {
        public enum State { Waiting, Injecting, Overloading, Spent }

        const string ElectrocutionResource = "CoreBuster/VFX_Electrocution";

        public FinaleDirector Director;
        [Tooltip("The gun port; its forward points out of the console toward the player.")]
        public Transform Slot;
        public PowerCable Cable;
        [Tooltip("Used for the cores run down the gun into the port.")]
        public Material CoreOrbMaterial;

        [Header("Status")]
        public Renderer Body;
        public Renderer[] Lamps;
        public Light StatusLight;
        [Tooltip("Points the overload's arcs jump between and down to the floor from.")]
        public Transform[] ArcAnchors;
        public Color IdleColor = new Color(1f, .55f, .1f);
        public Color ReadyColor = new Color(.2f, 1f, .45f);
        public Color ChargingColor = new Color(.35f, .7f, 1f);
        public Color OverloadColor = new Color(.85f, .55f, 1f);
        public Color SpentColor = new Color(.6f, .08f, .05f);

        [Header("Overload")]
        [Min(.5f)] public float OverloadDuration = 5f;
        [Tooltip("Seconds of warning before standing at the console starts to hurt.")]
        [Min(0f)] public float WarningTime = .8f;
        [Min(.5f)] public float OverloadRadius = 3.2f;
        [Min(0f)] public float OverloadDamagePerSecond = 22f;

        [Header("Sound")]
        public AudioClip CoreInjectedSfx;
        public AudioClip OverloadSfx;
        public AudioClip SpentSfx;

        public State Current { get; private set; }
        public int CoresInjected { get; private set; }

        static readonly int k_Emission = Shader.PropertyToID("_EmissionColor");
        static readonly int k_Base = Shader.PropertyToID("_BaseColor");

        Disassembler m_Disassembler;
        CoreExecutioner m_Executioner;
        Health m_PlayerHealth;
        Transform m_Player;
        ElectrocutionVfx m_Vfx;
        MaterialPropertyBlock m_Block;
        float m_OverloadStart;

        public override bool IsAvailable =>
            Current == State.Waiting && Director && Director.ConsolesOnline && !Director.ConsoleBusy;

        public override string PromptAction(KeycardInteractor user) =>
            Charged(user) ? "INJECT CORES" : "REACTOR CONSOLE";

        public override string PromptDetail(KeycardInteractor user)
        {
            if (Charged(user)) return "OVERCHARGE THE REACTOR - THEN GET CLEAR";
            var disassembler = DisassemblerOf(user);
            int have = disassembler ? disassembler.Charge : 0, need = disassembler ? disassembler.ExecutionsForFullCharge : 3;
            return $"NEEDS A FULL DISASSEMBLER ({have}/{need} CORES) - EXECUTE ENEMIES";
        }

        void Awake()
        {
            if (!FocusPoint) FocusPoint = Slot;
            m_Block = new MaterialPropertyBlock();
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
            if (!started) return;
            Current = State.Injecting;
            Director.ConsoleStarted(this);
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
            m_OverloadStart = Time.time;
            ShowMessage("CORE OVERLOAD DETECTED - CLEAR THE CONSOLE");
            PlayAt(OverloadSfx, transform.position);
            var prefab = Resources.Load<ElectrocutionVfx>(ElectrocutionResource);
            if (prefab)
            {
                m_Vfx = Instantiate(prefab, transform.position, Quaternion.identity, transform);
                m_Vfx.Play(transform, ArcAnchors, Body);
            }
            Director.ConsoleOverloaded(this);

            while (Time.time - m_OverloadStart < OverloadDuration)
            {
                if (Time.time - m_OverloadStart >= WarningTime) HurtNearby();
                yield return null;
            }
            if (m_Vfx) m_Vfx.Stop();
            m_Vfx = null;
            Current = State.Spent;
            PlayAt(SpentSfx, transform.position);
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
            // An injection cut short (the player died mid-way) gives the console back.
            if (Current == State.Injecting && m_Executioner && !m_Executioner.IsExecuting)
            {
                Current = State.Waiting;
                Director.ConsoleAbandoned(this);
            }
            ShowStatus();
        }

        void ShowStatus()
        {
            bool charged = m_Disassembler && m_Disassembler.IsCharged;
            bool online = Director && Director.ConsolesOnline;
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
                    color = online && charged ? ReadyColor : IdleColor;
                    glow = online && charged ? 3f * StatusLamps.Pulse(4f) + 1f : online ? 1.4f : .5f;
                    break;
            }
            StatusLamps.Set(Lamps, StatusLight, color, glow, 2.2f);
            if (Cable) Cable.SetCharge(Current == State.Waiting && online && charged ? 1f : Current == State.Injecting ? 1f : 0f);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(.8f, .5f, 1f, .4f);
            Gizmos.DrawWireSphere(transform.position, OverloadRadius);
        }
    }
}
