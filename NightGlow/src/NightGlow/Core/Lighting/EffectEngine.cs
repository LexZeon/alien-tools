namespace NightGlow.Core.Lighting;

/// <summary>
/// Software effects. Runs a frame loop on a background task and pushes colors through the
/// lighting service's exclusive hardware queue. Frames are dropped if the hardware is slower
/// than the frame rate, so the queue never grows. Colors come from <see cref="SpatialEffects"/>
/// with the shared <see cref="EffectClock"/>, so the hardware matches the on-screen preview.
/// </summary>
internal sealed class EffectEngine
{
    private const int FrameMs = 33;              // ~30 fps for per-light devices
    private const int WholeDeviceMinMs = 100;    // whole-keyboard hardware calls are heavier: max 10/s

    private readonly LightingService _svc;
    private CancellationTokenSource? _cts;

    public EffectEngine(LightingService svc) => _svc = svc;

    public bool IsRunning => _cts is not null;

    public void Start(EffectDefinition fx, EffectParameters p, IReadOnlyList<LayoutPoint> points)
    {
        Stop();
        var cts = new CancellationTokenSource();
        _cts = cts;
        _ = Task.Run(() => LoopAsync(fx, p, points.ToList(), cts.Token));
    }

    public void Stop()
    {
        var cts = Interlocked.Exchange(ref _cts, null);
        cts?.Cancel();
    }

    private async Task LoopAsync(EffectDefinition fx, EffectParameters p, List<LayoutPoint> points, CancellationToken ct)
    {
        var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(FrameMs));
        var lastWhole = new Dictionary<int, (double At, Rgb Color)>();
        var byDevice = points.GroupBy(pt => pt.Device.Index).ToList();
        var pal = p.GetPalette();
        Task? inFlight = null;

        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                double now = EffectClock.Now;
                // drops are consumed every tick (even dropped frames) so auto-rain keeps its rhythm
                var drops = fx.Interactive
                    ? _svc.Ripples.Frame(now, SpatialEffects.RippleLife(p.Speed), p.AutoRain, SpatialEffects.RainEvery(p.Speed))
                    : Array.Empty<RippleField.Drop>();
                if (inFlight is { IsCompleted: false }) continue; // drop frame

                var frame = new List<(int Dev, List<(ushort, Rgb)>? Lights, Rgb? Whole)>();
                foreach (var group in byDevice)
                {
                    List<(ushort, Rgb)>? lights = null;
                    foreach (var pt in group)
                    {
                        var c = SpatialEffects.ColorAt(fx, p, pal, now, pt, drops);
                        if (pt.IsWholeDevice)
                        {
                            if (lastWhole.TryGetValue(group.Key, out var last) &&
                                ((now - last.At) * 1000 < WholeDeviceMinMs || last.Color == c))
                                continue;
                            lastWhole[group.Key] = (now, c);
                            frame.Add((group.Key, null, c));
                        }
                        else (lights ??= new()).Add((pt.LightId, c));
                    }
                    if (lights is not null) frame.Add((group.Key, lights, null));
                }

                if (frame.Count == 0) continue;
                inFlight = _svc.Run(b =>
                {
                    foreach (var (dev, lights, whole) in frame)
                    {
                        if (ct.IsCancellationRequested) return;
                        if (lights is not null) b.SetColors(dev, lights);
                        else if (whole is { } w) b.SetHardwareEffect(dev, HardwareEffect.Static, 5, ColorMode.One, w, w);
                    }
                });
            }
        }
        catch (OperationCanceledException) { }
        finally { timer.Dispose(); }
    }
}
