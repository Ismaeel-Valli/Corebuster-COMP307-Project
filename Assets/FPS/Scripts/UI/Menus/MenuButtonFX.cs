using UnityEngine;
using UnityEngine.EventSystems;

namespace Unity.FPS.UI
{
    /// <summary>Swells a menu button slightly while it is hovered or selected.</summary>
    public class MenuButtonFX : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
    {
        [Min(1f)] public float HoverScale = 1.06f;
        [Min(.1f)] public float Speed = 14f;

        Vector3 m_BaseScale;
        bool m_Hovered;
        bool m_Selected;

        void Awake() => m_BaseScale = transform.localScale;

        void OnDisable()
        {
            m_Hovered = m_Selected = false;
            transform.localScale = m_BaseScale;
        }

        void Update()
        {
            Vector3 target = m_BaseScale * (m_Hovered || m_Selected ? HoverScale : 1f);
            transform.localScale = Vector3.Lerp(transform.localScale, target, 1f - Mathf.Exp(-Speed * Time.unscaledDeltaTime));
        }

        public void OnPointerEnter(PointerEventData eventData) => m_Hovered = true;
        public void OnPointerExit(PointerEventData eventData) => m_Hovered = false;
        public void OnSelect(BaseEventData eventData) => m_Selected = true;
        public void OnDeselect(BaseEventData eventData) => m_Selected = false;
    }
}
