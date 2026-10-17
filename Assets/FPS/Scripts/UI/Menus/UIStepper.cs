using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Unity.FPS.UI
{
    /// <summary>A "&lt; value &gt;" option picker: the arrow buttons step through a list of choices.</summary>
    public class UIStepper : MonoBehaviour
    {
        public Button PreviousButton;
        public Button NextButton;
        public TMP_Text ValueLabel;
        [Tooltip("Wrap from the last choice back to the first")]
        public bool Wrap = true;

        public event Action<int> OnValueChanged;

        string[] m_Options = new string[0];

        public int Index { get; private set; }

        void Awake()
        {
            if (PreviousButton) PreviousButton.onClick.AddListener(() => Step(-1));
            if (NextButton) NextButton.onClick.AddListener(() => Step(1));
        }

        /// <summary>Sets the choices and the current one without raising OnValueChanged.</summary>
        public void SetOptions(string[] options, int index)
        {
            m_Options = options ?? new string[0];
            Index = m_Options.Length == 0 ? 0 : Mathf.Clamp(index, 0, m_Options.Length - 1);
            Refresh();
        }

        public void Step(int direction)
        {
            if (m_Options.Length == 0) return;
            int next = Index + direction;
            next = Wrap ? (next + m_Options.Length) % m_Options.Length : Mathf.Clamp(next, 0, m_Options.Length - 1);
            if (next == Index) return;
            Index = next;
            Refresh();
            OnValueChanged?.Invoke(Index);
        }

        void Refresh()
        {
            if (ValueLabel) ValueLabel.text = m_Options.Length > 0 ? m_Options[Index] : "-";
        }
    }
}
