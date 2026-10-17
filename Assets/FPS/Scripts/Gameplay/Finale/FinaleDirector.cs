using System;
using System.Collections;
using System.Collections.Generic;
using Unity.FPS.Game;
using UnityEngine;

namespace Unity.FPS.Gameplay
{
    /// <summary>
    /// The game's two endings. Once all three keycards are in the Master terminal the player chooses:
    /// <list type="bullet">
    /// <item><b>Escape</b> - the North-West Room locks down (the Boss Hall stays shut) and Elites warp in for
    /// SurviveDuration, counted down on screen. Surviving launches the escape pod: a white flash, then the win
    /// screen. Dying there plays the player's own core expanding and bursting (the overlay), then the lose
    /// screen.</item>
    /// <item><b>Fight</b> - the player's guns fire faster and run hotter for longer, the Boss Hall opens, and once
    /// inside, endless enemies warp in, faster the longer it takes. The reactor under the giant core feeds it a beam
    /// through the opening in its armour. Executing three enemies fills the Disassembler; injecting those cores into
    /// one of the three reactor consoles overloads it, the surge runs down its cable into the reactor, the beam
    /// overcharges and blows one armour layer off the core (one console per layer). With the heart exposed the
    /// player busts it from the platform: overload, collapse, burst, a slow white flash, then the win screen.</item>
    /// </list>
    /// While a finale is in the scene, completing objectives no longer ends the game. Visuals that need UI (the
    /// choice, the meltdown, the white flash) are raised as events for the overlay in fps.UI.
    /// </summary>
    public class FinaleDirector : MonoBehaviour
    {
        public enum Choice { Escape, Fight }
        public enum Phase { Waiting, Choosing, Escape, Fight, Ending }

        [Header("Progression")]
        public MasterTerminal Master;
        [Tooltip("Shut for the escape lockdown: the North-West Room door and the Boss Hall door.")]
        public SecurityDoor[] EscapeDoors;

        [Header("Escape")]
        public FinaleSpawner EscapeSpawner;
        public FinaleObjective EscapeObjective;
        [Min(1f)] public float SurviveDuration = 120f;
        [Tooltip("The lose screen's line when the player dies during the lockdown.")]
        [TextArea] public string EscapeEndingLine = "You expended your own core trying to save yourself.";
        [Tooltip("The win screen's line when the player survives the lockdown.")]
        [TextArea] public string EscapeWinLine =
            "You held out until the escape pod launched. The ship is lost, but you made it off alive.";

        [Header("Fight")]
        public FinaleSpawner ArenaSpawner;
        public FinaleObjective CoreObjective;
        public GiantCore Core;
        [Tooltip("The Boss Hall's interior: the fight starts when the player steps inside.")]
        public Bounds ArenaBounds;
        [Tooltip("Multiplies how many shots per second the player's guns fire.")]
        [Min(1f)] public float FireRateMultiplier = 1.45f;
        [Tooltip("Multiplies how long the player's guns can fire before they overheat.")]
        [Min(1f)] public float HeatCapacityMultiplier = 2f;
        [Tooltip("Multiplies how fast the player's guns cool down.")]
        [Min(1f)] public float CoolingRateMultiplier = 1.5f;
        [TextArea] public string WinEndingLine =
            "You saved the ship, at the cost of your own core. Your sacrifice will be remembered.";

        [Header("Reactor")]
        [Tooltip("The consoles around the reactor; each overcharge blows one armour layer off the core.")]
        public ReactorConsole[] Consoles;
        public ReactorBeam Beam;
        [Tooltip("How long the overcharged beam builds, holds and settles; the layer goes as it peaks.")]
        public Vector3 OverchargeTiming = new Vector3(.45f, 2.2f, 1.2f);
        [Tooltip("Seconds into the beam's peak at which the armour layer is blown off.")]
        [Min(0f)] public float BlowOffDelay = .5f;

        [Header("Final Bust")]
        [Min(.1f)] public float TurnDuration = .6f;
        [Min(.1f)] public float OverloadDuration = 2f;
        [Min(.1f)] public float CollapseDuration = .5f;
        [Min(.1f)] public float BurstDuration = 1.6f;
        [Tooltip("The white flash that brings up the win screen; it starts as the core bursts.")]
        [Min(.1f)] public float WinFlashDuration = 3f;
        [Min(1f)] public float BurstScale = 14f;
        [Min(0f)] public float CameraShake = .08f;

        [Header("Sound")]
        public AudioClip ChoiceSfx;
        public AudioClip AlarmSfx;
        public AudioClip OverloadSfx;
        public AudioClip BurstSfx;
        [Range(0f, 1f)] public float SfxVolume = 1f;

        public Phase Current { get; private set; }
        public float SurviveRemaining { get; private set; }
        /// <summary>The consoles can take cores: the core fight is on and armour remains.</summary>
        public bool ConsolesOnline => Current == Phase.Fight && m_ArenaStarted && Core && !Core.IsExposed && !m_EndingStarted;
        /// <summary>A console is being injected or its surge is still on its way to the core.</summary>
        public bool ConsoleBusy => m_ActiveConsole;
        public int ConsolesUsed { get; private set; }
        /// <summary>The player has entered the Boss Hall and the core fight is underway.</summary>
        public bool ArenaStarted => m_ArenaStarted;
        public bool CanBustCore => Current == Phase.Fight && Core && Core.IsExposed && !m_EndingStarted &&
                                   m_Character && !m_Character.IsDead;

        /// <summary>The overlay shows the choice and answers with Choose.</summary>
        public event Action ChoiceRequested;
        /// <summary>The overlay plays the player's core rising, expanding and bursting into white, then calls back.</summary>
        public event Action<Action> MeltdownRequested;
        /// <summary>The overlay fades the screen to white over the given seconds, then calls back.</summary>
        public event Action<float, Action> WhiteFlashRequested;

        GameFlowManager m_Flow;
        PlayerCharacterController m_Character;
        PlayerWeaponsManager m_Weapons;
        Health m_Health;
        readonly List<Behaviour> m_Locked = new List<Behaviour>();
        readonly HashSet<WeaponController> m_Buffed = new HashSet<WeaponController>();
        bool m_WasInvincible;
        bool m_EndingStarted;
        bool m_ArenaStarted;
        int m_ShownSeconds = -1;
        ReactorConsole m_ActiveConsole;
        float m_NextCoreReadout;

        void Awake()
        {
            m_Flow = FindAnyObjectByType<GameFlowManager>();
            if (m_Flow) m_Flow.ObjectivesEndGame = false;
            if (Master)
            {
                Master.DeferDoors = true;
                Master.OnUnlocked += OnCardsAccepted;
            }
        }

        void Start()
        {
            m_Character = FindAnyObjectByType<PlayerCharacterController>();
            if (!m_Character) return;
            m_Weapons = m_Character.GetComponent<PlayerWeaponsManager>();
            m_Health = m_Character.GetComponent<Health>();
        }

        void OnDestroy()
        {
            if (Master) Master.OnUnlocked -= OnCardsAccepted;
            if (m_Weapons) m_Weapons.OnAddedWeapon -= OnWeaponAdded;
            if (m_Health) m_Health.OnDie -= OnEscapeDeath;
            if (Core)
            {
                Core.OnLayerBroken -= OnLayerBroken;
                Core.OnExposed -= OnCoreExposed;
            }
        }

        // ------------------------------------------------------------------ choice

        void OnCardsAccepted()
        {
            if (Current == Phase.Waiting) StartCoroutine(OfferChoice());
        }

        IEnumerator OfferChoice()
        {
            Current = Phase.Choosing;
            // Let the card-insertion sequence hand the player back before taking control away again.
            var interactor = m_Character ? m_Character.GetComponent<KeycardInteractor>() : null;
            while (interactor && interactor.IsBusy) yield return null;

            LockPlayer();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            PlayAt(ChoiceSfx, m_Character ? m_Character.transform.position : transform.position);
            if (ChoiceRequested != null) ChoiceRequested.Invoke();
            else
            {
                Debug.LogWarning("FinaleDirector: no overlay is listening for the choice; defaulting to Fight.", this);
                Choose(Choice.Fight);
            }
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>Testing: skips the keycards and the choice and starts the core fight (or the escape).</summary>
        public void BeginForTesting(Choice choice)
        {
            if (Current != Phase.Waiting) return;
            Current = Phase.Choosing;
            Choose(choice);
        }
#endif

        public void Choose(Choice choice)
        {
            if (Current != Phase.Choosing) return;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            UnlockPlayer();
            if (choice == Choice.Escape) StartEscape();
            else StartFight();
        }

        // ------------------------------------------------------------------ escape

        void StartEscape()
        {
            Current = Phase.Escape;
            foreach (var door in EscapeDoors)
                if (door) door.Lock(true);
            if (m_Flow) m_Flow.DeathHandledExternally = true;
            if (m_Health) m_Health.OnDie += OnEscapeDeath;
            PlayAt(AlarmSfx, m_Character ? m_Character.transform.position : transform.position);
            ShowMessage("LOCKDOWN - SURVIVE UNTIL THE ESCAPE POD IS PRIMED");
            if (EscapeObjective) EscapeObjective.gameObject.SetActive(true);
            if (EscapeSpawner) EscapeSpawner.Begin();
            StartCoroutine(EscapeCountdown());
        }

        IEnumerator EscapeCountdown()
        {
            SurviveRemaining = SurviveDuration;
            while (SurviveRemaining > 0f && !m_EndingStarted)
            {
                SurviveRemaining = Mathf.Max(0f, SurviveRemaining - Time.deltaTime);
                int seconds = Mathf.CeilToInt(SurviveRemaining);
                if (seconds != m_ShownSeconds && EscapeObjective)
                {
                    m_ShownSeconds = seconds;
                    EscapeObjective.Show(EscapeObjective.Description, $"{seconds / 60}:{seconds % 60:00}");
                }
                yield return null;
            }
            if (m_EndingStarted) yield break;
            if (EscapeObjective) EscapeObjective.Complete("Escape pod primed");
            StartCoroutine(EscapeLaunch());
        }

        void OnEscapeDeath()
        {
            if (Current == Phase.Escape) StartCoroutine(Meltdown());
        }

        // Survived: the pod launches and the white flash carries the player to the win screen.
        IEnumerator EscapeLaunch()
        {
            if (m_EndingStarted) yield break;
            m_EndingStarted = true;
            Current = Phase.Ending;
            if (EscapeSpawner) EscapeSpawner.Stop();
            LockPlayer();
            ShowMessage("ESCAPE POD LAUNCHING");
            PlayAt(BurstSfx, m_Character ? m_Character.transform.position : transform.position);

            bool flashed = false;
            bool hasOverlay = WhiteFlashRequested != null;
            WhiteFlashRequested?.Invoke(WinFlashDuration, () => flashed = true);
            float start = Time.time;
            while (hasOverlay ? !flashed : Time.time - start < WinFlashDuration) yield return null;
            if (m_Flow) m_Flow.EndGameScripted(true, EscapeWinLine, true);
        }

        // Died during the lockdown: saving yourself cost your core.
        IEnumerator Meltdown()
        {
            if (m_EndingStarted) yield break;
            m_EndingStarted = true;
            Current = Phase.Ending;
            if (EscapeSpawner) EscapeSpawner.Stop();
            if (m_Character && !m_Character.IsDead) LockPlayer();

            bool finished = MeltdownRequested == null;
            MeltdownRequested?.Invoke(() => finished = true);
            while (!finished) yield return null;
            if (m_Flow) m_Flow.EndGameScripted(false, EscapeEndingLine, true);
        }

        // ------------------------------------------------------------------ fight

        void StartFight()
        {
            Current = Phase.Fight;
            if (m_Weapons)
            {
                for (int i = 0; i < 9; i++) Buff(m_Weapons.GetWeaponAtSlotIndex(i));
                m_Weapons.OnAddedWeapon += OnWeaponAdded;
            }
            if (Master) Master.OpenDoors("CORE VAULT OPEN - WEAPONS OVERCHARGED");
            if (CoreObjective) CoreObjective.gameObject.SetActive(true);
        }

        void OnWeaponAdded(WeaponController weapon, int slot) => Buff(weapon);

        void Buff(WeaponController weapon)
        {
            if (!weapon || !m_Buffed.Add(weapon)) return;
            weapon.DelayBetweenShots /= FireRateMultiplier;
            weapon.MaxAmmo = Mathf.Max(1, Mathf.RoundToInt(weapon.MaxAmmo * HeatCapacityMultiplier));
            weapon.AmmoReloadRate *= CoolingRateMultiplier;
        }

        void Update()
        {
            if (Current != Phase.Fight || !m_Character) return;
            // Tested at chest height: the bounds' floor is the hall's floor, where the player's feet sit.
            if (!m_ArenaStarted && ArenaBounds.Contains(m_Character.transform.position + Vector3.up)) StartArena();
            if (m_ArenaStarted && Core && !Core.IsExposed && CoreObjective && Time.time >= m_NextCoreReadout)
            {
                m_NextCoreReadout = Time.time + .25f;
                CoreObjective.Show("Overcharge a reactor console to blow off the core's " +
                    Core.ActiveLayerName.ToLowerInvariant(), $"{ConsolesUsed}/{(Consoles != null ? Consoles.Length : 3)}");
            }
        }

        void StartArena()
        {
            m_ArenaStarted = true;
            if (ArenaSpawner) ArenaSpawner.Begin();
            if (!Core) return;
            Core.IsArmed = true;
            Core.OnLayerBroken += OnLayerBroken;
            Core.OnExposed += OnCoreExposed;
            ShowMessage("THE CORE IS ARMOURED - EXECUTE ENEMIES, THEN OVERCHARGE THE REACTOR CONSOLES");
        }

        // ------------------------------------------------------------------ reactor consoles

        public void ConsoleStarted(ReactorConsole console)
        {
            if (!m_ActiveConsole) m_ActiveConsole = console;
        }

        public void ConsoleAbandoned(ReactorConsole console)
        {
            if (m_ActiveConsole == console) m_ActiveConsole = null;
        }

        /// <summary>A console has taken its cores: run the surge into the reactor and blow off a layer.</summary>
        public void ConsoleOverloaded(ReactorConsole console)
        {
            m_ActiveConsole = console;
            ConsolesUsed++;
            StartCoroutine(OverchargeReactor(console));
        }

        IEnumerator OverchargeReactor(ReactorConsole console)
        {
            if (console && console.Cable) yield return console.Cable.Surge();
            ShowMessage("REACTOR OVERCHARGED");
            if (Beam) StartCoroutine(Beam.Overcharge(OverchargeTiming.x, OverchargeTiming.y, OverchargeTiming.z));
            yield return new WaitForSeconds(OverchargeTiming.x + BlowOffDelay);
            if (Core && !Core.IsExposed) Core.BlowOffActiveLayer();
            yield return new WaitForSeconds(Mathf.Max(0f, OverchargeTiming.y - BlowOffDelay));
            if (m_ActiveConsole == console) m_ActiveConsole = null;
        }

        void OnLayerBroken(int layer)
        {
            if (Core && layer < Core.Layers.Length) ShowMessage(Core.Layers[layer].Name.ToUpperInvariant() + " BREACHED");
        }

        void OnCoreExposed()
        {
            ShowMessage("CORE EXPOSED - EXECUTE IT FROM THE PLATFORM");
            if (CoreObjective) CoreObjective.Show("Bust the core from the platform in front of it", "EXPOSED");
        }

        public void BeginFinalBust()
        {
            if (CanBustCore) StartCoroutine(FinalBust());
        }

        IEnumerator FinalBust()
        {
            m_EndingStarted = true;
            if (ArenaSpawner) ArenaSpawner.Stop();
            LockPlayer();
            if (CoreObjective) CoreObjective.Complete("The ship's core is going critical");

            Transform eye = m_Character.PlayerCamera.transform;
            Vector3 eyeRest = eye.localPosition;
            Vector3 heart = Core.HeartPosition;

            // Face the heart.
            Vector3 flat = Vector3.ProjectOnPlane(heart - eye.position, Vector3.up);
            Quaternion bodyFrom = m_Character.transform.rotation;
            Quaternion bodyTo = flat.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(flat) : bodyFrom;
            float pitchFrom = m_Character.CameraVerticalAngle;
            float pitchTo = -Mathf.Atan2(heart.y - eye.position.y, flat.magnitude) * Mathf.Rad2Deg;
            for (float t = 0f; t < TurnDuration; t += Time.deltaTime)
            {
                float k = Smooth(t / TurnDuration);
                m_Character.transform.rotation = Quaternion.Slerp(bodyFrom, bodyTo, k);
                m_Character.CameraVerticalAngle = Mathf.Lerp(pitchFrom, pitchTo, k);
                eye.localEulerAngles = new Vector3(m_Character.CameraVerticalAngle, 0f, 0f);
                yield return null;
            }

            // Overload: the heart throbs harder and harder.
            PlayAt(OverloadSfx, heart);
            for (float t = 0f; t < OverloadDuration; t += Time.deltaTime)
            {
                float k = t / OverloadDuration;
                Core.Overload = k;
                Shake(eye, eyeRest, CameraShake * (.25f + .75f * k));
                yield return null;
            }
            Core.Overload = 1f;

            // Collapse: it caves in on itself...
            for (float t = 0f; t < CollapseDuration; t += Time.deltaTime)
            {
                Core.Collapse = Smooth(t / CollapseDuration);
                Shake(eye, eyeRest, CameraShake * .4f);
                yield return null;
            }

            // ...and bursts, while the white flash slowly rises over everything.
            PlayAt(BurstSfx, heart);
            bool flashed = false;
            bool hasOverlay = WhiteFlashRequested != null;
            WhiteFlashRequested?.Invoke(WinFlashDuration, () => flashed = true);
            float burstStart = Time.time;
            // Without an overlay, just hold the burst for the flash's length.
            while (hasOverlay ? !flashed : Time.time - burstStart < WinFlashDuration)
            {
                float k = Mathf.Clamp01((Time.time - burstStart) / BurstDuration);
                Core.Collapse = Mathf.Max(0f, 1f - k * 4f);
                Core.Burst = Mathf.Lerp(1f, BurstScale, 1f - (1f - k) * (1f - k));
                Shake(eye, eyeRest, CameraShake * 1.5f * (1f - .7f * k));
                yield return null;
            }
            eye.localPosition = eyeRest;
            if (m_Flow) m_Flow.EndGameScripted(true, WinEndingLine, true);
        }

        // ------------------------------------------------------------------ helpers

        // Stops the player moving, shooting and interacting, and keeps them safe, for a choice or a cinematic.
        void LockPlayer()
        {
            if (!m_Character || m_Locked.Count > 0) return;
            m_Character.CharacterVelocity = Vector3.zero;
            Lock(m_Character);
            Lock(m_Weapons);
            Lock(m_Character.GetComponent<KeycardInteractor>());
            Lock(m_Character.GetComponent<CoreExecutioner>());
            Lock(m_Character.GetComponent<Disassembler>());
            if (m_Health)
            {
                m_WasInvincible = m_Health.Invincible;
                m_Health.Invincible = true;
            }
        }

        void Lock(Behaviour behaviour)
        {
            if (!behaviour || !behaviour.enabled) return;
            behaviour.enabled = false;
            m_Locked.Add(behaviour);
        }

        void UnlockPlayer()
        {
            foreach (var behaviour in m_Locked)
                if (behaviour) behaviour.enabled = true;
            m_Locked.Clear();
            if (m_Health) m_Health.Invincible = m_WasInvincible;
        }

        static void Shake(Transform eye, Vector3 rest, float amount) =>
            eye.localPosition = rest + UnityEngine.Random.insideUnitSphere * amount;

        static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        void PlayAt(AudioClip clip, Vector3 position)
        {
            if (clip) AudioSource.PlayClipAtPoint(clip, position, SfxVolume);
        }

        static void ShowMessage(string message)
        {
            DisplayMessageEvent evt = Events.DisplayMessageEvent;
            evt.Message = message;
            evt.DelayBeforeDisplay = 0f;
            EventManager.Broadcast(evt);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, .3f, .1f, .5f);
            Gizmos.DrawWireCube(ArenaBounds.center, ArenaBounds.size);
        }
    }
}
