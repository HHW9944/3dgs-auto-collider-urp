using UnityEngine;
using GsPhysicsToolkit.Core;

namespace GsPhysicsToolkit.Rendering
{
    public class SplatGizmoDrawer : MonoBehaviour
    {
        [Header("Data Source")]
        public SplatData Data;

        [Header("Visualization Settings")]
        [Range(100, 50000)]
        public int MaxPointsToShow = 10000; // 성능을 위해 화면에 뿌릴 최대 점 개수 제한

        [Range(0.01f, 0.5f)]
        public float Radius = 0.05f;        // 점(구체)들의 크기

        private void OnDrawGizmos()
        {
            // 데이터 에셋이 없거나 안에 파싱된 점이 없으면 리턴
            if (Data == null || Data.Count == 0) return;

            int count = Data.Count;

            // 너무 많은 점을 한 번에 그리면 유니티가 멈추므로 징검다리 건너뛰며 샘플링 연산
            int step = Mathf.Max(1, count / MaxPointsToShow);

            for (int i = 0; i < count; i += step)
            {
                // 배열 범위 안전성 체크
                if (i >= Data.Positions.Length || i >= Data.Colors.Length) break;

                // 오브젝트의 위치와 회전을 반영한 글로벌 좌표 계산
                Vector3 worldPosition = transform.TransformPoint(Data.Positions[i]);

                // 가우시안 고유의 색상 지정 (투명도 포함)
                Gizmos.color = Data.Colors[i];

                // 씬 뷰에 작은 구체 형태로 점 찍기
                Gizmos.DrawSphere(worldPosition, Radius);
            }
        }
    }
}
