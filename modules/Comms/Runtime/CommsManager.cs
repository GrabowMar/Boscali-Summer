using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Features.Comms.Configuration;
using BoscaliSummer.Features.Comms.Domain;
using BoscaliSummer.Features.Comms.Networking;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Features;
using BoscaliSummer.Framework.Lifecycle;
using BoscaliSummer.Runtime;
using NOAvionics;
using NOAvionics.Ui;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Features.Comms.Runtime
{
    /// <summary>What a left click on the open map does while COM owns it.</summary>
    internal enum CommsTool : byte
    {
        None = 0,
        Ping,
        Pen,
        Line,
        Arrow,
        Circle,
        Box,
        Sticker,
        Label,
        Eraser,
        Measure,
        HuntHide,
        HuntGuess,
    }

    /// <summary>
    /// The live half of COMMS. On the host it runs the <see cref="CommsAuthority"/> and hands
    /// its routed messages to the transport and to its own player's view; on every peer it
    /// keeps the <see cref="CommsClientState"/>, turns map gestures into requests, asks for a
    /// snapshot whenever the player's side changes, and raises HUD notices for what arrives.
    ///
    /// <para>Map input borrows the shared <c>MapPicker</c> while a tool is armed, so a Wing
    /// Command point order or an OPS call-in and a COMMS pen never fight over one click.
    /// The two quick paths — hold-to-draw and the quick-ping key — are momentary and stand
    /// down whenever another owner holds the map.</para>
    /// </summary>
    internal sealed class CommsManager : MonoBehaviour, ISceneService
    {
        public const string PickerOwner = "boscali.comms";
        private const string HudChannel = "comms";

        private const float ClickSlopPixels = 6f;
        private const float ShapeMinimumPixels = 6f;
        private const float PenStepPixels = 3f;
        private const float EraseRadiusPixels = 16f;
        private const int MaxRawPenPoints = 4096;
        private const float SyncRetrySeconds = 5f;
        private const float SilentHostSeconds = 8f;
        private const float HighlightSeconds = 3.5f;
        private const float SoundGapSeconds = 0.35f;

        private CommsSettings settings;
        private CommsNet net;
        private ManualLogSource logger;

        private readonly CommsAuthority authority = new CommsAuthority(Environment.TickCount);
        private readonly CommsClientState client = new CommsClientState();
        private readonly List<CommsOutbound> outbox = new List<CommsOutbound>(32);
        private readonly List<CommsArrival> arrivals = new List<CommsArrival>(8);
        private readonly List<float> penPoints = new List<float>(512);

        // ---- local choices --------------------------------------------------------------------
        public CommsTool Tool { get; private set; }
        public int PingKind;
        public int StickerKind;
        public int PenInk = 1;
        public int PenWidth = 1;
        public CommsChannel Channel = CommsChannel.Team;
        public string LabelText = "";
        public int PollDurationIndex = 1;
        public int HuntDurationIndex = 1;
        private uint huntTarget;

        // ---- gesture ------------------------------------------------------------------------
        private bool pressTracked;
        private Vector2 pressScreen;
        private bool quickTracked;
        private Vector2 quickScreen;
        private bool gesture;
        private CommsTool gestureTool;
        private Vector2 lastPenScreen;
        private float anchorX, anchorZ, currentX, currentZ;

        // ---- sync ---------------------------------------------------------------------------
        private int lastFaction = int.MinValue;
        private bool syncWanted = true;
        private bool awaitingSnapshot;
        private float nextSync;
        private float syncSentAt;
        private bool wasEnabled;
        private float nextSound;
        private bool hudDeclared;

        public CommsClientState State => client;
        public CommsSettings Settings => settings;
        public ulong LocalId => client.LocalId;
        public int LocalFaction { get; private set; }
        public bool IsHost => GameAccess.IsServer();
        public bool Online => GameAccess.IsServer() || (net != null && net.ClientActive);

        /// <summary>A peer asked for a snapshot and heard nothing: the host probably lacks the mod.</summary>
        public bool HostSilent { get; private set; }

        public bool HasMeasure { get; private set; }
        public float MeasureAX { get; private set; }
        public float MeasureAZ { get; private set; }
        public float MeasureBX { get; private set; }
        public float MeasureBZ { get; private set; }

        public float HighlightX { get; private set; }
        public float HighlightZ { get; private set; }
        public float HighlightUntil { get; private set; }

        /// <summary>The stroke or shape being dragged right now, for the map layer's preview.</summary>
        public bool PreviewActive => gesture;
        public CommsTool PreviewTool => gestureTool;
        public IReadOnlyList<float> PreviewPen => penPoints;
        public float PreviewAX => anchorX;
        public float PreviewAZ => anchorZ;
        public float PreviewBX => currentX;
        public float PreviewBZ => currentZ;

        public void Configure(CommsSettings config, CommsNet transport, ManualLogSource log)
        {
            settings = config;
            net = transport;
            logger = log;
        }

        public void ResetForScene()
        {
            authority.Reset();
            client.Clear();
            outbox.Clear();
            arrivals.Clear();
            net?.ResetScene();
            EndGesture();
            pressTracked = false;
            quickTracked = false;
            Tool = CommsTool.None;
            MapPicker.Disarm(PickerOwner);
            CommsInput.DrawToolArmed = false;
            CommsInput.GestureActive = false;
            huntTarget = 0;
            HasMeasure = false;
            HighlightUntil = 0f;
            lastFaction = int.MinValue;
            syncWanted = true;
            awaitingSnapshot = false;
            HostSilent = false;
            nextSync = 0f;
            hudDeclared = false;
        }

        private void OnDestroy()
        {
            MapPicker.Disarm(PickerOwner);
            CommsInput.DrawToolArmed = false;
            CommsInput.GestureActive = false;
            CommsInput.DrawHoldKey = KeyCode.None;
        }

        /// <summary>The transport re-registered: a new connection needs a fresh snapshot.</summary>
        public void OnTransportChanged()
        {
            syncWanted = true;
            nextSync = 0f;
        }

        private void Update()
        {
            if (settings == null) return;
            if (!settings.Enabled.Value)
            {
                if (wasEnabled) ResetForScene();
                wasEnabled = false;
                CommsInput.DrawHoldKey = KeyCode.None;
                return;
            }
            wasEnabled = true;

            float now = Time.unscaledTime;
            if (GameAccess.IsServer())
            {
                authority.Rules = ReadRules();
                outbox.Clear();
                authority.Tick(now, outbox);
                Dispatch();
            }

            UpdateIdentity(now);
            client.Tick(now);

            if (Application.isBatchMode) return;
            Announce(now);
            UpdateInput(now);
        }

        // ---- Requests --------------------------------------------------------------------------

        /// <summary>Send a request: resolved in-process on the host, sent up from a peer.</summary>
        public bool Submit(CommsIntent intent)
        {
            if (settings == null || !settings.Enabled.Value) return false;
            float now = Time.unscaledTime;
            if (GameAccess.IsServer())
            {
                if (!CommsIdentity.TryLocal(out Player local)) return false;
                authority.Rules = ReadRules();
                outbox.Clear();
                authority.Handle(Sender(local, moderator: true), intent, now, outbox);
                Dispatch();
                return true;
            }
            if (net != null && net.SendUp(intent)) return true;
            client.SetNotice("NOT CONNECTED TO A HOST", true, now);
            return false;
        }

        /// <summary>Host: a peer's request, with the identity the host resolved for it.</summary>
        public void HandleRemote(Player player, CommsIntent intent)
        {
            if (settings == null || !settings.Enabled.Value || player == null) return;
            authority.Rules = ReadRules();
            outbox.Clear();
            authority.Handle(Sender(player, moderator: false), intent, Time.unscaledTime, outbox);
            Dispatch();
        }

        /// <summary>Peer: a message from the host.</summary>
        public void ApplyRemote(CommsEnvelope envelope)
        {
            if (settings == null || !settings.Enabled.Value) return;
            if (envelope.Event == CommsEvent.Reset && (envelope.Flags & CommsFlags.Snapshot) != 0)
            {
                awaitingSnapshot = false;
                HostSilent = false;
            }
            client.Apply(envelope, Time.unscaledTime);
        }

        private void Dispatch()
        {
            if (outbox.Count == 0) return;
            bool hasLocal = CommsIdentity.TryLocal(out Player local);
            ulong localId = hasLocal ? CommsIdentity.Id(local) : 0UL;
            int faction = hasLocal ? CommsIdentity.Faction(local) : 0;
            float now = Time.unscaledTime;
            for (int i = 0; i < outbox.Count; i++)
            {
                CommsOutbound outbound = outbox[i];
                net?.Route(outbound);
                if (hasLocal && outbound.Reaches(localId, faction)) client.Apply(outbound.Envelope, now);
            }
            outbox.Clear();
        }

        private static CommsSender Sender(Player player, bool moderator) => new CommsSender
        {
            Id = CommsIdentity.Id(player),
            Name = CommsIdentity.Name(player),
            Faction = CommsIdentity.Faction(player),
            Moderator = moderator,
        };

        private CommsRules ReadRules() => new CommsRules
        {
            PingSeconds = settings.PingSeconds.Value,
            StickerSeconds = settings.StickerSeconds.Value,
            DrawingSeconds = settings.DrawingSeconds.Value,
            AllowAllChannel = settings.AllowAllChannel.Value,
            AllowDrawing = settings.AllowDrawing.Value,
            AllowGames = settings.AllowGames.Value,
        };

        /// <summary>
        /// Track who and which side this player is. A side change — including the first one,
        /// when a joining player picks a faction — asks the host for everything that side may
        /// see, and the old side's drawings go with the reset.
        /// </summary>
        private void UpdateIdentity(float now)
        {
            if (awaitingSnapshot && now - syncSentAt > SilentHostSeconds)
            {
                awaitingSnapshot = false;
                if (!HostSilent)
                    logger?.LogWarning("[COMMS] The host did not answer a snapshot request; comms need Boscali Summer with COMMS enabled on the host.");
                HostSilent = true;
                syncWanted = true;
                nextSync = now + SyncRetrySeconds * 3f;
            }

            if (!CommsIdentity.TryLocal(out Player local)) return;
            client.LocalId = CommsIdentity.Id(local);
            LocalFaction = CommsIdentity.Faction(local);
            if (LocalFaction != lastFaction)
            {
                lastFaction = LocalFaction;
                syncWanted = true;
                nextSync = 0f;
            }
            if (!syncWanted || now < nextSync) return;
            nextSync = now + SyncRetrySeconds;

            bool host = GameAccess.IsServer();
            if (!host && (net == null || !net.ClientActive)) return;
            if (!Submit(new CommsIntent { Op = CommsOp.Sync })) return;
            syncWanted = false;
            if (host)
            {
                HostSilent = false;
                return;
            }
            awaitingSnapshot = true;
            syncSentAt = now;
        }

        // ---- Notices ---------------------------------------------------------------------------

        private void Announce(float now)
        {
            arrivals.Clear();
            client.DrainArrivals(arrivals);
            if (arrivals.Count == 0) return;

            bool sound = false;
            IHudBoard board = null;
            if (settings.HudNotices.Value && ModServices.TryGet(out board) && !hudDeclared)
            {
                board.DeclareChannel(HudChannel, "COMMS");
                hudDeclared = true;
            }
            for (int i = 0; i < arrivals.Count; i++)
            {
                CommsArrival arrival = arrivals[i];
                if (board != null) board.Notice(HudChannel, HudToneOf(arrival.Tone), arrival.Text, arrival.Detail);
                sound |= arrival.Ping || arrival.Tone >= CommsTone.Caution;
            }
            if (sound && settings.PingSound.Value && now >= nextSound)
            {
                nextSound = now + SoundGapSeconds;
                AvUiSound.Tick(0.55f);
            }
        }

        private static HudTone HudToneOf(CommsTone tone)
        {
            switch (tone)
            {
                case CommsTone.Danger: return HudTone.Warning;
                case CommsTone.Caution: return HudTone.Caution;
                default: return HudTone.Info;
            }
        }

        // ---- Map input -------------------------------------------------------------------------

        private void UpdateInput(float now)
        {
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            bool open = map != null && map.mapImage != null && DynamicMap.mapMaximized;
            CommsInput.DrawHoldKey = open ? settings.DrawHoldKey.Value : KeyCode.None;
            if (!open)
            {
                CancelGesture();
                pressTracked = false;
                if (Tool != CommsTool.None) SetTool(CommsTool.None);
                CommsInput.DrawToolArmed = false;
                return;
            }

            // Another owner may have taken the map picker from under an armed tool.
            if (Tool != CommsTool.None && !MapPicker.IsOwner(PickerOwner)) Tool = CommsTool.None;
            CommsInput.DrawToolArmed = IsDrawTool(Tool);

            if (CommsInput.Typing())
            {
                CancelGesture();
                pressTracked = false;
                return;
            }

            if (Input.GetKeyDown(KeyCode.Escape) && (Tool != CommsTool.None || gesture))
            {
                CancelGesture();
                SetTool(CommsTool.None);
                return;
            }

            // The quick ping commits on release and only without travel, so a middle-drag
            // that pans or rotates something never drops a ping where it started.
            KeyCode quick = settings.QuickPingKey.Value;
            if (quick != KeyCode.None)
            {
                if (Input.GetKeyDown(quick))
                {
                    quickTracked = !PickerHeldByOther();
                    quickScreen = Input.mousePosition;
                }
                else if (quickTracked && Input.GetKeyUp(quick))
                {
                    quickTracked = false;
                    Vector2 release = Input.mousePosition;
                    if ((release - quickScreen).sqrMagnitude <= ClickSlopPixels * ClickSlopPixels &&
                        TryCursor(map, out float qx, out float qz))
                        PlacePoint(CommsItemKind.Ping, PingKind, qx, qz, null);
                }
            }

            if (gesture)
            {
                if (Input.GetMouseButton(0)) ContinueGesture(map);
                else FinishGesture(map, now);
                return;
            }

            if (Input.GetMouseButtonDown(0))
            {
                pressTracked = false;
                if (!TryCursor(map, out float x, out float z)) return;
                pressTracked = true;
                pressScreen = Input.mousePosition;

                KeyCode hold = settings.DrawHoldKey.Value;
                bool holdDraw = hold != KeyCode.None && Input.GetKey(hold) && !PickerHeldByOther();
                CommsTool tool = holdDraw && !IsDrawTool(Tool) ? CommsTool.Pen : Tool;
                if (IsDrawTool(tool)) BeginGesture(tool, x, z);
                return;
            }

            if (pressTracked && Input.GetMouseButtonUp(0))
            {
                pressTracked = false;
                Vector2 release = Input.mousePosition;
                if ((release - pressScreen).sqrMagnitude > ClickSlopPixels * ClickSlopPixels) return;
                if (TryCursor(map, out float x, out float z)) PointAction(map, x, z, now);
            }
        }

        private void BeginGesture(CommsTool tool, float x, float z)
        {
            if (tool == CommsTool.Pen && !settings.AllowDrawing.Value && GameAccess.IsServer())
            {
                client.SetNotice("DRAWING IS OFF ON THIS SERVER", true, Time.unscaledTime);
                return;
            }
            gesture = true;
            gestureTool = tool;
            CommsInput.GestureActive = true;
            anchorX = currentX = x;
            anchorZ = currentZ = z;
            penPoints.Clear();
            penPoints.Add(x);
            penPoints.Add(z);
            lastPenScreen = Input.mousePosition;
        }

        private void ContinueGesture(DynamicMap map)
        {
            // Off the map the stroke simply waits: over a panel it neither extends nor ends.
            if (!TryCursor(map, out float x, out float z)) return;
            currentX = x;
            currentZ = z;
            if (gestureTool != CommsTool.Pen) return;
            Vector2 screen = Input.mousePosition;
            if ((screen - lastPenScreen).sqrMagnitude < PenStepPixels * PenStepPixels) return;
            lastPenScreen = screen;
            penPoints.Add(x);
            penPoints.Add(z);
            if (penPoints.Count >= MaxRawPenPoints * 2) FinishGesture(map, Time.unscaledTime);
        }

        private void FinishGesture(DynamicMap map, float now)
        {
            CommsTool tool = gestureTool;
            bool moved = ((Vector2)Input.mousePosition - pressScreen).sqrMagnitude >= ShapeMinimumPixels * ShapeMinimumPixels;
            float[] pen = tool == CommsTool.Pen ? penPoints.ToArray() : null;
            EndGesture();
            pressTracked = false;

            switch (tool)
            {
                case CommsTool.Pen:
                    SubmitStroke(map, pen);
                    break;
                case CommsTool.Measure:
                    if (!moved) return;
                    HasMeasure = true;
                    MeasureAX = anchorX;
                    MeasureAZ = anchorZ;
                    MeasureBX = currentX;
                    MeasureBZ = currentZ;
                    break;
                case CommsTool.Line:
                case CommsTool.Arrow:
                case CommsTool.Circle:
                case CommsTool.Box:
                    if (!moved) return;
                    float[][] shape = CommsShapes.Build(ShapeOf(tool), anchorX, anchorZ, currentX, currentZ);
                    for (int i = 0; i < shape.Length; i++) SubmitStroke(map, shape[i]);
                    break;
            }
        }

        private void SubmitStroke(DynamicMap map, float[] xz)
        {
            if (xz == null || xz.Length < 4) return;
            float[] thin = StrokeCodec.Simplify(xz, MetresPerPixel(map) * 1.2f);
            int[] points = StrokeCodec.Encode(thin);
            if (points.Length < 4) return;
            Submit(new CommsIntent
            {
                Op = CommsOp.Place,
                Kind = (byte)CommsItemKind.Stroke,
                Channel = Channel,
                Style = (byte)Mathf.Clamp(PenInk, 0, CommsCatalog.Pens.Length - 1),
                Size = (byte)Mathf.Clamp(PenWidth, 0, CommsCatalog.PenWidths.Length - 1),
                Points = points,
            });
        }

        private void PointAction(DynamicMap map, float x, float z, float now)
        {
            switch (Tool)
            {
                case CommsTool.Ping:
                    PlacePoint(CommsItemKind.Ping, PingKind, x, z, null);
                    break;
                case CommsTool.Sticker:
                    PlacePoint(CommsItemKind.Sticker, StickerKind, x, z, null);
                    break;
                case CommsTool.Label:
                    string text = CommsText.Clean(LabelText, CommsText.MaxLabel);
                    if (text.Length == 0)
                    {
                        client.SetNotice("TYPE THE LABEL IN COM › MAP FIRST", true, now);
                        return;
                    }
                    PlacePoint(CommsItemKind.Label, 0, x, z, text);
                    break;
                case CommsTool.Eraser:
                    float radius = MetresPerPixel(map) * EraseRadiusPixels;
                    CommsItem hit = client.Board.Nearest(x, z, radius, IsHost ? (ulong?)null : LocalId);
                    if (hit == null)
                    {
                        client.SetNotice(IsHost ? "NOTHING TO ERASE THERE" : "NONE OF YOUR MARKS THERE", false, now);
                        return;
                    }
                    Submit(new CommsIntent { Op = CommsOp.Erase, Target = hit.Id });
                    break;
                case CommsTool.HuntHide:
                    client.NoteLocalHide(x, z);
                    Submit(new CommsIntent
                    {
                        Op = CommsOp.HuntStart,
                        Channel = Channel,
                        Size = (byte)Mathf.Clamp(HuntDurationIndex, 0, CommsCatalog.HuntDurations.Length - 1),
                        Points = StrokeCodec.Point(x, z),
                    });
                    SetTool(CommsTool.None);
                    break;
                case CommsTool.HuntGuess:
                    uint target = huntTarget;
                    if (Submit(new CommsIntent { Op = CommsOp.HuntGuess, Target = target, Points = StrokeCodec.Point(x, z) }))
                        client.NoteLocalGuess(target, x, z);
                    SetTool(CommsTool.None);
                    break;
            }
        }

        private void PlacePoint(CommsItemKind kind, int style, float x, float z, string text)
        {
            Submit(new CommsIntent
            {
                Op = CommsOp.Place,
                Kind = (byte)kind,
                Channel = Channel,
                Style = (byte)Mathf.Max(0, style),
                Points = StrokeCodec.Point(x, z),
                Text = text,
            });
        }

        private void EndGesture()
        {
            gesture = false;
            CommsInput.GestureActive = false;
            penPoints.Clear();
        }

        private void CancelGesture()
        {
            if (gesture) EndGesture();
        }

        private static bool TryCursor(DynamicMap map, out float x, out float z)
        {
            x = 0f;
            z = 0f;
            if (map == null || CommsInput.OverForeignUi(map)) return false;
            if (!map.TryGetCursorCoordinates(out GlobalPosition position)) return false;
            x = position.x;
            z = position.z;
            return !float.IsNaN(x) && !float.IsNaN(z) && !float.IsInfinity(x) && !float.IsInfinity(z);
        }

        /// <summary>World metres under one screen pixel at the current zoom.</summary>
        public static float MetresPerPixel(DynamicMap map)
        {
            if (map == null || map.mapImage == null) return 50f;
            float factor = map.mapDisplayFactor;
            float scale = Mathf.Abs(map.mapImage.transform.lossyScale.x);
            float product = factor * scale;
            return product > 1e-6f && !float.IsNaN(product) ? 1f / product : 50f;
        }

        private static bool PickerHeldByOther() => MapPicker.IsBusy && !MapPicker.IsOwner(PickerOwner);

        public static bool IsDrawTool(CommsTool tool) =>
            tool == CommsTool.Pen || tool == CommsTool.Line || tool == CommsTool.Arrow ||
            tool == CommsTool.Circle || tool == CommsTool.Box || tool == CommsTool.Measure;

        private static CommsShape ShapeOf(CommsTool tool)
        {
            switch (tool)
            {
                case CommsTool.Arrow: return CommsShape.Arrow;
                case CommsTool.Circle: return CommsShape.Circle;
                case CommsTool.Box: return CommsShape.Box;
                default: return CommsShape.Line;
            }
        }

        // ---- Panel verbs -----------------------------------------------------------------------

        /// <summary>Arm a tool, or disarm it when it is already the armed one.</summary>
        public void ToggleTool(CommsTool tool) => SetTool(Tool == tool ? CommsTool.None : tool);

        public void SetTool(CommsTool tool)
        {
            if (tool == CommsTool.None)
            {
                Tool = CommsTool.None;
                MapPicker.Disarm(PickerOwner);
                CommsInput.DrawToolArmed = false;
                return;
            }
            if (tool == Tool) return;
            if (!MapPicker.TryArm(PickerOwner, MapPicker.GestureLeft, Prompt(tool)))
            {
                client.SetNotice("THE MAP IS BUSY WITH ANOTHER ORDER — FINISH OR CANCEL IT", true, Time.unscaledTime);
                return;
            }
            Tool = tool;
            if (tool != CommsTool.Measure) HasMeasure = false;
            CommsInput.DrawToolArmed = IsDrawTool(tool);
        }

        /// <summary>What the status strip says while a tool is armed.</summary>
        public string Prompt(CommsTool tool)
        {
            switch (tool)
            {
                case CommsTool.Ping: return "PING · CLICK THE MAP · " + CommsCatalog.Pings[Mathf.Clamp(PingKind, 0, CommsCatalog.Pings.Length - 1)].Code + " · ESC ENDS";
                case CommsTool.Pen: return "PEN · DRAG ON THE MAP · ESC ENDS";
                case CommsTool.Line: return "LINE · DRAG FROM START TO END";
                case CommsTool.Arrow: return "ARROW · DRAG FROM TAIL TO TIP";
                case CommsTool.Circle: return "CIRCLE · DRAG FROM CENTRE TO EDGE";
                case CommsTool.Box: return "BOX · DRAG CORNER TO CORNER";
                case CommsTool.Sticker: return "STICKER · CLICK THE MAP · " + CommsCatalog.Stickers[Mathf.Clamp(StickerKind, 0, CommsCatalog.Stickers.Length - 1)].Name;
                case CommsTool.Label: return "LABEL · CLICK THE MAP TO PLACE \"" + CommsText.Clean(LabelText, CommsText.MaxLabel) + "\"";
                case CommsTool.Eraser: return IsHost ? "ERASE · CLICK ANY MARK" : "ERASE · CLICK ONE OF YOUR MARKS";
                case CommsTool.Measure: return "MEASURE · DRAG FOR RANGE AND BEARING (ONLY YOU SEE IT)";
                case CommsTool.HuntHide: return "HUNT · CLICK WHERE TO HIDE THE TARGET";
                case CommsTool.HuntGuess: return "HUNT · CLICK YOUR ONE GUESS";
                default: return null;
            }
        }

        public void Call(int call)
        {
            if (!CommsCatalog.ValidCall(call)) return;
            int[] points = null;
            if (CommsCatalog.Calls[call].MarksPosition &&
                GameManager.GetLocalAircraft(out Aircraft aircraft) && aircraft != null)
            {
                GlobalPosition at = aircraft.transform.position.ToGlobalPosition();
                points = StrokeCodec.Point(at.x, at.z);
            }
            Submit(new CommsIntent { Op = CommsOp.Call, Channel = Channel, Style = (byte)call, Points = points });
        }

        public void CreatePoll(string question, string[] options)
        {
            Submit(new CommsIntent
            {
                Op = CommsOp.PollCreate,
                Channel = Channel,
                Size = (byte)Mathf.Clamp(PollDurationIndex, 0, CommsCatalog.PollDurations.Length - 1),
                Text = question,
                Items = options,
            });
        }

        public void Vote(uint poll, int option)
        {
            if (Submit(new CommsIntent { Op = CommsOp.PollVote, Target = poll, Style = (byte)Mathf.Clamp(option, 0, 255) }))
                client.NoteLocalVote(poll, option);
        }

        public void ClosePoll(uint poll) => Submit(new CommsIntent { Op = CommsOp.PollClose, Target = poll });

        public void Roll(int die) =>
            Submit(new CommsIntent { Op = CommsOp.Roll, Channel = Channel, Style = (byte)Mathf.Clamp(die, 0, 255) });

        public void Challenge(int throwIndex) =>
            Submit(new CommsIntent { Op = CommsOp.RpsChallenge, Channel = Channel, Style = (byte)Mathf.Clamp(throwIndex, 0, 255) });

        public void AcceptDuel(uint duel, int throwIndex) =>
            Submit(new CommsIntent { Op = CommsOp.RpsAccept, Target = duel, Style = (byte)Mathf.Clamp(throwIndex, 0, 255) });

        public void CancelDuel(uint duel) => Submit(new CommsIntent { Op = CommsOp.RpsCancel, Target = duel });

        public void ArmHuntGuess(uint hunt)
        {
            huntTarget = hunt;
            SetTool(CommsTool.HuntGuess);
        }

        public void Undo()
        {
            CommsItem latest = client.Board.LatestBy(LocalId);
            if (latest == null)
            {
                client.SetNotice("NOTHING OF YOURS TO UNDO", false, Time.unscaledTime);
                return;
            }
            Submit(new CommsIntent { Op = CommsOp.Erase, Target = latest.Id });
        }

        public void ClearMine() => Submit(new CommsIntent { Op = CommsOp.ClearMine });

        public void ClearAll() => Submit(new CommsIntent { Op = CommsOp.ClearAll });

        public void ToggleChannel() =>
            Channel = Channel == CommsChannel.Team ? CommsChannel.All : CommsChannel.Team;

        /// <summary>Flash a ring on the map at a logged position so the reader can find it.</summary>
        public void Highlight(float x, float z)
        {
            HighlightX = x;
            HighlightZ = z;
            HighlightUntil = Time.unscaledTime + HighlightSeconds;
        }

        public void ClearMeasure() => HasMeasure = false;
    }
}
