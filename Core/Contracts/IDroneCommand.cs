using UnityEngine;

namespace BoscaliSummer.Core.Contracts
{
    /// <summary>Drone roster, pilot link and sensor feed shared by the map and interaction menu.</summary>
    internal interface IDroneCommand
    {
        bool Striking { get; }
        bool CanStrike { get; }
        int Ready { get; }
        void Strike();
        void Screen();
        bool RamMode { get; }
        void ToggleRamMode();

        int Linkable { get; }
        int LinkedIndex { get; }
        Unit Track(int index);
        string Name(int index);
        string State(int index);
        void Link(int index);

        bool Owns(int index);
        bool IsShared(int index);
        bool CanPilot(int index);
        bool Piloting { get; }
        void ToggleShare(int index);
        void Pilot(int index);
        void StopPiloting();

        bool FeedShown { get; }
        string LookWord { get; }
        string ZoomWord { get; }
        void ToggleFeed();
        void ToggleLook();
        void CycleZoom();
        Texture WantFeed();
    }
}
