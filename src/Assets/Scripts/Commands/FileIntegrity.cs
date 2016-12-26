﻿namespace PatchKit.Unity.Patcher.Commands
{
    internal class FileIntegrity
    {
        public FileIntegrity(string fileName, FileIntegrityStatus status)
        {
            FileName = fileName;
            Status = status;
        }

        public string FileName { get; private set; }

        public FileIntegrityStatus Status { get; private set; }
    }
}