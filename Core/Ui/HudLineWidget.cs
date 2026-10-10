using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Services;
using UnityEngine;

namespace BoscaliSummer.Core.Ui
{
    /// <summary>
    /// One held line on the common cockpit HUD element. A module supplies its own words and
    /// its own data; this base owns the board lookup, the slow tick, the
    /// line's lifetime and the scene reset, so every mechanic widget behaves the same way.
    ///
    /// <para>Presentation only. It resolves the board late through the service registry, so a
    /// disabled or failed Hud module leaves the widget silent instead of taking its module
    /// down, and it writes nothing but its own line.</para>
    /// </summary>
    internal abstract class HudLineWidget : MonoBehaviour, ISceneService
    {
        private IHudBoard board;
        private IHudLine line;
        private float nextRefresh;

        /// <summary>Lifetime scope and acquisition key; unique per widget.</summary>
        protected abstract string Owner { get; }

        /// <summary>Feed key: the board picks the cell's symbol and its context priority from it.</summary>
        protected abstract string ChannelKey { get; }

        /// <summary>How often the line is rewritten. Widgets are readouts, not animation.</summary>
        protected virtual float RefreshSeconds => 0.25f;

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

            if (!ModuleServices.TryGet(out board)) return;
            if (!WantsLine())
            {
                Release();
                return;
            }

            // Acquire is idempotent and recovers after board/scene replacement.
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
