using System.Collections.Generic;

namespace BoscaliSummer.Core.Contracts
{
    /// <summary>Where a job comes from; MIS prints it as the grey source chip.</summary>
    internal enum JobKind : byte { Contract, Sortie, StrikePackage }

    /// <summary>One paid task on MIS JOBS, as its source words it. Pay below zero is a price (a strike package costs allocation).</summary>
    internal struct JobRow
    {
        public JobKind Kind;
        public int Id;
        public string Title, Detail, State;
        /// <summary>The row's verb (ACCEPT, SELECT, FIRE); empty for none. <see cref="ActLive"/> false draws it off.</summary>
        public string Act;
        public bool ActLive;
        /// <summary>This pilot's own job now (on the HUD, or armed): it leads the page and offers RELEASE.</summary>
        public bool Mine;
        public int Pay;
        public string Currency;
        /// <summary>LOCATE: a known ground point, global metres.</summary>
        public bool HasSite;
        public float X, Z;
    }

    /// <summary>
    /// A module's jobs for MIS JOBS. Each source keeps its own network verb: <see cref="Act"/> does what the row's Act says and
    /// <see cref="Release"/> hands back a job that is <see cref="JobRow.Mine"/>. A source whose module is off is not registered, so its rows drop.
    /// </summary>
    internal interface IJobSource
    {
        /// <summary>Appends the open jobs and this pilot's own; closed and stale ones are left out.</summary>
        void CopyJobs(List<JobRow> into);
        void Act(int id);
        void Release(int id);
    }

    // Registration keys: ModuleServices holds one service per type, so each source module registers under its own.
    /// <summary>DynamicOperations: contracts, and the sorties tied to the live operation.</summary>
    internal interface IOperationJobs : IJobSource { }
    /// <summary>Support: the faction's posted strike packages.</summary>
    internal interface ISupportJobs : IJobSource { }
}
