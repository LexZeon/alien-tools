using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using NightGlow.Core;
using NightGlow.Core.Lighting;
using Windows.UI;

namespace NightGlow.Controls;

/// <summary>
/// Schematic top view of the laptop: rear light ring, logo, keyboard and other lights.
/// Shows live colors, lets the user pick regions by clicking them and, for interactive
/// effects, reports where the user clicked so a ripple can start there.
/// </summary>
public sealed class LaptopCanvas : Grid
{
    private const double DW = 800, DH = 500;   // design size, aspect = SpatialLayout.Aspect

    private static readonly Color Accent = Color.FromArgb(255, 0x35, 0xD6, 0xE8);
    private static readonly Color Idle = Color.FromArgb(255, 0x2A, 0x2F, 0x40);
    private static readonly Color OffKey = Color.FromArgb(255, 0x1A, 0x1E, 0x2A);
    private static readonly Color Label = Color.FromArgb(255, 0x8A, 0x93, 0xA8);

    private readonly Canvas _canvas = new() { Width = DW, Height = DH, Background = new SolidColorBrush(Colors.Transparent) };
    private readonly Dictionary<RegionKind, (Shape Outline, TextBlock Label)> _regions = new();
    private readonly List<LightVisual> _lights = new();
    private readonly List<Ellipse> _rippleRings = new();
    private HashSet<RegionKind> _selected = new();
    private SpatialLayout _layout = SpatialLayout.Empty;

    private sealed record LightVisual(LayoutPoint Point, SolidColorBrush Fill, SolidColorBrush? Glow, bool IsKey);

    /// <summary>Raised when the user clicks a region (not in interactive mode).</summary>
    public event EventHandler<RegionKind>? RegionTapped;

    /// <summary>Raised with the normalized click position when in interactive mode.</summary>
    public event EventHandler<(double X, double Y)>? SurfaceTapped;

    /// <summary>When true, clicks report a position (for ripples) instead of toggling regions.</summary>
    public bool InteractiveMode { get; set; }

    /// <summary>
    /// Light identification: a click reports which region was hit (even if it has no lights yet)
    /// and, for the ring, the position along the loop (0..1).
    /// </summary>
    public bool PickMode { get; set; }

    public event EventHandler<(RegionKind Kind, double Along)>? Picked;

    /// <summary>Position along the ring loop closest to a canvas point.</summary>
    private static double NearestAlong(double x, double y)
    {
        double best = 0, bestD = double.MaxValue;
        for (int i = 0; i < 400; i++)
        {
            double s = i / 400.0;
            var (px, py) = SpatialLayout.StadiumPoint(SpatialLayout.RingFrame, s);
            double d = Math.Pow((px - x) * SpatialLayout.Aspect, 2) + Math.Pow(py - y, 2);
            if (d < bestD) { bestD = d; best = s; }
        }
        return best;
    }

    public LaptopCanvas()
    {
        Children.Add(new Viewbox { Stretch = Stretch.Uniform, Child = _canvas });
        _canvas.Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, DW, DH) }; // ripples stay inside
        _canvas.Tapped += Canvas_Tapped;
    }

    public SpatialLayout CurrentLayout => _layout;

    public void SetLayout(SpatialLayout layout)
    {
        _layout = layout;
        _canvas.Children.Clear();
        _regions.Clear();
        _lights.Clear();
        _rippleRings.Clear();

        // chassis
        AddRect(0.10, 0.005, 0.80, 0.265, 22, Color.FromArgb(255, 0x0B, 0x0D, 0x14), Color.FromArgb(255, 0x1B, 0x1F, 0x2C));
        AddRect(0.03, 0.285, 0.94, 0.705, 24, Color.FromArgb(255, 0x10, 0x13, 0x1C), Color.FromArgb(255, 0x1E, 0x22, 0x30));
        AddText("机身背部", 0.112, 0.012, 15, Label, 0.55);

        foreach (var kind in Regions.All) AddRegion(kind, layout.Get(kind));

        // ripple overlay on top, not hit-testable
        for (int i = 0; i < 16; i++)
        {
            var e = new Ellipse { StrokeThickness = 3.5, IsHitTestVisible = false, Opacity = 0, Stroke = new SolidColorBrush(Colors.White) };
            _rippleRings.Add(e);
            _canvas.Children.Add(e);
        }
        ApplySelectionStyle();
    }

    public void SetSelected(IEnumerable<RegionKind> selected)
    {
        _selected = selected.ToHashSet();
        ApplySelectionStyle();
    }

    /// <summary>Updates every light. <paramref name="colorOf"/> returns null for lights the effect does not reach.</summary>
    public void Render(Func<LayoutPoint, Rgb?> colorOf, RippleField.Drop[]? drops = null, Palette? palette = null, double now = 0, int speed = 5)
    {
        foreach (var v in _lights)
        {
            var c = _selected.Contains(v.Point.Region) ? colorOf(v.Point) : null;
            if (c is { } rgb)
            {
                var col = rgb.ToColor();
                // keep a hint of the key shape even when the color is black
                v.Fill.Color = v.IsKey && rgb.R + rgb.G + rgb.B < 24 ? OffKey : col;
                if (v.Glow is not null) v.Glow.Color = Color.FromArgb((byte)(Math.Max(rgb.R, Math.Max(rgb.G, rgb.B)) * 0.45), rgb.R, rgb.G, rgb.B);
            }
            else
            {
                v.Fill.Color = v.IsKey ? OffKey : Idle;
                if (v.Glow is not null) v.Glow.Color = Colors.Transparent;
            }
        }

        // faint water rings where ripples are travelling
        int n = 0;
        if (drops is not null && palette is not null)
        {
            double speedPx = SpatialEffects.RippleSpeed(speed) * DH, life = SpatialEffects.RippleLife(speed);
            foreach (var d in drops)
            {
                if (n >= _rippleRings.Count) break;
                double age = now - d.T0;
                if (age < 0 || age > life) continue;
                double r = age * speedPx;
                var e = _rippleRings[n++];
                e.Width = e.Height = 2 * r;
                Canvas.SetLeft(e, d.X * DW - r);
                Canvas.SetTop(e, d.Y * DH - r);
                var c = palette.Sample(d.Hue);
                ((SolidColorBrush)e.Stroke).Color = Color.FromArgb(255, c.R, c.G, c.B);
                double fade = 1 - age / life;
                e.Opacity = 0.8 * fade;
            }
        }
        for (; n < _rippleRings.Count; n++) _rippleRings[n].Opacity = 0;
    }

    // ---- drawing helpers ----

    private void AddRegion(RegionKind kind, LightRegion? region)
    {
        bool present = region is not null && region.Points.Count > 0;
        Shape outline;
        TextBlock label;
        switch (kind)
        {
            case RegionKind.Ring:
            {
                var f = SpatialLayout.RingFrame;
                outline = Frame(f, f.H * DH / 2, present);
                label = AddText(present ? "背部灯环" : "背部灯环（未标定）", f.X + 0.01, f.Y + f.H + 0.004, 18, Label);
                if (present)
                    foreach (var pt in region!.Points) AddDot(pt, 16, 34);
                break;
            }
            case RegionKind.Logo:
            {
                var f = SpatialLayout.LogoFrame;
                outline = Frame(f, f.H * DH / 2, present, ellipse: true);
                label = AddText("Logo", f.X + f.W + 0.008, f.Cy - 0.022, 16, Label);
                if (present)
                    foreach (var pt in region!.Points) AddDot(pt, 22, 46);
                break;
            }
            case RegionKind.Keyboard:
            {
                var f = SpatialLayout.KeyboardFrame;
                outline = Frame(new NRect(f.X - 0.012, f.Y - 0.018, f.W + 0.024, f.H + 0.036), 12, present);
                label = AddText(present && region!.IsWholeDevice ? "键盘（整块）" : "键盘", f.X - 0.01, f.Y - 0.064, 18, Label);
                if (present) AddKeys(region!);
                break;
            }
            case RegionKind.Touchpad:
            {
                var f = SpatialLayout.TouchpadFrame;
                outline = Frame(f, 10, present);
                label = AddText(present ? "触控板" : "触控板（未标定）", f.X - (present ? 0.08 : 0.15), f.Cy - 0.024, 18, Label);
                if (present)
                    foreach (var pt in region!.Points) AddDot(pt, 22, 52);
                break;
            }
            default:
            {
                var f = SpatialLayout.OtherFrame;
                outline = Frame(f, 10, present);
                label = AddText("其他", f.X - 0.06, f.Cy - 0.024, 18, Label);
                if (present)
                    foreach (var pt in region!.Points) AddDot(pt, 14, 30);
                if (!present) { outline.Visibility = Visibility.Collapsed; label.Visibility = Visibility.Collapsed; }
                break;
            }
        }
        _regions[kind] = (outline, label);
    }

    private void AddKeys(LightRegion region)
    {
        var f = SpatialLayout.KeyboardFrame;
        double kw = f.W * DW / KeyboardLayout.Width, kh = f.H * DH / KeyboardLayout.Height;
        if (region.IsWholeDevice)
        {
            // one light for the whole keyboard: draw every key, all tied to the same device
            var whole = region.Points[0];
            foreach (var k in KeyboardLayout.Keys)
            {
                double x = f.X + (k.X + k.W / 2) * kw / DW, y = f.Y + (k.Y + 0.5) * kh / DH;
                var pt = whole with { X = x, Y = y, W = k.W * kw / DW, H = kh / DH, Label = k.Label };
                AddKey(pt);
            }
            return;
        }
        foreach (var pt in region.Points) AddKey(pt);
    }

    private void AddKey(LayoutPoint pt)
    {
        var fill = new SolidColorBrush(OffKey);
        double w = Math.Max(4, pt.W * DW - 3), h = Math.Max(4, pt.H * DH - 3);
        var r = new Rectangle { Width = w, Height = h, RadiusX = 3, RadiusY = 3, Fill = fill, IsHitTestVisible = false };
        Canvas.SetLeft(r, pt.X * DW - w / 2);
        Canvas.SetTop(r, pt.Y * DH - h / 2);
        _canvas.Children.Add(r);
        _lights.Add(new LightVisual(pt, fill, null, true));
    }

    private void AddDot(LayoutPoint pt, double size, double glowSize)
    {
        var glow = new SolidColorBrush(Colors.Transparent);
        var g = new Ellipse { Width = glowSize, Height = glowSize, Fill = glow, IsHitTestVisible = false };
        Canvas.SetLeft(g, pt.X * DW - glowSize / 2);
        Canvas.SetTop(g, pt.Y * DH - glowSize / 2);
        _canvas.Children.Add(g);

        var fill = new SolidColorBrush(Idle);
        var e = new Ellipse { Width = size, Height = size, Fill = fill, IsHitTestVisible = false };
        ToolTipService.SetToolTip(e, pt.Label);
        Canvas.SetLeft(e, pt.X * DW - size / 2);
        Canvas.SetTop(e, pt.Y * DH - size / 2);
        _canvas.Children.Add(e);
        _lights.Add(new LightVisual(pt, fill, glow, false));
    }

    private Shape Frame(NRect f, double radius, bool present, bool ellipse = false)
    {
        Shape s = ellipse
            ? new Ellipse { Width = f.W * DW, Height = f.H * DH }
            : new Rectangle { Width = f.W * DW, Height = f.H * DH, RadiusX = radius, RadiusY = radius };
        s.Fill = new SolidColorBrush(Colors.Transparent);
        s.Stroke = new SolidColorBrush(Idle);
        s.StrokeThickness = 1.5;
        s.IsHitTestVisible = false;
        if (!present) s.StrokeDashArray = new DoubleCollection { 4, 4 };
        Canvas.SetLeft(s, f.X * DW);
        Canvas.SetTop(s, f.Y * DH);
        _canvas.Children.Add(s);
        return s;
    }

    private void AddRect(double x, double y, double w, double h, double radius, Color fill, Color stroke)
    {
        var r = new Rectangle
        {
            Width = w * DW, Height = h * DH, RadiusX = radius, RadiusY = radius,
            Fill = new SolidColorBrush(fill), Stroke = new SolidColorBrush(stroke), StrokeThickness = 1, IsHitTestVisible = false,
        };
        Canvas.SetLeft(r, x * DW);
        Canvas.SetTop(r, y * DH);
        _canvas.Children.Add(r);
    }

    private TextBlock AddText(string text, double x, double y, double size, Color color, double opacity = 1)
    {
        var t = new TextBlock { Text = text, FontSize = size, Foreground = new SolidColorBrush(color), Opacity = opacity, IsHitTestVisible = false };
        Canvas.SetLeft(t, x * DW);
        Canvas.SetTop(t, y * DH);
        _canvas.Children.Add(t);
        return t;
    }

    private void ApplySelectionStyle()
    {
        foreach (var (kind, (outline, label)) in _regions)
        {
            bool sel = _selected.Contains(kind) && _layout.Get(kind) is { Points.Count: > 0 };
            ((SolidColorBrush)outline.Stroke).Color = sel ? Accent : Idle;
            outline.StrokeThickness = sel ? 2.5 : 1.5;
            ((SolidColorBrush)label.Foreground).Color = sel ? Accent : Label;
            label.FontWeight = sel ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    private void Canvas_Tapped(object sender, TappedRoutedEventArgs e)
    {
        var p = e.GetPosition(_canvas);
        double x = p.X / DW, y = p.Y / DH;
        if (PickMode)
        {
            if (HitRegion(x, y) is { } hit)
                Picked?.Invoke(this, (hit, hit == RegionKind.Ring ? NearestAlong(x, y) : 0));
            return;
        }
        if (InteractiveMode)
        {
            SurfaceTapped?.Invoke(this, (x, y));
            return;
        }
        if (HitRegion(x, y) is { } kind && _layout.Get(kind) is { Points.Count: > 0 })
            RegionTapped?.Invoke(this, kind);
    }

    private static RegionKind? HitRegion(double x, double y)
    {
        static bool In(NRect f, double x, double y, double m) => x >= f.X - m && x <= f.X + f.W + m && y >= f.Y - m && y <= f.Y + f.H + m;
        if (In(SpatialLayout.LogoFrame, x, y, 0.01)) return RegionKind.Logo;
        if (In(SpatialLayout.RingFrame, x, y, 0.03)) return RegionKind.Ring;
        if (In(SpatialLayout.KeyboardFrame, x, y, 0.03)) return RegionKind.Keyboard;
        if (In(SpatialLayout.TouchpadFrame, x, y, 0.02)) return RegionKind.Touchpad;
        if (In(SpatialLayout.OtherFrame, x, y, 0.03)) return RegionKind.Other;
        return null;
    }
}
