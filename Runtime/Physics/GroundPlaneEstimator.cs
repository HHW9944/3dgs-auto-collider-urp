using System.Collections.Generic;
using UnityEngine;

namespace GsPhysicsToolkit.Physics
{
    public struct GroundPlaneEstimate
    {
        public bool success;
        public string message;
        public Vector3 normal;
        public Vector3 origin;
        public float offset;
        public float extent;
        public float supportRatio;
        public float tiltDegrees;
        public float relativeHeight;
        public float spread;
        public int sampleCount;
        public int inlierCount;
    }

    public struct GroundPlaneSettings
    {
        public int sampleCount;
        public int iterations;
        public int seed;
        public float maxTiltDegrees;
        public float minSupportRatio;
        public float maxRelativeHeight;
        public float minSpread;
        public float distanceFraction;

        public static GroundPlaneSettings Default => new GroundPlaneSettings
        {
            sampleCount = 10000,
            iterations = 150,
            seed = 1,
            maxTiltDegrees = 60f,
            minSupportRatio = 0.08f,
            maxRelativeHeight = 0.45f,
            minSpread = 0.2f,
            distanceFraction = 0.008f
        };
    }

    public static class GroundPlaneEstimator
    {
        public static GroundPlaneEstimate Estimate(Vector3[] positions, GroundPlaneSettings settings)
        {
            GroundPlaneEstimate result = new GroundPlaneEstimate
            {
                normal = Vector3.up,
                message = "지면을 찾지 못했습니다."
            };

            if (positions == null || positions.Length < 3)
            {
                result.message = "지면 추정에 필요한 점이 3개 미만입니다.";
                return result;
            }

            int requested = Mathf.Clamp(settings.sampleCount, 300, 20000);
            int iterations = Mathf.Clamp(settings.iterations, 20, 200);
            Vector3[] sample = SamplePositions(positions, requested, settings.seed);
            result.sampleCount = sample.Length;
            if (sample.Length < 3)
            {
                result.message = "유효한 좌표 샘플이 3개 미만입니다.";
                return result;
            }

            Vector3 sampleMin = sample[0];
            Vector3 sampleMax = sample[0];
            for (int i = 1; i < sample.Length; i++)
            {
                sampleMin = Vector3.Min(sampleMin, sample[i]);
                sampleMax = Vector3.Max(sampleMax, sample[i]);
            }

            float diagonal = Vector3.Distance(sampleMin, sampleMax);
            if (diagonal < 1e-4f)
            {
                result.message = "샘플 좌표가 한 점에 모여 있어 평면을 만들 수 없습니다.";
                return result;
            }

            float threshold = Mathf.Max(diagonal * Mathf.Max(0.001f, settings.distanceFraction), 0.02f);
            float minEdge = diagonal * 0.02f;
            float minEdgeSqr = minEdge * minEdge;
            float maxTilt = Mathf.Clamp(settings.maxTiltDegrees, 5f, 80f);

            System.Random random = new System.Random(settings.seed);
            float bestScore = float.MinValue;
            bool found = false;
            PlaneHypothesis best = default;
            PlaneHypothesis nearest = default;
            float nearestSupport = -1f;

            for (int iteration = 0; iteration < iterations; iteration++)
            {
                int i0 = random.Next(sample.Length);
                int i1 = random.Next(sample.Length);
                int i2 = random.Next(sample.Length);
                if (i0 == i1 || i0 == i2 || i1 == i2)
                    continue;

                Vector3 p0 = sample[i0];
                Vector3 p1 = sample[i1];
                Vector3 p2 = sample[i2];
                if ((p1 - p0).sqrMagnitude < minEdgeSqr || (p2 - p0).sqrMagnitude < minEdgeSqr || (p2 - p1).sqrMagnitude < minEdgeSqr)
                    continue;

                Vector3 normal = Vector3.Cross(p1 - p0, p2 - p0);
                if (normal.sqrMagnitude < minEdgeSqr * 0.01f)
                    continue;
                normal.Normalize();
                if (Vector3.Dot(normal, Vector3.up) < 0f)
                    normal = -normal;

                float tilt = Vector3.Angle(normal, Vector3.up);
                if (tilt > maxTilt)
                    continue;

                if (!TryScorePlane(sample, sampleMin, sampleMax, normal, p0, threshold, out PlaneHypothesis hypothesis))
                    continue;

                hypothesis.tiltDegrees = tilt;
                bool accepted = hypothesis.supportRatio >= settings.minSupportRatio
                    && hypothesis.spread >= settings.minSpread
                    && hypothesis.relativeHeight <= settings.maxRelativeHeight;

                if (hypothesis.supportRatio > nearestSupport)
                {
                    nearestSupport = hypothesis.supportRatio;
                    nearest = hypothesis;
                }

                if (!accepted)
                    continue;

                float horizontal = 1f - (tilt / maxTilt);
                float lowWeight = 1f - hypothesis.relativeHeight;
                float score = hypothesis.supportRatio * (0.5f + 0.5f * horizontal) * (0.5f + 0.5f * lowWeight) * hypothesis.spread;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = hypothesis;
                    found = true;
                }
            }

            if (!found)
            {
                result.message = nearestSupport < 0f
                    ? "기울기 조건을 통과한 평면 후보가 없습니다."
                    : $"지면 조건을 만족하는 평면이 없습니다. 최대 후보 support {nearest.supportRatio:F2}, tilt {nearest.tiltDegrees:F1}°, spread {nearest.spread:F2}, height {nearest.relativeHeight:F2}";
                return result;
            }

            RefineNormal(sample, best, threshold, out Vector3 refinedNormal, out Vector3 origin);
            if (Vector3.Dot(refinedNormal, best.normal) < 0f)
                refinedNormal = -refinedNormal;
            if (Vector3.Dot(refinedNormal, Vector3.up) < 0f)
                refinedNormal = -refinedNormal;

            float refinedTilt = Vector3.Angle(refinedNormal, Vector3.up);
            if (refinedTilt > maxTilt)
            {
                refinedNormal = best.normal;
                origin = best.origin;
                refinedTilt = best.tiltDegrees;
            }

            result.success = true;
            result.normal = refinedNormal;
            result.origin = origin;
            result.offset = Vector3.Dot(refinedNormal, origin);
            result.extent = best.extent;
            result.supportRatio = best.supportRatio;
            result.tiltDegrees = refinedTilt;
            result.relativeHeight = best.relativeHeight;
            result.spread = best.spread;
            result.inlierCount = best.inlierCount;
            result.message = $"지면 추정 support {best.supportRatio:F2}, tilt {refinedTilt:F1}°, inliers {best.inlierCount}/{sample.Length}";
            return result;
        }

        static Vector3[] SamplePositions(Vector3[] positions, int targetCount, int seed)
        {
            int target = Mathf.Min(targetCount, positions.Length);
            int[] picked = new int[target];
            HashSet<int> chosen = new HashSet<int>();
            System.Random random = new System.Random(seed);
            int filled = 0;
            int guard = 0;
            int guardLimit = target * 30;
            while (filled < target && guard < guardLimit)
            {
                int index = random.Next(positions.Length);
                guard++;
                if (!chosen.Add(index))
                    continue;
                picked[filled++] = index;
            }

            List<Vector3> sample = new List<Vector3>(filled);
            for (int i = 0; i < filled; i++)
            {
                int index = picked[i];
                Vector3 position = positions[index];
                if (float.IsNaN(position.x) || float.IsNaN(position.y) || float.IsNaN(position.z))
                    continue;
                if (float.IsInfinity(position.x) || float.IsInfinity(position.y) || float.IsInfinity(position.z))
                    continue;
                sample.Add(position);
            }

            return sample.ToArray();
        }

        static bool TryScorePlane(Vector3[] sample, Vector3 sampleMin, Vector3 sampleMax, Vector3 normal, Vector3 planePoint, float threshold, out PlaneHypothesis hypothesis)
        {
            hypothesis = default;
            BuildPlaneAxes(normal, out Vector3 axisU, out Vector3 axisV);
            ProjectAabb(sampleMin, sampleMax, axisU, axisV, out float sampleMinU, out float sampleMaxU, out float sampleMinV, out float sampleMaxV);
            float sampleArea = Mathf.Max(sampleMaxU - sampleMinU, 1e-5f) * Mathf.Max(sampleMaxV - sampleMinV, 1e-5f);

            int inliers = 0;
            Vector3 inlierSum = Vector3.zero;
            float minProj = float.MaxValue;
            float maxProj = float.MinValue;
            float inlierProjSum = 0f;
            float minU = float.MaxValue;
            float maxU = float.MinValue;
            float minV = float.MaxValue;
            float maxV = float.MinValue;
            float planeOffset = Vector3.Dot(normal, planePoint);

            for (int i = 0; i < sample.Length; i++)
            {
                Vector3 position = sample[i];
                float projection = Vector3.Dot(normal, position);
                if (projection < minProj) minProj = projection;
                if (projection > maxProj) maxProj = projection;

                if (Mathf.Abs(projection - planeOffset) > threshold)
                    continue;

                inliers++;
                inlierSum += position;
                inlierProjSum += projection;
                float u = Vector3.Dot(axisU, position);
                float v = Vector3.Dot(axisV, position);
                if (u < minU) minU = u;
                if (u > maxU) maxU = u;
                if (v < minV) minV = v;
                if (v > maxV) maxV = v;
            }

            if (inliers < 3 || maxProj - minProj < 1e-4f)
                return false;

            float inlierArea = Mathf.Max(maxU - minU, 0f) * Mathf.Max(maxV - minV, 0f);
            hypothesis.normal = normal;
            hypothesis.origin = inlierSum / inliers;
            hypothesis.offset = Vector3.Dot(normal, hypothesis.origin);
            hypothesis.inlierCount = inliers;
            hypothesis.supportRatio = inliers / (float)sample.Length;
            hypothesis.relativeHeight = Mathf.InverseLerp(minProj, maxProj, inlierProjSum / inliers);
            hypothesis.spread = Mathf.Clamp01(inlierArea / sampleArea);
            hypothesis.extent = 0.5f * Mathf.Max(maxU - minU, maxV - minV);
            return true;
        }

        static void RefineNormal(Vector3[] sample, PlaneHypothesis hypothesis, float threshold, out Vector3 normal, out Vector3 origin)
        {
            Vector3 centroid = Vector3.zero;
            int count = 0;
            int limit = Mathf.Min(sample.Length, 4000);
            for (int i = 0; i < limit; i++)
            {
                if (Mathf.Abs(Vector3.Dot(hypothesis.normal, sample[i]) - hypothesis.offset) > threshold)
                    continue;
                centroid += sample[i];
                count++;
            }

            if (count < 3)
            {
                normal = hypothesis.normal;
                origin = hypothesis.origin;
                return;
            }

            centroid /= count;
            float xx = 0f, xy = 0f, xz = 0f, yy = 0f, yz = 0f, zz = 0f;
            int used = 0;
            for (int i = 0; i < limit; i++)
            {
                if (Mathf.Abs(Vector3.Dot(hypothesis.normal, sample[i]) - hypothesis.offset) > threshold)
                    continue;
                Vector3 residual = sample[i] - centroid;
                xx += residual.x * residual.x;
                xy += residual.x * residual.y;
                xz += residual.x * residual.z;
                yy += residual.y * residual.y;
                yz += residual.y * residual.z;
                zz += residual.z * residual.z;
                used++;
            }

            float detX = yy * zz - yz * yz;
            float detY = xx * zz - xz * xz;
            float detZ = xx * yy - xy * xy;
            normal = new Vector3(detX, xz * yz - xy * zz, xy * yz - xz * yy);
            float bestDet = detX;
            if (detY > bestDet)
            {
                bestDet = detY;
                normal = new Vector3(xz * yz - xy * zz, detY, xy * xz - yz * xx);
            }
            if (detZ > bestDet)
                normal = new Vector3(xy * yz - xz * yy, xy * xz - yz * xx, detZ);

            if (used < 3 || normal.sqrMagnitude < 1e-10f)
            {
                normal = hypothesis.normal;
                origin = hypothesis.origin;
                return;
            }

            normal.Normalize();
            origin = centroid;
        }

        static void BuildPlaneAxes(Vector3 normal, out Vector3 axisU, out Vector3 axisV)
        {
            Vector3 reference = Mathf.Abs(normal.y) > 0.9f ? Vector3.right : Vector3.up;
            axisU = Vector3.Cross(normal, reference);
            if (axisU.sqrMagnitude < 1e-8f)
                axisU = Vector3.Cross(normal, Vector3.forward);
            axisU.Normalize();
            axisV = Vector3.Cross(normal, axisU);
        }

        static void ProjectAabb(Vector3 min, Vector3 max, Vector3 axisU, Vector3 axisV, out float minU, out float maxU, out float minV, out float maxV)
        {
            minU = float.MaxValue;
            maxU = float.MinValue;
            minV = float.MaxValue;
            maxV = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = new Vector3(
                    (i & 1) == 0 ? min.x : max.x,
                    (i & 2) == 0 ? min.y : max.y,
                    (i & 4) == 0 ? min.z : max.z);
                float u = Vector3.Dot(axisU, corner);
                float v = Vector3.Dot(axisV, corner);
                if (u < minU) minU = u;
                if (u > maxU) maxU = u;
                if (v < minV) minV = v;
                if (v > maxV) maxV = v;
            }
        }

        struct PlaneHypothesis
        {
            public Vector3 normal;
            public Vector3 origin;
            public float offset;
            public int inlierCount;
            public float supportRatio;
            public float relativeHeight;
            public float spread;
            public float extent;
            public float tiltDegrees;
        }
    }
}
