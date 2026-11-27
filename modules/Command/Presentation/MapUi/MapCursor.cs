using BoscaliSummer.Modules.Command.Configuration;
using BoscaliSummer.Core.Ui;
using BoscaliSummer.Modules.Command.Domain;
using NOAvionics;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>
    /// The cursor tells what the mouse would do now. Off the big map it is the themed arrow. On it, a drag shows its gesture
    /// (orbit, pan, box add / drop); an armed tool shows the placing reticle; a dense stack shows the stack glyph; a contact or
    /// airbase under the pointer shows select brackets (or drop mark when already selected); Shift / Ctrl / Alt modifiers preview
    /// their drag mode; a commanding selection over ground shows the move diamond; bare ground shows the map crosshair.
    /// It only reads the input rules (<see cref="MfdMapInteractions"/>, <see cref="ReliefNavigator"/>), never decides one.
    /// </summary>
    internal static class MapCursor
    {
        private const float HoverRefresh = .1f;

        private static Vector2 leftDown;
        private static bool leftOnMap;
        private static UnitMapIcon hover;
        private static AirbaseMapIcon hoverBase;
        private static Vector2 hoverAt = new Vector2(-1f, -1f);
        private static float hoverUntil;

        internal static UnitMapIcon HoveredContact => hover != null && hover.unit != null && !hover.unit.disabled ? hover : null;
        internal static AirbaseMapIcon HoveredAirbase => hoverBase != null && hoverBase.airbase != null ? hoverBase : null;
        internal static CursorShape CurrentShape { get; private set; } = CursorShape.Arrow;
        internal static bool OverMap { get; private set; }

        internal static void Tick()
        {
            bool on = CommandSettingsScope.Current?.MapCursor?.Value != false;
            ModCursor.Base = on ? CursorShape.Arrow : CursorShape.None;
            if (MapUiPointer.WindowHoldsMouse())
            {
                // A big window or the C menu owns the pointer: the shape the window claims, else the base arrow; a pad menu's blank is left alone.
                if (ModCursor.Window != CursorShape.None) ModCursor.Set(ModCursor.Window);
                else if (ModCursor.Current != CursorShape.Blank) ModCursor.Restore();
                leftOnMap = false;
                OverMap = false;
                CurrentShape = ModCursor.Current;
                return;
            }
            CurrentShape = Shape();
            ModCursor.Set(on ? CurrentShape : CursorShape.None);
        }

        internal static void Reset()
        {
            hover = null;
            hoverBase = null;
            leftOnMap = false;
            OverMap = false;
            CurrentShape = CursorShape.Arrow;
            ModCursor.Restore();
        }

        private static CursorShape Shape()
        {
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (map == null || !DynamicMap.mapMaximized || map.mapBackground == null)
            {
                leftOnMap = false;
                OverMap = false;
                hover = null;
                hoverBase = null;
                return CursorShape.Arrow;
            }
            Vector2 mouse = Input.mousePosition;
            bool overMap = !MapUiPointer.OverControls(mouse) && MapUiPointer.Contains(map.mapBackground.rectTransform, mouse);
            OverMap = overMap;
            if (Input.GetMouseButtonDown(0)) { leftDown = mouse; leftOnMap = overMap; }
            if (!Input.GetMouseButton(0)) leftOnMap = false;

            // A drag keeps its glyph wherever the pointer wanders.
            if (ReliefNavigator.Turning) return CursorShape.Orbit;
            if (ReliefNavigator.Grabbing) return CursorShape.Grab;
            MapBoxMode dragging = MfdMapInteractions.BoxDragging;
            if (dragging != MapBoxMode.None) return dragging == MapBoxMode.Drop ? CursorShape.BoxDrop : CursorShape.BoxAdd;
            // The flat map pans natively on a left drag that does not box.
            if (leftOnMap && !MfdTerrainRelief.IsDrawing && !MapTool.IsClick(leftDown, mouse)) return CursorShape.Grab;

            if (!overMap)
            {
                hover = null;
                hoverBase = null;
                return CursorShape.Arrow;
            }
            if (MapPicker.IsBusy) return CursorShape.Place;
            if (MfdTerrainRelief.IsDrawing && (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)))
                return CursorShape.Orbit;
            MapBoxMode box = MfdMapInteractions.BoxModeNow();
            if (box != MapBoxMode.None) return box == MapBoxMode.Drop ? CursorShape.BoxDrop : CursorShape.BoxAdd;
            if (MfdTerrainRelief.IsDrawing && MfdTerrainRelief.HoveredStackCount >= 3) return CursorShape.Stack;
            RefreshHover(map, mouse);
            UnitMapIcon contact = HoveredContact;
            if (contact != null) return map.selectedIcons != null && map.selectedIcons.Contains(contact) ? CursorShape.Drop : CursorShape.Select;
            if (HoveredAirbase != null) return CursorShape.Select;
            MapRightClick right = MfdMapInteractions.RightRule(map);
            return right == MapRightClick.Wing || right == MapRightClick.Native ? CursorShape.Move : CursorShape.Ground;
        }

        // The icon scans walk the map icons: redo only when the pointer moves, or ten times a second for moving contacts.
        private static void RefreshHover(DynamicMap map, Vector2 mouse)
        {
            if ((mouse - hoverAt).sqrMagnitude > 1f || Time.unscaledTime >= hoverUntil)
            {
                hover = MfdMapInteractions.HoverContact(map, mouse);
                hoverBase = hover == null ? MfdMapInteractions.HoverAirbase(map, mouse) : null;
                hoverAt = mouse;
                hoverUntil = Time.unscaledTime + HoverRefresh;
            }
        }
    }
}
