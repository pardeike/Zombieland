using HarmonyLib;
using RimBridgeServer.Sdk;
using System;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	public sealed partial class ZombielandBridgeTools
	{
		static GameObject dustRenderSample;
		static Material dustRenderMaterial;

		[Tool("zombieland/thumper_dust_render_sample", Description = "Freeze the production dust prefab at a particle age for screenshot comparisons. Negative seconds removes the sample. This checks rendered pixels separately from seismic damage.")]
		public static object ThumperDustRenderSample(float seconds = 0.3f, float opacity = 1f, float tint = -1f)
		{
			var map = CurrentMap;
			if (map == null)
				return new { success = false, error = "No current map is loaded." };
			if (!float.IsFinite(seconds) || !float.IsFinite(opacity) || !float.IsFinite(tint) || seconds > 1.1f || opacity < 0f || opacity > 8f || tint < -1f || tint > 1f)
				return new { success = false, error = "Use seconds <= 1.1, an opacity multiplier between 0 and 8, and tint -1 for the production color or 0 through 1 for gray." };

			Find.TickManager.Pause();
			if (dustRenderSample != null)
				UnityEngine.Object.Destroy(dustRenderSample);
			if (dustRenderMaterial != null)
				UnityEngine.Object.Destroy(dustRenderMaterial);
			dustRenderSample = null;
			dustRenderMaterial = null;
			if (seconds < 0f)
				return new { success = true, removed = true };

			var center = new IntVec3(55, 0, 55);

			dustRenderSample = Assets.NewDust();
			dustRenderSample.transform.position = center.ToVector3ShiftedWithAltitude(0.5f);
			var particles = dustRenderSample.GetComponent<ParticleSystem>();
			AccessTools.Method(typeof(ZombieThumper), "ConfigureDustParticleSystem").Invoke(null, new object[] { particles, 25 });
			var main = particles.main;
			main.simulationSpeed = 1f;
			particles.useAutoRandomSeed = false;
			particles.randomSeed = 1234;
			var renderer = dustRenderSample.GetComponent<ParticleSystemRenderer>();
			dustRenderMaterial = new Material(renderer.sharedMaterial);
			var color = dustRenderMaterial.color;
			color.a = Mathf.Clamp01(color.a * opacity);
			if (tint >= 0f)
				color.r = color.g = color.b = tint;
			dustRenderMaterial.color = color;
			renderer.sharedMaterial = dustRenderMaterial;
			particles.Simulate(seconds, true, true, false);
			var sample = new ParticleSystem.Particle[1];
			var sampleCount = particles.GetParticles(sample);
			// Waylith emits the burst after its first simulation step. Align the
			// particle age with the requested reference age before taking a screenshot.
			if (sampleCount > 0)
			{
				var missingAge = seconds - (sample[0].startLifetime - sample[0].remainingLifetime);
				if (missingAge > 0.00001f)
					particles.Simulate(missingAge, true, false, false);
				sampleCount = particles.GetParticles(sample);
			}
			particles.Pause(true);
			var gradient = particles.colorOverLifetime.color;
			return new
			{
				success = true,
				seconds,
				opacity,
				particleTime = particles.time,
				particleCount = particles.particleCount,
				gradientAlpha = gradient.Evaluate(Mathf.Clamp01(seconds)).a,
				particle = sampleCount == 0 ? null : new { sample[0].startLifetime, sample[0].remainingLifetime, startAlpha = sample[0].startColor.a },
				shader = dustRenderMaterial.shader.name,
				color = new { color.r, color.g, color.b, color.a },
				renderQueue = dustRenderMaterial.renderQueue,
				texture = dustRenderMaterial.mainTexture?.name,
				keywords = dustRenderMaterial.shaderKeywords,
				capture = new { x = 17, z = 17, width = 76, height = 76, rootSize = 50 }
			};
		}
	}
}
