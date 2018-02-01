﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JetBrains.Annotations;
using PatchKit.Api.Models.Main;
using PatchKit.Logging;
using PatchKit.Unity.Patcher.AppData;
using PatchKit.Unity.Patcher.AppData.Local;
using PatchKit.Unity.Patcher.AppData.Remote;
using PatchKit.Unity.Patcher.Cancellation;
using PatchKit.Unity.Patcher.Debug;
using PatchKit.Unity.Patcher.Status;
using PatchKit.Unity.Utilities;
using ILogger = PatchKit.Logging.ILogger;

namespace PatchKit.Unity.Patcher.AppUpdater.Commands
{
    public class InstallDiffCommand : BaseAppUpdaterCommand, IInstallDiffCommand
    {
        private const string Suffix = "_"; // FIX: Bug #714
        private readonly ILogger _logger;

        private readonly string _packagePath;
        private readonly string _packageMetaPath;
        private readonly string _packagePassword;
        private readonly int _versionId;
        private readonly ILocalDirectory _localData;
        private readonly ILocalMetaData _localMetaData;
        private readonly ITemporaryDirectory _temporaryData;
        private readonly IRemoteMetaData _remoteMetaData;

        private IGeneralStatusReporter _addFilesStatusReporter;
        private IGeneralStatusReporter _modifiedFilesStatusReporter;
        private IGeneralStatusReporter _removeFilesStatusReporter;
        private IGeneralStatusReporter _unarchivePackageStatusReporter;

        private AppContentSummary _previousContentSummary;
        private AppContentSummary _contentSummary;
        private AppDiffSummary _diffSummary;
        private Pack1Meta _pack1Meta;

        public InstallDiffCommand([NotNull] string packagePath, string packageMetaPath, string packagePassword,
            int versionId,
            [NotNull] ILocalDirectory localData, [NotNull] ILocalMetaData localMetaData,
            [NotNull] ITemporaryDirectory temporaryData, [NotNull] IRemoteMetaData remoteMetaData)
        {
            if (packagePath == null)
            {
                throw new ArgumentNullException("packagePath");
            }

            if (versionId <= 0)
            {
                throw new ArgumentOutOfRangeException("versionId");
            }

            if (localData == null)
            {
                throw new ArgumentNullException("localData");
            }

            if (localMetaData == null)
            {
                throw new ArgumentNullException("localMetaData");
            }

            if (temporaryData == null)
            {
                throw new ArgumentNullException("temporaryData");
            }

            if (remoteMetaData == null)
            {
                throw new ArgumentNullException("remoteMetaData");
            }

            _logger = PatcherLogManager.DefaultLogger;
            _packagePath = packagePath;
            _packageMetaPath = packageMetaPath;
            _packagePassword = packagePassword;
            _versionId = versionId;
            _localData = localData;
            _localMetaData = localMetaData;
            _temporaryData = temporaryData;
            _remoteMetaData = remoteMetaData;
        }

        public override void Prepare([NotNull] IStatusMonitor statusMonitor)
        {
            if (statusMonitor == null)
            {
                throw new ArgumentNullException("statusMonitor");
            }

            try
            {
                _logger.LogDebug("Preparing diff installation...");

                base.Prepare(statusMonitor);

                _localData.PrepareForWriting();
                _temporaryData.PrepareForWriting();

                _previousContentSummary = _remoteMetaData.GetContentSummary(_versionId - 1);
                _contentSummary = _remoteMetaData.GetContentSummary(_versionId);
                _diffSummary = _remoteMetaData.GetDiffSummary(_versionId);

                double unarchivePackageWeight = StatusWeightHelper.GetUnarchivePackageWeight(_diffSummary.Size);
                _logger.LogTrace("unarchivePackageWeight = " + unarchivePackageWeight);
                _unarchivePackageStatusReporter = statusMonitor.CreateGeneralStatusReporter(unarchivePackageWeight);

                double addFilesWeight = StatusWeightHelper.GetAddDiffFilesWeight(_diffSummary);
                _logger.LogTrace("addFilesWeight = " + addFilesWeight);
                _addFilesStatusReporter = statusMonitor.CreateGeneralStatusReporter(addFilesWeight);

                double modifiedFilesWeight = StatusWeightHelper.GetModifyDiffFilesWeight(_diffSummary);
                _logger.LogTrace("modifiedFilesWeight = " + modifiedFilesWeight);
                _modifiedFilesStatusReporter = statusMonitor.CreateGeneralStatusReporter(modifiedFilesWeight);

                double removeFilesWeight = StatusWeightHelper.GetRemoveDiffFilesWeight(_diffSummary);
                _logger.LogTrace("removeFilesWeight = " + removeFilesWeight);
                _removeFilesStatusReporter = statusMonitor.CreateGeneralStatusReporter(removeFilesWeight);

                _logger.LogDebug("Diff installation prepared.");
            }
            catch (Exception e)
            {
                _logger.LogError("Failed to prepare diff installation.", e);
                throw;
            }
        }

        public override void Execute(CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogDebug("Installing diff...");

                base.Execute(cancellationToken);

                _logger.LogTrace("diffSummary.compressionMethod = " + _diffSummary.CompressionMethod);

                if (_diffSummary.CompressionMethod == "pack1")
                {
                    ReadPack1MetaFile();
                }

                using (var packageDir = new TemporaryDirectory(_temporaryData.GetUniquePath()))
                {
                    _logger.LogTrace("packageDir = " + packageDir.Path);

                    string usedSuffix;

                    UnarchivePackage(packageDir.Path, out usedSuffix, cancellationToken);

                    ProcessAddedFiles(packageDir.Path, usedSuffix, cancellationToken);
                    ProcessRemovedFiles(cancellationToken);
                    ProcessModifiedFiles(packageDir.Path, usedSuffix, cancellationToken);
                    DeleteEmptyMacAppDirectories();
                }

                _logger.LogDebug("Diff installed.");
            }
            catch (Exception e)
            {
                _logger.LogError("Diff installation failed", e);
                throw;
            }
        }

        private void ReadPack1MetaFile()
        {
            _logger.LogDebug("Parsing package meta file...");
            _logger.LogTrace("packageMetaPath = " + _packageMetaPath);

            if (!File.Exists(_packageMetaPath))
            {
                throw new MissingPackageMetaFileException("Pack1 meta file does not exist.");
            }

            _pack1Meta = Pack1Meta.ParseFromFile(_packageMetaPath);
            _logger.LogDebug("Meta file parsed.");
            _logger.LogTrace("pack1Meta.iv = " + _pack1Meta.Iv);
            _logger.LogTrace("pack1Meta.version = " + _pack1Meta.Version);
            _logger.LogTrace("pack1Meta.encryption = " + _pack1Meta.Encryption);
            for (int i = 0; i < _pack1Meta.Files.Length; i++)
            {
                _logger.LogTrace(string.Format("pack1Meta.files[{0}] = {1}", i, _pack1Meta.Files[i]));
            }
        }

        private void UnarchivePackage(string packageDirPath, out string usedSuffix, CancellationToken cancellationToken)
        {
            _logger.LogDebug("Unarchiving diff package...");

            var unarchiver = CreateUnrachiver(packageDirPath, out usedSuffix);
            _logger.LogTrace("usedSuffix = " + usedSuffix);

            _unarchivePackageStatusReporter.OnProgressChanged(0.0, "Unarchiving package...");

            int lastEntry = 0;

            unarchiver.UnarchiveProgressChanged += (name, isFile, entry, amount, entryProgress) =>
            {
                if (lastEntry != entry)
                {
                    lastEntry = entry;

                    _logger.LogDebug(string.Format("Unarchiving entry ({0}/{1})...", entry, amount));
                    _logger.LogTrace("entry = " + entry);
                }

                var entryMinProgress = (entry - 1) / (double) amount;
                var entryMaxProgress = entry / (double) amount;

                var progress = entryMinProgress + (entryMaxProgress - entryMinProgress) * entryProgress;

                _unarchivePackageStatusReporter.OnProgressChanged(progress, "Unarchiving package...");
            };

            unarchiver.Unarchive(cancellationToken);

            _unarchivePackageStatusReporter.OnProgressChanged(1.0, string.Empty);

            _logger.LogDebug("Diff package unarchived.");
        }

        private IUnarchiver CreateUnrachiver(string destinationDir, out string usedSuffix)
        {
            switch (_diffSummary.CompressionMethod)
            {
                case "zip":
                    usedSuffix = string.Empty;
                    return new ZipUnarchiver(_packagePath, destinationDir, _packagePassword);
                case "pack1":
                    usedSuffix = Suffix;
                    return new Pack1Unarchiver(_packagePath, _pack1Meta, destinationDir, _packagePassword, Suffix);
                default:
                    throw new UnknownPackageCompressionModeException(string.Format("Unknown compression method: {0}",
                        _diffSummary.CompressionMethod));
            }
        }

        private void ProcessRemovedFiles(CancellationToken cancellationToken)
        {
            _logger.LogDebug("Processing diff removed files...");

            var fileNames = _diffSummary.RemovedFiles.Where(s => !s.EndsWith("/"));
            var dirNames = _diffSummary.RemovedFiles.Where(s => s.EndsWith("/"));

            int counter = 0;

            _removeFilesStatusReporter.OnProgressChanged(0.0, "Installing package...");

            foreach (var fileName in fileNames)
            {
                cancellationToken.ThrowIfCancellationRequested();

                RemoveFile(fileName);

                counter++;
                _removeFilesStatusReporter.OnProgressChanged(counter / (double) _diffSummary.RemovedFiles.Length,
                    "Installing package...");
            }

            foreach (var dirName in dirNames)
            {
                cancellationToken.ThrowIfCancellationRequested();

                RemoveDir(dirName);

                counter++;
                _removeFilesStatusReporter.OnProgressChanged(counter / (double) _diffSummary.RemovedFiles.Length,
                    "Installing package...");
            }

            _removeFilesStatusReporter.OnProgressChanged(1.0, string.Empty);

            _logger.LogDebug("Diff removed files processed.");
        }

        private void RemoveFile(string fileName)
        {
            _logger.LogDebug(string.Format("Processing remove file entry {0}", fileName));

            string filePath = _localData.Path.PathCombine(fileName);
            _logger.LogTrace("filePath = " + filePath);

            _logger.LogDebug("Deleting file in local data. Checking whether it actually exists...");
            if (File.Exists(filePath))
            {
                _logger.LogDebug("File exists. Deleting it...");
                File.Delete(filePath);
                _logger.LogDebug("File deleted.");
            }
            else
            {
                _logger.LogDebug("File already doesn't exist.");
            }

            _localMetaData.UnregisterEntry(fileName);

            _logger.LogDebug("Remove file entry processed.");
        }

        private void RemoveDir(string dirName)
        {
            _logger.LogDebug(string.Format("Processing remove directory entry {0}", dirName));

            string dirPath = _localData.Path.PathCombine(dirName);
            _logger.LogTrace("dirPath = " + dirPath);

            _logger.LogDebug("Deleting directory in local data. Checking whether it actually exists...");
            if (Directory.Exists(dirPath))
            {
                _logger.LogDebug("Directory exists. Checking whether directory is empty...");

                if (IsDirectoryEmpty(dirPath))
                {
                    _logger.LogDebug("Directory is empty. Deleting it...");
                    Directory.Delete(dirPath);
                    _logger.LogDebug("Directory deleted.");
                }
                else
                {
                    _logger.LogDebug("Directory is not empty. Couldn't delete it.");
                }
            }

            _logger.LogDebug("Remove directory entry processed.");

            // TODO: Uncomment this after fixing directory registration in install content command
            //_localMetaData.UnregisterEntry(dirName);
        }

        private bool IsDirectoryEmpty(string dirPath)
        {
            bool isEmpty = Directory.GetFiles(dirPath, "*", SearchOption.TopDirectoryOnly).Length == 0 &&
                           Directory.GetDirectories(dirPath, "*", SearchOption.TopDirectoryOnly).Length == 0;

            return isEmpty;
        }

        private void ProcessAddedFiles(string packageDirPath, string suffix,
            CancellationToken cancellationToken)
        {
            _logger.LogDebug("Processing diff added files...");

            _addFilesStatusReporter.OnProgressChanged(0.0, "Installing package...");

            for (int i = 0; i < _diffSummary.AddedFiles.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var entryName = _diffSummary.AddedFiles[i];

                if (entryName.EndsWith("/"))
                {
                    AddDirectory(entryName);
                }
                else
                {
                    AddFile(entryName, packageDirPath, suffix);
                }

                _addFilesStatusReporter.OnProgressChanged((i + 1) / (double) _diffSummary.AddedFiles.Length,
                    "Installing package...");
            }

            _addFilesStatusReporter.OnProgressChanged(1.0, "Installing package...");

            _logger.LogDebug("Diff added files processed.");
        }

        private void AddDirectory(string dirName)
        {
            _logger.LogDebug(string.Format("Processing add directory entry {0}", dirName));

            var dirPath = _localData.Path.PathCombine(dirName);
            _logger.LogTrace("dirPath = " + dirPath);

            _logger.LogDebug("Creating directory in local data...");
            Directory.CreateDirectory(dirPath);
            _logger.LogDebug("Directory created.");

            _logger.LogDebug("Add directory entry processed.");
        }

        private void AddFile(string fileName, string packageDirPath, string suffix)
        {
            _logger.LogDebug(string.Format("Processing add file entry {0}", fileName));

            var filePath = _localData.Path.PathCombine(fileName);
            _logger.LogTrace("filePath = " + filePath);
            var sourceFilePath = Path.Combine(packageDirPath, fileName + suffix);
            _logger.LogTrace("sourceFilePath = " + sourceFilePath);

            if (!File.Exists(sourceFilePath))
            {
                throw new MissingFileFromPackageException(string.Format("Cannot find file {0} in diff package.",
                    fileName));
            }

            _logger.LogDebug("Creating file parent directories in local data...");
            var fileParentDirPath = Path.GetDirectoryName(filePath);
            _logger.LogTrace("fileParentDirPath = " + fileParentDirPath);
            //TODO: Assert that fileParentDirPath is not null
            // ReSharper disable once AssignNullToNotNullAttribute
            Directory.CreateDirectory(fileParentDirPath);
            _logger.LogDebug("File parent directories created in local data.");

            _logger.LogDebug("Copying file to local data (overwriting if needed)...");
            File.Copy(sourceFilePath, filePath, true);
            _logger.LogDebug("File copied to local data.");

            _localMetaData.RegisterEntry(fileName, _versionId);

            _logger.LogDebug("Add file entry processed.");
        }

        private void ProcessModifiedFiles(string packageDirPath, string suffix,
            CancellationToken cancellationToken)
        {
            _logger.LogDebug("Processing diff modified files...");

            using (var tempDiffDir = new TemporaryDirectory(_temporaryData.GetUniquePath()))
            {
                _logger.LogTrace("tempDiffDir = " + tempDiffDir.Path);

                _modifiedFilesStatusReporter.OnProgressChanged(0.0, "Installing package...");

                for (int i = 0; i < _diffSummary.ModifiedFiles.Length; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var entryName = _diffSummary.ModifiedFiles[i];

                    if (!entryName.EndsWith("/"))
                    {
                        PatchFile(entryName, packageDirPath, suffix, tempDiffDir);
                    }

                    _modifiedFilesStatusReporter.OnProgressChanged(
                        (i + 1) / (double) _diffSummary.ModifiedFiles.Length, "Installing package...");
                }

                _modifiedFilesStatusReporter.OnProgressChanged(1.0, "Installing package...");
            }

            _logger.LogDebug("Diff modified files processed.");
        }


        private void PatchFile(string fileName, string packageDirPath, string suffix, TemporaryDirectory tempDiffDir)
        {
            _logger.LogDebug(string.Format("Processing patch file entry {0}", fileName));

            var filePath = _localData.Path.PathCombine(fileName);
            _logger.LogTrace("filePath = " + filePath);

            if (!File.Exists(filePath))
            {
                throw new MissingLocalDataFileException(
                    string.Format("Couldn't patch file {0} because it doesn't exists in local data.", fileName));
            }

            var sourceDeltaFilePath = Path.Combine(packageDirPath, fileName + suffix);
            _logger.LogTrace("sourceDeltaFilePath = " + sourceDeltaFilePath);

            if (!File.Exists(sourceDeltaFilePath))
            {
                throw new MissingFileFromPackageException(string.Format("Cannot find delta file {0} in diff package.",
                    fileName));
            }

            var fileVersion = _localMetaData.GetEntryVersionId(fileName);
            _logger.LogTrace("fileVersion = " + fileVersion);

            if (fileVersion != _versionId - 1)
            {
                throw new InvalidLocalDataFileVersionException(string.Format(
                    "Couldn't patch file {0} because expected file version to be ({1}) but it's {2}.",
                    fileName, _versionId - 1, fileVersion));
            }

            _logger.LogDebug("Checking whether patching file content is necessary...");
            if (IsPatchingFileContentNecessary(fileName))
            {
                _logger.LogDebug("Patching is necessary. Generating new file with patched content...");

                var newFilePath = tempDiffDir.GetUniquePath();
                _logger.LogTrace("newFilePath = " + newFilePath);

                var filePatcher = new FilePatcher(filePath, sourceDeltaFilePath, newFilePath);
                filePatcher.Patch();

                _logger.LogDebug("New file generated. Deleting old file in local data...");
                File.Delete(filePath);

                _logger.LogDebug("Old file deleted. Moving new file to local data...");
                File.Move(newFilePath, filePath);

                _logger.LogDebug("New file moved.");
            }
            else
            {
                _logger.LogDebug("Patching is not necessary. File content is the same as in previous version.");
            }

            _localMetaData.RegisterEntry(fileName, _versionId);

            _logger.LogDebug("Patch file entry processed.");
        }

        private bool IsPatchingFileContentNecessary(string fileName)
        {
            //TODO: Throw exceptions if file is not present in any of both content summaries
            var fileHash = _contentSummary.Files.First(x => x.Path == fileName).Hash;
            var previousFileHash = _previousContentSummary.Files.First(x => x.Path == fileName).Hash;

            return fileHash != previousFileHash;
        }

        // TODO: Temporary solution for situation when .app directory is not deleted
        private void DeleteEmptyMacAppDirectories()
        {
            if (!Platform.IsOSX())
            {
                return;
            }

            _logger.LogDebug("Deleting empty Mac OSX '.app' directories...");

            foreach (var dir in FindEmptyMacAppDirectories())
            {
                _logger.LogDebug(string.Format("Deleting {0}", dir));
                Directory.Delete(dir, true);
                _logger.LogDebug("Directory deleted.");
            }

            _logger.LogDebug("Empty Mac OSX '.app' directories deleted.");
        }

        private IEnumerable<string> FindEmptyMacAppDirectories()
        {
            return Directory
                .GetFileSystemEntries(_localData.Path)
                .Where(IsEmptyMacAppDirectory);
        }

        private static bool IsEmptyMacAppDirectory(string dirPath)
        {
            return Directory.Exists(dirPath) &&
                   dirPath.EndsWith(".app") &&
                   Directory.GetFiles(dirPath, "*", SearchOption.AllDirectories).Length == 0;
        }
    }
}