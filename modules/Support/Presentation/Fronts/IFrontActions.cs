using BoscaliSummer.Modules.Support.Domain.Fronts;
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
        /// <summary>Asks for this front's funding share to move by <paramref name="deltaPct"/> points.</summary>
        void SetPriority(Front front, int deltaPct);
        void SetFocus(Front front);
        void Queue(Front front, ProgrammeId id);
        void Donate(Front front, ProgrammeId id, int amount);
        void SelectFront(Front front);
        // NETWORK room.
        void SelectNode(int nodeId);
        void Hop(int nodeId);
        void Burn(int nodeId);
        void Drop(int nodeId);
        // SHADOW room. Verbs: 0 PUSH, 1 HOLD, 2 EXFIL, 3 LIFT; missions are the MissionKind byte.
        void SelectTeam(int slot);
        void SelectTarget(int targetId);
        void TeamOrder(int slot, int verb);
        void TeamMission(int slot, int missionKind, int targetId);
        /// <summary>Any real operator input (keeps the idle clock).</summary>
        void Touch();
        /// <summary>Asks for one of our satellites (a BirdKind) to burn to a new parked point (map u,v in 0..1). The host validates fuel and range.</summary>
        void RelocateBird(int birdKind, float u, float v);
        /// <summary>The ORBIT room's requests: the existing satellite-feed action set.</summary>
        ISpaceFeedActions Orbit { get; }
    }

    /// <summary>One room: a hero view that fills the left of the window; the rail beside it is shared.</summary>
    internal interface IFrontRoom
    {
        void Build(RectTransform hero, float w, float h);
        void Paint(FrontRoomView v);
        /// <summary>One frame of pointer input over the room's map (wheel zoom, drag pan); true when the room must repaint.</summary>
        bool Tick(UnityEngine.Vector2 mouse);
    }
}
