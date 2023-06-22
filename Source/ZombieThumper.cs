using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Analytics;
using Verse;
using Verse.Sound;

namespace ZombieLand
{

	[StaticConstructorOnStartup]
	public class ZombieThumper : Building
	{
		private enum State
		{
			Resting,
			Upwards,
			Paused,
			Falling,
			Impacting
		}

		private class Dust
		{
			public long impactStart;
			public GameObject obj;
			public int currentRadius;
		}

		public const int upwardsTicks = 180;
		public const int impactDurationTicks = 360;
		public const float accelerationFactor = 0.35f;

		public int intervalTicks = GenDate.TicksPerHour;
		public float intensity = 0.25f;

		private static readonly float cyanComponent = 155f / 255f;
		private static readonly Vector3 drawSize = new(2f, 0f, 4f);
		private static readonly Vector3 drawOffset = new(0f, 0f, 1f);
		private static readonly OverlayDrawer ThumperBase = new("Thumper/ThumperBase", ShaderDatabase.Cutout, drawSize: drawSize, drawOffset: drawOffset);
		private static readonly OverlayDrawer ThumperBackground = new("Thumper/ThumperBackground", ShaderDatabase.Cutout, drawSize: drawSize, drawOffset: drawOffset);
		private static readonly OverlayDrawer ThumperForeground = new("Thumper/ThumperForeground", ShaderDatabase.Cutout, drawSize: drawSize, drawOffset: drawOffset);
		private static readonly OverlayDrawer ThumperRing = new("Thumper/ThumperRing", ShaderDatabase.Transparent, drawSize: drawSize, drawOffset: drawOffset);
		private static readonly OverlayDrawer ThumperLight = new("Thumper/ThumperLight", ShaderDatabase.Cutout, drawSize: drawSize, drawOffset: drawOffset);
		private static readonly Texture2D gizmoIcon = ContentFinder<Texture2D>.Get("ThumperControl", true);

		private Action[] nextStates = Array.Empty<Action>();
		private readonly List<Dust> dusts = new();
		private Vector3 dustOffset = new(0.5f, 0, 0.5f);

		private State state = State.Resting;
		private int stateValue = 0;
		private int lastImpactTicks = 0;

		private Sustainer operatingSustainer;
		private Sustainer liftingSustainer;

		public const int MaxRadius = 50;
		private static readonly HashSet<IntVec3>[] radialCells = new HashSet<IntVec3>[MaxRadius + 1];

		static ZombieThumper()
		{
			var min = 0;
			radialCells[0] = new HashSet<IntVec3>();
			radialCells[1] = new HashSet<IntVec3>();
			for (var radius = 2; radius <= MaxRadius; radius++)
			{
				var max = GenRadial.NumCellsInRadius(radius);
				radialCells[radius] = new HashSet<IntVec3>();
				for (var i = min; i < max; i++)
				{
					var cell = GenRadial.RadialPattern[i];
					radialCells[radius].Add(cell);
				}
				min = max;
			}
		}

		public override void SpawnSetup(Map map, bool respawningAfterLoad)
		{
			base.SpawnSetup(map, respawningAfterLoad);

			nextStates = new[] { PrepareUp, PreparePause, PrepareFall, PrepareImpact, PrepareRest };
			nextStates.Last()();
			lastImpactTicks = GenTicks.TicksGame;

			operatingSustainer ??= CustomDefs.ThumperOperating.TrySpawnSustainer(SoundInfo.InMap(this, MaintenanceType.None));

			TimeControlService.Subscribe(this, speed => dusts.Do(dust =>
			{
				var particleSystem = dust.obj.GetComponent<ParticleSystem>();
				var main = particleSystem.main;
				main.simulationSpeed = Find.TickManager.TickRateMultiplier / 10f;
			}));

			var compRefuelable = this.TryGetComp<CompRefuelable>();
			if (compRefuelable != null)
				compRefuelable.configuredTargetFuelLevel = compRefuelable.Props.fuelCapacity;

			ClearMapsService.Subscribe(this, RemoveDusts);
		}

		private void RemoveDusts()
		{
			foreach (var dust in dusts)
				UnityEngine.Object.Destroy(dust.obj);
			dusts.Clear();
		}

		public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
		{
			base.DeSpawn(mode);

			RemoveDusts();

			TimeControlService.Unsubscribe(this);
			ClearMapsService.Unsubscribe(this);

			operatingSustainer?.End();
			operatingSustainer = null;

			liftingSustainer?.End();
			liftingSustainer = null;
		}

		public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
		{
			base.Destroy(mode);

			RemoveDusts();

			TimeControlService.Unsubscribe(this);
			ClearMapsService.Unsubscribe(this);

			operatingSustainer?.End();
			operatingSustainer = null;

			liftingSustainer?.End();
			liftingSustainer = null;
		}

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.Look(ref intensity, "intensity", 0.25f);
			Scribe_Values.Look(ref intervalTicks, "intervalTicks", GenDate.TicksPerHour);
			Scribe_Values.Look(ref state, "state", State.Resting);
			Scribe_Values.Look(ref stateValue, "stateValue", 0);
			Scribe_Values.Look(ref lastImpactTicks, "lastImpactTicks", 0);
		}

		public override void Tick()
		{
			base.Tick();

			stateValue--;
			if (nextStates.Length > 0 && stateValue <= 0)
				nextStates[(int)state]();

			var map = Map;
			if (map == null)
				return;

			var pheromoneGrid = map.GetGrid();
			var thingGrid = map.thingGrid;
			var center = Position;

			for(var i = 0; i < dusts.Count; i++)
			{
				var dust = dusts[i];

				var particleSystem = dust.obj.GetComponent<ParticleSystem>();
				var time = particleSystem.time;
				var maxRadius = Radius;
				var radius = Tools.Boxed(2 + Mathf.FloorToInt(time * particleSystem.main.startSpeed.constant), 2, maxRadius);
				if (radius != dust.currentRadius)
				{
					var seenThings = new HashSet<Thing>() { this };
					for (var r = dust.currentRadius + 1; r <= radius; r++)
					{
						var timestamp = dust.impactStart - Mathf.FloorToInt(r * r / 2.5f);
						var damage = (MaxRadius - r) * 2f / MaxRadius;
						var dinfo = new DamageInfo(CustomDefs.SeismicWave, damage, 0f, -1f, this);
						radialCells[r].Do(cell =>
						{
							cell += center;
							pheromoneGrid.BumpTimestamp(cell, timestamp);
							thingGrid.ThingsAt(cell)
								.Except(seenThings)
								.Do(thing =>
								{
									seenThings.Add(thing);
									_ = thing.TakeDamage(dinfo);
								});
						});
					}
					dust.currentRadius = radius;

					if (radius >= maxRadius)
					{
						_ = dusts.Remove(dust);
						UnityEngine.Object.Destroy(dust.obj);
						i--;
					}
				}
			}
		}

		public bool IsActive
		{
			get
			{
				var refuelable = this.TryGetComp<CompRefuelable>();
				if (refuelable != null && refuelable.Fuel <= 0f)
					return false;

				var switchable = this.TryGetComp<CompSwitchable>();
				if (switchable != null && switchable.isActive == false)
					return false;

				return true;
			}
		}

		public int Radius => Mathf.FloorToInt(MaxRadius * intensity);

		public override IEnumerable<Gizmo> GetGizmos()
		{
			var gizmos = base.GetGizmos();
			foreach(var gizmo in gizmos)
				yield return gizmo;

			yield return new Command_Action
			{
				action = () =>
				{
					var window = new Dialog_ThumperSettings(this);
					Find.WindowStack.Add(window);
				},
				defaultLabel = "ThumperControl".Translate(),
				defaultDesc = "ThumperControlDesc".Translate(),
				hotKey = KeyBindingDefOf.Misc1,
				icon = gizmoIcon
			};
		}

		public void PrepareRest()
		{
			state = State.Resting;
			stateValue = 30;
		}

		public void PrepareUp()
		{
			if (IsActive == false)
				return;

			state = State.Upwards;
			stateValue = Mathf.FloorToInt(upwardsTicks * intensity);
			liftingSustainer ??= CustomDefs.ThumperLifting.TrySpawnSustainer(SoundInfo.InMap(this, MaintenanceType.None));
		}

		public void PreparePause()
		{
			state = State.Paused;
			var ticksSinceLastImpact = GenTicks.TicksGame - lastImpactTicks;
			stateValue = intervalTicks - ticksSinceLastImpact;
			liftingSustainer?.End();
			liftingSustainer = null;
			CustomDefs.ThumperConnecting.PlayOneShot(SoundInfo.InMap(this));
		}

		public void PrepareFall()
		{
			state = State.Falling;
			var height = Mathf.FloorToInt(upwardsTicks * intensity);
			stateValue = Mathf.FloorToInt(Mathf.Sqrt(height / accelerationFactor));
			CustomDefs.ThumperRelease.PlayOneShot(SoundInfo.InMap(this));
		}

		public void PrepareImpact()
		{
			lastImpactTicks = GenTicks.TicksGame;
			var dust = new Dust() { impactStart = Tools.Ticks(), obj = Assets.NewDust(), currentRadius = -1 };
			dust.obj.transform.position = Position.ToVector3ShiftedWithAltitude(0.5f) + dustOffset;
			var particleSystem = dust.obj.GetComponent<ParticleSystem>();
			var main = particleSystem.main;
			main.simulationSpeed = Find.TickManager.TickRateMultiplier / 10f;
			dusts.Add(dust);

			state = State.Impacting;
			stateValue = impactDurationTicks;

			this.TryGetComp<CompRefuelable>()?.ConsumeFuel(Tools.Difficulty());

			CustomDefs.ThumperClang.PlayOneShot(SoundInfo.InMap(this));
			CustomDefs.ThumperImpact.PlayOneShot(SoundInfo.InMap(this));
		}

		static void DrawRadiusRing(IntVec3 postion, float radius)
		{
			var ringDrawCells = new HashSet<IntVec3>();
			var num = GenRadial.NumCellsInRadius(radius);
			for (var i = 0; i < num; i++)
			{
				var intVec = postion + GenRadial.RadialPattern[i];
				ringDrawCells.Add(intVec);
				ringDrawCells.Add(intVec + new IntVec3(0, 0, 1));
				ringDrawCells.Add(intVec + new IntVec3(1, 0, 0));
				ringDrawCells.Add(intVec + new IntVec3(1, 0, 1));
			}
			GenDraw.DrawFieldEdges(ringDrawCells.ToList(), Color.white, null);
		}

		public override void Draw()
		{
			base.Draw();

			float polePosition = 0f;
			float impactRingColor = 0f;

			var max = Mathf.FloorToInt(upwardsTicks * intensity);
			switch (state)
			{
				case State.Resting:
					break;
				case State.Upwards:
					polePosition = GenMath.LerpDoubleClamped(0, max, intensity, 0f, stateValue);
					break;
				case State.Paused:
					polePosition = intensity;
					break;
				case State.Falling:
					// https://www.desmos.com/calculator/wzqh80n9mx
					var start = Mathf.FloorToInt(Mathf.Sqrt(max / accelerationFactor));
					var val = (start - stateValue) * (start - stateValue) * accelerationFactor;
					polePosition = GenMath.LerpDoubleClamped(0, max, intensity, 0f, val);
					break;
				case State.Impacting:
					// https://www.desmos.com/calculator/6mbuuucmcd
					const float a = -0.8f;
					const float b = 0.65f;
					const float c = 1.39f;
					const float d = 0.5f;
					var x = c - 2 * GenMath.LerpDoubleClamped(0, impactDurationTicks, 1f, 0f, stateValue);
					impactRingColor = Mathf.Sin((x * x * x + a) / b) / 2f + d;
					break;
			}

			var color = GenMath.LerpDoubleClamped(0f, 1f, 0f, cyanComponent, impactRingColor);
			var z = GenMath.LerpDoubleClamped(0f, 1f, 1.6f, 0f, polePosition);

			var position = DrawPos;
			ThumperBase.Draw(position, AltitudeLayer.BuildingOnTop, altitudeOffset: 0, Color.white);
			ThumperRing.Draw(position, AltitudeLayer.BuildingOnTop, altitudeOffset: 1, new Color(0f, color, color));
			ThumperBackground.Draw(position, AltitudeLayer.BuildingOnTop, altitudeOffset: 2, Color.white);
			ThumperLight.Draw(position - new Vector3(0, 0, z), AltitudeLayer.BuildingOnTop, altitudeOffset: 3, Color.white);
			ThumperForeground.Draw(position, AltitudeLayer.BuildingOnTop, altitudeOffset: 4, Color.white);

			if (Find.Selector.IsSelected(this))
				DrawRadiusRing(Position, Radius);
		}
	}
}