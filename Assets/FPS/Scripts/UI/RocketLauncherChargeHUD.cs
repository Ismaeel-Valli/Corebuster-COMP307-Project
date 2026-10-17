using TMPro;
using Unity.FPS.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace Unity.FPS.UI
{
    /// <summary>
    /// The rocket launcher's recharge gauge, a twin of the Disassembler's: it fills over the 15 s countdown (shown in
    /// seconds), flares when the rocket is back and then flashes yellow with the key that draws it until it is fired.
    /// Hidden, and out of the HUD's layout, until the player carries the launcher. Added by Build Rocket Launcher.
    /// </summary>
    public class RocketLauncherChargeHUD : MonoBehaviour
    {
        public RectTransform Body;
        public Image Fill;
        public Image Frame;
        public TMP_Text Label;
        public TMP_Text Status;
        [Tooltip("Lets the gauge drop out of the HUD's layout while it is hidden.")]
        public LayoutElement Layout;

        public Color ChargingColor = new Color(1f, .5f, .16f);
        public Color ReadyColor = new Color(1f, .86f, .1f);
        public Color ReadyFlashColor = new Color(1f, 1f, .7f);
        [Tooltip("Flashes per second while ready.")]
        public float FlashRate = 3.2f;

        PlayerWeaponsManager m_Weapons;
        LauncherCooldown m_Launcher;
        bool m_Visible = true;

        void Start()
        {
            m_Weapons = FindAnyObjectByType<PlayerWeaponsManager>();
            SetVisible(false);
        }

        void SetVisible(bool visible)
        {
            if (visible == m_Visible) return;
            m_Visible = visible;
            if (Body) Body.gameObject.SetActive(visible);
            if (Layout) Layout.ignoreLayout = !visible;
        }

        void Update()
        {
            if (!m_Launcher && m_Weapons)
                for (int i = 0; i < 9 && !m_Launcher; i++)
                {
                    var weapon = m_Weapons.GetWeaponAtSlotIndex(i);
                    if (weapon) m_Launcher = weapon.GetComponent<LauncherCooldown>();
                }
            SetVisible(m_Launcher);
            if (!m_Launcher) return;

            float now = Time.time;
            bool ready = m_Launcher.IsReady;
            float flare = Mathf.Clamp01(1f - (now - m_Launcher.ReadyTime) / .6f);
            float flash = Mathf.PingPong(now * FlashRate * 2f, 1f);
            flash = flash * flash * (3f - 2f * flash);

            Color color = ready ? Color.Lerp(ReadyColor, ReadyFlashColor, Mathf.Max(flash, flare)) : ChargingColor;
            if (Fill)
            {
                Fill.fillAmount = m_Launcher.Progress;
                Fill.color = color;
            }
            if (Frame) Frame.color = ready ? Color.Lerp(Color.white, ReadyFlashColor * 1.3f, Mathf.Max(flash, flare)) : Color.white;
            if (Status)
            {
                Status.text = ready ? $"READY  [{m_Launcher.SlotKey()}]" : $"{Mathf.CeilToInt(m_Launcher.Remaining)}s";
                Status.color = ready ? color : new Color(.8f, .88f, 1f);
            }
            if (Label) Label.color = ready ? Color.Lerp(Color.white, ReadyColor, flash) : Color.white;
            if (Body) Body.localScale = Vector3.one * (1f + flare * .12f);
        }
    }
}
