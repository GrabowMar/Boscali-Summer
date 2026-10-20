using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Presentation.Fronts
{
    /// <summary>
    /// The front window's mouse and Esc key. It reads Unity Input; in dev builds the in-game GUI check (<c>OpsGuiAutomation</c>) can play scripted
    /// frames through it instead, so a scripted click walks the same hit test, map gestures and room code a real one does.
    /// One scripted frame is consumed per rendered frame, on the first read of that frame, so every reader in a frame sees the same state.
    /// </summary>
    internal static class FrontPointer
    {
#pragma warning disable CS0649 // a public release strips the dev-only writers below; the readers stay
        private struct Frame { public Vector2 Pos; public int Held; public float Wheel; public bool Esc; public float Gap; }

        private static readonly Queue<Frame> script = new Queue<Frame>(64);
        private static Frame cur, prev;
        private static int frame = -1;
        private static bool scripted;
#pragma warning restore CS0649
        private static float steppedAt;

        public static Vector2 Position { get { Step(); return scripted ? cur.Pos : (Vector2)Input.mousePosition; } }
        public static bool Held(int button) { Step(); return scripted ? (cur.Held & (1 << button)) != 0 : Input.GetMouseButton(button); }
        public static bool Down(int button) { Step(); return scripted ? (cur.Held & ~prev.Held & (1 << button)) != 0 : Input.GetMouseButtonDown(button); }
        public static bool Up(int button) { Step(); return scripted ? (prev.Held & ~cur.Held & (1 << button)) != 0 : Input.GetMouseButtonUp(button); }
        public static float Wheel { get { Step(); return scripted ? cur.Wheel : Input.mouseScrollDelta.y; } }
        public static bool Escape { get { Step(); return scripted ? cur.Esc : Input.GetKeyDown(KeyCode.Escape); } }

        private static void Step()
        {
            if (frame == Time.frameCount) return;
            frame = Time.frameCount;
            if (!scripted) return;
            prev = cur;
            // A frame with a Gap waits until that many real seconds have passed since the last scripted frame was taken (slow frames cannot squeeze two clicks together).
            if (script.Count > 0 && Time.unscaledTime - steppedAt >= script.Peek().Gap) { cur = script.Dequeue(); steppedAt = Time.unscaledTime; }
            else if (script.Count > 0) { cur.Wheel = 0f; cur.Esc = false; }
            else { cur.Wheel = 0f; cur.Esc = false; } // the last frame holds its position and buttons until the next script
#if !PUBLIC_RELEASE
            Played++;
            if (cur.Wheel != 0f) { LastWheel = cur.Wheel; LastWheelAt = cur.Pos; LastWheelFrame = frame; }
#endif
        }

#if !PUBLIC_RELEASE
        /// <summary>Queues one scripted frame (dev builds only): pointer at <paramref name="pos"/> (screen pixels), <paramref name="held"/> button bits (1 left, 2 right, 4 middle).</summary>
        internal static void Play(Vector2 pos, int held, float wheel = 0f, bool esc = false, float gap = 0f)
        {
            scripted = true;
            script.Enqueue(new Frame { Pos = pos, Held = held, Wheel = wheel, Esc = esc, Gap = gap });
        }

        /// <summary>Frames still queued.</summary>
        internal static int Pending => script.Count;

        /// <summary>Diagnostics for the GUI check: scripted frames stepped, and the last wheel notch delivered (value, screen point, frame).</summary>
        internal static int Played, LastWheelFrame;
        internal static float LastWheel;
        internal static Vector2 LastWheelAt;

        /// <summary>Hands the window back to the real mouse.</summary>
        internal static void Release() { script.Clear(); scripted = false; cur = prev = default; }
#endif
    }
}
