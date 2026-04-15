using System.Diagnostics;
using System.IO;

namespace ReelsConverterUI.Services;

public sealed class BackendLauncher : IDisposable
{
    private Process? _process;

    public event Action<string>? OutputReceived;

    public void Start()
    {
        var solutionDir = FindSolutionDir();
        var serverPy = Path.Combine(solutionDir, "backend", "server.py");

        if (!File.Exists(serverPy))
            throw new FileNotFoundException($"Backend not found: {serverPy}");

        var (pythonExe, versionArg) = FindPythonExe();
        var arguments = string.IsNullOrEmpty(versionArg)
            ? $"-u \"{serverPy}\""
            : $"{versionArg} -u \"{serverPy}\"";

        _process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = pythonExe,
                Arguments = arguments,
                WorkingDirectory = Path.GetDirectoryName(serverPy)!,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
            },
            EnableRaisingEvents = true,
        };
        _process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null) OutputReceived?.Invoke(e.Data);
        };
        _process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null) OutputReceived?.Invoke($"[stderr] {e.Data}");
        };
        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
    }

    public void SendInput(string text)
    {
        if (_process is { HasExited: false })
            _process.StandardInput.WriteLine(text);
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

    private static (string exe, string versionArg) FindPythonExe()
    {
        // Prefer the stable Python 3.12 via absolute path, then py launcher,
        // then generic fallbacks.
        var py312 = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            @"Programs\Python\Python312\python.exe");

        var candidates = new (string exe, string testArgs, string versionArg)[]
        {
            (py312, "--version", ""),
            ("py", "-3.12 --version", "-3.12"),
            ("python", "--version", ""),
            ("py", "--version", ""),
            ("python3", "--version", ""),
        };

        foreach (var (exe, testArgs, versionArg) in candidates)
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = testArgs,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                });
                if (p is not null)
                {
                    p.WaitForExit(3000);
                    if (p.ExitCode == 0)
                        return (exe, versionArg);
                }
            }
            catch { }
        }
        throw new FileNotFoundException(
            "Python wurde nicht gefunden. Bitte Python installieren und zum PATH hinzufügen.");
    }
}
