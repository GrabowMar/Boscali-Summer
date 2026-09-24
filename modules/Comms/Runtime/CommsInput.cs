using System;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace BoscaliSummer.Features.Comms.Runtime
{
    /// <summary>
    /// Pointer and keyboard questions COMMS asks before it acts on the map: is the player
    /// typing, is the cursor over the map or over a panel, and should the map's own pan be held
    /// off for this frame.
    ///
    /// <para><c>DynamicMap.MapControls</c> reads the mouse through Rewired and raw input, not
    /// uGUI, so a stroke would also drag the map under the pen. The guard patch asks
    /// <see cref="HoldMapPan"/> live, in the same frame the map reads the button, so the answer
    /// never lags a frame behind the manager's own Update.</para>
    /// </summary>
    internal static class CommsInput
    {
        /// <summary>Set by the manager: a drawing tool is armed and owns left-drag.</summary>
        internal static bool DrawToolArmed;

        /// <summary>Set by the manager: the hold-to-draw key this session uses; None disables it.</summary>
        internal static KeyCode DrawHoldKey = KeyCode.None;

        /// <summary>Set by the manager while a stroke, shape or measurement is being dragged.</summary>
        internal static bool GestureActive;

        private static readonly List<RaycastResult> hits = new List<RaycastResult>(16);
        private static PointerEventData pointer;
        private static EventSystem pointerSystem;
        private static bool chatResolved;
        private static MethodInfo chatGetFlag;
        private static object chatFlag;

        /// <summary>The map's pan and zoom-by-drag should skip this frame.</summary>
        public static bool HoldMapPan()
        {
            if (!DynamicMap.mapMaximized) return false;
            if (GestureActive) return true;
            if (!Input.GetMouseButton(0) && !Input.GetMouseButtonDown(0)) return false;
            if (DrawToolArmed) return true;
            return DrawHoldKey != KeyCode.None && Input.GetKey(DrawHoldKey);
        }

        /// <summary>A text field has focus, or the game's chat is open: keys are words, not commands.</summary>
        public static bool Typing()
        {
            EventSystem events = EventSystem.current;
            GameObject selected = events != null ? events.currentSelectedGameObject : null;
            if (selected != null && selected.GetComponent<TMP_InputField>() != null) return true;
            return ChatOpen();
        }

        /// <summary>
        /// Whether the topmost UI element under the cursor belongs to something other than the
        /// map: a bezel screen, the control rail, a dialog. A cursor over the terrain, a map icon
        /// or nothing at all is fair game for a pen.
        /// </summary>
        public static bool OverForeignUi(DynamicMap map)
        {
            EventSystem events = EventSystem.current;
            if (events == null || map == null) return false;
            if (pointer == null || pointerSystem != events)
            {
                pointer = new PointerEventData(events);
                pointerSystem = events;
            }
            pointer.position = Input.mousePosition;
            hits.Clear();
            try
            {
                events.RaycastAll(pointer, hits);
            }
            catch (Exception)
            {
                return false;
            }
            if (hits.Count == 0) return false;
            GameObject top = hits[0].gameObject;
            if (top == null) return false;
            Transform mapImage = map.mapImage != null ? map.mapImage.transform : null;
            if (top == map.gameObject) return false;
            return mapImage == null || !top.transform.IsChildOf(mapImage);
        }

        /// <summary>
        /// The game's chat flag, found by name so a build that renames it only loses this one
        /// guard. Hotkeys stand down while chat is open, exactly like the game's own binds.
        /// </summary>
        private static bool ChatOpen()
        {
            if (!chatResolved)
            {
                chatResolved = true;
                try
                {
                    Type manager = AccessType("CursorManager");
                    Type flags = AccessType("CursorFlags");
                    if (manager != null && flags != null && Enum.IsDefined(flags, "Chat"))
                    {
                        chatFlag = Enum.Parse(flags, "Chat");
                        chatGetFlag = manager.GetMethod("GetFlag", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                            null, new[] { flags }, null);
                    }
                }
                catch (Exception)
                {
                    chatGetFlag = null;
                }
            }
            if (chatGetFlag == null) return false;
            try
            {
                return chatGetFlag.Invoke(null, new[] { chatFlag }) is bool open && open;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static Type AccessType(string name)
        {
            Type type = typeof(DynamicMap).Assembly.GetType(name, false);
            if (type != null) return type;
            foreach (Type candidate in typeof(DynamicMap).Assembly.GetTypes())
                if (candidate.Name == name) return candidate;
            return null;
        }
    }
}
