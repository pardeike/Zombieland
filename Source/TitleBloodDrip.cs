using System;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	[StaticConstructorOnStartup]
	static class TitleBloodDrip
	{
		const int MaximumPendantSites = 16;
		const float MaximumDeltaTime = 0.25f;
		const int SimulationPass = 0;
		const int DisplayPass = 1;
		static readonly int deltaTimeId = Shader.PropertyToID("_DeltaTime");
		static readonly int simulationTimeId = Shader.PropertyToID("_SimulationTime");
		static Texture2D mask;
		static Material material;
		static RenderTexture state;
		static RenderTexture nextState;
		static float simulationTime;
		static float lastRealtime;
		static int lastFrame = -1;
		static bool disabledAfterError;

		public static void Draw(Rect rect)
		{
			if (disabledAfterError)
				return;

			try
			{
				var shader = Assets.TitleBloodDripShader;
				if (shader == null || shader.isSupported == false || shader.passCount < 2)
				{
					disabledAfterError = true;
					Log.Warning("Zombieland title blood drips are unavailable because their fluid shader is missing or unsupported. The static title will still be shown.");
					return;
				}

				if (material == null || material.shader != shader)
					Initialize(shader);
				EnsureRenderTextures();

				AdvanceOncePerFrame();
				var effectRect = new Rect(rect.x, rect.y, rect.width, rect.height * 2f);
				Graphics.DrawTexture(effectRect, state, material, DisplayPass);
			}
			catch (Exception ex)
			{
				disabledAfterError = true;
				ReleaseSimulation();
				Log.Warning($"Zombieland disabled title blood drips after a fluid-simulation error; the static title will still be shown: {ex}");
			}
		}

		static void Initialize(Shader shader)
		{
			ReleaseSimulation();
			if (SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf) == false)
				throw new NotSupportedException("ARGBHalf render textures are not supported by this graphics device.");

			mask = Tools.LoadTexture("ZombielandTitle-DripArea", true, false);
			mask.filterMode = FilterMode.Point;
			material = new Material(shader)
			{
				name = "Zombieland title blood fluid simulation",
				hideFlags = HideFlags.HideAndDontSave
			};
			material.SetTexture("_MaskTex", mask);

			RecreateRenderTextures();
		}

		static RenderTexture CreateStateTexture(string name)
		{
			var texture = new RenderTexture(MaximumPendantSites, 1, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
			{
				name = name,
				filterMode = FilterMode.Point,
				wrapMode = TextureWrapMode.Clamp,
				useMipMap = false,
				autoGenerateMips = false,
				hideFlags = HideFlags.HideAndDontSave
			};
			texture.Create();
			if (texture.IsCreated() == false)
				throw new InvalidOperationException($"Could not create {name}.");
			return texture;
		}

		static void EnsureRenderTextures()
		{
			if (state != null && nextState != null && state.IsCreated() && nextState.IsCreated())
				return;
			RecreateRenderTextures();
		}

		static void RecreateRenderTextures()
		{
			ReleaseRenderTexture(ref state);
			ReleaseRenderTexture(ref nextState);
			state = CreateStateTexture("Zombieland title blood pendant state");
			nextState = CreateStateTexture("Zombieland title blood next pendant state");
			Clear(state);
			Clear(nextState);
			simulationTime = 0f;
			lastRealtime = Time.realtimeSinceStartup;
			lastFrame = -1;
		}

		static void AdvanceOncePerFrame()
		{
			if (lastFrame == Time.frameCount)
				return;
			lastFrame = Time.frameCount;

			var now = Time.realtimeSinceStartup;
			var deltaTime = Mathf.Clamp(now - lastRealtime, 0f, MaximumDeltaTime);
			lastRealtime = now;
			if (deltaTime <= 0f)
				return;

			var previous = RenderTexture.active;
			var previousSrgbWrite = GL.sRGBWrite;
			try
			{
				GL.sRGBWrite = false;
				SimulateStep(deltaTime);
			}
			finally
			{
				GL.sRGBWrite = previousSrgbWrite;
				RenderTexture.active = previous;
			}
		}

		static void SimulateStep(float deltaTime)
		{
			material.SetFloat(deltaTimeId, deltaTime);
			material.SetFloat(simulationTimeId, simulationTime);

			Graphics.Blit(state, nextState, material, SimulationPass);
			(state, nextState) = (nextState, state);
			simulationTime += deltaTime;
		}

		static void Clear(RenderTexture texture)
		{
			var previous = RenderTexture.active;
			try
			{
				RenderTexture.active = texture;
				GL.Clear(false, true, Color.clear);
			}
			finally
			{
				RenderTexture.active = previous;
			}
		}

		public static void Release()
		{
			ReleaseSimulation();
		}

		static void ReleaseSimulation()
		{
			ReleaseRenderTexture(ref state);
			ReleaseRenderTexture(ref nextState);
			Destroy(material);
			Destroy(mask);
			material = null;
			mask = null;
			lastFrame = -1;
		}

		static void ReleaseRenderTexture(ref RenderTexture texture)
		{
			if (texture == null)
				return;
			if (texture.IsCreated())
				texture.Release();
			Destroy(texture);
			texture = null;
		}

		static void Destroy(UnityEngine.Object value)
		{
			if (value != null)
				UnityEngine.Object.Destroy(value);
		}
	}
}
