using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using MiloSplitPro.App.Services;
using MiloSplitPro.Core.Models;
using NAudio.Wave;

namespace MiloSplitPro.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly AudioEngineService _audioService;
    private readonly SeparationEngineService _separationService;
    private readonly DispatcherTimer _playbackTimer;
    private CancellationTokenSource? _cancellationTokenSource;

    // File input properties
    [ObservableProperty]
    private string _inputFilePath = string.Empty;

    [ObservableProperty]
    private string _inputFileName = "Arrastrá o seleccioná una canción";

    [ObservableProperty]
    private string _durationText = "--:--";

    [ObservableProperty]
    private string _formatInfo = "WAV, MP3, FLAC, M4A";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStartSeparation))]
    private bool _hasInputFile;

    public bool CanStartSeparation => HasInputFile && !IsProcessing;

    // Folder output properties
    [ObservableProperty]
    private string _outputDirectory = string.Empty;

    [ObservableProperty]
    private bool _hasOutputDirectory;

    // Hardware
    [ObservableProperty]
    private HardwareInfo _hardwareInfo = new();

    [ObservableProperty]
    private HardwareDevicePreference _selectedHardwarePreference = HardwareDevicePreference.Auto;

    // Stems selection flags
    [ObservableProperty]
    private bool _vocalsSelected = true;

    [ObservableProperty]
    private bool _leadVocalsSelected;

    [ObservableProperty]
    private bool _backingVocalsSelected;

    [ObservableProperty]
    private bool _drumsSelected = true;

    [ObservableProperty]
    private bool _bassSelected = true;

    [ObservableProperty]
    private bool _guitarSelected;

    [ObservableProperty]
    private bool _pianoSelected;

    [ObservableProperty]
    private bool _generateComplementOther = true;

    [ObservableProperty]
    private AudioOutputFormat _selectedOutputFormat = AudioOutputFormat.MP3;

    public bool IsMp3Format
    {
        get => SelectedOutputFormat == AudioOutputFormat.MP3;
        set { if (value) SelectedOutputFormat = AudioOutputFormat.MP3; }
    }

    public bool IsWavFormat
    {
        get => SelectedOutputFormat == AudioOutputFormat.WAV;
        set { if (value) SelectedOutputFormat = AudioOutputFormat.WAV; }
    }

    public bool IsFlacFormat
    {
        get => SelectedOutputFormat == AudioOutputFormat.FLAC;
        set { if (value) SelectedOutputFormat = AudioOutputFormat.FLAC; }
    }

    partial void OnSelectedOutputFormatChanged(AudioOutputFormat value)
    {
        OnPropertyChanged(nameof(IsMp3Format));
        OnPropertyChanged(nameof(IsWavFormat));
        OnPropertyChanged(nameof(IsFlacFormat));
    }

    // Separation Process State
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStartSeparation))]
    private bool _isProcessing;

    [ObservableProperty]
    private double _progressPercentage;

    [ObservableProperty]
    private string _currentStageText = "Listo";

    [ObservableProperty]
    private string _statusMessage = "Seleccioná las pistas y la carpeta de destino para comenzar.";

    [ObservableProperty]
    private string _currentStemText = string.Empty;

    [ObservableProperty]
    private string _deviceUsedText = "CPU";

    // Player State
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayPauseGlyph))]
    private bool _isPlaying;

    public string PlayPauseGlyph => IsPlaying ? "⏸" : "▶";

    [ObservableProperty]
    private double _timelinePositionSec;

    [ObservableProperty]
    private double _timelineMaximumSec = 100.0;

    [ObservableProperty]
    private string _positionFormatted = "00:00";

    [ObservableProperty]
    private string _totalDurationFormatted = "00:00";

    [ObservableProperty]
    private bool _hasSeparatedTracks;

    // Tracks
    public ObservableCollection<TrackViewModel> SeparatedTracks { get; } = new();

    public MainViewModel()
    {
        _audioService = new AudioEngineService();
        _separationService = new SeparationEngineService();
        _hardwareInfo = HardwareDetectionService.DetectHardware();

        _audioService.PlaybackStopped += (s, e) =>
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                IsPlaying = false;
                _playbackTimer?.Stop();
            });
        };

        _playbackTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _playbackTimer.Tick += PlaybackTimer_Tick;

        // Default output dir to Music/Milo Split Pro
        var myMusic = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        OutputDirectory = Path.Combine(myMusic, "Milo Split Pro");
        HasOutputDirectory = true;
    }

    private void PlaybackTimer_Tick(object? sender, EventArgs e)
    {
        if (_audioService.IsPlaying)
        {
            var cur = _audioService.CurrentPosition;
            TimelinePositionSec = cur.TotalSeconds;
            PositionFormatted = cur.ToString(@"mm\:ss");

            if (cur >= _audioService.TotalDuration && _audioService.TotalDuration > TimeSpan.Zero)
            {
                StopPlayback();
            }
        }
    }

    [RelayCommand]
    public void BrowseInputFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Seleccionar canción para separar",
            Filter = "Archivos de audio (*.wav;*.mp3;*.flac;*.m4a;*.ogg)|*.wav;*.mp3;*.flac;*.m4a;*.ogg|Todos los archivos (*.*)|*.*",
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            LoadInputAudioFile(dialog.FileName);
        }
    }

    public void LoadInputAudioFile(string filePath)
    {
        if (!File.Exists(filePath)) return;

        InputFilePath = filePath;
        InputFileName = Path.GetFileName(filePath);
        HasInputFile = true;

        try
        {
            using var reader = new AudioFileReader(filePath);
            DurationText = reader.TotalTime.ToString(@"mm\:ss");
            TotalDurationFormatted = DurationText;
            TimelineMaximumSec = reader.TotalTime.TotalSeconds;
            FormatInfo = $"{reader.WaveFormat.SampleRate} Hz | {reader.WaveFormat.Channels} canales | {Path.GetExtension(filePath).ToUpperInvariant().Replace(".", "")}";
        }
        catch (Exception)
        {
            DurationText = "Desconocida";
            FormatInfo = Path.GetExtension(filePath).ToUpperInvariant();
        }
    }

    [RelayCommand]
    public void BrowseOutputFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Seleccionar carpeta de trabajo y exportación",
            InitialDirectory = Directory.Exists(OutputDirectory) ? OutputDirectory : Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)
        };

        if (dialog.ShowDialog() == true)
        {
            OutputDirectory = dialog.FolderName;
            HasOutputDirectory = true;
        }
    }

    [RelayCommand]
    public void SelectAllStems()
    {
        VocalsSelected = true;
        LeadVocalsSelected = true;
        BackingVocalsSelected = true;
        DrumsSelected = true;
        BassSelected = true;
        GuitarSelected = true;
        PianoSelected = true;
    }

    [RelayCommand]
    public void DeselectAllStems()
    {
        VocalsSelected = false;
        LeadVocalsSelected = false;
        BackingVocalsSelected = false;
        DrumsSelected = false;
        BassSelected = false;
        GuitarSelected = false;
        PianoSelected = false;
    }

    [RelayCommand]
    public async Task StartSeparation()
    {
        if (!HasInputFile || !File.Exists(InputFilePath))
        {
            MessageBox.Show("Por favor seleccioná un archivo de audio válido.", "Milo Split Pro", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(OutputDirectory))
        {
            MessageBox.Show("Por favor elegí una carpeta de destino para las pistas.", "Milo Split Pro", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var stems = new List<StemCategory>();
        if (VocalsSelected) stems.Add(StemCategory.Vocals);
        if (LeadVocalsSelected) stems.Add(StemCategory.LeadVocals);
        if (BackingVocalsSelected) stems.Add(StemCategory.BackingVocals);
        if (DrumsSelected) stems.Add(StemCategory.Drums);
        if (BassSelected) stems.Add(StemCategory.Bass);
        if (GuitarSelected) stems.Add(StemCategory.Guitar);
        if (PianoSelected) stems.Add(StemCategory.Piano);

        if (stems.Count == 0)
        {
            MessageBox.Show("Debés seleccionar al menos una pista para separar.", "Milo Split Pro", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        IsProcessing = true;
        ProgressPercentage = 0.0;
        CurrentStageText = "Iniciando...";
        StatusMessage = "Preparando modelos y entorno de audio...";

        // Close any active audio file readers to release file locks on destination files
        _audioService.UnloadStems();
        HasSeparatedTracks = false;
        IsPlaying = false;
        _playbackTimer.Stop();

        _cancellationTokenSource = new CancellationTokenSource();
        var progressHandler = new Progress<SeparationProgressEvent>(OnSeparationProgress);

        var songNameNoExt = Path.GetFileNameWithoutExtension(InputFilePath);
        var jobOutputDir = Path.Combine(OutputDirectory, $"{songNameNoExt}_Separated");

        var request = new SeparationRequest(
            InputFilePath,
            jobOutputDir,
            stems,
            SelectedHardwarePreference,
            GenerateComplementOther,
            OutputFormat: SelectedOutputFormat
        );

        try
        {
            var result = await _separationService.ExecuteSeparationAsync(request, progressHandler, _cancellationTokenSource.Token);

            if (result.Success)
            {
                SeparatedTracks.Clear();
                foreach (var stem in result.Stems)
                {
                    var trackVm = new TrackViewModel
                    {
                        Category = stem.Category,
                        StemName = stem.StemName,
                        FilePath = stem.FilePath,
                        Duration = stem.Duration,
                        PeakAmplitude = stem.PeakAmplitude,
                        ColorHex = GetStemColorHex(stem.Category),
                        IconGlyph = GetStemIconGlyph(stem.Category)
                    };

                    trackVm.PropertyChanged += (s, e) =>
                    {
                        if (s is TrackViewModel tvm)
                        {
                            if (e.PropertyName == nameof(TrackViewModel.Volume))
                            {
                                _audioService.SetTrackVolume(tvm.StemName, tvm.Volume);
                            }
                            else if (e.PropertyName == nameof(TrackViewModel.IsMuted))
                            {
                                _audioService.SetTrackMute(tvm.StemName, tvm.IsMuted);
                            }
                            else if (e.PropertyName == nameof(TrackViewModel.IsSolo))
                            {
                                _audioService.SetTrackSolo(tvm.StemName, tvm.IsSolo);
                            }
                        }
                    };

                    SeparatedTracks.Add(trackVm);
                }

                _audioService.LoadStems(result.Stems);
                HasSeparatedTracks = SeparatedTracks.Count > 0;

                var totalDur = _audioService.TotalDuration > TimeSpan.Zero
                    ? _audioService.TotalDuration
                    : (SeparatedTracks.Count > 0 ? SeparatedTracks.Max(t => t.Duration) : TimeSpan.Zero);

                TotalDurationFormatted = totalDur.ToString(@"mm\:ss");
                TimelineMaximumSec = Math.Max(1.0, totalDur.TotalSeconds);
                TimelinePositionSec = 0;
                PositionFormatted = "00:00";

                StatusMessage = $"¡Separación completada con éxito! {result.Stems.Count} pistas generadas.";
                CurrentStageText = "Completado";
            }
            else
            {
                StatusMessage = result.ErrorMessage ?? "Ocurrió un problema durante la separación.";
                CurrentStageText = "Detenido";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            CurrentStageText = "Error";
        }
        finally
        {
            IsProcessing = false;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    [RelayCommand]
    public void CancelSeparation()
    {
        _cancellationTokenSource?.Cancel();
        _separationService.CancelCurrentOperation();
        StatusMessage = "Cancelación solicitada por el usuario...";
    }

    private void OnSeparationProgress(SeparationProgressEvent p)
    {
        ProgressPercentage = p.ProgressPercentage;
        CurrentStageText = p.Stage.ToString();
        StatusMessage = p.Message;
        CurrentStemText = p.CurrentStem ?? "";
        if (!string.IsNullOrEmpty(p.DeviceName)) DeviceUsedText = p.DeviceName;
    }

    [RelayCommand]
    public void PlayPause()
    {
        if (!HasSeparatedTracks) return;

        if (IsPlaying)
        {
            _audioService.Pause();
            IsPlaying = false;
            _playbackTimer.Stop();
        }
        else
        {
            _audioService.Play();
            IsPlaying = true;
            _playbackTimer.Start();
        }
    }

    [RelayCommand]
    public void StopPlayback()
    {
        _audioService.Stop();
        IsPlaying = false;
        _playbackTimer.Stop();
        TimelinePositionSec = 0;
        PositionFormatted = "00:00";
    }

    [RelayCommand]
    public void SeekTimeline(double seconds)
    {
        TimelinePositionSec = seconds;
        var targetTime = TimeSpan.FromSeconds(seconds);
        _audioService.Seek(targetTime);
        PositionFormatted = targetTime.ToString(@"mm\:ss");
    }

    [RelayCommand]
    public void ExportMix()
    {
        if (!HasSeparatedTracks) return;

        var sfd = new SaveFileDialog
        {
            Title = "Exportar mezcla personalizada",
            Filter = "Audio WAV (*.wav)|*.wav",
            FileName = $"{Path.GetFileNameWithoutExtension(InputFilePath)}_MixExport.wav",
            InitialDirectory = OutputDirectory
        };

        if (sfd.ShowDialog() == true)
        {
            try
            {
                var peak = _audioService.ExportMixOffline(sfd.FileName);
                var peakDb = 20 * Math.Log10(Math.Max(peak, 1e-6));
                var msg = $"Mezcla exportada exitosamente a:\n{sfd.FileName}\nPico máximo: {peak:F3} ({peakDb:F1} dBFS)";
                if (peak > 1.0)
                {
                    msg += "\n\n⚠️ Advertencia: El audio superó 0 dBFS (posible clipping). Se recomienda atenuar los faders.";
                }
                MessageBox.Show(msg, "Exportación Completada", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al exportar mezcla: {ex.Message}", "Error de Exportación", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    [RelayCommand]
    public void OpenOutputFolder()
    {
        if (Directory.Exists(OutputDirectory))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = OutputDirectory,
                UseShellExecute = true
            });
        }
    }

    private static string GetStemColorHex(StemCategory category) => category switch
    {
        StemCategory.Vocals or StemCategory.LeadVocals or StemCategory.BackingVocals => "#EC4899", // Rosa
        StemCategory.Drums or StemCategory.Kick or StemCategory.Snare or StemCategory.Toms or StemCategory.Cymbals => "#EF4444", // Rojo
        StemCategory.Bass => "#F59E0B", // Ámbar
        StemCategory.Guitar or StemCategory.AcousticGuitar or StemCategory.ElectricGuitar => "#10B981", // Esmeralda
        StemCategory.Piano => "#8B5CF6", // Púrpura
        _ => "#6366F1" // Índigo / Other
    };

    private static string GetStemIconGlyph(StemCategory category) => category switch
    {
        StemCategory.Vocals or StemCategory.LeadVocals or StemCategory.BackingVocals => "🎤",
        StemCategory.Drums or StemCategory.Kick or StemCategory.Snare or StemCategory.Toms or StemCategory.Cymbals => "🥁",
        StemCategory.Bass => "🎸",
        StemCategory.Guitar or StemCategory.AcousticGuitar or StemCategory.ElectricGuitar => "🪕",
        StemCategory.Piano => "🎹",
        _ => "🎛️"
    };
}
