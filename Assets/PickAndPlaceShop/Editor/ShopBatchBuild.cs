using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace PickAndPlaceShop.Editor
{
    public static class ShopBatchBuild
    {
        [MenuItem("Tools/Pick And Place Shop/Configure GitHub Pages WebGL")]
        public static void ConfigureGitHubPagesWebGL()
        {
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
            AssetDatabase.SaveAssets();
            Debug.Log("[ShopBatchBuild] GitHub Pages WebGL: Gzip + decompression fallback enabled.");
        }

        public static void BuildWebGL()
        {
            string output = CommandLineValue("-shopBuildOutput") ??
                            "docs";
            string[] scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();
            ConfigureGitHubPagesWebGL();
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
