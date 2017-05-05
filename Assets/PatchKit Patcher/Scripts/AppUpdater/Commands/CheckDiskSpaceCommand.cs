﻿using System.IO;
using PatchKit.Api.Models.Main;
using PatchKit.Unity.Patcher.Cancellation;
using PatchKit.Unity.Patcher.Debug;
using PatchKit.Unity.Patcher.Status;

namespace PatchKit.Unity.Patcher.AppUpdater.Commands
{
    public class CheckDiskSpaceCommand : ICheckDiskSpace
    {
        private const int Reserve = 1024 * 1024 * 20; // 20 megabytes of reserve

        private static readonly DebugLogger DebugLogger = new DebugLogger(typeof(CheckDiskSpaceCommand));

        private readonly AppContentSummary? _contentSummary;
        private readonly AppDiffSummary? _diffSummary;
        private readonly string _localDirectoryPath;
        private readonly long _bigestFileSize;

        public CheckDiskSpaceCommand(AppContentSummary contentSummary, string localDirectoryPath)
        {
            Checks.ArgumentNotNull(localDirectoryPath, "localDirectoryPath");

            _contentSummary = contentSummary;
            _localDirectoryPath = localDirectoryPath;
        }

        public CheckDiskSpaceCommand(AppDiffSummary diffSummary, string localDirectoryPath, long bigestFileSize)
        {
            Checks.ArgumentNotNull(localDirectoryPath, "localDirectoryPath");

            _diffSummary = diffSummary;
            _localDirectoryPath = localDirectoryPath;
            _bigestFileSize = bigestFileSize;
        }

        public void Execute(CancellationToken cancellationToken)
        {
            // TODO: DriveInfo constructor is throwing NotImplementedException on Windows
            // http://redmine.patchkit.net/issues/454?issue_count=25&issue_position=2&next_issue_id=322&prev_issue_id=455#note-3
            // Will have to get around that somehow

            /*var requiredDiskSpace = GetRequiredDiskSpace();

            var dir = new FileInfo(_localDirectoryPath);
            var drive = new DriveInfo(dir.Directory.Root.FullName);

            if (drive.AvailableFreeSpace >= requiredDiskSpace)
            {
                DebugLogger.Log("Available free space " + drive.AvailableFreeSpace + " >= required disk space " +
                                requiredDiskSpace);
            }
            else
            {
                throw new NotEnoughtDiskSpaceException("There's no enough disk space to install/update this application. " +
                                                       "Available free space " + drive.AvailableFreeSpace +
                                                       " < required disk space " + requiredDiskSpace);
            }*/
        }

        private long GetRequiredDiskSpace()
        {
            if (UseContentSummary())
            {
                long requiredDiskSpaceForContent = GetRequiredDiskSpaceForContent();
                return requiredDiskSpaceForContent;
            }

            long requiredDiskSpaceForDiff = GetRequiredDiskSpaceForDiff();
            return requiredDiskSpaceForDiff;
        }

        private bool UseContentSummary()
        {
            return _contentSummary != null;
        }

        private long GetRequiredDiskSpaceForContent()
        {
            long requiredDiskSpace = _contentSummary.Value.Size + _contentSummary.Value.Size + Reserve;
            return requiredDiskSpace;
        }

        private long GetRequiredDiskSpaceForDiff()
        {
            long requiredDiskSpace = _diffSummary.Value.Size + _diffSummary.Value.Size + _bigestFileSize + Reserve;
            return requiredDiskSpace;
        }

        public void Prepare(IStatusMonitor statusMonitor)
        {
            // do nothing
        }
    }
}