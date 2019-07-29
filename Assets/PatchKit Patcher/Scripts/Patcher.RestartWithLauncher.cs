using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using JetBrains.Annotations;
using UnityEngine;
using Debug = UnityEngine.Debug;

public partial class Patcher
{
    private async Task<bool> RestartWithLauncherAsync()
    {
        if (!CanPerformNewForegroundTask())
        {
            return false;
        }

        Debug.Log(message: "Restarting with launcher...");

        _hasRestartWithLauncherTask = true;
        SendStateChanged();

        try
        {
            var processStartInfo = GetLauncherProcessStartInfo();

            if (processStartInfo == null)
            {
                Debug.LogWarning(
                    message:
                    "Failed to restart with launcher: can't resolve process start info.");

                return false;
            }

            if (Process.Start(startInfo: processStartInfo) == null)
            {
                Debug.LogWarning(
                    message:
                    "Failed to restart with launcher: process hasn't started.");

                return false;
            }

            Debug.Log(message: "Successfully restarted with launcher.");
        }
        catch (Exception e)
        {
            Debug.LogError(
                message: "Failed to restart with launcher: unknown error.");
            Debug.LogException(exception: e);

            return false;
        }
        finally
        {
            _hasRestartWithLauncherTask = false;
            SendStateChanged();
        }

        await QuitAsync();

        return true;
    }

    private const string LauncherPathFileName = "launcher_path";

    [NotNull]
    private string GetDefaultLauncherName()
    {
        switch (Application.platform)
        {
            case RuntimePlatform.LinuxPlayer:
                return "Launcher";
            case RuntimePlatform.WindowsPlayer:
                return "Launcher.exe";
            case RuntimePlatform.OSXPlayer:
                return "Launcher.app";
            default:
                throw new NotSupportedException();
        }
    }

    private string GetLauncherPath()
    {
        string patcherRootPath = Path.GetDirectoryName(path: Application.dataPath);

        string launcherPathFilePath = Path.Combine(
            path1: patcherRootPath,
            path2: LauncherPathFileName);

        Debug.Log(message: $"Retrieving launcher path from file {launcherPathFilePath} ...");

        if (File.Exists(path: launcherPathFilePath))
        {
            string launcherPath = File.ReadAllText(path: launcherPathFilePath);

            Debug.Log(message: $"Got launcher path {launcherPath} from file.");

            if (File.Exists(path: launcherPath))
            {
                return launcherPath;
            }
        }
        else
        {
            Debug.Log("Cannot reterive launcher path from file - file doesn't exist.");
        }

        string defaultLauncherPath = Path.Combine(
            path1: Path.GetDirectoryName(patcherRootPath),
            path2: GetDefaultLauncherName());

        defaultLauncherPath = Path.GetFullPath(path: defaultLauncherPath);

        Debug.Log(message: $"Using default launcher path {defaultLauncherPath}");

        return File.Exists(path: defaultLauncherPath)
            ? defaultLauncherPath
            : null;
    }

    private ProcessStartInfo GetLauncherProcessStartInfo()
    {
        string launcherPath = GetLauncherPath();

        if (launcherPath == null)
        {
            return null;
        }

        //TODO: Expose in libpkapps function to check if file is executable
        /*if (!Files.IsExecutable(
            filePath: launcherPath))
        {
            return null;
        }*/

        switch (Application.platform)
        {
            case RuntimePlatform.LinuxPlayer:
                return new ProcessStartInfo
                {
                    FileName = launcherPath
                };
            case RuntimePlatform.WindowsPlayer:
                return new ProcessStartInfo
                {
                    FileName = launcherPath
                };
            case RuntimePlatform.OSXPlayer:
                return new ProcessStartInfo
                {
                    FileName = "open",
                    Arguments = $"\"{launcherPath}\""
                };
            default:
                throw new NotSupportedException();
        }
    }
}