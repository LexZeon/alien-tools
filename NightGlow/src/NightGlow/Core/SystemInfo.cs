using System.Diagnostics;
using System.Security.Principal;

namespace NightGlow.Core;

public static class SystemInfo
{
    public static bool IsElevated
    {
        get
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    /// <summary>Restarts NightGlow elevated. Returns false if the user cancelled UAC.</summary>
    public static bool RelaunchElevated()
    {
        var exe = Environment.ProcessPath;
        if (exe is null) return false;
        try
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, Verb = "runas" });
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false; // UAC cancelled
        }
    }

    /// <summary>
    /// Detects the vendor control center. Both apps writing to the same light controller makes
    /// them overwrite each other, so the UI warns. NightGlow never stops the vendor software.
    /// </summary>
    public static IReadOnlyList<string> VendorControlProcesses()
    {
        string[] names = { "AWCC", "AWCC.UCSubAgent", "AWCC.SCSubAgent", "AlienFXSubAgent", "OCControl.Service", "AlienFX.Service" };
        var found = new List<string>();
        foreach (var n in names)
        {
            try
            {
                var ps = Process.GetProcessesByName(n);
                if (ps.Length > 0) found.Add(n);
                foreach (var p in ps) p.Dispose();
            }
            catch { /* access denied on some system processes */ }
        }
        return found;
    }

    public static string MachineModel
    {
        get
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
                var maker = (key?.GetValue("SystemManufacturer") as string)?.Trim() ?? "";
                var model = (key?.GetValue("SystemProductName") as string)?.Trim() ?? "";
                // Many vendors already put the brand in the product name ("Alienware x16 R2").
                if (maker.Length > 0 && model.StartsWith(maker, StringComparison.OrdinalIgnoreCase)) return model;
                return string.Join(" ", new[] { maker, model }.Where(s => s.Length > 0));
            }
            catch { return ""; }
        }
    }
}
