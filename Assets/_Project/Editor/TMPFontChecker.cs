#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using TMPro;

/// <summary>
/// 씬 내 Font Asset이 누락된 TMP 텍스트를 모두 찾아 콘솔에 출력하는 에디터 유틸리티
/// </summary>
public class TMPFontChecker
{
    [MenuItem("Tools/TMP Font 누락 검사")]
    static void CheckMissingFonts()
    {
        var allTMP = Object.FindObjectsByType<TextMeshProUGUI>(FindObjectsSortMode.None);
        int count = 0;

        foreach (var tmp in allTMP)
        {
            if (tmp.font == null)
            {
                Debug.LogError($"[폰트 누락] {GetPath(tmp.gameObject)}", tmp.gameObject);
                count++;
            }
        }

        Debug.Log(count == 0
            ? "모든 TMP 텍스트에 폰트가 정상 할당되어 있습니다."
            : $"총 {count}개의 TMP 텍스트에서 폰트가 누락되었습니다.");
    }

    static string GetPath(GameObject go)
    {
        string path = go.name;
        Transform t = go.transform.parent;
        while (t != null) { path = t.name + "/" + path; t = t.parent; }
        return path;
    }
}
#endif