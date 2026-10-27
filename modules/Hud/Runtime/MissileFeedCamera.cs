using System.Collections.Generic;
using BoscaliSummer.Core.Fx;
using UnityEngine;

namespace BoscaliSummer.Modules.Hud.Runtime
{
    /// <summary>
    /// The missile feed (2026-10-12): a <see cref="FeedCamera"/> on the nose of the last own weapon in the air (<see cref="MissileTracker.Missiles"/> is launch order),
    /// drawn into a small texture the third-person cluster shows as a card. When the weapon ends the last picture is held <see cref="HoldSeconds"/>
    /// (<see cref="Lost"/>), then the feed goes, or falls back to an older weapon still flying. Ticked every frame by <c>HudBoard</c>, after the floating-origin shift. Client-local.
    /// </summary>
    internal sealed class MissileFeedCamera
    {
        // A seeker video link, not a camera: a small picture at a choppy rate, graded by its own post stack (160x90 point-filtered read as blocks, 2026-10-27).
        private const int Width = 256, Height = 144;
        private const float Fps = 15f, FieldOfView = 40f, HoldSeconds = 1.5f;

        private readonly FeedCamera feed = new FeedCamera("BoscaliMissileFeed", Width, Height, FeedLook.Seeker, pointFilter: false) { Fps = Fps, FieldOfView = FieldOfView };
        private Missile riding;
        private float lostAt = -1f;

        /// <summary>The picture while there is a feed to show (live or held), else null.</summary>
        public Texture Texture { get; private set; }
        /// <summary>The ridden weapon's short name, kept through the hold.</summary>
        public string Name { get; private set; }
        /// <summary>The weapon ended; the last picture is being held.</summary>
        public bool Lost => lostAt >= 0f;

        /// <summary>Metres from the ridden weapon to what it guides on; NaN with no target or no weapon.</summary>
        public float RangeM
        {
            get
            {
                Unit target = riding != null && !riding.disabled ? ShotBook.TargetOf(riding) : null;
                return target != null ? Vector3.Distance(riding.transform.position, target.transform.position) : float.NaN;
            }
        }

        public void Tick(IReadOnlyList<Missile> live, bool allowed, float now)
        {
            if (!allowed)
            {
                feed.Idle();
                riding = null;
                Texture = null;
                lostAt = -1f;
                return;
            }

            Missile last = null;
            for (int i = live.Count - 1; last == null && i >= 0; i--)
                if (live[i] != null && !live[i].disabled) last = live[i];

            if (last == null)
            {
                feed.Idle();
                riding = null;
                if (Texture == null) { lostAt = -1f; }
                else if (lostAt < 0f) lostAt = now;
                else if (now - lostAt > HoldSeconds) { Texture = null; lostAt = -1f; }
                return;
            }
            // Ahead of the nose, so the weapon's own body and plume stay behind the lens.
            Transform t = last.transform;
            if (!feed.Shoot(t.position + t.forward * (last.maxRadius + 0.5f), t.rotation, now)) { Texture = null; return; }
            if (!ReferenceEquals(last, riding))
            {
                riding = last;
                WeaponInfo info = null;
                try { info = last.GetWeaponInfo(); } catch { info = null; }
                Name = ShotBook.Short(info);
            }
            lostAt = -1f;
            Texture = feed.Texture;
        }

        /// <summary>Drop the camera and its picture (scene reset, board off). Idempotent.</summary>
        public void Release()
        {
            feed.Release();
            riding = null;
            Texture = null;
            Name = null;
            lostAt = -1f;
        }
    }
}
