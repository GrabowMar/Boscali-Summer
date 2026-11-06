using BoscaliSummer.Modules.Support.Domain.Fronts;
using System.Collections.Generic;
using NOAvionics;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Presentation.Fronts
{
    /// <summary>
    /// What the front window asks of its controller. Every method is a request: nothing here decides, spends or sends.
    /// The ORBIT room's presses go through <see cref="Orbit"/>; the NETWORK and SHADOW rooms use the members below.
    /// </summary>
    internal interface IFrontActions
    {
        void SetDirective(Front front, FrontDirective directive);
        /// <summary>
        /// Queues a build (<paramref name="kind"/> = the satellite kind for a SATELLITE): at the map point <paramref name="u"/>,<paramref name="v"/> (0..1, y down) when
        /// <paramref name="hasPoint"/>, else with automatic placement. The host judges the place. False when it was not sent (an earlier press still waits for the host):
        /// a map pick then stays armed for another click.
        /// </summary>
        bool Queue(Front front, ProgrammeId id, byte kind, bool hasPoint = false, float u = 0f, float v = 0f);
        /// <summary>CHIP IN: allocation into one queued row, by the host's row id.</summary>
        void Donate(Front front, ushort row, int amount);
        /// <summary>Packs up one of our EW trucks (its anchor ordinal) and redeploys it at a map point; the host judges the 20 km data center reach. False when not sent.</summary>
        bool MoveTruck(int truck, float x, float z);
        void SelectFront(Front front);
        // NETWORK room.
        void SelectNode(int nodeId);
        void Hop(int nodeId);
        void Burn(int nodeId);
        void Drop(int nodeId);
        /// <summary>Ends our whole session: every held node released, no package.</summary>
        void DropAll();
        /// <summary>Purges every enemy hold on our own network (after a countdown; the host judges cost and cooldown).</summary>
        void Purge();
        // SHADOW room. Verbs: 0 PUSH, 1 HOLD, 2 EXFIL, 3 LIFT; missions are the MissionKind byte.
        void SelectTeam(int slot);
        void SelectTarget(int targetId);
        void TeamOrder(int slot, int verb);
        void TeamMission(int slot, int missionKind, int targetId);
        /// <summary>Sends a QRF against every hostile team detected near our sites (the host judges cost and cooldown).</summary>
        void Sweep();
        /// <summary>Any real operator input (keeps the idle clock).</summary>
        void Touch();
        /// <summary>Asks for one of our satellites (a BirdKind) to burn to a new parked point (map u,v in 0..1). The host validates fuel and range.</summary>
        void RelocateBird(int birdKind, float u, float v);
        /// <summary>The ORBIT room's requests: the satellite-feed action set (null without a feed). A bird picked on the SATELLITES tab or the map asks for its feed with <c>Orbit.SetSource(kind)</c>.</summary>
        ISpaceFeedActions Orbit { get; }
    }

    /// <summary>One panel of a room's management column: its tab word and glyph, and the panel the tab shows.</summary>
    internal readonly struct ColumnTab
    {
        public readonly string Label;
        public readonly AvIcon Icon;
        public readonly RectTransform Panel;
        public ColumnTab(string label, AvIcon icon, RectTransform panel) { Label = label; Icon = icon; Panel = panel; }
    }

    /// <summary>One room: its map fills the left of the window; its management panels are tabs of the column on the right (spec 6).</summary>
    internal interface IFrontRoom
    {
        /// <summary>Builds the map into <paramref name="hero"/> (w x h) and one panel per <see cref="Column"/> tab into <paramref name="column"/> (cw x ch).</summary>
        void Build(RectTransform hero, float w, float h, RectTransform column, float cw, float ch);
        /// <summary>The room's column panels in tab order; the window adds the FRONT tab after them.</summary>
        IReadOnlyList<ColumnTab> Column { get; }
        /// <summary>The column tab a map pick asked for since the last call (a bird, node, team or target picked), or -1.</summary>
        int TakeTab();
        void Paint(FrontRoomView v);
        /// <summary>One frame of pointer input over the room's map (wheel zoom, drag pan); true when the room must repaint.</summary>
        bool Tick(UnityEngine.Vector2 mouse);
        /// <summary>The cursor the room's map asks for under <paramref name="mouse"/> (placing reticle while a pick is armed), or None for the window's arrow.</summary>
        BoscaliSummer.Core.Ui.CursorShape Cursor(UnityEngine.Vector2 mouse);
        /// <summary>Esc: ends a local mode the room is in (a relocation proposal) and returns true, so the window stays open; false when there is nothing to end.</summary>
        bool CancelMode();
        /// <summary>The window closed, switched rooms or left the room's first tab: every armed map pick ends (nothing was sent).</summary>
        void EndPicks();
    }
}
