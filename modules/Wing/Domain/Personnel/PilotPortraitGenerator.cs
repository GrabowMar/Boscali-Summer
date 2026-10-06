using System;
using BoscaliSummer.Core.Contracts;
namespace BoscaliSummer.Modules.Wing.Domain
{
    /// <summary>Body-specific portrait asset pool. The source art and outfit proportions differ by body.</summary>
    internal enum PortraitBody
    {
        Male = 0,
        Female = 1,
    }

    /// <summary>
    /// Persisted, editor-facing portrait choices. Values are semantic selectors, never atlas tile IDs.
    /// Hair zero means bald; accessory zero means no equipment.
    /// </summary>
    internal readonly struct PortraitSelection : IEquatable<PortraitSelection>
    {
        public PortraitBody Body { get; }
        public int Face { get; }
        public int Hair { get; }
        public int Uniform { get; }
        public int Accessory { get; }
        public int Backdrop { get; }

        public PortraitSelection(PortraitBody body, int face, int hair, int uniform, int accessory, int backdrop)
        {
            Body = body;
            Face = face;
            Hair = hair;
            Uniform = uniform;
            Accessory = Math.Max(0, Math.Min(PilotPortraitGenerator.AccessoryCount - 1, accessory));
            Backdrop = backdrop;
        }

        public bool Equals(PortraitSelection other) =>
            Body == other.Body && Face == other.Face && Hair == other.Hair && Uniform == other.Uniform &&
            Accessory == other.Accessory && Backdrop == other.Backdrop;

        public override bool Equals(object obj) => obj is PortraitSelection other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)Body;
                hash = hash * 31 + Face;
                hash = hash * 31 + Hair;
                hash = hash * 31 + Uniform;
                hash = hash * 31 + Accessory;
                return hash * 31 + Backdrop;
            }
        }

        public static bool operator ==(PortraitSelection left, PortraitSelection right) => left.Equals(right);
        public static bool operator !=(PortraitSelection left, PortraitSelection right) => !left.Equals(right);
    }

    /// <summary>Renderer-only atlas layer addresses resolved from a <see cref="PortraitSelection"/>.</summary>
    internal readonly struct ResolvedPortraitParts
    {
        public int FaceTile { get; }
        public int HairTile { get; }
        public int UniformTile { get; }
        public int FrontCollarTile { get; }
        public int AccessoryTile { get; }
        public int BackdropTile { get; }

        public ResolvedPortraitParts(int faceTile, int hairTile, int uniformTile, int frontCollarTile, int accessoryTile, int backdropTile)
        {
            FaceTile = faceTile;
            HairTile = hairTile;
            UniformTile = uniformTile;
            FrontCollarTile = frontCollarTile;
            AccessoryTile = accessoryTile;
            BackdropTile = backdropTile;
        }
    }

    /// <summary>Registered paper doll: backdrop, body, face, hair, front collar and equipment. RGBA rows run bottom-up, like Unity.</summary>
    internal static class PilotPortraitGenerator
    {
        public const int Width = 256;
        public const int Height = 320;
        public const int AtlasColumns = 8;
        public const int AtlasRows = 16;
        public const int AtlasWidth = Width * AtlasColumns;
        public const int AtlasHeight = Height * AtlasRows;
        public const int AtlasTileCount = AtlasColumns * AtlasRows;

        public const int FacesPerBody = 8;
        public const int HairCount = 9;       // 0 is bald, 1-8 are hair layers.
        public const int UniformCount = 14;
        public const int AccessoryCount = 8;  // 0 is none, 1-7 are faction-specific equipment layers.
        public const int BackdropCount = 8;

        private const int MaleFaceStart = 0;
        private const int FemaleFaceStart = 8;
        private const int MaleHairStart = 16;
        private const int FemaleHairStart = 24;
        private const int MaleUniformStart = 32;
        private const int FemaleUniformStart = 40;
        private const int BdfAccessoryStart = 48;
        private const int PalaAccessoryStart = 55;
        private const int FrontCollarStart = 62;
        private const int BackdropStart = 78;
        private const int OriginalUniformCount = 8;
        private const int AdditionalUniformStart = 86;
        private const int AdditionalFrontCollarStart = 98;

        public static PortraitSelection DefaultSelection =>
            new PortraitSelection(PortraitBody.Male, 0, 0, 0, 0, 0);

        public static PortraitSelection Normalize(PortraitSelection selection)
        {
            PortraitBody body = selection.Body == PortraitBody.Female ? PortraitBody.Female : PortraitBody.Male;
            return new PortraitSelection(
                body,
                Clamp(selection.Face, 0, FacesPerBody - 1),
                Clamp(selection.Hair, 0, HairCount - 1),
                Clamp(selection.Uniform, 0, UniformCount - 1),
                Clamp(selection.Accessory, 0, AccessoryCount - 1),
                Clamp(selection.Backdrop, 0, BackdropCount - 1));
        }

        public static ResolvedPortraitParts Resolve(PortraitSelection selection)
        {
            selection = Normalize(selection);
            bool female = selection.Body == PortraitBody.Female;
            int face = (female ? FemaleFaceStart : MaleFaceStart) + selection.Face;
            int hair = selection.Hair == 0 || selection.Accessory == 5 || selection.Accessory == 6
                ? -1 : (female ? FemaleHairStart : MaleHairStart) + selection.Hair - 1;
            int uniform, frontCollar;
            if (selection.Uniform < OriginalUniformCount)
            {
                uniform = (female ? FemaleUniformStart : MaleUniformStart) + selection.Uniform;
                frontCollar = FrontCollarStart + (female ? OriginalUniformCount : 0) + selection.Uniform;
            }
            else
            {
                int variant = (female ? UniformCount - OriginalUniformCount : 0) + selection.Uniform - OriginalUniformCount;
                uniform = AdditionalUniformStart + variant;
                frontCollar = AdditionalFrontCollarStart + variant;
            }
            bool pala = selection.Uniform == 2 || selection.Uniform == 3 || selection.Uniform == 6 || selection.Uniform == 7 ||
                selection.Uniform == 10 || selection.Uniform == 11 || selection.Uniform == 13;
            int accessory = selection.Accessory == 0 ? -1 :
                (pala ? PalaAccessoryStart : BdfAccessoryStart) + selection.Accessory - 1;
            return new ResolvedPortraitParts(face, hair, uniform, frontCollar, accessory, BackdropStart + selection.Backdrop);
        }

        public static string BodyLabel(PortraitBody body) => body == PortraitBody.Female ? "FEMALE" : "MALE";

        public static string UniformLabel(int uniform)
        {
            switch (Clamp(uniform, 0, UniformCount - 1))
            {
                case 1: return "BDF COMMANDER";
                case 2: return "PALA PILOT";
                case 3: return "PALA COMMANDER";
                case 4: return "BDF SOLDIER";
                case 5: return "BDF CIVILIAN";
                case 6: return "PALA SOLDIER";
                case 7: return "PALA CIVILIAN";
                case 8: return "BDF LIGHT FLIGHT";
                case 9: return "BDF HEAVY FLIGHT";
                case 10: return "PALA DESERT FLIGHT";
                case 11: return "PALA HIGH-ALT FLIGHT";
                case 12: return "BDF FIELD COMMAND";
                case 13: return "PALA FIELD COMMAND";
                default: return "BDF PILOT";
            }
        }

        public static string AccessoryLabel(int accessory)
        {
            switch (Clamp(accessory, 0, AccessoryCount - 1))
            {
                case 1: return "GLASSES";
                case 2: return "COMMS HEADSET";
                case 3: return "FIELD CAP";
                case 4: return "SERVICE CAP";
                case 5: return "FLIGHT HELMET";
                case 6: return "COMBAT HELMET";
                case 7: return "BERET";
                default: return "NONE";
            }
        }

        public static string BackdropLabel(int backdrop)
        {
            switch (Clamp(backdrop, 0, BackdropCount - 1))
            {
                case 1: return "HANGAR";
                case 2: return "FLIGHT DECK";
                case 3: return "DESERT RAMP";
                case 4: return "OPERATIONS ROOM";
                case 5: return "COASTAL CITY";
                case 6: return "WOODLAND";
                case 7: return "NIGHT AIRFIELD";
                default: return "STUDIO SLATE";
            }
        }

        public static PortraitSelection Select(string identity) => Select(identity, PortraitRole.Pilot);

        /// <summary>Identity owns body, face, hair and scene; changing service changes clothing/equipment. Faction 0 is BDF, 1 is PALA.</summary>
        public static PortraitSelection Select(string identity, PortraitRole role, int faction = -1)
        {
            // String.GetHashCode is process-dependent; an identity must keep its look across launches.
            uint hash = 2166136261;
            foreach (char c in identity ?? "WingCommand") hash = unchecked((hash ^ c) * 16777619);
            var random = new Random(unchecked((int)hash));
            PortraitBody body = random.Next(2) == 0 ? PortraitBody.Male : PortraitBody.Female;
            int face = random.Next(FacesPerBody);
            int hair = random.Next(HairCount);
            int backdrop = random.Next(BackdropCount);
            int identityFaction = random.Next(2);
            int service = faction == 0 || faction == 1 ? faction : identityFaction;
            // Clothing gets its own draw so adding outfits never rerolls anatomy, scenes or equipment.
            int variation = new Random(unchecked((int)(hash ^ 0x6D2B79F5u))).Next(role == PortraitRole.Commander ? 2 : 3);
            return new PortraitSelection(body, face, hair, UniformFor(role, service, variation), AccessoryFor(role, random.Next(4)), backdrop);
        }

        private static int AccessoryFor(PortraitRole role, int choice)
        {
            switch (role)
            {
                case PortraitRole.Commander: return choice == 2 ? 4 : choice == 3 ? 7 : choice;
                case PortraitRole.Soldier: return choice == 1 ? 2 : choice == 2 ? 3 : choice == 3 ? 6 : 0;
                case PortraitRole.Civilian: return choice;
                default: return choice == 3 ? 5 : choice;
            }
        }

        private static int UniformFor(PortraitRole role, int faction, int variation)
        {
            switch (role)
            {
                case PortraitRole.Commander: return variation == 0 ? (faction == 1 ? 3 : 1) : (faction == 1 ? 13 : 12);
                case PortraitRole.Soldier: return faction == 1 ? 6 : 4;
                case PortraitRole.Civilian: return faction == 1 ? 7 : 5;
                default: return variation == 0 ? (faction == 1 ? 2 : 0) : (faction == 1 ? 9 : 7) + variation;
            }
        }

        public static byte[] Compose(string identity, byte[] atlas) => Compose(Select(identity), atlas);

        public static byte[] Compose(PortraitSelection selection, byte[] atlas) =>
            FinishDisplay(ComposeLayers(selection, atlas));

        // Unfinished pixels keep anatomical/layer checks independent of the display treatment.
        internal static byte[] ComposeLayers(PortraitSelection selection, byte[] atlas)
        {
            if (atlas == null || atlas.Length != AtlasWidth * AtlasHeight * 4)
                throw new ArgumentException($"Expected a {AtlasWidth} x {AtlasHeight} RGBA portrait atlas.", nameof(atlas));

            ResolvedPortraitParts parts = Resolve(selection);
            var pixels = new byte[Width * Height * 4];
            for (int p = 3; p < pixels.Length; p += 4) pixels[p] = 255;
            Layer(pixels, atlas, parts.BackdropTile);
            Layer(pixels, atlas, parts.UniformTile);
            Layer(pixels, atlas, parts.FaceTile);
            int[] hairTop = selection.Accessory == 3 || selection.Accessory == 4 || selection.Accessory == 7
                ? CapUnderside(atlas, parts.AccessoryTile) : null;
            if (parts.HairTile >= 0) Layer(pixels, atlas, parts.HairTile, hairTop);
            Layer(pixels, atlas, parts.FrontCollarTile);
            if (parts.AccessoryTile >= 0)
            {
                int[] headsetRows = selection.Accessory == 2 && parts.HairTile < 0
                    ? HeadsetBandRows(atlas, parts.FaceTile, parts.AccessoryTile) : null;
                Layer(pixels, atlas, parts.AccessoryTile, sourceTopRows: headsetRows);
            }

            return pixels;
        }

        internal static byte[] FinishDisplay(byte[] pixels)
        {
            var finished = new byte[pixels.Length];
            for (int y = 0; y < Height; y++)
            {
                // Static scanlines use portrait coordinates, independent of Unity's bottom-up storage.
                int shade = (Height - 1 - y) % 4 == 2 ? 220 : 256;
                int lower = Math.Max(0, y - 1) * Width * 4;
                int upper = Math.Min(Height - 1, y + 1) * Width * 4;
                for (int x = 0; x < Width; x++)
                {
                    int p = (y * Width + x) * 4;
                    int left = (y * Width + Math.Max(0, x - 1)) * 4;
                    int right = (y * Width + Math.Min(Width - 1, x + 1)) * 4;
                    for (int c = 0; c < 3; c++)
                    {
                        // Half-pixel soft focus: retain the center and blend only its immediate neighbors.
                        int soft = (pixels[p + c] * 8 + 2 * (pixels[left + c] + pixels[right + c] +
                            pixels[lower + x * 4 + c] + pixels[upper + x * 4 + c]) + 8) / 16;
                        finished[p + c] = (byte)((soft * shade + 128) / 256);
                    }
                    finished[p + 3] = pixels[p + 3];
                }
            }
            return finished;
        }

        private static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(max, value));

        private static int[] HeadsetBandRows(byte[] atlas, int faceTile, int equipmentTile)
        {
            int Alpha(int tile, int x, int top) => atlas[((AtlasHeight - (tile / AtlasColumns + 1) * Height + Height - 1 - top) *
                AtlasWidth + tile % AtlasColumns * Width + x) * 4 + 3];
            int crown = Height, bandBottom = -1;
            for (int top = 0; top < 80; top++)
            for (int x = 123; x <= 133; x++)
            {
                if (Alpha(faceTile, x, top) >= 230) crown = Math.Min(crown, top);
                if (Alpha(equipmentTile, x, top) >= 128) bandBottom = Math.Max(bandBottom, top);
            }
            if (crown >= 80 || bandBottom < 0 || crown == bandBottom) return null;
            const int join = 95;
            var rows = new int[Height];
            for (int top = 0; top < Height; top++)
                rows[top] = top >= join ? top : Clamp((int)Math.Round(join + (top - join) *
                    (join - bandBottom) / (double)(join - crown)), 0, Height - 1);
            rows[0] = 0;
            return rows;
        }

        private static int[] CapUnderside(byte[] atlas, int tile)
        {
            var minimumTop = new int[Width];
            int left = tile % AtlasColumns * Width;
            int bottom = AtlasHeight - (tile / AtlasColumns + 1) * Height;
            for (int x = 0; x < Width; x++)
            {
                int underside = -1;
                for (int top = 0; top < Height; top++)
                    if (atlas[((bottom + Height - 1 - top) * AtlasWidth + left + x) * 4 + 3] >= 230) underside = top;
                minimumTop[x] = underside < 0 ? -1 : Math.Max(0, underside - 2);
            }
            // Empty edge columns follow the nearest actual brim, including a tilted cap's lower side.
            int[] supported = (int[])minimumTop.Clone();
            for (int x = 0; x < Width; x++)
            {
                if (supported[x] >= 0) continue;
                minimumTop[x] = 0; // An empty equipment layer must leave hair visible.
                for (int distance = 1; distance < Width; distance++)
                {
                    int nearest = x - distance >= 0 && supported[x - distance] >= 0 ? x - distance :
                        x + distance < Width && supported[x + distance] >= 0 ? x + distance : -1;
                    if (nearest < 0) continue;
                    minimumTop[x] = supported[nearest];
                    break;
                }
            }
            return minimumTop;
        }

        private static void Layer(byte[] pixels, byte[] atlas, int tile, int[] minimumTop = null, int[] sourceTopRows = null)
        {
            if (tile < 0 || tile >= AtlasTileCount) return;
            int left = tile % AtlasColumns * Width;
            int bottom = AtlasHeight - (tile / AtlasColumns + 1) * Height;
            for (int y = 0; y < Height; y++)
            {
                int sourceY = sourceTopRows == null ? y : Height - 1 - sourceTopRows[Height - 1 - y];
                for (int x = 0; x < Width; x++)
                {
                    if (minimumTop != null && Height - 1 - y < minimumTop[x]) continue;
                    int source = ((bottom + sourceY) * AtlasWidth + left + x) * 4;
                    int target = (y * Width + x) * 4;
                    int alpha = atlas[source + 3];
                    // Empty pixels are skipped; only edges need alpha blending.
                    if (alpha == 0) continue;
                    if (alpha == 255)
                    {
                        pixels[target] = atlas[source];
                        pixels[target + 1] = atlas[source + 1];
                        pixels[target + 2] = atlas[source + 2];
                        continue;
                    }
                    for (int c = 0; c < 3; c++)
                        pixels[target + c] = (byte)((atlas[source + c] * alpha + pixels[target + c] * (255 - alpha) + 127) / 255);
                }
            }
        }
    }
}
