using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using MiloSplitPro.Core.Models;

namespace MiloSplitPro.Core.Services;

public class ModelEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("author")]
    public string Author { get; set; } = string.Empty;

    [JsonPropertyName("origin")]
    public string Origin { get; set; } = string.Empty;

    [JsonPropertyName("license")]
    public string License { get; set; } = string.Empty;

    [JsonPropertyName("licenseEvidence")]
    public string LicenseEvidence { get; set; } = string.Empty;

    [JsonPropertyName("commercialUseAllowed")]
    public bool CommercialUseAllowed { get; set; }

    [JsonPropertyName("redistributionAllowed")]
    public bool RedistributionAllowed { get; set; }

    [JsonPropertyName("capabilities")]
    public List<string> Capabilities { get; set; } = new();

    [JsonPropertyName("relativePath")]
    public string RelativePath { get; set; } = string.Empty;

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;

    [JsonPropertyName("expectedSizeBytes")]
    public long ExpectedSizeBytes { get; set; }

    [JsonPropertyName("sampleRate")]
    public int SampleRate { get; set; } = 44100;

    [JsonPropertyName("channels")]
    public int Channels { get; set; } = 2;
}

public class ManifestContainer
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = "1.0.0";

    [JsonPropertyName("appName")]
    public string AppName { get; set; } = "Milo Split Pro";

    [JsonPropertyName("verificationMode")]
    public string VerificationMode { get; set; } = "FailClosed";

    [JsonPropertyName("models")]
    public List<ModelEntry> Models { get; set; } = new();
}

public class ManifestValidationResult
{
    public bool IsValid { get; init; }
    public string? ErrorMessage { get; init; }
    public IReadOnlyList<ModelEntry> ValidatedModels { get; init; } = Array.Empty<ModelEntry>();
    public IReadOnlyList<string> MissingFiles { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> CorruptedFiles { get; init; } = Array.Empty<string>();
}

public class ManifestValidator
{
    public static ManifestValidationResult ValidateManifest(string manifestJson, string? baseDirectory = null)
    {
        try
        {
            var container = JsonSerializer.Deserialize<ManifestContainer>(manifestJson);
            if (container == null || container.Models.Count == 0)
            {
                return new ManifestValidationResult
                {
                    IsValid = false,
                    ErrorMessage = "El manifiesto de modelos está vacío o no tiene un formato JSON válido."
                };
            }

            var missing = new List<string>();
            var corrupted = new List<string>();
            var validated = new List<ModelEntry>();

            foreach (var model in container.Models)
            {
                // Validación de metadatos obligatorios
                if (string.IsNullOrWhiteSpace(model.Id) ||
                    string.IsNullOrWhiteSpace(model.License) ||
                    string.IsNullOrWhiteSpace(model.Sha256) ||
                    model.Capabilities.Count == 0)
                {
                    return new ManifestValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = $"El modelo '{model.Name}' carece de campos obligatorios (ID, Licencia, Hash o Capacidades)."
                    };
                }

                // Verificación física de archivo si se proporciona baseDirectory y existe el archivo
                if (!string.IsNullOrEmpty(baseDirectory))
                {
                    var fullPath = Path.Combine(baseDirectory, model.RelativePath);
                    if (File.Exists(fullPath))
                    {
                        var actualHash = ComputeSha256(fullPath);
                        if (!string.Equals(actualHash, model.Sha256, StringComparison.OrdinalIgnoreCase))
                        {
                            corrupted.Add($"{model.Name} (Hash mismatch: esperado {model.Sha256}, actual {actualHash})");
                            continue;
                        }
                    }
                }

                validated.Add(model);
            }

            if (corrupted.Count > 0)
            {
                return new ManifestValidationResult
                {
                    IsValid = false,
                    ErrorMessage = $"Se detectaron archivos de modelo alterados o corruptos: {string.Join(", ", corrupted)}",
                    CorruptedFiles = corrupted
                };
            }

            return new ManifestValidationResult
            {
                IsValid = true,
                ValidatedModels = validated,
                MissingFiles = missing
            };
        }
        catch (Exception ex)
        {
            return new ManifestValidationResult
            {
                IsValid = false,
                ErrorMessage = $"Error al procesar el manifiesto: {ex.Message}"
            };
        }
    }

    public static string ComputeSha256(string filePath)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var bytes = sha256.ComputeHash(stream);
        return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
    }
}
