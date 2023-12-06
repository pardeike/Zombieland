using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	public class ZombieBlobRenderer
	{
		public const int MAX_METABALLS = 64;

		public struct Metaball
		{
			public float radius;
			public float power;
			public Vector2 position;
			public Vector2 direction;
			public Vector4 color;
		}

		public static readonly (int x, int y)[] basicShape = new (int, int)[]
		{
			/* (0, 0), (0, 1), (-1, 0), (-2, 0), (-2, 1), (-2, 2), (1, 0), (2, 0), (2, 1), (2, 2), (0, -1), (0, -2) */
			(-1, -1),
			(-1, 0),
			(-1, 1),
			(0, -1),
			(0, 0),
			(0, 1),
			(1, -1),
			(1, 0),
			(1, 1),
			(2, 1)
		};

		public static readonly Color color = new Color(0, 0.8f, 0);

		public Map map;
		public ComputeBuffer metaballBuffer;
		public Material metaballMaterial;
		public List<Metaball> metaballs = new List<Metaball>();

		public ZombieBlobRenderer(Map map, List<IntVec3> cells)
		{
			this.map = map;
			metaballBuffer = new ComputeBuffer(MAX_METABALLS, Marshal.SizeOf(typeof(Metaball)));
			metaballMaterial = new Material(Assets.MetaballShader);

			var min_x = cells.Min(c => c.x) - 0.5f;
			var min_y = cells.Min(c => c.z) - 0.5f;
			var max_x = cells.Max(c => c.x) + 0.5f;
			var max_y = cells.Max(c => c.z) + 0.5f;

			cells.Do(cell =>
			{
				Add(new Vector2(cell.x, cell.z), min_x, min_y, max_x, max_y, 0.035f);
				// map.designationManager.AddDesignation(new Designation(cell, DesignationDefOf.Plan, null));
			});
		}

		~ZombieBlobRenderer()
		{
			Object.Destroy(metaballMaterial);
			metaballBuffer.Dispose();
		}

		public static List<IntVec3> ValidPosition(Map map, IntVec3 cell)
		{
			var cells = basicShape.Select(v => cell + new IntVec3(v.x, 0, v.y)).ToList();
			if (cells.Any(cell => !cell.InBounds(map) || cell.Standable(map) == false))
				return null;
			return cells;
		}

		public void Add(Vector2 cell, float min_x, float min_y, float max_x, float max_y, float radius)
		{
			var x = GenMath.LerpDouble(min_x, max_x, 0, 1, cell.x);
			var y = GenMath.LerpDouble(min_y, max_y, 0, 1, cell.y);
			metaballs.Add(new Metaball
			{
				radius = radius,
				power = 1,
				position = new Vector2(x, y),
				direction = Vector2.zero,
				color = color,
			});
			Update();
		}

		public void Remove(Vector2 position)
		{
			metaballs.RemoveAll(metaballs => metaballs.position == position);
			Update();
		}

		public void Update()
		{
			metaballBuffer.SetData(metaballs, 0, 0, metaballs.Count);
			metaballMaterial.SetBuffer("_MetaballBuffer", metaballBuffer);
		}
	}
}
