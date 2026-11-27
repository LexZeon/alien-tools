namespace NightGlow.Core.Lighting;

/// <summary>Areas the user can select as a whole, like a vendor control center does.</summary>
public enum RegionKind { Keyboard, Ring, Logo, Other }

public static class Regions
{
    public static readonly RegionKind[] All = { RegionKind.Keyboard, RegionKind.Ring, RegionKind.Logo, RegionKind.Other };

    public static string Name(RegionKind k) => k switch
    {
        RegionKind.Keyboard => "键盘",
        RegionKind.Ring => "背部灯环",
        RegionKind.Logo => "Logo",
        _ => "其他灯光",
    };

    public static string Glyph(RegionKind k) => k switch
    {
        RegionKind.Keyboard => "",
        RegionKind.Ring => "",
        RegionKind.Logo => "",
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
    public static readonly NRect OtherFrame = new(0.28, 0.85, 0.44, 0.1);

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

    /// <summary>Stored assignment: "ring:3", "keyboard:1", "logo", "other" or "none".</summary>
    public static (RegionKind? Kind, int Order) ParseAssignment(string? s)
    {
        if (string.IsNullOrWhiteSpace(s) || s == "none") return (null, 0);
        var parts = s.Split(':');
        int order = parts.Length > 1 && int.TryParse(parts[1], out var o) ? o : 0;
        return Enum.TryParse<RegionKind>(parts[0], true, out var k) ? (k, order) : (null, 0);
    }

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
        if (n.Contains("logo") || n.Contains("徽标") || n.Contains("标志") || n.Contains("head") || n.Contains("alien"))
            return RegionKind.Logo;
        return RegionKind.Other;
    }

    public static SpatialLayout Build(IEnumerable<LightDevice> devices, AppSettings s, bool simulated)
    {
        var ring = new List<(LightDevice Dev, LightZone Z, int Order)>();
        var logo = new List<(LightDevice Dev, LightZone Z)>();
        var other = new List<(LightDevice Dev, LightZone Z)>();
        var kbZones = new List<(LightDevice Dev, LightZone Z, int Order)>();
        var keyboard = new LightRegion { Kind = RegionKind.Keyboard };

        foreach (var dev in devices.Where(d => d.Present))
        {
            foreach (var z in dev.Zones)
            {
                var (kind, order) = s.LightRegions.TryGetValue(Key(dev, z.Id), out var a)
                    ? ParseAssignment(a)
                    : (GuessRegion(dev, z), z.Id);
                switch (kind) // null = the user excluded this light

                {
                    case RegionKind.Ring: ring.Add((dev, z, order)); break;
                    case RegionKind.Logo: logo.Add((dev, z)); break;
                    case RegionKind.Keyboard: kbZones.Add((dev, z, order)); break;
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
            var sorted = ring.OrderBy(x => x.Order).ThenBy(x => x.Z.Id).ToList();
            for (int i = 0; i < sorted.Count; i++)
            {
                double along = (i + 0.5) / sorted.Count;
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

        if (other.Count > 0)
        {
            var r = new LightRegion { Kind = RegionKind.Other };
            for (int i = 0; i < other.Count; i++)
            {
                double along = (i + 0.5) / other.Count;
                r.Points.Add(new LayoutPoint(other[i].Dev, other[i].Z.Id, RegionKind.Other,
                    OtherFrame.X + OtherFrame.W * along, OtherFrame.Cy, 0.03, 0.045, along, other[i].Z.Name));
            }
            regions.Add(r);
        }
        return new SpatialLayout(regions);
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
