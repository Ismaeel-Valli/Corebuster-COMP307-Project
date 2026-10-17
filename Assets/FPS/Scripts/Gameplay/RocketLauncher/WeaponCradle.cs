using System.Collections;
using Unity.FPS.Game;
using UnityEngine;

namespace Unity.FPS.Gameplay
{
    /// <summary>
    /// A weapon held hovering in a cradle by containment beams, one to its barrel and one to its stock, until its
    /// <see cref="OverloadPanel"/> is overloaded. The beams surge while the panel overloads, cut out when it is spent,
    /// and the weapon drops onto the cradle, where the player can take it (as the sidearm is taken from its locker).
    /// </summary>
    public class WeaponCradle : KeycardInteractable
    {
        public enum State { Held, Surging, Releasing, Ready, Taken }

        [Tooltip("The weapon handed to the player.")]
        public WeaponController Weapon;
        [Tooltip("The model held in the beams; it drops to RestPoint when released and is hidden once taken.")]
        public Transform Display;
        public Transform RestPoint;
        [Min(0f)] public float Hold = .6f;

        [Header("Beams")]
        [Tooltip("Where each beam comes from (the emitter heads).")]
        public Transform[] BeamSources;
        [Tooltip("Where each beam meets the weapon (its barrel and its stock), children of Display.")]
        public Transform[] BeamTargets;
        [Tooltip("Two line renderers per beam (Electric Arc material), interleaved strands.")]
        public LineRenderer[] BeamLines;
        [Min(2)] public int BeamSegments = 12;
        [Tooltip("Seconds each strand holds its shape before it re-strikes (min, max).")]
        public Vector2 StrikeInterval = new Vector2(.04f, .09f);
        [Tooltip("How far the strands zig-zag off the straight line, as a fraction of its length: held, surging.")]
        public Vector2 Jaggedness = new Vector2(.035f, .09f);
        [Tooltip("Strand width: held, surging.")]
        public Vector2 BeamWidth = new Vector2(.05f, .085f);
        [Min(.05f)] public float CutDuration = .45f;

        [Header("Effects")]
        [Tooltip("Sparks where each beam meets the weapon, looping while it is held.")]
        public ParticleSystem[] ContactSparks;
        [Tooltip("A burst along the weapon when the beams cut out.")]
        public ParticleSystem ReleaseBurst;
        [Tooltip("The emitter heads; they glow with the beams.")]
        public Renderer[] Lamps;
        public Color HeldColor = new Color(.35f, .75f, 1f);
        public Color SurgeColor = new Color(.85f, .6f, 1f);
        public Color SpentColor = new Color(.25f, .05f, .04f);
        [Min(.05f)] public float DropDuration = .4f;

        [Header("Wording")]
        public string ActionText = "TAKE ROCKET LAUNCHER";
        public string DetailText = "ARMOUR BUSTER - ITS BLASTS STRIP ARMOUR, THEN SHOOT THE CORE";
        public string LockedText = "HELD IN CONTAINMENT BEAMS - OVERLOAD THE PANEL TO RELEASE IT";

        [Header("Sound")]
        public AudioSource Hum;
        public AudioClip SurgeSfx;
        public AudioClip CutSfx;
        public AudioClip LandSfx;
        public AudioClip TakeSfx;

        public State Current { get; private set; }

        float[] m_NextStrike;
        Vector3[] m_Points;
        float m_BeamFade = 1f;

        public override bool IsAvailable => Current == State.Held || Current == State.Ready;
        public override float HoldDuration => Current == State.Ready ? Hold : 0f;
        public override string PromptAction(KeycardInteractor user) => Current == State.Ready ? ActionText : "ROCKET LAUNCHER";
        public override string PromptDetail(KeycardInteractor user) => Current == State.Ready ? DetailText : LockedText;

        void Awake()
        {
            m_Points = new Vector3[BeamSegments + 1];
            m_NextStrike = new float[BeamLines != null ? BeamLines.Length : 0];
            if (BeamLines != null)
                foreach (var line in BeamLines)
                {
                    if (!line) continue;
                    line.useWorldSpace = true;
                    line.positionCount = BeamSegments + 1;
                }
        }

        /// <summary>The panel is overloading: the beams strain and flare.</summary>
        public void Surge()
        {
            if (Current != State.Held) return;
            Current = State.Surging;
            PlayAt(SurgeSfx, Display ? Display.position : transform.position);
        }

        /// <summary>The panel is spent: the beams cut out and the weapon drops onto the cradle.</summary>
        public void Release()
        {
            if (Current != State.Held && Current != State.Surging) return;
            Current = State.Releasing;
            StartCoroutine(ReleaseSequence());
        }

        IEnumerator ReleaseSequence()
        {
            PlayAt(CutSfx, Display ? Display.position : transform.position);
            if (ContactSparks != null)
                foreach (var sparks in ContactSparks)
                    if (sparks) sparks.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            if (ReleaseBurst) ReleaseBurst.Play(true);

            // The beams stutter out.
            for (float t = 0f; t < CutDuration; t += Time.deltaTime)
            {
                float k = t / CutDuration;
                m_BeamFade = (1f - k) * (Random.value < .35f ? 0f : 1f);
                if (Hum) Hum.volume *= .9f;
                yield return null;
            }
            m_BeamFade = 0f;
            foreach (var line in BeamLines)
                if (line) line.enabled = false;
            if (Hum) Hum.Stop();

            // The weapon falls the short way onto the cradle.
            if (Display && RestPoint)
            {
                Vector3 from = Display.position;
                Quaternion fromRotation = Display.rotation;
                for (float t = 0f; t < DropDuration; t += Time.deltaTime)
                {
                    float k = t / DropDuration;
                    Display.SetPositionAndRotation(Vector3.Lerp(from, RestPoint.position, k * k),
                        Quaternion.Slerp(fromRotation, RestPoint.rotation, k));
                    yield return null;
                }
                Display.SetPositionAndRotation(RestPoint.position, RestPoint.rotation);
                PlayAt(LandSfx, Display.position);
            }
            Current = State.Ready;
            ShowMessage("CONTAINMENT DOWN - ROCKET LAUNCHER RELEASED");
        }

        public override void Interact(KeycardInteractor user)
        {
            if (Current == State.Held)
            {
                PlayAt(DeniedSfx, FocusPosition);
                ShowMessage("CONTAINMENT BEAMS ACTIVE - OVERLOAD THE PANEL WITH A FULL DISASSEMBLER");
                return;
            }
            if (Current != State.Ready || !Weapon) return;
            var weapons = user.GetComponent<PlayerWeaponsManager>();
            if (!weapons) return;

            Current = State.Taken;
            if (Display) Display.gameObject.SetActive(false);
            PlayAt(TakeSfx, FocusPosition);
            if (!weapons.HasWeapon(Weapon)) weapons.AddWeapon(Weapon);
            var owned = weapons.HasWeapon(Weapon);
            for (int i = 0; i < 9; i++)
                if (owned && weapons.GetWeaponAtSlotIndex(i) == owned)
                {
                    weapons.SwitchToWeaponIndex(i);
                    ShowMessage($"ROCKET LAUNCHER - PRESS {i + 1} OR SCROLL TO SWITCH WEAPONS", .6f);
                    break;
                }
        }

        void Update()
        {
            UpdateBeams();
            Color color;
            float glow;
            switch (Current)
            {
                case State.Held:
                    color = HeldColor;
                    glow = 3f + StatusLamps.Pulse(6f) * 1.5f;
                    break;
                case State.Surging:
                    color = Color.Lerp(SurgeColor, Color.white, Random.value * .5f);
                    glow = 6f + Random.value * 8f;
                    break;
                case State.Releasing:
                    color = HeldColor;
                    glow = 4f * m_BeamFade;
                    break;
                default:
                    color = SpentColor;
                    glow = .4f;
                    break;
            }
            StatusLamps.Set(Lamps, null, color, glow);
        }

        void UpdateBeams()
        {
            if (BeamLines == null || BeamSources == null || BeamTargets == null) return;
            if (Current != State.Held && Current != State.Surging && Current != State.Releasing) return;
            bool surging = Current == State.Surging;
            float jag = surging ? Jaggedness.y : Jaggedness.x;
            float width = (surging ? BeamWidth.y : BeamWidth.x) * m_BeamFade;
            for (int i = 0; i < BeamLines.Length; i++)
            {
                var line = BeamLines[i];
                int beam = i / 2;
                if (!line || beam >= BeamSources.Length || beam >= BeamTargets.Length) continue;
                line.widthMultiplier = width * (surging ? Random.Range(.7f, 1.3f) : Random.Range(.85f, 1.1f));
                if (Time.time < m_NextStrike[i]) continue;
                m_NextStrike[i] = Time.time + Random.Range(StrikeInterval.x, StrikeInterval.y) * (surging ? .6f : 1f);
                Strike(line, BeamSources[beam].position, BeamTargets[beam].position, jag);
            }
        }

        void Strike(LineRenderer line, Vector3 from, Vector3 to, float jaggedness)
        {
            Vector3 along = to - from;
            float length = along.magnitude;
            Vector3 axis = along / Mathf.Max(length, .001f);
            Vector3 side = Vector3.Cross(axis, Mathf.Abs(axis.y) < .9f ? Vector3.up : Vector3.right).normalized;
            Vector3 up = Vector3.Cross(axis, side);
            int last = m_Points.Length - 1;
            for (int j = 0; j <= last; j++)
            {
                float k = j / (float)last;
                // Pinned at both ends, widest in the middle.
                float swing = Mathf.Sin(k * Mathf.PI) * jaggedness * length;
                Vector2 offset = j == 0 || j == last ? Vector2.zero : Random.insideUnitCircle * swing;
                m_Points[j] = from + along * k + side * offset.x + up * offset.y;
            }
            line.SetPositions(m_Points);
        }
    }
}
