using System;
using System.IO;
using UnityEngine;

namespace NOAvionics
{
    /// <summary>The kit's sheet: a palette layered over avionics.fui.avss.</summary>
    public static partial class AvStyleHost
    {
        private static AvStyleSheet fui;

        public static AvThemeId Theme { get; private set; } = AvThemeId.Portal;
        public static int FuiGeneration { get; private set; }

        public static AvStyleSheet Fui
        {
            get
            {
                if (fui == null) SetTheme(Theme);
                return fui;
            }
        }

        /// <summary>Compose palette + base, parse, and bump the generation. Never throws; keeps the old sheet on total failure.</summary>
        public static void SetTheme(AvThemeId id)
        {
            try
            {
                string palette = ReadFile(AvThemes.OverrideFile(id)) ?? ReadResource(AvThemes.PaletteResource(id));
                string baseSheet = ReadFile(AvThemes.BaseOverrideFile) ?? ReadResource(AvThemes.BaseResource);
                AvStyleSheet parsed = AvStyleSheet.Parse(AvThemes.Compose(palette, baseSheet));
                foreach (string e in parsed.Errors) Warn("avionics.fui (" + id + ") " + e);
                if (parsed.RuleCount == 0 && fui != null) { Warn("keeping the previous v2 sheet"); return; }
                fui = parsed;
                Theme = id;
                FuiGeneration++;
                Info("avionics v2 sheet: theme " + id + " (" + parsed.RuleCount + " rules)");
            }
            catch (Exception e)
            {
                Warn("avionics v2 sheet failed: " + e.Message);
                if (fui == null) fui = AvStyleSheet.Parse("");
            }
        }

        public static AvStyle FuiStyle(string classes, string state = null) => Fui.Resolve(classes, state);

        /// <summary>A :root role resolved against the live game theme.</summary>
        public static Color FuiColor(string role, Color fallback)
        {
            AvPaint p = Fui.Paint(role, fallback.ToRgba());
            return Resolve(p, fallback);
        }

        private static string ReadFile(string relative)
        {
            if (configDir == null) return null;
            string path = Path.Combine(configDir, relative);
            try { return File.Exists(path) ? File.ReadAllText(path) : null; }
            catch (Exception e) { Warn("could not read " + path + ": " + e.Message); return null; }
        }

        private static string ReadResource(string name)
        {
            using (Stream s = typeof(AvStyleHost).Assembly.GetManifestResourceStream(name))
            {
                if (s == null) return null;
                using (var r = new StreamReader(s)) return r.ReadToEnd();
            }
        }
    }
}
