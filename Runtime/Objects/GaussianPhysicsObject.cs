using System.Collections.Generic;
using System.Text;
using GaussianSplatting.Runtime;
using GsPhysicsToolkit.Core;
using GsPhysicsToolkit.Physics;
using UnityEngine;

public class GaussianPhysicsObject : MonoBehaviour
{
    public const string VisualName = "GaussianVisual";
    public const string RendererName = "GaussianSplatRenderer";
    public const string PhysicsName = "GaussianPhysics";
    public const string ColliderRootName = "GeneratedColliders";

    // SplatData는 (-x,-y,-z), aras-p 에셋은 (x,y,z)이다. 렌더러에만 -I를 두면 월드 좌표가 같다.
    public static readonly Vector3 RendererLocalScale = new Vector3(-1f, -1f, -1f);

    public SplatData splatData;
    public GaussianSplatAsset gaussianAsset;

    public Transform visualRoot;
    public GaussianSplatRenderer splatRenderer;
    public Transform physicsRoot;
    public DensityCompute density;
    public Transform generatedColliders;

    public enum ColliderGenerationMode
    {
        Legacy = 0,
        Surface = 1
    }

    [Header("Collider Mode")]
    public ColliderGenerationMode colliderMode = ColliderGenerationMode.Legacy;
    [Min(6)] public int surfaceMinPoints = 24;
    [Range(0.02f, 0.5f)] public float surfaceMaxNormalRatio = 0.15f;
    [Range(0.05f, 0.9f)] public float surfaceMinTangentRatio = 0.2f;
    [Range(0.05f, 0.75f)] public float surfaceMaxThicknessRatio = 0.3f;
    public bool drawColliderModeGizmos = true;
    [Range(100, 8000)] public int colliderGizmoBudget = 2000;

    [Header("Ground Leveling")]
    public bool autoLevel = true;
    public bool drawGroundGizmo = true;
    [Range(300, 20000)] public int sampleCount = 10000;
    [Range(20, 200)] public int ransacIterations = 150;
    public int randomSeed = 1;
    [Range(5f, 80f)] public float maxTiltDegrees = 60f;
    [Range(0.02f, 0.9f)] public float minSupportRatio = 0.08f;
    public Vector3 manualEuler;

    [Header("Leveling Result")]
    [HideInInspector] public bool levelingApplied;
    public bool planeValid;
    public Vector3 estimatedNormal = Vector3.up;
    public Vector3 planeOrigin;
    public float planeExtent;
    public float supportRatio;
    public float tiltDegrees;
    public Vector3 appliedEuler;
    public Quaternion originalLocalRotation = Quaternion.identity;

    [Header("Alignment Debug")]
    public bool drawAlignmentSamples = true;
    [Range(4, 64)] public int alignmentSampleCount = 16;
    public List<AlignmentSampleMarker> alignmentSamples = new List<AlignmentSampleMarker>();

    [System.Serializable]
    public struct AlignmentSampleMarker
    {
        public Vector3 localPoint;
        public Vector3 localCellCenter;
        public Vector3 localColliderCorner;
        public Vector3 localBoxCenter;
        public bool insideGrid;
        public bool hasCollider;
    }

    [ContextMenu("Build And Generate Colliders")]
    public void BuildAndGenerate()
    {
        EnsureHierarchy();
        AlignTransforms();

        if (splatRenderer != null)
            splatRenderer.m_Asset = gaussianAsset;

        if (splatData == null)
        {
            Debug.LogWarning("[GaussianPhysicsObject] SplatData가 없어 Collider를 생성하지 않습니다.");
            return;
        }

        if (density == null || physicsRoot == null || generatedColliders == null)
        {
            Debug.LogError("[GaussianPhysicsObject] 물리 Hierarchy가 없습니다.");
            return;
        }

        if (density.densityShader == null)
        {
            Debug.LogError("[GaussianPhysicsObject] DensityCompute.densityShader가 비어 있습니다.");
            return;
        }

        density.collidersParent = generatedColliders;
        if (colliderMode == ColliderGenerationMode.Surface)
        {
            if (levelingApplied || Quaternion.Angle(transform.localRotation, Quaternion.identity) > 0.05f)
            {
                Debug.LogWarning(
                    "[GaussianPhysicsObject] Surface 모드에서는 수평 보정을 추가하지 않습니다. " +
                    "루트에 기존 회전이 남아 있습니다. 원본 기울기가 필요하면 Reset Leveling 후 다시 생성하세요.");
            }

            density.GenerateSurfaceColliders(splatData, physicsRoot, CreateSurfaceSettings());
        }
        else
        {
            if (autoLevel)
                ApplyLeveling(false);

            density.GenerateColliders(splatData, physicsRoot);
        }

        Debug.Log("[GaussianPhysicsObject] 렌더러 Local Scale (-1,-1,-1). Collider는 로컬 좌표로 생성됩니다.");

        CollisionDebugMode collisionDebug = GetComponent<CollisionDebugMode>();
        if (collisionDebug != null)
            collisionDebug.NotifyCollidersRebuilt();
    }

    SurfaceColliderSettings CreateSurfaceSettings()
    {
        return new SurfaceColliderSettings
        {
            minPointCount = surfaceMinPoints,
            maxNormalEigenRatio = surfaceMaxNormalRatio,
            minTangentEigenRatio = surfaceMinTangentRatio,
            maxThicknessRatio = surfaceMaxThicknessRatio,
            sizePadding = 0.005f
        };
    }

    public bool ApplyLeveling(bool warnIfAlreadyApplied)
    {
        if (levelingApplied)
        {
            if (warnIfAlreadyApplied)
                Debug.LogWarning("[GaussianPhysicsObject] 수평 보정이 이미 적용되어 있습니다. Reset Leveling 후 다시 실행하세요.");
            return false;
        }

        if (!TryEstimateGround(out GroundPlaneEstimate estimate))
            return false;

        originalLocalRotation = transform.localRotation;
        Vector3 worldNormal = transform.TransformDirection(estimate.normal);
        Quaternion delta = Quaternion.FromToRotation(worldNormal, Vector3.up);
        transform.localRotation = delta * transform.localRotation;
        appliedEuler = delta.eulerAngles;
        levelingApplied = true;
        Debug.Log($"[GaussianPhysicsObject] 수평 보정을 적용했습니다. {estimate.message} Euler {appliedEuler}");
        return true;
    }

    public bool ApplyManualLeveling(bool warnIfAlreadyApplied)
    {
        if (levelingApplied)
        {
            if (warnIfAlreadyApplied)
                Debug.LogWarning("[GaussianPhysicsObject] 수평 보정이 이미 적용되어 있습니다. Reset Leveling 후 다시 실행하세요.");
            return false;
        }

        originalLocalRotation = transform.localRotation;
        Quaternion delta = Quaternion.Euler(manualEuler);
        transform.localRotation = delta * transform.localRotation;
        appliedEuler = manualEuler;
        levelingApplied = true;
        Debug.Log($"[GaussianPhysicsObject] 수동 회전을 적용했습니다. Euler {manualEuler}");
        return true;
    }

    public void ResetLeveling()
    {
        if (!levelingApplied)
        {
            Debug.LogWarning("[GaussianPhysicsObject] 되돌릴 수평 보정이 없습니다.");
            return;
        }

        transform.localRotation = originalLocalRotation;
        levelingApplied = false;
        appliedEuler = Vector3.zero;
        Debug.Log("[GaussianPhysicsObject] 수평 보정을 원래 회전으로 되돌렸습니다.");
    }

    public bool TryEstimateGround(out GroundPlaneEstimate estimate)
    {
        estimate = default;
        if (splatData == null || splatData.Positions == null || splatData.Count < 3)
        {
            planeValid = false;
            Debug.LogWarning("[GaussianPhysicsObject] 지면 추정에 필요한 SplatData가 없습니다. 회전을 적용하지 않습니다.");
            return false;
        }

        GroundPlaneSettings settings = GroundPlaneSettings.Default;
        settings.sampleCount = sampleCount;
        settings.iterations = ransacIterations;
        settings.seed = randomSeed;
        settings.maxTiltDegrees = maxTiltDegrees;
        settings.minSupportRatio = minSupportRatio;
        estimate = GroundPlaneEstimator.Estimate(splatData.Positions, settings);
        planeValid = estimate.success;
        if (!estimate.success)
        {
            Debug.LogWarning($"[GaussianPhysicsObject] 지면을 찾지 못해 자동 회전을 적용하지 않습니다. {estimate.message}");
            return false;
        }

        estimatedNormal = estimate.normal;
        planeOrigin = estimate.origin;
        planeExtent = estimate.extent;
        supportRatio = estimate.supportRatio;
        tiltDegrees = estimate.tiltDegrees;
        return true;
    }

    void OnDrawGizmosSelected()
    {
        DrawAlignmentGizmos();
        DrawColliderModeGizmos();
        if (!drawGroundGizmo || !planeValid || estimatedNormal.sqrMagnitude < 1e-6f)
            return;

        Matrix4x4 previous = Gizmos.matrix;
        Color previousColor = Gizmos.color;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(0.15f, 0.85f, 0.25f, 0.95f);

        Vector3 normal = estimatedNormal.normalized;
        Vector3 reference = Mathf.Abs(normal.y) > 0.9f ? Vector3.right : Vector3.up;
        Vector3 axisU = Vector3.Cross(normal, reference).normalized;
        Vector3 axisV = Vector3.Cross(normal, axisU);
        float extent = Mathf.Max(planeExtent, 0.5f);
        Vector3 center = planeOrigin;
        Vector3 a = center + (-axisU - axisV) * extent;
        Vector3 b = center + (axisU - axisV) * extent;
        Vector3 c = center + (axisU + axisV) * extent;
        Vector3 d = center + (-axisU + axisV) * extent;
        Gizmos.DrawLine(a, b);
        Gizmos.DrawLine(b, c);
        Gizmos.DrawLine(c, d);
        Gizmos.DrawLine(d, a);
        Gizmos.color = new Color(0.2f, 0.45f, 1f, 0.95f);
        Gizmos.DrawLine(center, center + normal * extent * 0.35f);
        Gizmos.matrix = previous;
        Gizmos.color = previousColor;
    }

    [ContextMenu("Validate Alignment")]
    public void ValidateAlignment()
    {
        alignmentSamples.Clear();
        if (splatData == null || splatData.Positions == null || density == null || physicsRoot == null || generatedColliders == null || splatRenderer == null)
        {
            Debug.LogWarning("[Alignment] SplatData, 렌더러, GaussianPhysics, GeneratedColliders가 모두 있어야 검증할 수 있습니다.");
            return;
        }

        StringBuilder report = new StringBuilder();
        report.AppendLine("[Alignment] 좌표계 검증");
        AppendHierarchyReport(report);
        if (colliderMode == ColliderGenerationMode.Surface)
        {
            AppendSurfaceContainmentReport(report);
            Debug.Log(report.ToString());
            return;
        }

        if (!TryComputeFiniteBounds(splatData.Positions, out Vector3 min, out Vector3 max))
        {
            Debug.LogWarning("[Alignment] 유효한 SplatData 좌표가 없습니다.");
            return;
        }

        AppendAssetBoundsReport(report, min, max);

        float voxelSize = density.VoxelSize;
        Vector3Int gridSize = density.GridSize;
        int sampleCount = Mathf.Clamp(alignmentSampleCount, 4, 64);
        int stride = Mathf.Max(1, splatData.Count / sampleCount);
        float maxCoordinateError = 0f;
        float maxCornerError = 0f;
        float maxCenterError = 0f;
        float maxVolumeError = 0f;
        float maxPointToCellCenter = 0f;
        float maxPointToBoxCenter = 0f;
        Vector3 expectedBoxCenter = Vector3.one * (voxelSize * 0.5f);
        int compared = 0;
        int withCollider = 0;
        int outsideGrid = 0;

        for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
        {
            int index = Mathf.Min(splatData.Count - 1, sampleIndex * stride);
            Vector3 localPoint = splatData.Positions[index];
            if (!IsFinite(localPoint))
                continue;

            Vector3 physicsWorld = physicsRoot.TransformPoint(localPoint);
            Vector3 rendererWorld = splatRenderer.transform.TransformPoint(-localPoint);
            float coordinateError = Vector3.Distance(physicsWorld, rendererWorld);
            maxCoordinateError = Mathf.Max(maxCoordinateError, coordinateError);

            Vector3 indexFloat = new Vector3(
                (localPoint.x - min.x) / voxelSize,
                (localPoint.y - min.y) / voxelSize,
                (localPoint.z - min.z) / voxelSize);
            int ix = (int)indexFloat.x;
            int iy = (int)indexFloat.y;
            int iz = (int)indexFloat.z;
            bool insideGrid = ix >= 0 && iy >= 0 && iz >= 0 && ix < gridSize.x && iy < gridSize.y && iz < gridSize.z;

            Vector3 localCorner = min + new Vector3(ix, iy, iz) * voxelSize;
            Vector3 localCellCenter = localCorner + expectedBoxCenter;
            Transform collider = insideGrid ? generatedColliders.Find($"Voxel_{ix}_{iy}_{iz}") : null;
            bool hasCollider = collider != null;
            Vector3 localBoxCenter = localCellCenter;
            if (!insideGrid)
                outsideGrid++;
            if (hasCollider)
            {
                withCollider++;
                float cornerError = Vector3.Distance(collider.localPosition, localCorner);
                maxCornerError = Mathf.Max(maxCornerError, cornerError);
                BoxCollider box = collider.GetComponent<BoxCollider>();
                Vector3 center = box != null ? box.center : Vector3.zero;
                localBoxCenter = collider.localPosition + center;
                maxCenterError = Mathf.Max(maxCenterError, Vector3.Distance(center, expectedBoxCenter));
                maxVolumeError = Mathf.Max(maxVolumeError, Vector3.Distance(localBoxCenter, localCellCenter));
                maxPointToBoxCenter = Mathf.Max(maxPointToBoxCenter, Vector3.Distance(localPoint, localBoxCenter));
            }

            if (insideGrid)
                maxPointToCellCenter = Mathf.Max(maxPointToCellCenter, Vector3.Distance(localPoint, localCellCenter));

            alignmentSamples.Add(new AlignmentSampleMarker
            {
                localPoint = localPoint,
                localCellCenter = localCellCenter,
                localColliderCorner = hasCollider ? collider.localPosition : localCorner,
                localBoxCenter = localBoxCenter,
                insideGrid = insideGrid,
                hasCollider = hasCollider
            });
            compared++;

            if (sampleIndex < 4)
            {
                report.AppendLine(
                    $"샘플 {index}: 좌표오차 {coordinateError:F5}, 셀중심까지 {Vector3.Distance(localPoint, localCellCenter):F4}, " +
                    $"Collider {(hasCollider ? "있음" : insideGrid ? "없음(밀도 미달 가능)" : "없음(그리드 밖)")}, " +
                    $"코너오차 {(hasCollider ? Vector3.Distance(collider.localPosition, localCorner).ToString("F5") : "-")}, " +
                    $"박스중심오차 {(hasCollider ? Vector3.Distance(localBoxCenter, localCellCenter).ToString("F5") : "-")}");
            }
        }

        float halfDiagonal = voxelSize * Mathf.Sqrt(3f) * 0.5f;
        report.AppendLine($"샘플 {compared}개, Collider 매칭 {withCollider}개, 그리드 밖 {outsideGrid}개");
        report.AppendLine($"좌표계 오차 max |렌더러 월드 - 물리 월드| = {maxCoordinateError:F6}");
        report.AppendLine($"autoLevel {autoLevel}, levelingApplied {levelingApplied}, 루트 Euler {transform.localEulerAngles}");
        report.AppendLine($"복셀 코너 오차 max |localPosition - (min + index * size)| = {maxCornerError:F6}");
        report.AppendLine($"BoxCollider.center 오차 max |center - voxelSize/2| = {maxCenterError:F6}");
        report.AppendLine($"영역 오차 max |(localPosition + center) - 셀 중심| = {maxVolumeError:F6}");
        report.AppendLine($"점-셀중심 거리 max = {maxPointToCellCenter:F4} (셀 내부 한계 {halfDiagonal:F4}, VoxelSize {voxelSize:F4})");
        report.AppendLine($"점-박스중심 거리 max = {maxPointToBoxCenter:F4}");
        report.AppendLine("localPosition은 최소 모서리, center는 반 칸입니다. 둘을 더하면 셀 중심이며 박스는 칸 전체를 덮습니다.");
        if (withCollider == 0)
            report.AppendLine("판정: 샘플과 일치하는 Collider가 없습니다. Collider를 다시 생성한 뒤 검증하세요.");
        else if (maxCoordinateError < 0.001f && maxCornerError < 0.001f && maxCenterError < 0.001f && maxVolumeError < 0.001f)
            report.AppendLine("판정: 렌더와 Collider 좌표가 같고, 복셀 영역과 BoxCollider 영역이 일치합니다. 남은 틈은 VoxelSize 해상도입니다.");
        else if (maxCoordinateError < 0.001f && maxCornerError < 0.001f)
            report.AppendLine("판정: 좌표는 일치합니다. BoxCollider.center가 반 칸이 아니면 Collider를 다시 생성하세요.");
        else
            report.AppendLine("판정: 좌표계 오차가 있습니다. 자식 로컬 Transform이 단위 변환이 아니거나 렌더러 스케일이 (-1,-1,-1)이 아닙니다.");

        Debug.Log(report.ToString());
    }

    void AppendSurfaceContainmentReport(StringBuilder report)
    {
        if (generatedColliders == null || density == null || splatData == null || splatData.Positions == null || !density.hasLastBounds)
        {
            report.AppendLine("Surface 검증: Collider를 Surface 모드로 다시 생성한 뒤 확인하세요.");
            return;
        }

        int oriented = 0;
        int neighborBoxes = 0;
        int fallback = 0;
        int badScale = 0;
        ForEachGeneratedBox(true, (child, marker) =>
        {
            if (marker != null && marker.kind == ColliderBuildKind.Pca)
                oriented++;
            else if (marker != null && marker.kind == ColliderBuildKind.Neighbor)
                neighborBoxes++;
            else
                fallback++;
            if (Vector3.Distance(child.localScale, Vector3.one) > 0.001f)
                badScale++;
        });

        float voxelSize = density.VoxelSize;
        Vector3 min = density.lastMinBounds;
        Vector3Int gridSize = density.GridSize;
        int sampleTarget = 2000;
        int stride = Mathf.Max(1, splatData.Count / sampleTarget);
        int orientedSamples = 0;
        int neighborSamples = 0;
        int fallbackSamples = 0;
        int unmatched = 0;
        float orientedOutsideSum = 0f;
        float orientedOutsideMax = 0f;
        float orientedFaceSum = 0f;
        float neighborOutsideSum = 0f;
        float neighborOutsideMax = 0f;
        float neighborFaceSum = 0f;
        float fallbackOutsideSum = 0f;
        float fallbackOutsideMax = 0f;
        float fallbackFaceSum = 0f;

        for (int index = 0; index < splatData.Count; index += stride)
        {
            Vector3 point = splatData.Positions[index];
            if (!IsFinite(point))
                continue;

            int ix = (int)((point.x - min.x) / voxelSize);
            int iy = (int)((point.y - min.y) / voxelSize);
            int iz = (int)((point.z - min.z) / voxelSize);
            if (ix < 0 || iy < 0 || iz < 0 || ix >= gridSize.x || iy >= gridSize.y || iz >= gridSize.z)
                continue;

            Transform collider = FindGeneratedBox($"Surface_{ix}_{iy}_{iz}");
            if (collider == null)
                collider = FindGeneratedBox($"Neighbor_{ix}_{iy}_{iz}");
            if (collider == null)
                collider = FindGeneratedBox($"Voxel_{ix}_{iy}_{iz}");
            if (collider == null)
            {
                unmatched++;
                continue;
            }

            BoxCollider box = collider.GetComponent<BoxCollider>();
            if (box == null)
                continue;

            Vector3 local = Quaternion.Inverse(collider.localRotation) * (point - collider.localPosition) - box.center;
            Vector3 half = box.size * 0.5f;
            float outside = Mathf.Sqrt(
                Mathf.Pow(Mathf.Max(0f, Mathf.Abs(local.x) - half.x), 2f) +
                Mathf.Pow(Mathf.Max(0f, Mathf.Abs(local.y) - half.y), 2f) +
                Mathf.Pow(Mathf.Max(0f, Mathf.Abs(local.z) - half.z), 2f));
            float face = outside > 0f
                ? outside
                : Mathf.Min(half.x - Mathf.Abs(local.x), half.y - Mathf.Abs(local.y), half.z - Mathf.Abs(local.z));
            ColliderShapeMarker marker = collider.GetComponent<ColliderShapeMarker>();
            if (marker != null && marker.kind == ColliderBuildKind.Neighbor)
            {
                neighborSamples++;
                neighborOutsideSum += outside;
                neighborOutsideMax = Mathf.Max(neighborOutsideMax, outside);
                neighborFaceSum += face;
            }
            else if (marker != null && marker.kind == ColliderBuildKind.Pca)
            {
                orientedSamples++;
                orientedOutsideSum += outside;
                orientedOutsideMax = Mathf.Max(orientedOutsideMax, outside);
                orientedFaceSum += face;
            }
            else
            {
                fallbackSamples++;
                fallbackOutsideSum += outside;
                fallbackOutsideMax = Mathf.Max(fallbackOutsideMax, outside);
                fallbackFaceSum += face;
            }
        }

        report.AppendLine($"PCA {oriented}개, 이웃 평면 {neighborBoxes}개, Legacy fallback {fallback}개, 스케일이 1이 아닌 자식 {badScale}개");
        report.AppendLine($"샘플 밖 점 {unmatched}개. 내부 점유 칸이나 밀도 미달 칸은 Collider가 없습니다.");
        report.AppendLine(orientedSamples == 0
            ? "PCA 샘플 없음"
            : $"PCA 샘플 {orientedSamples}, 박스 밖 거리 mean {orientedOutsideSum / orientedSamples:F4} max {orientedOutsideMax:F4}, 면까지 거리 mean {orientedFaceSum / orientedSamples:F4}");
        report.AppendLine(neighborSamples == 0
            ? "이웃 평면 샘플 없음"
            : $"이웃 평면 샘플 {neighborSamples}, 박스 밖 거리 mean {neighborOutsideSum / neighborSamples:F4} max {neighborOutsideMax:F4}, 면까지 거리 mean {neighborFaceSum / neighborSamples:F4}");
        report.AppendLine(fallbackSamples == 0
            ? "Legacy fallback 샘플 없음"
            : $"Legacy fallback 샘플 {fallbackSamples}, 박스 밖 거리 mean {fallbackOutsideSum / fallbackSamples:F4} max {fallbackOutsideMax:F4}, 면까지 거리 mean {fallbackFaceSum / fallbackSamples:F4}");
        report.AppendLine("하늘색은 PCA, 초록색은 이웃 평면, 주황색은 fallback입니다. Gizmo는 BoxCollider의 center와 size를 사용합니다.");
    }

    void DrawColliderModeGizmos()
    {
        if (!drawColliderModeGizmos || generatedColliders == null)
            return;

        Matrix4x4 previous = Gizmos.matrix;
        Color previousColor = Gizmos.color;
        int drawn = 0;
        int budget = Mathf.Max(colliderGizmoBudget, 1);
        bool budgetReached = false;
        ForEachGeneratedBox(false, (child, marker) =>
        {
            if (budgetReached)
                return;

            BoxCollider box = child.GetComponent<BoxCollider>();
            ColliderBuildKind kind = marker != null ? marker.kind : ColliderBuildKind.Fallback;
            if (kind == ColliderBuildKind.Pca)
                Gizmos.color = new Color(0.1f, 0.85f, 1f, 0.95f);
            else if (kind == ColliderBuildKind.Neighbor)
                Gizmos.color = new Color(0.15f, 0.95f, 0.35f, 0.95f);
            else
                Gizmos.color = new Color(1f, 0.55f, 0.1f, 0.85f);
            Gizmos.matrix = child.localToWorldMatrix;
            Gizmos.DrawWireCube(box.center, box.size);
            drawn++;
            if (drawn >= budget * 2)
                budgetReached = true;
        });

        Gizmos.matrix = previous;
        Gizmos.color = previousColor;
    }

    void ForEachGeneratedBox(bool includeInactive, System.Action<Transform, ColliderShapeMarker> visit)
    {
        if (generatedColliders == null)
            return;

        for (int i = 0; i < generatedColliders.childCount; i++)
            VisitGeneratedBox(generatedColliders.GetChild(i), includeInactive, visit);
    }

    void VisitGeneratedBox(Transform node, bool includeInactive, System.Action<Transform, ColliderShapeMarker> visit)
    {
        if (!includeInactive && !node.gameObject.activeInHierarchy)
            return;

        BoxCollider box = node.GetComponent<BoxCollider>();
        if (box != null)
        {
            visit(node, node.GetComponent<ColliderShapeMarker>());
            return;
        }

        for (int i = 0; i < node.childCount; i++)
            VisitGeneratedBox(node.GetChild(i), includeInactive, visit);
    }

    Transform FindGeneratedBox(string name)
    {
        Transform direct = generatedColliders.Find(name);
        if (direct != null)
            return direct;

        for (int i = 0; i < generatedColliders.childCount; i++)
        {
            Transform nested = generatedColliders.GetChild(i).Find(name);
            if (nested != null)
                return nested;
        }

        return null;
    }

    void DrawAlignmentGizmos()
    {
        if (!drawAlignmentSamples || alignmentSamples == null || physicsRoot == null || density == null)
            return;

        float voxelSize = Mathf.Max(density.VoxelSize, 0.01f);
        Vector3 cubeSize = Vector3.one * voxelSize;
        for (int i = 0; i < alignmentSamples.Count; i++)
        {
            AlignmentSampleMarker sample = alignmentSamples[i];
            Vector3 worldPoint = physicsRoot.TransformPoint(sample.localPoint);
            Vector3 worldCellCenter = physicsRoot.TransformPoint(sample.localCellCenter);
            Transform space = generatedColliders != null ? generatedColliders : physicsRoot;
            Vector3 worldBoxCenter = space.TransformPoint(sample.localBoxCenter);

            Gizmos.color = new Color(1f, 0.85f, 0.1f, 0.95f);
            Gizmos.DrawSphere(worldPoint, voxelSize * 0.08f);
            Gizmos.color = sample.insideGrid ? new Color(0.2f, 1f, 0.4f, 0.9f) : Color.red;
            Gizmos.DrawWireCube(worldCellCenter, cubeSize);
            Gizmos.DrawLine(worldPoint, worldCellCenter);

            if (!sample.hasCollider)
                continue;

            Gizmos.color = new Color(0.3f, 0.7f, 1f, 0.9f);
            Gizmos.DrawWireCube(worldBoxCenter, cubeSize);
            Gizmos.DrawLine(worldCellCenter, worldBoxCenter);
        }
    }

    void AppendHierarchyReport(StringBuilder report)
    {
        report.AppendLine($"루트 회전 {transform.localEulerAngles}, 루트 스케일 {transform.localScale}");
        AppendLocal(report, "GaussianVisual", visualRoot);
        AppendLocal(report, "GaussianSplatRenderer", splatRenderer != null ? splatRenderer.transform : null);
        AppendLocal(report, "GaussianPhysics", physicsRoot);
        AppendLocal(report, "GeneratedColliders", generatedColliders);
        bool childrenIdentity = IsIdentity(visualRoot) && IsIdentity(physicsRoot) && IsIdentity(generatedColliders);
        bool rendererAligned = splatRenderer != null
            && splatRenderer.transform.localPosition.sqrMagnitude < 1e-8f
            && splatRenderer.transform.localRotation == Quaternion.identity
            && Vector3.Distance(splatRenderer.transform.localScale, RendererLocalScale) < 1e-4f;
        report.AppendLine(childrenIdentity && rendererAligned
            ? "부모 회전은 루트에만 있고, Visual/Physics는 로컬 단위 변환입니다. 렌더러 스케일 (-1,-1,-1)이 파일 좌표를 SplatData 좌표로 바꿉니다."
            : "자식 로컬 Transform이 기대값과 다릅니다. 루트 회전이 두 번 적용되거나 렌더러 반전이 빠졌을 수 있습니다.");
    }

    static void AppendLocal(StringBuilder report, string label, Transform target)
    {
        if (target == null)
        {
            report.AppendLine($"{label}: 없음");
            return;
        }

        report.AppendLine($"{label} localPos {target.localPosition}, localEuler {target.localEulerAngles}, localScale {target.localScale}");
    }

    static bool IsIdentity(Transform target)
    {
        return target != null
            && target.localPosition.sqrMagnitude < 1e-8f
            && target.localRotation == Quaternion.identity
            && Vector3.Distance(target.localScale, Vector3.one) < 1e-4f;
    }

    void AppendAssetBoundsReport(StringBuilder report, Vector3 splatMin, Vector3 splatMax)
    {
        if (gaussianAsset == null)
        {
            report.AppendLine("GaussianSplatAsset이 없어 bounds 비교를 건너뜁니다.");
            return;
        }

        Vector3 expectedMin = new Vector3(-splatMax.x, -splatMax.y, -splatMax.z);
        Vector3 expectedMax = new Vector3(-splatMin.x, -splatMin.y, -splatMin.z);
        float minDelta = Vector3.Distance(gaussianAsset.boundsMin, expectedMin);
        float maxDelta = Vector3.Distance(gaussianAsset.boundsMax, expectedMax);
        report.AppendLine($"SplatData bounds {splatMin} .. {splatMax}");
        report.AppendLine($"Asset bounds {gaussianAsset.boundsMin} .. {gaussianAsset.boundsMax}");
        report.AppendLine($"Asset bounds와 -SplatData bounds 차이 min {minDelta:F5}, max {maxDelta:F5}");
    }

    static bool TryComputeFiniteBounds(Vector3[] positions, out Vector3 min, out Vector3 max)
    {
        min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        bool found = false;
        for (int i = 0; i < positions.Length; i++)
        {
            Vector3 position = positions[i];
            if (float.IsNaN(position.x) || float.IsInfinity(position.x))
                continue;
            min = Vector3.Min(min, position);
            max = Vector3.Max(max, position);
            found = true;
        }

        return found;
    }

    static bool IsFinite(Vector3 position)
    {
        return !float.IsNaN(position.x) && !float.IsNaN(position.y) && !float.IsNaN(position.z)
            && !float.IsInfinity(position.x) && !float.IsInfinity(position.y) && !float.IsInfinity(position.z);
    }

    public void EnsureHierarchy()
    {
        visualRoot = FindOrCreateChild(transform, VisualName);
        Transform rendererTransform = FindOrCreateChild(visualRoot, RendererName);
        splatRenderer = rendererTransform.GetComponent<GaussianSplatRenderer>();
        if (splatRenderer == null)
            splatRenderer = rendererTransform.gameObject.AddComponent<GaussianSplatRenderer>();

        physicsRoot = FindOrCreateChild(transform, PhysicsName);
        density = physicsRoot.GetComponent<DensityCompute>();
        if (density == null)
            density = physicsRoot.gameObject.AddComponent<DensityCompute>();

        generatedColliders = FindOrCreateChild(physicsRoot, ColliderRootName);
    }

    public void AlignTransforms()
    {
        SetIdentity(visualRoot);
        SetIdentity(physicsRoot);
        SetIdentity(generatedColliders);

        if (splatRenderer == null)
            return;

        Transform rendererTransform = splatRenderer.transform;
        rendererTransform.localPosition = Vector3.zero;
        rendererTransform.localRotation = Quaternion.identity;
        rendererTransform.localScale = RendererLocalScale;
    }

    static Transform FindOrCreateChild(Transform parent, string childName)
    {
        Transform child = parent.Find(childName);
        if (child == null)
        {
            Transform[] descendants = parent.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < descendants.Length; i++)
            {
                if (descendants[i] != parent && descendants[i].name == childName)
                {
                    child = descendants[i];
                    break;
                }
            }
        }

        if (child == null)
        {
            GameObject childObject = new GameObject(childName);
            childObject.transform.SetParent(parent, false);
            return childObject.transform;
        }

        if (child.parent != parent)
            child.SetParent(parent, false);

        return child;
    }

    static void SetIdentity(Transform target)
    {
        if (target == null)
            return;

        bool resetScale = Vector3.Distance(target.localScale, Vector3.one) > 0.0001f;
        bool resetPose = target.localPosition.sqrMagnitude > 1e-8f
            || Quaternion.Angle(target.localRotation, Quaternion.identity) > 0.01f;
        if (resetScale || resetPose)
        {
            Debug.Log(
                $"[GaussianPhysicsObject] {target.name} Transform을 단위값으로 되돌립니다. " +
                $"이전 position {target.localPosition}, rotation {target.localEulerAngles}, scale {target.localScale}");
        }

        target.localPosition = Vector3.zero;
        target.localRotation = Quaternion.identity;
        target.localScale = Vector3.one;
    }
}
