using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class CodexAndroidBuild
{
    private const string ToolchainArg = "-codexAndroidToolchain";
    private const string OutputArg = "-codexBuildOutput";
    private const string AndroidPackageIdentifier = "jp.naist.MemPalace";

    public static void BuildApk()
    {
        try
        {
            ConfigureAndroidToolchain();
            ConfigureAndroidPlayerSettings();

            var outputPath = GetCommandLineArg(OutputArg);
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                outputPath = Path.GetFullPath(Path.Combine("Builds", "MemPalaceLLM.apk"));
            }

            var outputDirectory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            var scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();

            if (scenes.Length == 0)
            {
                throw new InvalidOperationException("No enabled scenes found in EditorBuildSettings.");
            }

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = BuildTarget.Android,
                options = BuildOptions.None
            };

            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            Debug.Log($"Codex Android build result: {summary.result}, size={summary.totalSize}, output={outputPath}");

            EditorApplication.Exit(summary.result == BuildResult.Succeeded ? 0 : 1);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            EditorApplication.Exit(1);
        }
    }

    private static void ConfigureAndroidToolchain()
    {
        var toolchainRoot = GetCommandLineArg(ToolchainArg);
        if (string.IsNullOrWhiteSpace(toolchainRoot))
        {
            toolchainRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "UnityAndroidToolchains",
                "6000.3.12f1");
        }

        var jdkPath = Path.Combine(toolchainRoot, "OpenJDK");
        var sdkPath = Path.Combine(toolchainRoot, "SDK");
        var ndkPath = Path.Combine(toolchainRoot, "NDK");
        var gradlePath = Path.Combine(
            EditorApplication.applicationContentsPath,
            "PlaybackEngines",
            "AndroidPlayer",
            "Tools",
            "gradle");

        RequireDirectory(jdkPath, "JDK");
        RequireDirectory(sdkPath, "Android SDK");
        RequireDirectory(ndkPath, "Android NDK");
        RequireDirectory(gradlePath, "Gradle");

        EditorPrefs.SetBool("JdkUseEmbedded", false);
        EditorPrefs.SetBool("SdkUseEmbedded", false);
        EditorPrefs.SetBool("NdkUseEmbedded", false);
        EditorPrefs.SetBool("GradleUseEmbedded", true);
        EditorPrefs.SetString("JdkPath", jdkPath);
        EditorPrefs.SetString("AndroidSdkPath", sdkPath);
        EditorPrefs.SetString("AndroidNdkPath", ndkPath);
        EditorPrefs.SetString("GradlePath", gradlePath);

        var settingsType = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(GetTypesSafely)
            .FirstOrDefault(type => type.FullName == "UnityEditor.Android.AndroidExternalToolsSettings")
            ?? AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(GetTypesSafely)
                .FirstOrDefault(type => type.Name.Contains("ExternalToolsSettings"));

        if (settingsType == null)
        {
            Debug.LogWarning("Could not find AndroidExternalToolsSettings type; falling back to EditorPrefs only.");
            return;
        }

        Debug.Log($"Using Android tools settings type: {settingsType.FullName}");
        SetStaticMember(settingsType, "jdkUseEmbedded", false);
        SetStaticMember(settingsType, "sdkUseEmbedded", false);
        SetStaticMember(settingsType, "ndkUseEmbedded", false);
        SetStaticMember(settingsType, "gradleUseEmbedded", true);
        SetStaticMember(settingsType, "jdkRootPath", jdkPath);
        SetStaticMember(settingsType, "sdkRootPath", sdkPath);
        SetStaticMember(settingsType, "ndkRootPath", ndkPath);
        SetStaticMember(settingsType, "gradlePath", gradlePath);

        Debug.Log($"Configured Android toolchain: JDK={jdkPath}, SDK={sdkPath}, NDK={ndkPath}, Gradle={gradlePath}");
    }

    private static void ConfigureAndroidPlayerSettings()
    {
        EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
        PlayerSettings.productName = "MemPalaceLLM";
        PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, AndroidPackageIdentifier);
        Debug.Log($"Configured Android player settings: applicationId={AndroidPackageIdentifier}");
    }

    private static void RequireDirectory(string path, string label)
    {
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException($"{label} directory not found: {path}");
        }
    }

    private static string GetCommandLineArg(string name)
    {
        var args = Environment.GetCommandLineArgs();
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static Type[] GetTypesSafely(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(type => type != null).ToArray();
        }
    }

    private static void SetStaticMember(Type type, string memberName, object value)
    {
        const BindingFlags Flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        var property = type.GetProperty(memberName, Flags);
        if (property != null && property.CanWrite)
        {
            property.SetValue(null, value);
            Debug.Log($"Set {type.Name}.{memberName} = {value}");
            return;
        }

        var field = type.GetField(memberName, Flags);
        if (field != null)
        {
            field.SetValue(null, value);
            Debug.Log($"Set {type.Name}.{memberName} = {value}");
            return;
        }

        Debug.LogWarning($"Android toolchain member not found or not writable: {type.Name}.{memberName}");
    }
}
