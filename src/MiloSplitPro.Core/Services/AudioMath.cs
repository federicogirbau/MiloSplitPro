namespace MiloSplitPro.Core.Services;

public class AudioMetrics
{
    public double PeakAmplitude { get; init; }
    public double RmsEnergy { get; init; }
    public bool IsSilent { get; init; }
    public double MaxReconstructionError { get; init; }
}

public static class AudioMath
{
    private const double SilenceThresholdRms = 1e-4; // ~ -80 dBFS

    public static AudioMetrics CalculateMetrics(float[] samples)
    {
        if (samples == null || samples.Length == 0)
        {
            return new AudioMetrics { PeakAmplitude = 0, RmsEnergy = 0, IsSilent = true };
        }

        double peak = 0.0;
        double sumSquares = 0.0;

        for (int i = 0; i < samples.Length; i++)
        {
            double abs = Math.Abs(samples[i]);
            if (abs > peak) peak = abs;
            sumSquares += samples[i] * samples[i];
        }

        double rms = Math.Sqrt(sumSquares / samples.Length);
        bool isSilent = rms < SilenceThresholdRms && peak < 1e-3;

        return new AudioMetrics
        {
            PeakAmplitude = peak,
            RmsEnergy = rms,
            IsSilent = isSilent
        };
    }

    /// <summary>
    /// Calcula la pista de complemento (Other) restando exactamente las pistas seleccionadas del audio original.
    /// Other[i] = Original[i] - Sum(SelectedStems[i])
    /// </summary>
    public static float[] ComputeComplement(float[] original, IReadOnlyList<float[]> selectedStems)
    {
        if (original == null) throw new ArgumentNullException(nameof(original));
        if (selectedStems == null || selectedStems.Count == 0)
        {
            var copy = new float[original.Length];
            Array.Copy(original, copy, original.Length);
            return copy;
        }

        int length = original.Length;
        float[] complement = new float[length];

        for (int i = 0; i < length; i++)
        {
            float stemSum = 0.0f;
            for (int s = 0; s < selectedStems.Count; s++)
            {
                if (i < selectedStems[s].Length)
                {
                    stemSum += selectedStems[s][i];
                }
            }
            complement[i] = original[i] - stemSum;
        }

        return complement;
    }

    /// <summary>
    /// Mezcla múltiples pistas aplicando volumen, solo y mute.
    /// </summary>
    public static float[] MixStems(
        IReadOnlyList<float[]> stems,
        IReadOnlyList<float> volumes,
        IReadOnlyList<bool> mutes,
        IReadOnlyList<bool> solos,
        out double peakDetected)
    {
        if (stems == null || stems.Count == 0)
        {
            peakDetected = 0;
            return Array.Empty<float>();
        }

        int maxLength = stems.Max(s => s.Length);
        float[] mix = new float[maxLength];
        bool anySoloActive = solos.Any(s => s);

        for (int i = 0; i < maxLength; i++)
        {
            float sampleSum = 0.0f;
            for (int s = 0; s < stems.Count; s++)
            {
                // Si hay solo activo, solo participan las pistas en solo
                if (anySoloActive && !solos[s]) continue;
                // Si la pista está muteada, no suena
                if (mutes[s]) continue;

                if (i < stems[s].Length)
                {
                    float vol = (s < volumes.Count) ? volumes[s] : 1.0f;
                    sampleSum += stems[s][i] * vol;
                }
            }
            mix[i] = sampleSum;
        }

        // Calcular pico
        double maxPeak = 0.0;
        for (int i = 0; i < mix.Length; i++)
        {
            double abs = Math.Abs(mix[i]);
            if (abs > maxPeak) maxPeak = abs;
        }

        peakDetected = maxPeak;
        return mix;
    }

    /// <summary>
    /// Valida que la suma de todas las pistas y el complemento reconstruyan el original con error mínimo.
    /// </summary>
    public static double VerifyReconstructionError(float[] original, IReadOnlyList<float[]> allStemsAndComplement)
    {
        if (original == null || allStemsAndComplement == null || allStemsAndComplement.Count == 0)
            return 0.0;

        int length = original.Length;
        double maxDiff = 0.0;

        for (int i = 0; i < length; i++)
        {
            float sum = 0.0f;
            for (int s = 0; s < allStemsAndComplement.Count; s++)
            {
                if (i < allStemsAndComplement[s].Length)
                {
                    sum += allStemsAndComplement[s][i];
                }
            }

            double diff = Math.Abs(original[i] - sum);
            if (diff > maxDiff) maxDiff = diff;
        }

        return maxDiff;
    }
}
