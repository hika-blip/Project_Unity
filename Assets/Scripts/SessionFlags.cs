using UnityEngine;

/// <summary>
/// 1回のセッション（QRコードを読んでから終了まで）の中だけで使うフラグ
/// ・UserSessionManager.StartNewSession() で毎回リセットされる
///   → 同じ子が2回遊んでも、新しいセッションごとにクイズが1回出る
/// </summary>
public static class SessionFlags
{
    /// <summary>このセッションでクイズシーンを開いたか（開いた時点でtrue）</summary>
    public static bool QuizDone = false;

    /// <summary>このセッションで昆虫を1回以上観察したか（選択画面で昆虫を決定した時点でtrue）</summary>
    public static bool HasObserved = false;

    public static void Reset()
    {
        QuizDone = false;
        HasObserved = false;
        Debug.Log("[SessionFlags] リセットしました（QuizDone=false, HasObserved=false）");
    }
}
