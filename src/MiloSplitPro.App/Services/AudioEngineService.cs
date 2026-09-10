using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using MiloSplitPro.Core.Models;

namespace MiloSplitPro.App.Services;

public class TrackAudioSource : IDisposable
{
    public string StemName { get; }
    public string FilePath { get; }
    public AudioFileReader AudioReader { get; }
    public VolumeSampleProvider VolumeProvider { get; }
    public ISampleProvider FormattedSampleProvider { get; }

    public float TargetVolume { get; set; } = 1.0f;
    public bool IsMuted { get; set; }
    public bool IsSolo { get; set; }

    public TrackAudioSource(string stemName, string filePath)
    {
        StemName = stemName;
        FilePath = filePath;
        AudioReader = new AudioFileReader(filePath);
        VolumeProvider = new VolumeSampleProvider(AudioReader) { Volume = 1.0f };

        ISampleProvider current = VolumeProvider;

        // Convert Mono to Stereo if needed
        if (current.WaveFormat.Channels == 1)
        {
            current = new MonoToStereoSampleProvider(current);
        }

        // Resample to 44.1 kHz if needed
        if (current.WaveFormat.SampleRate != 44100)
        {
            current = new WdlResamplingSampleProvider(current, 44100);
        }

        FormattedSampleProvider = current;
    }

    public void UpdateEffectiveVolume(bool anySoloActive)
    {
        if (IsMuted)
        {
            VolumeProvider.Volume = 0.0f;
        }
        else if (anySoloActive)
        {
            VolumeProvider.Volume = IsSolo ? TargetVolume : 0.0f;
        }
        else
        {
            VolumeProvider.Volume = TargetVolume;
        }
    }

    public void Dispose()
    {
        AudioReader.Dispose();
    }
}

public class AudioEngineService : IDisposable
{
    private IWavePlayer? _wavePlayer;
    private MixingSampleProvider? _mixer;
    private readonly List<TrackAudioSource> _tracks = new();
    private readonly object _lock = new();

    public event EventHandler? PlaybackStopped;

    public bool IsPlaying => _wavePlayer?.PlaybackState == PlaybackState.Playing;
    public bool IsPaused => _wavePlayer?.PlaybackState == PlaybackState.Paused;

    public TimeSpan TotalDuration { get; private set; } = TimeSpan.Zero;
    public TimeSpan CurrentPosition
    {
        get
        {
            lock (_lock)
            {
                var first = _tracks.FirstOrDefault();
                return first != null ? first.AudioReader.CurrentTime : TimeSpan.Zero;
            }
        }
    }

    public void UnloadStems()
    {
        Stop();

        lock (_lock)
        {
            if (_wavePlayer != null)
            {
                try { _wavePlayer.Stop(); } catch { }
                try { _wavePlayer.Dispose(); } catch { }
                _wavePlayer = null;
            }

            foreach (var track in _tracks)
            {
                try { track.Dispose(); } catch { }
            }
            _tracks.Clear();
            _mixer = null;
            TotalDuration = TimeSpan.Zero;
        }
    }

    public void LoadStems(IReadOnlyList<SeparatedStemInfo> stems)
    {
        UnloadStems();

        lock (_lock)
        {
            if (stems == null || stems.Count == 0) return;

            var waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
            _mixer = new MixingSampleProvider(waveFormat) { ReadFully = true };

            TimeSpan maxDuration = TimeSpan.Zero;

            foreach (var stem in stems)
            {
                if (!File.Exists(stem.FilePath)) continue;

                try
                {
                    var trackSource = new TrackAudioSource(stem.StemName, stem.FilePath);
                    var duration = stem.Duration > trackSource.AudioReader.TotalTime ? stem.Duration : trackSource.AudioReader.TotalTime;
                    if (duration > maxDuration)
                    {
                        maxDuration = duration;
                    }

                    _mixer.AddMixerInput(trackSource.FormattedSampleProvider);
                    _tracks.Add(trackSource);
                }
                catch (Exception)
                {
                    // Skip unreadable files safely
                }
            }

            if (maxDuration <= TimeSpan.Zero && stems.Count > 0)
            {
                maxDuration = stems.Max(s => s.Duration);
            }

            TotalDuration = maxDuration;
            InitWavePlayer();
        }
    }

    public void SetTotalDuration(TimeSpan duration)
    {
        lock (_lock)
        {
            if (duration > TimeSpan.Zero)
            {
                TotalDuration = duration;
            }
        }
    }

    private void InitWavePlayer()
    {
        if (_wavePlayer != null)
        {
            try { _wavePlayer.Stop(); } catch { }
            try { _wavePlayer.Dispose(); } catch { }
            _wavePlayer = null;
        }

        if (_mixer == null) return;

        try
        {
            // Primary: WaveOutEvent (100% reliable across all Windows output devices, bit depths, and sample rates)
            var waveOut = new WaveOutEvent
            {
                DesiredLatency = 100,
                NumberOfBuffers = 3
            };
            waveOut.Init(_mixer.ToWaveProvider16());
            waveOut.PlaybackStopped += (s, e) => PlaybackStopped?.Invoke(this, EventArgs.Empty);
            _wavePlayer = waveOut;
        }
        catch (Exception)
        {
            try
            {
                // Fallback: WASAPI Shared Mode
                var wasapi = new WasapiOut(NAudio.CoreAudioApi.AudioClientShareMode.Shared, 100);
                wasapi.Init(_mixer.ToWaveProvider16());
                wasapi.PlaybackStopped += (s, e) => PlaybackStopped?.Invoke(this, EventArgs.Empty);
                _wavePlayer = wasapi;
            }
            catch (Exception)
            {
                try
                {
                    // Fallback: DirectSound
                    var ds = new DirectSoundOut(100);
                    ds.Init(_mixer.ToWaveProvider16());
                    ds.PlaybackStopped += (s, e) => PlaybackStopped?.Invoke(this, EventArgs.Empty);
                    _wavePlayer = ds;
                }
                catch (Exception)
                {
                    _wavePlayer = null;
                }
            }
        }
    }

    public void Play()
    {
        lock (_lock)
        {
            if (_tracks.Count > 0)
            {
                if (_wavePlayer == null)
                {
                    InitWavePlayer();
                }

                // If at the end, restart from beginning
                var first = _tracks.FirstOrDefault();
                if (first != null && first.AudioReader.CurrentTime >= TotalDuration && TotalDuration > TimeSpan.Zero)
                {
                    Seek(TimeSpan.Zero);
                }

                _wavePlayer?.Play();
            }
        }
    }

    public void Pause()
    {
        lock (_lock)
        {
            _wavePlayer?.Pause();
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            _wavePlayer?.Stop();
            Seek(TimeSpan.Zero);
        }
    }

    public void Seek(TimeSpan targetTime)
    {
        lock (_lock)
        {
            if (targetTime < TimeSpan.Zero) targetTime = TimeSpan.Zero;
            if (TotalDuration > TimeSpan.Zero && targetTime > TotalDuration) targetTime = TotalDuration;

            foreach (var track in _tracks)
            {
                try
                {
                    track.AudioReader.CurrentTime = targetTime;
                }
                catch
                {
                    // Safe guard
                }
            }
        }
    }

    public void SetTrackVolume(string stemName, float volume)
    {
        lock (_lock)
        {
            var track = _tracks.FirstOrDefault(t => t.StemName == stemName);
            if (track != null)
            {
                track.TargetVolume = Math.Clamp(volume, 0.0f, 1.5f);
                RefreshTrackVolumes();
            }
        }
    }

    public void SetTrackMute(string stemName, bool isMuted)
    {
        lock (_lock)
        {
            var track = _tracks.FirstOrDefault(t => t.StemName == stemName);
            if (track != null)
            {
                track.IsMuted = isMuted;
                if (isMuted) track.IsSolo = false;
                RefreshTrackVolumes();
            }
        }
    }

    public void SetTrackSolo(string stemName, bool isSolo)
    {
        lock (_lock)
        {
            var track = _tracks.FirstOrDefault(t => t.StemName == stemName);
            if (track != null)
            {
                track.IsSolo = isSolo;
                if (isSolo) track.IsMuted = false;
                RefreshTrackVolumes();
            }
        }
    }

    private void RefreshTrackVolumes()
    {
        bool anySolo = _tracks.Any(t => t.IsSolo);
        foreach (var track in _tracks)
        {
            track.UpdateEffectiveVolume(anySolo);
        }
    }

    /// <summary>
    /// Exporta una mezcla offline con precisión sample a sample respetando volúmenes, mute y solo.
    /// </summary>
    public double ExportMixOffline(string outputWavPath)
    {
        lock (_lock)
        {
            if (_tracks.Count == 0) throw new InvalidOperationException("No hay pistas cargadas para exportar.");

            // Guardar posiciones actuales
            var savedPositions = _tracks.Select(t => t.AudioReader.Position).ToList();

            try
            {
                // Reset a posición cero
                foreach (var track in _tracks)
                {
                    track.AudioReader.Position = 0;
                }

                var targetSampleRate = 44100;
                var targetChannels = 2;
                var waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(targetSampleRate, targetChannels);
                var exportMixer = new MixingSampleProvider(waveFormat) { ReadFully = false };

                bool anySolo = _tracks.Any(t => t.IsSolo);
                var readersToDispose = new List<IDisposable>();

                foreach (var track in _tracks)
                {
                    var reader = new AudioFileReader(track.FilePath);
                    readersToDispose.Add(reader);

                    var vol = track.IsMuted ? 0.0f : (anySolo ? (track.IsSolo ? track.TargetVolume : 0.0f) : track.TargetVolume);
                    var volProv = new VolumeSampleProvider(reader) { Volume = vol };

                    ISampleProvider formatted = volProv;
                    if (formatted.WaveFormat.Channels == 1)
                    {
                        formatted = new MonoToStereoSampleProvider(formatted);
                    }
                    if (formatted.WaveFormat.SampleRate != targetSampleRate)
                    {
                        formatted = new WdlResamplingSampleProvider(formatted, targetSampleRate);
                    }

                    exportMixer.AddMixerInput(formatted);
                }

                var meter = new MeteringSampleProvider(exportMixer);
                double maxPeak = 0.0;
                meter.StreamVolume += (s, args) =>
                {
                    foreach (var peak in args.MaxSampleValues)
                    {
                        if (peak > maxPeak) maxPeak = peak;
                    }
                };

                // Render offline directo a archivo WAV PCM 16-bit a 44.1 kHz estéreo
                WaveFileWriter.CreateWaveFile16(outputWavPath, meter);

                foreach (var r in readersToDispose)
                {
                    r.Dispose();
                }

                return maxPeak;
            }
            finally
            {
                // Restaurar posiciones
                for (int i = 0; i < _tracks.Count && i < savedPositions.Count; i++)
                {
                    _tracks[i].AudioReader.Position = savedPositions[i];
                }
            }
        }
    }

    public void Dispose()
    {
        Stop();
        _wavePlayer?.Dispose();
        foreach (var track in _tracks)
        {
            track.Dispose();
        }
        _tracks.Clear();
    }
}
