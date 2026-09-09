using System.Text.Json.Serialization;

namespace MiloSplitPro.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StemCategory
{
    Vocals,
    LeadVocals,
    BackingVocals,
    VocalEffects,
    Noise,
    Drums,
    Kick,
    Snare,
    Toms,
    Cymbals,
    Bass,
    Guitar,
    AcousticGuitar,
    ElectricGuitar,
    Piano,
    Other
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum HardwareDevicePreference
{
    Auto,
    Cuda,
    Cpu
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SeparationStage
{
    Initializing,
    ValidatingFile,
    VerifyingModels,
    LoadingModel,
    SeparatingAudio,
    CalculatingComplement,
    NormalizingStems,
    ExportingWav,
    Completed,
    Cancelled,
    Failed
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AudioOutputFormat
{
    MP3,
    WAV,
    FLAC,
    OGG
}
