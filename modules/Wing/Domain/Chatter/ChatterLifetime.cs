using System;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Runtime;
using BoscaliSummer.Modules.Wing.Presentation;
using BoscaliSummer.Modules.Wing.Patches;
using BoscaliSummer.Modules.Wing.Networking;
using BoscaliSummer.Modules.Wing.Configuration;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Core.Util;
using BoscaliSummer.Core.Storage;
namespace BoscaliSummer.Modules.Wing.Domain
{
    /// <summary>Checks queued speech against its age and the action that produced it.</summary>
    internal readonly struct ChatterLifetime
    {
        private readonly float queuedAt;
        private readonly bool urgent;
        private readonly Func<bool> isRelevant;

        public ChatterLifetime(float queuedAt, bool urgent, Func<bool> isRelevant)
        {
            this.queuedAt = queuedAt;
            this.urgent = urgent;
            this.isRelevant = isRelevant;
        }

        public bool IsRelevant => isRelevant == null || isRelevant();
        public bool CanStart(float now) => IsRelevant && now - queuedAt < (urgent ? 15f : 6f);
    }
}
