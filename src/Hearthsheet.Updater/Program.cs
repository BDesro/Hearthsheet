using System.Diagnostics;
using Hearthsheet.Updater;

// Replaces the application's install folder with a verified, staged update after the app exits,
// then restarts it. Runs from a copy outside the install folder so it can overwrite everything.
// A windowless exe: the user sees nothing until the app comes back, so every path below relaunches it.
var options = UpdaterOptions.Parse(args);
if (options is null)
{
    Console.Error.WriteLine("Usage: Hearthsheet.Updater --pid <id> --source <dir> --target <dir> --exe <name> --log <dir>");
    return 2;
}

using var log = new UpdaterLog(options.LogDirectory);
var exitCode = 1;
try
{
    log.Info($"Waiting for process {options.ProcessId} to exit");
    if (!WaitForExit(options.ProcessId, TimeSpan.FromSeconds(60)))
    {
        log.Error("The application did not exit within 60 seconds; update aborted, nothing was changed.");
    }
    else
    {
        var result = new UpdateInstaller(log).Install(options.SourceDirectory, options.TargetDirectory, options.Executable);
        log.Info(result.Succeeded ? "Update installed" : $"Update failed and was rolled back: {result.Error}");
        if (result.Succeeded) exitCode = 0;
    }
}
catch (Exception ex)
{
    log.Error("Updater crashed: " + ex);
}

try
{
    Process.Start(new ProcessStartInfo(Path.Combine(options.TargetDirectory, options.Executable))
    {
        UseShellExecute = false,
        WorkingDirectory = options.TargetDirectory,
    });
}
catch (Exception ex)
{
    log.Error("Could not restart the application: " + ex.Message);
}
return exitCode;

static bool WaitForExit(int pid, TimeSpan timeout)
{
    try
    {
        using var process = Process.GetProcessById(pid);
        return process.WaitForExit(timeout);
    }
    catch (ArgumentException)
    {
        return true; // Already exited.
    }
}
