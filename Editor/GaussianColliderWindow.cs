using GaussianSplatting.Runtime;
using UnityEditor;
using UnityEngine;
using GsPhysicsToolkit.Core;
using GsPhysicsToolkit.Physics;

public class GaussianColliderWindow : EditorWindow
{
    private SplatData targetSplatData;
    private GaussianSplatAsset gaussianAsset;
    private bool useArasRenderer;
    private bool autoLevelOnCreate = true;
    private GaussianPhysicsObject.ColliderGenerationMode colliderMode = GaussianPhysicsObject.ColliderGenerationMode.Legacy;

    [MenuItem("Gaussian Tool/Collider Generator")]
    public static void ShowWindow() => GetWindow<GaussianColliderWindow>("Gaussian Physics Tool");

    private void OnGUI()
    {
        targetSplatData = (SplatData)EditorGUILayout.ObjectField("Splat Data", targetSplatData, typeof(SplatData), false);
        useArasRenderer = EditorGUILayout.Toggle("aras-p 렌더러 사용", useArasRenderer);
        if (useArasRenderer)
        {
            gaussianAsset = (GaussianSplatAsset)EditorGUILayout.ObjectField("Gaussian Splat Asset", gaussianAsset, typeof(GaussianSplatAsset), false);
            colliderMode = (GaussianPhysicsObject.ColliderGenerationMode)EditorGUILayout.EnumPopup("Collider 모드", colliderMode);
            if (colliderMode == GaussianPhysicsObject.ColliderGenerationMode.Legacy)
            {
                autoLevelOnCreate = EditorGUILayout.Toggle("생성 시 자동 수평 보정", autoLevelOnCreate);
            }
            else
            {
                EditorGUILayout.HelpBox("Surface 모드는 자동 수평 보정을 적용하지 않습니다. 원본 PLY 기울기를 유지합니다.", MessageType.Info);
            }
            EditorGUILayout.HelpBox(
                "같은 PLY로 만든 SplatData와 GaussianSplatAsset을 연결합니다. 렌더러 Local Scale은 (-1, -1, -1)이고 Collider는 SplatData 좌표를 그대로 사용합니다.",
                MessageType.Info);
        }

        if (GUILayout.Button("가우시안 오브젝트 생성 및 물리 적용"))
        {
            if (targetSplatData == null) return;

            if (useArasRenderer)
            {
                if (gaussianAsset == null)
                {
                    Debug.LogWarning("[GaussianColliderWindow] Gaussian Splat Asset을 지정해야 합니다.");
                    return;
                }

                GameObject linked = GaussianPhysicsObjectEditor.CreateLinkedObject(targetSplatData, gaussianAsset, autoLevelOnCreate, colliderMode);
                Selection.activeGameObject = linked;
                return;
            }

            // 1. 새 오브젝트 생성
            GameObject go = new GameObject($"GS_{targetSplatData.name}");

            // 2. 필수 컴포넌트 추가
            var renderer = go.AddComponent<GaussianSplat>();
            var density = go.AddComponent<DensityCompute>();
            var physicsObj = go.AddComponent<GaussianObject>();

            // 3. 에셋 자동 할당 (프로젝트 경로를 본인의 실제 경로와 일치시키세요)
            renderer.splatMaterial = AssetDatabase.LoadAssetAtPath<Material>("Packages/com.3dgs.physics-toolkit/Runtime/Rendering/GaussianMat.mat");
            density.densityShader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/com.3dgs.physics-toolkit/Shaders/DensityKernel.compute");

            // 4. 데이터 연결
            renderer.data = targetSplatData;
            physicsObj.splatData = targetSplatData;

            // 5. 콜라이더 루트 생성 및 물리 생성 실행
            GameObject colliderRoot = new GameObject("Gaussian_Colliders_Root");
            colliderRoot.transform.SetParent(go.transform);
            density.collidersParent = colliderRoot.transform;

            density.GenerateColliders(targetSplatData, go.transform);

            Selection.activeGameObject = go;
        }
    }
}
