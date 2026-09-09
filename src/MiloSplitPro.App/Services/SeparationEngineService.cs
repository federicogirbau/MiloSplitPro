using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MiloSplitPro.Core.Models;

namespace MiloSplitPro.App.Services;

public class SeparationEngineService
{
    private Process? _currentProcess;
    private readonly string _pythonExePath;
    private readonly string _engineScriptPath;
    private readonly string _manifestPath;

    public SeparationEngineService()
    {
        var appBaseDir = AppDomain.CurrentDomain.BaseDirectory;
        var embeddedPython = Path.Combine(appBaseDir, "runtime", "python", "python.exe");

        // Prefer embedded runtime if present, otherwise fallback to system python
        _pythonExePath = File.Exists(embeddedPython) ? embeddedPython : "python.exe";

        // Locate engine script
        var devEngineScript = Path.GetFullPath(Path.Combine(appBaseDir, "..", "..", "..", "..", "MiloSplitPro.Engine", "engine_main.py"));
        var prodEngineScript = Path.Combine(appBaseDir, "engine", "engine_main.py");
        _engineScriptPath = File.Exists(prodEngineScript) ? prodEngineScript : devEngineScript;

        var devManifest = Path.GetFullPath(Path.Combine(appBaseDir, "..", "..", "..", "..", "MiloSplitPro.Core", "Manifests", "models.manifest.json"));
        var prodManifest = Path.Combine(appBaseDir, "models.manifest.json");
        _manifestPath = File.Exists(prodManifest) ? prodManifest : devManifest;
    }

    public async Task<SeparationResult> ExecuteSeparationAsync(
        SeparationRequest request,
        IProgress<SeparationProgressEvent> progress,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(request.InputFilePath))
        {
            throw new FileNotFoundException("El archivo de audio no existe", request.InputFilePath);
        }

        Directory.CreateDirectory(request.OutputDirectory);

        var stemsArg = string.Join(",", request.SelectedStems.Select(s => s.ToString()));
        var deviceArg = request.HardwarePreference.ToString().ToLowerInvariant();

        var psi = new ProcessStartInfo
        {
            FileName = _pythonExePath,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
            CreateNoWindow = true
        };

        var isEmbedded = _pythonExePath.Contains("runtime");
        if (isEmbedded)
        {
            psi.EnvironmentVariables["PYTHONNOUSERSITE"] = "1";
        }
        psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
        psi.EnvironmentVariables["PYTHONUTF8"] = "1";

        psi.ArgumentList.Add(_engineScriptPath);
        psi.ArgumentList.Add("--input");
        psi.ArgumentList.Add(request.InputFilePath);
        psi.ArgumentList.Add("--output-dir");
        psi.ArgumentList.Add(request.OutputDirectory);
        psi.ArgumentList.Add("--stems");
        psi.ArgumentList.Add(stemsArg);
        psi.ArgumentList.Add("--device");
        psi.ArgumentList.Add(deviceArg);
        psi.ArgumentList.Add("--format");
        psi.ArgumentList.Add(request.OutputFormat.ToString().ToLowerInvariant());

        if (File.Exists(_manifestPath))
        {
            psi.ArgumentList.Add("--manifest");
            psi.ArgumentList.Add(_manifestPath);
        }

        if (request.GenerateComplementOther)
        {
            psi.ArgumentList.Add("--complement");
        }

        var process = new Process { StartInfo = psi };
        _currentProcess = process;

        var stdoutTcs = new TaskCompletionSource<SeparationResult>();
        var separatedStems = new List<SeparatedStemInfo>();
        var sw = Stopwatch.StartNew();
        string deviceUsed = "CPU";

        process.OutputDataReceived += (sender, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data)) return;

            try
            {
                using var doc = JsonDocument.Parse(e.Data);
                var root = doc.RootElement;

                var stageStr = root.TryGetProperty("stage", out var sProp) ? sProp.GetString() : "SeparatingAudio";
                Enum.TryParse<SeparationStage>(stageStr, true, out var stage);

                var progressPct = root.TryGetProperty("progress", out var pProp) ? pProp.GetDouble() : 0.0;
                var msg = root.TryGetProperty("message", out var mProp) ? mProp.GetString() ?? "" : "";
                var stem = root.TryGetProperty("stem", out var stProp) ? stProp.GetString() : null;
                var model = root.TryGetProperty("model", out var mdProp) ? mdProp.GetString() : null;
                var dev = root.TryGetProperty("device", out var dProp) ? dProp.GetString() : null;
                var error = root.TryGetProperty("errorDetails", out var errProp) ? errProp.GetString() : null;

                if (!string.IsNullOrEmpty(dev)) deviceUsed = dev;

                progress.Report(new SeparationProgressEvent(
                    stage,
                    progressPct,
                    msg,
                    stem,
                    model,
                    dev,
                    error
                ));

                if (root.TryGetProperty("result", out var resProp))
                {
                    if (resProp.TryGetProperty("stems", out var stemsArray))
                    {
                        foreach (var stemEl in stemsArray.EnumerateArray())
                        {
                            var catStr = stemEl.GetProperty("category").GetString() ?? "Other";
                            Enum.TryParse<StemCategory>(catStr, true, out var category);
                            var stemName = stemEl.GetProperty("stemName").GetString() ?? catStr;
                            var path = stemEl.GetProperty("filePath").GetString() ?? "";
                            var durationSec = stemEl.GetProperty("duration").GetDouble();
                            var sampleRate = stemEl.GetProperty("sampleRate").GetInt32();
                            var channels = stemEl.GetProperty("channels").GetInt32();
                            var peak = stemEl.GetProperty("peakAmplitude").GetDouble();
                            var rms = stemEl.GetProperty("rmsEnergy").GetDouble();

                            separatedStems.Add(new SeparatedStemInfo(
                                category,
                                stemName,
                                path,
                                TimeSpan.FromSeconds(durationSec),
                                sampleRate,
                                channels,
                                peak,
                                rms
                            ));
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Non-JSON telemetry line
            }
        };

        var errorOutput = new List<string>();
        process.ErrorDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                errorOutput.Add(e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // Registration for cancellation
        using var reg = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.StandardInput.WriteLine("CANCEL");
                    process.StandardInput.Flush();
                    Task.Delay(500).Wait();
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
            }
            catch (Exception)
            {
                // Ignored during cleanup
            }
        });

        await process.WaitForExitAsync(cancellationToken);
        sw.Stop();

        if (process.ExitCode == 0)
        {
            return new SeparationResult(
                true,
                request.OutputDirectory,
                separatedStems,
                sw.Elapsed,
                deviceUsed
            );
        }
        else if (cancellationToken.IsCancellationRequested)
        {
            return new SeparationResult(
                false,
                request.OutputDirectory,
                Array.Empty<SeparatedStemInfo>(),
                sw.Elapsed,
                deviceUsed,
                "La operación fue cancelada por el usuario."
            );
        }
        else
        {
            var errStr = string.Join(Environment.NewLine, errorOutput);
            return new SeparationResult(
                false,
                request.OutputDirectory,
                Array.Empty<SeparatedStemInfo>(),
                sw.Elapsed,
                deviceUsed,
                $"Error en motor de separación (código {process.ExitCode}): {errStr}"
            );
        }
    }

    public void CancelCurrentOperation()
    {
        try
        {
            if (_currentProcess != null && !_currentProcess.HasExited)
            {
                _currentProcess.StandardInput.WriteLine("CANCEL");
                _currentProcess.StandardInput.Flush();
                _currentProcess.Kill(entireProcessTree: true);
            }
        }
        catch (Exception)
        {
        }
    }
}
