using UnityEngine;

namespace GsPhysicsToolkit.Physics
{
    public enum ColliderBuildKind
    {
        Fallback = 0,
        Pca = 1,
        Neighbor = 2
    }

    public class ColliderShapeMarker : MonoBehaviour
    {
        public bool oriented;
        public ColliderBuildKind kind;
    }
}
