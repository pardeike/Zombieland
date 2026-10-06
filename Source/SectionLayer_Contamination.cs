using RimWorld;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	// Sight already dirties native map sections. Keep contamination meshes local
	// to those sections instead of rebuilding the whole map when sight changes.
	[StaticConstructorOnStartup]
	public class SectionLayer_Contamination : SectionLayer
	{
		static readonly MapMeshFlagDef fogFlag = DefDatabase<MapMeshFlagDef>.GetNamedSilentFail("RealFogOfWar");
		static readonly Material material = CreateMaterial();
		bool pendingRebuild = true;
		int lastDrawFrame = -2;

		public SectionLayer_Contamination(Section section) : base(section)
		{
			relevantChangeTypes = CustomDefs.ZombieContaminationOverlay | MapMeshFlagDefOf.FogOfWar;
			if (fogFlag != null)
				relevantChangeTypes |= fogFlag;
		}

		internal static bool ActiveFor(Map map) => TotalFogSupport.HasCellVisibilityFilter
			&& Constants.CONTAMINATION && ContaminationManager.CanDrawOverlayFor(map)
			&& ContaminationManager.Instance.showContaminationOverlay
			&& Find.CameraDriver.CurrentViewRect.Area >= Constants.MAX_CELLS_FOR_DETAILED_CONTAMINATION;

		internal static void SetDirty(Map map)
		{
			if (ActiveFor(map))
				map.mapDrawer.WholeMapChanged(CustomDefs.ZombieContaminationOverlay);
		}

		public override bool Visible => ActiveFor(Map);
		public override void Regenerate() => pendingRebuild = true;

		public override void DrawLayer()
		{
			if (Visible == false)
				return;
			var frame = Time.frameCount;
			if (pendingRebuild || (lastDrawFrame != frame && lastDrawFrame != frame - 1))
				Rebuild();
			lastDrawFrame = frame;
			base.DrawLayer();
		}

		void Rebuild()
		{
			ClearSubMeshes(MeshParts.All);
			var mesh = GetSubMesh(material);
			var grid = Map.GetContamination();
			var manager = ContaminationManager.Instance;
			foreach (var cell in section.CellRect)
			{
				if (TotalFogSupport.IsVisible(Map, cell) == false)
					continue;
				var index = Map.cellIndices.CellToIndex(cell);
				if (grid.GetCellBool(index))
					AddCell(mesh, cell, grid.GetCellExtraColor(index));
				if (manager.GetCellBool(Map, index))
				{
					var color = manager.GetCellExtraColor(Map, index);
					color.a *= 0.8f;
					AddCell(mesh, cell, color);
				}
			}
			mesh.disabled = mesh.verts.Count == 0;
			if (mesh.disabled == false)
				mesh.FinalizeMesh(MeshParts.All);
			pendingRebuild = false;
		}

		static void AddCell(LayerSubMesh mesh, IntVec3 cell, Color color)
		{
			var vertex = mesh.verts.Count;
			var y = AltitudeLayer.MapDataOverlay.AltitudeFor();
			mesh.verts.Add(new Vector3(cell.x, y, cell.z));
			mesh.verts.Add(new Vector3(cell.x, y, cell.z + 1));
			mesh.verts.Add(new Vector3(cell.x + 1, y, cell.z + 1));
			mesh.verts.Add(new Vector3(cell.x + 1, y, cell.z));
			for (var i = 0; i < 4; i++)
				mesh.colors.Add(color);
			mesh.tris.Add(vertex); mesh.tris.Add(vertex + 1); mesh.tris.Add(vertex + 2);
			mesh.tris.Add(vertex); mesh.tris.Add(vertex + 2); mesh.tris.Add(vertex + 3);
		}

		static Material CreateMaterial()
		{
			var result = SolidColorMaterials.NewSolidColorMaterial(Color.white, ShaderDatabase.VertexColor);
			result.renderQueue = 3640;
			return result;
		}
	}
}
