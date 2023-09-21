using HarmonyLib;
using System;
using System.Reflection;
using Verse;
using static HarmonyLib.AccessTools;

namespace ZombieLand
{
	public delegate float GetStatValue(Pawn pawn, Def stat);

	public static class VehicleTools
	{
		public static Type vehicleType;
		public static Def moveSpeedDef;
		public static MethodInfo getStatValue;

		public static void Init()
		{
			vehicleType = TypeByName("Vehicles.VehiclePawn");
			if (vehicleType == null)
				return;

			moveSpeedDef = Traverse.Create(TypeByName("Vehicles.VehicleStatDefOf")).Field("MoveSpeed").GetValue<Def>();
			getStatValue = Method(vehicleType, "GetStatValue");

			var harmony = new Harmony("net.pardeike.zombieland.vehicles");
			var method = Method("Vehicles.VehicleDamager:TryFindDirectFleeDestination");
			if (method != null)
			{
				var prefix = SymbolExtensions.GetMethodInfo((bool b) => VehicleDamager_TryFindDirectFleeDestination_Prefix(default, ref b));
				harmony.Patch(method, prefix: new HarmonyMethod(prefix));
			}
		}

		public static bool IsVehicle(this Pawn pawn)
		{
			if (vehicleType == null)
				return false;
			return vehicleType.IsAssignableFrom(pawn.GetType());
		}

		public static float GetMoveSpeed(this Pawn pawn) => (float)getStatValue.Invoke(pawn, new object[] { moveSpeedDef });

		public static bool BumpTimestamps(Pawn pawn, IntVec3 position)
		{
			if (pawn.IsVehicle() == false)
				return false;
			var now = Tools.Ticks();
			var grid = pawn.Map.GetGrid();
			var speed = pawn.GetMoveSpeed();
			Tools.GetCircle(1.5f * speed).Do(vec => grid.BumpTimestamp(position + vec, now - (long)(2f * vec.LengthHorizontal)));
			return true;
		}

		static bool VehicleDamager_TryFindDirectFleeDestination_Prefix(Pawn pawn, ref bool __result)
		{
			if (pawn is Zombie)
			{
				__result = false;
				return false;
			}
			return true;
		}
	}
}