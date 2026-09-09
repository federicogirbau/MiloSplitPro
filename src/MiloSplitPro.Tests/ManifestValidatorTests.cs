using System.IO;
using FluentAssertions;
using MiloSplitPro.Core.Services;
using Xunit;

namespace MiloSplitPro.Tests;

public class ManifestValidatorTests
{
    [Fact]
    public void ValidateManifest_ValidJson_ReturnsSuccess()
    {
        var json = @"{
          ""schemaVersion"": ""1.0.0"",
          ""appName"": ""Milo Split Pro"",
          ""verificationMode"": ""FailClosed"",
          ""models"": [
            {
              ""id"": ""htdemucs_ft_4s"",
              ""name"": ""HTDemucs Fine-Tuned (4 Stems)"",
              ""version"": ""v4.0.0"",
              ""author"": ""Alexandre Défossez"",
              ""origin"": ""https://github.com/facebookresearch/demucs"",
              ""license"": ""MIT"",
              ""licenseEvidence"": ""https://github.com/facebookresearch/demucs/blob/main/LICENSE"",
              ""commercialUseAllowed"": true,
              ""redistributionAllowed"": true,
              ""capabilities"": [""vocals"", ""drums"", ""bass"", ""other""],
              ""relativePath"": ""models/htdemucs_ft.yaml"",
              ""sha256"": ""f7e8a9390234c7b8c80521e1a8a3df54fbe87bb2e882410a80e4b85c1860d5b2"",
              ""expectedSizeBytes"": 83886080,
              ""sampleRate"": 44100,
              ""channels"": 2
            }
          ]
        }";

        var result = ManifestValidator.ValidateManifest(json);

        result.IsValid.Should().BeTrue();
        result.ValidatedModels.Should().HaveCount(1);
        result.ValidatedModels[0].Id.Should().Be("htdemucs_ft_4s");
    }

    [Fact]
    public void ValidateManifest_MissingRequiredLicense_FailsValidation()
    {
        var json = @"{
          ""schemaVersion"": ""1.0.0"",
          ""models"": [
            {
              ""id"": ""unauthorized_model"",
              ""name"": ""No License Model"",
              ""version"": ""v1.0.0"",
              ""license"": """",
              ""sha256"": ""abc123"",
              ""capabilities"": [""vocals""]
            }
          ]
        }";

        var result = ManifestValidator.ValidateManifest(json);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("carece de campos obligatorios");
    }
}
