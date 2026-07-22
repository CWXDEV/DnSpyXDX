using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using DecompilerApp.Application;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp.ProjectDecompiler;
using ICSharpCode.Decompiler.Metadata;

namespace DecompilerApp.Export;

public sealed class ProjectExportService(IDecompilerBackend backend) : IProjectExportService
{
    public async Task<ExportReport> ExportAsync(ExportRequest request, IProgress<ExportProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var assemblies = request.SessionIds.Select(id => backend.TryGetAssembly(id, out var value) ? value : null).Where(x => x is not null).Cast<AssemblyDescriptor>().ToArray();
        if (assemblies.Length == 0) throw new InvalidOperationException("Select at least one open assembly.");
        var destination = Path.GetFullPath(request.Destination);
        if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any()) throw new IOException("The export destination must be empty.");
        Directory.CreateDirectory(destination);
        var staging = Path.Combine(destination, $".dnspyxdx-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        var projects = new List<string>(); var warnings = new List<string>();
        try
        {
            for (var i = 0; i < assemblies.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var assembly = assemblies[i];
                progress?.Report(new ExportProgress(0, 0, $"Preparing {assembly.Name}"));
                var safeName = Sanitize(assembly.Name);
                var projectDirectory = Path.Combine(staging, "src", safeName);
                Directory.CreateDirectory(projectDirectory);
                try
                {
                    using var module = new PEFile(assembly.Path, PEStreamOptions.PrefetchEntireImage);
                    var resolver = new UniversalAssemblyResolver(assembly.Path, false, module.DetectTargetFrameworkId());
                    resolver.AddSearchDirectory(Path.GetDirectoryName(assembly.Path)!);
                    var exporter = new WholeProjectDecompiler(resolver);
                    if (progress is not null)
                    {
                        exporter.ProgressIndicator = new CallbackProgress<DecompilationProgress>(update =>
                        {
                            var total = Math.Max(0, update.TotalUnits);
                            var completed = Math.Clamp(update.UnitsCompleted, 0, total);
                            var file = string.IsNullOrWhiteSpace(update.Status) ? "Decompiling source" : update.Status;
                            progress.Report(new ExportProgress(completed, total, $"{assembly.Name}: {file}"));
                        });
                    }
                    exporter.DecompileProject(module, projectDirectory, cancellationToken);
                    var csproj = Directory.EnumerateFiles(projectDirectory, "*.csproj").FirstOrDefault();
                    if (csproj is null) warnings.Add($"{assembly.Name}: no project file was generated.");
                    else projects.Add(Path.GetRelativePath(staging, csproj).Replace('\\', '/'));
                }
                catch (Exception ex) when (ex is not OperationCanceledException) { warnings.Add($"{assembly.Name}: {ex.Message}"); }
            }
            progress?.Report(new ExportProgress(0, 0, "Writing solution and extraction report"));
            await SlnxWriter.WriteAsync(Path.Combine(staging, "DnSpyXDXExport.slnx"), projects, cancellationToken);
            string? buildOutput = null;
            if (request.ValidateBuild && projects.Count > 0) buildOutput = await ValidateAsync(staging, cancellationToken);
            var report = new ExportReport(projects.Count > 0, destination, projects, warnings, buildOutput);
            await File.WriteAllTextAsync(Path.Combine(staging, "export-report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
            foreach (var entry in Directory.EnumerateFileSystemEntries(staging))
            {
                var target = Path.Combine(destination, Path.GetFileName(entry));
                if (Directory.Exists(entry)) Directory.Move(entry, target); else File.Move(entry, target);
            }
            Directory.Delete(staging);
            return report;
        }
        catch { if (Directory.Exists(staging)) Directory.Delete(staging, true); throw; }
    }

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var value = new string(name.Select(c => invalid.Contains(c) || c is '/' or '\\' ? '_' : c).ToArray()).Trim('.', ' ');
        return string.IsNullOrWhiteSpace(value) ? "Assembly" : value;
    }

    private static async Task<string> ValidateAsync(string directory, CancellationToken ct)
    {
        var start = new ProcessStartInfo("dotnet", "build DnSpyXDXExport.slnx --nologo") { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start dotnet.");
        var output = process.StandardOutput.ReadToEndAsync(ct); var error = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        return (await output) + (await error);
    }

    private sealed class CallbackProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }
}
