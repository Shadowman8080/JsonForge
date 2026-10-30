using System.Diagnostics;

namespace JsonForge.Services;

public static class OllamaLocator
{
    public static bool TryFindExe(out string exePath)
    {
        exePath = string.Empty;

        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        // Common Windows install locations.
        var candidates = new List<string>
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Ollama", "ollama.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Ollama", "ollama.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Ollama", "ollama.exe"),
        };

        foreach (var c in candidates)
        {
            if (File.Exists(c))
            {
                exePath = c;
                return true;
            }
        }

        // Fallback: resolve from PATH.
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "where",
                Arguments = "ollama",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var p = Process.Start(psi);
            if (p is null)
            {
                return false;
            }

            var output = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit(1000);

            var first = output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(first) && File.Exists(first))
            {
                exePath = first;
                return true;
            }
        }
        catch
        {
            // ignore
        }

        return false;
    }
}
