using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Unity.FPS.UI
{
    /// <summary>Shows a slider's current value in a text label.</summary>
    public class SliderValueLabel : MonoBehaviour
    {
        public Slider Slider;
        public TMP_Text Label;
        public string Format = "0.00";

        void OnEnable()
        {
            if (!Slider) return;
            Slider.onValueChanged.AddListener(Refresh);
            Refresh(Slider.value);
        }

        void OnDisable()
        {
            if (Slider) Slider.onValueChanged.RemoveListener(Refresh);
        }

        void Refresh(float value)
        {
            if (Label) Label.text = value.ToString(Format);
        }
    }
}
