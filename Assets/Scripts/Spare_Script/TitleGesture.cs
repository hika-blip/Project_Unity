using UnityEngine;
using UnityEngine.SceneManagement;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Collections;

public class TitleGesture : MonoBehaviour
{
    [Header("UDP Settings")]
    [SerializeField] private int buttonGesturePort = 9997;
    [SerializeField] private string serverIP = "127.0.0.1";

    [Header("Scene Management")]
    [SerializeField] private string fromSceneName = "Title";
    [SerializeField] private string toSceneName = "Forest";
    [SerializeField] private bool allowSceneChangeFromAnyScene = false;

    [Header("Visual Feedback")]
    [SerializeField] private float feedbackDisplayTime = 2.0f;
    [SerializeField] private bool showGestureDetectedMessage = true;

    [Header("Debug")]
    [SerializeField] private bool enableDebugLogs = true;
    [SerializeField] private bool showConnectionStatus = true;

    // UDP関連
    private UdpClient udpClient;
    private Thread udpListenerThread;
    private bool isListening = false;
    private bool shouldStop = false;

    // ジェスチャー処理用
    private bool gestureDetected = false;
    private readonly object gestureDataLock = new object();

    // UI表示用
    private string statusMessage = "";
    private float statusMessageTimer = 0f;
    private bool connectionEstablished = false;

    void Start()
    {
        InitializeUDPListener();
    }

    void Update()
    {
        // メインスレッドでジェスチャー処理
        lock (gestureDataLock)
        {
            if (gestureDetected)
            {
                gestureDetected = false;
                ProcessButtonGesture();
            }
        }

        // ステータスメッセージのタイマー更新
        if (statusMessageTimer > 0)
        {
            statusMessageTimer -= Time.deltaTime;
        }
    }

    /// <summary>
    /// UDP受信の初期化
    /// </summary>
    void InitializeUDPListener()
    {
        try
        {
            // 既存の接続があれば先に閉じる
            if (udpClient != null)
            {
                if (enableDebugLogs)
                {
                    Debug.Log("ButtonGestureReceiver: Closing existing UDP client before reinitializing");
                }
                StopUDPListener();
                Thread.Sleep(100); // 少し待機してポートが解放されるのを待つ
            }

            // UdpClientを作成し、ReuseAddressオプションを設定
            udpClient = new UdpClient();
            udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, buttonGesturePort));
            udpClient.Client.ReceiveTimeout = 1000; // 1秒のタイムアウト

            shouldStop = false;
            isListening = true;

            // UDP受信スレッドを開始
            udpListenerThread = new Thread(UDPListenerLoop);
            udpListenerThread.IsBackground = true;
            udpListenerThread.Start();

            connectionEstablished = true;

            if (enableDebugLogs)
            {
                Debug.Log($"ButtonGestureReceiver: UDP listener started on port {buttonGesturePort}");
                Debug.Log($"Waiting for BUTTON_PRESSED messages from {serverIP}:{buttonGesturePort}");
            }

            ShowStatusMessage($"UDP Listener Ready (Port: {buttonGesturePort})");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"ButtonGestureReceiver: Failed to initialize UDP listener: {e.Message}");
            ShowStatusMessage("UDP Connection Failed!");
            connectionEstablished = false;
        }
    }

    /// <summary>
    /// UDP受信ループ（別スレッド）
    /// </summary>
    void UDPListenerLoop()
    {
        IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);

        while (isListening && !shouldStop)
        {
            try
            {
                byte[] receivedData = udpClient.Receive(ref remoteEndPoint);
                string message = Encoding.UTF8.GetString(receivedData).Trim();

                if (enableDebugLogs)
                {
                    Debug.Log($"ButtonGestureReceiver: Received UDP message: '{message}' from {remoteEndPoint}");
                }

                // BUTTON_PRESSEDメッセージをチェック
                if (message == "BUTTON_PRESSED")
                {
                    lock (gestureDataLock)
                    {
                        gestureDetected = true;
                    }

                    if (enableDebugLogs)
                    {
                        Debug.Log("ButtonGestureReceiver: Button gesture detected!");
                    }
                }
            }
            catch (System.Net.Sockets.SocketException e)
            {
                // タイムアウトは正常な動作なのでログを出さない
                if (e.SocketErrorCode != SocketError.TimedOut && isListening)
                {
                    Debug.LogWarning($"ButtonGestureReceiver: UDP Socket error: {e.Message}");
                }
            }
            catch (System.Exception e)
            {
                if (isListening)
                {
                    Debug.LogError($"ButtonGestureReceiver: UDP error: {e.Message}");
                }
            }
        }

        if (enableDebugLogs)
        {
            Debug.Log("ButtonGestureReceiver: UDP listener thread stopped");
        }
    }

    /// <summary>
    /// ボタンジェスチャーの処理（メインスレッド）
    /// </summary>
    void ProcessButtonGesture()
    {
        string currentScene = SceneManager.GetActiveScene().name;

        if (enableDebugLogs)
        {
            Debug.Log($"ButtonGestureReceiver: Processing button gesture in scene '{currentScene}'");
        }

        // シーン変更の条件をチェック
        bool shouldChangeScene = allowSceneChangeFromAnyScene || currentScene == fromSceneName;

        if (shouldChangeScene)
        {
            // シーンの存在チェック
            if (IsSceneInBuildSettings(toSceneName))
            {
                StartCoroutine(ChangeSceneWithFeedback());
            }
            else
            {
                Debug.LogError($"ButtonGestureReceiver: Scene '{toSceneName}' not found in build settings!");
                ShowStatusMessage($"Scene '{toSceneName}' not found!");
            }
        }
        else
        {
            if (enableDebugLogs)
            {
                Debug.Log($"ButtonGestureReceiver: Scene change ignored. Current scene '{currentScene}' != target scene '{fromSceneName}'");
            }

            ShowStatusMessage($"Gesture detected but scene change not allowed from '{currentScene}'");
        }
    }

    /// <summary>
    /// シーンがBuild Settingsに含まれているかチェック
    /// </summary>
    bool IsSceneInBuildSettings(string sceneName)
    {
        for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
        {
            string scenePath = SceneUtility.GetScenePathByBuildIndex(i);
            string sceneNameInBuild = System.IO.Path.GetFileNameWithoutExtension(scenePath);
            if (sceneNameInBuild == sceneName)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// フィードバック付きでシーンを変更
    /// </summary>
    IEnumerator ChangeSceneWithFeedback()
    {
        if (showGestureDetectedMessage)
        {
            ShowStatusMessage("Button Gesture Detected! Changing scene...");

            if (enableDebugLogs)
            {
                Debug.Log($"ButtonGestureReceiver: Changing scene from '{fromSceneName}' to '{toSceneName}'");
            }

            // フィードバック表示時間だけ待機
            yield return new WaitForSeconds(1.0f);
        }

        // シーン変更を実行
        AsyncOperation sceneLoad = SceneManager.LoadSceneAsync(toSceneName);

        if (sceneLoad != null)
        {
            while (!sceneLoad.isDone)
            {
                yield return null;
            }

            if (enableDebugLogs)
            {
                Debug.Log($"ButtonGestureReceiver: Successfully changed to scene '{toSceneName}'");
            }
        }
        else
        {
            Debug.LogError($"ButtonGestureReceiver: Failed to load scene '{toSceneName}': Scene not found in build settings");
            ShowStatusMessage($"Failed to load scene '{toSceneName}'");
        }
    }

    /// <summary>
    /// ステータスメッセージを表示
    /// </summary>
    void ShowStatusMessage(string message)
    {
        statusMessage = message;
        statusMessageTimer = feedbackDisplayTime;

        if (enableDebugLogs)
        {
            Debug.Log($"ButtonGestureReceiver: {message}");
        }
    }

    /// <summary>
    /// UDP接続を停止
    /// </summary>
    void StopUDPListener()
    {
        shouldStop = true;
        isListening = false;

        // スレッドの終了を待つ
        if (udpListenerThread != null && udpListenerThread.IsAlive)
        {
            udpListenerThread.Join(1000); // 1秒待機
        }

        // UdpClientを確実に閉じる
        if (udpClient != null)
        {
            try
            {
                udpClient.Close();
            }
            catch (System.Exception e)
            {
                if (enableDebugLogs)
                {
                    Debug.LogWarning($"ButtonGestureReceiver: Error closing UDP client: {e.Message}");
                }
            }
            finally
            {
                udpClient = null;
            }
        }

        connectionEstablished = false;

        if (enableDebugLogs)
        {
            Debug.Log("ButtonGestureReceiver: UDP listener stopped");
        }
    }

    /// <summary>
    /// GUI表示（デバッグ用）
    /// </summary>
    void OnGUI()
    {
        if (!showConnectionStatus && statusMessageTimer <= 0) return;

        // GUI用のスタイル設定
        GUIStyle style = new GUIStyle(GUI.skin.box);
        style.fontSize = 16;
        style.normal.textColor = connectionEstablished ? Color.green : Color.red;
        style.alignment = TextAnchor.MiddleCenter;

        // 接続状態を表示
        if (showConnectionStatus)
        {
            string connectionStatus = connectionEstablished ?
                $"UDP Connected (Port: {buttonGesturePort})" :
                "UDP Disconnected";

            GUI.Box(new Rect(10, 10, 300, 30), connectionStatus, style);
        }

        // ステータスメッセージを表示
        if (statusMessageTimer > 0 && !string.IsNullOrEmpty(statusMessage))
        {
            GUIStyle messageStyle = new GUIStyle(GUI.skin.box);
            messageStyle.fontSize = 14;
            messageStyle.normal.textColor = Color.yellow;
            messageStyle.alignment = TextAnchor.MiddleCenter;

            GUI.Box(new Rect(10, 50, 400, 40), statusMessage, messageStyle);
        }
    }

    /// <summary>
    /// 手動テスト用（インスペクターから呼び出し可能）
    /// </summary>
    [ContextMenu("Test Scene Change")]
    public void TestSceneChange()
    {
        ProcessButtonGesture();
    }

    /// <summary>
    /// UDP接続を再開
    /// </summary>
    [ContextMenu("Restart UDP Connection")]
    public void RestartUDPConnection()
    {
        StopUDPListener();
        System.Threading.Thread.Sleep(100);
        InitializeUDPListener();
    }

    // Unity Events
    void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
        {
            StopUDPListener();
        }
        else
        {
            InitializeUDPListener();
        }
    }

    void OnDisable()
    {
        StopUDPListener();
    }

    void OnApplicationQuit()
    {
        StopUDPListener();
    }

    void OnDestroy()
    {
        StopUDPListener();
    }
}