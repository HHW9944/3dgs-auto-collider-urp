using System.IO;
using System.Diagnostics;
using UnityEngine;
using System.Text;

public static class GaussianPerfLogger
{
    private static string logPath = Application.dataPath + "/GaussianPhysicsStats.csv";

    public static void LogStats(string fileName, int originalPoints, int colliderCount, long durationMs)
    {
        try
        {
            bool exists = File.Exists(logPath);
            using (StreamWriter sw = new StreamWriter(logPath, true, Encoding.UTF8))
            {
                if (!exists)
                    sw.WriteLine("FileName,OriginalPoints,ColliderCount,ReductionRate(%),ProcessingTime(ms)");

                float reduction = (1f - ((float)colliderCount / (float)originalPoints)) * 100f;
                sw.WriteLine($"{fileName},{originalPoints},{colliderCount},{reduction:F2},{durationMs}");
            }
            // 로그가 호출되었음을 확실히 표시
            UnityEngine.Debug.Log($"[Performance Success] {fileName}: {durationMs}ms, Collider Count: {colliderCount}");
        }
        catch (System.Exception e)
        {
            // 파일 쓰기 권한이나 경로 문제 발생 시 에러 출력
            UnityEngine.Debug.LogError($"[Performance Error] 로그 저장 실패: {e.Message}");
        }
    }
}
