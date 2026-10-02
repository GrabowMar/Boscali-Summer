using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NOAvionics
{
    /// <summary>
    /// Input isolation helpers to strip controller/joystick axis steering from Selectables
    /// and neutralize persistent EventSystem selection after clicks.
    /// </summary>
    public static class AvInput
    {
        public static void StripNavigation(Selectable selectable)
        {
            if (selectable != null)
            {
                selectable.navigation = new Navigation { mode = Navigation.Mode.None };
            }
        }

        /// <summary>Force-release Rewired keyboard after a screen reset or hide.</summary>
        public static void ReleaseKeyboardGuard() => KeyboardGuard.Reset();

        /// <summary>Counted hold on Rewired's keyboard: flight keys never fire while any text field has focus.</summary>
        internal static class KeyboardGuard
        {
            private static int holds;
            private static bool previous = true;

            public static void Acquire()
            {
                if (holds++ > 0) return;
                Rewired.Keyboard keyboard = TryKeyboard();
                if (keyboard == null) return;
                previous = keyboard.enabled;
                keyboard.enabled = false;
            }

            public static void Release()
            {
                if (holds <= 0) return;
                if (--holds > 0) return;
                Restore();
            }

            public static void Reset()
            {
                if (holds == 0) return;
                holds = 0;
                Restore();
            }

            private static void Restore()
            {
                Rewired.Keyboard keyboard = TryKeyboard();
                if (keyboard != null) keyboard.enabled = previous;
            }

            private static Rewired.Keyboard TryKeyboard()
            {
                if (!Rewired.ReInput.isReady || Rewired.ReInput.controllers == null) return null;
                return Rewired.ReInput.controllers.Keyboard;
            }
        }

        public static void Deselect(GameObject go = null)
        {
            if (EventSystem.current != null)
            {
                if (go == null || EventSystem.current.currentSelectedGameObject == go)
                {
                    EventSystem.current.SetSelectedGameObject(null);
                }
            }
        }
    }
}
