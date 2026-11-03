using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using Lyricod.Core;
using Microsoft.Win32;

namespace Lyricod.Platform;

/// <summary>NetEase Cloud Music on this PC: its folder and version, and whether sync is set up there.</summary>
public sealed record NeteaseInstall(string Folder, Version Version, bool Is64Bit, bool SyncInstalled)
{
    public string Exe => Path.Combine(Folder, NeteaseSetup.ExeName);
}

/// <summary>
/// Sets up NetEase Cloud Music sync through Lyricod CM Helper (see <see cref="NeteaseSync"/>): downloads
/// the release of the helper this Lyricod was made with, checks it against the SHA-256 Lyricod expects
/// (which GitHub must list too), runs it as administrator, then opens NetEase Cloud Music again if it was open.
/// </summary>
public static class NeteaseSetup
{
    public const string ExeName = "cloudmusic.exe";
    const string LoaderName = "msimg32.dll";

    /// <summary>The installed NetEase Cloud Music, or null when there's none Lyricod can find.</summary>
    public static NeteaseInstall? Find()
    {
        try
        {
            string? folder = AppPath(RegistryHive.LocalMachine, RegistryView.Registry64) ?? AppPath(RegistryHive.LocalMachine, RegistryView.Registry32)
                ?? AppPath(RegistryHive.CurrentUser, RegistryView.Default) ?? RunningFolder();
            if (folder == null) return null;
            string exe = Path.Combine(folder, ExeName);
            if (!File.Exists(exe)) return null;
            var info = FileVersionInfo.GetVersionInfo(exe);
            var version = new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart);
            bool is64Bit;
            using (var file = File.OpenRead(exe)) is64Bit = NeteaseSync.Machine(file) == NeteaseSync.X64;
            bool installed = File.Exists(Path.Combine(folder, LoaderName)) && NeteaseSync.HasInfLink(DataFolder());
            return new NeteaseInstall(folder, version, is64Bit, installed);
        }
        catch (Exception) { return null; }
    }

    /// <summary>Where installers register cloudmusic.exe (32-bit installers under the 32-bit view).</summary>
    static string? AppPath(RegistryHive hive, RegistryView view)
    {
        using var root = RegistryKey.OpenBaseKey(hive, view);
        using var key = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + ExeName);
        if (key?.GetValue(null) is not string path) return null;
        path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        return Path.GetDirectoryName(path);
    }

    static string? RunningFolder()
    {
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ExeName)))
        {
            using (process)
            {
                try { if (Path.GetDirectoryName(process.MainModule?.FileName) is { } folder) return folder; }
                catch (Exception) { /* another user's, or a different bitness */ }
            }
        }
        return null;
    }

    /// <summary>
    /// BetterNCM's data folder, found as BetterNCM finds it: the BETTERNCM_PROFILE environment variable
    /// (which BetterNCM's installer sets when you change the folder), else C:\betterncm. Read from
    /// Windows' saved settings first, the user's over the machine's as NetEase Cloud Music sees them:
    /// they're current even if the folder changed after Lyricod started, unlike Lyricod's own copy.
    /// </summary>
    public static string DataFolder()
    {
        try
        {
            if (Registry.CurrentUser.OpenSubKey("Environment") is { } user)
                using (user) if (user.GetValue("BETTERNCM_PROFILE") is string path && path.Length > 0) return Environment.ExpandEnvironmentVariables(path);
            using var machine = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Environment");
            if (machine?.GetValue("BETTERNCM_PROFILE") is string shared && shared.Length > 0) return Environment.ExpandEnvironmentVariables(shared);
        }
        catch (Exception) { /* Lyricod's own copy, then */ }
        return Environment.GetEnvironmentVariable("BETTERNCM_PROFILE") is { Length: > 0 } inherited ? inherited : @"C:\betterncm";
    }

    /// <summary>Installs (or removes) BetterNCM and InfLink-rs through the helper. Returns the result and, when it failed, why.</summary>
    public static async Task<(NeteaseResult Result, string? Error)> RunAsync(NeteaseInstall netease, bool remove, IProgress<NeteaseStage> progress)
    {
        var work = Directory.CreateTempSubdirectory("Lyricod-CM-Helper-");
        try
        {
            progress.Report(NeteaseStage.Downloading);
            string? helper = await DownloadHelperAsync(work.FullName);
            if (helper == null) return (NeteaseResult.CouldntDownload, null);

            progress.Report(NeteaseStage.WaitingForPermission);
            bool wasOpen = IsOpen();
            string result = Path.Combine(work.FullName, "result.json");
            var start = new ProcessStartInfo(helper)
            {
                UseShellExecute = true,
                Verb = "runas",
                Arguments = NeteaseSync.Arguments(remove, netease.Folder, DataFolder(), result),
                WorkingDirectory = work.FullName,
            };
            Process? process;
            try { process = Process.Start(start); }
            catch (Win32Exception e) when (e.NativeErrorCode == 1223) { return (NeteaseResult.Cancelled, null); } // ERROR_CANCELLED: said no
            if (process == null) return (NeteaseResult.Failed, "The helper didn't start.");
            int exitCode;
            using (process)
            {
                await process.WaitForExitAsync();
                exitCode = process.ExitCode;
            }
            string? json = File.Exists(result) ? await File.ReadAllTextAsync(result) : null;
            var outcome = NeteaseSync.ReadResult(json, exitCode);
            // The helper closes NetEase Cloud Music to change its files; open it again, as the signed-in user.
            if (wasOpen && !IsOpen())
            {
                try { Process.Start(new ProcessStartInfo(netease.Exe) { UseShellExecute = true, WorkingDirectory = netease.Folder })?.Dispose(); }
                catch (Exception) { /* opened by hand, then */ }
            }
            return outcome.Ok ? (NeteaseResult.Done, null) : (NeteaseResult.Failed, outcome.Error);
        }
        catch (Exception e) { return (NeteaseResult.Failed, e.Message); }
        finally
        {
            try { work.Delete(true); } catch (Exception) { /* left in the temp folder */ }
        }
    }

    static bool IsOpen()
    {
        var processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ExeName));
        foreach (var process in processes) process.Dispose();
        return processes.Length > 0;
    }

    /// <summary>The helper from its latest release, checked against its SHA-256; null when it can't be had or doesn't match.</summary>
    static async Task<string?> DownloadHelperAsync(string folder)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd(UpdateCheck.UserAgent(UpdateChecker.Version));
            using var request = new HttpRequestMessage(HttpMethod.Get, NeteaseSync.HelperEndpoint);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var reply = await http.SendAsync(request);
            if (!reply.IsSuccessStatusCode || NeteaseSync.FindHelper(await reply.Content.ReadAsStringAsync()) is not { } helper
                || !NeteaseSync.IsPinned(helper)) return null;
            string path = Path.Combine(folder, NeteaseSync.HelperName);
            using (var response = await http.GetAsync(helper.Url, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                if (response.RequestMessage?.RequestUri?.Scheme != "https" || response.Content.Headers.ContentLength is { } length && length != helper.Size)
                    return null;
                await using var body = await response.Content.ReadAsStreamAsync();
                await using var file = File.Create(path);
                await body.CopyToAsync(file);
            }
            await using (var file = File.OpenRead(path))
            {
                if (file.Length != helper.Size) return null;
                string hash = Convert.ToHexString(await SHA256.HashDataAsync(file));
                if (!hash.Equals(helper.Sha256, StringComparison.OrdinalIgnoreCase)) return null;
            }
            return path;
        }
        catch (Exception) { return null; } // offline, GitHub unreachable, or a cut-off download
    }
}
