using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using NightGlow.Core;

namespace NightGlow.Pages;

/// <summary>Row view-model for one light zone.</summary>
public sealed partial class ZoneRow : ObservableObject
{
    public required LightDevice Device { get; init; }
    public required LightZone Zone { get; init; }

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private SolidColorBrush _brush = new(Microsoft.UI.Colors.Black);

    public string IdText => Zone.Id.ToString();
    public string FlagText => Zone.IsPowerButton ? " · 电源键" : Zone.IsIndicator ? " · 指示灯" : "";
    public Rgb Color { get; set; } = Rgb.Black;
}

public sealed partial class LightingPage : Page, IRefreshable
{
    private readonly ObservableCollection<ZoneRow> _rows = new();
    private LightDevice? _device;
    private ZoneRow? _editing;
    private bool _loading;

    public LightingPage()
    {
        InitializeComponent();
        ZoneList.ItemsSource = _rows;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e) => Refresh();

    public void Refresh()
    {
        DeviceListView.ItemsSource = App.Lighting.Devices;
        if (DeviceListView.SelectedItem is null && App.Lighting.Devices.Count > 0)
            DeviceListView.SelectedIndex = 0;
        ShowDevice(DeviceListView.SelectedItem as LightDevice);
    }

    private void DeviceListView_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        ShowDevice(DeviceListView.SelectedItem as LightDevice);

    private static string Key(LightDevice d, ushort id) => $"{d.IdText}/{id}";

    private void ShowDevice(LightDevice? dev)
    {
        _device = dev;
        DetailPanel.Visibility = dev is null ? Visibility.Collapsed : Visibility.Visible;
        if (dev is null) return;

        _loading = true;
        DevTitle.Text = dev.Kind;
        DevSub.Text = $"{dev.Name} · {dev.IdText} · {dev.ApiText}" + (dev.HasHardwareEffects ? " · 支持硬件整体灯效" : "");
        BrightnessSlider.Value = Math.Round(dev.Brightness / 2.55);
        _loading = false;

        _rows.Clear();
        foreach (var z in dev.Zones)
        {
            var color = App.Settings.ZoneColors.TryGetValue(Key(dev, z.Id), out var hex) && Rgb.TryParse(hex, out var c) ? c : Rgb.Black;
            _rows.Add(new ZoneRow { Device = dev, Zone = z, Name = z.Name, Color = color, Brush = new SolidColorBrush(color.ToColor()) });
        }
        bool hasZones = dev.Zones.Count > 0;
        NoZonesInfo.IsOpen = !hasZones;
        ZonesSection.Visibility = hasZones ? Visibility.Visible : Visibility.Collapsed;
        CalibExpander.IsExpanded = !hasZones && !dev.HasHardwareEffects;
        ProbeStatus.Text = "";
    }

    private async void ApplyWhole_Click(object sender, RoutedEventArgs e)
    {
        if (_device is null) return;
        var c = Rgb.FromColor(WholePicker.Color);
        WholeSwatch.Background = new SolidColorBrush(WholePicker.Color);
        WholeFlyout.Hide();
        await App.Lighting.SetDeviceColorAsync(_device, c);
        foreach (var r in _rows)
        {
            r.Color = c;
            r.Brush = new SolidColorBrush(c.ToColor());
            App.Settings.ZoneColors[Key(_device, r.Zone.Id)] = c.ToHex();
        }
        App.Settings.Save();
    }

    private async void BrightnessSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_loading || _device is null) return;
        await App.Lighting.SetBrightnessAsync(_device, (byte)Math.Round(e.NewValue * 2.55));
    }

    private void ZoneColor_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ZoneRow row) return;
        _editing = row;
        ZonePicker.Color = row.Color.ToColor();
        ZoneNameBox.Text = row.Name;
        ZoneFlyout.ShowAt((FrameworkElement)sender);
    }

    private async void ApplyZone_Click(object sender, RoutedEventArgs e)
    {
        var row = _editing;
        if (row is null) return;
        ZoneFlyout.Hide();
        var c = Rgb.FromColor(ZonePicker.Color);
        row.Color = c;
        row.Brush = new SolidColorBrush(ZonePicker.Color);
        App.Settings.ZoneColors[Key(row.Device, row.Zone.Id)] = c.ToHex();
        App.Settings.Save();
        await App.Lighting.SetZoneColorAsync(row.Device, row.Zone, c);

        var newName = ZoneNameBox.Text.Trim();
        if (newName.Length > 0 && newName != row.Name)
        {
            row.Name = newName;
            await App.Lighting.RenameZoneAsync(row.Device, row.Zone, newName);
        }
    }

    private async void Probe_Click(object sender, RoutedEventArgs e)
    {
        if (_device is null || double.IsNaN(ProbeId.Value)) return;
        var id = (ushort)ProbeId.Value;
        await App.Lighting.ProbeLightAsync(_device, id, new Rgb(255, 255, 255));
        ProbeStatus.Text = $"已点亮编号 {id}（白色）。看是哪个灯，起名后保存；没有反应就点“下一个”。";
    }

    private void ProbeNext_Click(object sender, RoutedEventArgs e)
    {
        ProbeId.Value = Math.Min(255, (double.IsNaN(ProbeId.Value) ? 0 : ProbeId.Value) + 1);
        Probe_Click(sender, e);
    }

    private async void SaveZone_Click(object sender, RoutedEventArgs e)
    {
        if (_device is null || double.IsNaN(ProbeId.Value)) return;
        var name = ProbeName.Text.Trim();
        if (name.Length == 0) { ProbeStatus.Text = "请先填名称。"; return; }
        var id = (ushort)ProbeId.Value;
        if (_device.Zones.Any(z => z.Id == id)) { ProbeStatus.Text = $"编号 {id} 已经命名过了。"; return; }
        await App.Lighting.AddZoneAsync(_device, id, name);
        ProbeName.Text = "";
        ShowDevice(_device);
        CalibExpander.IsExpanded = true;
        ProbeStatus.Text = $"已保存：{id} = {name}";
    }
}
