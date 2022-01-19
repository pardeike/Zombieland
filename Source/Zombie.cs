using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace ZombieLand
{
	public enum NthTick
	{
		Every2,
		Every10,
		Every15,
		Every30,
		Every60,
		Every480,
		Every960
	}

	public class Verb_Shock : Verb
	{
		public override bool TryCastShot()
		{
			return true;
		}
	}

	public class ZombieSerum : ThingWithComps
	{
	}

	public class ZombieExtract : ThingWithComps
	{
	}

	public class HealerInfo : IExposable
	{
		public int step;
		public Pawn pawn;

		public void ExposeData()
		{
			Scribe_Values.Look(ref step, "step");
			Scribe_References.Look(ref pawn, "pawn");
		}

		public HealerInfo(Pawn pawn)
		{
			step = 0;
			this.pawn = pawn;
		}
	}

	[StaticConstructorOnStartup]
	public class Zombie : Pawn, IDisposable
	{
		public ZombieState state = ZombieState.Emerging;
		public int raging;
		public IntVec3 wanderDestination = IntVec3.Invalid;
		public static Color[] zombieColors;

		int rubbleTicks = Rand.Range(0, 60);
		public int rubbleCounter;
		List<Rubble> rubbles = new List<Rubble>();

		public IntVec2 sideEyeOffset;
		public bool wasMapPawnBefore;
		public IntVec3 lastGotoPosition = IntVec3.Invalid;
		public bool isHealing = false;
		public float consciousness = 1f;
		public Pawn ropedBy;

		// suicide bomber
		public float bombTickingInterval = -1f;
		public bool bombWillGoOff;
		public int lastBombTick;
		public bool IsSuicideBomber => bombTickingInterval != -1;

		// toxic splasher
		public bool isToxicSplasher = false;

		// tanky operator
		public float hasTankyShield = -1f;
		public float hasTankyHelmet = -1f;
		public float hasTankySuit = -1f;
		public bool IsTanky => hasTankyHelmet > 0f || hasTankySuit > 0f;

		// miner
		public bool isMiner = false;
		public int miningCounter = 0;

		// electrifier
		public bool isElectrifier = false;
		public int electricDisabledUntil = 0;
		public bool IsActiveElectric => isElectrifier && Downed == false && Find.TickManager.TicksGame > electricDisabledUntil && this.InWater() == false;
		public void DisableElectric(int ticks) { electricDisabledUntil = Find.TickManager.TicksGame + ticks; }
		public int electricCounter = -1000;
		public float electricAngle = 0;
		public List<KeyValuePair<float, int>> absorbAttack = new List<KeyValuePair<float, int>>();

		// albino
		public bool isAlbino = false;
		public int scream = -1;

		// dark slimer
		public bool isDarkSlimer = false;

		// healer
		public bool isHealer = false;
		public List<HealerInfo> healInfo = new List<HealerInfo>();

		// transient vars
		public bool needsGraphics = false;
		public bool isOnFire = false;
		public bool checkSmashable = true;
		public float currentDownedAngle = 0f;
		bool disposed = false;

		public ZombieStateHandler.TrackMove[] topTrackingMoves = new ZombieStateHandler.TrackMove[Constants.NUMBER_OF_TOP_MOVEMENT_PICKS];
		public readonly int[] adjIndex8 = { 0, 1, 2, 3, 4, 5, 6, 7 };
		public int prevIndex8;

		static readonly int totalNthTicks;
		static public int[] nthTickValues;

		static Zombie()
		{
			var nths = Enum.GetNames(typeof(NthTick));
			totalNthTicks = nths.Length;
			nthTickValues = new int[totalNthTicks];
			for (var n = 0; n < totalNthTicks; n++)
			{
				var vstr = nths[n].ReplaceFirst("Every", "");
				nthTickValues[n] = int.Parse(vstr);
			}

			zombieColors = new Color[]
			{
				"442a0a".HexColor(),
				"615951".HexColor(),
				"1f4960".HexColor(),
				"182a64".HexColor(),
				"73000d".HexColor(),
				"2c422a".HexColor(),
				"332341".HexColor()
			};
			(zombieColors.Clone() as Color[]).Do(c =>
			{
				c.r *= Rand.Range(0.2f, 1f);
				c.g *= Rand.Range(0.2f, 1f);
				c.b *= Rand.Range(0.2f, 1f);
				_ = zombieColors.AddItem(c);
			});
			_ = zombieColors.AddItem("000000".HexColor());
		}

		public void UpgradeOldZombieData()
		{
			// fix leaner
			if ((Drawer.leaner is ZombieLeaner) == false)
				Drawer.leaner = new ZombieLeaner(this);

			// define suicide bombers
			if (bombTickingInterval == 0f)
				bombTickingInterval = -1f;

			// define tanky operators
			if (hasTankyShield == 0f)
				hasTankyShield = -1f;
			if (hasTankyHelmet == 0f)
				hasTankyHelmet = -1f;
			if (hasTankySuit == 0f)
				hasTankySuit = -1f;
		}

		public override void ExposeData()
		{
			base.ExposeData();

			var wasColonist = wasMapPawnBefore;
			Scribe_Values.Look(ref state, "zstate");
			Scribe_Values.Look(ref raging, "raging");
			Scribe_Values.Look(ref wanderDestination, "wanderDestination");
			Scribe_Values.Look(ref rubbleTicks, "rubbleTicks");
			Scribe_Values.Look(ref rubbleCounter, "rubbleCounter");
			Scribe_Collections.Look(ref rubbles, "rubbles", LookMode.Deep);
			Scribe_Values.Look(ref wasColonist, "wasColonist");
			Scribe_Values.Look(ref wasMapPawnBefore, "wasMapPawnBefore");
			Scribe_Values.Look(ref bombWillGoOff, "bombWillGoOff");
			Scribe_Values.Look(ref bombTickingInterval, "bombTickingInterval");
			Scribe_Values.Look(ref isToxicSplasher, "toxicSplasher");
			Scribe_Values.Look(ref isMiner, "isMiner");
			Scribe_Values.Look(ref isElectrifier, "isElectrifier");
			Scribe_Values.Look(ref electricDisabledUntil, "electricDisabledUntil");
			Scribe_Values.Look(ref isAlbino, "isAlbino");
			Scribe_Values.Look(ref isDarkSlimer, "isDarkSlimer");
			Scribe_Values.Look(ref isHealer, "isHealer");
			Scribe_Values.Look(ref scream, "scream");
			Scribe_Values.Look(ref hasTankyShield, "tankyShield");
			Scribe_Values.Look(ref hasTankyHelmet, "tankyHelmet");
			Scribe_Values.Look(ref hasTankySuit, "tankySuit");
			Scribe_Values.Look(ref isHealing, "isHealing");
			Scribe_Values.Look(ref consciousness, "consciousness");
			Scribe_References.Look(ref ropedBy, "ropedBy");
			wasMapPawnBefore |= wasColonist;

			if (Scribe.mode == LoadSaveMode.PostLoadInit)
			{
				UpgradeOldZombieData();

				_ = ZombieGenerator.FixGlowingEyeOffset(this);

				if (ZombieSettings.Values.useCustomTextures)
					needsGraphics = true; // make custom textures in renderer

				isOnFire = this.HasAttachment(ThingDefOf.Fire);
				checkSmashable = true;
			}

			if (Scribe.mode == LoadSaveMode.ResolvingCrossRefs)
				_ = ageTracker.CurLifeStageIndex; // trigger calculations
		}

		~Zombie()
		{
			Dispose(false);
		}

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		void Dispose(bool disposing)
		{
			_ = disposing;
			if (disposed) return;
			disposed = true;
			CleanupZombie();
		}

		public void Randomize8()
		{
			var nextIndex = Constants.random.Next(8);
			var c = adjIndex8[prevIndex8];
			adjIndex8[prevIndex8] = adjIndex8[nextIndex];
			adjIndex8[nextIndex] = c;
			prevIndex8 = nextIndex;
		}

		void CleanupZombie()
		{
			// log
			Find.BattleLog.Battles.Do(battle => battle.Entries.RemoveAll(entry => entry.Concerns(this)));

			// tales
			_ = Find.TaleManager.AllTalesListForReading.RemoveAll(tale =>
			{
				var singlePawnTale = tale as Tale_SinglePawn;
				if (singlePawnTale?.pawnData?.pawn == this) return true;
				var doublePawnTale = tale as Tale_DoublePawn;
				if (doublePawnTale?.firstPawnData?.pawn == this) return true;
				if (doublePawnTale?.secondPawnData?.pawn == this) return true;
				return false;
			});

			// worldpawns
			var worldPawns = Find.WorldPawns;
			if (worldPawns.Contains(this))
				worldPawns.RemovePawn(this);

			// graphics
			var head = Drawer.renderer.graphics.headGraphic as VariableGraphic;
			head?.Dispose();
			Drawer.renderer.graphics.headGraphic = null;
			var naked = Drawer.renderer.graphics.nakedGraphic as VariableGraphic;
			naked?.Dispose();
			Drawer.renderer.graphics.nakedGraphic = null;
		}

		public override void Kill(DamageInfo? dinfo, Hediff exactCulprit = null)
		{
			if (IsSuicideBomber)
			{
				bombTickingInterval = -1f;
				bombWillGoOff = false;
				hasTankyShield = -1f;

				var def = ThingDef.Named("Apparel_BombVest");
				_ = Drawer.renderer.graphics.apparelGraphics.RemoveAll(record => record.sourceApparel?.def == def);

				Map.GetComponent<TickManager>()?.AddExplosion(Position);
			}

			if (isToxicSplasher)
				DropStickyGoo();

			base.Kill(dinfo, exactCulprit);
		}

		public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
		{
			_ = Map.GetComponent<TickManager>()?.hummingZombies.Remove(this);

			var map = Map;
			if (map == null) return;

			var grid = map.GetGrid();
			grid.ChangeZombieCount(lastGotoPosition, -1);
			base.DeSpawn(mode);
		}

		public float RopingFactorTo(Pawn pawn)
		{
			var delta = pawn.DrawPos - DrawPos;
			return (delta.x * delta.x + delta.z * delta.z) / Constants.MAX_ROPING_DISTANCE_SQUARED;
		}

		void DropStickyGoo()
		{
			var pos = Position;
			var map = Map;
			if (map == null) return;

			var amount = 1 + (int)(ZombieLand.Tools.Difficulty() + 0.5f);
			if (story.bodyType == BodyTypeDefOf.Thin)
				amount -= 1;
			if (story.bodyType == BodyTypeDefOf.Fat)
				amount += 1;
			if (story.bodyType == BodyTypeDefOf.Hulk)
				amount += 2;

			var maxRadius = 0f;
			var count = (int)GenMath.LerpDouble(0, 10, 2, 30, amount);
			var hasFilth = 0;

			for (var i = 0; i < count; i++)
			{
				var n = (int)GenMath.LerpDouble(0, 10, 1, 4, amount);
				var vec = new IntVec3(Rand.Range(-n, n), 0, Rand.Range(-n, n));
				var r = vec.LengthHorizontalSquared;
				if (r > maxRadius) maxRadius = r;
				var cell = pos + vec;
				if (GenSight.LineOfSight(pos, cell, map, true, null, 0, 0) && cell.Walkable(map))
					if (FilthMaker.TryMakeFilth(cell, map, ThingDef.Named("StickyGoo"), Name.ToStringShort, 1))
						hasFilth++;
			}
			if (hasFilth >= 6)
			{
				GenExplosion.DoExplosion(pos, map, Mathf.Max(0.5f, Mathf.Sqrt(maxRadius) - 1), CustomDefs.ToxicSplatter, null, 0, 0);
				if (Constants.USE_SOUND && Prefs.VolumeAmbient > 0f)
					CustomDefs.ToxicSplash.PlayOneShot(SoundInfo.InMap(new TargetInfo(pos, map)));
			}
		}

		public void ElectrifyAnimation()
		{
			electricCounter = 1;
			absorbAttack.Add(new KeyValuePair<float, int>(0f, -2));
			absorbAttack.Add(new KeyValuePair<float, int>(0f, -2));
			absorbAttack.Add(new KeyValuePair<float, int>(0f, -2));
			absorbAttack.Add(new KeyValuePair<float, int>(0f, -2));
		}

		void HandleRubble()
		{
			if (rubbleCounter == 0 && Constants.USE_SOUND && Prefs.VolumeAmbient > 0f)
			{
				var map = Map;
				if (map != null)
				{
					var info = SoundInfo.InMap(new TargetInfo(Position, map));
					CustomDefs.ZombieDigOut.PlayOneShot(info);
				}
			}

			if (rubbleCounter == Constants.RUBBLE_AMOUNT)
			{
				state = ZombieState.Wandering;
				rubbles = new List<Rubble>();
			}
			else if (rubbleCounter < Constants.RUBBLE_AMOUNT && rubbleTicks-- < 0)
			{
				var idx = Rand.Range(rubbleCounter * 4 / 5, rubbleCounter);
				rubbles.Insert(idx, Rubble.Create(rubbleCounter / (float)Constants.RUBBLE_AMOUNT));

				var deltaTicks = Constants.MIN_DELTA_TICKS + (float)(Constants.MAX_DELTA_TICKS - Constants.MIN_DELTA_TICKS) / Math.Min(1, rubbleCounter * 2 - Constants.RUBBLE_AMOUNT);
				rubbleTicks = (int)deltaTicks;

				rubbleCounter++;
			}

			foreach (var r in rubbles)
			{
				var dx = Mathf.Sign(r.pX) / 2f - r.pX;
				r.pX += (r.destX - r.pX) * 0.5f;
				var dy = r.destY - r.pY;
				r.pY += dy * 0.5f + Mathf.Abs(0.5f - dx) / 10f;
				r.rot = r.rot * 0.95f - (r.destX - r.pX) / 2f;

				if (dy < 0.1f)
				{
					r.dropSpeed += 0.01f;
					if (r.drop < 0.3f) r.drop += r.dropSpeed;
				}
			}
		}

		void RenderRubble(Vector3 drawLoc)
		{
			foreach (var r in rubbles)
			{
				var scale = Constants.MIN_SCALE + (Constants.MAX_SCALE - Constants.MIN_SCALE) * r.scale;
				var x = 0f + r.pX / 2f;
				var bottomExtend = Mathf.Abs(r.pX) / 6f;
				var y = -0.5f + Mathf.Max(bottomExtend, r.pY - r.drop) * (Constants.MAX_HEIGHT - scale / 2f) + (scale - Constants.MAX_SCALE) / 2f;
				var pos = drawLoc + new Vector3(x, 0, y);
				pos.y = Altitudes.AltitudeFor(AltitudeLayer.Pawn + 1);
				var rot = Quaternion.Euler(0f, r.rot * 360f, 0f);
				GraphicToolbox.DrawScaledMesh(MeshPool.plane10, Constants.RUBBLE, pos, rot, scale, scale);
			}
		}

		static readonly Color[] severity = new[]
		{
			new Color(0.9f, 0, 0),
			new Color(1f, 0.5f, 0),
			new Color(1f, 1f, 0),
		};
		public override void DrawGUIOverlay()
		{
			base.DrawGUIOverlay();
			var width = 60;

			if (UI.MapToUIPosition(Vector3.one).x - UI.MapToUIPosition(Vector3.zero).x < width / 2)
				return;

			var pos = DrawPos;
			if ((UI.MouseMapPosition() - pos).MagnitudeHorizontalSquared() > 0.64f)
				return;

			pos.z -= 0.65f;
			Vector2 vec = Find.Camera.WorldToScreenPoint(pos) / Prefs.UIScale;
			vec.y = UI.screenHeight - vec.y;

			var barRect = new Rect(vec - new Vector2(width / 2, 0), new Vector2(width, width / 5));
			Widgets.DrawBoxSolid(barRect, Constants.healthBarBG);
			var barInnerRect = barRect;
			var percent = health.summaryHealth.SummaryHealthPercent;
			barInnerRect.width *= percent;
			Widgets.DrawBoxSolid(barInnerRect, new Color(1 - percent, 0, percent));
			Widgets.DrawBox(barRect, 1, Constants.healthBarFrame);

			int num = HealthUtility.TicksUntilDeathDueToBloodLoss(this);
			if (num < 60000)
			{
				var text = "TimeToDeath".Translate(num.ToStringTicksToPeriod(true, true, true, true));
				var color = num <= GenDate.TicksPerHour ? severity[0] : (num < GenDate.TicksPerHour * 4 ? severity[1] : severity[2]);

				Text.Font = GameFont.Tiny;
				var textWidth = Text.CalcSize(text).x;
				vec.y -= 16;
				GUI.DrawTexture(new Rect(vec.x - textWidth / 2f - 4f, vec.y, textWidth + 8f, 12f), TexUI.GrayTextBG);
				GUI.color = color;
				Text.Anchor = TextAnchor.UpperCenter;
				Widgets.Label(new Rect(vec.x - textWidth / 2f, vec.y - 3f, textWidth, 999f), text.RawText);
				GUI.color = Color.white;
				Text.Anchor = TextAnchor.UpperLeft;
				Text.Font = GameFont.Small;
			}
		}

		readonly int[] nextNthTick = new int[totalNthTicks];
		public bool EveryNTick(NthTick interval)
		{
			var n = (int)interval;
			var t = GenTicks.TicksAbs;
			if (t > nextNthTick[n])
			{
				var d = nthTickValues[n];
				nextNthTick[n] = t + d;
				return true;
			}
			return false;
		}

		public override void Tick()
		{
			var comps = AllComps;
			for (var i = 0; i < comps.Count; i++)
				comps[i].CompTick();
		}

		static DamageInfo damageInfo = new DamageInfo(DamageDefOf.Crush, 20f, 20f, -1f, null, null, null, DamageInfo.SourceCategory.ThingOrUnknown, null);
		public void CustomTick(float threatLevel)
		{
			var map = Map;
			if (!ThingOwnerUtility.ContentsSuspended(ParentHolder) && map != null)
			{
				if (Spawned)
				{
					pather?.PatherTick();
					jobs?.JobTrackerTick();
					stances?.StanceTrackerTick();
					verbTracker?.VerbsTick();
					natives?.NativeVerbsTick();
					Drawer?.DrawTrackerTick();
					rotationTracker?.RotationTrackerTick();
					health?.HealthTick();
				}
			}

			if (state == ZombieState.Emerging)
				HandleRubble();

			if (threatLevel <= 0.002f && ZombieSettings.Values.zombiesDieOnZeroThreat && Rand.Chance(0.002f))
				_ = TakeDamage(damageInfo);

			if (isHealer && state != ZombieState.Emerging && EveryNTick(NthTick.Every15))
			{
				var radius = 4 + ZombieLand.Tools.Difficulty() * 2;
				GenRadial.RadialDistinctThingsAround(Position, map, radius, false)
					.OfType<Zombie>()
					.Where(zombie => zombie.health.hediffSet.hediffs.Any())
					.Do(zombie =>
					{
						zombie.health.hediffSet.Clear();
						healInfo.Add(new HealerInfo(zombie));
						map.debugDrawer.debugLines.Add(new DebugLine(DrawPos, zombie.DrawPos, 60, SimpleColor.Cyan));
					});
			}
		}

		public static Quaternion ZombieAngleAxis(float angle, Vector3 axis, Pawn pawn)
		{
			var result = Quaternion.AngleAxis(angle, axis);

			if (!(pawn is Zombie zombie))
				return result;

			var progress = zombie.rubbleCounter / (float)Constants.RUBBLE_AMOUNT;
			if (progress >= Constants.EMERGE_DELAY)
			{
				var bodyRot = GenMath.LerpDouble(Constants.EMERGE_DELAY, 1, 90, 0, progress);
				result *= Quaternion.Euler(Vector3.right * bodyRot);
			}
			return result;
		}

		public void Render(PawnRenderer renderer, Vector3 drawLoc)
		{
			drawLoc.x = (int)(drawLoc.x) + 0.5f;

			var progress = rubbleCounter / (float)Constants.RUBBLE_AMOUNT;
			if (progress >= Constants.EMERGE_DELAY)
			{
				var bodyOffset = GenMath.LerpDouble(Constants.EMERGE_DELAY, 1, -0.45f, 0, progress);
				var flags = PawnRenderFlags.DrawNow; // TODO: what flags to use and is RenderPawnInternal actually correct usage here?
				renderer.RenderPawnInternal(drawLoc + new Vector3(0, 0, bodyOffset), 0f, true, Rot4.South, renderer.CurRotDrawMode, flags);
			}

			RenderRubble(drawLoc);
		}
	}
}
