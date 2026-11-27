using System.Diagnostics;

namespace NightGlow.Core.Lighting;

/// <summary>One clock for the hardware engine and the on-screen preview, so both show the same phase.</summary>
public static class EffectClock
{
    private static readonly Stopwatch Sw = Stopwatch.StartNew();
    public static double Now => Sw.Elapsed.TotalSeconds;
}

public enum WaveDirection { LeftToRight, RightToLeft, TopToBottom, BottomToTop, CenterOut, Inward, Clockwise, CounterClockwise }

public static class WaveDirections
{
    public static readonly (WaveDirection Dir, string Name, string Glyph)[] All =
    {
        (WaveDirection.LeftToRight, "从左到右", "→"),
        (WaveDirection.RightToLeft, "从右到左", "←"),
        (WaveDirection.TopToBottom, "从后往前", "↓"),
        (WaveDirection.BottomToTop, "从前往后", "↑"),
        (WaveDirection.CenterOut, "由中心扩散", "◎"),
        (WaveDirection.Inward, "向中心汇聚", "◉"),
        (WaveDirection.Clockwise, "顺时针环绕", "↻"),
        (WaveDirection.CounterClockwise, "逆时针环绕", "↺"),
    };
}

/// <summary>A cyclic color palette. An empty stop list means the full HSV rainbow.</summary>
public sealed record Palette(string Id, string Name, string[] StopsHex)
{
    private readonly Rgb[] _stops = StopsHex.Select(h => Rgb.TryParse(h, out var c) ? c : Rgb.White).ToArray();

    /// <summary>Color at position <paramref name="u"/> (wraps every 1.0).</summary>
    public Rgb Sample(double u)
    {
        u -= Math.Floor(u);
        if (_stops.Length == 0) return Rgb.FromHsv(u * 360, 1, 1);
        if (_stops.Length == 1) return _stops[0];
        double f = u * _stops.Length;
        int i = (int)f % _stops.Length;
        double k = f - Math.Floor(f);
        k = k * k * (3 - 2 * k); // smoothstep: softer blends between stops
        return Rgb.Lerp(_stops[i], _stops[(i + 1) % _stops.Length], k);
    }

    /// <summary>Preview gradient stops for the UI.</summary>
    public string[] PreviewHex => StopsHex.Length > 0 ? StopsHex : new[] { "#FF3B3B", "#FFB13B", "#F5FF3B", "#3BFF6E", "#3BD8FF", "#5B3BFF", "#E23BFF" };

    public const string CustomId = "custom";

    public static readonly IReadOnlyList<Palette> All = new Palette[]
    {
        new("rainbow", "彩虹", Array.Empty<string>()),
        new("aurora", "极光", new[] { "#00FFA3", "#00C8FF", "#6A5CFF", "#00E0C0" }),
        new("sunset", "日落", new[] { "#FF4E2A", "#FF2A7A", "#FF9A3C", "#FFC93C" }),
        new("ocean", "深海", new[] { "#0047FF", "#00B4FF", "#00FFE0", "#0080FF" }),
        new("sakura", "樱花", new[] { "#FF7AC8", "#FFC2E6", "#B47CFF", "#FF9AD5" }),
        new("ember", "熔岩", new[] { "#FF1E00", "#FF7A00", "#FFD000", "#FF3D00" }),
        new(CustomId, "自定义", Array.Empty<string>()),
    };

    public static Palette Resolve(string? id, Rgb c1, Rgb c2) =>
        id == CustomId
            ? new Palette(CustomId, "自定义", new[] { c1.ToHex(), c2.ToHex() })
            : All.FirstOrDefault(p => p.Id == id) ?? All[0];
}

/// <summary>
/// Water-drop ripples. Shared by the engine and the preview: a click on the canvas adds a
/// drop that both of them render. Thread-safe.
/// </summary>
public sealed class RippleField
{
    public readonly record struct Drop(double X, double Y, double T0, double Hue);

    private readonly object _gate = new();
    private readonly List<Drop> _drops = new();
    private readonly Random _rng = new();
    private double _hue;
    private double _lastRain;
    private double _nextRainGap = 1;

    public void Add(double x, double y)
    {
        lock (_gate) AddLocked(x, y, EffectClock.Now);
    }

    private void AddLocked(double x, double y, double now)
    {
        _hue = (_hue + 0.618034) % 1; // golden ratio: successive drops get well-separated colors
        _drops.Add(new Drop(x, y, now, _hue));
        if (_drops.Count > 16) _drops.RemoveAt(0);
    }

    /// <summary>Live drops for this frame; also lets it rain when <paramref name="rain"/> is on.</summary>
    public Drop[] Frame(double now, double life, bool rain, double rainEvery)
    {
        lock (_gate)
        {
            _drops.RemoveAll(d => now - d.T0 > life);
            if (rain && now - _lastRain > _nextRainGap)
            {
                _lastRain = now;
                _nextRainGap = rainEvery * (0.6 + _rng.NextDouble() * 0.8);
                AddLocked(0.08 + _rng.NextDouble() * 0.84, 0.06 + _rng.NextDouble() * 0.86, now);
            }
            return _drops.ToArray();
        }
    }
}

/// <summary>Software effects computed from a light's position on the canvas.</summary>
public static class SpatialEffects
{
    /// <summary>Ripple speed in canvas heights per second.</summary>
    public static double RippleSpeed(int speed) => 0.35 + Math.Clamp(speed, 1, 10) * 0.09;
    public static double RippleLife(int speed) => 2.0 / RippleSpeed(speed);
    public static double RainEvery(int speed) => 2.6 - Math.Clamp(speed, 1, 10) * 0.18;

    public static double Period(int speed) => 12.0 / Math.Clamp(speed, 1, 10); // seconds per cycle

    /// <summary>Position of a point along the wave direction, 0..1.</summary>
    public static double Project(LayoutPoint pt, WaveDirection dir)
    {
        switch (dir)
        {
            case WaveDirection.LeftToRight: return pt.X;
            case WaveDirection.RightToLeft: return 1 - pt.X;
            case WaveDirection.TopToBottom: return pt.Y;
            case WaveDirection.BottomToTop: return 1 - pt.Y;
            case WaveDirection.CenterOut:
            case WaveDirection.Inward:
            {
                double dx = (pt.X - 0.5) * SpatialLayout.Aspect, dy = pt.Y - 0.5;
                double d = Math.Sqrt(dx * dx + dy * dy) / 0.95;
                return dir == WaveDirection.CenterOut ? d : 1 - d;
            }
            default:
            {
                // around: the ring uses its own loop order; other regions turn around their center
                double a;
                if (pt.Region == RegionKind.Ring) a = pt.Along;
                else
                {
                    var c = pt.Region == RegionKind.Keyboard ? SpatialLayout.KeyboardFrame
                        : pt.Region == RegionKind.Logo ? SpatialLayout.LogoFrame : SpatialLayout.OtherFrame;
                    a = Math.Atan2(pt.Y - c.Cy, (pt.X - c.Cx) * SpatialLayout.Aspect) / (2 * Math.PI) + 0.5;
                }
                return dir == WaveDirection.Clockwise ? a : 1 - a;
            }
        }
    }

    public static Rgb ColorAt(EffectDefinition fx, EffectParameters p, Palette pal, double now, LayoutPoint pt, RippleField.Drop[] drops)
    {
        double bright = p.Brightness / 255.0;
        double t = now / Period(p.Speed);
        switch (fx.Id)
        {
            case "sp-wave":
                // the color at a point is the palette at (time - position): the pattern moves along the direction
                return pal.Sample(t - Project(pt, p.Direction)).Scale(bright);

            case "sp-ripple":
            {
                double v = RippleSpeed(p.Speed), life = RippleLife(p.Speed);
                var ambient = pal.Sample(t * 0.25 - pt.X * 0.3);
                double r = ambient.R * 0.05, g = ambient.G * 0.05, b = ambient.B * 0.05;
                foreach (var d in drops)
                {
                    double age = now - d.T0;
                    if (age < 0 || age > life) continue;
                    double dx = (pt.X - d.X) * SpatialLayout.Aspect, dy = pt.Y - d.Y;
                    double dist = Math.Sqrt(dx * dx + dy * dy);
                    double radius = age * v;
                    double fade = 1 - age / life; fade *= fade;
                    double ring = Math.Exp(-Sq((dist - radius) / 0.075));
                    double inner = 0.45 * Math.Exp(-Sq((dist - radius * 0.55) / 0.05));
                    double splash = age < 0.25 ? (1 - age / 0.25) * Math.Exp(-Sq(dist / 0.06)) : 0;
                    double k = (ring + inner) * fade + splash;
                    var c = pal.Sample(d.Hue);
                    r += c.R * k; g += c.G * k; b += c.B * k;
                }
                return new Rgb(Clamp(r * bright), Clamp(g * bright), Clamp(b * bright));
            }

            case "sw-cycle":
                return pal.Sample(t).Scale(bright);

            case "sw-breath":
            {
                double k = (1 - Math.Cos(t * 2 * Math.PI)) / 2;
                return Rgb.Lerp(p.Color1, p.Color2, k).Scale(bright);
            }

            default:
                return p.Color1.Scale(bright);
        }
    }

    private static double Sq(double v) => v * v;
    private static byte Clamp(double v) => (byte)Math.Clamp(Math.Round(v), 0, 255);
}
