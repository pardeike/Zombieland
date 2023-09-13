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

		[ValueRange(0.175f, 0.175f)] public float contaminationElevationDelta = 0.175f;
		[ValueRange(GenDate.TicksPerYear / 4, GenDate.TicksPerYear * 2)] public float decontaminationQuestInterval = GenDate.TicksPerYear / 2f;

		[ValueRange(1f, 1f)] public float ambrosiaAdd = 1f;
		[ValueRange(0.2f, 1f)] public float constructionAdd = 0.5f;
		[ValueRange(0.2f, 1f)] public float deepDrillAdd = 0.5f;
		[ValueRange(1f, 1f)] public float destroyMineableAdd = 1f;
		[ValueRange(0.001f, 0.1f)] public float floorAdd = 0.01f;
		[ValueRange(0.2f, 1f)] public float jellyAdd = 0.5f;
		[ValueRange(0.5f, 1f)] public float meteoriteAdd = 1f;
		[ValueRange(0.1f, 1f)] public float plantAdd = 0.5f;
		[ValueRange(0.01f, 0.5f)] public float pollutionAdd = 0.05f;
		[ValueRange(0.1f, 1f)] public float shipChunkAdd = 0.25f;
		[ValueRange(0.001f, 0.2f)] public float snowAdd = 0.1f;
		[ValueRange(0.05f, 0.4f)] public float sowedPlantAdd = 0.2f;
		[ValueRange(0.2f, 1f)] public float wastePackAdd = 0.5f;

		[ValueRange(0.01f, 0.5f)] public float disassembleTransfer = 0.1f;
		[ValueRange(0.01f, 0.5f)] public float dispenseFoodTransfer = 0.1f;
		[ValueRange(0.01f, 0.5f)] public float fermentingBarrelTransfer = 0.1f;
		[ValueRange(0.001f, 0.1f)] public float filthTransfer = 0.01f;
		[ValueRange(0.05f, 0.4f)] public float geneAssemblerTransfer = 0.2f;
		[ValueRange(0.05f, 0.4f)] public float geneExtractorTransfer = 0.2f;
		[ValueRange(0.01f, 0.5f)] public float generalTransfer = 0.1f;
		[ValueRange(0.1f, 1f)] public float ingestTransfer = 0.25f;
		[ValueRange(0.2f, 1f)] public float leavingsTransfer = 0.5f;
		[ValueRange(0.75f, 1f)] public float medicineTransfer = 0.9f;
		[ValueRange(0.2f, 1f)] public float plantTransfer = 0.5f;
		[ValueRange(0.2f, 1f)] public float receipeTransfer = 0.5f;
		[ValueRange(0.001f, 0.1f)] public float repairTransfer = 0.01f;
		[ValueRange(1f, 1f)] public float stumpTransfer = 1f;
		[ValueRange(0.05f, 0.4f)] public float subcoreScannerTransfer = 0.2f;
		[ValueRange(0.005f, 0.25f)] public float workerTransfer = 0.02f;

		[ValueRange(0.005f, 0.25f)] public float benchEqualize = 0.02f;
		[ValueRange(0.01f, 0.5f)] public float bloodEqualize = 0.1f;
		[ValueRange(0.0005f, 0.025f)] public float carryEqualize = 0.002f;
		[ValueRange(0.00025f, 0.0125f)] public float enterCellEqualize = 0.001f;
		[ValueRange(0.001f, 0.1f)] public float filthEqualize = 0.01f;
		[ValueRange(0.01f, 0.5f)] public float meleeEqualize = 0.1f;
		[ValueRange(0.01f, 0.5f)] public float produceEqualize = 0.1f;
		[ValueRange(0.00025f, 0.0125f)] public float restEqualize = 0.001f;
		[ValueRange(0.01f, 0.5f)] public float sowingPawnEqualize = 0.1f;
		[ValueRange(0.05f, 0.4f)] public float tendEqualizeWorst = 0.2f;
		[ValueRange(0f, 0f)] public float tendEqualizeBest = 0f;

		[ValueRange(0.01f, 0.5f)] public float fireReduction = 0.05f;
		[ValueRange(0.01f, 0.5f)] public float randomThingCreateChance = 0.1f;
		[ValueRange(0.2f, 1f)] public float randomThingDensityDistribution = 0.5f;
		[ValueRange(1f, 1f)] public float mechClusterChance = 1f;
		[ValueRange(0.2f, 1f)] public float mechClusterDensityDistribution = 0.5f;

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
