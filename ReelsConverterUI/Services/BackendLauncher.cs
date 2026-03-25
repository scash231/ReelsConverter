using System.Diagnostics;
using System.IO;

namespace ReelsConverterUI.Services;

public sealed class BackendLauncher : IDisposable
{
    private Process? _process;

    public void Start()
    {
        var solutionDir = FindSolutionDir();
        var serverPy = Path.Combine(solutionDir, "backend", "server.py");

        if (!File.Exists(serverPy))
            throw new FileNotFoundException($"Backend not found: {serverPy}");

        var python = FindPythonExe();

        _process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = python,
                Arguments = $"\"{serverPy}\"",
                WorkingDirectory = Path.GetDirectoryName(serverPy)!,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
            EnableRaisingEvents = true,
        };
        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
    }

    public void Dispose()
    {
        if (_process is { HasExited: false })
        {
            try { _process.Kill(entireProcessTree: true); } catch { }
        }
        _process?.Dispose();
    }

    private static string FindSolutionDir()
    {
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        for (int i = 0; i < 8; i++)
        {
            if (Directory.Exists(Path.Combine(dir, "backend")))
                return dir;
            var parent = Directory.GetParent(dir);
            if (parent is null) break;
            dir = parent.FullName;
        }
        return AppDomain.CurrentDomain.BaseDirectory;
    }

    private static string FindPythonExe()
    {
        foreach (var name in new[] { "python", "py", "python3" })
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo
                {
                    FileName = name,
                    Arguments = "--version",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                });
                if (p is not null)
                {
                    p.WaitForExit(3000);
                    return name;
                }
            }
            catch { }
        }
        throw new FileNotFoundException(
            "Python wurde nicht gefunden. Bitte Python installieren und zum PATH hinzufügen.");
    }
}
