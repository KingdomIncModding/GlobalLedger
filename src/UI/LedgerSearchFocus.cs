using KingdomFoundation.Api;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace KingdomGlobalLedger
{
    internal sealed class LedgerSearchFocus : MonoBehaviour
    {
        TMP_InputField input;
        bool ownsInput;

        internal void Initialize(TMP_InputField field)
        {
            input = field;
            field.onSelect.AddListener(_ => Begin());
            field.onDeselect.AddListener(_ => Release());
            field.onEndEdit.AddListener(_ => Release());
        }

        internal void Begin()
        {
            if (ownsInput || !FoundationApi.UiAccess.IsGameInputEnabled) return;
            ownsInput = true;
            FoundationApi.UiAccess.SetGameInputEnabled(false);
        }

        internal void Release()
        {
            if (!ownsInput) return;
            ownsInput = false;
            FoundationApi.UiAccess.SetGameInputEnabled(true);
        }

        void Update()
        {
            if (!input.isFocused) return;
            if (FoundationApi.UiAccess.IsGameInputEnabled)
            {
                ownsInput = true;
                FoundationApi.UiAccess.SetGameInputEnabled(false);
            }
            if (Keyboard.current?.escapeKey.wasPressedThisFrame == true)
            {
                input.DeactivateInputField();
                if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null);
                Release();
            }
        }

        void OnDisable() { if (input) input.DeactivateInputField(); Release(); }
    }
}
