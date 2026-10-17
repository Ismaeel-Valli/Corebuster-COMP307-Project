using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.FPS.Gameplay;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Unity.FPS.UI
{
    /// <summary>
    /// One line of the key bindings list: shows the keyboard/mouse key for an action (or one direction of a
    /// composite like Move) and rebinds it when clicked. A key already used by another row is swapped.
    /// </summary>
    public class KeyRebindRow : MonoBehaviour
    {
        const string k_Group = "Keyboard&Mouse";

        [Tooltip("Action in the project-wide input actions, e.g. Player/Jump")]
        public string ActionPath = "Player/Jump";
        [Tooltip("Composite part to rebind (up, down, left, right, negative, positive); empty for a plain binding")]
        public string CompositePart = "";
        public Button RebindButton;
        public TMP_Text KeyLabel;

        static readonly List<KeyRebindRow> s_Rows = new List<KeyRebindRow>();

        /// <summary>True while any row is waiting for a key, so menus can ignore Escape.</summary>
        public static bool IsRebinding { get; private set; }

        InputAction m_Action;
        int m_BindingIndex = -1;
        InputActionRebindingExtensions.RebindingOperation m_Operation;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_Rows.Clear();
            IsRebinding = false;
        }

        void Awake()
        {
            if (RebindButton) RebindButton.onClick.AddListener(StartRebind);
        }

        void OnEnable()
        {
            s_Rows.Add(this);
            Resolve();
            Refresh();
        }

        void OnDisable()
        {
            s_Rows.Remove(this);
            CancelRebind();
        }

        void Resolve()
        {
            m_Action = InputSystem.actions != null ? InputSystem.actions.FindAction(ActionPath) : null;
            m_BindingIndex = -1;
            if (m_Action == null) return;
            var bindings = m_Action.bindings;
            for (int i = 0; i < bindings.Count; i++)
            {
                var binding = bindings[i];
                bool keyboard = binding.groups != null && binding.groups.Contains(k_Group);
                bool match = string.IsNullOrEmpty(CompositePart)
                    ? !binding.isComposite && !binding.isPartOfComposite
                    : binding.isPartOfComposite && binding.name == CompositePart;
                if (keyboard && match)
                {
                    m_BindingIndex = i;
                    return;
                }
            }
        }

        public string EffectivePath =>
            m_Action != null && m_BindingIndex >= 0 ? m_Action.bindings[m_BindingIndex].effectivePath : null;

        public void Refresh()
        {
            if (!KeyLabel) return;
            if (m_Action == null || m_BindingIndex < 0)
            {
                KeyLabel.text = "-";
                return;
            }
            // Just the key: Execute Core's binding has a Hold interaction that would otherwise read "HOLD E".
            string display = m_Action.GetBindingDisplayString(m_BindingIndex,
                InputBinding.DisplayStringOptions.DontIncludeInteractions);
            KeyLabel.text = string.IsNullOrEmpty(display) ? "-" : display.ToUpperInvariant();
        }

        public static void RefreshAll()
        {
            foreach (var row in s_Rows)
            {
                row.Resolve();
                row.Refresh();
            }
        }

        void ApplyPath(string path)
        {
            if (m_Action == null || m_BindingIndex < 0) return;
            m_Action.ApplyBindingOverride(m_BindingIndex, path);
            Refresh();
        }

        public void StartRebind()
        {
            if (IsRebinding || m_Action == null || m_BindingIndex < 0) return;
            StartCoroutine(Rebind());
        }

        IEnumerator Rebind()
        {
            IsRebinding = true;
            if (KeyLabel) KeyLabel.text = "PRESS A KEY...";

            // Let go of the click or Enter that opened the rebind first, so it isn't taken as the new key.
            float wait = 0f;
            while (wait < .5f && ((Keyboard.current != null && Keyboard.current.anyKey.isPressed) ||
                                  (Mouse.current != null && Mouse.current.leftButton.isPressed)))
            {
                wait += Time.unscaledDeltaTime;
                yield return null;
            }

            string oldPath = EffectivePath;
            bool wasEnabled = m_Action.enabled;
            m_Action.Disable();

            m_Operation = m_Action.PerformInteractiveRebinding(m_BindingIndex)
                .WithControlsHavingToMatchPath("<Keyboard>")
                .WithControlsHavingToMatchPath("<Mouse>")
                .WithControlsExcluding("<Mouse>/position")
                .WithControlsExcluding("<Mouse>/delta")
                .WithControlsExcluding("<Mouse>/scroll")
                .WithCancelingThrough("<Keyboard>/escape")
                .OnMatchWaitForAnother(.05f)
                .OnComplete(op => Finish(oldPath, wasEnabled, true))
                .OnCancel(op => Finish(oldPath, wasEnabled, false))
                .Start();
        }

        void Finish(string oldPath, bool reenable, bool completed)
        {
            m_Operation?.Dispose();
            m_Operation = null;
            if (reenable && m_Action != null) m_Action.Enable();

            if (completed)
            {
                // Another row already on this key takes this row's old key instead.
                string newPath = EffectivePath;
                foreach (var row in s_Rows)
                    if (row != this && row.EffectivePath == newPath) row.ApplyPath(oldPath);
                KeyBindings.Save();
            }
            Refresh();
            if (isActiveAndEnabled) StartCoroutine(ClearRebindingFlag());
            else IsRebinding = false;
        }

        // Escape cancels the rebind; wait a frame so the same press doesn't also close the menu.
        IEnumerator ClearRebindingFlag()
        {
            yield return null;
            IsRebinding = false;
        }

        void CancelRebind()
        {
            if (m_Operation == null) return;
            m_Operation.Cancel();
            m_Operation?.Dispose();
            m_Operation = null;
            IsRebinding = false;
        }
    }
}
