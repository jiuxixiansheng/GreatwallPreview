using System.IO;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>Ships a user-editable file beside Windows/Linux builds. Keeps existing deployment settings.</summary>
public sealed class NetworkConfigBuildProcessor : IPostprocessBuildWithReport
{
    public int callbackOrder { get { return 100; } }

    public void OnPostprocessBuild(BuildReport report)
    {
        if (report.summary.platform != UnityEditor.BuildTarget.StandaloneWindows &&
            report.summary.platform != UnityEditor.BuildTarget.StandaloneWindows64 &&
            report.summary.platform != UnityEditor.BuildTarget.StandaloneLinux64)
            return;

        string directory = Path.Combine(Path.GetDirectoryName(report.summary.outputPath), "NetworkConfig");
        string destination = Path.Combine(directory, "network.txt");
        Directory.CreateDirectory(directory);
        if (File.Exists(destination))
        {
            Debug.Log("[NetworkConfig] Preserved existing configuration: " + destination);
            return;
        }
        string source = Path.Combine(Application.dataPath, "sza/NetworkConfig/network.txt");
        if (File.Exists(source)) File.Copy(source, destination);
        else File.WriteAllText(destination, NetworkFileSettings.DefaultText, new System.Text.UTF8Encoding(false));
        Debug.Log("[NetworkConfig] Exported editable configuration: " + destination);
    }
}
