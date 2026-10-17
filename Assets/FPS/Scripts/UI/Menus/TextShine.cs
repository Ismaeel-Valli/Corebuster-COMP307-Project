using System.Collections;
using TMPro;
using UnityEngine;

namespace Unity.FPS.UI
{
    /// <summary>
    /// A band of light sweeping across a TextMeshPro text: the glyphs brighten as it passes (vertex
    /// colours), and an optional glint streak slides across on top.
    /// </summary>
    [RequireComponent(typeof(TMP_Text))]
    public class TextShine : MonoBehaviour
    {
        [Tooltip("Resting brightness of the text; the shine lifts it to full white.")]
        public Color BaseColor = new Color(.78f, .74f, .66f, 1f);
        public Color ShineColor = Color.white;
        [Min(.05f)] public float Duration = .8f;
        [Tooltip("Width of the bright band, as a fraction of the text width")]
        [Range(.02f, 1f)] public float BandWidth = .18f;
        [Tooltip("Slant of the band (x offset per unit of height)")]
        public float Slant = .35f;
        [Tooltip("Optional streak moved across the text with the band")]
        public RectTransform Glint;

        TMP_Text m_Text;

        void Awake()
        {
            m_Text = GetComponent<TMP_Text>();
            // Mesh rebuilds reset vertex colours to the text colour, so keep that at the resting shade.
            m_Text.color = BaseColor;
            if (Glint) Glint.gameObject.SetActive(false);
        }

        void OnEnable()
        {
            m_Text.ForceMeshUpdate();
            Apply(-1f);
        }

        public void Play()
        {
            StopAllCoroutines();
            StartCoroutine(Sweep());
        }

        IEnumerator Sweep()
        {
            m_Text.ForceMeshUpdate();
            Rect rect = m_Text.rectTransform.rect;
            if (Glint) Glint.gameObject.SetActive(true);
            for (float t = 0f; t <= Duration; t += Time.unscaledDeltaTime)
            {
                // Ease in-out so the light accelerates across the middle.
                float k = t / Duration;
                k = k * k * (3f - 2f * k);
                float band = Mathf.Lerp(-.3f, 1.3f, k);
                Apply(band);
                if (Glint)
                    Glint.anchoredPosition = new Vector2(Mathf.Lerp(rect.xMin - rect.width * .3f, rect.xMax + rect.width * .3f, k),
                        Glint.anchoredPosition.y);
                yield return null;
            }
            Apply(-1f);
            if (Glint) Glint.gameObject.SetActive(false);
        }

        // band: position of the light across the text, 0 = left edge, 1 = right edge.
        void Apply(float band)
        {
            TMP_TextInfo info = m_Text.textInfo;
            if (info == null) return;
            Bounds bounds = m_Text.textBounds;
            float width = Mathf.Max(1f, bounds.size.x);
            for (int m = 0; m < info.meshInfo.Length; m++)
            {
                Color32[] colors = info.meshInfo[m].colors32;
                Vector3[] vertices = info.meshInfo[m].vertices;
                if (colors == null || vertices == null) continue;
                for (int v = 0; v < colors.Length && v < vertices.Length; v++)
                {
                    Vector3 p = vertices[v];
                    float x = (p.x - bounds.min.x + (p.y - bounds.center.y) * Slant) / width;
                    float light = Mathf.Clamp01(1f - Mathf.Abs(x - band) / BandWidth);
                    colors[v] = Color.Lerp(BaseColor, ShineColor, light * light);
                }
            }
            m_Text.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
        }
    }
}
