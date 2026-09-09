using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace MiloSplitPro.App.Services;

public class HardwareInfo
{
    public int CpuCores { get; set; }
    public double TotalRamGb { get; set; }
    public bool HasCudaGpu { get; set; }
    public string GpuName { get; set; } = "No detectada";
    public double GpuMemoryGb { get; set; }
    public string RecommendedDevice { get; set; } = "CPU";
}

public static class HardwareDetectionService
{
    public static HardwareInfo DetectHardware()
    {
        var info = new HardwareInfo
        {
            CpuCores = Environment.ProcessorCount,
            TotalRamGb = Math.Round(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024.0 * 1024.0 * 1024.0), 1),
            RecommendedDevice = "CPU"
        };

        try
        {
            // Query nvidia-smi for NVIDIA CUDA GPU
            var psi = new ProcessStartInfo
            {
                FileName = "nvidia-smi",
                Arguments = "--query-gpu=name,memory.total --format=csv,noheader,nounits",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                var output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit(2000);

                if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                {
                    var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    if (lines.Length > 0)
                    {
                        var parts = lines[0].Split(',');
                        if (parts.Length >= 2)
                        {
                            info.HasCudaGpu = true;
                            info.GpuName = parts[0].Trim();
                            if (double.TryParse(parts[1].Trim(), out var memMb))
                            {
                                info.GpuMemoryGb = Math.Round(memMb / 1024.0, 1);
                            }
                            info.RecommendedDevice = "CUDA (Aceleración por GPU)";
                        }
                    }
                }
            }
        }
        catch (Exception)
        {
            // nvidia-smi not in PATH or non-NVIDIA GPU
        }

        return info;
    }
}
