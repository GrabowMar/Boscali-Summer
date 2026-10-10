using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Hud.Domain;
using NOAvionics;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    /// <summary>What <see cref="ScopePageBase.Poll"/> is told (10 Hz).</summary>
    internal readonly struct ScopeEnv
    {
        /// <summary>The board's clock: unscaled seconds (pinned by the offline check).</summary>
        public readonly float Now;
        public readonly AvUnits Units;
        /// <summary>Pushes a notice onto the board's ticker (channel, tone, text, detail): a page announces an EVENT (a mark crossed, a hit taken) once, not a state.</summary>
        public readonly Action<string, HudTone, string, string> Notice;

        public ScopeEnv(float now, AvUnits units, Action<string, HudTone, string, string> notice)
        {
            Now = now;
            Units = units;
            Notice = notice;
        }
    }

    /// <summary>The surface a page paints on and the clock it paints at.</summary>
    internal readonly struct PagePaint
    {
        /// <summary>The page body's size in design units (the scope less its page strip); the prototype's <c>env.w</c> / <c>env.h</c>.</summary>
        public readonly float W, H;
        /// <summary>The compact SYSTEMS strip rather than a full page.</summary>
        public readonly bool Compact;
        /// <summary>Keep content below this y: the header band overlays the top of the body.</summary>
        public readonly float SafeTop;
        /// <summary>The board's clock: use it for every animation (never a random number or the wall clock).</summary>
        public readonly float Now;
        public readonly AvUnits Units;

        public PagePaint(float w, float h, bool compact, float safeTop, float now, AvUnits units)
        {
            W = w; H = h; Compact = compact; SafeTop = safeTop; Now = now; Units = units;
        }
    }

    /// <summary>The one figure at the right of the page's tab: readable in under a second (<c>7 S</c>, <c>2 OUT</c>, <c>GS +0.3</c>).</summary>
    internal struct ScopeHeadline
    {
        public string Text;
        public ScopeTone Tone;
        /// <summary>Pulses the headline's plate (a warning that has just changed).</summary>
        public bool Flash;
    }

    /// <summary>
    /// One page of the TAC BOARD scope (HUD v8). All nine are registered in <see cref="ScopePages"/>; a page group fills in its own
    /// <c>Pages/&lt;Name&gt;Page.cs</c> and nothing else shared. The life of a page:
    /// <list type="bullet">
    /// <item><see cref="Poll"/> (10 Hz): read the page's feed(s) with <c>ScopeFeeds.TryRead</c>, cache what <see cref="Headline"/> and <see cref="Paint"/>
    /// need, announce events through <see cref="ScopeEnv.Notice"/>; return whether the page has live content (it is then selectable and
    /// earns a pip on the other pages). A page with no live feed returns false and never shows.</item>
    /// <item><see cref="Paint"/> (while it is the current page; every frame when <see cref="Fast"/>, else at 10 Hz): draw the body with the
    /// <see cref="ScopeCanvas"/>, as a port of the prototype page's <c>draw</c>. A page that moves per frame (trails, chevrons) reads its
    /// feed again inside Paint. The canvas is cleared before every call.</item>
    /// </list>
    /// Pages hold their own cache and edge state; one instance per board. Never allocate in Paint (use <see cref="ScopeStr"/>).
    /// </summary>
    internal abstract class ScopePageBase
    {
        public abstract ScopePage Id { get; }

        /// <summary>The tab's name (<c>SYSTEMS</c>).</summary>
        public abstract string Title { get; }

        /// <summary>The pip letter other pages show for this one.</summary>
        public abstract char Code { get; }

        /// <summary>The page has data to draw at all. Only SYSTEMS is ever shown without being live (cruise), and it is not Ready without an aircraft: the shell then leaves the scope out.</summary>
        public virtual bool Ready => true;

        /// <summary>10 Hz data read; true while the page has live content.</summary>
        public abstract bool Poll(in ScopeEnv env);

        /// <summary>Repaint every frame (something moves or pulses) instead of at 10 Hz.</summary>
        public virtual bool Fast => false;

        /// <summary>What the page has to say cannot wait for a glance at a pip (a fire, an engine out, BINGO): while live and critical it ranks above the pages that only inform (<see cref="CuePriority.CriticalSystemsRank"/>). Only SYSTEMS uses it.</summary>
        public virtual bool Critical => false;

        /// <summary>The headline figure (read after <see cref="Poll"/>). Default: none.</summary>
        public virtual ScopeHeadline Headline() => default;

        /// <summary>Draw the page body.</summary>
        public abstract void Paint(ScopeCanvas g, in PagePaint p);

        /// <summary>The page has a <see cref="ScopeLayout.Strip"/>-high strip under its body (the body is that much shorter on a full page).</summary>
        public virtual bool HasStrip => false;

        public virtual void PaintStrip(ScopeCanvas g, in PagePaint p) { }

        /// <summary>This page's own gauges for the row (at most <see cref="GaugeRow.Slots"/>), written into <paramref name="into"/>; the registered gauges fill the rest. Returns the count.</summary>
        public virtual int Gauges(GaugeSpec[] into) => 0;

        /// <summary>
        /// A lamp this page overrides (by its legend, e.g. <c>ENG</c>, <c>MSL</c>, <c>SPKE</c>): its state, whether it flashes and the word it shows instead of its own legend
        /// (<paramref name="text"/> null keeps the legend: an engine out lights ENG, a strike landing close lights MSL as DNGR). False = the lamp shows its own state.
        /// A threat lamp that is lit by a real threat keeps that; a page only fills a dark one.
        /// </summary>
        public virtual bool Lamp(string legend, out LampState state, out bool flash, out string text)
        {
            state = LampState.Dark;
            flash = false;
            text = null;
            return false;
        }
    }
}
