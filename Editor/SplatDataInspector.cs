using UnityEditor;
using GsPhysicsToolkit.Core;

namespace GsPhysicsToolkit.EditorTools
{
    // SplatData 에셋의 인스펙터 화면을 커스텀 정의
    [CustomEditor(typeof(SplatData))]
    public class SplatDataInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var data = target as SplatData;

            // 인스펙터 창에 가우시안 총 개수를 표시
            EditorGUILayout.LabelField("Gaussian Count", data.Count.ToString("N0"));
        }
    }
}
