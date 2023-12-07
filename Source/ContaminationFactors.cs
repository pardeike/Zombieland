using HarmonyLib;
using RimWorld;
using System;
using System.Reflection;
using Verse;

namespace ZombieLand
{
	public class ContaminationFactors : IExposable
	{
		public const float minContaminationThreshold = 0.0001f;

		[ValueRange(0f, 1f)] public float contaminationElevationPercentage = 0.5f;
		[ValueRange(GenDate.TicksPerYear / 4, GenDate.TicksPerYear * 2)] public float decontaminationQuestInterval = GenDate.TicksPerYear / 2f;
		[ValueRange(0f, 1f)] public float contaminationEffectivenessPercentage = 0.5f;

		[ValueRange(1f, 1f)] public float ambrosiaAdd = 1f;
		[ValueRange(0.3f, 1f)] public float constructionAdd = 0.5f;
		[ValueRange(0.3f, 0.9f)] public float deepDrillAdd = 0.5f;
		[ValueRange(1f, 1f)] public float destroyMineableAdd = 1f;
		[ValueRange(0.002f, 0.1f)] public float floorAdd = 0.01f;
		[ValueRange(0.3f, 1f)] public float jellyAdd = 0.5f;
		[ValueRange(0.5f, 1f)] public float meteoriteAdd = 1f;
		[ValueRange(0.1f, 1f)] public float plantAdd = 0.5f;
		[ValueRange(0.02f, 0.5f)] public float pollutionAdd = 0.05f;
		[ValueRange(0.2f, 1f)] public float shipChunkAdd = 0.25f;
		[ValueRange(0.002f, 0.3f)] public float snowAdd = 0.1f;
		[ValueRange(0.1f, 0.5f)] public float sowedPlantAdd = 0.2f;
		[ValueRange(0.25f, 1f)] public float wastePackAdd = 0.5f;
		[ValueRange(0.004f, 0.2f)] public float zombieDeathAdd = 0.01f;
		[ValueRange(0.004f, 0.04f)] public float enterCellAdd = 0.01f;

		[ValueRange(0.001f, 0.05f)] public float constructionTransfer = 0.01f;
		[ValueRange(0.02f, 0.75f)] public float disassembleTransfer = 0.1f;
		[ValueRange(0.02f, 0.75f)] public float dispenseFoodTransfer = 0.1f;
		[ValueRange(0.02f, 0.75f)] public float fermentingBarrelTransfer = 0.1f;
		[ValueRange(0.002f, 0.2f)] public float filthTransfer = 0.01f;
		[ValueRange(0.075f, 0.5f)] public float geneAssemblerTransfer = 0.2f;
		[ValueRange(0.075f, 0.5f)] public float geneExtractorTransfer = 0.2f;
		[ValueRange(0.02f, 0.75f)] public float generalTransfer = 0.1f;
		[ValueRange(0.2f, 4f)] public float ingestTransfer = 1f;
		[ValueRange(0.25f, 1f)] public float leavingsTransfer = 0.5f;
		[ValueRange(0.75f, 1f)] public float medicineTransfer = 0.9f;
		[ValueRange(0.25f, 1f)] public float plantTransfer = 0.5f;
		[ValueRange(0.25f, 1f)] public float receipeTransfer = 0.5f;
		[ValueRange(0.002f, 0.2f)] public float repairTransfer = 0.01f;
		[ValueRange(1f, 1f)] public float stumpTransfer = 1f;
		[ValueRange(0.075f, 0.5f)] public float subcoreScannerTransfer = 0.2f;
		[ValueRange(0.01f, 0.5f)] public float workerTransfer = 0.02f;

		[ValueRange(0.0075f, 0.5f)] public float benchEqualize = 0.02f;
		[ValueRange(0.02f, 0.75f)] public float bloodEqualize = 0.2f;
		[ValueRange(0.02f, 0.35f)] public float carryEqualize = 0.1f;
		[ValueRange(0.0015f, 0.2f)] public float filthEqualize = 0.01f;
		[ValueRange(0.02f, 0.75f)] public float meleeEqualize = 0.1f;
		[ValueRange(0.015f, 0.65f)] public float produceEqualize = 0.1f;
		[ValueRange(0.0005f, 0.02f)] public float restEqualize = 0.001f;
		[ValueRange(0.02f, 0.75f)] public float sowingPawnEqualize = 0.1f;
		[ValueRange(1f, 1f)] public float tendEqualizeWorst = 1f;
		[ValueRange(0f, 0f)] public float tendEqualizeBest = 0f;

		[ValueRange(0.01f, 0.5f)] public float fireReduction = 0.05f;
		[ValueRange(0.01f, 0.9f)] public float randomThingCreateChance = 0.1f;
		[ValueRange(0f, 1f)] public float randomThingDensityDistribution = 0.5f;
		[ValueRange(1f, 1f)] public float mechClusterChance = 1f;
		[ValueRange(0f, 1f)] public float mechClusterDensityDistribution = 0.5f;

		[ValueRange(1f, 1.1f)] public float cellFactor = 1.05f;
		[ValueRange(0.00005f, 0.002f)] public float enterCellGain = 0.0001f;
		[ValueRange(0.000005f, 0.0002f)] public float enterCellLoose = 0.00001f;
		[ValueRange(0.0015f, 0.2f)] public float filthGain = 0.01f;

		private static readonly ContaminationFactors defaults = new();
		public static void ApplyBaseFactor(ContaminationFactors factors, float baseFactor)
		{
			AccessTools.GetDeclaredFields(typeof(ContaminationFactors))
				.Do(field =>
				{
					var range = field.GetCustomAttribute<ValueRangeAttribute>();
					if (range != null)
					{
						var value = range.GetScaledValue(baseFactor);
						field.SetValue(factors, value);
					}
				});
		}

		// actually not really needed but we keep it for compatibility with old saves
		public void ExposeData()
		{
			AccessTools.GetDeclaredFields(typeof(ContaminationFactors)).Do(field =>
			{
				var range = field.GetCustomAttribute<ValueRangeAttribute>();
				if (range != null)
				{
					var name = field.Name;
					var value = (float)field.GetValue(this);
					Scribe_Values.Look(ref value, name, (float)field.GetValue(defaults));
					field.SetValue(this, value);
				}
			});
		}
	}

	[AttributeUsage(AttributeTargets.Field)]
	class ValueRangeAttribute : Attribute
	{
		public float minValue;
		public float maxValue;

		public ValueRangeAttribute(float minValue, float maxValue)
		{
			this.minValue = minValue;
			this.maxValue = maxValue;
		}

		public float GetScaledValue(float difficulty) => GenMath.LerpDouble(0f, 5f, minValue, maxValue, difficulty);
	}
}
