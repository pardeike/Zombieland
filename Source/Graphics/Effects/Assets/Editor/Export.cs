using UnityEditor;
using System.IO;

public class CreateAssetBundles
{
	static string deploymentDir = @"C:\Users\Brrainz\Source\ModRepos\Zombieland\Resources";

	[MenuItem("Assets/Export Zombieland")]
	public static void BuildStandaloneAssetBundles()
	{
		Build("Win64", BuildTarget.StandaloneWindows64);
		Build("Linux", BuildTarget.StandaloneLinux64);
		Build("MacOS", BuildTarget.StandaloneOSX);
	}


	static void Build(string arch, BuildTarget target)
	{
		var src = $"Assets/AssetBundles/{arch}";
		if (!Directory.Exists(src))
			Directory.CreateDirectory(src);

		BuildPipeline.BuildAssetBundles(src, BuildAssetBundleOptions.None, target);

		var dest = $"{deploymentDir}\\{arch}";
		if (!Directory.Exists(dest))
			Directory.CreateDirectory(dest);

		File.Copy($"{src}/zombieland", $"{dest}\\zombieland", true);
	}
}
