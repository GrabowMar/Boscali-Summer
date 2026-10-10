using System;
using System.Collections.Generic;

namespace BoscaliSummer.Core.Contracts
{
    /// <summary>Fills <paramref name="data"/> with the feed's current reading; false = nothing live (the page hides).</summary>
    internal delegate bool ScopeProvider<T>(out T data) where T : struct;

    /// <summary>
    /// The typed page-data registry of the HUD v8 TAC BOARD scope. One struct per feed, one provider per struct, no shared file:
    /// <list type="number">
    /// <item>The feed's data is a plain <c>struct</c> in a file of its own: <c>Core/Contracts/Scope/&lt;Name&gt;Scope.cs</c> when
    /// the owning module is not Hud (Wing, Support, Autopilot, Vanguard, Radio, ...), <c>modules/Hud/Domain/Scope/&lt;Name&gt;Scope.cs</c>
    /// when Hud itself owns it (THREAT, SHOT, MISSILE CAM, SYSTEMS). Scalars, strings and PROVIDER-OWNED arrays only (an
    /// array field is filled in place every read and never reallocated; its consumer must not keep it past the call). Nothing
    /// else is shared, so two feeds never touch the same file.</item>
    /// <item>The owning module registers a provider when its service starts and unregisters it on teardown:
    /// <code>ScopeFeeds.Register&lt;WingScope&gt;(ReadWing);   // bool ReadWing(out WingScope d) { d = cache; return d.Count &gt; 0; }
    /// ScopeFeeds.Unregister&lt;WingScope&gt;(ReadWing);</code>
    /// A provider answers from what its module already holds (it may be called every frame while its page shows): no scans, no
    /// new strings per call, no allocation. It returns false when there is nothing live, and the page then does not show.</item>
    /// <item>The page (<c>modules/Hud/Presentation/Scope/Pages/&lt;Name&gt;Page.cs</c>) reads it:
    /// <code>if (!ScopeFeeds.TryRead(out WingScope d)) return false;   // not live</code></item>
    /// </list>
    /// One provider per struct: a second <see cref="Register{T}"/> replaces the first. A page that needs data from two
    /// modules reads two structs. Main thread only. A client that has no mirror of the data registers no provider (the
    /// element hides on clients); say which in the owning module's AGENTS.md. Offline fixtures call <see cref="Publish{T}"/>.
    /// </summary>
    internal static class ScopeFeeds
    {
        private static class Slot<T> where T : struct
        {
            public static ScopeProvider<T> Provider;
            public static bool Tracked;
        }

        private static readonly List<Action> Resets = new List<Action>();

        /// <summary>Where a throwing provider is reported (the Hud wires <c>PatchGuard.Report</c>); the reading is then "not live".</summary>
        public static Action<string, Exception> OnError;

        /// <summary>Make <paramref name="provider"/> the feed of <typeparamref name="T"/> (replaces any earlier one).</summary>
        public static void Register<T>(ScopeProvider<T> provider) where T : struct
        {
            if (provider == null) return;
            if (!Slot<T>.Tracked)
            {
                Slot<T>.Tracked = true;
                Resets.Add(() => Slot<T>.Provider = null);
            }
            Slot<T>.Provider = provider;
        }

        /// <summary>Drop <paramref name="provider"/> if it is still the registered one (a newer owner's is left alone).</summary>
        public static void Unregister<T>(ScopeProvider<T> provider) where T : struct
        {
            if (provider != null && Slot<T>.Provider == provider) Slot<T>.Provider = null;
        }

        /// <summary>Is anyone providing <typeparamref name="T"/> (not whether it has anything live right now).</summary>
        public static bool Has<T>() where T : struct => Slot<T>.Provider != null;

        /// <summary>The feed's current reading; false (and a default struct) when nothing is registered or live.</summary>
        public static bool TryRead<T>(out T data) where T : struct
        {
            ScopeProvider<T> provider = Slot<T>.Provider;
            if (provider != null)
            {
                try { if (provider(out data)) return true; }
                catch (Exception e) { OnError?.Invoke("ScopeFeed." + typeof(T).Name, e); }
            }
            data = default;
            return false;
        }

        /// <summary>A constant feed: <typeparamref name="T"/> always reads <paramref name="data"/> (offline fixtures and tests).</summary>
        public static void Publish<T>(T data) where T : struct => Register<T>((out T d) => { d = data; return true; });

        /// <summary>Withdraw every feed (scene teardown, tests).</summary>
        public static void Clear()
        {
            foreach (Action reset in Resets) reset();
        }
    }
}
