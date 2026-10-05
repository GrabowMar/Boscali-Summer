using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Wing.Domain;

internal static class Program
{
    private static void Main(string[] args)
    {
        IdentityAndRoles();
        SemanticSelectors();
        Composition();
        DisplayFinish();
        if (args.Length != 0)
        {
            Check((args.Length == 4 || args.Length == 6 && args[4] == "--manifest") && args[0] == "--atlas" && args[2] == "--out",
                  "Usage: PortraitCheck [--atlas <bottom-up RGBA path> --out <directory> [--manifest <atlas.json>]]");
            RealAtlas(args[1], args[3], args.Length == 6 ? args[5] : null);
        }
        Console.WriteLine("PortraitCheck passed: identity/role invariance, legacy selectors, all atlas addresses and RGBA composition.");
    }

    private static void RealAtlas(string atlasPath, string outputDirectory, string manifestPath)
    {
        byte[] atlas = File.ReadAllBytes(atlasPath);
        Check(atlas.Length == PilotPortraitGenerator.AtlasWidth * PilotPortraitGenerator.AtlasHeight * 4,
              "Real atlas dimensions do not match the production compositor.");
        for (int tile = 0; tile < PilotPortraitGenerator.AtlasTileCount; tile++)
        {
            int left = tile % PilotPortraitGenerator.AtlasColumns * PilotPortraitGenerator.Width;
            int bottom = PilotPortraitGenerator.AtlasHeight - (tile / PilotPortraitGenerator.AtlasColumns + 1) * PilotPortraitGenerator.Height;
            bool visible = false, anyAlpha = false;
            for (int y = 0; y < PilotPortraitGenerator.Height; y++)
            for (int x = 0; x < PilotPortraitGenerator.Width; x++)
            {
                byte alpha = atlas[((bottom + y) * PilotPortraitGenerator.AtlasWidth + left + x) * 4 + 3];
                visible |= alpha > 16;
                anyAlpha |= alpha != 0;
            }
            Check(tile < 110 ? visible : !anyAlpha, "Real atlas tile " + tile + " violates the 110 used/18 reserved layout.");
            if (tile >= 78 && tile <= 85)
                for (int y = 0; y < PilotPortraitGenerator.Height; y++)
                for (int x = 0; x < PilotPortraitGenerator.Width; x++)
                    Check(atlas[AtlasOffset(tile, x, y) + 3] == 255, "Background atlas cells must be fully opaque.");
        }

        int eyesY = 120, mouthY = 170;
        if (manifestPath != null)
        {
            using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
            JsonElement landmarks = manifest.RootElement.GetProperty("landmarks");
            eyesY = landmarks.GetProperty("eyes_y").GetInt32();
            if (landmarks.TryGetProperty("mouth_y", out JsonElement mouth)) mouthY = mouth.GetInt32();
            Check(eyesY == 120 && mouthY == 170, "Manifest face anchors disagree with the compositor fit contract.");
            Registration(manifest.RootElement, atlas);
        }
        HeadsetFit(atlas);
        // The brown side-part must bridge the right temple; source gutter clipping left a background wedge here.
        foreach (int topY in new[] { 100, 108, 116 })
        foreach (int x in new[] { 181, 183, 185 })
            Check(atlas[AtlasOffset(16, x, PilotPortraitGenerator.Height - 1 - topY) + 3] >= 230,
                  "Brown side-part has a clipped temple gap at " + x + "," + topY);

        outputDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(outputDirectory);
        var cases = new List<object>();
        void Write(string group, int index, PortraitSelection selection, byte[] pixels = null)
        {
            pixels ??= PilotPortraitGenerator.ComposeLayers(selection, atlas);
            if (group == "variety")
                File.WriteAllBytes(Path.Combine(outputDirectory, "unfiltered-" + index.ToString("D3") + ".rgba"), pixels);
            pixels = PilotPortraitGenerator.FinishDisplay(pixels);
            Check(pixels.Length == PilotPortraitGenerator.Width * PilotPortraitGenerator.Height * 4, "Invalid output dimensions.");
            for (int i = 3; i < pixels.Length; i += 4) Check(pixels[i] == 255, "Real portrait contains a transparent output pixel.");
            string file = group + "-" + index.ToString("D3") + ".rgba";
            File.WriteAllBytes(Path.Combine(outputDirectory, file), pixels);
            cases.Add(new
            {
                group, file,
                label = PilotPortraitGenerator.BodyLabel(selection.Body) + " F" + selection.Face + " H" + selection.Hair +
                        " / " + PilotPortraitGenerator.UniformLabel(selection.Uniform) + " / " +
                        PilotPortraitGenerator.AccessoryLabel(selection.Accessory),
                body = (int)selection.Body, face = selection.Face, hair = selection.Hair,
                uniform = selection.Uniform, accessory = selection.Accessory, backdrop = selection.Backdrop,
                background = PilotPortraitGenerator.BackdropLabel(selection.Backdrop),
            });
        }

        int[] outfits = { 0, 1, 4, 5, 2, 3, 6, 7 };
        int[] equipment = { 0, 4, 6, 1, 5, 7, 3, 2 };
        for (int i = 0; i < 16; i++)
            Write("variety", i, new PortraitSelection((PortraitBody)(i / 8), i % 8, i % 8 + 1, outfits[i % 8], equipment[i % 8], i % 8));

        int[,] uniforms = { { 0, 2 }, { 1, 3 }, { 4, 6 }, { 5, 7 } };
        for (int body = 0; body < 2; body++)
        for (int faction = 0; faction < 2; faction++)
        {
            byte[] baseline = null;
            for (int role = 0; role < 4; role++)
            {
                var selection = new PortraitSelection((PortraitBody)body, body == 0 ? 0 : 4,
                    body == 0 ? 1 : 5, uniforms[role, faction], 0, 0);
                byte[] pixels = PilotPortraitGenerator.ComposeLayers(selection, atlas);
                if (baseline == null) baseline = pixels;
                else
                {
                    RegionEqual(baseline, pixels, 88, eyesY - 7, 168, eyesY + 7);
                    RegionEqual(baseline, pixels, 108, mouthY - 7, 148, mouthY + 7);
                    Check(!baseline.AsSpan().SequenceEqual(pixels),
                          "Real role outfits have identical visible clothing.");
                }
                Write("roles", body * 8 + faction * 4 + role, selection, pixels);
            }
        }

        for (int body = 0; body < 2; body++)
        for (int face = 0; face < PilotPortraitGenerator.FacesPerBody; face++)
        for (int hair = 0; hair < PilotPortraitGenerator.HairCount; hair++)
            Write("coverage", body * 72 + face * 9 + hair, new PortraitSelection((PortraitBody)body, face, hair, 0, 0, 0));

        for (int body = 0; body < 2; body++)
        for (int face = 0; face < PilotPortraitGenerator.FacesPerBody; face++)
        for (int hair = 0; hair < PilotPortraitGenerator.HairCount; hair++)
            Write("headset-fit", body * 72 + face * 9 + hair, new PortraitSelection((PortraitBody)body, face, hair, 0, 2, 0));

        for (int body = 0; body < 2; body++)
        for (int uniform = 8; uniform < PilotPortraitGenerator.UniformCount; uniform++)
        for (int gear = 0; gear < 2; gear++)
            Write("uniform-variety", (body * 6 + uniform - 8) * 2 + gear,
                new PortraitSelection((PortraitBody)body, uniform - 8, uniform - 7, uniform, gear == 0 ? 5 : 2, (uniform - 8) % 8));

        for (int body = 0; body < 2; body++)
        for (int face = 0; face < PilotPortraitGenerator.FacesPerBody; face++)
        for (int uniform = 0; uniform < PilotPortraitGenerator.UniformCount; uniform++)
        {
            int index = uniform < 8 ? body * 64 + face * 8 + uniform :
                128 + (body * 8 + face) * (PilotPortraitGenerator.UniformCount - 8) + uniform - 8;
            Write("fit", index, new PortraitSelection((PortraitBody)body, face, 0, uniform, 0, 0));
        }

        for (int backdrop = 0; backdrop < PilotPortraitGenerator.BackdropCount; backdrop++)
            Write("backgrounds", backdrop, new PortraitSelection(PortraitBody.Male, 0, 1, 0, 0, backdrop));

        int[] caps = { 3, 4, 7 };
        for (int body = 0; body < 2; body++)
        for (int face = 0; face < PilotPortraitGenerator.FacesPerBody; face++)
        for (int hair = 0; hair < PilotPortraitGenerator.HairCount; hair++)
        for (int cap = 0; cap < caps.Length; cap++)
        for (int faction = 0; faction < 2; faction++)
        {
            int uniform = caps[cap] == 3 ? (faction == 0 ? 4 : 6) : (faction == 0 ? 1 : 3);
            int index = ((((body * 8 + face) * 9 + hair) * 3 + cap) * 2 + faction);
            Write("cap-fit", index, new PortraitSelection((PortraitBody)body, face, hair, uniform, caps[cap], 0));
        }
        int[] otherEquipment = { 1, 2, 5, 6 };
        for (int body = 0; body < 2; body++)
        for (int face = 0; face < PilotPortraitGenerator.FacesPerBody; face++)
        for (int gear = 0; gear < otherEquipment.Length; gear++)
        for (int faction = 0; faction < 2; faction++)
        {
            int accessory = otherEquipment[gear];
            int uniform = accessory == 1 ? (faction == 0 ? 5 : 7) : accessory == 6 ? (faction == 0 ? 4 : 6) : (faction == 0 ? 0 : 2);
            Write("equipment-fit", (((body * 8 + face) * 4 + gear) * 2 + faction),
                new PortraitSelection((PortraitBody)body, face, face + 1, uniform, accessory, 0));
        }

        // Sample registered eye-line and mouth landmarks with no gear/hair; retain the source's near-opaque alpha.
        for (int body = 0; body < 2; body++)
        for (int face = 0; face < PilotPortraitGenerator.FacesPerBody; face++)
        {
            var selection = new PortraitSelection((PortraitBody)body, face, 0, 0, 0, 0);
            byte[] pixels = PilotPortraitGenerator.ComposeLayers(selection, atlas);
            foreach (int topDownY in new[] { eyesY, mouthY })
            {
                int y = PilotPortraitGenerator.Height - 1 - topDownY;
                int source = AtlasOffset(body * 8 + face, 128, y);
                int alpha = atlas[source + 3], target = (y * PilotPortraitGenerator.Width + 128) * 4;
                int background = AtlasOffset(78, 128, y);
                Check(alpha >= 240, "Face landmark is not near-opaque: body " + body + ", face " + face);
                for (int c = 0; c < 3; c++)
                    Check(pixels[target + c] == (atlas[source + c] * alpha + atlas[background + c] * (255 - alpha) + 127) / 255,
                          "Registered face landmark RGB/alpha changed during compositing.");
            }
        }

        var gearlessAtlas = (byte[])atlas.Clone();
        for (int tile = 48; tile <= 61; tile++)
        for (int y = 0; y < PilotPortraitGenerator.Height; y++)
        for (int x = 0; x < PilotPortraitGenerator.Width; x++) gearlessAtlas[AtlasOffset(tile, x, y) + 3] = 0;
        for (int body = 0; body < 2; body++)
        for (int faction = 0; faction < 2; faction++)
        for (int accessory = 0; accessory < PilotPortraitGenerator.AccessoryCount; accessory++)
        {
            int face = body == 0 ? 0 : 4, hair = body == 0 ? 1 : 5;
            var selection = new PortraitSelection((PortraitBody)body, face, hair, faction == 0 ? 0 : 2, accessory, 0);
            byte[] pixels = PilotPortraitGenerator.ComposeLayers(selection, atlas);
            if (accessory != 0)
            {
                byte[] baseline = PilotPortraitGenerator.ComposeLayers(selection, gearlessAtlas);
                Check(!baseline.AsSpan().SequenceEqual(pixels), "Accessory " + accessory + " has no visible effect.");
                int tile = PilotPortraitGenerator.Resolve(selection).AccessoryTile;
                bool sampleFound = false;
                for (int y = 0; y < PilotPortraitGenerator.Height && !sampleFound; y++)
                for (int x = 0; x < PilotPortraitGenerator.Width && !sampleFound; x++)
                {
                    int source = AtlasOffset(tile, x, y), target = (y * PilotPortraitGenerator.Width + x) * 4;
                    int alpha = atlas[source + 3];
                    if (alpha <= 16) continue;
                    // A cap's empty clone loses its contour; only sample where removing it cannot change hair clipping.
                    int hairTile = PilotPortraitGenerator.Resolve(selection).HairTile;
                    if ((accessory == 3 || accessory == 4 || accessory == 7) && hairTile >= 0 && atlas[AtlasOffset(hairTile, x, y) + 3] != 0) continue;
                    for (int c = 0; c < 3; c++)
                        Check(pixels[target + c] == (atlas[source + c] * alpha + baseline[target + c] * (255 - alpha) + 127) / 255,
                              "Real equipment overlay does not match its registered source pixel.");
                    sampleFound = true;
                }
                Check(sampleFound, "Equipment layer has no sampleable pixels.");
            }
            Write("gear", body * 16 + faction * 8 + accessory, selection, pixels);
        }

        File.WriteAllText(Path.Combine(outputDirectory, "cases.json"), JsonSerializer.Serialize(new
        {
            width = PilotPortraitGenerator.Width, height = PilotPortraitGenerator.Height,
            format = "bottom-up RGBA", displayFinish = "0.5px soft focus; static 1px scanline every 4px at 14.1% darkness", cases,
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Real atlas passed: 110 nonempty/18 reserved tiles, " +
                          (manifestPath == null ? "" : "neckline coverage, neck contours and shoulder fit, ") +
                          "facial ROI invariance, headset eyes/ears/crown fit, landmarks and equipment pixels; " +
                          cases.Count + " portraits written to " + outputDirectory);
    }

    private static void HeadsetFit(byte[] atlas)
    {
        int Alpha(int tile, int x, int topY) => atlas[AtlasOffset(tile, x, PilotPortraitGenerator.Height - 1 - topY) + 3];
        foreach (int tile in new[] { 49, 56 })
        {
            for (int y = 113; y <= 126; y++)
            foreach (int eye in new[] { 88, 146 })
            for (int x = eye; x <= eye + 21; x++)
                Check(Alpha(tile, x, y) < 16, "Headset padding occludes an eye: tile " + tile + ", pixel " + x + "," + y);
            int left = 128, right = 128;
            while (left > 0 && Alpha(tile, left, 120) < 128) left--;
            while (right < 255 && Alpha(tile, right, 120) < 128) right++;
            Check(left <= 80 && right >= 176, "Headset inner ear pads crowd the face: tile " + tile + ", gap " + (right - left - 1));
            foreach (int ear in new[] { 48, 174 })
            {
                int nearOpaque = 0;
                for (int x = ear; x <= ear + 34; x++) if (Alpha(tile, x, 136) >= 230) nearOpaque++;
                Check(nearOpaque >= 10, "Headset cup misses the registered ear row: tile " + tile);
            }
            // The isolated capsule region excludes the ear cup and must sit beside the mouth, not the jaw.
            int micPixels = 0, micAlpha = 0, micWeightedY = 0;
            for (int y = 150; y <= 210; y++)
            for (int x = 146; x <= 169; x++)
            {
                int alpha = Alpha(tile, x, y);
                if (alpha < 128) continue;
                micPixels++;
                micAlpha += alpha;
                micWeightedY += y * alpha;
            }
            Check(micPixels >= 64, "Headset microphone capsule is missing: tile " + tile);
            double micY = micWeightedY / (double)micAlpha;
            Check(micY >= 168 && micY <= 182,
                  "Headset microphone misses mouth height: tile " + tile + ", capsule centroid " + micY);
            int bandBottom = -1;
            for (int y = 0; y < 80; y++)
            for (int x = 123; x <= 133; x++) if (Alpha(tile, x, y) >= 128) bandBottom = Math.Max(bandBottom, y);
            for (int face = 0; face < 16; face++)
            {
                int crown = PilotPortraitGenerator.Height;
                for (int y = 0; y < 80; y++)
                for (int x = 123; x <= 133; x++) if (Alpha(face, x, y) >= 230) crown = Math.Min(crown, y);
                Check(bandBottom >= 0 && Math.Abs(bandBottom - crown) <= 6,
                      "Headset band floats above or sinks into a bald crown: tile " + tile + ", face " + face +
                      ", band underside " + bandBottom + ", scalp " + crown);
                var selection = new PortraitSelection((PortraitBody)(face / 8), face % 8, 0, tile == 49 ? 0 : 2, 2, 0);
                byte[] fitted = PilotPortraitGenerator.ComposeLayers(selection, atlas);
                byte[] bare = PilotPortraitGenerator.ComposeLayers(new PortraitSelection(selection.Body, selection.Face, 0, selection.Uniform, 0, 0), atlas);
                int visibleBottom = -1;
                for (int y = 0; y < 80; y++)
                for (int x = 123; x <= 133; x++)
                {
                    int p = ((PilotPortraitGenerator.Height - 1 - y) * PilotPortraitGenerator.Width + x) * 4;
                    for (int c = 0; c < 3; c++)
                        if (Math.Abs(fitted[p + c] - bare[p + c]) >= 16) visibleBottom = Math.Max(visibleBottom, y);
                }
                Check(Math.Abs(visibleBottom - crown) <= 1,
                      "Composed headset leaves a bald scalp gap: tile " + tile + ", face " + face +
                      ", visible underside " + visibleBottom + ", scalp " + crown);
                // Ear pads and microphone below the join must retain their original source positions and single blend.
                for (int y = 95; y < PilotPortraitGenerator.Height; y++)
                for (int x = 0; x < PilotPortraitGenerator.Width; x++)
                {
                    int source = AtlasOffset(tile, x, PilotPortraitGenerator.Height - 1 - y);
                    int p = ((PilotPortraitGenerator.Height - 1 - y) * PilotPortraitGenerator.Width + x) * 4;
                    int alpha = atlas[source + 3];
                    for (int c = 0; c < 3; c++)
                        Check(fitted[p + c] == (atlas[source + c] * alpha + bare[p + c] * (255 - alpha) + 127) / 255,
                              "Bald headset fit moved its ear pads/microphone or blended equipment twice.");
                }
            }
        }
    }

    private static void IdentityAndRoles()
    {
        int[][] allowedUniforms = { new[] { 0, 8, 9 }, new[] { 2, 10, 11 }, new[] { 1, 12 }, new[] { 3, 13 },
                                  new[] { 4 }, new[] { 6 }, new[] { 5 }, new[] { 7 } };
        int[][] allowedEquipment = { new[] { 0, 1, 2, 5 }, new[] { 0, 1, 4, 7 }, new[] { 0, 2, 3, 6 }, new[] { 0, 1, 2, 3 } };
        var facesSeen = new bool[2, PilotPortraitGenerator.FacesPerBody];
        var uniformsSeen = new bool[PilotPortraitGenerator.UniformCount];
        var equipmentSeen = new bool[4, PilotPortraitGenerator.AccessoryCount];
        for (int i = 0; i < 256; i++)
        {
            string identity = "PERSON|" + i;
            PortraitSelection original = PilotPortraitGenerator.Select(identity);
            Check(original == PilotPortraitGenerator.Select(identity), "Identity selection changed between calls.");
            Check(Array.IndexOf(allowedUniforms[0], original.Uniform) >= 0 || Array.IndexOf(allowedUniforms[1], original.Uniform) >= 0,
                  "Automatic pilots selected another role's uniform.");
            facesSeen[(int)original.Body, original.Face] = true;
            for (int role = 0; role < 4; role++)
            for (int faction = 0; faction < 2; faction++)
            {
                PortraitSelection service = PilotPortraitGenerator.Select(identity, (PortraitRole)role, faction);
                Check(service.Body == original.Body && service.Face == original.Face && service.Hair == original.Hair &&
                      service.Backdrop == original.Backdrop, "Role/faction changed identity traits.");
                Check(Array.IndexOf(allowedUniforms[role * 2 + faction], service.Uniform) >= 0, "Role/faction mapped to the wrong uniform.");
                uniformsSeen[service.Uniform] = true;
                Check(Array.IndexOf(allowedEquipment[role], service.Accessory) >= 0, "Automatic role selected incompatible equipment.");
                Check(service.Accessory == PilotPortraitGenerator.Select(identity, (PortraitRole)role, 1 - faction).Accessory,
                      "Faction changed the equipment selector.");
                equipmentSeen[role, service.Accessory] = true;
            }
            Check(PilotPortraitGenerator.Select(identity, (PortraitRole)999) == original, "Unknown role must fall back to pilot.");
            Check(PilotPortraitGenerator.Select(identity, PortraitRole.Pilot, 999) == original, "Unknown faction must use identity faction.");
        }
        foreach (bool seen in facesSeen) Check(seen, "A body-specific face is excluded from automatic portraits.");
        foreach (bool seen in uniformsSeen) Check(seen, "A role-compatible uniform is excluded from automatic portraits.");
        for (int role = 0; role < 4; role++)
        foreach (int accessory in allowedEquipment[role]) Check(equipmentSeen[role, accessory], "A compatible equipment choice is excluded.");
        Check(PilotPortraitGenerator.Select(null) == PilotPortraitGenerator.Select("WingCommand"), "Null identity fallback changed.");
    }

    private static void Registration(JsonElement manifest, byte[] atlas)
    {
        var faces = new Dictionary<(int body, int selector), JsonElement>();
        var outfits = new Dictionary<(int body, int selector), JsonElement>();
        foreach (JsonElement tile in manifest.GetProperty("tiles").EnumerateArray())
        {
            string family = tile.GetProperty("family").GetString();
            if (family != "faces" && family != "uniforms") continue;
            string bodyName = tile.GetProperty("body").GetString();
            Check(bodyName == "male" || bodyName == "female", "Registered face/outfit has an unknown body.");
            var key = (bodyName == "female" ? 1 : 0, tile.GetProperty("selector").GetInt32());
            (family == "faces" ? faces : outfits).Add(key, tile);
        }
        Check(faces.Count == 16 && outfits.Count == 2 * PilotPortraitGenerator.UniformCount, "Manifest is missing anatomical face/outfit records.");
        for (int body = 0; body < 2; body++)
        for (int face = 0; face < PilotPortraitGenerator.FacesPerBody; face++)
        for (int uniform = 0; uniform < PilotPortraitGenerator.UniformCount; uniform++)
        {
            JsonElement head = faces[(body, face)].GetProperty("anatomy");
            JsonElement outfit = outfits[(body, uniform)].GetProperty("anatomy");
            int chin = head.GetProperty("chin_y").GetInt32();
            int seat = outfit.GetProperty("neck_seat_y").GetInt32();
            int opening = outfit.GetProperty("neck_opening_width").GetInt32();
            int openingY = outfit.GetProperty("opening_sample_y").GetInt32();
            ResolvedPortraitParts parts = PilotPortraitGenerator.Resolve(new PortraitSelection((PortraitBody)body, face, 0, uniform, 0, 0));
            Check(head.GetProperty("eyes_y").GetInt32() == 120 && seat - chin >= 12 && seat - chin <= 38,
                  "Head/collar registration gives an implausible exposed-neck length.");
            Check(head.GetProperty("neck_base_y").GetInt32() >= seat && outfit.GetProperty("shoulder_width").GetInt32() >= 210,
                  "Face neck does not meet its collar or shoulders are too narrow for the head.");
            (int left, int right) Span(int topY)
            {
                int row = PilotPortraitGenerator.Height - 1 - topY, left = PilotPortraitGenerator.Width, right = -1;
                for (int x = 0; x < PilotPortraitGenerator.Width; x++)
                    if (atlas[AtlasOffset(body * 8 + face, x, row) + 3] >= 230) { left = Math.Min(left, x); right = x; }
                return (left, right);
            }
            int neckSampleY = head.GetProperty("neck_sample_y").GetInt32();
            var neck = Span(neckSampleY);
            int headWidth = manifest.GetProperty("landmarks").GetProperty("head_width").GetInt32();
            Check(neckSampleY == chin + 14 && neck.right >= neck.left && neck.right - neck.left + 1 >= 50 &&
                  neck.right - neck.left + 1 <= headWidth * 82 / 100 && Math.Abs(neck.left + neck.right - 255) <= 6,
                  "Actual upper neck span is implausible or misses its center: body " + body + ", face " + face +
                  ", row " + neckSampleY + ", width " + (neck.right - neck.left + 1));
            var previous = Span(chin + 1);
            for (int topY = chin + 2; topY <= neckSampleY; topY++)
            {
                var current = Span(topY);
                Check(current.right >= current.left && Math.Abs(current.left - previous.left) <= 6 && Math.Abs(current.right - previous.right) <= 6,
                      "Visible neck silhouette has an abrupt ledge: body " + body + ", face " + face + ", row " + topY);
                previous = current;
            }
            var neckBase = Span(head.GetProperty("neck_base_y").GetInt32());
            Check(neckBase.right < neckBase.left || neckBase.right - neckBase.left + 1 <= head.GetProperty("neck_width").GetInt32() + 1,
                  "Skin extends outside the registered neck base mask.");
            Check(opening > 0 && opening <= 100, "Registered collar aperture width is implausible.");
            // Check each exposed-neck row; an aperture above the jaw can still have gaps farther down.
            for (int topY = Math.Max(openingY, chin + 3); topY <= seat; topY++)
            {
                int row = PilotPortraitGenerator.Height - 1 - topY;
                int left = 128, right = 128;
                while (left > 0 && atlas[AtlasOffset(parts.UniformTile, left, row) + 3] < 128) left--;
                while (right < 255 && atlas[AtlasOffset(parts.UniformTile, right, row) + 3] < 128) right++;
                for (int x = left + 1; x < right; x++)
                {
                    int faceAlpha = atlas[AtlasOffset(body * 8 + face, x, row) + 3];
                    int bodyAlpha = atlas[AtlasOffset(parts.UniformTile, x, row) + 3];
                    int collarAlpha = atlas[AtlasOffset(parts.FrontCollarTile, x, row) + 3];
                    int coverage = bodyAlpha + (faceAlpha * (255 - bodyAlpha) + 127) / 255;
                    coverage += (collarAlpha * (255 - coverage) + 127) / 255;
                    Check(coverage >= 230, "Same-row neckline aperture exposes background: body " + body + ", face " + face +
                          ", outfit " + uniform + ", pixel " + x + "," + topY + ", face/body/collar alpha " +
                          faceAlpha + "/" + bodyAlpha + "/" + collarAlpha + ", coverage " + coverage);
                }
            }
            for (int topY = chin + 4; topY <= seat + 4; topY++)
            foreach (int x in new[] { 125, 128, 131 })
            {
                int y = PilotPortraitGenerator.Height - 1 - topY;
                int headAlpha = atlas[AtlasOffset(body * 8 + face, x, y) + 3];
                int bodyAlpha = atlas[AtlasOffset(parts.UniformTile, x, y) + 3];
                int collarAlpha = atlas[AtlasOffset(parts.FrontCollarTile, x, y) + 3];
                int coverage = bodyAlpha + (headAlpha * (255 - bodyAlpha) + 127) / 255;
                coverage += (collarAlpha * (255 - coverage) + 127) / 255;
                Check(coverage >= 230, "Registered head/collar seam has an open central gap: body " + body + ", face " + face +
                      ", outfit " + uniform + ", pixel " + x + "," + topY + ", coverage " + coverage);
            }
        }
    }

    private static void SemanticSelectors()
    {
        for (int body = 0; body < 2; body++)
        for (int face = 0; face < PilotPortraitGenerator.FacesPerBody; face++)
        for (int hair = 0; hair < PilotPortraitGenerator.HairCount; hair++)
        for (int uniform = 0; uniform < PilotPortraitGenerator.UniformCount; uniform++)
        for (int accessory = 0; accessory < PilotPortraitGenerator.AccessoryCount; accessory++)
        {
            var selection = new PortraitSelection((PortraitBody)body, face, hair, uniform, accessory, 3);
            Check(PilotPortraitGenerator.Normalize(selection) == selection, "Valid semantic selectors changed, including v2 saves.");
            ResolvedPortraitParts parts = PilotPortraitGenerator.Resolve(selection);
            Check(parts.FaceTile == body * 8 + face, "Face atlas address changed.");
            Check(parts.HairTile == (hair == 0 || accessory == 5 || accessory == 6 ? -1 : 16 + body * 8 + hair - 1), "Hair atlas address changed.");
            int outfitTile = uniform < 8 ? 32 + body * 8 + uniform : 86 + body * 6 + uniform - 8;
            int collarTile = uniform < 8 ? 62 + body * 8 + uniform : 98 + body * 6 + uniform - 8;
            Check(parts.UniformTile == outfitTile && parts.UniformTile < PilotPortraitGenerator.AtlasTileCount,
                  "Uniform atlas address left the atlas.");
            Check(parts.FrontCollarTile == collarTile && parts.BackdropTile == 81,
                  "Front collar or saved backdrop atlas address changed.");
            bool pala = uniform == 2 || uniform == 3 || uniform == 6 || uniform == 7 || uniform == 10 || uniform == 11 || uniform == 13;
            Check(parts.AccessoryTile == (accessory == 0 ? -1 : (pala ? 55 : 48) + accessory - 1), "Faction equipment address changed.");
        }
        PortraitSelection legacy = PilotPortraitGenerator.FromLegacySelection(4, 11, 17, 2);
        Check(legacy == new PortraitSelection(PortraitBody.Female, 1, 2, 1, 0, 2), "Legacy resolved selectors stopped migrating.");
        PortraitSelection bounded = PilotPortraitGenerator.Normalize(new PortraitSelection((PortraitBody)99, 99, 99, 99, 99, 99));
        Check(bounded == new PortraitSelection(PortraitBody.Male, 7, 8, 13, 7, 7), "Out-of-range selectors escaped normalization.");
        Check(PilotPortraitGenerator.DefaultSelection.Accessory == 0 && PilotPortraitGenerator.AccessoryLabel(7) == "BERET",
              "Default equipment or public studio labels changed.");
        for (int backdrop = 0; backdrop < PilotPortraitGenerator.BackdropCount; backdrop++)
            Check(PilotPortraitGenerator.Resolve(new PortraitSelection(PortraitBody.Male, 0, 0, 0, 0, backdrop)).BackdropTile == 78 + backdrop,
                  "Background selector left its eight atlas cells.");
        Check(PilotPortraitGenerator.BackdropLabel(7) == "NIGHT AIRFIELD", "Background studio label changed.");
    }

    private static void Composition()
    {
        var atlas = new byte[PilotPortraitGenerator.AtlasWidth * PilotPortraitGenerator.AtlasHeight * 4];
        int frontY = PilotPortraitGenerator.Height - 1 - 110;
        int crownY = PilotPortraitGenerator.Height - 1 - 70;
        int rearY = PilotPortraitGenerator.Height - 1 - 180;
        Put(atlas, 78, 23, frontY, 30, 40, 50, 255);
        Put(atlas, 0, 23, frontY, 220, 100, 60, 255);
        Put(atlas, 32, 23, frontY, 20, 80, 160, 255);
        Put(atlas, 62, 23, frontY, 20, 80, 160, 128);
        Put(atlas, 0, 24, frontY, 220, 100, 60, 255);
        Put(atlas, 62, 24, frontY, 20, 80, 160, 128);
        Put(atlas, 16, 24, frontY, 5, 7, 9, 255);
        Put(atlas, 0, 26, crownY, 220, 100, 60, 255);
        Put(atlas, 16, 26, crownY, 5, 7, 9, 255);
        Put(atlas, 16, 27, rearY, 5, 7, 9, 255);
        Put(atlas, 32, 27, rearY, 20, 80, 160, 255);
        Put(atlas, 16, 28, rearY, 5, 7, 9, 255);
        Put(atlas, 0, 28, rearY, 220, 100, 60, 255);
        Put(atlas, 62, 28, rearY, 90, 80, 70, 255);
        Put(atlas, 16, 30, rearY, 5, 7, 9, 255);
        Put(atlas, 78, 31, crownY, 100, 150, 200, 255);
        Put(atlas, 16, 31, crownY, 20, 40, 60, 128);
        Put(atlas, 78, 32, rearY, 100, 150, 200, 255);
        Put(atlas, 16, 32, rearY, 20, 40, 60, 128);
        Put(atlas, 0, 10, 0, 1, 2, 3, 255);
        Put(atlas, 0, 10, PilotPortraitGenerator.Height - 1, 200, 210, 220, 255);
        byte[] composed = PilotPortraitGenerator.ComposeLayers(new PortraitSelection(PortraitBody.Male, 0, 1, 0, 0, 0), atlas);
        Pixel(composed, 23, frontY, 120, 90, 110);
        Pixel(composed, 24, frontY, 13, 44, 85);
        Pixel(composed, 26, crownY, 5, 7, 9);
        Pixel(composed, 27, rearY, 5, 7, 9);
        Pixel(composed, 28, rearY, 90, 80, 70);
        Pixel(composed, 31, crownY, 60, 95, 130);
        Pixel(composed, 32, rearY, 60, 95, 130);
        Pixel(composed, 10, 0, 1, 2, 3);
        Pixel(composed, 10, PilotPortraitGenerator.Height - 1, 200, 210, 220);
        Check(composed[3] == 255, "Transparent cells did not retain an opaque backdrop.");
        Put(atlas, 48, 24, frontY, 90, 80, 70, 255);
        byte[] glasses = PilotPortraitGenerator.ComposeLayers(new PortraitSelection(PortraitBody.Male, 0, 1, 0, 1, 0), atlas);
        Pixel(glasses, 24, frontY, 90, 80, 70);
        byte[] emptyCap = PilotPortraitGenerator.ComposeLayers(new PortraitSelection(PortraitBody.Male, 0, 1, 0, 4, 0), atlas);
        Pixel(emptyCap, 26, crownY, 5, 7, 9);
        Put(atlas, 50, 25, frontY, 10, 20, 30, 255);
        byte[] cap = PilotPortraitGenerator.ComposeLayers(new PortraitSelection(PortraitBody.Male, 0, 1, 0, 3, 0), atlas);
        Pixel(cap, 24, frontY, 13, 44, 85);
        Pixel(cap, 25, frontY, 10, 20, 30);
        Pixel(cap, 26, crownY, 220, 100, 60);
        Pixel(cap, 30, rearY, 5, 7, 9);
        byte[] helmet = PilotPortraitGenerator.ComposeLayers(new PortraitSelection(PortraitBody.Male, 0, 1, 0, 5, 0), atlas);
        Pixel(helmet, 24, frontY, 120, 90, 110);
        Pixel(helmet, 26, crownY, 220, 100, 60);
        Pixel(helmet, 30, rearY, 0, 0, 0);
        // Tilted cap underside: left brim ends at90, right at110; temples follow those contours, not a horizontal cut.
        int leftTemple = PilotPortraitGenerator.Height - 1 - 95;
        int rightTemple = PilotPortraitGenerator.Height - 1 - 111;
        int overlap = PilotPortraitGenerator.Height - 1 - 89;
        Put(atlas, 50, 40, PilotPortraitGenerator.Height - 1 - 90, 10, 20, 30, 255);
        Put(atlas, 50, 41, PilotPortraitGenerator.Height - 1 - 110, 10, 20, 30, 255);
        Put(atlas, 50, 40, overlap, 10, 20, 30, 128);
        Put(atlas, 16, 40, leftTemple, 5, 7, 9, 255);
        Put(atlas, 16, 41, leftTemple, 5, 7, 9, 255);
        Put(atlas, 16, 41, rightTemple, 5, 7, 9, 255);
        Put(atlas, 16, 40, overlap, 5, 7, 9, 255);
        Put(atlas, 16, 39, overlap, 5, 7, 9, 255);
        Put(atlas, 16, 42, leftTemple, 5, 7, 9, 255);
        byte[] tilted = PilotPortraitGenerator.ComposeLayers(new PortraitSelection(PortraitBody.Male, 0, 1, 0, 3, 0), atlas);
        Pixel(tilted, 40, leftTemple, 5, 7, 9);
        Pixel(tilted, 41, leftTemple, 0, 0, 0);
        Pixel(tilted, 41, rightTemple, 5, 7, 9);
        Pixel(tilted, 40, overlap, 8, 14, 20);
        Pixel(tilted, 39, overlap, 5, 7, 9);
        Pixel(tilted, 42, leftTemple, 0, 0, 0);
        // Fit a bald scalp down and up, using partial alpha so a duplicated equipment pass fails.
        int lowCrown = PilotPortraitGenerator.Height - 1 - 48;
        int highCrown = PilotPortraitGenerator.Height - 1 - 39;
        int band = PilotPortraitGenerator.Height - 1 - 43;
        int ear = PilotPortraitGenerator.Height - 1 - 136;
        Put(atlas, 0, 128, lowCrown, 200, 160, 120, 255);
        Put(atlas, 8, 128, highCrown, 200, 160, 120, 255);
        Put(atlas, 49, 128, band, 20, 40, 60, 128);
        Put(atlas, 56, 128, band, 20, 40, 60, 128);
        Put(atlas, 49, 72, ear, 9, 8, 7, 255);
        byte[] bald = PilotPortraitGenerator.ComposeLayers(new PortraitSelection(PortraitBody.Male, 0, 0, 0, 2, 0), atlas);
        Pixel(bald, 128, lowCrown, 110, 100, 90);
        Pixel(bald, 72, ear, 9, 8, 7);
        byte[] high = PilotPortraitGenerator.ComposeLayers(new PortraitSelection(PortraitBody.Female, 0, 0, 2, 2, 0), atlas);
        Pixel(high, 128, highCrown, 110, 100, 90);
        Pixel(high, 128, PilotPortraitGenerator.Height - 1, 0, 0, 0);
        byte[] haired = PilotPortraitGenerator.ComposeLayers(new PortraitSelection(PortraitBody.Male, 0, 1, 0, 2, 0), atlas);
        Pixel(haired, 128, band, 10, 20, 30);
        Pixel(haired, 72, ear, 9, 8, 7);
        try
        {
            PilotPortraitGenerator.ComposeLayers(PilotPortraitGenerator.DefaultSelection, new byte[4]);
            throw new InvalidOperationException("Invalid atlas dimensions were accepted.");
        }
        catch (ArgumentException) { }
    }

    private static void DisplayFinish()
    {
        var flat = new byte[PilotPortraitGenerator.Width * PilotPortraitGenerator.Height * 4];
        for (int p = 0; p < flat.Length; p += 4)
        {
            flat[p] = 120; flat[p + 1] = 160; flat[p + 2] = 200; flat[p + 3] = 255;
        }
        byte[] finished = PilotPortraitGenerator.FinishDisplay(flat);
        Check(!ReferenceEquals(flat, finished), "Display finish must not overwrite raw anatomical evidence.");
        Pixel(flat, 128, PilotPortraitGenerator.Height - 1 - 2, 120, 160, 200);
        Pixel(finished, 128, PilotPortraitGenerator.Height - 1, 120, 160, 200);
        Pixel(finished, 128, PilotPortraitGenerator.Height - 1 - 2, 103, 138, 172);
        Pixel(finished, 128, PilotPortraitGenerator.Height - 1 - 6, 103, 138, 172);
        Check(finished.AsSpan().SequenceEqual(PilotPortraitGenerator.FinishDisplay(flat)),
              "Static scanline phase must be deterministic.");
        Array.Clear(flat);
        for (int p = 3; p < flat.Length; p += 4) flat[p] = 255;
        flat[0] = flat[1] = flat[2] = 255;
        finished = PilotPortraitGenerator.FinishDisplay(flat);
        Check(finished[0] < 255 && finished[4] > 0, "Soft focus must soften a one-pixel edge.");
        Pixel(finished, PilotPortraitGenerator.Width - 1, 0, 0, 0, 0);
        Pixel(finished, 0, PilotPortraitGenerator.Height - 1, 0, 0, 0);
        for (int p = 3; p < finished.Length; p += 4) Check(finished[p] == 255, "Display finish created transparency.");
        var atlas = new byte[PilotPortraitGenerator.AtlasWidth * PilotPortraitGenerator.AtlasHeight * 4];
        Put(atlas, 78, 128, PilotPortraitGenerator.Height - 1 - 8, 120, 160, 200, 255);
        byte[] raw = PilotPortraitGenerator.ComposeLayers(PilotPortraitGenerator.DefaultSelection, atlas);
        Check(PilotPortraitGenerator.Compose(PilotPortraitGenerator.DefaultSelection, atlas).AsSpan().SequenceEqual(
            PilotPortraitGenerator.FinishDisplay(raw)), "Production portrait skipped or doubled its display finish.");
    }

    private static void Put(byte[] atlas, int tile, int x, int y, byte r, byte g, byte b, byte a)
    {
        int offset = AtlasOffset(tile, x, y);
        atlas[offset] = r; atlas[offset + 1] = g; atlas[offset + 2] = b; atlas[offset + 3] = a;
    }

    private static int AtlasOffset(int tile, int x, int y)
    {
        int left = tile % PilotPortraitGenerator.AtlasColumns * PilotPortraitGenerator.Width;
        int bottom = PilotPortraitGenerator.AtlasHeight - (tile / PilotPortraitGenerator.AtlasColumns + 1) * PilotPortraitGenerator.Height;
        return ((bottom + y) * PilotPortraitGenerator.AtlasWidth + left + x) * 4;
    }

    private static void Pixel(byte[] pixels, int x, int y, byte r, byte g, byte b)
    {
        int offset = (y * PilotPortraitGenerator.Width + x) * 4;
        Check(pixels[offset] == r && pixels[offset + 1] == g && pixels[offset + 2] == b && pixels[offset + 3] == 255,
              "RGBA orientation, layer order, alpha blending or source color preservation changed.");
    }

    private static void RegionEqual(byte[] first, byte[] second, int left, int top, int right, int bottom)
    {
        for (int topY = top; topY <= bottom; topY++)
        for (int x = left; x <= right; x++)
        {
            int offset = ((PilotPortraitGenerator.Height - 1 - topY) * PilotPortraitGenerator.Width + x) * 4;
            Check(first.AsSpan(offset, 4).SequenceEqual(second.AsSpan(offset, 4)),
                  "Role outfit occludes the registered eye/mouth region at " + x + "," + topY);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
