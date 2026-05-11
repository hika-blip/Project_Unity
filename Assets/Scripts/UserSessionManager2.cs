using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// ユーザーセッション情報を管理し、1つのCSVファイルとして出力する
/// ユーザーIDごとにデータを紐付けて管理
/// </summary>
public class UserSessionManager : MonoBehaviour
{
    public static UserSessionManager Instance { get; private set; }

    // 現在のユーザー情報
    public string CurrentUserID { get; private set; } = "";
    public bool IsUserActive => !string.IsNullOrEmpty(CurrentUserID);

    // 全セッション記録（全ユーザー分を1つのリストで管理）
    private List<SessionData> allSessions = new List<SessionData>();
    private SessionData currentSession = null;

    // シーン滞在時間の追跡用
    private string currentSceneName = "";
    private DateTime currentSceneEnterTime;

    // 出力設定
    [Header("データ出力設定")]
    [Tooltip("保存先の絶対パス。空欄の場合はApplication.persistentDataPathを使用")]
    public string customSavePath = "";
    public string outputFolderName = "SessionData";
    public string outputFileNamePrefix = "user_sessions"; // ファイル名の接頭辞（日時が自動付与される）
    [Tooltip("自動保存の間隔（秒）。0で無効")]
    public float autoSaveInterval = 0f; // デフォルトで無効（アプリ終了時のみ保存）
    [Tooltip("アプリケーション終了時のみCSV保存")]
    public bool saveOnlyOnQuit = true;

    [Header("セッション終了設定")]
    [Tooltip("このシーンに到達したらセッションを自動終了")]
    public string sessionEndSceneName = "ending";
    [Tooltip("セッション終了時に自動でCSV保存（saveOnlyOnQuitがfalseの場合のみ有効）")]
    public bool autoSaveOnSessionEnd = false;

    [Header("特定シーン滞在時間の記録")]
    [Tooltip("滞在時間を記録したいシーン名のリスト")]
    public List<string> trackingSceneNames = new List<string> { "Forest", "tutorial" };

    [Header("チュートリアル時間の記録")]
    [Tooltip("TutorialManagerからPathFollowerまでの時間を記録")]
    public bool recordTutorialTime = true;
    private float tutorialStartTime = -1f; // TutorialManager開始時刻
    private float tutorialEndTime = -1f;   // PathFollower開始時刻

    [Header("ゾーン選択の記録")]
    [Tooltip("ゾーン選択履歴を記録")]
    public bool recordZoneSelection = true;

    private float autoSaveTimer = 0f;
    private DateTime applicationStartTime; // アプリケーション開始時刻

    /// <summary>
    /// 保存先のベースパスを取得
    /// </summary>
    private string GetBasePath()
    {
        // カスタムパスが設定されている場合はそれを使用
        if (!string.IsNullOrEmpty(customSavePath))
        {
            return customSavePath;
        }
        // デフォルトはpersistentDataPath
        return Application.persistentDataPath;
    }

    void Awake()
    {
        // シングルトンパターン
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);

            // アプリケーション開始時刻を記録
            applicationStartTime = DateTime.Now;

            string basePath = GetBasePath();
            string fullPath = Path.Combine(basePath, outputFolderName);

            Debug.Log("=== UserSessionManager 初期化 ===");
            Debug.Log($"アプリケーション開始時刻: {applicationStartTime:yyyy-MM-dd HH:mm:ss}");
            Debug.Log($"ベースパス: {basePath}");
            Debug.Log($"保存先フォルダ: {fullPath}");
            Debug.Log($"ファイル名接頭辞: {outputFileNamePrefix}");
            Debug.Log($"セッション終了シーン: {sessionEndSceneName}");
            Debug.Log($"滞在時間記録対象シーン: {string.Join(", ", trackingSceneNames)}");
            Debug.Log($"保存モード: {(saveOnlyOnQuit ? "アプリ終了時のみ" : "通常モード")}");
            Debug.Log($"ゾーン選択記録: {(recordZoneSelection ? "有効" : "無効")}");
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void OnEnable()
    {
        // シーン読み込み時のイベントを登録
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDisable()
    {
        // イベントを解除
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    /// <summary>
    /// シーン読み込み時の処理
    /// </summary>
    private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        Debug.Log($"=== シーン読み込み ===");
        Debug.Log($"シーン名: {scene.name}");
        Debug.Log($"セッション終了シーン: {sessionEndSceneName}");
        Debug.Log($"現在のセッション: {(currentSession != null ? currentSession.UserID : "なし")}");

        // 前のシーンからの遷移を記録
        if (!string.IsNullOrEmpty(currentSceneName) && currentSession != null)
        {
            RecordSceneTransition(currentSceneName, scene.name);
        }

        // 現在のシーン情報を更新
        currentSceneName = scene.name;
        currentSceneEnterTime = DateTime.Now;

        // endingシーンに到達したらセッション終了
        if (scene.name.ToLower() == sessionEndSceneName.ToLower())
        {
            Debug.Log($"*** {sessionEndSceneName}シーン到達を検出 ***");

            if (currentSession != null)
            {
                Debug.Log($"セッションを終了します (UserID: {currentSession.UserID})");
                EndCurrentSession();

                // 自動保存が有効で、かつsaveOnlyOnQuitがfalseの場合のみ保存
                if (autoSaveOnSessionEnd && !saveOnlyOnQuit)
                {
                    Debug.Log("CSV自動保存を実行します");
                    ExportToCSV(endCurrentSession: false);
                }
                else
                {
                    Debug.Log("saveOnlyOnQuit=trueのため、CSV保存はスキップします（アプリ終了時に保存されます）");
                }
            }
            else
            {
                Debug.LogWarning($"{sessionEndSceneName}シーンに到達しましたが、アクティブなセッションがありません！");
            }
        }
    }

    void Update()
    {
        // saveOnlyOnQuitがtrueの場合は自動保存を無効化
        if (saveOnlyOnQuit)
            return;

        // 自動保存処理
        if (autoSaveInterval > 0 && currentSession != null)
        {
            autoSaveTimer += Time.deltaTime;
            if (autoSaveTimer >= autoSaveInterval)
            {
                autoSaveTimer = 0f;
                ExportToCSV();
            }
        }
    }

    /// <summary>
    /// 新しいユーザーセッションを開始（IDが変わった時も対応）
    /// </summary>
    public void StartNewSession(string userID)
    {
        if (string.IsNullOrEmpty(userID))
        {
            Debug.LogWarning("ユーザーIDが空です。セッションを開始できません。");
            return;
        }

        // 前のセッションがあれば終了
        if (currentSession != null)
        {
            Debug.Log($"[StartNewSession] 前のセッションを終了します: UserID={currentSession.UserID}");
            EndCurrentSession();
        }

        // 新しいユーザーIDに切り替え
        CurrentUserID = userID;

        // 新しいセッションを開始
        currentSession = new SessionData
        {
            UserID = userID,
            SessionStartTime = DateTime.Now,
            SceneTransitions = new List<SceneTransition>(),
            CustomEvents = new List<CustomEvent>(),
            SceneStayDurations = new Dictionary<string, double>(),
            ZoneSelections = new List<ZoneSelection>(),
            TutorialDuration = 0f
        };

        // チュートリアル時間をリセット
        tutorialStartTime = -1f;
        tutorialEndTime = -1f;

        // 現在のシーン情報を記録
        currentSceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        currentSceneEnterTime = DateTime.Now;

        Debug.Log($"=== セッション開始 ===");
        Debug.Log($"UserID: {userID}");
        Debug.Log($"開始時刻: {currentSession.SessionStartTime:yyyy-MM-dd HH:mm:ss.fff}");
        Debug.Log($"総セッション数: {allSessions.Count}");
        Debug.Log($"現在のシーン: {currentSceneName}");
    }

    /// <summary>
    /// ユーザーIDを変更（自動的に前のセッションを終了し、新しいセッションを開始）
    /// </summary>
    public void ChangeUserID(string newUserID)
    {
        if (CurrentUserID == newUserID)
        {
            Debug.Log($"同じユーザーIDです: {newUserID}");
            return;
        }

        Debug.Log($"ユーザーID変更: {CurrentUserID} → {newUserID}");
        StartNewSession(newUserID);
    }

    /// <summary>
    /// 現在のセッションを終了
    /// </summary>
    public void EndCurrentSession()
    {
        if (currentSession != null)
        {
            // 最後のシーンの滞在時間を記録
            if (!string.IsNullOrEmpty(currentSceneName))
            {
                double stayTime = (DateTime.Now - currentSceneEnterTime).TotalSeconds;
                AddSceneStayDuration(currentSceneName, stayTime);
            }

            // チュートリアル時間を記録
            if (tutorialStartTime >= 0 && tutorialEndTime >= 0)
            {
                currentSession.TutorialDuration = tutorialEndTime - tutorialStartTime;
            }

            currentSession.SessionEndTime = DateTime.Now;
            currentSession.TotalPlayTimeSeconds =
                (currentSession.SessionEndTime - currentSession.SessionStartTime).TotalSeconds;

            // 全セッションリストに追加
            allSessions.Add(currentSession);

            Debug.Log($"=== セッション終了 ===");
            Debug.Log($"UserID: {currentSession.UserID}");
            Debug.Log($"開始時刻: {currentSession.SessionStartTime:yyyy-MM-dd HH:mm:ss.fff}");
            Debug.Log($"終了時刻: {currentSession.SessionEndTime:yyyy-MM-dd HH:mm:ss.fff}");
            Debug.Log($"プレイ時間: {currentSession.TotalPlayTimeSeconds:F2}秒 ({currentSession.TotalPlayTimeSeconds / 60:F2}分)");
            Debug.Log($"総セッション数: {allSessions.Count}");
            Debug.Log($"ゾーン選択回数: {currentSession.ZoneSelections.Count}");

            // チュートリアル時間をログ出力
            if (currentSession.TutorialDuration > 0)
            {
                Debug.Log($"チュートリアル時間: {currentSession.TutorialDuration:F2}秒");
            }

            // 追跡対象シーンの滞在時間をログ出力
            foreach (string sceneName in trackingSceneNames)
            {
                if (currentSession.SceneStayDurations.ContainsKey(sceneName))
                {
                    Debug.Log($"{sceneName}シーン滞在時間: {currentSession.SceneStayDurations[sceneName]:F2}秒");
                }
            }

            // ゾーン選択履歴をログ出力
            if (currentSession.ZoneSelections.Count > 0)
            {
                Debug.Log("=== ゾーン選択履歴 ===");
                for (int i = 0; i < currentSession.ZoneSelections.Count; i++)
                {
                    var selection = currentSession.ZoneSelections[i];
                    Debug.Log($"  {i + 1}回目: Zone {selection.ZoneNumber} ({selection.SelectionTime:HH:mm:ss})");
                }
            }

            Debug.Log($"呼び出し元: {new System.Diagnostics.StackTrace().GetFrame(1).GetMethod().Name}");

            currentSession = null;
            CurrentUserID = "";
            currentSceneName = "";
            tutorialStartTime = -1f;
            tutorialEndTime = -1f;
        }
        else
        {
            Debug.LogWarning("EndCurrentSession: 終了するセッションがありません");
        }
    }

    /// <summary>
    /// シーン滞在時間を累積
    /// </summary>
    private void AddSceneStayDuration(string sceneName, double seconds)
    {
        if (currentSession == null || string.IsNullOrEmpty(sceneName))
            return;

        if (currentSession.SceneStayDurations.ContainsKey(sceneName))
        {
            currentSession.SceneStayDurations[sceneName] += seconds;
        }
        else
        {
            currentSession.SceneStayDurations[sceneName] = seconds;
        }

        Debug.Log($"[{CurrentUserID}] {sceneName}シーン滞在時間追加: {seconds:F2}秒 (累計: {currentSession.SceneStayDurations[sceneName]:F2}秒)");
    }

    /// <summary>
    /// シーン遷移を記録
    /// </summary>
    public void RecordSceneTransition(string fromScene, string toScene)
    {
        if (currentSession != null)
        {
            DateTime transitionTime = DateTime.Now;

            // 前のシーンの滞在時間を計算
            if (!string.IsNullOrEmpty(fromScene) && fromScene == currentSceneName)
            {
                double stayTime = (transitionTime - currentSceneEnterTime).TotalSeconds;
                AddSceneStayDuration(fromScene, stayTime);
            }

            currentSession.SceneTransitions.Add(new SceneTransition
            {
                FromScene = fromScene,
                ToScene = toScene,
                TransitionTime = transitionTime
            });

            Debug.Log($"[{CurrentUserID}] シーン遷移記録: {fromScene} → {toScene}");

            // 新しいシーンの開始時刻を記録
            currentSceneName = toScene;
            currentSceneEnterTime = transitionTime;

            // 遷移先がendingシーンの場合はセッション終了
            if (toScene.ToLower() == sessionEndSceneName.ToLower())
            {
                Debug.Log($"=== {sessionEndSceneName}シーン到達（遷移記録経由） ===");
                Debug.Log("セッションを終了します");

                EndCurrentSession();

                if (autoSaveOnSessionEnd && !saveOnlyOnQuit)
                {
                    Debug.Log("CSV自動保存を実行します");
                    ExportToCSV(endCurrentSession: false);
                }
                else
                {
                    Debug.Log("saveOnlyOnQuit=trueのため、CSV保存はスキップします（アプリ終了時に保存されます）");
                }
            }
        }
        else
        {
            Debug.LogWarning("セッションが開始されていません。シーン遷移を記録できません。");
        }
    }

    /// <summary>
    /// ゾーン選択を記録
    /// </summary>
    public void RecordZoneSelection(int zoneNumber)
    {
        if (!recordZoneSelection) return;

        if (currentSession != null)
        {
            if (currentSession.ZoneSelections == null)
            {
                currentSession.ZoneSelections = new List<ZoneSelection>();
            }

            int selectionCount = currentSession.ZoneSelections.Count + 1;

            currentSession.ZoneSelections.Add(new ZoneSelection
            {
                ZoneNumber = zoneNumber,
                SelectionTime = DateTime.Now,
                SelectionOrder = selectionCount
            });

            Debug.Log($"[{CurrentUserID}] ゾーン選択記録: Zone {zoneNumber} ({selectionCount}回目の選択)");

            // カスタムイベントとしても記録
            RecordCustomEvent("ZoneSelected", $"Zone={zoneNumber},Order={selectionCount}");
        }
        else
        {
            Debug.LogWarning("セッションが開始されていません。ゾーン選択を記録できません。");
        }
    }

    /// <summary>
    /// 現在のセッションのゾーン選択回数を取得
    /// </summary>
    public int GetZoneSelectionCount()
    {
        if (currentSession != null && currentSession.ZoneSelections != null)
        {
            return currentSession.ZoneSelections.Count;
        }
        return 0;
    }

    /// <summary>
    /// 現在のセッションの最後に選択されたゾーンを取得
    /// </summary>
    public int GetLastSelectedZone()
    {
        if (currentSession != null && currentSession.ZoneSelections != null && currentSession.ZoneSelections.Count > 0)
        {
            return currentSession.ZoneSelections[currentSession.ZoneSelections.Count - 1].ZoneNumber;
        }
        return -1;
    }

    /// <summary>
    /// TutorialManager開始時刻を記録
    /// </summary>
    public void RecordTutorialStart()
    {
        if (currentSession != null && recordTutorialTime)
        {
            tutorialStartTime = Time.time;
            Debug.Log($"[{CurrentUserID}] TutorialManager開始時刻を記録: {tutorialStartTime:F2}秒");
            RecordCustomEvent("TutorialManagerStart", $"Time={tutorialStartTime:F2}");
        }
    }

    /// <summary>
    /// PathFollower開始時刻を記録（チュートリアル終了）
    /// </summary>
    public void RecordTutorialEnd()
    {
        if (currentSession != null && recordTutorialTime && tutorialStartTime >= 0)
        {
            tutorialEndTime = Time.time;
            float tutorialDuration = tutorialEndTime - tutorialStartTime;

            Debug.Log($"[{CurrentUserID}] PathFollower開始時刻を記録: {tutorialEndTime:F2}秒");
            Debug.Log($"[{CurrentUserID}] チュートリアル所要時間: {tutorialDuration:F2}秒");

            RecordCustomEvent("PathFollowerStart", $"Time={tutorialEndTime:F2},TutorialDuration={tutorialDuration:F2}");
        }
    }

    /// <summary>
    /// 現在のセッションのチュートリアル時間を取得
    /// </summary>
    public float GetTutorialDuration()
    {
        if (tutorialStartTime >= 0 && tutorialEndTime >= 0)
        {
            return tutorialEndTime - tutorialStartTime;
        }
        return 0f;
    }

    public void RecordCustomEvent(string eventName, string eventData = "")
    {
        if (currentSession != null)
        {
            if (currentSession.CustomEvents == null)
            {
                currentSession.CustomEvents = new List<CustomEvent>();
            }

            currentSession.CustomEvents.Add(new CustomEvent
            {
                EventName = eventName,
                EventData = eventData,
                EventTime = DateTime.Now
            });

            Debug.Log($"[{CurrentUserID}] カスタムイベント記録: {eventName} - {eventData}");
        }
        else
        {
            Debug.LogWarning("セッションが開始されていません。イベントを記録できません。");
        }
    }

    /// <summary>
    /// 全セッションデータを1つのCSVファイルに出力
    /// </summary>
    /// <param name="endCurrentSession">現在のセッションを終了するか（デフォルト: false）</param>
    public void ExportToCSV(bool endCurrentSession = false)
    {
        Debug.Log($"ExportToCSV開始 - 現在のセッション数: {allSessions.Count}");
        Debug.Log($"現在のセッションを終了: {endCurrentSession}");

        // endCurrentSessionがtrueの場合のみセッションを終了
        if (endCurrentSession && currentSession != null)
        {
            Debug.Log($"アクティブなセッションを終了: {CurrentUserID}");
            EndCurrentSession();
        }

        if (allSessions.Count == 0)
        {
            Debug.LogWarning("出力するセッションデータがありません。StartNewSession()でセッションを開始してください。");
            return;
        }

        try
        {
            // 出力フォルダのパスを作成（ビルド後も書き込み可能な場所）
            string basePath = GetBasePath();
            string folderPath = Path.Combine(basePath, outputFolderName);

            Debug.Log($"=== CSV出力処理開始 ===");
            Debug.Log($"ベースパス: {basePath}");
            Debug.Log($"フォルダパス: {folderPath}");

            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
                Debug.Log($"フォルダを作成しました: {folderPath}");
            }
            else
            {
                Debug.Log($"フォルダは既に存在します: {folderPath}");
            }

            // ファイルパス（アプリケーション開始時刻を使用）
            string timestamp = applicationStartTime.ToString("yyyyMMdd_HHmmss");
            string fileName = $"{outputFileNamePrefix}_{timestamp}.csv";
            string filePath = Path.Combine(folderPath, fileName);
            Debug.Log($"ファイルパス: {filePath}");
            Debug.Log($"アプリケーション開始時刻: {applicationStartTime:yyyy-MM-dd HH:mm:ss}");

            // CSVデータを作成
            StringBuilder csv = new StringBuilder();

            // ヘッダー行（追跡対象シーンの滞在時間列を動的に追加）
            StringBuilder header = new StringBuilder();
            header.Append("UserID,SessionStart,SessionEnd,TotalPlayTime(sec),TutorialTime(sec)");
            foreach (string sceneName in trackingSceneNames)
            {
                header.Append($",{sceneName}StayTime(sec)");
            }
            header.Append(",ZoneSelectionCount,ZoneSelections,SceneTransitions,CustomEvents");
            csv.AppendLine(header.ToString());

            // データ行（全ユーザーのセッションを時系列で出力）
            foreach (var session in allSessions)
            {
                string userID = EscapeCSV(session.UserID);
                string startTime = session.SessionStartTime.ToString("yyyy-MM-dd HH:mm:ss");
                string endTime = session.SessionEndTime.ToString("yyyy-MM-dd HH:mm:ss");
                string playTime = session.TotalPlayTimeSeconds.ToString("F2");
                string tutorialTime = session.TutorialDuration.ToString("F2");

                StringBuilder dataRow = new StringBuilder();
                dataRow.Append($"{userID},{startTime},{endTime},{playTime},{tutorialTime}");

                // 各追跡対象シーンの滞在時間を取得
                foreach (string sceneName in trackingSceneNames)
                {
                    string stayTime = "0.00";
                    if (session.SceneStayDurations != null && session.SceneStayDurations.ContainsKey(sceneName))
                    {
                        stayTime = session.SceneStayDurations[sceneName].ToString("F2");
                    }
                    dataRow.Append($",{stayTime}");
                }

                // ゾーン選択回数
                int zoneSelectionCount = 0;
                if (session.ZoneSelections != null)
                {
                    zoneSelectionCount = session.ZoneSelections.Count;
                }
                dataRow.Append($",{zoneSelectionCount}");

                // ゾーン選択情報を文字列化
                string zoneSelections = "";
                if (session.ZoneSelections != null && session.ZoneSelections.Count > 0)
                {
                    List<string> selections = new List<string>();
                    foreach (var selection in session.ZoneSelections)
                    {
                        selections.Add($"Zone{selection.ZoneNumber}({selection.SelectionTime:HH:mm:ss})");
                    }
                    zoneSelections = string.Join("; ", selections);
                }
                zoneSelections = EscapeCSV(zoneSelections);

                // シーン遷移情報を文字列化
                string sceneTransitions = "";
                if (session.SceneTransitions != null && session.SceneTransitions.Count > 0)
                {
                    List<string> transitions = new List<string>();
                    foreach (var transition in session.SceneTransitions)
                    {
                        transitions.Add($"{transition.FromScene}→{transition.ToScene}({transition.TransitionTime:HH:mm:ss})");
                    }
                    sceneTransitions = string.Join("; ", transitions);
                }
                sceneTransitions = EscapeCSV(sceneTransitions);

                // カスタムイベント情報を文字列化
                string customEvents = "";
                if (session.CustomEvents != null && session.CustomEvents.Count > 0)
                {
                    List<string> events = new List<string>();
                    foreach (var evt in session.CustomEvents)
                    {
                        events.Add($"{evt.EventName}:{evt.EventData}({evt.EventTime:HH:mm:ss})");
                    }
                    customEvents = string.Join("; ", events);
                }
                customEvents = EscapeCSV(customEvents);

                dataRow.Append($",{zoneSelections},{sceneTransitions},{customEvents}");
                csv.AppendLine(dataRow.ToString());
            }

            // ファイルに書き込み（UTF-8 BOM付きで保存してExcelで文字化けを防ぐ）
            Debug.Log($"CSVデータサイズ: {csv.Length} 文字");
            Debug.Log($"書き込み先: {filePath}");

            File.WriteAllText(filePath, csv.ToString(), new UTF8Encoding(true));

            // ファイルが実際に作成されたか確認
            if (File.Exists(filePath))
            {
                FileInfo fileInfo = new FileInfo(filePath);
                Debug.Log($"✓ ファイル作成成功!");
                Debug.Log($"✓ ファイルサイズ: {fileInfo.Length} バイト");
                Debug.Log($"✓ 作成日時: {fileInfo.CreationTime}");
                Debug.Log($"✓ 完全パス: {filePath}");
            }
            else
            {
                Debug.LogError($"✗ ファイルが作成されませんでした: {filePath}");
            }

            Debug.Log($"=== CSV出力完了 ===");
            Debug.Log($"合計セッション数: {allSessions.Count}");

            // ユニークなユーザー数をカウント
            HashSet<string> uniqueUsers = new HashSet<string>();
            foreach (var session in allSessions)
            {
                uniqueUsers.Add(session.UserID);
            }
            Debug.Log($"ユニークユーザー数: {uniqueUsers.Count}");
        }
        catch (Exception e)
        {
            Debug.LogError($"CSVファイル出力エラー: {e.Message}");
        }
    }

    /// <summary>
    /// CSV用に文字列をエスケープ
    /// </summary>
    private string EscapeCSV(string text)
    {
        if (string.IsNullOrEmpty(text))
            return "";

        if (text.Contains(",") || text.Contains("\"") || text.Contains("\n"))
        {
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }
        return text;
    }

    /// <summary>
    /// 特定ユーザーのセッション数を取得
    /// </summary>
    public int GetUserSessionCount(string userID)
    {
        int count = 0;
        foreach (var session in allSessions)
        {
            if (session.UserID == userID)
                count++;
        }
        return count;
    }

    /// <summary>
    /// 特定ユーザーの特定シーン滞在時間の合計を取得
    /// </summary>
    public double GetUserSceneStayTime(string userID, string sceneName)
    {
        double totalTime = 0;
        foreach (var session in allSessions)
        {
            if (session.UserID == userID && session.SceneStayDurations != null &&
                session.SceneStayDurations.ContainsKey(sceneName))
            {
                totalTime += session.SceneStayDurations[sceneName];
            }
        }
        return totalTime;
    }

    void OnApplicationQuit()
    {
        Debug.Log("=== アプリケーション終了 ===");
        Debug.Log($"記録されたセッション数: {allSessions.Count}");
        Debug.Log($"アクティブなセッション: {(currentSession != null ? currentSession.UserID : "なし")}");

        // アプリケーション終了時にデータを出力
        // 現在のセッションを終了してから保存
        ExportToCSV(endCurrentSession: true);
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Debug.Log("=== UserSessionManager破棄 ===");
            ExportToCSV(endCurrentSession: true);
        }
    }
}

/// <summary>
/// セッションデータ構造
/// </summary>
[System.Serializable]
public class SessionData
{
    public string UserID;
    public DateTime SessionStartTime;
    public DateTime SessionEndTime;
    public double TotalPlayTimeSeconds;
    public List<SceneTransition> SceneTransitions;
    public List<CustomEvent> CustomEvents;
    public Dictionary<string, double> SceneStayDurations; // 各シーンの滞在時間（秒）
    public List<ZoneSelection> ZoneSelections; // ゾーン選択履歴
    public float TutorialDuration; // チュートリアル時間（秒）
}

/// <summary>
/// シーン遷移情報
/// </summary>
[System.Serializable]
public class SceneTransition
{
    public string FromScene;
    public string ToScene;
    public DateTime TransitionTime;
}

/// <summary>
/// カスタムイベント情報
/// </summary>
[System.Serializable]
public class CustomEvent
{
    public string EventName;
    public string EventData;
    public DateTime EventTime;
}

/// <summary>
/// ゾーン選択情報
/// </summary>
[System.Serializable]
public class ZoneSelection
{
    public int ZoneNumber; // 選択されたゾーン番号（1〜7）
    public DateTime SelectionTime; // 選択時刻
    public int SelectionOrder; // 何回目の選択か（1始まり）
}