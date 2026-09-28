namespace NOAvionics
{
    public enum AvFace { Cond, CondStrong, Mono, MonoStrong, Icons }

    /// <summary>What a piece of text is, not how big it is. Spec §5.2.</summary>
    public enum AvTextRole { Display, Title, Head, Label, Micro, Prose, ProseSmall, Data, DataSmall, DataStrong }

    public readonly struct AvTypeSpec
    {
        public readonly AvFace Face;
        public readonly float Size;
        public readonly bool Upper;
        /// <summary>TMP characterSpacing units (1/100 em).</summary>
        public readonly float Tracking;

        public AvTypeSpec(AvFace face, float size, bool upper, float tracking)
        { Face = face; Size = size; Upper = upper; Tracking = tracking; }
    }

    public static class AvTypeScale
    {
        public const float Floor = 11f;

        public static AvTypeSpec Of(AvTextRole role)
        {
            switch (role)
            {
                case AvTextRole.Display: return new AvTypeSpec(AvFace.MonoStrong, 24f, false, 0f);
                case AvTextRole.Title: return new AvTypeSpec(AvFace.CondStrong, 18f, true, 2f);
                case AvTextRole.Head: return new AvTypeSpec(AvFace.CondStrong, 13f, true, 5f);
                case AvTextRole.Label: return new AvTypeSpec(AvFace.Cond, 12f, true, 3f);
                case AvTextRole.Micro: return new AvTypeSpec(AvFace.Cond, 11f, true, 4f);
                case AvTextRole.Prose: return new AvTypeSpec(AvFace.Cond, 13f, false, 0f);
                case AvTextRole.ProseSmall: return new AvTypeSpec(AvFace.Cond, 12f, false, 0f);
                case AvTextRole.DataSmall: return new AvTypeSpec(AvFace.Mono, 12f, false, 0f);
                case AvTextRole.DataStrong: return new AvTypeSpec(AvFace.MonoStrong, 13f, false, 0f);
                default: return new AvTypeSpec(AvFace.Mono, 13f, false, 0f);
            }
        }

        public static string AssetName(AvFace face)
        {
            switch (face)
            {
                case AvFace.CondStrong: return "NOA Barlow Condensed SemiBold SDF";
                case AvFace.Mono: return "NOA JetBrains Mono Regular SDF";
                case AvFace.MonoStrong: return "NOA JetBrains Mono Bold SDF";
                case AvFace.Icons: return "NOA Icons SDF";
                default: return "NOA Barlow Condensed Medium SDF";
            }
        }
    }
}
