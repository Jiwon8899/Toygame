using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace PickAndPlaceShop.Editor
{
    public sealed class ShopGitHubPagesWebGLPostprocessor : IPostprocessBuildWithReport
    {
        private const long ChunkSizeBytes = 90L * 1024L * 1024L;
        private const string NoJekyllFileName = ".nojekyll";

        [Serializable]
        private sealed class SplitManifest
        {
            public string originalFile;
            public long totalBytes;
            public long chunkSizeBytes;
            public string sha256;
            public List<SplitPart> parts = new List<SplitPart>();
        }

        [Serializable]
        private sealed class SplitPart
        {
            public string file;
            public long size;
        }

        public int callbackOrder => 1000;

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.WebGL ||
                report.summary.result != BuildResult.Succeeded)
                return;

            PackBuild(report.summary.outputPath);
        }

        [MenuItem("Tools/Pick And Place Shop/Prepare docs for GitHub Pages")]
        public static void PrepareCurrentDocs()
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(projectRoot))
                throw new InvalidOperationException("Could not resolve the Unity project root.");

            PackBuild(Path.Combine(projectRoot, "docs"));
        }

        public static void PackBuild(string outputPath)
        {
            string resolvedOutput = Path.GetFullPath(outputPath);
            string buildDirectory = Path.Combine(resolvedOutput, "Build");
            if (!Directory.Exists(buildDirectory))
                throw new DirectoryNotFoundException($"WebGL Build directory was not found: {buildDirectory}");

            File.WriteAllText(Path.Combine(resolvedOutput, NoJekyllFileName), string.Empty);

            string[] dataFiles = Directory.GetFiles(buildDirectory, "*.data.unityweb", SearchOption.TopDirectoryOnly);
            if (dataFiles.Length == 0)
            {
                Debug.Log("[ShopGitHubPages] No unsplit WebGL data file found. Existing split package was kept.");
                return;
            }
            if (dataFiles.Length != 1)
                throw new InvalidOperationException($"Expected one WebGL data file, found {dataFiles.Length}.");

            SplitAndVerify(dataFiles[0]);
        }

        private static void SplitAndVerify(string dataPath)
        {
            FileInfo sourceInfo = new FileInfo(dataPath);
            if (sourceInfo.Length <= ChunkSizeBytes)
            {
                Debug.Log($"[ShopGitHubPages] Data file already fits GitHub's limit: {sourceInfo.Length} bytes.");
                return;
            }

            string directory = sourceInfo.DirectoryName;
            string fileName = sourceInfo.Name;
            string manifestPath = dataPath + ".parts.json";
            foreach (string stalePart in Directory.GetFiles(directory, fileName + ".part*", SearchOption.TopDirectoryOnly))
                File.Delete(stalePart);

            var manifest = new SplitManifest
            {
                originalFile = fileName,
                totalBytes = sourceInfo.Length,
                chunkSizeBytes = ChunkSizeBytes,
                sha256 = ComputeSha256(dataPath)
            };

            byte[] buffer = new byte[1024 * 1024];
            using (FileStream input = File.OpenRead(dataPath))
            {
                int partIndex = 0;
                while (input.Position < input.Length)
                {
                    string partName = $"{fileName}.part{partIndex:000}";
                    string partPath = Path.Combine(directory, partName);
                    long remainingForPart = Math.Min(ChunkSizeBytes, input.Length - input.Position);
                    long written = 0;
                    using (FileStream output = File.Create(partPath))
                    {
                        while (written < remainingForPart)
                        {
                            int request = (int)Math.Min(buffer.Length, remainingForPart - written);
                            int read = input.Read(buffer, 0, request);
                            if (read <= 0)
                                throw new EndOfStreamException("Unexpected end of WebGL data while splitting.");
                            output.Write(buffer, 0, read);
                            written += read;
                        }
                    }
                    manifest.parts.Add(new SplitPart { file = partName, size = written });
                    partIndex++;
                }
            }

            string combinedHash = ComputeCombinedSha256(directory, manifest.parts);
            if (!string.Equals(manifest.sha256, combinedHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Split WebGL data SHA-256 verification failed; original file was preserved.");

            File.WriteAllText(manifestPath, JsonUtility.ToJson(manifest, true));
            File.Delete(dataPath);
            Debug.Log($"[ShopGitHubPages] Split {fileName} into {manifest.parts.Count} verified chunks; " +
                      $"SHA-256={manifest.sha256}.");
        }

        private static string ComputeSha256(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
                return BytesToHex(sha.ComputeHash(stream));
        }

        private static string ComputeCombinedSha256(string directory, List<SplitPart> parts)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] buffer = new byte[1024 * 1024];
                foreach (SplitPart part in parts)
                {
                    using (FileStream stream = File.OpenRead(Path.Combine(directory, part.file)))
                    {
                        int read;
                        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                            sha.TransformBlock(buffer, 0, read, null, 0);
                    }
                }
                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                return BytesToHex(sha.Hash);
            }
        }

        private static string BytesToHex(byte[] bytes)
        {
            return BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
        }
    }
}
