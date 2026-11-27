namespace NightGlow.Core.Lighting;

/// <summary>
/// What an effect looks like on one light, for the on-screen canvas. Software effects use
/// exactly the engine's math and clock; hardware effects are drawn to resemble the
/// controller's behavior (the real look depends on the firmware).
/// </summary>
public static class PreviewRenderer
{
    /// <summary>Returns null when the effect does not reach this light.</summary>
    public static Rgb? ColorAt(EffectDefinition fx, EffectParameters p, Palette pal, double now, LayoutPoint pt, RippleField.Drop[] drops)
    {
        if (fx.Kind == EffectKind.Software) return SpatialEffects.ColorAt(fx, p, pal, now, pt, drops);
        if (fx.Kind == EffectKind.Off) return Rgb.Black;

        bool firmware = pt.Device.HasHardwareEffects && fx.KeyboardEffect is not null;
        if (!firmware && (fx.ZoneAction is null || pt.IsWholeDevice)) return null;
        p = p.For(fx);

        double t = now / SpatialEffects.Period(p.Speed);
        double bright = p.Brightness / 255.0;
        Rgb dark = Rgb.Black;
        // keyboard firmware effects run left to right across the keys; chassis lights animate together
        double x = firmware ? Math.Clamp((pt.X - SpatialLayout.KeyboardFrame.X) / SpatialLayout.KeyboardFrame.W, 0, 1) : 0;

        switch (fx.Id)
        {
            case "static":
            case "default-blue":
                return p.Color1.Scale(bright);
            case "breathing":
                return Rgb.Lerp(dark, p.Color1, (1 - Math.Cos(t * 2 * Math.PI)) / 2).Scale(bright);
            case "spectrum":
                return Rgb.FromHsv(t * 360, 1, bright);
            case "rainbow-wave":
                return firmware
                    ? Rgb.FromHsv((t - x) * 360, 1, bright)
                    : pal.Sample(t - SpatialEffects.Project(pt, p.Direction)).Scale(bright);
            case "side-wave":
            {
                double d = Frac(t - x);
                return Rgb.Lerp(dark, p.Color1, Math.Pow(1 - d, 3)).Scale(bright);
            }
            case "dual-wave":
                return Rgb.Lerp(p.Color1, p.Color2, (1 + Math.Sin((t - x) * 2 * Math.PI)) / 2).Scale(bright);
            case "pulse":
                return (Frac(t * 2) < 0.5 ? p.Color1 : dark).Scale(bright);
            case "morph":
                return Rgb.Lerp(p.Color1, p.Color2, (1 - Math.Cos(t * 2 * Math.PI)) / 2).Scale(bright);
            case "bounce":
            {
                double pos = 1 - Math.Abs(Frac(t) * 2 - 1);
                return Rgb.Lerp(dark, p.Color1, Math.Max(0, 1 - Math.Abs(x - pos) * 4)).Scale(bright);
            }
            case "laser":
            {
                double pos = Frac(t);
                return Rgb.Lerp(dark, p.Color1, Math.Max(0, 1 - Math.Abs(x - pos) * 10)).Scale(bright);
            }
            default:
                return p.Color1.Scale(bright);
        }
    }

    private static double Frac(double v) => v - Math.Floor(v);
}
