namespace NightGlow.Core.Lighting;

/// <summary>Hardware access for lights. Implementations must be thread-safe.</summary>
public interface ILightingBackend
{
    /// <summary>True for the built-in demo backend (no hardware is touched).</summary>
    bool IsSimulated { get; }

    /// <summary>Enumerates devices. Safe to call again to rescan.</summary>
    IReadOnlyList<LightDevice> Open();
    void Close();

    bool SetColor(int device, ushort lightId, Rgb color);
    bool SetColors(int device, IReadOnlyList<(ushort Id, Rgb Color)> items);
    bool SetAction(int device, ushort lightId, IReadOnlyList<LightAction> actions);
    bool SetHardwareEffect(int device, HardwareEffect effect, byte tempo, ColorMode mode, Rgb c1, Rgb c2);
    bool SetBrightness(int device, byte brightness, bool includePower);
    bool Update(int device);
    bool SetLightName(int device, ushort lightId, string name, ushort flags);
}
