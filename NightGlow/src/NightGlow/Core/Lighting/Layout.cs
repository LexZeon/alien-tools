namespace NightGlow.Core.Lighting;

/// <summary>Areas the user can select as a whole, like a vendor control center does.</summary>
public enum RegionKind { Keyboard, Ring, Logo, Other, Touchpad }

public static class Regions
{
    public static readonly RegionKind[] All = { RegionKind.Keyboard, RegionKind.Ring, RegionKind.Logo, RegionKind.Touchpad, RegionKind.Other };

    public static string Name(RegionKind k) => k switch
    {
        RegionKind.Keyboard => "键盘",
        RegionKind.Ring => "背部灯环",
        RegionKind.Logo => "Logo",
        RegionKind.Touchpad => "触控板",
        _ => "其他灯光",
    };

    public static string Glyph(RegionKind k) => k switch
    {
        RegionKind.Keyboard => "",
        RegionKind.Ring => "",
        RegionKind.Logo => "",
        RegionKind.Touchpad => "",
        _ => "",
    };
}

/// <summary>Normalized rectangle on the canvas (0..1 on both axes, origin top-left).</summary>
public readonly record struct NRect(double X, double Y, double W, double H)
{
    public double Cx => X + W / 2;
    public double Cy => Y + H / 2;
}

/// <summary>
/// One addressable light placed on the shared canvas. <see cref="Along"/> is the position
/// along the region's own path (0..1), used for effects that travel around the ring.
/// </summary>
public sealed record LayoutPoint(
    LightDevice Device, ushort LightId, RegionKind Region,
    double X, double Y, double W, double H, double Along, string Label)
{
    public const ushort WholeDevice = 0xFFFF;
    public bool IsWholeDevice => LightId == WholeDevice;
}

public sealed class LightRegion
{
    public required RegionKind Kind { get; init; }
    public string Name => Regions.Name(Kind);
    public List<LayoutPoint> Points { get; } = new();
    public bool IsWholeDevice => Points.Count == 1 && Points[0].IsWholeDevice;
}

/// <summary>
/// Places every known light on one canvas so that effects can be computed from position:
/// a wave runs across the keyboard and the ring with the same rhythm, a ripple spreads
/// from where the user clicked. The canvas is a schematic top view: rear ring at the top,
/// keyboard in the middle, other lights below.
/// </summary>
public sealed class SpatialLayout
{
    /// <summary>Canvas width / height. Distances are measured with x scaled by this.</summary>
    public const double Aspect = 1.6;

    public static readonly NRect RingFrame = new(0.17, 0.035, 0.66, 0.17);
    public static readonly NRect LogoFrame = new(0.46, 0.07, 0.08, 0.095);
    public static readonly NRect KeyboardFrame = new(0.065, 0.35, 0.87, 0.435);
    public static readonly NRect TouchpadFrame = new(0.40, 0.83, 0.20, 0.13);
    public static readonly NRect OtherFrame = new(0.74, 0.86, 0.18, 0.08);

    public static NRect FrameOf(RegionKind k) => k switch
    {
        RegionKind.Keyboard => KeyboardFrame,
        RegionKind.Ring => RingFrame,
        RegionKind.Logo => LogoFrame,
        RegionKind.Touchpad => TouchpadFrame,
        _ => OtherFrame,
    };

    /// <summary>
    /// Chassis lights of the Alienware x16 R2 confirmed on hardware: id 0 is on the rear ring,
    /// id 62 is the touchpad corner light. The vendor app's own LED numbers do not match the
    /// hardware ids (its "061" logo is in the 64..95 range), so nothing else is guessed;
    /// the identification wizard places the rest. Used only for ids the user has not identified.
    /// </summary>
    private static readonly (ushort Id, RegionKind Kind, int Order, string Name)[] X16R2Chassis =
    {
        (0, RegionKind.Ring, 0, "灯环 0"), (62, RegionKind.Touchpad, 0, "触控板"),
    };

    /// <summary>Fallback grid for unknown per-key keyboards (API v5): ids row by row.</summary>
    public const int AutoKeysPerRow = 16, AutoKeyRows = 6;

    /// <summary>
    /// Alienware x16 R2 keyboard (0D62:2ABE): light id of every key, measured on hardware with
    /// four photographed color-coded patterns. The controller uses a 20-column matrix
    /// (id = 20 * row + column), with the right-hand media column wired into row 0 and Page Up
    /// into row 5. Power, Space, Left, Page Down and Right have no id below 128 and are left out,
    /// so effects never touch the power button. X, Y and W are in key units (17.2 x 6 board).
    /// </summary>
    private static readonly (int Id, string Label, double X, int Row, double W)[] X16R2Keys =
    {
        (0, "Esc", 0, 0, 1), (1, "F1", 1, 0, 1), (2, "F2", 2, 0, 1), (3, "F3", 3, 0, 1), (4, "F4", 4, 0, 1), (5, "F5", 5, 0, 1),
        (6, "F6", 6, 0, 1), (7, "F7", 7, 0, 1), (8, "F8", 8, 0, 1), (9, "F9", 9, 0, 1), (10, "F10", 10, 0, 1), (11, "F11", 11, 0, 1),
        (12, "F12", 12, 0, 1), (13, "Home", 13, 0, 1), (14, "End", 14, 0, 1), (15, "Del", 15, 0, 1),
        (19, "Mic", 16.2, 1, 1), (16, "Mute", 16.2, 2, 1), (18, "Vol-", 16.2, 3, 1), (17, "Vol+", 16.2, 4, 1),
        (20, "`", 0, 1, 1), (21, "1", 1, 1, 1), (22, "2", 2, 1, 1), (23, "3", 3, 1, 1), (24, "4", 4, 1, 1), (25, "5", 5, 1, 1),
        (26, "6", 6, 1, 1), (27, "7", 7, 1, 1), (28, "8", 8, 1, 1), (29, "9", 9, 1, 1), (30, "0", 10, 1, 1), (31, "-", 11, 1, 1),
        (32, "=", 12, 1, 1), (35, "Backspace", 13, 1, 2),
        (40, "Tab", 0, 2, 1.5), (42, "Q", 1.5, 2, 1), (43, "W", 2.5, 2, 1), (44, "E", 3.5, 2, 1), (45, "R", 4.5, 2, 1), (46, "T", 5.5, 2, 1),
        (47, "Y", 6.5, 2, 1), (48, "U", 7.5, 2, 1), (49, "I", 8.5, 2, 1), (50, "O", 9.5, 2, 1), (51, "P", 10.5, 2, 1), (52, "[", 11.5, 2, 1),
        (53, "]", 12.5, 2, 1), (54, "\\", 13.5, 2, 1.5),
        (61, "Caps", 0, 3, 1.75), (62, "A", 1.75, 3, 1), (63, "S", 2.75, 3, 1), (64, "D", 3.75, 3, 1), (65, "F", 4.75, 3, 1), (66, "G", 5.75, 3, 1),
        (67, "H", 6.75, 3, 1), (68, "J", 7.75, 3, 1), (69, "K", 8.75, 3, 1), (70, "L", 9.75, 3, 1), (71, ";", 10.75, 3, 1), (72, "'", 11.75, 3, 1),
        (74, "Enter", 12.75, 3, 2.25),
        (81, "Shift", 0, 4, 2.25), (83, "Z", 2.25, 4, 1), (84, "X", 3.25, 4, 1), (85, "C", 4.25, 4, 1), (86, "V", 5.25, 4, 1), (87, "B", 6.25, 4, 1),
        (88, "N", 7.25, 4, 1), (89, "M", 8.25, 4, 1), (90, ",", 9.25, 4, 1), (91, ".", 10.25, 4, 1), (92, "/", 11.25, 4, 1),
        (94, "Shift", 12.25, 4, 1.75), (114, "PgUp", 14, 4, 1),
        (100, "Ctrl", 0, 5, 1), (101, "Fn", 1, 5, 1), (103, "Win", 2, 5, 1), (104, "Alt", 3, 5, 1),
        (111, "Alt", 9, 5, 1), (109, "Lock", 10, 5, 1), (112, "Copilot", 11, 5, 1.5),
    };
    private const double X16R2BoardWidth = 17.2;

    public static readonly SpatialLayout Empty = new(new List<LightRegion>());

    public IReadOnlyList<LightRegion> Regions { get; }

    private SpatialLayout(List<LightRegion> regions) => Regions = regions;

    public LightRegion? Get(RegionKind k) => Regions.FirstOrDefault(r => r.Kind == k);
    public IEnumerable<LayoutPoint> Points => Regions.SelectMany(r => r.Points);

    public IReadOnlyList<LayoutPoint> PointsIn(IEnumerable<RegionKind> kinds)
    {
        var set = kinds.ToHashSet();
        return Regions.Where(r => set.Contains(r.Kind)).SelectMany(r => r.Points).ToList();
    }

    public static string Key(LightDevice d, ushort id) => $"{d.IdText}/{id}";

    public static bool IsBuiltInKeyboard(LightDevice d) => d.Vid == 0x0D62 || d.Api == LightApi.V5;

    // ---- building ----

    /// <summary>
    /// Stored assignment: "ring:3" (ring, 4th in order), "ring@250" (ring, placed by the user at
    /// 25% of the loop), "keyboard:1", "logo", "touchpad", "other" or "none".
    /// </summary>
    public static (RegionKind? Kind, int Order) ParseAssignment(string? s)
    {
        if (string.IsNullOrWhiteSpace(s) || s == "none") return (null, 0);
        var parts = s.Split(':', '@');
        int order = parts.Length > 1 && int.TryParse(parts[1], out var o) ? o : 0;
        return Enum.TryParse<RegionKind>(parts[0], true, out var k) ? (k, order) : (null, 0);
    }

    /// <summary>Exact position on the ring loop (0..1) for "ring@NNN" assignments.</summary>
    public static double? ExactAlong(string? s) =>
        s is not null && s.StartsWith("ring@", StringComparison.Ordinal) && int.TryParse(s.AsSpan(5), out var v) ? v / 1000.0 : null;

    public static string RingAt(double along) => $"ring@{(int)Math.Round((along - Math.Floor(along)) * 1000)}";

    public static string FormatAssignment(RegionKind? kind, int order) =>
        kind is null ? "none" : kind is RegionKind.Ring or RegionKind.Keyboard ? $"{kind.Value.ToString().ToLowerInvariant()}:{order}" : kind.Value.ToString().ToLowerInvariant();

    /// <summary>Region a zone belongs to when the user has not assigned one: guessed from its name.</summary>
    public static RegionKind GuessRegion(LightDevice dev, LightZone z)
    {
        if (IsBuiltInKeyboard(dev)) return RegionKind.Keyboard;
        if (dev.Vid != 0x187C) return RegionKind.Other; // mouse, external keyboard, monitor: not part of the chassis
        var n = z.Name.ToLowerInvariant();
        if (n.Contains('环') || n.Contains("灯带") || n.Contains("ring") || n.Contains("strip") || n.Contains("tron") || n.Contains("loop"))
            return RegionKind.Ring;
        if (n.Contains("logo") || n.Contains("徽标") || n.Contains("标志") || n.Contains("head") || n.Contains("alien") || n.Contains("外星人"))
            return RegionKind.Logo;
        if (n.Contains("触控") || n.Contains("touchpad") || n.Contains("trackpad"))
            return RegionKind.Touchpad;
        return RegionKind.Other;
    }

    public static SpatialLayout Build(IEnumerable<LightDevice> devices, AppSettings s, bool simulated, string model = "")
    {
        var ring = new List<(LightDevice Dev, LightZone Z, int Order, double? Exact)>();
        var logo = new List<(LightDevice Dev, LightZone Z)>();
        var other = new List<(LightDevice Dev, LightZone Z)>();
        var touchpad = new List<(LightDevice Dev, LightZone Z)>();
        var kbZones = new List<(LightDevice Dev, LightZone Z, int Order)>();
        var keyboard = new LightRegion { Kind = RegionKind.Keyboard };
        bool x16r2 = model.Contains("x16 R2", StringComparison.OrdinalIgnoreCase);

        foreach (var dev in devices.Where(d => d.Present))
        {
            var zones = dev.Zones.Select(z => (Z: z, Kind: (RegionKind?)GuessRegion(dev, z), Order: (int)z.Id)).ToList();
            // the x16 R2 chassis controller: known light ids fill in whatever the user has not identified
            if (!simulated && x16r2 && dev.Vid == 0x187C && dev.Pid == 0x0551)
            {
                var known = dev.Zones.Select(z => z.Id).ToHashSet();
                zones.AddRange(X16R2Chassis.Where(p => !known.Contains(p.Id))
                    .Select(p => (Z: new LightZone { Id = p.Id, Name = p.Name, Flags = (ushort)(p.Kind == RegionKind.Other ? 1 : 0) }, Kind: (RegionKind?)p.Kind, p.Order)));
            }

            foreach (var (z, guess, guessOrder) in zones)
            {
                s.LightRegions.TryGetValue(Key(dev, z.Id), out var a);
                var (kind, order) = a is not null ? ParseAssignment(a) : (guess, guessOrder);
                switch (kind) // null = the user excluded this light
                {
                    case RegionKind.Ring: ring.Add((dev, z, order, ExactAlong(a))); break;
                    case RegionKind.Logo: logo.Add((dev, z)); break;
                    case RegionKind.Keyboard: kbZones.Add((dev, z, order)); break;
                    case RegionKind.Touchpad: touchpad.Add((dev, z)); break;
                    case RegionKind.Other: other.Add((dev, z)); break;
                }
            }

            if (!IsBuiltInKeyboard(dev)) continue;
            AddKeyboard(keyboard, dev, s, simulated);
        }

        // keyboards with a few named zones (e.g. 4-zone): split the frame into columns
        if (keyboard.Points.Count == 0 && kbZones.Count > 0)
        {
            var sorted = kbZones.OrderBy(k => k.Order).ThenBy(k => k.Z.Id).ToList();
            var f = KeyboardFrame;
            for (int i = 0; i < sorted.Count; i++)
            {
                double w = f.W / sorted.Count;
                keyboard.Points.Add(new LayoutPoint(sorted[i].Dev, sorted[i].Z.Id, RegionKind.Keyboard,
                    f.X + w * (i + 0.5), f.Cy, w, f.H, (i + 0.5) / sorted.Count, sorted[i].Z.Name));
            }
        }
        // no zones and no key map: the keyboard as one light (hardware whole-device color)
        if (keyboard.Points.Count == 0)
            foreach (var dev in devices.Where(d => d.Present && IsBuiltInKeyboard(d) && d.HasHardwareEffects))
                keyboard.Points.Add(new LayoutPoint(dev, LayoutPoint.WholeDevice, RegionKind.Keyboard,
                    KeyboardFrame.Cx, KeyboardFrame.Cy, KeyboardFrame.W, KeyboardFrame.H, 0.5, "整个键盘"));

        var regions = new List<LightRegion>();
        if (keyboard.Points.Count > 0) regions.Add(keyboard);

        if (ring.Count > 0)
        {
            var r = new LightRegion { Kind = RegionKind.Ring };
            // lights placed by the user keep their exact spot; the rest are spread evenly by order
            var sorted = ring.OrderBy(x => x.Exact ?? 2).ThenBy(x => x.Order).ThenBy(x => x.Z.Id).ToList();
            int loose = sorted.Count(x => x.Exact is null), li = 0;
            for (int i = 0; i < sorted.Count; i++)
            {
                double along = sorted[i].Exact ?? (li++ + 0.5) / loose;
                var (x, y) = StadiumPoint(RingFrame, along);
                r.Points.Add(new LayoutPoint(sorted[i].Dev, sorted[i].Z.Id, RegionKind.Ring, x, y, 0.03, 0.045, along, sorted[i].Z.Name));
            }
            regions.Add(r);
        }

        if (logo.Count > 0)
        {
            var r = new LightRegion { Kind = RegionKind.Logo };
            for (int i = 0; i < logo.Count; i++)
            {
                double dx = (i - (logo.Count - 1) / 2.0) * 0.05;
                r.Points.Add(new LayoutPoint(logo[i].Dev, logo[i].Z.Id, RegionKind.Logo,
                    LogoFrame.Cx + dx, LogoFrame.Cy, 0.04, 0.064, logo.Count > 1 ? (double)i / (logo.Count - 1) : 0.5, logo[i].Z.Name));
            }
            regions.Add(r);
        }

        AddRow(regions, RegionKind.Touchpad, TouchpadFrame, touchpad, 0.05, 0.08);
        AddRow(regions, RegionKind.Other, OtherFrame, other, 0.03, 0.045);
        return new SpatialLayout(regions);
    }

    /// <summary>Lights spread evenly across a frame (touchpad, other lights).</summary>
    private static void AddRow(List<LightRegion> regions, RegionKind kind, NRect f, List<(LightDevice Dev, LightZone Z)> lights, double w, double h)
    {
        if (lights.Count == 0) return;
        var r = new LightRegion { Kind = kind };
        for (int i = 0; i < lights.Count; i++)
        {
            double along = (i + 0.5) / lights.Count;
            r.Points.Add(new LayoutPoint(lights[i].Dev, lights[i].Z.Id, kind, f.X + f.W * along, f.Cy, w, h, along, lights[i].Z.Name));
        }
        regions.Add(r);
    }

    /// <summary>Per-key points from the user's key map (or every key, in demo mode).</summary>
    private static void AddKeyboard(LightRegion region, LightDevice dev, AppSettings s, bool simulated)
    {
        var prefix = dev.IdText + "/";
        var mapped = s.KeyMap.Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal))
            .Select(kv => (Id: ushort.TryParse(kv.Key[prefix.Length..], out var id) ? id : (ushort)0xFFFF, Code: kv.Value))
            .Where(m => m.Id != 0xFFFF)
            .ToList();
        if (mapped.Count == 0 && simulated)
            mapped = KeyboardLayout.Keys.Select((k, i) => ((ushort)i, k.Code)).ToList();

        if (mapped.Count == 0 && dev.Vid == 0x0D62 && dev.Pid == 0x2ABE && !simulated)
        {
            // measured map of the Alienware x16 R2 keyboard
            var g = KeyboardFrame;
            double kw = g.W / X16R2BoardWidth, kh = g.H / 6;
            foreach (var (id, label, kx, row, w) in X16R2Keys)
            {
                double x = g.X + (kx + w / 2) * kw, y = g.Y + (row + 0.5) * kh;
                region.Points.Add(new LayoutPoint(dev, (ushort)id, RegionKind.Keyboard, x, y, w * kw, kh, (x - g.X) / g.W, label));
            }
            return;
        }

        if (mapped.Count == 0 && s.KeyRows.TryGetValue(dev.IdText, out var bounds) && bounds.Count >= 2)
        {
            // row calibration: each row's keys spread across the keyboard width
            var g = KeyboardFrame;
            int rows = bounds.Count - 1;
            double rh = g.H / rows;
            for (int r = 0; r < rows; r++)
            {
                int from = bounds[r], to = bounds[r + 1], len = Math.Max(1, to - from);
                double cw = g.W / len;
                for (int i = 0; i < len; i++)
                {
                    double x = g.X + (i + 0.5) * cw, y = g.Y + (r + 0.5) * rh;
                    region.Points.Add(new LayoutPoint(dev, (ushort)(from + i), RegionKind.Keyboard, x, y, cw, rh, (i + 0.5) / len, $"#{from + i}"));
                }
            }
            return;
        }

        if (mapped.Count == 0 && dev.Api == LightApi.V5)
        {
            // no key map yet: per-key keyboards number their keys row by row, so a grid gives
            // smooth per-key effects right away (key calibration makes positions exact)
            var g = KeyboardFrame;
            double cw = g.W / AutoKeysPerRow, rh = g.H / AutoKeyRows;
            for (int id = 0; id < AutoKeysPerRow * AutoKeyRows; id++)
            {
                int row = id / AutoKeysPerRow, col = id % AutoKeysPerRow;
                double x = g.X + (col + 0.5) * cw, y = g.Y + (row + 0.5) * rh;
                region.Points.Add(new LayoutPoint(dev, (ushort)id, RegionKind.Keyboard, x, y, cw, rh, (col + 0.5) / AutoKeysPerRow, $"#{id}"));
            }
            return;
        }

        foreach (var (id, code) in mapped)
        {
            if (!KeyboardLayout.TryGet(code, out var k)) continue;
            var f = KeyboardFrame;
            double kw = f.W / KeyboardLayout.Width, kh = f.H / KeyboardLayout.Height;
            double x = f.X + (k.X + k.W / 2) * kw, y = f.Y + (k.Y + 0.5) * kh;
            region.Points.Add(new LayoutPoint(dev, id, RegionKind.Keyboard, x, y, k.W * kw, kh, (x - f.X) / f.W, k.Label));
        }
    }

    /// <summary>
    /// Point at fraction <paramref name="s"/> along a stadium (rounded loop) inside <paramref name="f"/>,
    /// starting at the top-left of the straight part and running clockwise.
    /// </summary>
    public static (double X, double Y) StadiumPoint(NRect f, double s)
    {
        double w = f.W * Aspect, h = f.H, r = h / 2, l = Math.Max(0, w - h);
        double p = 2 * l + 2 * Math.PI * r;
        double d = (s - Math.Floor(s)) * p;
        double x0 = f.X * Aspect, y0 = f.Y, x, y;
        if (d < l) { x = x0 + r + d; y = y0; }
        else if (d < l + Math.PI * r)
        {
            double a = -Math.PI / 2 + (d - l) / r;
            x = x0 + r + l + r * Math.Cos(a); y = y0 + r + r * Math.Sin(a);
        }
        else if (d < 2 * l + Math.PI * r) { x = x0 + r + l - (d - l - Math.PI * r); y = y0 + h; }
        else
        {
            double a = Math.PI / 2 + (d - 2 * l - Math.PI * r) / r;
            x = x0 + r + r * Math.Cos(a); y = y0 + r + r * Math.Sin(a);
        }
        return (x / Aspect, y);
    }
}
