using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.Space;
using NOAvionics;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Presentation
{
    internal enum FeedImageKind : byte { None, Optical, Sar }

    /// <summary>A host-revealed contact bracket over the picture: 0..1 across the picture, origin bottom-left.</summary>
    internal struct FeedBracketView
    {
        public int Id;
        public float U, V;
        public ProbableClass Class;
        public bool Marked, Selected;
        public string Label;
    }

    /// <summary>One track-file row: a host-revealed contact (or a MARK whose reveal lapsed, a fixed point).</summary>
    internal struct FeedTileView
    {
        public bool Present, Selected, Marked, Moving, FixedPoint;
        public int Id;
        public byte Percent;
        public ProbableClass Class;
        public string Title, Sub;
    }

    /// <summary>One constellation cell: the bird, its real state word and the tone of that word.</summary>
    internal struct FeedBirdView
    {
        public string State;
        public AvState Tone;
    }

    /// <summary>
    /// The view model the ORBIT room paints. Built each refresh by <see cref="SpaceFeedController"/> from the faction mirror (never from a
    /// client registry); reused, never reallocated.
    /// </summary>
    internal sealed class SpaceFeedView
    {
        public const int MaxBrackets = 16;

        public bool Ground, ThreatActive;
        public string Threat = "";
        public FeedImageKind ImageKind;
        public Texture Image;
        public float ImageAspect = 1.6f;
        public string Refusal = "";
        public readonly List<FeedBracketView> Brackets = new List<FeedBracketView>(MaxBrackets);
        public BirdKind Source;
        public int Zoom;
        public string Status = "";
        public AvState StatusTone = AvState.Inert;
        public readonly FeedTileView[] Tiles = new FeedTileView[SpaceFeedRules.ContactsPerPage];
        /// <summary>The single line that replaces the track-file rows when none is revealed (empty when there are contacts).</summary>
        public string NoContacts = "";
        public AvState NoContactsTone = AvState.Inert;
        public int Page, Pages = 1;
        public int SelectedId;
        public bool CanConfirm, CanSend, ConfirmFull, ZoomEnabled = true;
        public int SendCount;
        public string ConfirmHelp = "", SendHelp = "";
        /// <summary>The three bird cells in <see cref="BirdKind"/> order: OPTICAL, RADAR, KINETIC.</summary>
        public readonly FeedBirdView[] Birds = new FeedBirdView[3];
        public string ConstellationMeta = "";
        public string Words = "";
        public AvState WordsTone = AvState.Inert;
    }

    /// <summary>What the ORBIT room asks of the feed controller. Every method is a request: none spends or decides anything.</summary>
    internal interface ISpaceFeedActions
    {
        void SelectEntry(int id);
        void PrevPage();
        void NextPage();
        void Confirm();
        void Send();
        void SetSource(BirdKind source);
        void CycleZoom();
        /// <summary>Any real operator input on the feed (keeps the idle clock and the host lease).</summary>
        void Touch();
    }
}
