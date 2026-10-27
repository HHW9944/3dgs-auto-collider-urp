using GsPhysicsToolkit.Core;
using System.Diagnostics;
using UnityEngine;

namespace GsPhysicsToolkit.Physics
{
    public class DensityCompute : MonoBehaviour
    {
        const int ThreadGroupSize = 64;
        const int MaxThreadGroupsPerDimension = 65535;

        public float VoxelSize = 0.2f;
        public int DensityThreshold = 5;
        public ComputeShader densityShader;
        public Vector3Int GridSize = new Vector3Int(64, 64, 64);
        public Transform collidersParent;
        [HideInInspector] public Vector3 lastMinBounds;
        [HideInInspector] public bool hasLastBounds;

        public void GenerateColliders(SplatData splatData, Transform targetTransform)
        {
            Stopwatch sw = Stopwatch.StartNew();
            if (!TryRunDensity(splatData, out uint[] results, out Vector3 min))
                return;

            PrepareLocalColliderParent(targetTransform);

            VoxelMerger.GenerateCollidersFromVoxelData(results, VoxelSize, DensityThreshold, GridSize, collidersParent, min);

            int voxelCount = 0;
            for (int i = 0; i < results.Length; i++)
            {
                if (results[i] >= DensityThreshold) voxelCount++;
            }

            int colliderCount = collidersParent.childCount;

            sw.Stop();

            UnityEngine.Debug.Log(
                $"[DensityCompute] 임계값 이상 Voxel: {voxelCount}, Box Collider: {colliderCount}");

            GaussianPerfLogger.LogStats(
                splatData.name,
                splatData.Count,
                colliderCount,
                sw.ElapsedMilliseconds
            );
        }

        public SurfaceBuildReport GenerateSurfaceColliders(SplatData splatData, Transform targetTransform, SurfaceColliderSettings settings)
        {
            Stopwatch sw = Stopwatch.StartNew();
            if (!TryRunDensity(splatData, out uint[] results, out Vector3 min))
                return default;

            long gpuMilliseconds = sw.ElapsedMilliseconds;
            PrepareLocalColliderParent(targetTransform);
            SurfaceBuildReport report = SurfaceColliderBuilder.Build(
                splatData.Positions,
                results,
                min,
                VoxelSize,
                GridSize,
                DensityThreshold,
                settings,
                collidersParent);
            report.gpuMilliseconds = gpuMilliseconds;
            sw.Stop();
            UnityEngine.Debug.Log(report.ToLogString());
            GaussianPerfLogger.LogStats(
                splatData.name,
                splatData.Count,
                report.orientedCount + report.neighborAccepted + report.legacyFallbackCount,
                sw.ElapsedMilliseconds);
            return report;
        }

        bool TryRunDensity(SplatData splatData, out uint[] results, out Vector3 min)
        {
            results = null;
            min = Vector3.zero;
            if (splatData == null || splatData.Count == 0)
                return false;

            min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            int validCount = 0;

            foreach (var pos in splatData.Positions)
            {
                if (float.IsNaN(pos.x) || float.IsInfinity(pos.x)) continue;
                min = Vector3.Min(min, pos);
                max = Vector3.Max(max, pos);
                validCount++;
            }

            if (validCount == 0)
                return false;

            lastMinBounds = min;
            hasLastBounds = true;
            Vector3 size = max - min;
            size.x = Mathf.Max(size.x, 0.01f);
            size.y = Mathf.Max(size.y, 0.01f);
            size.z = Mathf.Max(size.z, 0.01f);
            VoxelSize = Mathf.Max(size.x / GridSize.x, size.y / GridSize.y, size.z / GridSize.z, 0.01f);

            int totalGroups = Mathf.CeilToInt(splatData.Count / (float)ThreadGroupSize);
            int dispatchCount = Mathf.CeilToInt(totalGroups / (float)MaxThreadGroupsPerDimension);
            UnityEngine.Debug.Log(
                $"[DensityCompute] Gaussian 총개수: {splatData.Count}, 전체 Dispatch 그룹 수: {totalGroups}, Dispatch 실행 횟수: {dispatchCount}, VoxelSize: {VoxelSize}, DensityThreshold: {DensityThreshold}, 좌표계: 로컬");

            ComputeBuffer posBuffer = null;
            ComputeBuffer gridBuffer = null;
            results = new uint[GridSize.x * GridSize.y * GridSize.z];

            try
            {
                posBuffer = new ComputeBuffer(splatData.Count, sizeof(float) * 3);
                posBuffer.SetData(splatData.Positions);

                gridBuffer = new ComputeBuffer(GridSize.x * GridSize.y * GridSize.z, sizeof(uint));
                gridBuffer.SetData(results);

                int kernel = densityShader.FindKernel("CSMain");
                densityShader.SetBuffer(kernel, "_Positions", posBuffer);
                densityShader.SetBuffer(kernel, "_VoxelGrid", gridBuffer);
                densityShader.SetFloat("_VoxelSize", VoxelSize);
                densityShader.SetVector("_MinBounds", new Vector4(min.x, min.y, min.z, 0));
                densityShader.SetInts("_GridSizeX", GridSize.x);
                densityShader.SetInts("_GridSizeY", GridSize.y);
                densityShader.SetInts("_GridSizeZ", GridSize.z);
                densityShader.SetInt("_PointCount", splatData.Count);

                int groupOffset = 0;
                for (int batchIndex = 0; batchIndex < dispatchCount; batchIndex++)
                {
                    int groupsThisDispatch = Mathf.Min(MaxThreadGroupsPerDimension, totalGroups - groupOffset);
                    int startIndex = groupOffset * ThreadGroupSize;
                    int batchCount = Mathf.Min(groupsThisDispatch * ThreadGroupSize, splatData.Count - startIndex);

                    densityShader.SetInt("_IndexOffset", startIndex);
                    densityShader.Dispatch(kernel, groupsThisDispatch, 1, 1);
                    UnityEngine.Debug.Log(
                        $"[DensityCompute] 배치 {batchIndex + 1}/{dispatchCount}: 시작 인덱스 {startIndex}, 처리 대상 {batchCount}개");

                    groupOffset += groupsThisDispatch;
                }

                gridBuffer.GetData(results);
            }
            finally
            {
                if (posBuffer != null) posBuffer.Release();
                if (gridBuffer != null) gridBuffer.Release();
            }

            return true;
        }

        void PrepareLocalColliderParent(Transform targetTransform)
        {
            if (collidersParent == null)
            {
                GameObject colliderRoot = new GameObject("Gaussian_Colliders_Root");
                colliderRoot.transform.SetParent(targetTransform, false);
                collidersParent = colliderRoot.transform;
            }
            else if (collidersParent != targetTransform && !collidersParent.IsChildOf(targetTransform))
            {
                collidersParent.SetParent(targetTransform, false);
            }

            if (collidersParent == targetTransform)
                return;

            collidersParent.localPosition = Vector3.zero;
            collidersParent.localRotation = Quaternion.identity;
            collidersParent.localScale = Vector3.one;
        }
    }
}
