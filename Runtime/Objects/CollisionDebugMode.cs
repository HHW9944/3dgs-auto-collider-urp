using System.Collections.Generic;
using GsPhysicsToolkit.Physics;
using UnityEngine;

[DisallowMultipleComponent]
[AddComponentMenu("3DGS/Collision Debug Mode")]
public partial class CollisionDebugMode : MonoBehaviour
{
    const string CyanName = "CyanPca";
    const string GreenName = "GreenNeighbor";
    const string OrangeName = "OrangeFallback";

    static readonly Color CyanColor = new Color(0.1f, 0.85f, 1f, 0.95f);
    static readonly Color GreenColor = new Color(0.15f, 0.95f, 0.35f, 0.95f);
    static readonly Color OrangeColor = new Color(1f, 0.55f, 0.1f, 0.85f);
    static readonly Color HighlightColor = new Color(1f, 0.92f, 0.16f, 1f);
    static readonly Color ContactColor = new Color(1f, 0.2f, 0.15f, 1f);

    [Tooltip("켜야 충돌 추적, 디버그 와이어, 로그, OrangeFallback 실험이 동작합니다.")]
    public bool enableCollisionDebugMode;

    [Tooltip("Scene 뷰에 CyanPca 와이어를 그립니다. Collider.enabled는 바꾸지 않습니다.")]
    public bool showCyanPca;

    [Tooltip("Scene 뷰에 GreenNeighbor 와이어를 그립니다. Collider.enabled는 바꾸지 않습니다.")]
    public bool showGreenNeighbor;

    [Tooltip("Scene 뷰에 OrangeFallback 와이어를 그립니다. Collider.enabled는 바꾸지 않습니다.")]
    public bool showOrangeFallback;

    [Tooltip("그룹 와이어를 그리지 않고 마지막 충돌 Collider만 표시합니다.")]
    public bool showOnlyLastHitCollider = true;

    [Tooltip("Game 뷰에 마지막 충돌 와이어를 추가로 표시합니다. 3DGS 렌더러 설정은 바꾸지 않습니다.")]
    public bool showInGameView;

    [Min(0f)]
    [Tooltip("공이 박스에서 떠난 뒤 마지막 충돌 박스를 유지하는 시간(초)입니다. Keep Hit Colliders Visible이 켜져 있으면 이 시간과 관계없이 기록이 남습니다.")]
    public float highlightSeconds = 10f;

    [Tooltip("충돌했던 BoxCollider의 모서리와 접촉 면을 공이 떠난 뒤에도 유지합니다.")]
    public bool keepHitCollidersVisible;

    [Tooltip("추적할 공의 Rigidbody입니다. 비어 있으면 충돌을 추적하지 않습니다.")]
    public Rigidbody ball;

    public GaussianPhysicsObject physicsObject;

    [Tooltip("Play Mode에서 OrangeFallback의 BoxCollider.enabled만 잠시 끕니다. Debug Mode를 끄면 원래 값으로 되돌립니다.")]
    public bool disableOrangeFallbackPhysics;

    [Tooltip("현재 접촉 중인 Collider 하나만 보고, 그룹 와이어는 숨깁니다.")]
    public bool isolateCurrentContact;

    [Tooltip("동시에 여러 박스에 닿아 있을 때 단독 검사할 접촉 번호입니다.")]
    public int isolateContactIndex;

    [Tooltip("Play Mode에서 Time.timeScale만 0으로 멈춥니다. Rigidbody 설정은 바꾸지 않습니다.")]
    public bool debugPause;

    [System.NonSerialized] bool debugActive;
    [System.NonSerialized] bool experimentApplied;
    [System.NonSerialized] BoxCollider hitCollider;
    [System.NonSerialized] string hitObjectName;
    [System.NonSerialized] string hitKindName;
    [System.NonSerialized] string hitLabel;
    [System.NonSerialized] ColliderBuildKind hitKind;
    [System.NonSerialized] bool hitKindKnown;
    [System.NonSerialized] Vector3 hitPoint;
    [System.NonSerialized] float hitGameTime;
    [System.NonSerialized] float hitExpireTime;
    [System.NonSerialized] bool hitEnabled;
    [System.NonSerialized] bool hitIsTrigger;
    [System.NonSerialized] int hitLayer;
    [System.NonSerialized] string hitLayerName;
    [System.NonSerialized] Rigidbody boundBall;
    [System.NonSerialized] bool warnedMissingOrange;
    [System.NonSerialized] bool loggedTrackedBody;
    [System.NonSerialized] List<OrangeEnabledState> orangeStates;
    [System.NonSerialized] List<CollisionDebugRelay> relays;
    [System.NonSerialized] GameObject hitLineRoot;
    [System.NonSerialized] LineRenderer[] hitLines;
    [System.NonSerialized] Material hitLineMaterial;

    static readonly int[] HitEdges =
    {
        0, 1, 0, 2, 0, 4,
        1, 3, 1, 5, 2, 3,
        2, 6, 3, 7, 4, 5,
        4, 6, 5, 7, 6, 7
    };

    struct OrangeEnabledState
    {
        public BoxCollider collider;
        public bool wasEnabled;
    }

    public bool HasHit => hitCollider != null;
    public string HitLabel => hitLabel;
    public Vector3 HitPoint => hitPoint;
    public bool IsHighlightFresh => hitCollider != null && Time.unscaledTime <= hitExpireTime;

    public bool ShouldDrawHit
    {
        get
        {
            return enableCollisionDebugMode && hitCollider != null && Time.unscaledTime <= hitExpireTime;
        }
    }

    void OnEnable()
    {
        if (!Application.isPlaying || !enableCollisionDebugMode)
            return;

        if (!debugActive)
            ActivateDebug();
        if (disableOrangeFallbackPhysics && !experimentApplied)
            ApplyOrangeExperiment();
    }

    void Update()
    {
        if (!enableCollisionDebugMode)
        {
            if (debugActive)
                DeactivateDebug();
            return;
        }

        if (!Application.isPlaying)
            return;

        if (!debugActive)
            ActivateDebug();

        if (disableOrangeFallbackPhysics != experimentApplied)
        {
            if (disableOrangeFallbackPhysics)
                ApplyOrangeExperiment();
            else
                RestoreOrange();
        }

        if (hitObjectName != null && hitCollider == null)
            ClearHitRecord();

        RefreshBallRelay();
        ApplyDebugPause();

        if (showInGameView && ShouldDrawHit && !keepHitCollidersVisible)
            DrawRuntimeWire(hitCollider, HighlightColor);
    }

    void LateUpdate()
    {
        if (!enableCollisionDebugMode || !Application.isPlaying)
        {
            HideHitLines();
            return;
        }

        if (ball == null)
            AttachSpawnedRigidbodies();

        UpdateHitLines();
        DrawGameContactMesh();
    }

    void OnDisable()
    {
        if (debugActive || experimentApplied)
            DeactivateDebug();
    }

    void OnDrawGizmos()
    {
        if (!enableCollisionDebugMode)
            return;

        Matrix4x4 previousMatrix = Gizmos.matrix;
        Color previousColor = Gizmos.color;
        DrawSceneGizmos();
        Gizmos.matrix = previousMatrix;
        Gizmos.color = previousColor;
    }

    public void NotifyCollidersRebuilt()
    {
        if (hitObjectName != null && hitCollider == null)
            ClearHitRecord();

        if (!Application.isPlaying || !enableCollisionDebugMode || !disableOrangeFallbackPhysics)
            return;

        ApplyOrangeExperiment();
    }

    public void ReportBallCollision(Collision collision)
    {
        if (!enableCollisionDebugMode || collision == null || collision.contactCount == 0)
            return;

        BoxCollider box = collision.collider as BoxCollider;
        if (box == null || !BelongsToToolkit(box))
            return;

        ContactPoint contact = collision.GetContact(0);
        RecordHit(box, contact.point);
    }

    void ActivateDebug()
    {
        debugActive = true;
        boundBall = null;
        RefreshBallRelay();
    }

    void DeactivateDebug()
    {
        RestoreOrange();
        RemoveRelays();
        DestroyHitLines();
        ClearLiveContacts();
        ClearHitColliderHistory();
        ReleaseDebugPause();
        debugActive = false;
        boundBall = null;
        warnedMissingOrange = false;
        loggedTrackedBody = false;
    }

    void RefreshBallRelay()
    {
        if (!Application.isPlaying || !enableCollisionDebugMode || ball == null)
            return;

        if (ball == boundBall && HasLiveRelay())
            return;

        RemoveRelays();
        boundBall = ball;
        EnsureRelay(ball);
    }

    void AttachSpawnedRigidbodies()
    {
        Rigidbody[] bodies = FindObjectsByType<Rigidbody>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        Transform root = ResolveGeneratedRoot();
        for (int i = 0; i < bodies.Length; i++)
        {
            Rigidbody body = bodies[i];
            if (body == null)
                continue;
            if (root != null && body.transform.IsChildOf(root))
                continue;
            EnsureRelay(body);
        }
    }

    void EnsureRelay(Rigidbody body)
    {
        if (body == null)
            return;

        CollisionDebugRelay relay = body.GetComponent<CollisionDebugRelay>();
        if (relay == null)
            relay = body.gameObject.AddComponent<CollisionDebugRelay>();
        relay.Bind(this);
        if (relays == null)
            relays = new List<CollisionDebugRelay>();
        if (!relays.Contains(relay))
            relays.Add(relay);

        if (!loggedTrackedBody)
        {
            loggedTrackedBody = true;
            Debug.Log("[CollisionDebug] 공 Rigidbody를 추적합니다: " + body.name, body);
        }
    }

    bool HasLiveRelay()
    {
        return relays != null && relays.Count == 1 && relays[0] != null;
    }

    void RemoveRelays()
    {
        if (relays == null)
            return;

        for (int i = relays.Count - 1; i >= 0; i--)
        {
            CollisionDebugRelay relay = relays[i];
            if (relay != null)
                Destroy(relay);
        }

        relays.Clear();
    }

    void RecordHit(BoxCollider box, Vector3 point)
    {
        ColliderShapeMarker marker = box.GetComponent<ColliderShapeMarker>();
        hitCollider = box;
        hitObjectName = box.gameObject.name;
        hitPoint = point;
        hitGameTime = Time.time;
        hitExpireTime = Time.unscaledTime + Mathf.Max(0f, highlightSeconds);
        hitEnabled = box.enabled;
        hitIsTrigger = box.isTrigger;
        hitLayer = box.gameObject.layer;
        hitLayerName = LayerMask.LayerToName(hitLayer);
        if (string.IsNullOrEmpty(hitLayerName))
            hitLayerName = "Unnamed";

        if (marker != null)
        {
            hitKind = marker.kind;
            hitKindKnown = true;
            hitKindName = KindName(marker.kind);
        }
        else
        {
            hitKindKnown = false;
            hitKindName = ParentKindName(box.transform);
        }

        hitLabel = hitObjectName
            + "\n" + hitKindName
            + "\npoint " + hitPoint.ToString("F3")
            + "   t " + hitGameTime.ToString("F2") + "s"
            + "\nenabled " + hitEnabled
            + "   isTrigger " + hitIsTrigger
            + "   layer " + hitLayer + " (" + hitLayerName + ")";
    }

    void ClearHitRecord()
    {
        hitCollider = null;
        hitObjectName = null;
        hitKindName = null;
        hitLabel = null;
        hitKindKnown = false;
        hitExpireTime = 0f;
    }

    void ApplyOrangeExperiment()
    {
        RestoreOrange();
        experimentApplied = true;

        Transform group = FindGroup(OrangeName);
        if (group == null)
        {
            if (!warnedMissingOrange)
            {
                warnedMissingOrange = true;
                Debug.LogWarning("[CollisionDebug] OrangeFallback 그룹이 없습니다. Surface 모드로 생성한 Collider에서만 이 실험을 할 수 있습니다.");
            }
            return;
        }

        if (orangeStates == null)
            orangeStates = new List<OrangeEnabledState>();

        for (int i = 0; i < group.childCount; i++)
        {
            BoxCollider box = group.GetChild(i).GetComponent<BoxCollider>();
            if (box == null)
                continue;

            orangeStates.Add(new OrangeEnabledState { collider = box, wasEnabled = box.enabled });
            box.enabled = false;
        }
    }

    void RestoreOrange()
    {
        if (orangeStates != null)
        {
            for (int i = 0; i < orangeStates.Count; i++)
            {
                BoxCollider box = orangeStates[i].collider;
                if (box != null)
                    box.enabled = orangeStates[i].wasEnabled;
            }

            orangeStates.Clear();
        }

        experimentApplied = false;
    }

    void DrawSceneGizmos()
    {
        if (keepHitCollidersVisible)
            DrawKeptHitGizmos();

        if (isolateCurrentContact && LiveContactCount > 0)
        {
            DrawIsolatedContactGizmo();
            return;
        }

        if (!showOnlyLastHitCollider)
        {
            if (showCyanPca)
                DrawGroup(CyanName, CyanColor);
            if (showGreenNeighbor)
                DrawGroup(GreenName, GreenColor);
            if (showOrangeFallback)
                DrawGroup(OrangeName, OrangeColor);
        }

        DrawLiveContactGizmos();

        if (keepHitCollidersVisible || !ShouldDrawHit || IsLiveContact(hitCollider))
            return;

        Gizmos.color = HighlightColor;
        DrawWire(hitCollider);

        if (!IsHighlightFresh)
            return;

        Gizmos.matrix = Matrix4x4.identity;
        Gizmos.color = ContactColor;
        float radius = Mathf.Max(hitCollider.size.magnitude * 0.08f, 0.05f);
        Gizmos.DrawLine(hitPoint - Vector3.right * radius, hitPoint + Vector3.right * radius);
        Gizmos.DrawLine(hitPoint - Vector3.up * radius, hitPoint + Vector3.up * radius);
        Gizmos.DrawLine(hitPoint - Vector3.forward * radius, hitPoint + Vector3.forward * radius);
    }

    void DrawGroup(string groupName, Color color)
    {
        Transform group = FindGroup(groupName);
        if (group == null)
            return;

        Gizmos.color = color;
        for (int i = 0; i < group.childCount; i++)
        {
            BoxCollider box = group.GetChild(i).GetComponent<BoxCollider>();
            if (box != null)
                DrawWire(box);
        }
    }

    static void DrawWire(BoxCollider box)
    {
        Gizmos.matrix = box.transform.localToWorldMatrix;
        Gizmos.DrawWireCube(box.center, box.size);
    }

    static void DrawRuntimeWire(BoxCollider box, Color color)
    {
        Transform target = box.transform;
        Vector3 center = box.center;
        Vector3 half = box.size * 0.5f;
        Vector3 p000 = target.TransformPoint(center + new Vector3(-half.x, -half.y, -half.z));
        Vector3 p001 = target.TransformPoint(center + new Vector3(-half.x, -half.y, half.z));
        Vector3 p010 = target.TransformPoint(center + new Vector3(-half.x, half.y, -half.z));
        Vector3 p011 = target.TransformPoint(center + new Vector3(-half.x, half.y, half.z));
        Vector3 p100 = target.TransformPoint(center + new Vector3(half.x, -half.y, -half.z));
        Vector3 p101 = target.TransformPoint(center + new Vector3(half.x, -half.y, half.z));
        Vector3 p110 = target.TransformPoint(center + new Vector3(half.x, half.y, -half.z));
        Vector3 p111 = target.TransformPoint(center + new Vector3(half.x, half.y, half.z));
        Debug.DrawLine(p000, p001, color);
        Debug.DrawLine(p001, p011, color);
        Debug.DrawLine(p011, p010, color);
        Debug.DrawLine(p010, p000, color);
        Debug.DrawLine(p100, p101, color);
        Debug.DrawLine(p101, p111, color);
        Debug.DrawLine(p111, p110, color);
        Debug.DrawLine(p110, p100, color);
        Debug.DrawLine(p000, p100, color);
        Debug.DrawLine(p001, p101, color);
        Debug.DrawLine(p010, p110, color);
        Debug.DrawLine(p011, p111, color);
    }

    void UpdateHitLines()
    {
        if (keepHitCollidersVisible || !showInGameView || !ShouldDrawHit || IsLiveContact(hitCollider))
        {
            HideHitLines();
            return;
        }

        EnsureHitLines();
        if (hitLineRoot == null)
            return;

        hitLineRoot.SetActive(true);
        Vector3 center = hitCollider.center;
        Vector3 half = hitCollider.size * 0.5f;
        Transform target = hitCollider.transform;
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

        Color color = IsHighlightFresh ? HighlightColor : new Color(1f, 0.75f, 0.1f, 0.85f);
        if (hitLineMaterial != null)
            hitLineMaterial.SetColor("_Color", color);
        float width = Mathf.Max(hitCollider.size.magnitude * 0.035f, 0.12f);
        for (int edge = 0; edge < hitLines.Length; edge++)
        {
            LineRenderer line = hitLines[edge];
            line.startWidth = width;
            line.endWidth = width;
            line.startColor = color;
            line.endColor = color;
            line.SetPosition(0, corners[HitEdges[edge * 2]]);
            line.SetPosition(1, corners[HitEdges[edge * 2 + 1]]);
        }
    }

    void EnsureHitLines()
    {
        if (hitLineRoot != null)
            return;

        Shader shader = Shader.Find("Hidden/GsPhysicsToolkit/CollisionDebugWire");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");
        if (shader == null)
            return;

        hitLineMaterial = new Material(shader);
        hitLineMaterial.hideFlags = HideFlags.HideAndDontSave;
        hitLineMaterial.renderQueue = 4001;
        hitLineRoot = new GameObject("CollisionDebugHitWire");
        hitLineRoot.hideFlags = HideFlags.HideAndDontSave;
        hitLines = new LineRenderer[12];
        for (int i = 0; i < hitLines.Length; i++)
        {
            GameObject edge = new GameObject("Edge");
            edge.hideFlags = HideFlags.HideAndDontSave;
            edge.transform.SetParent(hitLineRoot.transform, false);
            LineRenderer line = edge.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.useWorldSpace = true;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sharedMaterial = hitLineMaterial;
            hitLines[i] = line;
        }
    }

    void HideHitLines()
    {
        if (hitLineRoot != null)
            hitLineRoot.SetActive(false);
    }

    void DestroyHitLines()
    {
        if (hitLineRoot != null)
            Destroy(hitLineRoot);
        if (hitLineMaterial != null)
            Destroy(hitLineMaterial);
        hitLineRoot = null;
        hitLineMaterial = null;
        hitLines = null;
    }

    Transform FindGroup(string groupName)
    {
        Transform root = ResolveGeneratedRoot();
        if (root == null)
            return null;
        return root.Find(groupName);
    }

    Transform ResolveGeneratedRoot()
    {
        if (physicsObject == null)
            physicsObject = GetComponent<GaussianPhysicsObject>();
        if (physicsObject == null || physicsObject.generatedColliders == null)
            return null;
        return physicsObject.generatedColliders;
    }

    bool BelongsToToolkit(BoxCollider box)
    {
        if (box.GetComponent<ColliderShapeMarker>() != null)
            return true;

        Transform root = ResolveGeneratedRoot();
        return root != null && box.transform.IsChildOf(root);
    }

    static string KindName(ColliderBuildKind kind)
    {
        switch (kind)
        {
            case ColliderBuildKind.Pca:
                return CyanName;
            case ColliderBuildKind.Neighbor:
                return GreenName;
            default:
                return OrangeName;
        }
    }

    static string ParentKindName(Transform boxTransform)
    {
        Transform parent = boxTransform.parent;
        if (parent == null)
            return "Legacy";
        if (parent.name == CyanName || parent.name == GreenName || parent.name == OrangeName)
            return parent.name;
        return "Legacy";
    }
}
