#if UNITY_EDITOR
using System.Collections.Generic;
using BoscaliSummer.Framework.Contracts;
namespace BoscaliSummer.Features.Support.Runtime
{
    internal enum SupportActionId { Recon=4, Fortify=5, Artillery=6, Emp=7, FlareMissile=9 }
    internal sealed class SupportActionDefinition
    {
        public SupportActionId Id;
        public string Name, Description, Capability = "TEST";
        public bool Enabled = true;
    }
    internal struct SpaceState
    {
        public bool Known, Queued;
        public float X, Z, Wait, Window, StrikeWait;
        public int CancelRequest, Contacts, Outcome;
    }
    internal sealed class SupportManager
    {
        public bool BypassRequirements, DisableCooldowns, RequestPending;
        public float LocalAllocation=100, LocalCooldownRemaining, LocalCooldownTotal=30;
        public string FireTelemetry="", Status="Select an operation, then right-click the map.";
        public SupportActionId? ArmedAction;
        public SpaceState SpaceState;
        public bool SpaceStateFresh=true;
        public readonly List<SupportActionDefinition> Actions = new List<SupportActionDefinition> {
            new SupportActionDefinition { Id=SupportActionId.Recon, Name="SATELLITE SCAN", Description="Queue a faction scan for the next satellite pass. Allocation reserved; cancellable in NETWORK." },
            new SupportActionDefinition { Id=SupportActionId.Fortify, Name="ZONE FORTIFICATION", Description="Reinforce a controlled airbase or captured strategic zone." },
            new SupportActionDefinition { Id=SupportActionId.Artillery, Name="ROD FROM GOD", Description="One kinetic projectile. Requires STRIKE-1 coverage and faction capacity; select a map grid to check access." },
            new SupportActionDefinition { Id=SupportActionId.Emp, Name="EMP SHOCK", Description="Electronic warfare: 30s radar disruption after delivery. WARNING: affects friendly and hostile units." },
            new SupportActionDefinition { Id=SupportActionId.FlareMissile, Name="FLARE BARRAGE", Description="Airburst countermeasure rocket deploys a cloud of flares." }
        };
        public float Cost(SupportActionDefinition action) => 10;
        public bool IsAuthorised(SupportActionDefinition action) => true;
        public void Request(SupportActionId id) => ArmedAction=id;
        public void RequestAtMark(IObservationSource source) { }
        public void CancelScan() { SpaceState.Queued=false; SpaceState.Outcome=2; }
        public void PollSpace() { }
    }
}
#endif
