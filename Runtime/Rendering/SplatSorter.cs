using UnityEngine;

namespace GsPhysicsToolkit.Rendering
{
    public static class SplatSorter
    {
        public static void SortBackToFront(GaussianSplat.Gaussian[] gaussians, Matrix4x4 localToWorld, Camera camera)
        {
            if (gaussians == null || gaussians.Length <= 1 || camera == null)
            {
                return;
            }

            Vector3 camForward = camera.transform.forward;
            Vector3 camPosition = camera.transform.position;

            System.Array.Sort(gaussians, (a, b) =>
            {
                Vector3 worldA = localToWorld.MultiplyPoint3x4(new Vector3(a.pos.x, a.pos.y, a.pos.z));
                Vector3 worldB = localToWorld.MultiplyPoint3x4(new Vector3(b.pos.x, b.pos.y, b.pos.z));
                float depthA = Vector3.Dot(worldA - camPosition, camForward);
                float depthB = Vector3.Dot(worldB - camPosition, camForward);
                return depthB.CompareTo(depthA);
            });
        }
    }
}
