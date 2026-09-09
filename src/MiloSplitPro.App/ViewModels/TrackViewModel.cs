using System;
using CommunityToolkit.Mvvm.ComponentModel;
using MiloSplitPro.Core.Models;

namespace MiloSplitPro.App.ViewModels;

public partial class TrackViewModel : ObservableObject
{
    [ObservableProperty]
    private StemCategory _category;

    [ObservableProperty]
    private string _stemName = string.Empty;

    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private float _volume = 1.0f;

    [ObservableProperty]
    private bool _isMuted;

    [ObservableProperty]
    private bool _isSolo;

    [ObservableProperty]
    private double _peakAmplitude;

    [ObservableProperty]
    private TimeSpan _duration = TimeSpan.Zero;

    [ObservableProperty]
    private string _colorHex = "#3B82F6";

    [ObservableProperty]
    private string _iconGlyph = "🎵";

    public string DurationFormatted => Duration.ToString(@"mm\:ss");
    public string VolumePercentFormatted => $"{(int)(Volume * 100)}%";

    partial void OnVolumeChanged(float value)
    {
        OnPropertyChanged(nameof(VolumePercentFormatted));
    }

    partial void OnIsMutedChanged(bool value)
    {
        if (value && IsSolo)
        {
            IsSolo = false;
        }
    }

    partial void OnIsSoloChanged(bool value)
    {
        if (value && IsMuted)
        {
            IsMuted = false;
        }
    }
}
