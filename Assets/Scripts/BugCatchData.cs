using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 虫の獲得状態を管理するstaticクラス
/// シーンをまたいでデータを保持します
/// </summary>
public static class BugCatchData
{
    // 各虫の獲得状態 (true = 獲得済み, false = 未獲得)
    private static Dictionary<int, bool> caughtStatus = new Dictionary<int, bool>();

    // 総虫数
    private static int totalBugCount = 0;

    /// <summary>
    /// 初期化（ゲーム開始時に呼び出す）
    /// </summary>
    public static void Initialize(int bugCount)
    {
        totalBugCount = bugCount;
        caughtStatus.Clear();

        for (int i = 0; i < bugCount; i++)
        {
            caughtStatus[i] = false;
        }

        Debug.Log($"BugCatchData初期化: 総虫数 {bugCount}");
    }

    /// <summary>
    /// 虫を獲得済みにする
    /// </summary>
    public static void SetCaught(int bugIndex)
    {
        if (caughtStatus.ContainsKey(bugIndex))
        {
            caughtStatus[bugIndex] = true;
            Debug.Log($"虫 #{bugIndex} を獲得済みに設定");
        }
    }

    /// <summary>
    /// 特定の虫が獲得済みかチェック
    /// </summary>
    public static bool IsCaught(int bugIndex)
    {
        if (caughtStatus.ContainsKey(bugIndex))
        {
            return caughtStatus[bugIndex];
        }
        return false;
    }

    /// <summary>
    /// 獲得済みの虫の数を取得
    /// </summary>
    public static int GetCaughtCount()
    {
        int count = 0;
        foreach (var status in caughtStatus.Values)
        {
            if (status) count++;
        }
        return count;
    }

    /// <summary>
    /// 総虫数を取得
    /// </summary>
    public static int GetTotalCount()
    {
        return totalBugCount;
    }

    /// <summary>
    /// 全ての獲得状態をリセット
    /// </summary>
    public static void Reset()
    {
        foreach (var key in new List<int>(caughtStatus.Keys))
        {
            caughtStatus[key] = false;
        }
        Debug.Log("全ての獲得状態をリセット");
    }

    /// <summary>
    /// デバッグ用：現在の状態を文字列で取得
    /// </summary>
    public static string GetStatusString()
    {
        string status = $"獲得状況: {GetCaughtCount()}/{GetTotalCount()}\n";
        for (int i = 0; i < totalBugCount; i++)
        {
            status += $"虫 #{i}: {(IsCaught(i) ? "獲得済み" : "未獲得")}\n";
        }
        return status;
    }
}