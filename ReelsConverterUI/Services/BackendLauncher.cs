using System.Diagnostics;
using System.IO;

namespace ReelsConverterUI.Services;

public sealed class BackendLauncher : IDisposable
{
    private Process? _process;

    public event Action<string>? OutputReceived;

    public void Start()
    {
        KillProcessOnPort(8765);

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
        // 1. Neben der .exe suchen (funktioniert bei Published/Release)
        var exeDir = Path.GetDirectoryName(Environment.ProcessPath);
        if (exeDir is not null && Directory.Exists(Path.Combine(exeDir, "backend")))
            return exeDir;

        // 2. Fallback: von BaseDirectory nach oben laufen (funktioniert in Dev/Debug)
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        for (int i = 0; i < 8; i++)
        {
            if (Directory.Exists(Path.Combine(dir, "backend")))
                return dir;
            var parent = Directory.GetParent(dir);
            if (parent is null) break;
            dir = parent.FullName;
        }

        return exeDir ?? AppDomain.CurrentDomain.BaseDirectory;
    }

    private static (string exe, string versionArg) FindPythonExe()
    {
        var py312 = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            @"Programs\Python\Python312\python.exe");

        var list = new System.Collections.Generic.List<(string exe, string testArgs, string versionArg)>
        {
            (py312, "--version", "")
        };

        var pathVal = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathVal))
        {
            foreach (var part in pathVal.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var clean = part.Trim().Trim('"');
                if (string.IsNullOrEmpty(clean)) continue;
                if (clean.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    var fullPath = Path.Combine(clean, "python.exe");
                    if (File.Exists(fullPath))
                    {
                        list.Add((fullPath, "--version", ""));
                    }
                }
                catch { }
            }
        }

        list.Add(("py", "-3.12 --version", "-3.12"));
        list.Add(("python", "--version", ""));
        list.Add(("py", "--version", ""));
        list.Add(("python3", "--version", ""));

        foreach (var (exe, testArgs, versionArg) in list)
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

    private static void KillProcessOnPort(int port)
    {
        try
        {
            var pids = GetPidsUsingPort(port);
            foreach (var pid in pids)
            {
                try
                {
                    using var proc = Process.GetProcessById(pid);
                    var name = proc.ProcessName.ToLowerInvariant();
                    if (name.Contains("python") || name.Contains("uvicorn"))
                    {
                        proc.Kill(true);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to kill process {pid}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error killing process on port {port}: {ex.Message}");
        }
    }

    private static System.Collections.Generic.List<int> GetPidsUsingPort(int port)
    {
        var pids = new System.Collections.Generic.List<int>();
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "netstat.exe",
                Arguments = "-ano",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using var proc = Process.Start(startInfo);
            if (proc == null) return pids;

            string? line;
            while ((line = proc.StandardOutput.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                
                var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 4) continue;

                var localAddress = parts[1];
                if (localAddress.EndsWith($":{port}") || localAddress.EndsWith($"]:{port}"))
                {
                    var pidStr = parts[parts.Length - 1];
                    if (int.TryParse(pidStr, out int pid) && pid > 0)
                    {
                        if (!pids.Contains(pid))
                        {
                            pids.Add(pid);
                        }
                    }
                }
            }
            proc.WaitForExit();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error running netstat: {ex.Message}");
        }
        return pids;
    }
}
