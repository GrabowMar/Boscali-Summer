using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Features;
using UnityEngine;

namespace BoscaliSummer.Framework.Lifecycle
{
    /// <summary>
    /// One held line on the common cockpit HUD element. A feature supplies its own words and
    /// its own data; this base owns the feed declaration, the board lookup, the slow tick, the
    /// line's lifetime and the scene reset, so every mechanic widget behaves the same way.
    ///
    /// <para>Presentation only. It resolves the board late through the service registry, so a
    /// disabled or failed Hud module leaves the widget silent instead of taking its feature
    /// down, and it writes nothing but its own line.</para>
    /// </summary>
    internal abstract class HudLineWidget : MonoBehaviour, ISceneService
    {
        private IHudBoard board;
        private IHudLine line;
        private float nextRefresh;

        /// <summary>Lifetime scope and acquisition key; unique per widget.</summary>
        protected abstract string Owner { get; }

        /// <summary>Feed key and the label the SET page shows for it.</summary>
        protected abstract string ChannelKey { get; }
        protected abstract string ChannelLabel { get; }

        /// <summary>How often the line is rewritten. Widgets are readouts, not animation.</summary>
        protected virtual float RefreshSeconds => 0.25f;

        /// <summary>
        /// Declare the feed so SET can list it before the widget first shows. Call from the
        /// feature's Configure, after the Hud module has installed.
        /// </summary>
        protected void DeclareFeed()
        {
            if (ModServices.TryGet(out board)) board.DeclareChannel(ChannelKey, ChannelLabel);
        }

        public void ResetForScene()
        {
            Release();
            nextRefresh = 0f;
        }

        private void OnDestroy() => ResetForScene();

        private void Update()
        {
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + RefreshSeconds;

            if (!ModServices.TryGet(out board)) return;
            if (!WantsLine())
            {
                Release();
                return;
            }

            // Acquire is idempotent and recovers after board/scene replacement.
            board.DeclareChannel(ChannelKey, ChannelLabel);
            line = board.Acquire(Owner, ChannelKey, "widget");
            if (line == null) return;
            Write(line);
        }

        /// <summary>Whether the line's condition holds this tick. False releases the line.</summary>
        protected virtual bool WantsLine() => true;

        /// <summary>Push this tick's reading. The base handles the acquire and the release.</summary>
        protected abstract void Write(IHudLine line);

        private void Release()
        {
            if (line == null) return;
            line.Release();
            line = null;
        }
    }
}
