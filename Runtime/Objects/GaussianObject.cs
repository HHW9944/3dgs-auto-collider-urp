using UnityEngine;
using GsPhysicsToolkit.Core;
using GsPhysicsToolkit.Physics;

[RequireComponent(typeof(GaussianSplat))]
[RequireComponent(typeof(DensityCompute))]
public class GaussianObject : MonoBehaviour
{
    public SplatData splatData;

    // [우클릭 메뉴 추가] 씬 뷰에서 우클릭으로 즉시 물리/렌더링 준비
    [ContextMenu("Initialize 3DGS Object")]
    public void Initialize()
    {
        if (splatData == null) return;

        // 1. 렌더러 준비
        var renderer = GetComponent<GaussianSplat>();
        renderer.Initialize(splatData);

        // 2. 물리 준비
        var dc = GetComponent<DensityCompute>();
        dc.GenerateColliders(splatData, transform);

        Debug.Log("3DGS 객체 통합 완료: 물리와 렌더러가 하나로 연결되었습니다.");
    }
}
