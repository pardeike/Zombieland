using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using Verse;

namespace ZombieLand
{
	[HarmonyPatch]
	static class RimConnectAPI_PostValidCommands_Patch
	{
		static readonly string configDefault = @"
			<ZombieCommands>
				<Command type='Albino'>
					<name>Zombies (albino)</name>
					<costs>20</costs>
				</Command>
				<Command type='DarkSlimer'>
					<name>Zombies (dark slimer)</name>
					<costs>15</costs>
				</Command>
				<Command type='Electrifier'>
					<name>Zombies (electrifier)</name>
					<costs>10</costs>
				</Command>
				<Command type='Miner'>
					<name>Zombies (miner)</name>
					<costs>5</costs>
				</Command>
				<Command type='Normal'>
					<name>Zombies (normal)</name>
					<costs>2</costs>
				</Command>
				<Command type='Random'>
					<name>Zombies (random)</name>
					<costs>25</costs>
				</Command>
				<Command type='SuicideBomber'>
					<name>Zombies (bomber)</name>
					<costs>20</costs>
				</Command>
				<Command type='TankyOperator'>
					<name>Zombies (tanky)</name>
					<costs>20</costs>
				</Command>
				<Command type='ToxicSplasher'>
					<name>Zombies (toxic)</name>
					<costs>20</costs>
				</Command>
			</ZombieCommands>".Replace('\'', '"');

		static bool Prepare()
		{
			return TargetMethod() != null;
		}

		static MethodBase TargetMethod()
		{
			var tRimConnectAPI = AccessTools.TypeByName("RimConnection.RimConnectAPI");
			if (tRimConnectAPI == null) return null;
			return AccessTools.Method(tRimConnectAPI, "PostValidCommands");
		}

		static void Prefix(object commandList)
		{
			var list = Traverse.Create(commandList).Property("validCommands").GetValue() as IList;
			var path = $"{GenFilePaths.ConfigFolderPath}{Path.DirectorySeparatorChar}ZombieLand-RimConnect.xml";
			if (File.Exists(path) == false)
				File.WriteAllText(path, configDefault);

			var contents = File.ReadAllText(path);
			var xmlReaderSettings = new XmlReaderSettings { IgnoreComments = true, IgnoreWhitespace = true, CheckCharacters = false };
			using var stringReader = new StringReader(contents);
			using XmlReader xmlReader = XmlReader.Create(stringReader, xmlReaderSettings);
			var xmlDoc = new XmlDocument();
			xmlDoc.Load(xmlReader);
			foreach (XmlNode command in xmlDoc.DocumentElement.ChildNodes)
			{
				if (Enum.TryParse<ZombieType>(command.Attributes["type"].Value, out var confType))
				{
					var confName = "";
					var confCosts = -1;
					for (var i = 0; i < command.ChildNodes.Count; i++)
					{
						var name = command.ChildNodes[i].Name;
						var value = command.ChildNodes[i].InnerText;
						switch (name)
						{
							case "name":
								confName = value;
								break;
							case "costs":
								confCosts = int.Parse(value);
								break;
						}
					}
					if (confCosts > 0 && confName != "")
						RimConnectSupport.AddCommand(list, confName, $"Creates {confName}", confCosts, (Map map, int amount, string user) =>
						{
							var success = ZombiesRising.TryExecute(map, amount, IntVec3.Invalid, true, confType);
						});
				}
			}
		}
	}

	[HarmonyPatch]
	static class RimConnectAPI_GetCommands_Patch
	{
		static bool Prepare()
		{
			return TargetMethod() != null;
		}

		static MethodBase TargetMethod()
		{
			var tRimConnectAPI = AccessTools.TypeByName("RimConnection.RimConnectAPI");
			if (tRimConnectAPI == null) return null;
			return AccessTools.Method(tRimConnectAPI, "GetCommands");
		}

		static void Postfix(IList __result)
		{
			var zlObj = new List<object>();
			foreach (var obj in __result)
			{
				var cmd = RimConnectSupport.Command.Convert(obj);
				var action = RimConnectSupport.LookupAction(cmd.actionHash);
				if (action != null)
				{
					RimConnectSupport.QueueRimConnectAction(map => action(map, cmd.amount, cmd.boughtBy));
					zlObj.Add(obj);
				}
			}
			foreach (var obj in zlObj)
				__result.Remove(obj);
		}
	}

	public class RimConnectSupport
	{
		public delegate void ZombieAction(Map map, int amount, string user);
		public static Dictionary<string, ZombieAction> zlCommands = new Dictionary<string, ZombieAction>();

		public class ValidCommand
		{
#pragma warning disable IDE1006
			public string actionHash { get; set; }
			public string name { get; set; }
			public string description { get; set; }
			public string category { get; set; }
			public string prefix { get; set; }
			public bool shouldShowAmount { get; set; }
			public int localCooldownMs { get; set; }
			public int globalCooldownMs { get; set; }
			public int costSilverStore { get; set; }
			public string bitStoreSKU { get; set; }
#pragma warning restore IDE1006

			public void UpdateActionHash()
			{
				var input = $"{name}{description}{category}{prefix}";
				var hash = new SHA1Managed().ComputeHash(Encoding.UTF8.GetBytes(input));

				var sb = new StringBuilder();
				foreach (byte b in hash) _ = sb.Append(b.ToString("X2"));

				actionHash = sb.ToString();
			}
		}

		public class Command
		{
#pragma warning disable IDE1006
			public string actionHash { get; set; }
			public int amount { get; set; }
			public string boughtBy { get; set; }
#pragma warning restore IDE1006

			public static Command Convert(object cmd)
			{
				var trv = Traverse.Create(cmd);
				var actionHash = trv.Property("actionHash").GetValue<string>();
				var amount = trv.Property("amount").GetValue<int>();
				var boughtBy = trv.Property("boughtBy").GetValue<string>();
				return new Command() { actionHash = actionHash, amount = amount, boughtBy = boughtBy };
			}
		}

		public static void AddCommand(IList list, string name, string description, int costs, ZombieAction action)
		{
			var cmd = new ValidCommand
			{
				name = name,
				description = description,
				category = "Event",
				prefix = "Spawn",
				localCooldownMs = 120000,
				globalCooldownMs = 60000,
				costSilverStore = costs,
				bitStoreSKU = ""
			};
			cmd.UpdateActionHash();

			zlCommands[cmd.actionHash] = action;

			var tValidCommand = AccessTools.TypeByName("RimConnection.ValidCommand");
			_ = list.Add(AccessTools.MakeDeepCopy(cmd, tValidCommand));
		}

		public static ZombieAction LookupAction(string actionHash)
		{
			if (zlCommands.TryGetValue(actionHash, out var action))
				return action;
			return null;
		}

		public static void QueueRimConnectAction(Action<Map> action)
		{
			var tickManager = Find.CurrentMap?.GetComponent<TickManager>();
			tickManager?.rimConnectActions.Enqueue(action);
		}
	}
}
