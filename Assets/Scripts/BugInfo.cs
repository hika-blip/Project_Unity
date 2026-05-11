using UnityEngine;

/// <summary>
/// 虫1体分の情報を保持するコンポーネント
/// ・表示名
/// ・管理用ID（CSV・ログ用）
/// を一元管理する
/// </summary>
[DisallowMultipleComponent]
public class BugInfo : MonoBehaviour
{
    [Header("基本情報")]

    [Tooltip("虫の一意な管理ID（CSV・ログ用）")]
    public int bugId;

    [Tooltip("画面表示用の虫の名前")]
    public string bugName;

    [Header("デバッグ用")]
    [SerializeField]
    private bool showDebugLog = false;

    /// <summary>
    /// 初期化時チェック
    /// </summary>
    private void Awake()
    {
        if (string.IsNullOrEmpty(bugName))
        {
            bugName = gameObject.name;
        }

        if (showDebugLog)
        {
            Debug.Log($"[BugInfo] 初期化: ID={bugId}, Name={bugName}, Object={gameObject.name}");
        }
    }

    /// <summary>
    /// CSV出力用の1行文字列を生成
    /// </summary>
    public string ToCsvRow(float timeStamp)
    {
        return $"{timeStamp},{bugId},{bugName}";
    }
}
