using System.Collections;
using Unity.FPS.Game;
using UnityEngine;

namespace Unity.FPS.Gameplay
{
    /// <summary>
    /// The rocket launcher's single shot and recharge, on the weapon. It holds one rocket that refills over
    /// <see cref="CooldownDuration"/> (the weapon's own automatic reload: one round at AmmoReloadRate). Each shot
    /// puts it away and draws the player's main gun; when the rocket is back it announces itself, as the Disassembler
    /// does when charged. The HUD gauge reads its countdown.
    /// </summary>
    [RequireComponent(typeof(WeaponController))]
    public class LauncherCooldown : MonoBehaviour
    {
        [Tooltip("Seconds after a shot before the launcher is put away and the main gun drawn (time to watch the rocket go).")]
        [Min(0f)] public float SwitchBackDelay = .55f;
        public AudioClip ReadySfx;
        public string ReadyMessage = "ROCKET LAUNCHER READY";

        WeaponController m_Weapon;
        bool m_WasReady = true;

        public float CooldownDuration => m_Weapon ? m_Weapon.MaxAmmo / Mathf.Max(m_Weapon.AmmoReloadRate, .0001f) : 15f;
        public float Progress => m_Weapon ? m_Weapon.CurrentAmmoRatio : 1f;
        public bool IsReady => Progress >= .999f;
        public float Remaining => (1f - Progress) * CooldownDuration;
        /// <summary>When the rocket last came back (for the HUD's flare).</summary>
        public float ReadyTime { get; private set; } = -100f;

        void Awake()
        {
            m_Weapon = GetComponent<WeaponController>();
            m_Weapon.OnShoot += OnShot;
        }

        void OnDestroy()
        {
            if (m_Weapon) m_Weapon.OnShoot -= OnShot;
        }

        void OnShot()
        {
            m_WasReady = false;
            StartCoroutine(SwitchBack());
        }

        // Back to the first other weapon the player carries (the main gun in slot 1).
        IEnumerator SwitchBack()
        {
            yield return new WaitForSeconds(SwitchBackDelay);
            var weapons = m_Weapon.Owner ? m_Weapon.Owner.GetComponent<PlayerWeaponsManager>() : null;
            if (!weapons || weapons.GetActiveWeapon() != m_Weapon) yield break;
            for (int i = 0; i < 9; i++)
            {
                var other = weapons.GetWeaponAtSlotIndex(i);
                if (!other || other == m_Weapon) continue;
                weapons.SwitchToWeaponIndex(i);
                break;
            }
        }

        void Update()
        {
            bool ready = IsReady;
            if (ready && !m_WasReady)
            {
                ReadyTime = Time.time;
                if (ReadySfx) AudioUtility.CreateSFX(ReadySfx, transform.position, AudioUtility.AudioGroups.HUDObjective, 0f);
                var evt = Events.DisplayMessageEvent;
                evt.Message = $"{ReadyMessage}  -  PRESS {SlotKey()}";
                evt.DelayBeforeDisplay = 0f;
                EventManager.Broadcast(evt);
            }
            m_WasReady = ready;
        }

        /// <summary>The number key that selects the launcher.</summary>
        public int SlotKey()
        {
            var weapons = m_Weapon && m_Weapon.Owner ? m_Weapon.Owner.GetComponent<PlayerWeaponsManager>() : null;
            if (weapons)
                for (int i = 0; i < 9; i++)
                    if (weapons.GetWeaponAtSlotIndex(i) == m_Weapon) return i + 1;
            return 2;
        }
    }
}
