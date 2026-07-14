using UnityEditor;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public class CreateAssetBundles
{
	static readonly string[] Architectures = { "Win64", "Linux", "MacOS" };
	const string BundleName = "zombieland";
	const string GeneratedAssetDir = "Assets/_Zombieland";
	static readonly string[] ExpectedAssetNames =
	{
		"assets/_zombieland/dust.prefab",
		"assets/_zombieland/metaballs.shader",
		"assets/_zombieland/smoke_n.png",
		"assets/_zombieland/smoke_thin.mat",
		"assets/_zombieland/smoke_thin.png",
		"assets/_zombieland/mainmenubackgroundeffect.shader",
		"assets/_zombieland/titleblooddrip.shader",
		"assets/_zombieland/zombiesymbiant.mat",
		"assets/_zombieland/zombiesymbiant.shader"
	};

	static string DeploymentDir()
	{
		var env = System.Environment.GetEnvironmentVariable("ZOMBIELAND_RESOURCES_DIR");
		if (string.IsNullOrEmpty(env) == false)
			return env;

		return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "1.6", "Resources"));
	}

	[MenuItem("Assets/Export Zombieland")]
	public static void BuildStandaloneAssetBundles()
	{
		BuildAssetBundles(Architectures);
	}

	[MenuItem("Assets/Export Zombieland Current Platform")]
	public static void BuildCurrentMachineAssetBundle()
	{
		BuildSingleAssetBundle(CurrentArchitecture());
	}

	public static void BuildWin64AssetBundle()
	{
		BuildSingleAssetBundle("Win64");
	}

	public static void BuildLinuxAssetBundle()
	{
		BuildSingleAssetBundle("Linux");
	}

	public static void BuildMacOSAssetBundle()
	{
		BuildSingleAssetBundle("MacOS");
	}

	static void BuildSingleAssetBundle(string arch)
	{
		BuildAssetBundles(new[] { arch });
	}

	static void BuildAssetBundles(IEnumerable<string> architectures)
	{
		var selectedArchitectures = architectures.ToArray();
		EnsureGeneratedBundleAssets();
		foreach (var arch in selectedArchitectures)
			Build(arch);
		ValidateDeployedAssetBundles(selectedArchitectures);
	}

	public static void ListDeployedAssetBundles()
	{
		foreach (var arch in Architectures)
		{
			var path = Path.Combine(DeploymentDir(), arch, BundleName);
			Debug.Log($"Zombieland bundle {arch}: {path}");
			var bundle = AssetBundle.LoadFromFile(path);
			if (bundle == null)
			{
				Debug.LogError($"Could not load asset bundle {path}");
				continue;
			}
			foreach (var name in bundle.GetAllAssetNames())
				Debug.Log($"Zombieland bundle asset {arch}: {name}");
			bundle.Unload(false);
		}
	}

	public static void InspectDeployedAssetBundles()
	{
		foreach (var arch in Architectures)
		{
			var path = Path.Combine(DeploymentDir(), arch, BundleName);
			var bundle = AssetBundle.LoadFromFile(path);
			if (bundle == null)
			{
				Debug.LogError($"Could not load asset bundle {path}");
				continue;
			}

			Debug.Log($"Zombieland bundle inspect {arch}: path={path}");
			foreach (var name in bundle.GetAllAssetNames())
			{
				var asset = bundle.LoadAsset<UnityEngine.Object>(name);
				Debug.Log($"Zombieland bundle inspect {arch}: asset={name}, type={asset?.GetType().FullName ?? "null"}, name={asset?.name ?? "null"}");
				if (asset is Material material)
					LogMaterial(arch, name, material);
				if (asset is Texture2D texture)
					WriteInspectedTexture(arch, name, texture);
				if (asset is GameObject gameObject)
					LogGameObject(arch, name, gameObject);
			}

			bundle.Unload(false);
		}
	}

	public static void ValidateDeployedAssetBundles()
	{
		ValidateDeployedAssetBundles(Architectures);
	}

	static void ValidateDeployedAssetBundles(IEnumerable<string> architectures)
	{
		foreach (var arch in architectures)
		{
			var path = Path.Combine(DeploymentDir(), arch, BundleName);
			var bundle = AssetBundle.LoadFromFile(path);
			if (bundle == null)
				throw new System.Exception($"Could not load asset bundle {path}");

			ValidateBundle(arch, path, bundle);
			bundle.Unload(false);
		}
	}

	static void ValidateBundle(string arch, string path, AssetBundle bundle)
	{
		var actualNames = new HashSet<string>(bundle.GetAllAssetNames());
		var missingNames = ExpectedAssetNames.Where(name => actualNames.Contains(name) == false).ToArray();
		if (missingNames.Length > 0)
			throw new Exception($"Zombieland bundle {arch} is missing assets: {string.Join(", ", missingNames)}");

		var dust = RequireAsset<GameObject>(bundle, arch, ExpectedAssetNames[0]);
		var metaballs = RequireAsset<Shader>(bundle, arch, ExpectedAssetNames[1]);
		_ = RequireAsset<Texture2D>(bundle, arch, ExpectedAssetNames[2]);
		_ = RequireAsset<Material>(bundle, arch, ExpectedAssetNames[3]);
		_ = RequireAsset<Texture2D>(bundle, arch, ExpectedAssetNames[4]);
		var mainMenuBackgroundEffect = RequireAsset<Shader>(bundle, arch, ExpectedAssetNames[5]);
		var titleBloodDrip = RequireAsset<Shader>(bundle, arch, ExpectedAssetNames[6]);
		_ = RequireAsset<Material>(bundle, arch, ExpectedAssetNames[7]);
		var zombieSymbiant = RequireAsset<Shader>(bundle, arch, ExpectedAssetNames[8]);
		if (titleBloodDrip.passCount < 2)
			throw new Exception($"Zombieland bundle {arch} loaded the title blood-drip shader without both its update and display passes.");

		Debug.Log($"Zombieland bundle validated {arch}: Dust={dust.name}, Metaballs={metaballs.name}, MainMenuBackgroundEffect={mainMenuBackgroundEffect.name}, TitleBloodDrip={titleBloodDrip.name}, TitleBloodDripPasses={titleBloodDrip.passCount}, ZombieSymbiant={zombieSymbiant.name}, assets={actualNames.Count}, Unity={Application.unityVersion}, path={path}");
	}

	static T RequireAsset<T>(AssetBundle bundle, string arch, string assetName) where T : UnityEngine.Object
	{
		var asset = bundle.LoadAsset<T>(assetName);
		if (asset == null)
			throw new Exception($"Zombieland bundle {arch} could not load {assetName} as {typeof(T).Name}");
		return asset;
	}

	static void LogMaterial(string arch, string assetName, Material material)
	{
		Debug.Log($"Zombieland bundle inspect {arch}: material={assetName}, shader={material.shader?.name ?? "null"}, renderQueue={material.renderQueue}, color={GetMaterialColor(material)}");
		foreach (var property in new[] { "_MainTex", "_BumpMap", "_Mode", "_SrcBlend", "_DstBlend", "_ZWrite", "_Cull", "_SymbiantOpacityMin", "_SymbiantOpacityMax", "_SymbiantNoiseScale", "_SymbiantFlowSpeed", "_SymbiantWaveShadeStrength", "_SymbiantEdgeContrast", "_SymbiantNoiseTime" })
		{
			if (material.HasProperty(property) == false)
				continue;
			if (property.EndsWith("Tex") || property == "_BumpMap")
			{
				var texture = material.GetTexture(property);
				Debug.Log($"Zombieland bundle inspect {arch}: material={assetName}, texture[{property}]={texture?.name ?? "null"}");
				continue;
			}
			Debug.Log($"Zombieland bundle inspect {arch}: material={assetName}, float[{property}]={material.GetFloat(property)}");
		}
	}

	static string GetMaterialColor(Material material)
	{
		return material.HasProperty("_Color") ? material.GetColor("_Color").ToString() : "none";
	}

	static void WriteInspectedTexture(string arch, string assetName, Texture2D texture)
	{
		var outputDir = Environment.GetEnvironmentVariable("ZOMBIELAND_INSPECT_OUTPUT_DIR");
		if (string.IsNullOrEmpty(outputDir))
			return;

		Directory.CreateDirectory(outputDir);
		var safeName = assetName.Replace('/', '_').Replace('\\', '_');
		var outputPath = Path.Combine(outputDir, $"{arch}_{safeName}.png");
		var previous = RenderTexture.active;
		var renderTexture = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32);
		Graphics.Blit(texture, renderTexture);
		RenderTexture.active = renderTexture;
		var readable = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
		readable.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
		readable.Apply();
		File.WriteAllBytes(outputPath, readable.EncodeToPNG());
		UnityEngine.Object.DestroyImmediate(readable);
		RenderTexture.active = previous;
		RenderTexture.ReleaseTemporary(renderTexture);
		Debug.Log($"Zombieland bundle inspect {arch}: texture={assetName}, width={texture.width}, height={texture.height}, output={outputPath}");
	}

	static void LogGameObject(string arch, string assetName, GameObject gameObject)
	{
		foreach (var component in gameObject.GetComponentsInChildren<Component>(true))
		{
			if (component == null)
				continue;

			Debug.Log($"Zombieland bundle inspect {arch}: gameObject={assetName}, component={component.GetType().FullName}, object={component.gameObject.name}");
			if (component is Transform transform)
				Debug.Log($"Zombieland bundle inspect {arch}: transform={component.gameObject.name}, localPosition={transform.localPosition}, localEulerAngles={transform.localEulerAngles}, localScale={transform.localScale}");
			if (component is ParticleSystemRenderer renderer)
			{
				var material = renderer.sharedMaterial;
				Debug.Log($"Zombieland bundle inspect {arch}: particleRenderer={component.gameObject.name}, renderMode={renderer.renderMode}, material={material?.name ?? "null"}, shader={material?.shader?.name ?? "null"}, alignment={renderer.alignment}, sortMode={renderer.sortMode}, sortingFudge={renderer.sortingFudge}, minParticleSize={renderer.minParticleSize}, maxParticleSize={renderer.maxParticleSize}");
			}
			if (component is ParticleSystem particleSystem)
			{
				var main = particleSystem.main;
				var emission = particleSystem.emission;
				var shape = particleSystem.shape;
				var colorOverLifetime = particleSystem.colorOverLifetime;
				var sizeOverLifetime = particleSystem.sizeOverLifetime;
				Debug.Log($"Zombieland bundle inspect {arch}: particleSystem={component.gameObject.name}, duration={main.duration}, loop={main.loop}, startLifetime={main.startLifetime.constant}, startSpeed={main.startSpeed.constant}, startSize={main.startSize.constant}, startColor={main.startColor.color}, simulationSpace={main.simulationSpace}, simulationSpeed={main.simulationSpeed}, scalingMode={main.scalingMode}, maxParticles={main.maxParticles}");
				Debug.Log($"Zombieland bundle inspect {arch}: particleEmission={component.gameObject.name}, enabled={emission.enabled}, rateOverTime={emission.rateOverTime.constant}, rateOverDistance={emission.rateOverDistance.constant}, burstCount={emission.burstCount}");
				Debug.Log($"Zombieland bundle inspect {arch}: particleShape={component.gameObject.name}, enabled={shape.enabled}, type={shape.shapeType}, radius={shape.radius}, angle={shape.angle}, arc={shape.arc}, rotation={shape.rotation}, scale={shape.scale}");
				Debug.Log($"Zombieland bundle inspect {arch}: particleColorOverLifetime={component.gameObject.name}, enabled={colorOverLifetime.enabled}");
				if (colorOverLifetime.enabled)
					LogMinMaxGradient(arch, component.gameObject.name, "particleColorOverLifetime", colorOverLifetime.color);
				Debug.Log($"Zombieland bundle inspect {arch}: particleSizeOverLifetime={component.gameObject.name}, enabled={sizeOverLifetime.enabled}, sizeMultiplier={sizeOverLifetime.sizeMultiplier}");
			}
		}
	}

	static void LogMinMaxGradient(string arch, string objectName, string label, ParticleSystem.MinMaxGradient value)
	{
		Debug.Log($"Zombieland bundle inspect {arch}: {label}={objectName}, mode={value.mode}, color={value.color}, colorMin={value.colorMin}, colorMax={value.colorMax}");
		LogGradient(arch, objectName, $"{label}.gradient", value.gradient);
		LogGradient(arch, objectName, $"{label}.gradientMin", value.gradientMin);
		LogGradient(arch, objectName, $"{label}.gradientMax", value.gradientMax);
	}

	static void LogGradient(string arch, string objectName, string label, Gradient gradient)
	{
		if (gradient == null)
			return;
		var colorKeys = string.Join(";", gradient.colorKeys.Select(key => $"{key.time:F3}:{key.color}"));
		var alphaKeys = string.Join(";", gradient.alphaKeys.Select(key => $"{key.time:F3}:{key.alpha:F3}"));
		Debug.Log($"Zombieland bundle inspect {arch}: {label}={objectName}, colorKeys=[{colorKeys}], alphaKeys=[{alphaKeys}]");
	}

	static void EnsureGeneratedBundleAssets()
	{
		FileUtil.DeleteFileOrDirectory(GeneratedAssetDir);
		FileUtil.DeleteFileOrDirectory($"{GeneratedAssetDir}.meta");
		Directory.CreateDirectory(GeneratedAssetDir);

		var zombieSymbiantShaderPath = $"{GeneratedAssetDir}/ZombieSymbiant.shader";
		File.Copy("Assets/Zombieland/ZombieSymbiant.shader", zombieSymbiantShaderPath, true);

		var mainMenuBackgroundEffectShaderPath = $"{GeneratedAssetDir}/MainMenuBackgroundEffect.shader";
		File.Copy("Assets/Zombieland/MainMenuBackgroundEffect.shader", mainMenuBackgroundEffectShaderPath, true);

		var titleBloodDripShaderPath = $"{GeneratedAssetDir}/TitleBloodDrip.shader";
		File.Copy("Assets/Zombieland/TitleBloodDrip.shader", titleBloodDripShaderPath, true);

		var metaballsShaderPath = $"{GeneratedAssetDir}/Metaballs.shader";
		File.WriteAllText(metaballsShaderPath, MetaballsShaderSource);

		var smokeTexturePath = $"{GeneratedAssetDir}/smoke_thin.png";
		WriteSmokeTexture(smokeTexturePath);

		var smokeNormalPath = $"{GeneratedAssetDir}/smoke_n.png";
		WriteFlatNormalTexture(smokeNormalPath);

		AssetDatabase.Refresh();
		ThrowOnShaderErrors(mainMenuBackgroundEffectShaderPath);
		ThrowOnShaderErrors(titleBloodDripShaderPath);
		ThrowOnShaderErrors(zombieSymbiantShaderPath);
		ThrowOnShaderErrors(metaballsShaderPath);
		SetTextureImporter(smokeTexturePath, TextureImporterType.Default);
		SetTextureImporter(smokeNormalPath, TextureImporterType.NormalMap);

		var zombieSymbiantShader = AssetDatabase.LoadAssetAtPath<Shader>(zombieSymbiantShaderPath);
		var zombieSymbiantMaterial = new Material(zombieSymbiantShader) { name = "ZombieSymbiant" };
		AssetDatabase.CreateAsset(zombieSymbiantMaterial, $"{GeneratedAssetDir}/ZombieSymbiant.mat");

		var smokeTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(smokeTexturePath);
		var smokeNormal = AssetDatabase.LoadAssetAtPath<Texture2D>(smokeNormalPath);
		var particleShader = Shader.Find("Particles/Standard Surface") ?? Shader.Find("Particles/Standard Unlit");
		var smokeMaterial = new Material(particleShader) { name = "Smoke_thin" };
		ConfigureLegacySmokeMaterial(smokeMaterial, smokeTexture, smokeNormal);
		AssetDatabase.CreateAsset(smokeMaterial, $"{GeneratedAssetDir}/Smoke_thin.mat");

		var dust = new GameObject("Dust");
		dust.transform.localEulerAngles = new Vector3(90f, 0f, 0f);
		var particleSystem = dust.AddComponent<ParticleSystem>();
		var main = particleSystem.main;
		main.duration = 0.75f;
		main.loop = false;
		main.startLifetime = 1f;
		main.startSpeed = 80f;
		main.startSize = 4f;
		main.startColor = new Color(1f, 1f, 1f, 0.376f);
		main.simulationSpace = ParticleSystemSimulationSpace.Local;
		main.simulationSpeed = 0.1f;
		main.maxParticles = 500;
		var emission = particleSystem.emission;
		emission.rateOverTime = 0f;
		emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 500) });
		var colorOverLifetime = particleSystem.colorOverLifetime;
		colorOverLifetime.enabled = true;
		colorOverLifetime.color = LegacyDustAlphaGradient();
		var shape = particleSystem.shape;
		shape.shapeType = ParticleSystemShapeType.Circle;
		shape.radius = 2f;
		shape.angle = 90f;
		shape.rotation = Vector3.zero;
		var renderer = dust.GetComponent<ParticleSystemRenderer>();
		renderer.renderMode = ParticleSystemRenderMode.Billboard;
		renderer.alignment = ParticleSystemRenderSpace.View;
		renderer.maxParticleSize = 1f;
		renderer.sharedMaterial = smokeMaterial;
		PrefabUtility.SaveAsPrefabAsset(dust, $"{GeneratedAssetDir}/Dust.prefab");
		UnityEngine.Object.DestroyImmediate(dust);

		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();

		foreach (var assetPath in new[]
		{
			$"{GeneratedAssetDir}/Dust.prefab",
			$"{GeneratedAssetDir}/Metaballs.shader",
			$"{GeneratedAssetDir}/smoke_n.png",
			$"{GeneratedAssetDir}/Smoke_thin.mat",
			$"{GeneratedAssetDir}/smoke_thin.png",
			$"{GeneratedAssetDir}/MainMenuBackgroundEffect.shader",
			$"{GeneratedAssetDir}/TitleBloodDrip.shader",
			$"{GeneratedAssetDir}/ZombieSymbiant.mat",
			$"{GeneratedAssetDir}/ZombieSymbiant.shader"
		})
		{
			var importer = AssetImporter.GetAtPath(assetPath);
			if (importer == null)
				throw new Exception($"Could not label generated bundle asset {assetPath}");
			importer.assetBundleName = "zombieland";
			importer.SaveAndReimport();
		}
	}

	static void ThrowOnShaderErrors(string shaderPath)
	{
		var shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
		if (shader == null)
			throw new Exception($"Could not import generated shader {shaderPath}");

		var errors = ShaderUtil.GetShaderMessages(shader)
			.Where(message => string.Equals(message.severity.ToString(), "Error", StringComparison.OrdinalIgnoreCase))
			.Select(message => message.message)
			.ToArray();
		if (errors.Length > 0)
			throw new Exception($"Shader compiler errors in {shaderPath}: {string.Join(" | ", errors)}");
	}

	static void SetTextureImporter(string path, TextureImporterType type)
	{
		var importer = AssetImporter.GetAtPath(path) as TextureImporter;
		if (importer == null)
			throw new Exception($"Could not import generated texture {path}");

		importer.textureType = type;
		importer.alphaIsTransparency = type == TextureImporterType.Default;
		importer.mipmapEnabled = false;
		importer.SaveAndReimport();
	}

	static void ConfigureLegacySmokeMaterial(Material smokeMaterial, Texture2D smokeTexture, Texture2D smokeNormal)
	{
		if (smokeMaterial.HasProperty("_MainTex"))
			smokeMaterial.SetTexture("_MainTex", smokeTexture);
		if (smokeMaterial.HasProperty("_BumpMap"))
			smokeMaterial.SetTexture("_BumpMap", smokeNormal);
		if (smokeMaterial.HasProperty("_Color"))
			smokeMaterial.SetColor("_Color", new Color(0.25f, 0.25f, 0.25f, 0.125f));
		if (smokeMaterial.HasProperty("_Mode"))
			smokeMaterial.SetFloat("_Mode", 2f);
		if (smokeMaterial.HasProperty("_SrcBlend"))
			smokeMaterial.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
		if (smokeMaterial.HasProperty("_DstBlend"))
			smokeMaterial.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
		if (smokeMaterial.HasProperty("_ZWrite"))
			smokeMaterial.SetFloat("_ZWrite", 0f);
		if (smokeMaterial.HasProperty("_Cull"))
			smokeMaterial.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Back);
		smokeMaterial.EnableKeyword("_ALPHABLEND_ON");
		smokeMaterial.DisableKeyword("_ALPHATEST_ON");
		smokeMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
		smokeMaterial.renderQueue = 3000;
	}

	static Gradient LegacyDustAlphaGradient()
	{
		var gradient = new Gradient();
		gradient.SetKeys(
			new[]
			{
				new GradientColorKey(Color.white, 0f),
				new GradientColorKey(Color.white, 1f)
			},
			new[]
			{
				new GradientAlphaKey(0f, 0f),
				new GradientAlphaKey(1f, 0.026f),
				new GradientAlphaKey(1f, 0.76f),
				new GradientAlphaKey(0f, 1f)
			});
		return gradient;
	}

	static void WriteSmokeTexture(string path)
	{
		var texture = new Texture2D(64, 64, TextureFormat.RGBA32, false);
		for (var y = 0; y < texture.height; y++)
		{
			for (var x = 0; x < texture.width; x++)
			{
				var dx = (x + 0.5f) / texture.width * 2f - 1f;
				var dy = (y + 0.5f) / texture.height * 2f - 1f;
				var distance = Mathf.Sqrt(dx * dx + dy * dy);
				var edge = distance >= 0.98f ? 0f : Mathf.SmoothStep(0.98f, 0.76f, distance);
				var center = Mathf.Lerp(0.73f, 0.38f, Mathf.SmoothStep(0f, 0.72f, distance));
				var alpha = 0.73f * edge * center;
				texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
			}
		}
		texture.Apply();
		File.WriteAllBytes(path, texture.EncodeToPNG());
		UnityEngine.Object.DestroyImmediate(texture);
	}

	static void WriteFlatNormalTexture(string path)
	{
		var texture = new Texture2D(16, 16, TextureFormat.RGBA32, false);
		for (var y = 0; y < texture.height; y++)
			for (var x = 0; x < texture.width; x++)
				texture.SetPixel(x, y, new Color(0.5f, 0.5f, 1f, 1f));
		texture.Apply();
		File.WriteAllBytes(path, texture.EncodeToPNG());
		UnityEngine.Object.DestroyImmediate(texture);
	}

	static string CurrentArchitecture()
	{
		switch (Application.platform)
		{
			case RuntimePlatform.WindowsEditor:
				return "Win64";
			case RuntimePlatform.LinuxEditor:
				return "Linux";
			case RuntimePlatform.OSXEditor:
				return "MacOS";
			default:
				throw new Exception($"Unsupported Unity editor platform for Zombieland asset bundle build: {Application.platform}");
		}
	}

	static BuildTarget TargetForArchitecture(string arch)
	{
		switch (arch)
		{
			case "Win64":
				return BuildTarget.StandaloneWindows64;
			case "Linux":
				return BuildTarget.StandaloneLinux64;
			case "MacOS":
				return BuildTarget.StandaloneOSX;
			default:
				throw new Exception($"Unknown Zombieland asset bundle architecture: {arch}");
		}
	}

	static string DeployBundle(string arch, string bundlePath)
	{
		var dest = Path.Combine(DeploymentDir(), arch);
		if (!Directory.Exists(dest))
			Directory.CreateDirectory(dest);

		var destPath = Path.Combine(dest, BundleName);
		File.Copy(bundlePath, destPath, true);
		Debug.Log($"Zombieland bundle deployed {arch}: path={destPath}");
		return destPath;
	}

	static void Build(string arch)
	{
		var src = $"Assets/AssetBundles/{arch}";
		if (Directory.Exists(src))
			Directory.Delete(src, true);
		Directory.CreateDirectory(src);
		AssetDatabase.Refresh();

		BuildPipeline.BuildAssetBundles(src, BuildAssetBundleOptions.None, TargetForArchitecture(arch));

		var bundlePath = Path.Combine(src, BundleName);
		if (File.Exists(bundlePath) == false)
			throw new Exception($"Unity did not produce {bundlePath}. No source asset is currently labelled for the {BundleName} asset bundle.");

		DeployBundle(arch, bundlePath);
	}

	const string MetaballsShaderSource = @"Shader ""Custom/Metaballs""
{
	SubShader
	{
		Tags { ""Queue""=""Transparent"" ""RenderType""=""Transparent"" }
		Blend One Zero
		ZWrite Off
		Cull Off

		Pass
		{
			CGPROGRAM
			#include ""UnityCG.cginc""

			#pragma target 5.0
			#pragma vertex vert_img
			#pragma fragment frag

				struct Metaball
				{
					float4 shape;
					float4 motion;
					float4 tint;
				};

				StructuredBuffer<Metaball> _MetaballBuffer;
				int _MetaballCount;
				float4 _MetaballWorldSize;

			float4 frag(v2f_img input) : SV_Target
			{
				float field = 0.0;
				float3 color = 0.0;
					for (int i = 0; i < _MetaballCount; i++)
					{
						Metaball ball = _MetaballBuffer[i];
						float radius = ball.shape.x * saturate(ball.shape.y);
						if (radius <= 0.0001)
							continue;

						float2 delta = (input.uv - ball.motion.xy) * max(_MetaballWorldSize.xy, float2(0.0001, 0.0001));
						float contribution = (radius * radius) / max(dot(delta, delta), 0.0001);
						contribution *= contribution;
					contribution *= max(ball.shape.z, 0.0);
					field += contribution;
					color += ball.tint.rgb * contribution;
				}

				float alpha = smoothstep(0.45, 1.20, field) * 0.40;
				float edge = smoothstep(0.45, 1.80, field);
				float3 body = field > 0.0001 ? color / field : float3(0.0, 0.8, 0.0);
				body = lerp(float3(0.0, 0.0, 0.0), body, edge);
				return float4(body, alpha);
			}
			ENDCG
		}
	}
}";

}
