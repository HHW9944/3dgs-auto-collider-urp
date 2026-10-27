using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UnityEngine;

namespace GsPhysicsToolkit.Physics
{
    public struct SurfaceColliderSettings
    {
        public int minPointCount;
        public float maxNormalEigenRatio;
        public float minTangentEigenRatio;
        public float maxThicknessRatio;
        public float sizePadding;
    }

    public struct SurfaceBuildReport
    {
        public bool built;
        public int surfaceCandidates;
        public int planarAccepted;
        public int planarRejected;
        public int interiorSkipped;
        public int orientedCount;
        public int neighborAccepted;
        public int legacyFallbackCount;
        public int neighborNoNeighbor;
        public int neighborTooFewPoints;
        public int neighborPoorFit;
        public int neighborAmbiguous;
        public int neighborThickness;
        public int neighborBoxTooSmall;
        public int neighborBasis;
        public int neighborOther;
        public int outlierPoints;
        public int containmentFailures;
        public int rejectTooFewPoints;
        public int rejectNormalRatio;
        public int rejectTangentRatio;
        public int rejectThickness;
        public int rejectOutlierRatio;
        public int rejectBoxTooSmall;
        public int rejectBasis;
        public int rejectEigenFailed;
        public int rejectDegenerateEigen;
        public int rejectEmptyPoints;
        public int rejectOther;
        public float meanPlaneResidual;
        public float maxPlaneResidual;
        public float voxelSize;
        public long gpuMilliseconds;
        public long cpuMilliseconds;
        public Vector3 parentPosition;
        public Vector3 parentEuler;
        public Vector3 parentScale;

        public string ToLogString()
        {
            int colliders = orientedCount + neighborAccepted + legacyFallbackCount;
            int pcaFailed = surfaceCandidates - orientedCount;
            int rejectSum = rejectTooFewPoints + rejectNormalRatio + rejectTangentRatio + rejectThickness
                + rejectOutlierRatio + rejectBoxTooSmall + rejectBasis
                + rejectEigenFailed + rejectDegenerateEigen + rejectEmptyPoints + rejectOther;
            int neighborRejectSum = neighborNoNeighbor + neighborTooFewPoints + neighborPoorFit + neighborAmbiguous
                + neighborThickness + neighborBoxTooSmall + neighborBasis + neighborOther;
            bool stageMatch = surfaceCandidates == orientedCount + neighborAccepted + legacyFallbackCount;
            bool pcaMatch = pcaFailed == rejectSum && pcaFailed == neighborAccepted + legacyFallbackCount;
            bool neighborMatch = legacyFallbackCount == neighborRejectSum;
            StringBuilder text = new StringBuilder();
            text.AppendLine(
                "[SurfaceCollider] " +
                $"표면후보 {surfaceCandidates}, PCA하늘색 {orientedCount}, 이웃초록 {neighborAccepted}, 주황 {legacyFallbackCount}, " +
                $"Collider {colliders}, GPU {gpuMilliseconds} ms, CPU {cpuMilliseconds} ms, " +
                $"단계 {(stageMatch && pcaMatch && neighborMatch ? "일치" : "불일치")}");
            text.AppendLine($"VoxelSize {voxelSize:F4}, 내부 점유 생략 {interiorSkipped}, 이상점 {outlierPoints}, 박스 밖 inlier {containmentFailures}");
            text.AppendLine($"[PCA 실패 {pcaFailed}] 점부족 {rejectTooFewPoints}, 평면성 {rejectNormalRatio}, 접선 {rejectTangentRatio}, 두께 {rejectThickness}, 이상점 {rejectOutlierRatio}, 박스크기 {rejectBoxTooSmall}, 기저 {rejectBasis}, 고유값실패 {rejectEigenFailed}, 축퇴 {rejectDegenerateEigen}, 점없음 {rejectEmptyPoints}, 기타 {rejectOther}, 합 {rejectSum}, {(pcaMatch ? "일치" : "불일치")}");
            text.AppendLine($"[이웃 재시도 실패 {legacyFallbackCount}] 이웃없음 {neighborNoNeighbor}, 점부족 {neighborTooFewPoints}, 적합도부족 {neighborPoorFit}, 다른평면모호 {neighborAmbiguous}, 두께 {neighborThickness}, 박스크기 {neighborBoxTooSmall}, 기저 {neighborBasis}, 기타 {neighborOther}, 합 {neighborRejectSum}, {(neighborMatch ? "일치" : "불일치")}");
            text.AppendLine("Gizmo는 각 BoxCollider의 localToWorldMatrix, center, size를 그대로 그립니다. 하늘색 PCA, 초록색 이웃 평면, 주황색 fallback.");
            text.AppendLine($"법선 잔차 mean {meanPlaneResidual:F4}, max {maxPlaneResidual:F4}");
            text.AppendLine($"GeneratedColliders localPos {parentPosition}, localEuler {parentEuler}, localScale {parentScale}");
            bool parentIdentity = parentPosition.sqrMagnitude < 1e-8f
                && parentEuler.sqrMagnitude < 0.01f
                && Vector3.Distance(parentScale, Vector3.one) < 0.001f;
            text.AppendLine(parentIdentity
                ? "부모 검증: GeneratedColliders의 위치·회전·스케일이 단위값입니다."
                : "부모 검증: GeneratedColliders Transform이 단위값이 아닙니다. 계층을 다시 구성한 뒤 생성하세요.");
            text.AppendLine(containmentFailures == 0
                ? "포함 검증: 평면으로 채택한 점들은 회전 박스 안에 있습니다."
                : "포함 검증: 회전 박스 밖에 inlier가 있습니다.");
            return text.ToString();
        }
    }

    public static class SurfaceColliderBuilder
    {
        const byte SurfaceFail = 1;
        const byte SurfaceCandidate = 2;
        const byte PcaSuccess = 3;
        const byte NeighborSuccess = 4;

        struct PlaneSlot
        {
            public Vector3 normal;
            public Vector3 tangent;
            public Vector3 origin;
            public float halfThickness;
            public bool valid;
        }

        enum NeighborReject : byte
        {
            None = 0,
            NoNeighbor = 1,
            TooFewPoints = 2,
            PoorFit = 3,
            Ambiguous = 4,
            Thickness = 5,
            BoxTooSmall = 6,
            Basis = 7,
            Other = 8
        }

        struct NeighborFit
        {
            public bool exists;
            public bool passed;
            public bool ratioPassed;
            public bool thicknessPassed;
            public Vector3 normal;
            public Vector3 tangent;
            public Vector3 origin;
            public float gate;
            public float inlierRatio;
            public float meanDistance;
            public float p95;
            public int inliers;
        }

        enum RejectReason : byte
        {
            None = 0,
            TooFewPoints = 1,
            NormalRatio = 2,
            TangentRatio = 3,
            Thickness = 4,
            OutlierRatio = 5,
            BoxTooSmall = 6,
            Basis = 7,
            Other = 8,
            EigenFailed = 9,
            DegenerateEigen = 10,
            EmptyPoints = 11
        }

        struct Accumulator
        {
            public int count;
            public double sx, sy, sz;
            public double xx, xy, xz, yy, yz, zz;
        }

        public static SurfaceBuildReport Build(
            Vector3[] positions,
            uint[] counts,
            Vector3 min,
            float voxelSize,
            Vector3Int gridSize,
            int densityThreshold,
            SurfaceColliderSettings settings,
            Transform parent)
        {
            Stopwatch watch = Stopwatch.StartNew();
            SurfaceBuildReport report = new SurfaceBuildReport { voxelSize = voxelSize };
            if (parent == null || positions == null || counts == null || voxelSize <= 0f)
                return report;

            report.parentPosition = parent.localPosition;
            report.parentEuler = parent.localEulerAngles;
            report.parentScale = parent.localScale;

            int minPoints = Mathf.Max(settings.minPointCount, 6);
            float maxNormalRatio = settings.maxNormalEigenRatio > 0f ? settings.maxNormalEigenRatio : 0.15f;
            float minTangentRatio = settings.minTangentEigenRatio > 0f ? settings.minTangentEigenRatio : 0.2f;
            float maxThicknessRatio = settings.maxThicknessRatio > 0f ? settings.maxThicknessRatio : 0.3f;
            float padding = settings.sizePadding > 0f ? settings.sizePadding : 0.005f;

            int width = gridSize.x;
            int height = gridSize.y;
            int depth = gridSize.z;
            int cellCount = width * height * depth;
            if (counts.Length < cellCount)
                return report;

            for (int childIndex = parent.childCount - 1; childIndex >= 0; childIndex--)
                UnityEngine.Object.DestroyImmediate(parent.GetChild(childIndex).gameObject);

            Transform pcaParent = CreateGroup(parent, "CyanPca");
            Transform neighborParent = CreateGroup(parent, "GreenNeighbor");
            Transform fallbackParent = CreateGroup(parent, "OrangeFallback");

            byte[] state = new byte[cellCount];
            byte[] rejectReasons = new byte[cellCount];
            byte[] neighborRejects = new byte[cellCount];
            PlaneSlot[] planes = new PlaneSlot[cellCount];
            Vector3[] outward = new Vector3[cellCount];
            for (int z = 0; z < depth; z++)
            {
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int flat = x + width * (y + height * z);
                        if (counts[flat] < (uint)densityThreshold)
                            continue;

                        Vector3 openDirection = Vector3.zero;
                        if (IsExposed(counts, width, height, depth, x, y, z, densityThreshold, ref openDirection))
                        {
                            state[flat] = SurfaceFail;
                            outward[flat] = openDirection;
                            report.surfaceCandidates++;
                        }
                        else
                        {
                            report.interiorSkipped++;
                        }
                    }
                }
            }

            Accumulator[] accumulators = new Accumulator[cellCount];
            for (int i = 0; i < positions.Length; i++)
            {
                Vector3 position = positions[i];
                if (!TryCell(position, min, voxelSize, width, height, depth, out int flat))
                    continue;
                if (state[flat] == 0)
                    continue;

                Accumulator accumulator = accumulators[flat];
                accumulator.count++;
                accumulator.sx += position.x;
                accumulator.sy += position.y;
                accumulator.sz += position.z;
                accumulator.xx += position.x * position.x;
                accumulator.xy += position.x * position.y;
                accumulator.xz += position.x * position.z;
                accumulator.yy += position.y * position.y;
                accumulator.yz += position.y * position.z;
                accumulator.zz += position.z * position.z;
                accumulators[flat] = accumulator;
            }

            for (int flat = 0; flat < cellCount; flat++)
            {
                if (state[flat] != SurfaceFail)
                    continue;
                if (TryEigenCandidate(accumulators[flat], minPoints, maxNormalRatio, minTangentRatio, out RejectReason reason))
                    state[flat] = SurfaceCandidate;
                else
                    rejectReasons[flat] = (byte)reason;
            }

            List<Vector3>[] bins = new List<Vector3>[cellCount];
            for (int flat = 0; flat < cellCount; flat++)
            {
                if (state[flat] == SurfaceCandidate && accumulators[flat].count < minPoints)
                {
                    state[flat] = SurfaceFail;
                    rejectReasons[flat] = (byte)RejectReason.TooFewPoints;
                }
            }

            for (int i = 0; i < positions.Length; i++)
            {
                Vector3 position = positions[i];
                if (!TryCell(position, min, voxelSize, width, height, depth, out int flat))
                    continue;
                if (state[flat] != SurfaceCandidate)
                    continue;
                if (bins[flat] == null)
                    bins[flat] = new List<Vector3>(32);
                bins[flat].Add(position);
            }

            for (int flat = 0; flat < cellCount; flat++)
            {
                if (state[flat] == SurfaceCandidate && bins[flat] == null)
                {
                    state[flat] = SurfaceFail;
                    rejectReasons[flat] = (byte)RejectReason.EmptyPoints;
                }
            }

            double residualSum = 0.0;
            int residualCount = 0;
            for (int flat = 0; flat < cellCount; flat++)
            {
                if (state[flat] != SurfaceCandidate || bins[flat] == null)
                    continue;

                if (!TryBuildOrientedBox(
                    bins[flat],
                    outward[flat],
                    voxelSize,
                    maxThicknessRatio,
                    padding,
                    out Vector3 center,
                    out Quaternion rotation,
                    out Vector3 size,
                    out int outliers,
                    out int outside,
                    out float meanResidual,
                    out float maxResidual,
                    out RejectReason buildReason))
                {
                    state[flat] = SurfaceFail;
                    rejectReasons[flat] = (byte)buildReason;
                    bins[flat] = null;
                    continue;
                }

                int z = flat / (width * height);
                int remainder = flat % (width * height);
                int y = remainder / width;
                int x = remainder % width;
                CreateOrientedBox(pcaParent, x, y, z, center, rotation, size);
                planes[flat] = new PlaneSlot
                {
                    normal = rotation * Vector3.up,
                    tangent = rotation * Vector3.right,
                    origin = center,
                    halfThickness = Mathf.Max(size.y * 0.5f, 0.01f),
                    valid = true
                };
                state[flat] = PcaSuccess;
                report.outlierPoints += outliers;
                report.containmentFailures += outside;
                residualSum += meanResidual * Mathf.Max(bins[flat].Count - outliers, 1);
                residualCount += Mathf.Max(bins[flat].Count - outliers, 1);
                if (maxResidual > report.maxPlaneResidual)
                    report.maxPlaneResidual = maxResidual;
                bins[flat] = null;
            }

            List<Vector3>[] failedPoints = new List<Vector3>[cellCount];
            for (int i = 0; i < positions.Length; i++)
            {
                Vector3 position = positions[i];
                if (!TryCell(position, min, voxelSize, width, height, depth, out int flat))
                    continue;
                if (state[flat] != SurfaceFail)
                    continue;
                if (failedPoints[flat] == null)
                    failedPoints[flat] = new List<Vector3>(16);
                failedPoints[flat].Add(position);
            }

            for (int flat = 0; flat < cellCount; flat++)
            {
                if (state[flat] != SurfaceFail)
                    continue;

                int z = flat / (width * height);
                int remainder = flat % (width * height);
                int y = remainder / width;
                int x = remainder % width;
                if (!TryAdoptNeighbor(
                    failedPoints[flat],
                    planes,
                    width,
                    height,
                    depth,
                    x,
                    y,
                    z,
                    voxelSize,
                    maxThicknessRatio,
                    padding,
                    out Vector3 center,
                    out Quaternion rotation,
                    out Vector3 size,
                    out NeighborReject neighborReason))
                {
                    neighborRejects[flat] = (byte)neighborReason;
                    failedPoints[flat] = null;
                    continue;
                }

                CreateNeighborBox(neighborParent, x, y, z, center, rotation, size);
                state[flat] = NeighborSuccess;
                failedPoints[flat] = null;
            }

            for (int flat = 0; flat < cellCount; flat++)
            {
                if (state[flat] == SurfaceCandidate)
                {
                    state[flat] = SurfaceFail;
                    if (rejectReasons[flat] == (byte)RejectReason.None)
                        rejectReasons[flat] = (byte)RejectReason.Other;
                    if (neighborRejects[flat] == (byte)NeighborReject.None)
                        neighborRejects[flat] = (byte)NeighborReject.Other;
                }

                if (state[flat] != SurfaceFail)
                    continue;

                int z = flat / (width * height);
                int remainder = flat % (width * height);
                int y = remainder / width;
                int x = remainder % width;
                CreateLegacyBox(fallbackParent, min, voxelSize, x, y, z);
            }

            for (int flat = 0; flat < cellCount; flat++)
            {
                if (state[flat] == PcaSuccess)
                {
                    report.orientedCount++;
                    report.planarAccepted++;
                }
                else if (state[flat] == NeighborSuccess)
                {
                    report.neighborAccepted++;
                    CountRejectReason(ref report, (RejectReason)rejectReasons[flat]);
                }
                else if (state[flat] == SurfaceFail)
                {
                    report.legacyFallbackCount++;
                    report.planarRejected++;
                    CountRejectReason(ref report, (RejectReason)rejectReasons[flat]);
                    CountNeighborReject(ref report, (NeighborReject)neighborRejects[flat]);
                }
            }

            report.meanPlaneResidual = residualCount > 0 ? (float)(residualSum / residualCount) : 0f;
            report.built = true;
            watch.Stop();
            report.cpuMilliseconds = watch.ElapsedMilliseconds;
            return report;
        }

        static bool IsExposed(uint[] counts, int width, int height, int depth, int x, int y, int z, int threshold, ref Vector3 openDirection)
        {
            bool exposed = false;
            AccumulateOpen(counts, width, height, depth, x - 1, y, z, threshold, new Vector3(-1f, 0f, 0f), ref openDirection, ref exposed);
            AccumulateOpen(counts, width, height, depth, x + 1, y, z, threshold, new Vector3(1f, 0f, 0f), ref openDirection, ref exposed);
            AccumulateOpen(counts, width, height, depth, x, y - 1, z, threshold, new Vector3(0f, -1f, 0f), ref openDirection, ref exposed);
            AccumulateOpen(counts, width, height, depth, x, y + 1, z, threshold, new Vector3(0f, 1f, 0f), ref openDirection, ref exposed);
            AccumulateOpen(counts, width, height, depth, x, y, z - 1, threshold, new Vector3(0f, 0f, -1f), ref openDirection, ref exposed);
            AccumulateOpen(counts, width, height, depth, x, y, z + 1, threshold, new Vector3(0f, 0f, 1f), ref openDirection, ref exposed);
            return exposed;
        }

        static void AccumulateOpen(
            uint[] counts,
            int width,
            int height,
            int depth,
            int x,
            int y,
            int z,
            int threshold,
            Vector3 direction,
            ref Vector3 openDirection,
            ref bool exposed)
        {
            if (x < 0 || y < 0 || z < 0 || x >= width || y >= height || z >= depth
                || counts[x + width * (y + height * z)] < (uint)threshold)
            {
                exposed = true;
                openDirection += direction;
            }
        }

        static bool TryCell(Vector3 position, Vector3 min, float voxelSize, int width, int height, int depth, out int flat)
        {
            flat = -1;
            if (float.IsNaN(position.x) || float.IsNaN(position.y) || float.IsNaN(position.z)
                || float.IsInfinity(position.x) || float.IsInfinity(position.y) || float.IsInfinity(position.z))
                return false;

            int x = (int)((position.x - min.x) / voxelSize);
            int y = (int)((position.y - min.y) / voxelSize);
            int z = (int)((position.z - min.z) / voxelSize);
            if (x < 0 || y < 0 || z < 0 || x >= width || y >= height || z >= depth)
                return false;

            flat = x + width * (y + height * z);
            return true;
        }

        static bool TryEigenCandidate(Accumulator accumulator, int minPoints, float maxNormalRatio, float minTangentRatio, out RejectReason reason)
        {
            reason = RejectReason.TooFewPoints;
            if (accumulator.count < minPoints)
                return false;

            double inverse = 1.0 / accumulator.count;
            double mx = accumulator.sx * inverse;
            double my = accumulator.sy * inverse;
            double mz = accumulator.sz * inverse;
            double cxx = accumulator.xx * inverse - mx * mx;
            double cxy = accumulator.xy * inverse - mx * my;
            double cxz = accumulator.xz * inverse - mx * mz;
            double cyy = accumulator.yy * inverse - my * my;
            double cyz = accumulator.yz * inverse - my * mz;
            double czz = accumulator.zz * inverse - mz * mz;
            reason = RejectReason.EigenFailed;
            if (!TryEigen(cxx, cxy, cxz, cyy, cyz, czz, out double l0, out double l1, out double l2, out _, out _, out _))
                return false;
            reason = RejectReason.DegenerateEigen;
            if (l2 <= 1e-10 || l1 <= 1e-10)
                return false;
            reason = RejectReason.NormalRatio;
            if (l0 > maxNormalRatio * l1)
                return false;
            reason = RejectReason.TangentRatio;
            if (l1 < minTangentRatio * l2)
                return false;
            reason = RejectReason.None;
            return true;
        }

        static void CountRejectReason(ref SurfaceBuildReport report, RejectReason reason)
        {
            switch (reason)
            {
                case RejectReason.TooFewPoints:
                    report.rejectTooFewPoints++;
                    break;
                case RejectReason.NormalRatio:
                    report.rejectNormalRatio++;
                    break;
                case RejectReason.TangentRatio:
                    report.rejectTangentRatio++;
                    break;
                case RejectReason.Thickness:
                    report.rejectThickness++;
                    break;
                case RejectReason.OutlierRatio:
                    report.rejectOutlierRatio++;
                    break;
                case RejectReason.BoxTooSmall:
                    report.rejectBoxTooSmall++;
                    break;
                case RejectReason.Basis:
                    report.rejectBasis++;
                    break;
                case RejectReason.EigenFailed:
                    report.rejectEigenFailed++;
                    break;
                case RejectReason.DegenerateEigen:
                    report.rejectDegenerateEigen++;
                    break;
                case RejectReason.EmptyPoints:
                    report.rejectEmptyPoints++;
                    break;
                default:
                    report.rejectOther++;
                    break;
            }
        }

        static void CountNeighborReject(ref SurfaceBuildReport report, NeighborReject reason)
        {
            switch (reason)
            {
                case NeighborReject.NoNeighbor:
                    report.neighborNoNeighbor++;
                    break;
                case NeighborReject.TooFewPoints:
                    report.neighborTooFewPoints++;
                    break;
                case NeighborReject.PoorFit:
                    report.neighborPoorFit++;
                    break;
                case NeighborReject.Ambiguous:
                    report.neighborAmbiguous++;
                    break;
                case NeighborReject.Thickness:
                    report.neighborThickness++;
                    break;
                case NeighborReject.BoxTooSmall:
                    report.neighborBoxTooSmall++;
                    break;
                case NeighborReject.Basis:
                    report.neighborBasis++;
                    break;
                default:
                    report.neighborOther++;
                    break;
            }
        }

        static bool TryAdoptNeighbor(
            List<Vector3> points,
            PlaneSlot[] planes,
            int width,
            int height,
            int depth,
            int x,
            int y,
            int z,
            float voxelSize,
            float maxThicknessRatio,
            float padding,
            out Vector3 center,
            out Quaternion rotation,
            out Vector3 size,
            out NeighborReject reason)
        {
            center = Vector3.zero;
            rotation = Quaternion.identity;
            size = Vector3.zero;
            reason = NeighborReject.TooFewPoints;
            if (points == null || points.Count < 6)
                return false;

            NeighborFit[] fits = new NeighborFit[6];
            int fitCount = 0;
            fitCount += EvaluateNeighbor(points, planes, width, height, depth, x - 1, y, z, voxelSize, maxThicknessRatio, ref fits[0]) ? 1 : 0;
            fitCount += EvaluateNeighbor(points, planes, width, height, depth, x + 1, y, z, voxelSize, maxThicknessRatio, ref fits[1]) ? 1 : 0;
            fitCount += EvaluateNeighbor(points, planes, width, height, depth, x, y - 1, z, voxelSize, maxThicknessRatio, ref fits[2]) ? 1 : 0;
            fitCount += EvaluateNeighbor(points, planes, width, height, depth, x, y + 1, z, voxelSize, maxThicknessRatio, ref fits[3]) ? 1 : 0;
            fitCount += EvaluateNeighbor(points, planes, width, height, depth, x, y, z - 1, voxelSize, maxThicknessRatio, ref fits[4]) ? 1 : 0;
            fitCount += EvaluateNeighbor(points, planes, width, height, depth, x, y, z + 1, voxelSize, maxThicknessRatio, ref fits[5]) ? 1 : 0;
            if (fitCount == 0)
            {
                reason = NeighborReject.NoNeighbor;
                return false;
            }

            int best = -1;
            int second = -1;
            for (int i = 0; i < fits.Length; i++)
            {
                if (!fits[i].exists)
                    continue;
                if (best < 0 || IsBetterFit(fits[i], fits[best]))
                {
                    second = best;
                    best = i;
                }
                else if (second < 0 || IsBetterFit(fits[i], fits[second]))
                {
                    second = i;
                }
            }

            if (best < 0)
            {
                reason = NeighborReject.NoNeighbor;
                return false;
            }

            if (!fits[best].passed)
            {
                reason = fits[best].ratioPassed && !fits[best].thicknessPassed
                    ? NeighborReject.Thickness
                    : NeighborReject.PoorFit;
                return false;
            }

            const float differentSurfaceDot = 0.9063078f;
            if (second >= 0 && fits[second].passed
                && Mathf.Abs(Vector3.Dot(fits[best].normal, fits[second].normal)) < differentSurfaceDot
                && fits[second].inlierRatio >= fits[best].inlierRatio - 0.10f)
            {
                reason = NeighborReject.Ambiguous;
                return false;
            }

            return TryBuildFixedFrame(points, fits[best], padding, out center, out rotation, out size, out reason);
        }

        static bool EvaluateNeighbor(
            List<Vector3> points,
            PlaneSlot[] planes,
            int width,
            int height,
            int depth,
            int x,
            int y,
            int z,
            float voxelSize,
            float maxThicknessRatio,
            ref NeighborFit fit)
        {
            fit = default;
            if (x < 0 || y < 0 || z < 0 || x >= width || y >= height || z >= depth)
                return false;

            PlaneSlot plane = planes[x + width * (y + height * z)];
            if (!plane.valid)
                return false;

            Vector3 normal = plane.normal.normalized;
            float gate = Mathf.Min(Mathf.Max(plane.halfThickness * 2f, 0.04f), Mathf.Max(voxelSize * maxThicknessRatio, 0.04f));
            float[] distances = new float[points.Count];
            int inliers = 0;
            float distanceSum = 0f;
            for (int i = 0; i < points.Count; i++)
            {
                float distance = Mathf.Abs(Vector3.Dot(points[i] - plane.origin, normal));
                distances[i] = distance;
                if (distance <= gate)
                {
                    inliers++;
                    distanceSum += distance;
                }
            }

            Array.Sort(distances);
            int p95Index = Mathf.Clamp(Mathf.CeilToInt(points.Count * 0.95f) - 1, 0, points.Count - 1);
            float p95 = distances[p95Index];
            float ratio = inliers / (float)points.Count;
            fit.exists = true;
            fit.normal = normal;
            fit.tangent = plane.tangent;
            fit.origin = plane.origin;
            fit.gate = gate;
            fit.inliers = inliers;
            fit.inlierRatio = ratio;
            fit.meanDistance = inliers > 0 ? distanceSum / inliers : float.MaxValue;
            fit.p95 = p95;
            fit.ratioPassed = inliers >= 6 && ratio >= 0.75f;
            fit.thicknessPassed = p95 * 2f <= voxelSize * maxThicknessRatio;
            fit.passed = fit.ratioPassed && fit.thicknessPassed;
            return true;
        }

        static bool IsBetterFit(NeighborFit candidate, NeighborFit current)
        {
            if (candidate.passed != current.passed)
                return candidate.passed;
            if (!Mathf.Approximately(candidate.inlierRatio, current.inlierRatio))
                return candidate.inlierRatio > current.inlierRatio;
            return candidate.meanDistance < current.meanDistance;
        }

        static bool TryBuildFixedFrame(
            List<Vector3> points,
            NeighborFit fit,
            float padding,
            out Vector3 center,
            out Quaternion rotation,
            out Vector3 size,
            out NeighborReject reason)
        {
            center = Vector3.zero;
            rotation = Quaternion.identity;
            size = Vector3.zero;
            Vector3 normal = fit.normal.normalized;
            Vector3 tangent = fit.tangent - normal * Vector3.Dot(fit.tangent, normal);
            reason = NeighborReject.Basis;
            if (tangent.sqrMagnitude < 1e-8f)
                return false;
            tangent.Normalize();
            Vector3 bitangent = Vector3.Cross(tangent, normal);
            if (bitangent.sqrMagnitude < 1e-8f)
                return false;
            bitangent.Normalize();
            tangent = Vector3.Cross(normal, bitangent).normalized;

            bool hasInlier = false;
            Vector3 minProjection = Vector3.zero;
            Vector3 maxProjection = Vector3.zero;
            int inlierCount = 0;
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 delta = points[i] - fit.origin;
                float alongNormal = Vector3.Dot(delta, normal);
                if (Mathf.Abs(alongNormal) > fit.gate)
                    continue;
                Vector3 projection = new Vector3(Vector3.Dot(delta, tangent), alongNormal, Vector3.Dot(delta, bitangent));
                if (!hasInlier)
                {
                    minProjection = maxProjection = projection;
                    hasInlier = true;
                }
                else
                {
                    minProjection = Vector3.Min(minProjection, projection);
                    maxProjection = Vector3.Max(maxProjection, projection);
                }

                inlierCount++;
            }

            reason = NeighborReject.TooFewPoints;
            if (!hasInlier || inlierCount < 6)
                return false;
            reason = NeighborReject.BoxTooSmall;
            if (maxProjection.x - minProjection.x < 0.05f || maxProjection.z - minProjection.z < 0.05f)
                return false;

            Vector3 mid = (minProjection + maxProjection) * 0.5f;
            center = fit.origin + tangent * mid.x + normal * mid.y + bitangent * mid.z;
            size = maxProjection - minProjection + Vector3.one * (padding * 2f);
            size = Vector3.Max(size, new Vector3(0.01f, 0.01f, 0.01f));

            Matrix4x4 basis = Matrix4x4.identity;
            basis.SetColumn(0, new Vector4(tangent.x, tangent.y, tangent.z, 0f));
            basis.SetColumn(1, new Vector4(normal.x, normal.y, normal.z, 0f));
            basis.SetColumn(2, new Vector4(bitangent.x, bitangent.y, bitangent.z, 0f));
            rotation = basis.rotation;

            Vector3 right = rotation * Vector3.right;
            Vector3 up = rotation * Vector3.up;
            Vector3 forward = rotation * Vector3.forward;
            reason = NeighborReject.Basis;
            if ((right - tangent).sqrMagnitude > 1e-4f || (up - normal).sqrMagnitude > 1e-4f || (forward - bitangent).sqrMagnitude > 1e-4f)
                return false;

            int outside = CountOutsideGate(points, fit.origin, normal, fit.gate, center, rotation, size);
            if (outside > 0)
            {
                float overflow = MeasureOverflowGate(points, fit.origin, normal, fit.gate, center, rotation, size);
                size += Vector3.one * (overflow * 2f + 0.001f);
                outside = CountOutsideGate(points, fit.origin, normal, fit.gate, center, rotation, size);
            }

            reason = outside == 0 ? NeighborReject.None : NeighborReject.Basis;
            return outside == 0;
        }

        static int CountOutsideGate(List<Vector3> points, Vector3 origin, Vector3 normal, float gate, Vector3 center, Quaternion rotation, Vector3 size)
        {
            Vector3 half = size * 0.5f;
            Quaternion inverse = Quaternion.Inverse(rotation);
            int outside = 0;
            for (int i = 0; i < points.Count; i++)
            {
                if (Mathf.Abs(Vector3.Dot(points[i] - origin, normal)) > gate)
                    continue;
                Vector3 local = inverse * (points[i] - center);
                if (Mathf.Abs(local.x) > half.x + 1e-4f || Mathf.Abs(local.y) > half.y + 1e-4f || Mathf.Abs(local.z) > half.z + 1e-4f)
                    outside++;
            }

            return outside;
        }

        static float MeasureOverflowGate(List<Vector3> points, Vector3 origin, Vector3 normal, float gate, Vector3 center, Quaternion rotation, Vector3 size)
        {
            Vector3 half = size * 0.5f;
            Quaternion inverse = Quaternion.Inverse(rotation);
            float overflow = 0f;
            for (int i = 0; i < points.Count; i++)
            {
                if (Mathf.Abs(Vector3.Dot(points[i] - origin, normal)) > gate)
                    continue;
                Vector3 local = inverse * (points[i] - center);
                overflow = Mathf.Max(overflow, Mathf.Abs(local.x) - half.x);
                overflow = Mathf.Max(overflow, Mathf.Abs(local.y) - half.y);
                overflow = Mathf.Max(overflow, Mathf.Abs(local.z) - half.z);
            }

            return Mathf.Max(overflow, 0f);
        }

        static bool TryBuildOrientedBox(
            List<Vector3> points,
            Vector3 openDirection,
            float voxelSize,
            float maxThicknessRatio,
            float padding,
            out Vector3 center,
            out Quaternion rotation,
            out Vector3 size,
            out int outliers,
            out int outside,
            out float meanResidual,
            out float maxResidual,
            out RejectReason reason)
        {
            center = Vector3.zero;
            rotation = Quaternion.identity;
            size = Vector3.zero;
            outliers = 0;
            outside = 0;
            meanResidual = 0f;
            maxResidual = 0f;
            reason = RejectReason.TooFewPoints;
            if (points == null || points.Count < 6)
                return false;

            Vector3 mean = Vector3.zero;
            for (int i = 0; i < points.Count; i++)
                mean += points[i];
            mean /= points.Count;

            double cxx = 0, cxy = 0, cxz = 0, cyy = 0, cyz = 0, czz = 0;
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 delta = points[i] - mean;
                cxx += delta.x * delta.x;
                cxy += delta.x * delta.y;
                cxz += delta.x * delta.z;
                cyy += delta.y * delta.y;
                cyz += delta.y * delta.z;
                czz += delta.z * delta.z;
            }

            double inverse = 1.0 / points.Count;
            reason = RejectReason.EigenFailed;
            if (!TryEigen(cxx * inverse, cxy * inverse, cxz * inverse, cyy * inverse, cyz * inverse, czz * inverse,
                out _, out _, out _, out Vector3 normal, out Vector3 tangent, out _))
                return false;

            if (openDirection.sqrMagnitude > 1e-6f && Vector3.Dot(normal, openDirection) < 0f)
            {
                normal = -normal;
                tangent = -tangent;
            }

            Vector3 bitangent = Vector3.Cross(tangent, normal);
            reason = RejectReason.Basis;
            if (bitangent.sqrMagnitude < 1e-8f)
                return false;
            bitangent.Normalize();
            tangent = Vector3.Cross(normal, bitangent).normalized;

            float[] distances = new float[points.Count];
            for (int i = 0; i < points.Count; i++)
                distances[i] = Mathf.Abs(Vector3.Dot(points[i] - mean, normal));
            Array.Sort(distances);
            int p95Index = Mathf.Clamp(Mathf.CeilToInt(points.Count * 0.95f) - 1, 0, points.Count - 1);
            float p95 = distances[p95Index];
            reason = RejectReason.Thickness;
            if (p95 * 2f > voxelSize * maxThicknessRatio)
                return false;

            float gate = Mathf.Max(p95 * 3f, 0.02f);
            for (int i = 0; i < points.Count; i++)
            {
                if (Mathf.Abs(Vector3.Dot(points[i] - mean, normal)) > gate)
                    outliers++;
            }

            reason = RejectReason.OutlierRatio;
            if (outliers > points.Count * 0.08f)
                return false;

            bool hasInlier = false;
            Vector3 minProjection = Vector3.zero;
            Vector3 maxProjection = Vector3.zero;
            double inlierResidual = 0.0;
            int inlierCount = 0;
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 delta = points[i] - mean;
                float alongNormal = Vector3.Dot(delta, normal);
                if (Mathf.Abs(alongNormal) > gate)
                    continue;

                Vector3 projection = new Vector3(Vector3.Dot(delta, tangent), alongNormal, Vector3.Dot(delta, bitangent));
                if (!hasInlier)
                {
                    minProjection = maxProjection = projection;
                    hasInlier = true;
                }
                else
                {
                    minProjection = Vector3.Min(minProjection, projection);
                    maxProjection = Vector3.Max(maxProjection, projection);
                }

                inlierResidual += Mathf.Abs(alongNormal);
                inlierCount++;
                if (Mathf.Abs(alongNormal) > maxResidual)
                    maxResidual = Mathf.Abs(alongNormal);
            }

            reason = RejectReason.TooFewPoints;
            if (!hasInlier || inlierCount < 6)
                return false;
            reason = RejectReason.BoxTooSmall;
            if (maxProjection.x - minProjection.x < 0.05f || maxProjection.z - minProjection.z < 0.05f)
                return false;

            Vector3 mid = (minProjection + maxProjection) * 0.5f;
            center = mean + tangent * mid.x + normal * mid.y + bitangent * mid.z;
            size = maxProjection - minProjection + Vector3.one * (padding * 2f);
            size = Vector3.Max(size, new Vector3(0.01f, 0.01f, 0.01f));
            meanResidual = (float)(inlierResidual / inlierCount);

            Matrix4x4 basis = Matrix4x4.identity;
            basis.SetColumn(0, new Vector4(tangent.x, tangent.y, tangent.z, 0f));
            basis.SetColumn(1, new Vector4(normal.x, normal.y, normal.z, 0f));
            basis.SetColumn(2, new Vector4(bitangent.x, bitangent.y, bitangent.z, 0f));
            rotation = basis.rotation;

            Vector3 right = rotation * Vector3.right;
            Vector3 up = rotation * Vector3.up;
            Vector3 forward = rotation * Vector3.forward;
            reason = RejectReason.Basis;
            if ((right - tangent).sqrMagnitude > 1e-4f || (up - normal).sqrMagnitude > 1e-4f || (forward - bitangent).sqrMagnitude > 1e-4f)
                return false;

            outside = CountOutside(points, mean, normal, gate, center, rotation, size);
            if (outside > 0)
            {
                float overflow = MeasureOverflow(points, mean, normal, gate, center, rotation, size);
                size += Vector3.one * (overflow * 2f + 0.001f);
                outside = CountOutside(points, mean, normal, gate, center, rotation, size);
            }

            reason = outside == 0 ? RejectReason.None : RejectReason.Basis;
            return outside == 0;
        }

        static int CountOutside(List<Vector3> points, Vector3 mean, Vector3 normal, float gate, Vector3 center, Quaternion rotation, Vector3 size)
        {
            Vector3 half = size * 0.5f;
            Quaternion inverse = Quaternion.Inverse(rotation);
            int outside = 0;
            for (int i = 0; i < points.Count; i++)
            {
                if (Mathf.Abs(Vector3.Dot(points[i] - mean, normal)) > gate)
                    continue;
                Vector3 local = inverse * (points[i] - center);
                if (Mathf.Abs(local.x) > half.x + 1e-4f || Mathf.Abs(local.y) > half.y + 1e-4f || Mathf.Abs(local.z) > half.z + 1e-4f)
                    outside++;
            }

            return outside;
        }

        static float MeasureOverflow(List<Vector3> points, Vector3 mean, Vector3 normal, float gate, Vector3 center, Quaternion rotation, Vector3 size)
        {
            Vector3 half = size * 0.5f;
            Quaternion inverse = Quaternion.Inverse(rotation);
            float overflow = 0f;
            for (int i = 0; i < points.Count; i++)
            {
                if (Mathf.Abs(Vector3.Dot(points[i] - mean, normal)) > gate)
                    continue;
                Vector3 local = inverse * (points[i] - center);
                overflow = Mathf.Max(overflow, Mathf.Abs(local.x) - half.x);
                overflow = Mathf.Max(overflow, Mathf.Abs(local.y) - half.y);
                overflow = Mathf.Max(overflow, Mathf.Abs(local.z) - half.z);
            }

            return Mathf.Max(overflow, 0f);
        }

        static Transform CreateGroup(Transform parent, string name)
        {
            GameObject group = new GameObject(name);
            group.transform.SetParent(parent, false);
            group.transform.localPosition = Vector3.zero;
            group.transform.localRotation = Quaternion.identity;
            group.transform.localScale = Vector3.one;
            return group.transform;
        }

        static void CreateOrientedBox(Transform parent, int x, int y, int z, Vector3 center, Quaternion rotation, Vector3 size)
        {
            GameObject obj = new GameObject($"Surface_{x}_{y}_{z}");
            obj.transform.SetParent(parent, false);
            obj.transform.localPosition = center;
            obj.transform.localRotation = rotation;
            obj.transform.localScale = Vector3.one;
            BoxCollider box = obj.AddComponent<BoxCollider>();
            box.center = Vector3.zero;
            box.size = size;
            ColliderShapeMarker marker = obj.AddComponent<ColliderShapeMarker>();
            marker.oriented = true;
            marker.kind = ColliderBuildKind.Pca;
        }

        static void CreateNeighborBox(Transform parent, int x, int y, int z, Vector3 center, Quaternion rotation, Vector3 size)
        {
            GameObject obj = new GameObject($"Neighbor_{x}_{y}_{z}");
            obj.transform.SetParent(parent, false);
            obj.transform.localPosition = center;
            obj.transform.localRotation = rotation;
            obj.transform.localScale = Vector3.one;
            BoxCollider box = obj.AddComponent<BoxCollider>();
            box.center = Vector3.zero;
            box.size = size;
            ColliderShapeMarker marker = obj.AddComponent<ColliderShapeMarker>();
            marker.oriented = false;
            marker.kind = ColliderBuildKind.Neighbor;
        }

        static void CreateLegacyBox(Transform parent, Vector3 minOffset, float voxelSize, int x, int y, int z)
        {
            GameObject obj = new GameObject($"Voxel_{x}_{y}_{z}");
            obj.transform.SetParent(parent, false);
            obj.transform.localPosition = minOffset + new Vector3(x, y, z) * voxelSize;
            obj.transform.localRotation = Quaternion.identity;
            obj.transform.localScale = Vector3.one;
            BoxCollider box = obj.AddComponent<BoxCollider>();
            box.size = Vector3.one * voxelSize;
            box.center = Vector3.one * (voxelSize * 0.5f);
            ColliderShapeMarker marker = obj.AddComponent<ColliderShapeMarker>();
            marker.oriented = false;
            marker.kind = ColliderBuildKind.Fallback;
        }

        static bool TryEigen(
            double xx, double xy, double xz, double yy, double yz, double zz,
            out double l0, out double l1, out double l2,
            out Vector3 a0, out Vector3 a1, out Vector3 a2)
        {
            double a00 = xx, a01 = xy, a02 = xz, a11 = yy, a12 = yz, a22 = zz;
            double v00 = 1, v01 = 0, v02 = 0;
            double v10 = 0, v11 = 1, v12 = 0;
            double v20 = 0, v21 = 0, v22 = 1;

            for (int sweep = 0; sweep < 12; sweep++)
            {
                Rotate(ref a00, ref a01, ref a02, ref a11, ref a12, ref a22, ref v00, ref v01, ref v02, ref v10, ref v11, ref v12, ref v20, ref v21, ref v22, 0, 1);
                Rotate(ref a00, ref a01, ref a02, ref a11, ref a12, ref a22, ref v00, ref v01, ref v02, ref v10, ref v11, ref v12, ref v20, ref v21, ref v22, 0, 2);
                Rotate(ref a00, ref a01, ref a02, ref a11, ref a12, ref a22, ref v00, ref v01, ref v02, ref v10, ref v11, ref v12, ref v20, ref v21, ref v22, 1, 2);
            }

            l0 = a00;
            l1 = a11;
            l2 = a22;
            a0 = new Vector3((float)v00, (float)v10, (float)v20);
            a1 = new Vector3((float)v01, (float)v11, (float)v21);
            a2 = new Vector3((float)v02, (float)v12, (float)v22);
            SortEigen(ref l0, ref l1, ref l2, ref a0, ref a1, ref a2);
            if (a0.sqrMagnitude < 1e-12f || a1.sqrMagnitude < 1e-12f || a2.sqrMagnitude < 1e-12f)
                return false;
            a0.Normalize();
            a1.Normalize();
            a2.Normalize();
            return true;
        }

        static void SortEigen(ref double l0, ref double l1, ref double l2, ref Vector3 a0, ref Vector3 a1, ref Vector3 a2)
        {
            if (l0 > l1)
                Swap(ref l0, ref l1, ref a0, ref a1);
            if (l1 > l2)
                Swap(ref l1, ref l2, ref a1, ref a2);
            if (l0 > l1)
                Swap(ref l0, ref l1, ref a0, ref a1);
        }

        static void Swap(ref double leftValue, ref double rightValue, ref Vector3 leftAxis, ref Vector3 rightAxis)
        {
            double value = leftValue;
            leftValue = rightValue;
            rightValue = value;
            Vector3 axis = leftAxis;
            leftAxis = rightAxis;
            rightAxis = axis;
        }

        static void Rotate(
            ref double a00, ref double a01, ref double a02, ref double a11, ref double a12, ref double a22,
            ref double v00, ref double v01, ref double v02, ref double v10, ref double v11, ref double v12, ref double v20, ref double v21, ref double v22,
            int p, int q)
        {
            double apq = p == 0 ? (q == 1 ? a01 : a02) : a12;
            if (Math.Abs(apq) < 1e-15)
                return;

            double app = p == 0 ? a00 : a11;
            double aqq = q == 1 ? a11 : a22;
            double phi = 0.5 * Math.Atan2(2.0 * apq, aqq - app);
            double c = Math.Cos(phi);
            double s = Math.Sin(phi);

            if (p == 0 && q == 1)
            {
                double n00 = c * c * a00 - 2 * s * c * a01 + s * s * a11;
                double n11 = s * s * a00 + 2 * s * c * a01 + c * c * a11;
                double n02 = c * a02 - s * a12;
                double n12 = s * a02 + c * a12;
                a00 = n00;
                a11 = n11;
                a01 = 0;
                a02 = n02;
                a12 = n12;
            }
            else if (p == 0 && q == 2)
            {
                double n00 = c * c * a00 - 2 * s * c * a02 + s * s * a22;
                double n22 = s * s * a00 + 2 * s * c * a02 + c * c * a22;
                double n01 = c * a01 - s * a12;
                double n12 = s * a01 + c * a12;
                a00 = n00;
                a22 = n22;
                a02 = 0;
                a01 = n01;
                a12 = n12;
            }
            else
            {
                double n11 = c * c * a11 - 2 * s * c * a12 + s * s * a22;
                double n22 = s * s * a11 + 2 * s * c * a12 + c * c * a22;
                double n01 = c * a01 - s * a02;
                double n02 = s * a01 + c * a02;
                a11 = n11;
                a22 = n22;
                a12 = 0;
                a01 = n01;
                a02 = n02;
            }

            ApplyVectorRotation(ref v00, ref v01, ref v02, c, s, p, q);
            ApplyVectorRotation(ref v10, ref v11, ref v12, c, s, p, q);
            ApplyVectorRotation(ref v20, ref v21, ref v22, c, s, p, q);
        }

        static void ApplyVectorRotation(ref double c0, ref double c1, ref double c2, double c, double s, int p, int q)
        {
            double vp = p == 0 ? c0 : c1;
            double vq = q == 1 ? c1 : c2;
            double np = c * vp - s * vq;
            double nq = s * vp + c * vq;
            if (p == 0)
                c0 = np;
            else
                c1 = np;
            if (q == 1)
                c1 = nq;
            else
                c2 = nq;
        }
    }
}
