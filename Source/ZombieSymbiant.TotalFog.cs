using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using RimWorld;
using Verse;

namespace ZombieLand
{
	public partial class ZombieSymbiant
	{
		SightMesh selectionCoreSightMesh;

		// Draw-only visibility. Logical cells, host state and gameplay keep their
		// existing owners. Include the animated contour beyond logical cells.
		internal bool HasVisibleRenderPart()
		{
			var map = MapHeld;
			if (map == null)
				return false;
			var view = RenderViewRect(map);
			// The selection core can travel between logical cells independently.
			var center = SelectionCoreVisualCenter;
			var coreBounds = CellRect.FromLimits(Mathf.FloorToInt(Position.x + 0.5f + center.x - 0.8f),
				Mathf.FloorToInt(Position.z + 0.5f + center.y - 0.8f),
				Mathf.FloorToInt(Position.x + 0.5f + center.x + 0.8f),
				Mathf.FloorToInt(Position.z + 0.5f + center.y + 0.8f));
			if (HasShown(coreBounds))
				return true;
			// A sparse body can enclose a large empty rectangle. Probe only the
			// visible field around real cells and active motion centers. The shader
			// sums power * radius^4 / distance^4 and has zero alpha below 0.45.
			// Bounding every contributor by the maximum radius gives a conservative
			// influence distance, including dense clusters and incoming/outgoing cells.
			var elements = orderedCells.Count + (cellMotions?.Count ?? 0);
			// Bilinear sampling can spread an opaque texel beyond the analytic field.
			// Include a world-space texel even when a large patch hits the size cap.
			var texelPadding = 0f;
			var footprint = renderPatches.Count == 0 ? default : PatchBounds(renderPatches[0]);
			foreach (var patch in renderPatches)
			{
				texelPadding = Mathf.Max(texelPadding, Mathf.Max(patch.renderWidth / DesiredMetaballTextureSize(patch.renderWidth),
					patch.renderHeight / DesiredMetaballTextureSize(patch.renderHeight)));
				footprint = footprint.Encapsulate(PatchBounds(patch));
			}
			var radius = Mathf.CeilToInt(MetaballCellRadiusMax * Mathf.Pow(elements * Mathf.Max(0f, power) / 0.44f, 0.25f) + texelPadding);
			// Reject the whole envelope once, including after resource release.
			// RenderCellBounds includes both endpoints of incoming/outgoing motions;
			// their interpolated centers and the analytic contour stay inside it.
			if (renderPatches.Count == 0)
				footprint = RenderCellBounds().MovedBy(Position).ExpandedBy(radius);
			if (HasShown(footprint) == false)
				return false;
			// A visible empty gap inside a sparse envelope is not enough to draw.
			var hasShownPatch = renderPatches.Count == 0;
			foreach (var patch in renderPatches)
				if (HasShown(PatchBounds(patch)))
				{
					hasShownPatch = true;
					break;
				}
			if (hasShownPatch == false)
				return false;
			foreach (var relative in orderedCells)
			{
				renderPatchByCell.TryGetValue(relative, out var patch);
				if (HasShown(CellRect.SingleCell(Position + relative).ExpandedBy(radius), patch))
					return true;
			}
			var ticks = GenTicks.TicksGame;
			if (cellMotions != null)
				foreach (var motion in cellMotions)
					if (ticks < motion.endTick)
					{
						var point = motion.CurrentCenter(ticks);
						var cell = new IntVec3(Mathf.FloorToInt(Position.x + 0.5f + point.x), 0,
							Mathf.FloorToInt(Position.z + 0.5f + point.y));
						renderPatchByMotion.TryGetValue(motion, out var patch);
						if (HasShown(CellRect.SingleCell(cell).ExpandedBy(radius), patch))
							return true;
					}
			return false;

			CellRect PatchBounds(MetaballRenderPatch patch) => CellRect.FromLimits(
				Mathf.FloorToInt(Position.x + 0.5f + patch.renderMinX), Mathf.FloorToInt(Position.z + 0.5f + patch.renderMinZ),
				Mathf.CeilToInt(Position.x + 0.5f + patch.renderMinX + patch.renderWidth) - 1,
				Mathf.CeilToInt(Position.z + 0.5f + patch.renderMinZ + patch.renderHeight) - 1);

			bool HasShown(CellRect bounds, MetaballRenderPatch patch = null)
			{
				var minX = Mathf.Max(bounds.minX, view.minX);
				var minZ = Mathf.Max(bounds.minZ, view.minZ);
				var maxX = Mathf.Min(bounds.maxX, view.maxX);
				var maxZ = Mathf.Min(bounds.maxZ, view.maxZ);
				if (patch != null)
				{
					var footprint = PatchBounds(patch);
					minX = Mathf.Max(minX, footprint.minX);
					minZ = Mathf.Max(minZ, footprint.minZ);
					maxX = Mathf.Min(maxX, footprint.maxX);
					maxZ = Mathf.Min(maxZ, footprint.maxZ);
				}
				if (minX > maxX || minZ > maxZ)
					return false;
				foreach (var cell in CellRect.FromLimits(minX, minZ, maxX, maxZ))
					if (TotalFogSupport.IsVisible(map, cell))
						return true;
				return false;
			}
		}

		static CellRect RenderViewRect(Map map)
		{
			var rect = Find.CameraDriver.CurrentViewRect.ExpandedBy(1);
			if (WorldComponent_GravshipController.GravshipRenderInProgess)
				rect = rect.Encapsulate(WorldComponent_GravshipController.GravshipRenderBounds);
			rect.ClipInsideMap(map);
			return rect;
		}

		// Clip the existing quad to visible cell runs. Keep its mask UVs and
		// world-space shading. No shader/texture capture or simulation snapshot.
		sealed class SightMesh
		{
			struct Vertex(Vector2 point, Vector2 uv)
			{
				internal Vector2 point = point;
				internal Vector2 uv = uv;
			}

			readonly List<Vector3> vertices = [];
			readonly List<Vector2> uvs = [];
			readonly List<int> triangles = [];
			readonly Vertex[] quad = new Vertex[4];
			readonly Vertex[] scratchA = new Vertex[16];
			readonly Vertex[] scratchB = new Vertex[16];
			bool[] mask = [];
			Mesh mesh;
			Vector3 previousPosition;
			Vector2 previousSize;
			float previousAngle;
			int previousMinX, previousMinZ, previousWidth, previousHeight;
			bool prepared;
			internal bool CurrentOriginal { get; private set; }

			internal Mesh Prepare(Map map, Mesh originalMesh, Vector3 position, Vector2 size, float angle, out bool original)
			{
				original = false;
				CurrentOriginal = false;
				var half = size * 0.5f;
				var radians = angle * Mathf.Deg2Rad;
				var cos = Mathf.Cos(radians);
				var sin = Mathf.Sin(radians);
				quad[0] = MakeVertex(-half.x, -half.y, 0f, 0f);
				quad[1] = MakeVertex(-half.x, half.y, 0f, 1f);
				quad[2] = MakeVertex(half.x, half.y, 1f, 1f);
				quad[3] = MakeVertex(half.x, -half.y, 1f, 0f);
				var min = quad[0].point;
				var max = min;
				for (var i = 1; i < 4; i++)
				{
					min = Vector2.Min(min, quad[i].point);
					max = Vector2.Max(max, quad[i].point);
				}
				var view = RenderViewRect(map);
				var minX = Mathf.Max(Mathf.FloorToInt(min.x), view.minX);
				var minZ = Mathf.Max(Mathf.FloorToInt(min.y), view.minZ);
				var maxX = Mathf.Min(Mathf.CeilToInt(max.x) - 1, view.maxX);
				var maxZ = Mathf.Min(Mathf.CeilToInt(max.y) - 1, view.maxZ);
				var width = Mathf.Max(0, maxX - minX + 1);
				var height = Mathf.Max(0, maxZ - minZ + 1);
				var dirty = prepared == false || previousPosition != position || previousSize != size || previousAngle != angle
					|| previousMinX != minX || previousMinZ != minZ || previousWidth != width || previousHeight != height;
				var count = width * height;
				if (mask.Length < count)
				{
					mask = new bool[Mathf.NextPowerOfTwo(count)];
					dirty = true;
				}
				var visible = 0;
				var index = 0;
				for (var z = 0; z < height; z++)
					for (var x = 0; x < width; x++, index++)
					{
						var shown = TotalFogSupport.IsVisible(map, new IntVec3(minX + x, 0, minZ + z));
						dirty |= shown != mask[index];
						mask[index] = shown;
						if (shown)
							visible++;
					}
				previousPosition = position;
				previousSize = size;
				previousAngle = angle;
				previousMinX = minX;
				previousMinZ = minZ;
				previousWidth = width;
				previousHeight = height;
				prepared = true;
				if (visible == 0)
					return null;
				if (visible == count && minX == Mathf.FloorToInt(min.x) && minZ == Mathf.FloorToInt(min.y)
					&& maxX == Mathf.CeilToInt(max.x) - 1 && maxZ == Mathf.CeilToInt(max.y) - 1)
				{
					original = true;
					CurrentOriginal = true;
					return originalMesh;
				}
				if (dirty == false)
					return mesh;

				vertices.Clear();
				uvs.Clear();
				triangles.Clear();
				for (var z = 0; z < height; z++)
				{
					var x = 0;
					while (x < width)
					{
						if (mask[z * width + x] == false)
						{
							x++;
							continue;
						}
						var first = x;
						while (x < width && mask[z * width + x])
							x++;
						EmitRun(minX + first, minX + x, minZ + z, minZ + z + 1, position);
					}
				}
				if (mesh == null)
				{
					mesh = new Mesh { name = "ZombieSymbiantCurrentSight", indexFormat = IndexFormat.UInt32 };
					mesh.MarkDynamic();
				}
				mesh.Clear();
				mesh.SetVertices(vertices);
				mesh.SetUVs(0, uvs);
				mesh.SetTriangles(triangles, 0);
				mesh.RecalculateNormals();
				mesh.RecalculateBounds();
				return mesh;

				Vertex MakeVertex(float x, float z, float u, float v) =>
					new(new Vector2(position.x + cos * x + sin * z, position.z - sin * x + cos * z), new Vector2(u, v));
			}

			void EmitRun(float minX, float maxX, float minZ, float maxZ, Vector3 position)
			{
				System.Array.Copy(quad, scratchA, 4);
				var count = Clip(scratchA, 4, scratchB, true, minX, true);
				count = Clip(scratchB, count, scratchA, true, maxX, false);
				count = Clip(scratchA, count, scratchB, false, minZ, true);
				count = Clip(scratchB, count, scratchA, false, maxZ, false);
				if (count < 3)
					return;
				var first = vertices.Count;
				for (var i = 0; i < count; i++)
				{
					var vertex = scratchA[i];
					vertices.Add(new Vector3(vertex.point.x - position.x, 0f, vertex.point.y - position.z));
					uvs.Add(vertex.uv);
				}
				for (var i = 1; i < count - 1; i++)
				{
					triangles.Add(first);
					triangles.Add(first + i);
					triangles.Add(first + i + 1);
				}
			}

			static int Clip(Vertex[] input, int count, Vertex[] output, bool xAxis, float edge, bool minimum)
			{
				if (count == 0)
					return 0;
				var written = 0;
				var previous = input[count - 1];
				var previousCoordinate = xAxis ? previous.point.x : previous.point.y;
				var previousInside = minimum ? previousCoordinate >= edge : previousCoordinate <= edge;
				for (var i = 0; i < count; i++)
				{
					var current = input[i];
					var coordinate = xAxis ? current.point.x : current.point.y;
					var inside = minimum ? coordinate >= edge : coordinate <= edge;
					if (inside != previousInside)
					{
						var fraction = (edge - previousCoordinate) / (coordinate - previousCoordinate);
						var point = Vector2.LerpUnclamped(previous.point, current.point, fraction);
						if (xAxis) point.x = edge;
						else point.y = edge;
						output[written++] = new Vertex(point,
							Vector2.LerpUnclamped(previous.uv, current.uv, fraction));
					}
					if (inside)
						output[written++] = current;
					previous = current;
					previousCoordinate = coordinate;
					previousInside = inside;
				}
				return written;
			}

			internal void Dispose()
			{
				if (mesh != null)
					Object.Destroy(mesh);
				mesh = null;
				prepared = false;
			}
		}
	}
}
