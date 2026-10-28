using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Command.Domain;
using NOAvionics;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    // DRAW: the pilot's own offensive, HOI4 style. The plate's DRAW button takes the shared map picker; a press on the map is the jump-off (the
    // host snaps it to the nearest front), the drag shows the arrow, the release on the objective sends it. A click without a drag keeps DRAW
    // armed; Esc, DRAW again or closing the map cancels. While armed the native pan, the relief grab and icon clicks stand aside.
    internal sealed partial class StrMapOverlay
    {
        internal const string DrawOwner = "boscali.str.draw";
        private const string DrawPrompt = "DRAW: PRESS ON A FRONT, DRAG TO THE OBJECTIVE, RELEASE \u00b7 ESC CANCELS";
        private const float DrawSlopPixels = 12f, DrawNoteSeconds = 6f;

        /// <summary>True while DRAW holds the map.</summary>
        internal static bool DrawArmed => MapPicker.IsOwner(DrawOwner);

        private bool drawHeld, drawPauseHeld;
        private int drawArmedFrame;
        private Vector2 drawDownScreen;
        private float drawSX, drawSZ, drawEX, drawEZ, drawNoteUntil = -1f, drawFactor;
        private IMapProjection drawProjection;

        /// <summary>The plate's DRAW button: arms the tool, or cancels it when armed.</summary>
        internal StrAct ToggleDraw()
        {
            if (DrawArmed) { CancelDraw("DRAW CANCELLED"); return new StrAct(StrWhy.None, "", true); }
            IStrHub hub = Hub;
            StrAct gate = StrGate.DrawGate(hub?.Current);
            if (!gate.Allowed) { DrawSay(gate.Text, DrawNoteSeconds); return gate; }
            if (!MapPicker.TryArm(DrawOwner, MapPicker.GestureLeft, DrawPrompt))
            {
                DrawSay("THE MAP IS BUSY WITH ANOTHER PICK", DrawNoteSeconds);
                return new StrAct(StrWhy.Pending, "THE MAP IS BUSY WITH ANOTHER PICK");
            }
            if (!hub.Ui.ShowOperation) hub.SetLayer(StrLayers.Operation, true);   // the arrow is drawn on the operation layer
            drawArmedFrame = Time.frameCount;
            drawHeld = false;
            if (!drawPauseHeld && !Application.isBatchMode) { GameplayUI.AllowPauseKeybind = false; drawPauseHeld = true; }   // Esc cancels instead of pausing
            DrawSay(DrawPrompt, float.PositiveInfinity);
            return new StrAct(StrWhy.None, "", true);
        }

        /// <summary>Drops the tool and says why (nothing when the text is empty).</summary>
        internal void CancelDraw(string why)
        {
            drawHeld = false;
            MapPicker.Disarm(DrawOwner);
            if (drawPauseHeld) { GameplayUI.AllowPauseKeybind = true; drawPauseHeld = false; }
            DrawSay(why, string.IsNullOrEmpty(why) ? 0f : DrawNoteSeconds);
        }

        /// <summary>Test seam: the harness and the sim send a drawn arrow without a mouse, through the same path as a release.</summary>
        internal StrAct ReleaseDraw(float sx, float sz, float ox, float oz)
        {
            IStrHub hub = Hub;
            StrAct act = hub != null ? hub.Draw(sx, sz, ox, oz) : new StrAct(StrWhy.NoBoard, "NO BOARD YET");
            if (act.Sent) CancelDraw(act.Text);
            else DrawSay(act.Text + (DrawArmed ? " \u00b7 TRY AGAIN" : ""), DrawArmed ? float.PositiveInfinity : DrawNoteSeconds);
            return act;
        }

        private void TickDraw(DynamicMap map, float now)
        {
            if (drawNoteUntil >= 0f && now > drawNoteUntil && !DrawArmed) DrawSay("", 0f);
            if (!DrawArmed)
            {
                drawHeld = false;
                if (drawPauseHeld) { GameplayUI.AllowPauseKeybind = true; drawPauseHeld = false; }
                return;
            }
            if (Time.frameCount <= drawArmedFrame + 1 || Application.isBatchMode) return;   // the click on DRAW is not the press
            if (!drawHeld && Input.GetMouseButtonDown(0) && !MapUiPointer.OverControls() && map.TryGetCursorCoordinates(out GlobalPosition at))
            {
                drawHeld = true;
                drawDownScreen = Input.mousePosition;
                drawSX = drawEX = at.x; drawSZ = drawEZ = at.z;
            }
            if (!drawHeld) return;
            if (map.TryGetCursorCoordinates(out GlobalPosition cursor)) { drawEX = cursor.x; drawEZ = cursor.z; }
            if (Input.GetMouseButton(0) && !Input.GetMouseButtonUp(0)) return;
            drawHeld = false;
            if (((Vector2)Input.mousePosition - drawDownScreen).sqrMagnitude < DrawSlopPixels * DrawSlopPixels)
            {
                DrawSay(DrawPrompt, float.PositiveInfinity);   // a click, not a drag: stay armed
                return;
            }
            ReleaseDraw(drawSX, drawSZ, drawEX, drawEZ);
        }

        // The arrow under the cursor, in the pulse mesh: amber while its length is allowed, the danger ink when it is too short or too long.
        private void DrawDraft(AvQuadBuffer pb)
        {
            if (!drawHeld) return;
            float dx = drawEX - drawSX, dz = drawEZ - drawSZ, d2 = dx * dx + dz * dz;
            if (!(d2 > 1f)) return;
            bool fits = d2 >= TheaterDrawLimits.MinimumMetres * TheaterDrawLimits.MinimumMetres && d2 <= TheaterDrawLimits.MaximumMetres * TheaterDrawLimits.MaximumMetres;
            StrInkRole ink = fits ? StrInkRole.Effort : StrInkRole.Danger;
            var draft = new StrPrim { Shape = StrShape.Offensive, Ink = ink, Id = new StrItemId(StrItemKind.Effort, 0), X = drawSX, Z = drawSZ, X2 = drawEX, Z2 = drawEZ };
            Offensive(pb, in draft, drawProjection, drawFactor, pulsePx, StrInk.RgbaOf(ink));
        }

        private void DrawSay(string text, float seconds)
        {
            if (chrome == null) return;
            text = text ?? "";
            drawNoteUntil = text.Length == 0 ? -1f : Time.unscaledTime + seconds;
            if (chrome.Note == text) return;
            chrome.Note = text;
            IStrHub hub = Hub;
            if (hub != null) chrome.BindPlate(hub.Current, hub.Ui);
        }
    }
}
