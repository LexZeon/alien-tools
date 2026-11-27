namespace NightGlow.Core.Lighting;

/// <summary>
/// Approximates what an effect looks like on a row of <c>n</c> lights, for the on-screen preview.
/// Software effects use exactly the engine's math; hardware effects are drawn to resemble the
/// controller's behavior (the real look depends on the firmware).
/// </summary>
public static class PreviewRenderer
{
    public static Rgb ColorAt(EffectDefinition fx, EffectParameters p, double seconds, int i, int n)
    {
        double period = 12.0 / Math.Clamp(p.Speed, 1, 10);
        double t = seconds / period;
        double x = n > 1 ? (double)i / (n - 1) : 0;
        double bright = p.Brightness / 255.0;
        Rgb dark = Rgb.Black;

        switch (fx.Id)
        {
            case "static":
                return p.Color1.Scale(bright);
            case "off":
                return dark;
            case "breathing":
                return Rgb.Lerp(dark, p.Color1, (1 - Math.Cos(t * 2 * Math.PI)) / 2).Scale(bright);
            case "spectrum":
                return Rgb.FromHsv(t * 360, 1, bright);
            case "rainbow-wave":
                return Rgb.FromHsv(t * 360 - x * 300, 1, bright);
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
                return EffectEngine.ColorAt(fx, p, t, i, n);
        }
    }

    private static double Frac(double v) => v - Math.Floor(v);
}
