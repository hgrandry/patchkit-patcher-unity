﻿using System;
using System.Linq;
using PatchKit.Api;
using PatchKit.Unity.Patcher.Debug;
using UnityEngine.Assertions;

namespace PatchKit.Unity.Patcher.Data.Remote
{
    public class RemoteData : IRemoteData
    {
        private static readonly DebugLogger DebugLogger = new DebugLogger(typeof(DebugLogger));

        private readonly string _appSecret;
        private readonly MainApiConnection _mainApiConnection;

        public IRemoteMetaData MetaData { get; private set; }

        public RemoteData(string appSecret, MainApiConnection mainApiConnection, KeysApiConnection keysApiConnection)
        {
            DebugLogger.LogConstructor();
            DebugLogger.LogVariable(appSecret, "appSecret");

            Checks.ArgumentNotNullOrEmpty(appSecret, "appSecret");
            Assert.IsNotNull(mainApiConnection, "mainApiConnection");
            Assert.IsNotNull(keysApiConnection, "keysApiConnection");

            _appSecret = appSecret;
            _mainApiConnection = mainApiConnection;

            MetaData = new RemoteMetaData(_appSecret, _mainApiConnection, keysApiConnection);
        }

        public RemoteResource GetContentPackageResource(int versionId, string keySecret)
        {
            DebugLogger.Log("Getting content package resource.");
            DebugLogger.LogVariable(versionId, "versionId");
            DebugLogger.LogVariable(keySecret, "keySecret");

            Checks.ArgumentValidVersionId(versionId, "versionId");
            Checks.ArgumentNotNullOrEmpty(keySecret, "keySecret");

            RemoteResource resource = new RemoteResource();

            var summary = _mainApiConnection.GetAppVersionContentSummary(_appSecret, versionId);
            var torrentUrl = _mainApiConnection.GetAppVersionContentTorrentUrl(_appSecret, versionId, keySecret);
            var urls = _mainApiConnection.GetAppVersionContentUrls(_appSecret, versionId); // TODO: Add key secret checking

            resource.Size = summary.Size;
            resource.HashCode = summary.HashCode;
            resource.ChunksData = ConvertToChunksData(summary.Chunks);
            resource.TorrentUrls = new[] {torrentUrl.Url};
            resource.Urls = urls.Select(u => u.Url).ToArray();

            return resource;
        }

        public RemoteResource GetDiffPackageResource(int versionId, string keySecret)
        {
            DebugLogger.Log("Getting diff package resource.");
            DebugLogger.LogVariable(versionId, "versionId");
            DebugLogger.LogVariable(keySecret, "keySecret");

            Checks.ArgumentValidVersionId(versionId, "versionId");
            Checks.ArgumentNotNullOrEmpty(keySecret, "keySecret");

            RemoteResource resource = new RemoteResource();

            var summary = _mainApiConnection.GetAppVersionDiffSummary(_appSecret, versionId);
            var torrentUrl = _mainApiConnection.GetAppVersionDiffTorrentUrl(_appSecret, versionId, keySecret);
            var urls = _mainApiConnection.GetAppVersionDiffUrls(_appSecret, versionId); // TODO: Add key secret checking

            resource.Size = summary.Size;
            resource.HashCode = summary.HashCode;
            resource.ChunksData = ConvertToChunksData(summary.Chunks);
            resource.TorrentUrls = new[] { torrentUrl.Url };
            resource.Urls = urls.Select(u => u.Url).ToArray();

            return resource;
        }

        private static ChunksData ConvertToChunksData(Api.Models.Chunks chunks)
        {
            var chunksData = new ChunksData
            {
                ChunkSize = chunks.Size,
                Chunks = new Chunk[chunks.Hashes.Length]
            };

            for (int index = 0; index < chunks.Hashes.Length; index++)
            {
                string hash = chunks.Hashes[index];
                var array = XXHashToByteArray(hash);

                chunksData.Chunks[index] = new Chunk
                {
                    Hash = array
                };
            }
            return chunksData;
        }

        // ReSharper disable once InconsistentNaming
        private static byte[] XXHashToByteArray(string hash)
        {
            while (hash.Length < 8)
            {
                hash = "0" + hash;
            }

            byte[] array = Enumerable.Range(0, hash.Length)
                .Where(x => x % 2 == 0)
                .Select(x => Convert.ToByte(hash.Substring(x, 2), 16))
                .ToArray();
            return array;
        }
    }
}