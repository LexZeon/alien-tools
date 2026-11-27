namespace NightGlow.Core.Lighting;

/// <summary>
/// Software effects. Runs a frame loop on a background task and pushes colors through the
/// lighting service's exclusive hardware queue. Frames are dropped if the hardware is slower
/// than the frame rate, so the queue never grows.
/// </summary>
internal sealed class EffectEngine
{
    private const int FrameMs = 33;              // ~30 fps for zone devices
    private const int WholeDeviceMinMs = 100;    // whole-keyboard hardware calls are heavier: max 10/s

    private readonly LightingService _svc;
    private CancellationTokenSource? _cts;

    public EffectEngine(LightingService svc) => _svc = svc;

    public void Start(EffectDefinition fx, EffectParameters p, IReadOnlyList<LightDevice> targets)
    {
        Stop();
        var cts = new CancellationTokenSource();
        _cts = cts;
        _ = Task.Run(() => LoopAsync(fx, p, targets.ToList(), cts.Token));
    }

    public void Stop()
    {
        var cts = Interlocked.Exchange(ref _cts, null);
        cts?.Cancel();
    }

    private async Task LoopAsync(EffectDefinition fx, EffectParameters p, List<LightDevice> targets, CancellationToken ct)
    {
        var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(FrameMs));
        var start = DateTime.UtcNow;
        var lastWhole = new Dictionary<int, (DateTime At, Rgb Color)>();
        Task? inFlight = null;
        double period = 12.0 / Math.Clamp(p.Speed, 1, 10); // seconds per cycle: speed 1 = 12s, 10 = 1.2s

        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                if (inFlight is { IsCompleted: false }) continue; // drop frame
                double t = (DateTime.UtcNow - start).TotalSeconds / period;
                var frame = new List<(LightDevice Dev, List<(ushort, Rgb)>? Zones, Rgb? Whole)>();

                foreach (var dev in targets)
                {
                    if (dev.Zones.Count > 0)
                    {
                        var list = new List<(ushort, Rgb)>(dev.Zones.Count);
                        for (int i = 0; i < dev.Zones.Count; i++)
                            list.Add((dev.Zones[i].Id, ColorAt(fx, p, t, i, dev.Zones.Count)));
                        frame.Add((dev, list, null));
                    }
                    else if (dev.HasHardwareEffects)
                    {
                        var c = ColorAt(fx, p, t, 0, 1);
                        var now = DateTime.UtcNow;
                        if (lastWhole.TryGetValue(dev.Index, out var last) &&
                            ((now - last.At).TotalMilliseconds < WholeDeviceMinMs || last.Color == c))
                            continue;
                        lastWhole[dev.Index] = (now, c);
                        frame.Add((dev, null, c));
                    }
                }

                if (frame.Count == 0) continue;
                inFlight = _svc.Run(b =>
                {
                    foreach (var (dev, zones, whole) in frame)
                    {
                        if (ct.IsCancellationRequested) return;
                        if (zones is not null) b.SetColors(dev.Index, zones);
                        else if (whole is { } w) b.SetHardwareEffect(dev.Index, HardwareEffect.Static, 5, ColorMode.One, w, w);
                    }
                });
            }
        }
        catch (OperationCanceledException) { }
        finally { timer.Dispose(); }
    }

    /// <summary>Color of light <paramref name="i"/> of <paramref name="n"/> at normalized time <paramref name="t"/> (cycles).</summary>
    internal static Rgb ColorAt(EffectDefinition fx, EffectParameters p, double t, int i, int n)
    {
        double bright = p.Brightness / 255.0;
        switch (fx.Id)
        {
            case "sw-cycle":
                return Rgb.FromHsv(t * 360, 1, bright);
            case "sw-breath":
            {
                double k = (1 - Math.Cos(t * 2 * Math.PI)) / 2; // 0..1..0
                return Rgb.Lerp(p.Color1, p.Color2, k).Scale(bright);
            }
            case "sw-wave":
                return Rgb.FromHsv(t * 360 + (n > 1 ? 360.0 * i / n : 0), 1, bright);
            default:
                return p.Color1.Scale(bright);
        }
    }
}
