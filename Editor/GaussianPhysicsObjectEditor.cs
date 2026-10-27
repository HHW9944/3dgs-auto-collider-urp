using GaussianSplatting.Runtime;
using GsPhysicsToolkit.Core;
using GsPhysicsToolkit.Physics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[CustomEditor(typeof(GaussianPhysicsObject))]
public class GaussianPhysicsObjectEditor : UnityEditor.Editor
{
    const string DensityShaderPath = "Packages/com.3dgs.physics-toolkit/Shaders/DensityKernel.compute";
    const string ShaderSplatsGuid = "ed800126ae8844a67aad1974ddddd59c";
    const string ShaderCompositeGuid = "7e184af7d01193a408eb916d8acafff9";
    const string ShaderDebugPointsGuid = "b44409fc67214394f8f47e4e2648425e";
    const string ShaderDebugBoxesGuid = "4006f2680fd7c8b4cbcb881454c782be";
    const string SplatUtilitiesGuid = "ec84f78b836bd4f96a105d6b804f08bd";

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.HelpBox(
            "기본 Collider Mode는 Legacy입니다. Surface는 수평 보정을 적용하지 않고, 평면성이 충분한 칸만 회전 박스로 만듭니다. 렌더러 Local Scale은 (-1, -1, -1)입니다.",
            MessageType.Info);

        GaussianPhysicsObject physicsObject = (GaussianPhysicsObject)target;
        EditorGUILayout.LabelField("추정 법선", physicsObject.estimatedNormal.ToString("F3"));
        EditorGUILayout.LabelField("지면 기울기", $"{physicsObject.tiltDegrees:F1}°");
        EditorGUILayout.LabelField("지지 비율", physicsObject.supportRatio.ToString("F3"));
        EditorGUILayout.LabelField("보정 Euler", physicsObject.appliedEuler.ToString("F1"));
        EditorGUILayout.LabelField("보정 적용", physicsObject.levelingApplied ? "예" : "아니오");

        if (GUILayout.Button("Hierarchy 구성 및 Collider 생성"))
            PrepareAndGenerate(physicsObject);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Apply Leveling"))
        {
            Undo.RecordObject(physicsObject.transform, "Apply Leveling");
            Undo.RecordObject(physicsObject, "Apply Leveling");
            physicsObject.ApplyLeveling(true);
            MarkEdited(physicsObject);
        }
        if (GUILayout.Button("Reset Leveling"))
        {
            Undo.RecordObject(physicsObject.transform, "Reset Leveling");
            Undo.RecordObject(physicsObject, "Reset Leveling");
            physicsObject.ResetLeveling();
            MarkEdited(physicsObject);
        }
        EditorGUILayout.EndHorizontal();

        if (GUILayout.Button("Apply Manual Euler"))
        {
            Undo.RecordObject(physicsObject.transform, "Apply Manual Leveling");
            Undo.RecordObject(physicsObject, "Apply Manual Leveling");
            physicsObject.ApplyManualLeveling(true);
            MarkEdited(physicsObject);
        }

        if (GUILayout.Button("정렬 검증"))
        {
            Undo.RecordObject(physicsObject, "Validate Alignment");
            physicsObject.ValidateAlignment();
            MarkEdited(physicsObject);
            SceneView.RepaintAll();
        }
    }

    public static GameObject CreateLinkedObject(
        SplatData splatData,
        GaussianSplatAsset gaussianAsset,
        bool autoLevel,
        GaussianPhysicsObject.ColliderGenerationMode colliderMode = GaussianPhysicsObject.ColliderGenerationMode.Legacy)
    {
        GameObject root = new GameObject($"GaussianPhysicsObject_{splatData.name}");
        GaussianPhysicsObject physicsObject = root.AddComponent<GaussianPhysicsObject>();
        physicsObject.splatData = splatData;
        physicsObject.gaussianAsset = gaussianAsset;
        physicsObject.colliderMode = colliderMode;
        physicsObject.autoLevel = colliderMode == GaussianPhysicsObject.ColliderGenerationMode.Legacy && autoLevel;
        PrepareAndGenerate(physicsObject);
        return root;
    }

    public static void PrepareAndGenerate(GaussianPhysicsObject physicsObject)
    {
        physicsObject.EnsureHierarchy();
        AssignDensityShader(physicsObject.density);
        EnsureRendererResources(physicsObject.splatRenderer);
        physicsObject.BuildAndGenerate();

        EditorUtility.SetDirty(physicsObject);
        if (physicsObject.splatRenderer != null)
            EditorUtility.SetDirty(physicsObject.splatRenderer);
        if (physicsObject.density != null)
            EditorUtility.SetDirty(physicsObject.density);
        MarkEdited(physicsObject);
    }

    static void MarkEdited(GaussianPhysicsObject physicsObject)
    {
        EditorUtility.SetDirty(physicsObject);
        EditorUtility.SetDirty(physicsObject.transform);
        EditorSceneManager.MarkSceneDirty(physicsObject.gameObject.scene);
    }

    public static void AssignDensityShader(DensityCompute density)
    {
        if (density == null || density.densityShader != null)
            return;

        density.densityShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(DensityShaderPath);
    }

    public static void EnsureRendererResources(GaussianSplatRenderer renderer)
    {
        if (renderer == null)
            return;

        renderer.m_ShaderSplats ??= Load<Shader>(ShaderSplatsGuid);
        renderer.m_ShaderComposite ??= Load<Shader>(ShaderCompositeGuid);
        renderer.m_ShaderDebugPoints ??= Load<Shader>(ShaderDebugPointsGuid);
        renderer.m_ShaderDebugBoxes ??= Load<Shader>(ShaderDebugBoxesGuid);
        renderer.m_CSSplatUtilities ??= Load<ComputeShader>(SplatUtilitiesGuid);
    }

    static T Load<T>(string guid) where T : Object
    {
        string path = AssetDatabase.GUIDToAssetPath(guid);
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset == null)
            Debug.LogError($"[GaussianPhysicsObject] 패키지 리소스를 찾지 못했습니다. GUID {guid}");
        return asset;
    }
}
