using System.Collections.Generic;

namespace BoscaliSummer.Framework.Contracts
{
    internal interface ISecondaryObjectivesView
    {
        IReadOnlyList<SecondaryObjectiveView> Objectives { get; }
        string Status { get; }
        void Refresh();
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

        public SecondaryObjectiveView(int id, string title, string description, string target,
            string status, string reward, float progress, float secondsRemaining, int money, int xp, bool isComplete)
        {
            Id = id; Title = title; Description = description; Target = target;
            Status = status; Reward = reward; Progress = progress; SecondsRemaining = secondsRemaining;
            Money = money; Xp = xp; IsComplete = isComplete;
        }
    }
}
