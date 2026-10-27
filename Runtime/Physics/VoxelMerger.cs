using UnityEngine;

namespace GsPhysicsToolkit.Physics
{
    public class VoxelMerger : MonoBehaviour
    {
        public static void GenerateCollidersFromVoxelData(uint[] results, float voxelSize, int threshold, Vector3Int gridSize, Transform parent, Vector3 minOffset)
        {
            for (int childIndex = parent.childCount - 1; childIndex >= 0; childIndex--)
                DestroyImmediate(parent.GetChild(childIndex).gameObject);
            for (int z = 0; z < gridSize.z; z++)
            {
                for (int y = 0; y < gridSize.y; y++)
                {
                    for (int x = 0; x < gridSize.x; x++)
                    {
                        int flatIdx = x + gridSize.x * (y + gridSize.y * z);
                        if (results[flatIdx] >= threshold)
                        {
                            GameObject obj = new GameObject($"Voxel_{x}_{y}_{z}");
                            obj.transform.SetParent(parent, false);
                            Vector3 pos = minOffset + (new Vector3(x, y, z) * voxelSize);
                            if (float.IsNaN(pos.x) || float.IsNaN(pos.y) || float.IsNaN(pos.z)) continue;
                            obj.transform.localPosition = pos;
                            obj.transform.localRotation = Quaternion.identity;
                            obj.transform.localScale = Vector3.one;
                            BoxCollider bc = obj.AddComponent<BoxCollider>();
                            bc.size = Vector3.one * voxelSize;
                            bc.center = Vector3.one * (voxelSize * 0.5f);
                        }
                    }
                }
            }
        }
    }
}
