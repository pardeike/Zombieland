using UnityEngine;
using UnityEngine.Tilemaps;

namespace UnityEditor.Tilemaps.Tests
{
    internal class StartUpTile : TileBase
    {
        public static int startUpCalls = 0;

        public override void GetTileData(Vector3Int location, ITilemap tilemap, ref TileData tileData)
        {
        }

        public override bool StartUp(Vector3Int location, ITilemap tilemap, GameObject go)
        {
            startUpCalls++;
            return true;
        }
    }
}
