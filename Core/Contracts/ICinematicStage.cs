using System;
using System.Collections.Generic;

namespace BoscaliSummer.Core.Contracts
{
    internal enum StageShotKind : byte
    {
        /// <summary>Circle a point given in world coordinates.</summary>
        OrbitPoint,
        /// <summary>Circle an airbase, named as the mission names it.</summary>
        OrbitAirbase,
        /// <summary>Circle a unit and travel with it, named by its unique name.</summary>
        OrbitUnit,
        /// <summary>Travel in a line from one point to another, looking at a third.</summary>
        Fly,
        /// <summary>Ride with a unit and look at it: X, Y, Z are metres to its right, above it and ahead of it.</summary>
        Chase,
    }

    /// <summary>One camera move of a cutscene. Positions are global (sea level) coordinates in metres; angles are compass degrees.</summary>
    internal struct StageShot
    {
        public StageShotKind Kind;
        /// <summary>The airbase or unit for the two kinds that circle one.</summary>
        public string Target;
        /// <summary>The orbit's centre, or where a Fly move starts.</summary>
        public float X, Y, Z;
        /// <summary>Where a Fly move ends and what it looks at.</summary>
        public float ToX, ToY, ToZ, LookX, LookY, LookZ;
        /// <summary>Orbit: distance from the centre, height above it, and the bearings the camera sweeps between.</summary>
        public float Radius, Height, FromDegrees, ToDegrees;
        public float Seconds, Fov;
        /// <summary>Handheld shake 0..1 and a held roll in degrees.</summary>
        public float Shake, Roll;
    }

    /// <summary>
    /// The cutscene camera: plays camera moves on the game's own free camera for the local viewer only. It takes the camera only from the detached free camera of
    /// a viewer who is not flying, and gives it back where it found it. Published by Cinematography; a script that finds no stage shows its captions without pictures.
    /// </summary>
    internal interface ICinematicStage
    {
        bool Playing { get; }

        /// <summary>
        /// Plays <paramref name="shots"/> in order. <paramref name="onShot"/> hears the index of each shot as it starts (a shot whose path is blocked or whose
        /// target is gone is skipped); <paramref name="onEnd"/> is called once when the camera is given back, also after <see cref="Stop"/>. False, with neither
        /// called, when the camera cannot be taken.
        /// </summary>
        bool Play(IReadOnlyList<StageShot> shots, Action<int> onShot, Action onEnd);

        void Stop();
    }
}
