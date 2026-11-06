using BoscaliSummer.Core.Ui;
using BoscaliSummer.Modules.Command.Domain;
using NOAvionics;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>
    /// The cursor tells what the mouse would do now. Off the big map it is the themed arrow. On it, a drag shows its gesture
    /// (orbit, pan, box add / drop); an armed tool shows the placing reticle; a contact under the pointer shows select brackets,
    /// or the drop mark when it is already selected; a Shift / Ctrl box mode shows the box cross; a wing or commandable selection
    /// that takes the right button shows the move diamond; bare ground shows the map crosshair. Over the map chrome it is the arrow.
    /// It only reads the input rules (<see cref="MfdMapInteractions"/>, <see cref="ReliefNavigator"/>), never decides one.
    /// </summary>
    internal static class MapCursor
    {
        private const float HoverRefresh = .1f;

        private static Vector2 leftDown;
        private static bool leftOnMap;
        private static UnitMapIcon hover;
        private static Vector2 hoverAt = new Vector2(-1f, -1f);
        private static float hoverUntil;

        internal static void Tick()
        {
            bool on = Plugin.Settings?.Command?.MapCursor?.Value != false;
            ModCursor.Base = on ? CursorShape.Arrow : CursorShape.None;
            if (MapUiPointer.WindowHoldsMouse())
            {
                // A big window or the C menu owns the pointer: give it the base arrow, and leave a pad menu's blank alone.
                if (ModCursor.Current != CursorShape.Blank) ModCursor.Restore();
                leftOnMap = false;
                return;
            }
            ModCursor.Set(on ? Shape() : CursorShape.None);
        }

        internal static void Reset()
        {
            hover = null;
            leftOnMap = false;
            ModCursor.Restore();
        }

        private static CursorShape Shape()
        {
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (map == null || !DynamicMap.mapMaximized || map.mapBackground == null)
            {
                leftOnMap = false;
                return CursorShape.Arrow;
            }
            Vector2 mouse = Input.mousePosition;
            bool overMap = !MapUiPointer.OverControls(mouse) && MapUiPointer.Contains(map.mapBackground.rectTransform, mouse);
            if (Input.GetMouseButtonDown(0)) { leftDown = mouse; leftOnMap = overMap; }
            if (!Input.GetMouseButton(0)) leftOnMap = false;

            // A drag keeps its glyph wherever the pointer wanders.
            if (ReliefNavigator.Turning) return CursorShape.Orbit;
            if (ReliefNavigator.Grabbing) return CursorShape.Grab;
            MapBoxMode dragging = MfdMapInteractions.BoxDragging;
            if (dragging != MapBoxMode.None) return dragging == MapBoxMode.Drop ? CursorShape.BoxDrop : CursorShape.BoxAdd;
            // The flat map pans natively on a left drag that does not box.
            if (leftOnMap && !MfdTerrainRelief.IsDrawing && !MapTool.IsClick(leftDown, mouse)) return CursorShape.Grab;

            if (!overMap) return CursorShape.Arrow;
            if (MapPicker.IsBusy) return CursorShape.Place;
            UnitMapIcon contact = Hover(map, mouse);
            if (contact != null) return map.selectedIcons != null && map.selectedIcons.Contains(contact) ? CursorShape.Drop : CursorShape.Select;
            MapBoxMode box = MfdMapInteractions.BoxModeNow();
            if (box != MapBoxMode.None) return box == MapBoxMode.Drop ? CursorShape.BoxDrop : CursorShape.BoxAdd;
            MapRightClick right = MfdMapInteractions.RightRule(map);
            return right == MapRightClick.Wing || right == MapRightClick.Native ? CursorShape.Move : CursorShape.Ground;
        }

        // The contact scan walks every map icon: redo it only when the pointer moves, or ten times a second for moving contacts.
        private static UnitMapIcon Hover(DynamicMap map, Vector2 mouse)
        {
            if ((mouse - hoverAt).sqrMagnitude > 1f || Time.unscaledTime >= hoverUntil)
            {
                hover = MfdMapInteractions.HoverContact(map, mouse);
                hoverAt = mouse;
                hoverUntil = Time.unscaledTime + HoverRefresh;
            }
            return hover != null && hover.unit != null && !hover.unit.disabled ? hover : null;
        }
    }
}
