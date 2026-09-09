namespace MiloSplitPro.Core.Models;

public record AudioMetadata(
    string FilePath,
    string FileName,
    TimeSpan Duration,
    int SampleRate,
    int Channels,
    int BitsPerSample,
    string Format
);

public record SeparationRequest(
    string InputFilePath,
    string OutputDirectory,
    IReadOnlyList<StemCategory> SelectedStems,
    HardwareDevicePreference HardwarePreference,
    bool GenerateComplementOther = true,
    int SampleRate = 44100,
    AudioOutputFormat OutputFormat = AudioOutputFormat.MP3
);

public record SeparationProgressEvent(
    SeparationStage Stage,
    double ProgressPercentage,
    string Message,
    string? CurrentStem = null,
    string? ModelName = null,
    string? DeviceName = null,
    string? ErrorDetails = null
);

public record SeparatedStemInfo(
    StemCategory Category,
    string StemName,
    string FilePath,
    TimeSpan Duration,
    int SampleRate,
    int Channels,
    double PeakAmplitude,
    double RmsEnergy
);

public record SeparationResult(
    bool Success,
    string OutputDirectory,
    IReadOnlyList<SeparatedStemInfo> Stems,
    TimeSpan ProcessingTime,
    string DeviceUsed,
    string? ErrorMessage = null
);
