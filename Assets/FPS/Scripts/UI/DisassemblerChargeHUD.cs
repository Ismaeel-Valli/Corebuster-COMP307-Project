using TMPro;
using Unity.FPS.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace Unity.FPS.UI
{
    /// <summary>
    /// The Disassembler's charge gauge: one cell per execution banked. It fills electric blue as executions
    /// charge it, flares when it tops up, and flashes yellow while it is ready to fire. Pressing the key
    /// without charge shakes it red. Built into the HUD by HudSkinBuilder.
    /// </summary>
    public class DisassemblerChargeHUD : MonoBehaviour
    {
        [Tooltip("Everything visible. It shakes and pulses; the root's own position belongs to the HUD's layout group.")]
        public RectTransform Body;
        public Image Fill;
        [Tooltip("The gauge's gold frame; it flashes yellow while charged.")]
        public Image Frame;
        public TMP_Text Label;
        public TMP_Text Status;

        public Color ChargingColor = new Color(.3f, .68f, 1f);
        public Color ChargedColor = new Color(1f, .86f, .1f);
        public Color ChargedFlashColor = new Color(1f, 1f, .7f);
        public Color DeniedColor = new Color(1f, .3f, .22f);
        [Tooltip("Flashes per second while charged.")]
        public float FlashRate = 3.2f;
        [Min(.1f)] public float FillSpeed = 3f;

        Disassembler m_Disassembler;
        float m_Shown;

        void Start()
        {
            m_Disassembler = FindAnyObjectByType<Disassembler>();
            if (!m_Disassembler) gameObject.SetActive(false);
        }

        void Update()
        {
            if (!m_Disassembler) return;
            float now = Time.time;
            bool charged = m_Disassembler.IsCharged;
            m_Shown = Mathf.MoveTowards(m_Shown, m_Disassembler.ChargeRatio, FillSpeed * Time.deltaTime);

            // A bright flare as it fills, then a steady yellow flash until it is fired.
            float sinceCharged = now - m_Disassembler.ChargedTime;
            float flare = Mathf.Clamp01(1f - sinceCharged / .6f);
            float flash = Mathf.PingPong(now * FlashRate * 2f, 1f);
            flash = flash * flash * (3f - 2f * flash);
            float denied = Mathf.Clamp01(1f - (now - m_Disassembler.DeniedTime) / .4f);

            Color color = charged ? Color.Lerp(ChargedColor, ChargedFlashColor, Mathf.Max(flash, flare)) : ChargingColor;
            color = Color.Lerp(color, DeniedColor, denied);
            if (Fill)
            {
                Fill.fillAmount = m_Shown;
                Fill.color = color;
            }
            if (Frame)
                Frame.color = charged ? Color.Lerp(Color.white, ChargedFlashColor * 1.3f, Mathf.Max(flash, flare)) : Color.white;
            if (Status)
            {
                Status.text = charged ? $"READY  [{m_Disassembler.PromptKey}]"
                    : $"{m_Disassembler.Charge}/{m_Disassembler.ExecutionsForFullCharge} CORES";
                Status.color = charged ? color : Color.Lerp(new Color(.8f, .88f, 1f), DeniedColor, denied);
            }
            if (Label) Label.color = charged ? Color.Lerp(Color.white, ChargedColor, flash) : Color.white;

            if (Body)
            {
                Body.anchoredPosition = new Vector2(Mathf.Sin(now * 70f) * denied * 7f, 0f);
                Body.localScale = Vector3.one * (1f + flare * .12f);
            }
        }
    }
}
