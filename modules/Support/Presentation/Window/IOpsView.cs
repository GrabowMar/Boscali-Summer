using System.Collections.Generic;
using BoscaliSummer.Features.Support.Domain;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Presentation.Window
{
    /// <summary>
    /// One room of the OPS window. The root never paints inside the outline: the room builds its
    /// own surface, header, footer, log and host reply, edge to edge. Rects are room-local with a
    /// top-left origin and Y growing downward (the same convention as <c>Domain/Layout</c>).
    /// </summary>
    internal interface IOpsView
    {
        OpsDomain Domain { get; }

        /// <summary>The length of the room's own entrance; the root only feeds it progress.</summary>
        float EntranceSeconds { get; }

        /// <summary>Once per size, pooled. <paramref name="area"/> is the whole interior.</summary>
        void Build(RectTransform room, Rect area);

        /// <summary>Selected node, aim point or nothing; may be null.</summary>
        void Show(object context);

        void Hide();

        /// <summary>The room's own choreography, 0..1; 1 is the final state and must be complete.</summary>
        void Entrance(float progress);

        void Refresh(double now, float time, bool textTick);

        /// <summary>Every key except Esc and the Ctrl chords the root owns.</summary>
        bool HandleKeys();

        void RightClickInside();

        /// <summary>The element the eye should land on first (harness).</summary>
        Rect Hero { get; }

        /// <summary>The room's section rects (harness: coverage and uniqueness).</summary>
        IReadOnlyList<Rect> Sections { get; }
    }
}
