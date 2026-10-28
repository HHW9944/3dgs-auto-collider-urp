using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(CollisionDebugMode))]
public class CollisionDebugModeEditor : UnityEditor.Editor
{
    static GUIStyle labelStyle;

    [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected | GizmoType.Active)]
    static void DrawHitLabel(CollisionDebugMode mode, GizmoType gizmoType)
    {
        if (mode == null || !mode.enableCollisionDebugMode || !mode.ShouldDrawHit || string.IsNullOrEmpty(mode.HitLabel))
            return;

        if (labelStyle == null)
        {
            labelStyle = new GUIStyle(EditorStyles.boldLabel);
            labelStyle.normal.textColor = new Color(1f, 0.95f, 0.4f);
            labelStyle.fontSize = 12;
        }

        Handles.Label(mode.HitPoint, mode.HitLabel, labelStyle);
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.HelpBox(
            "Enable Collision Debug Mode가 꺼져 있으면 충돌 추적, 디버그 와이어, 로그, OrangeFallback 실험을 하지 않습니다. 기존 Collider Gizmo는 GaussianPhysicsObject의 Draw Collider Mode Gizmos 설정을 그대로 따릅니다. 디버그 와이어만 보려면 그 옵션을 끄면 됩니다.",
            MessageType.Info);

        EditorGUILayout.PropertyField(serializedObject.FindProperty("enableCollisionDebugMode"), new GUIContent("Enable Collision Debug Mode"));
        bool debugEnabled = serializedObject.FindProperty("enableCollisionDebugMode").boolValue;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("그룹 표시", EditorStyles.boldLabel);
        EditorGUI.BeginDisabledGroup(!debugEnabled);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("showCyanPca"), new GUIContent("Show CyanPca"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("showGreenNeighbor"), new GUIContent("Show GreenNeighbor"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("showOrangeFallback"), new GUIContent("Show OrangeFallback"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("showOnlyLastHitCollider"), new GUIContent("Show Only Last Hit Collider"));
        if (serializedObject.FindProperty("showOnlyLastHitCollider").boolValue)
            EditorGUILayout.HelpBox("Show Only Last Hit Collider가 켜져 있으면 그룹 와이어는 그리지 않습니다.", MessageType.None);
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("충돌 추적", EditorStyles.boldLabel);
        EditorGUI.BeginDisabledGroup(!debugEnabled);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("ball"), new GUIContent("Ball Rigidbody"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("showInGameView"), new GUIContent("Show In Game View"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("highlightSeconds"), new GUIContent("Highlight Seconds"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("keepHitCollidersVisible"), new GUIContent("Keep Hit Colliders Visible"));
        if (GUILayout.Button("Clear Hit Collider History"))
        {
            serializedObject.ApplyModifiedProperties();
            for (int i = 0; i < targets.Length; i++)
            {
                CollisionDebugMode debugMode = targets[i] as CollisionDebugMode;
                if (debugMode != null)
                    debugMode.ClearHitColliderHistory();
            }
        }
        EditorGUILayout.PropertyField(serializedObject.FindProperty("isolateCurrentContact"), new GUIContent("Isolate Current Contact"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("isolateContactIndex"), new GUIContent("Isolate Contact Index"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("debugPause"), new GUIContent("Debug Pause"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("physicsObject"));
        EditorGUILayout.HelpBox(
            "Ball이 비어 있으면 Play 중에 생성된 공 Rigidbody를 자동으로 추적합니다. Show Only Last Hit Collider가 켜져 있으면 Cyan/Green/Orange 체크와 관계없이 충돌한 박스만 표시됩니다.",
            MessageType.None);
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("실험", EditorStyles.boldLabel);
        EditorGUI.BeginDisabledGroup(!debugEnabled);
        EditorGUILayout.PropertyField(
            serializedObject.FindProperty("disableOrangeFallbackPhysics"),
            new GUIContent("Disable OrangeFallback Physics (Experimental)"));
        EditorGUILayout.HelpBox(
            "Play Mode에서만 OrangeFallback 자식 BoxCollider.enabled를 끄고, 해제하거나 Debug Mode를 끄면 실험 전 값으로 되돌립니다.",
            MessageType.None);
        EditorGUI.EndDisabledGroup();

        serializedObject.ApplyModifiedProperties();

        CollisionDebugMode mode = (CollisionDebugMode)target;
        if (!debugEnabled || !mode.HasHit)
            return;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("마지막 충돌", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("기록", mode.HitLabel);
    }

    void OnEnable()
    {
        EditorApplication.update += RepaintWhileDebugging;
    }

    void OnDisable()
    {
        EditorApplication.update -= RepaintWhileDebugging;
    }

    void RepaintWhileDebugging()
    {
        CollisionDebugMode mode = target as CollisionDebugMode;
        if (mode != null && mode.enableCollisionDebugMode && Application.isPlaying)
            Repaint();
    }
}
