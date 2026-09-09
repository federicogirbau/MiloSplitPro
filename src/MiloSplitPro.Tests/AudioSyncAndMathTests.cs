using System;
using System.Collections.Generic;
using FluentAssertions;
using MiloSplitPro.Core.Services;
using Xunit;

namespace MiloSplitPro.Tests;

public class AudioSyncAndMathTests
{
    [Fact]
    public void Complement_CalculatesExactSampleResidual()
    {
        // Arrange: Synthetic 1-second 44.1kHz stereo audio
        int sampleRate = 44100;
        int length = sampleRate * 2; // Stereo samples
        float[] original = new float[length];
        float[] vocals = new float[length];
        float[] bass = new float[length];

        for (int i = 0; i < length; i++)
        {
            vocals[i] = (float)(0.4 * Math.Sin(2 * Math.PI * 1000 * i / sampleRate));
            bass[i] = (float)(0.3 * Math.Sin(2 * Math.PI * 80 * i / sampleRate));
            original[i] = vocals[i] + bass[i] + (float)(0.2 * Math.Sin(2 * Math.PI * 5000 * i / sampleRate)); // Contains remaining "other"
        }

        // Act: Compute Other = Original - (Vocals + Bass)
        var selectedStems = new List<float[]> { vocals, bass };
        var other = AudioMath.ComputeComplement(original, selectedStems);

        // Assert: Summing Vocals + Bass + Other should reconstruct original with zero sample error
        var allStems = new List<float[]> { vocals, bass, other };
        double error = AudioMath.VerifyReconstructionError(original, allStems);

        error.Should().BeLessThan(1e-6, "la reconstrucción de audio a partir de las pistas y su complemento debe ser exacta");
        
        var metrics = AudioMath.CalculateMetrics(other);
        metrics.IsSilent.Should().BeFalse("la pista de complemento debe contener el material residual no seleccionado");
        metrics.PeakAmplitude.Should().BeApproximately(0.2, 0.05);
    }

    [Fact]
    public void MixStems_RespectsSoloAndMuteAccurately()
    {
        int length = 1000;
        float[] trackA = new float[length];
        float[] trackB = new float[length];

        for (int i = 0; i < length; i++)
        {
            trackA[i] = 0.5f;
            trackB[i] = 0.3f;
        }

        var stems = new List<float[]> { trackA, trackB };

        // Test 1: Normal mix (Volumes 1.0, No solo, No mute)
        var mix1 = AudioMath.MixStems(stems, new float[] { 1.0f, 1.0f }, new bool[] { false, false }, new bool[] { false, false }, out double peak1);
        mix1[0].Should().BeApproximately(0.8f, 1e-4f);
        peak1.Should().BeApproximately(0.8f, 1e-4f);

        // Test 2: Track A is Muted
        var mix2 = AudioMath.MixStems(stems, new float[] { 1.0f, 1.0f }, new bool[] { true, false }, new bool[] { false, false }, out double peak2);
        mix2[0].Should().BeApproximately(0.3f, 1e-4f);

        // Test 3: Track A is Solo
        var mix3 = AudioMath.MixStems(stems, new float[] { 1.0f, 1.0f }, new bool[] { false, false }, new bool[] { true, false }, out double peak3);
        mix3[0].Should().BeApproximately(0.5f, 1e-4f);

        // Test 4: Track A is Solo AND Muted -> Should be silent
        var mix4 = AudioMath.MixStems(stems, new float[] { 1.0f, 1.0f }, new bool[] { true, false }, new bool[] { true, false }, out double peak4);
        mix4[0].Should().BeApproximately(0.0f, 1e-4f);
    }

    [Fact]
    public void CalculateMetrics_IdentifiesSilenceCorrectly()
    {
        float[] silence = new float[1000]; // all zeros
        var metrics = AudioMath.CalculateMetrics(silence);

        metrics.IsSilent.Should().BeTrue();
        metrics.PeakAmplitude.Should().Be(0.0);
        metrics.RmsEnergy.Should().Be(0.0);
    }

    [Fact]
    public void TrackViewModel_MuteAndSolo_AreMutuallyExclusive()
    {
        var vm = new MiloSplitPro.App.ViewModels.TrackViewModel();

        // 1. Enabling Solo should clear Mute if active
        vm.IsMuted = true;
        vm.IsMuted.Should().BeTrue();
        vm.IsSolo.Should().BeFalse();

        vm.IsSolo = true;
        vm.IsSolo.Should().BeTrue();
        vm.IsMuted.Should().BeFalse("activar Solo debe desmarcar Mute automáticamente");

        // 2. Enabling Mute should clear Solo if active
        vm.IsMuted = true;
        vm.IsMuted.Should().BeTrue();
        vm.IsSolo.Should().BeFalse("activar Mute debe desmarcar Solo automáticamente");
    }

    [Fact]
    public void AudioEngineService_LoadsAndPlaysCorrectly()
    {
        // 1. Create a dummy test WAV file
        string tempWav = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"test_stem_{Guid.NewGuid():N}.wav");
        try
        {
            var waveFormat = new NAudio.Wave.WaveFormat(44100, 16, 2);
            using (var writer = new NAudio.Wave.WaveFileWriter(tempWav, waveFormat))
            {
                // Write 1 second of audio
                byte[] data = new byte[44100 * 4];
                for (int i = 0; i < data.Length; i += 4)
                {
                    short sample = (short)(Math.Sin(2 * Math.PI * 440 * (i / 4) / 44100.0) * 10000);
                    byte[] sampleBytes = BitConverter.GetBytes(sample);
                    data[i] = sampleBytes[0];
                    data[i + 1] = sampleBytes[1];
                    data[i + 2] = sampleBytes[0];
                    data[i + 3] = sampleBytes[1];
                }
                writer.Write(data, 0, data.Length);
            }

            var stemInfo = new MiloSplitPro.Core.Models.SeparatedStemInfo(
                MiloSplitPro.Core.Models.StemCategory.Vocals,
                "Vocals",
                tempWav,
                TimeSpan.FromSeconds(1.0),
                44100,
                2,
                0.5,
                0.3
            );

            using var engine = new MiloSplitPro.App.Services.AudioEngineService();
            engine.LoadStems(new[] { stemInfo });

            engine.TotalDuration.TotalSeconds.Should().BeApproximately(1.0, 0.1);

            // Test play and position advance
            engine.Play();
            engine.IsPlaying.Should().BeTrue();

            System.Threading.Thread.Sleep(300);

            // Position should have advanced
            var pos = engine.CurrentPosition;
            pos.TotalMilliseconds.Should().BeGreaterThan(0);

            engine.Pause();
            engine.IsPlaying.Should().BeFalse();
        }
        finally
        {
            if (System.IO.File.Exists(tempWav))
            {
                System.IO.File.Delete(tempWav);
            }
        }
    }

    [Fact]
    public void AudioEngineService_PlaysActualMp3Stems()
    {
        string dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "Milo Split Pro", "A Mi Manera - Ven Aquí_Separated");
        if (!System.IO.Directory.Exists(dir)) return;

        var mp3Files = System.IO.Directory.GetFiles(dir, "*.mp3");
        if (mp3Files.Length == 0) return;

        var stems = mp3Files.Select(f => new MiloSplitPro.Core.Models.SeparatedStemInfo(
            MiloSplitPro.Core.Models.StemCategory.Vocals,
            System.IO.Path.GetFileNameWithoutExtension(f),
            f,
            TimeSpan.FromSeconds(184),
            44100,
            2,
            0.5,
            0.3
        )).ToList();

        using var engine = new MiloSplitPro.App.Services.AudioEngineService();
        engine.LoadStems(stems);

        engine.TotalDuration.TotalSeconds.Should().BeGreaterThan(0);

        engine.Play();
        engine.IsPlaying.Should().BeTrue();

        System.Threading.Thread.Sleep(500);

        var pos = engine.CurrentPosition;
        pos.TotalMilliseconds.Should().BeGreaterThan(0, "la posición debe avanzar durante la reproducción de los mp3");
    }
}
