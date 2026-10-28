using System.Collections.Generic;
using GsPhysicsToolkit.Physics;
using UnityEngine;

public partial class CollisionDebugMode
{
    static readonly Color LiveColor = new Color(1f, 0.15f, 0.85f, 1f);
    static readonly Color NormalColor = new Color(0.3f, 0.7f, 1f, 1f);
    static readonly Color SphereColor = new Color(1f, 1f, 1f, 1f);
    static readonly Color ContactFaceColor = new Color(1f, 0.92f, 0.16f, 0.42f);
    static readonly int[] FaceTriangles = { 0, 1, 2, 0, 2, 3, 0, 2, 1, 0, 3, 2 };

    readonly Dictionary<ContactKey, LiveContact> liveContacts = new Dictionary<ContactKey, LiveContact>();
    readonly List<ContactKey> liveOrder = new List<ContactKey>();
    readonly Dictionary<ContactKey, KeptCollider> keptHits = new Dictionary<ContactKey, KeptCollider>();
    readonly List<ContactKey> keptOrder = new List<ContactKey>();
    readonly List<Vector3> keptGizmoVertices = new List<Vector3>(64);
    readonly List<int> keptGizmoTriangles = new List<int>(96);
    readonly List<Vector3> meshVertices = new List<Vector3>(256);
    readonly List<Color> meshColors = new List<Color>(256);
    readonly List<Vector3> faceVertices = new List<Vector3>(64);
    readonly List<Color> faceColors = new List<Color>(64);
    readonly List<int> faceTriangles = new List<int>(96);
    readonly Vector3[] gizmoQuad = new Vector3[4];

    Mesh contactMesh;
    Mesh faceMesh;
    Mesh gizmoFaceMesh;
    Material faceMaterial;
    bool pausedByDebug;
    float savedTimeScale = 1f;

    public int LiveContactCount => liveOrder.Count;

    struct BoxFaceInfo
    {
        public int axis;
        public int sign;
        public bool ambiguous;
        public bool valid;
    }

    sealed class LiveContact
    {
        public BoxCollider box;
        public Rigidbody body;
        public SphereCollider sphere;
        public Vector3 point;
        public Vector3 normal;
        public float separation;
        public string kindName;
        public BoxFaceInfo face;
        public bool logged;
        public int loggedAxis;
        public int loggedSign;
        public bool loggedAmbiguous;
    }

    sealed class KeptCollider
    {
        public BoxCollider box;
        public readonly List<BoxFaceInfo> faces = new List<BoxFaceInfo>(2);
    }

    public void NoteCollision(Rigidbody body, Collision collision, bool ended)
    {
        if (!enableCollisionDebugMode || collision == null)
            return;

        BoxCollider box = collision.collider as BoxCollider;
        if (box == null || !BelongsToToolkit(box))
            return;

        ContactKey id = new ContactKey(box);
        if (ended)
        {
            if (liveContacts.TryGetValue(id, out LiveContact existing) && existing.box != null)
                Debug.Log("[CollisionDebug] 접촉 종료 " + existing.box.name + " / " + existing.kindName, existing.box);
            if (hitCollider == box)
                hitExpireTime = Time.unscaledTime + Mathf.Max(0f, highlightSeconds);
            liveContacts.Remove(id);
            liveOrder.Remove(id);
            return;
        }

        ContactPoint best = collision.GetContact(0);
        for (int i = 1; i < collision.contactCount; i++)
        {
            ContactPoint candidate = collision.GetContact(i);
            if (candidate.separation < best.separation)
                best = candidate;
        }

        LiveContact live = null;
        if (!liveContacts.TryGetValue(id, out live))
        {
            live = new LiveContact();
            liveContacts.Add(id, live);
            liveOrder.Add(id);
        }

        live.box = box;
        live.body = body;
        live.sphere = best.thisCollider as SphereCollider;
        if (live.sphere == null && body != null)
            live.sphere = body.GetComponent<SphereCollider>();
        live.point = best.point;
        live.normal = best.normal.sqrMagnitude > 1e-8f ? best.normal.normalized : Vector3.up;
        live.separation = best.separation;
        live.kindName = KindNameOf(box);
        live.face = ClassifyFace(box, live.point, live.normal);
        RememberHit(box, live.face);
        if (!live.logged || live.loggedAxis != live.face.axis || live.loggedSign != live.face.sign || live.loggedAmbiguous != live.face.ambiguous)
        {
            live.logged = true;
            live.loggedAxis = live.face.axis;
            live.loggedSign = live.face.sign;
            live.loggedAmbiguous = live.face.ambiguous;
            LogContact(live);
        }
    }

    public bool IsLiveContact(BoxCollider box)
    {
        return box != null && liveContacts.ContainsKey(new ContactKey(box));
    }

    public void ClearLiveContacts()
    {
        liveContacts.Clear();
        liveOrder.Clear();
        if (contactMesh != null)
            Destroy(contactMesh);
        if (faceMesh != null)
            Destroy(faceMesh);
        if (gizmoFaceMesh != null)
            Destroy(gizmoFaceMesh);
        if (faceMaterial != null)
            Destroy(faceMaterial);
        contactMesh = null;
        faceMesh = null;
        gizmoFaceMesh = null;
        faceMaterial = null;
    }

    [ContextMenu("Clear Hit Collider History")]
    public void ClearHitColliderHistory()
    {
        keptHits.Clear();
        keptOrder.Clear();
    }

    void DrawLiveContactGizmos()
    {
        PruneDeadContacts();
        for (int i = 0; i < liveOrder.Count; i++)
        {
            if (!liveContacts.TryGetValue(liveOrder[i], out LiveContact live) || live.box == null)
                continue;
            DrawContactMarks(live, true);
        }
    }

    void DrawIsolatedContactGizmo()
    {
        LiveContact live = SelectedLiveContact();
        if (live == null || live.box == null)
            return;

        DrawContactMarks(live, true);
        DrawSphere(live);
    }

    void DrawContactMarks(LiveContact live, bool drawSphere)
    {
        if (!IsKept(live.box))
        {
            DrawContactFaceGizmo(live);
            Gizmos.color = LiveColor;
            DrawWire(live.box);
        }
        Gizmos.matrix = Matrix4x4.identity;
        Gizmos.color = ContactColor;
        Gizmos.DrawSphere(live.point, Mathf.Max(MarkerSize(live), 0.04f));
        Gizmos.color = NormalColor;
        Vector3 arrowEnd = live.point + live.normal * ArrowLength(live);
        Gizmos.DrawLine(live.point, arrowEnd);
        Gizmos.DrawSphere(arrowEnd, MarkerSize(live) * 0.65f);
        if (drawSphere)
            DrawSphere(live);
    }

    void DrawSphere(LiveContact live)
    {
        if (!TrySphere(live, out Vector3 center, out float radius))
            return;

        Gizmos.matrix = Matrix4x4.identity;
        Gizmos.color = SphereColor;
        Gizmos.DrawWireSphere(center, radius);
    }

    void DrawGameContactMesh()
    {
        if (!showInGameView || (!HasHit && LiveContactCount == 0 && (!keepHitCollidersVisible || keptOrder.Count == 0)))
        {
            return;
        }

        PruneDeadContacts();

        EnsureContactMesh();
        if (contactMesh == null || hitLineMaterial == null)
            return;

        meshVertices.Clear();
        meshColors.Clear();
        faceVertices.Clear();
        faceColors.Clear();
        faceTriangles.Clear();

        if (keepHitCollidersVisible)
            AppendKeptHits();

        if (isolateCurrentContact && LiveContactCount > 0)
        {
            LiveContact isolated = SelectedLiveContact();
            if (isolated != null && isolated.box != null)
                AppendContactLines(isolated);
        }
        else
        {
            for (int i = 0; i < liveOrder.Count; i++)
            {
                if (liveContacts.TryGetValue(liveOrder[i], out LiveContact live) && live.box != null)
                    AppendContactLines(live);
            }
        }

        if (faceVertices.Count >= 4)
        {
            EnsureFaceMesh();
            if (faceMesh != null && faceMaterial != null)
            {
                faceMesh.Clear();
                faceMesh.SetVertices(faceVertices);
                faceMesh.SetColors(faceColors);
                faceMesh.SetTriangles(faceTriangles, 0);
                faceMesh.RecalculateBounds();
                Graphics.DrawMesh(faceMesh, Matrix4x4.identity, faceMaterial, 0);
            }
        }

        if (meshVertices.Count < 2)
            return;

        contactMesh.Clear();
        contactMesh.SetVertices(meshVertices);
        contactMesh.SetColors(meshColors);
        int[] indices = new int[meshVertices.Count];
        for (int i = 0; i < indices.Length; i++)
            indices[i] = i;
        contactMesh.SetIndices(indices, MeshTopology.Lines, 0);
        hitLineMaterial.SetColor("_Color", Color.white);
        hitLineMaterial.renderQueue = 4001;
        Graphics.DrawMesh(contactMesh, Matrix4x4.identity, hitLineMaterial, 0);
    }

    void AppendContactLines(LiveContact live)
    {
        if (!IsKept(live.box))
        {
            AppendBoxLines(live.box, LiveColor);
            AppendContactFace(live);
        }
        float marker = MarkerSize(live);
        AppendCross(live.point, marker, ContactColor);
        Vector3 end = live.point + live.normal * ArrowLength(live);
        AddLine(live.point, end, NormalColor);
        AppendCross(end, marker * 0.7f, NormalColor);
        if (TrySphere(live, out Vector3 center, out float radius))
            AppendWireSphere(center, radius, SphereColor);
    }

    void AppendContactFace(LiveContact live)
    {
        if (live == null)
            return;
        AppendFaceQuad(live.box, live.face);
    }

    void AppendFaceQuad(BoxCollider box, BoxFaceInfo face)
    {
        if (box == null || !face.valid || !TryFaceCorners(box, face, out Vector3 a, out Vector3 b, out Vector3 c, out Vector3 d))
            return;

        int start = faceVertices.Count;
        faceVertices.Add(a);
        faceVertices.Add(b);
        faceVertices.Add(c);
        faceVertices.Add(d);
        faceColors.Add(ContactFaceColor);
        faceColors.Add(ContactFaceColor);
        faceColors.Add(ContactFaceColor);
        faceColors.Add(ContactFaceColor);
        for (int i = 0; i < FaceTriangles.Length; i++)
            faceTriangles.Add(start + FaceTriangles[i]);
    }

    void RememberHit(BoxCollider box, BoxFaceInfo face)
    {
        if (!keepHitCollidersVisible || box == null)
            return;

        ContactKey id = new ContactKey(box);
        if (!keptHits.TryGetValue(id, out KeptCollider kept))
        {
            kept = new KeptCollider();
            kept.box = box;
            keptHits.Add(id, kept);
            keptOrder.Add(id);
        }

        kept.box = box;
        if (!face.valid || HasKeptFace(kept, face))
            return;
        kept.faces.Add(face);
    }

    static bool HasKeptFace(KeptCollider kept, BoxFaceInfo face)
    {
        for (int i = 0; i < kept.faces.Count; i++)
        {
            if (kept.faces[i].axis == face.axis && kept.faces[i].sign == face.sign)
                return true;
        }
        return false;
    }

    bool IsKept(BoxCollider box)
    {
        return keepHitCollidersVisible && box != null && keptHits.ContainsKey(new ContactKey(box));
    }

    void AppendKeptHits()
    {
        for (int i = keptOrder.Count - 1; i >= 0; i--)
        {
            ContactKey id = keptOrder[i];
            if (!keptHits.TryGetValue(id, out KeptCollider kept) || kept.box == null)
            {
                keptHits.Remove(id);
                keptOrder.RemoveAt(i);
                continue;
            }

            AppendBoxLines(kept.box, LiveColor);
            for (int faceIndex = 0; faceIndex < kept.faces.Count; faceIndex++)
                AppendFaceQuad(kept.box, kept.faces[faceIndex]);
        }
    }

    void DrawKeptHitGizmos()
    {
        if (!keepHitCollidersVisible || keptOrder.Count == 0)
            return;

        keptGizmoVertices.Clear();
        keptGizmoTriangles.Clear();
        for (int i = keptOrder.Count - 1; i >= 0; i--)
        {
            ContactKey id = keptOrder[i];
            if (!keptHits.TryGetValue(id, out KeptCollider kept) || kept.box == null)
            {
                keptHits.Remove(id);
                keptOrder.RemoveAt(i);
                continue;
            }

            Gizmos.color = LiveColor;
            DrawWire(kept.box);
            for (int faceIndex = 0; faceIndex < kept.faces.Count; faceIndex++)
                AppendKeptGizmoFace(kept.box, kept.faces[faceIndex]);
        }

        Gizmos.matrix = Matrix4x4.identity;
        if (keptGizmoVertices.Count < 4)
            return;

        if (gizmoFaceMesh == null)
        {
            gizmoFaceMesh = new Mesh();
            gizmoFaceMesh.name = "CollisionDebugFaceGizmo";
            gizmoFaceMesh.hideFlags = HideFlags.HideAndDontSave;
        }

        gizmoFaceMesh.Clear();
        gizmoFaceMesh.SetVertices(keptGizmoVertices);
        gizmoFaceMesh.SetTriangles(keptGizmoTriangles, 0);
        gizmoFaceMesh.RecalculateBounds();
        Gizmos.color = ContactFaceColor;
        Gizmos.DrawMesh(gizmoFaceMesh);
    }

    void AppendKeptGizmoFace(BoxCollider box, BoxFaceInfo face)
    {
        if (!face.valid || !TryFaceCorners(box, face, out Vector3 a, out Vector3 b, out Vector3 c, out Vector3 d))
            return;

        int start = keptGizmoVertices.Count;
        keptGizmoVertices.Add(a);
        keptGizmoVertices.Add(b);
        keptGizmoVertices.Add(c);
        keptGizmoVertices.Add(d);
        for (int i = 0; i < FaceTriangles.Length; i++)
            keptGizmoTriangles.Add(start + FaceTriangles[i]);
    }

    void DrawContactFaceGizmo(LiveContact live)
    {
        if (live == null || !live.face.valid || !TryFaceCorners(live.box, live.face, out Vector3 a, out Vector3 b, out Vector3 c, out Vector3 d))
            return;

        if (gizmoFaceMesh == null)
        {
            gizmoFaceMesh = new Mesh();
            gizmoFaceMesh.name = "CollisionDebugFaceGizmo";
            gizmoFaceMesh.hideFlags = HideFlags.HideAndDontSave;
        }

        gizmoQuad[0] = a;
        gizmoQuad[1] = b;
        gizmoQuad[2] = c;
        gizmoQuad[3] = d;
        gizmoFaceMesh.Clear();
        gizmoFaceMesh.vertices = gizmoQuad;
        gizmoFaceMesh.SetTriangles(FaceTriangles, 0);
        gizmoFaceMesh.RecalculateBounds();
        Gizmos.matrix = Matrix4x4.identity;
        Gizmos.color = ContactFaceColor;
        Gizmos.DrawMesh(gizmoFaceMesh);
    }

    void AppendBoxLines(BoxCollider box, Color color)
    {
        if (box == null)
            return;

        Vector3 center = box.center;
        Vector3 half = box.size * 0.5f;
        Transform target = box.transform;
        Vector3[] corners =
        {
            target.TransformPoint(center + new Vector3(-half.x, -half.y, -half.z)),
            target.TransformPoint(center + new Vector3(half.x, -half.y, -half.z)),
            target.TransformPoint(center + new Vector3(-half.x, half.y, -half.z)),
            target.TransformPoint(center + new Vector3(half.x, half.y, -half.z)),
            target.TransformPoint(center + new Vector3(-half.x, -half.y, half.z)),
            target.TransformPoint(center + new Vector3(half.x, -half.y, half.z)),
            target.TransformPoint(center + new Vector3(-half.x, half.y, half.z)),
            target.TransformPoint(center + new Vector3(half.x, half.y, half.z))
        };
        for (int edge = 0; edge < HitEdges.Length; edge += 2)
            AddLine(corners[HitEdges[edge]], corners[HitEdges[edge + 1]], color);
    }

    void AppendWireSphere(Vector3 center, float radius, Color color)
    {
        const int segments = 24;
        AppendCircle(center, Vector3.right, Vector3.up, radius, segments, color);
        AppendCircle(center, Vector3.forward, Vector3.up, radius, segments, color);
        AppendCircle(center, Vector3.right, Vector3.forward, radius, segments, color);
    }

    void AppendCircle(Vector3 center, Vector3 axisA, Vector3 axisB, float radius, int segments, Color color)
    {
        Vector3 previous = center + axisA * radius;
        for (int i = 1; i <= segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            Vector3 next = center + (axisA * Mathf.Cos(angle) + axisB * Mathf.Sin(angle)) * radius;
            AddLine(previous, next, color);
            previous = next;
        }
    }

    void AppendCross(Vector3 point, float size, Color color)
    {
        AddLine(point - Vector3.right * size, point + Vector3.right * size, color);
        AddLine(point - Vector3.up * size, point + Vector3.up * size, color);
        AddLine(point - Vector3.forward * size, point + Vector3.forward * size, color);
    }

    void AddLine(Vector3 a, Vector3 b, Color color)
    {
        meshVertices.Add(a);
        meshVertices.Add(b);
        meshColors.Add(color);
        meshColors.Add(color);
    }

    void EnsureContactMesh()
    {
        if (hitLineMaterial == null)
            EnsureHitLines();
        if (contactMesh != null)
            return;

        contactMesh = new Mesh();
        contactMesh.name = "CollisionDebugContacts";
        contactMesh.hideFlags = HideFlags.HideAndDontSave;
    }

    void EnsureFaceMesh()
    {
        if (hitLineMaterial == null)
            EnsureHitLines();
        if (faceMesh == null)
        {
            faceMesh = new Mesh();
            faceMesh.name = "CollisionDebugContactFace";
            faceMesh.hideFlags = HideFlags.HideAndDontSave;
        }

        if (faceMaterial != null || hitLineMaterial == null)
            return;

        faceMaterial = new Material(hitLineMaterial.shader);
        faceMaterial.hideFlags = HideFlags.HideAndDontSave;
        faceMaterial.renderQueue = 4000;
        faceMaterial.SetColor("_Color", Color.white);
    }

    void ApplyDebugPause()
    {
        if (!Application.isPlaying || !enableCollisionDebugMode)
        {
            ReleaseDebugPause();
            return;
        }

        if (!debugPause)
        {
            ReleaseDebugPause();
            return;
        }

        if (!pausedByDebug)
        {
            savedTimeScale = Time.timeScale;
            pausedByDebug = true;
        }

        Time.timeScale = 0f;
    }

    public void ReleaseDebugPause()
    {
        if (!pausedByDebug)
            return;

        Time.timeScale = savedTimeScale;
        pausedByDebug = false;
    }


    void PruneDeadContacts()
    {
        for (int i = liveOrder.Count - 1; i >= 0; i--)
        {
            ContactKey id = liveOrder[i];
            if (!liveContacts.TryGetValue(id, out LiveContact live) || live.box == null)
            {
                liveContacts.Remove(id);
                liveOrder.RemoveAt(i);
            }
        }
    }

    LiveContact SelectedLiveContact()
    {
        if (liveOrder.Count == 0)
            return null;
        int index = Mathf.Clamp(isolateContactIndex, 0, liveOrder.Count - 1);
        liveContacts.TryGetValue(liveOrder[index], out LiveContact live);
        return live;
    }

    static bool TrySphere(LiveContact live, out Vector3 center, out float radius)
    {
        center = Vector3.zero;
        radius = 0f;
        if (live == null || live.sphere == null)
            return false;

        center = live.sphere.transform.TransformPoint(live.sphere.center);
        Vector3 scale = live.sphere.transform.lossyScale;
        float scaleMax = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        radius = live.sphere.radius * scaleMax;
        return radius > 0f;
    }


    static float MarkerSize(LiveContact live)
    {
        if (live != null && live.box != null)
            return Mathf.Max(live.box.size.magnitude * 0.04f, 0.05f);
        return 0.08f;
    }

    static float ArrowLength(LiveContact live)
    {
        if (TrySphere(live, out _, out float radius))
            return Mathf.Max(radius * 2f, 0.25f);
        return 0.4f;
    }


    readonly struct ContactKey : System.IEquatable<ContactKey>
    {
#if UNITY_6000_0_OR_NEWER
        readonly EntityId id;

        public ContactKey(Object target)
        {
            id = target.GetEntityId();
        }

        public bool Equals(ContactKey other)
        {
            return id == other.id;
        }

        public override int GetHashCode()
        {
            return id.GetHashCode();
        }
#else
        readonly int id;

        public ContactKey(Object target)
        {
            id = target.GetInstanceID();
        }

        public bool Equals(ContactKey other)
        {
            return id == other.id;
        }

        public override int GetHashCode()
        {
            return id;
        }
#endif

        public override bool Equals(object obj)
        {
            return obj is ContactKey other && Equals(other);
        }
    }

    static BoxFaceInfo ClassifyFace(BoxCollider box, Vector3 worldPoint, Vector3 worldNormal)
    {
        BoxFaceInfo face = new BoxFaceInfo();
        if (box == null)
            return face;

        Vector3 half = box.size * 0.5f;
        half.x = Mathf.Max(half.x, 1e-6f);
        half.y = Mathf.Max(half.y, 1e-6f);
        half.z = Mathf.Max(half.z, 1e-6f);

        Transform target = box.transform;
        Vector3 delta = target.InverseTransformPoint(worldPoint) - box.center;
        float slackX = half.x - Mathf.Abs(delta.x);
        float slackY = half.y - Mathf.Abs(delta.y);
        float slackZ = half.z - Mathf.Abs(delta.z);
        int pointAxis = 0;
        float pointSlack = slackX;
        if (slackY < pointSlack)
        {
            pointAxis = 1;
            pointSlack = slackY;
        }
        if (slackZ < pointSlack)
        {
            pointAxis = 2;
            pointSlack = slackZ;
        }

        float pointValue = pointAxis == 0 ? delta.x : pointAxis == 1 ? delta.y : delta.z;
        int pointSign = pointValue >= 0f ? 1 : -1;

        Vector3 localNormal = target.worldToLocalMatrix.transpose.MultiplyVector(worldNormal);
        bool normalValid = localNormal.sqrMagnitude > 1e-10f;
        int axis = pointAxis;
        int sign = pointSign;
        bool ambiguous = !normalValid;
        if (normalValid)
        {
            localNormal.Normalize();
            float absX = Mathf.Abs(localNormal.x);
            float absY = Mathf.Abs(localNormal.y);
            float absZ = Mathf.Abs(localNormal.z);
            axis = 0;
            float best = absX;
            if (absY > best)
            {
                axis = 1;
                best = absY;
            }
            if (absZ > best)
            {
                axis = 2;
                best = absZ;
            }

            float second = axis == 0 ? Mathf.Max(absY, absZ) : axis == 1 ? Mathf.Max(absX, absZ) : Mathf.Max(absX, absY);
            float normalValue = axis == 0 ? localNormal.x : axis == 1 ? localNormal.y : localNormal.z;
            sign = normalValue >= 0f ? 1 : -1;
            if (best <= 1e-6f || second > best * 0.65f || axis != pointAxis || sign != pointSign)
                ambiguous = true;
        }

        float secondSlack = pointAxis == 0 ? Mathf.Min(slackY, slackZ) : pointAxis == 1 ? Mathf.Min(slackX, slackZ) : Mathf.Min(slackX, slackY);
        float edgeBand = 0.12f * Mathf.Min(half.x, Mathf.Min(half.y, half.z));
        if (pointSlack <= edgeBand && secondSlack <= edgeBand)
            ambiguous = true;

        face.axis = axis;
        face.sign = sign;
        face.ambiguous = ambiguous;
        face.valid = true;
        return face;
    }

    static bool TryFaceCorners(BoxCollider box, BoxFaceInfo face, out Vector3 a, out Vector3 b, out Vector3 c, out Vector3 d)
    {
        a = b = c = d = Vector3.zero;
        if (box == null || !face.valid)
            return false;

        Vector3 half = box.size * 0.5f;
        Vector3 normal = Vector3.zero;
        Vector3 tangentU = Vector3.zero;
        Vector3 tangentV = Vector3.zero;
        if (face.axis == 0)
        {
            normal = new Vector3(half.x * face.sign, 0f, 0f);
            tangentU = new Vector3(0f, half.y, 0f);
            tangentV = new Vector3(0f, 0f, half.z);
        }
        else if (face.axis == 1)
        {
            normal = new Vector3(0f, half.y * face.sign, 0f);
            tangentU = new Vector3(half.x, 0f, 0f);
            tangentV = new Vector3(0f, 0f, half.z);
        }
        else
        {
            normal = new Vector3(0f, 0f, half.z * face.sign);
            tangentU = new Vector3(half.x, 0f, 0f);
            tangentV = new Vector3(0f, half.y, 0f);
        }

        Transform target = box.transform;
        Vector3 origin = target.TransformPoint(box.center + normal);
        Vector3 axisU = target.TransformVector(tangentU);
        Vector3 axisV = target.TransformVector(tangentV);
        a = origin - axisU - axisV;
        b = origin + axisU - axisV;
        c = origin + axisU + axisV;
        d = origin - axisU + axisV;
        return true;
    }

    static string FaceLabel(BoxFaceInfo face)
    {
        string name;
        string axis;
        if (face.axis == 0)
        {
            name = face.sign > 0 ? "오른쪽" : "왼쪽";
            axis = "X";
        }
        else if (face.axis == 1)
        {
            name = face.sign > 0 ? "위" : "아래";
            axis = "Y";
        }
        else
        {
            name = face.sign > 0 ? "앞" : "뒤";
            axis = "Z";
        }

        string label = name + " (" + (face.sign > 0 ? "+" : "-") + axis + ")";
        if (face.ambiguous)
            label += " 모호(모서리/꼭짓점)";
        return label;
    }

    static void LogContact(LiveContact live)
    {
        if (live == null || live.box == null)
            return;

        Vector3 velocity = live.body != null ? BodyVelocity(live.body) : Vector3.zero;
        Debug.Log(
            "[CollisionDebug] " + live.box.name
            + " / " + live.kindName
            + "  면 " + FaceLabel(live.face)
            + "  점 " + live.point.ToString("F3")
            + "  법선 " + live.normal.ToString("F3")
            + "  속도 " + velocity.ToString("F3"),
            live.box);
    }

    static string KindNameOf(BoxCollider box)
    {
        ColliderShapeMarker marker = box.GetComponent<ColliderShapeMarker>();
        if (marker == null)
            return ParentKindName(box.transform);
        return KindName(marker.kind);
    }

    static Vector3 BodyVelocity(Rigidbody body)
    {
#if UNITY_6000_0_OR_NEWER
        return body.linearVelocity;
#else
        return body.velocity;
#endif
    }
}
