using System;
using System.Linq;
using System.Text;
using Verse;

namespace ZombieLand
{
	public static class ContaminationSerializer
	{
		public static void ExposeContamination(this ContaminationManager manager)
		{
			if (Scribe.EnterNode("contaminations") == false)
				return;

			manager.contaminations ??= new();
			try
			{
				if (Constants.CONTAMINATION > 0)
				{
					if (Scribe.mode == LoadSaveMode.Saving)
					{
						var thingIDs = Find.Maps
							.SelectMany(map => map.listerThings.AllThings.Where(thing => thing.Destroyed == false))
							.Select(thing => thing.thingIDNumber)
							.ToHashSet();
						var pairs = manager.contaminations
							.Where(pair => thingIDs.Contains(pair.Key))
							.OrderBy(pair => pair.Key);
						foreach (var pair in pairs)
						{
							Scribe.saver.writer.WriteStartElement("T" + pair.Key);
							Scribe.saver.writer.WriteString($"{pair.Value:R}");
							Scribe.saver.writer.WriteEndElement();
						}
					}
					else if (Scribe.mode == LoadSaveMode.LoadingVars)
					{
						var curXmlParent = Scribe.loader.curXmlParent.ChildNodes;
						for (var i = 0; i < curXmlParent.Count; i++)
						{
							var subNode = curXmlParent.Item(i);
							var id = int.Parse(subNode.Name.Substring(1));
							var val = float.Parse(subNode.InnerText);
							manager.contaminations[id] = val;
						}
					}
				}
			}
			finally
			{
				Scribe.ExitNode();
			}
		}

		public static void ExposeGrounds(this ContaminationManager manager)
		{
			if (Scribe.EnterNode("grounds") == false)
				return;

			if (Scribe.mode == LoadSaveMode.Saving)
				Scribe.saver.WriteAttribute("version", "2");

			var version = 1;
			if (Scribe.mode == LoadSaveMode.LoadingVars)
			{
				var xmlAttribute = Scribe.loader.curXmlParent.Attributes["version"];
				if (xmlAttribute != null)
					version = int.Parse(xmlAttribute.Value);
			}

			manager.grounds ??= new();

			try
			{
				if (Constants.CONTAMINATION > 0)
				{
					if (Scribe.mode == LoadSaveMode.Saving)
					{
						for (var idx = 0; idx < manager.grounds.Keys.Count; idx++)
						{
							var values = manager.grounds[idx].cells;
							var sb = new StringBuilder(values.Length * 10);
							for (var i = 0; i < values.Length; i++)
							{
								var f = values[i];
								var n = f == 0 ? 0 : (int)(f * 10000 + 0.9f);
								sb.Append(n);
								sb.Append(',');
							}
							Scribe.saver.writer.WriteStartElement("map");
							Scribe.saver.writer.WriteAttributeString("size", values.Length.ToString());
							Scribe.saver.writer.WriteString(sb.ToString());
							Scribe.saver.writer.WriteEndElement();
						}
					}
					else if (Scribe.mode == LoadSaveMode.LoadingVars)
					{
						var ChildNodes = Scribe.loader.curXmlParent.ChildNodes;
						var divideFactor = version == 1 ? 100f : 10000f;
						for (var i = 0; i < ChildNodes.Count; i++)
						{
							var subNode = ChildNodes.Item(i);
							var size = int.Parse(subNode.Attributes["size"].Value);
							var floats = new float[size];
							var txt = subNode.InnerText;
							var f = 0;
							var k = 0;
							for (var j = 0; j < txt.Length; j++)
							{
								if (txt[j] == ',')
								{
									floats[f++] = int.Parse(txt.Substring(k, j - k)) / divideFactor;
									k = j + 1;
								}
							}
							manager.grounds[i] = new ContaminationGrid(floats);
						}
					}
				}
			}
			finally
			{
				Scribe.ExitNode();
			}
		}

		public static void FixGrounds(this ContaminationManager manager)
		{
			foreach (var map in Find.Maps)
			{
				var idx = map.Index;
				if (manager.grounds.TryGetValue(idx, out var grounds))
					grounds.AddMap(map);
				else
					manager.grounds[idx] = new ContaminationGrid(map);
			}
		}
	}
}