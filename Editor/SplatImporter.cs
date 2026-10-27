using System.IO;
using UnityEditor.AssetImporters;
using UnityEngine;
using GsPhysicsToolkit.Core;

namespace GsPhysicsToolkit.EditorTools
{
    // 유니티에게 .ply 확장자를 가진 파일을 import
    [ScriptedImporter(1, "ply")]
    public class SplatImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            // 가우시안 데이터를 담을 ScriptableObject 인스턴스 생성
            var data = ScriptableObject.CreateInstance<SplatData>();
            data.name = Path.GetFileNameWithoutExtension(ctx.assetPath);

            // 기존에 작성해두신 바이너리 파싱 함수 호출하여 배열 데이터 채우기
            data.LoadFromFile(ctx.assetPath);

            // 유니티 프로젝트 내부에서 사용할 수 있는 에셋 오브젝트로 최종 등록
            ctx.AddObjectToAsset("Data", data);
            ctx.SetMainObject(data);
        }
    }
}
