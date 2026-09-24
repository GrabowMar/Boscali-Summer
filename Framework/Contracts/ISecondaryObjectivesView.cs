using System.Collections.Generic;

namespace BoscaliSummer.Framework.Contracts
{
    internal interface ISecondaryObjectivesView
    {
        IReadOnlyList<SecondaryObjectiveView> Objectives { get; }
        string Status { get; }

        /// <summary>
        /// How many contracts the local faction may hold accepted at once, or 0 when the
        /// host does not report a ceiling. A panel reads 0 as "no ceiling to show" and
        /// prints a plain count rather than inventing a limit.
        /// </summary>
        int ActiveLimit { get; }

        void Refresh();
        void RequestAccept(int id);
        void RequestCancel(int id);
    }

    internal sealed class SecondaryObjectiveView
    {
        public int Id { get; }
        public string Title { get; }
        public string Description { get; }
        public string Target { get; }
        public string Status { get; }
        public string Reward { get; }
        public float Progress { get; }
        public float SecondsRemaining { get; }
        public int Money { get; }
        public int Xp { get; }
        public bool IsComplete { get; }
        public bool IsOffered { get; }
        public bool IsActive { get; }
        public bool HasMarker { get; }
        public float X { get; }
        public float Z { get; }
        public float Radius { get; }
        /// <summary>The pilot who accepted this faction-wide contract, for display only.</summary>
        public string AcceptedBy { get; }

        public SecondaryObjectiveView(int id, string title, string description, string target,
            string status, string reward, float progress, float secondsRemaining, int money, int xp, bool isComplete,
            bool isOffered = false, bool isActive = false, bool hasMarker = false, float x = 0f, float z = 0f, float radius = 0f,
            string acceptedBy = null)
        {
            Id = id; Title = title; Description = description; Target = target;
            Status = status; Reward = reward; Progress = progress; SecondsRemaining = secondsRemaining;
            Money = money; Xp = xp; IsComplete = isComplete;
            IsOffered = isOffered; IsActive = isActive; HasMarker = hasMarker; X = x; Z = z; Radius = radius;
            AcceptedBy = acceptedBy ?? string.Empty;
        }
    }
}
