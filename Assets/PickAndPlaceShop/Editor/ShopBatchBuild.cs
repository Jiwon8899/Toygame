using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace PickAndPlaceShop.Editor
{
    public static class ShopBatchBuild
    {
        public static void BuildWebGL()
        {
            string output = CommandLineValue("-shopBuildOutput") ??
                            "Build/DocCodeAlignmentWebGLBatch";
            string[] scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = output,
                target = BuildTarget.WebGL,
                options = BuildOptions.CleanBuildCache | BuildOptions.DetailedBuildReport
            });
            BuildSummary summary = report.summary;
            Debug.Log($"[ShopBatchBuild] result={summary.result} errors={summary.totalErrors} " +
                      $"warnings={summary.totalWarnings} size={summary.totalSize} output={output}");
            if (summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException(
                    $"WebGL build failed with {summary.totalErrors} errors.");
        }

        private static string CommandLineValue(string name)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int i = 0; i < arguments.Length - 1; i++)
                if (string.Equals(arguments[i], name, StringComparison.OrdinalIgnoreCase))
                    return arguments[i + 1];
            return null;
        }
    }
}
