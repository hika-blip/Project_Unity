using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Collections;

public class WristCursorSceneController : MonoBehaviour
{
    [Header("UDP Settings")]
    [Tooltip("UDPポート番号")]
    public int udpPort = 9996;

    [Header("Cursor Settings")]
    [Tooltip("カーソルオブジェクトのRectTransform")]
    public RectTransform cursorObject;

    [Tooltip("カーソルを表示するCanvas")]
    public Canvas targetCanvas;

    [Header("Movement Settings")]
    [Tooltip("座標のスケール調整（X軸）")]
    public float movementScaleX = 1.0f;

    [Tooltip("座標のスケール調整（Y軸）")]
    public float movementScaleY = 1.0f;

    [Tooltip("スムージング時間")]
    public float smoothTime = 0.1f;

    [Header("Coordinate Settings")]
    [Tooltip("Y軸を反転するか")]
    public bool invertY = false;

    [Tooltip("X軸を反転するか")]
    public bool invertX = false;

    [Header("Scene Transition Settings")]
    [Tooltip("遷移先のシーン名")]
    public string targetSceneName = "Observation_selecter";

    [Header("Button Settings")]
    [Tooltip("戻るボタンのRectTransform")]
    public RectTransform backButton;

    [Tooltip("ボタンの画像コンポーネント（色変更用）")]
    public Image buttonImage;

    [Tooltip("ボタン上に滞在する必要がある時間（秒）")]
    public float hoverTimeRequired = 2.0f;

    [Tooltip("判定の余白（ピクセル）")]
    public float hitboxPadding = 10f;

    [Header("Visual Feedback")]
    [Tooltip("通常時のボタン色")]
    public Color normalColor = Color.white;

    [Tooltip("ホバー時のボタン色")]
    public Color hoverColor = Color.yellow;

    [Tooltip("決定時のボタン色")]
    public Color selectedColor = Color.green;

    [Tooltip("進捗バーを表示するか")]
    public bool showProgressBar = true;

    [Tooltip("進捗バーの画像")]
    public Image progressBarImage;

    [Header("Keyboard Input")]
    [Tooltip("戻るためのキー")]
    public KeyCode backKey = KeyCode.Escape;

    [Tooltip("代替キー")]
    public KeyCode alternativeBackKey = KeyCode.Backspace;

    [Header("Debug")]
    public bool showDebugInfo = true;

    // UDP受信用
    private UdpClient udpClient;
    private Thread receiveThread;
    private bool isRunning = false;
    private readonly object udpLock = new object();

    // 受信した手首座標（正規化済み: -1 to 1）
    private volatile float wristX = 0f;
    private volatile float wristY = 0f;
    private volatile bool hasData = false;

    // スムージング用
    private Vector2 currentPosition = Vector2.zero;
    private Vector2 targetPosition = Vector2.zero;
    private Vector2 velocity = Vector2.zero;

    // ボタンインタラクション用
    private float currentHoverTime = 0f;
    private bool isHovering = false;
    private bool isTransitioning = false;
    private Camera canvasCamera;

    // デバッグ用
    private int messagesReceived = 0;
    private string lastMessage = "No data";

    void Start()
    {
        // カーソルの初期化
        if (cursorObject != null)
        {
            cursorObject.anchorMin = new Vector2(0.5f, 0.5f);
            cursorObject.anchorMax = new Vector2(0.5f, 0.5f);
            cursorObject.pivot = new Vector2(0.5f, 0.5f);
            Debug.Log("WristCursorSceneController: Cursor initialized");
        }
        else
        {
            Debug.LogError("WristCursorSceneController: Cursor object is not assigned!");
        }

        // Canvasの自動検出
        if (targetCanvas == null && cursorObject != null)
        {
            targetCanvas = cursorObject.GetComponentInParent<Canvas>();
        }

        if (targetCanvas == null)
        {
            Debug.LogError("WristCursorSceneController: Canvas is not assigned!");
        }

        // ボタンの初期化
        if (backButton != null)
        {
            if (buttonImage == null)
            {
                buttonImage = backButton.GetComponent<Image>();
            }

            if (buttonImage != null)
            {
                buttonImage.color = normalColor;
            }

            // Canvasカメラの取得
            if (targetCanvas != null && targetCanvas.renderMode == RenderMode.ScreenSpaceCamera)
            {
                canvasCamera = targetCanvas.worldCamera;
                Debug.Log($"WristCursorSceneController: Canvas is Screen Space - Camera mode");
            }
        }

        // 進捗バーの初期化
        if (progressBarImage != null)
        {
            progressBarImage.fillAmount = 0f;
        }

        // UDP受信開始
        StartUDPReceiver();
    }

    void StartUDPReceiver()
    {
        lock (udpLock)
        {
            try
            {
                if (udpClient != null)
                {
                    Debug.Log("WristCursorSceneController: Closing existing UDP client");
                    StopUDPReceiverInternal();
                    Thread.Sleep(150);
                }

                udpClient = new UdpClient();
                udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ExclusiveAddressUse, false);
                udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, udpPort));
                udpClient.Client.ReceiveTimeout = 1000;

                isRunning = true;
                receiveThread = new Thread(ReceiveData);
                receiveThread.IsBackground = true;
                receiveThread.Start();

                Debug.Log($"WristCursorSceneController: UDP Receiver started on port {udpPort}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"WristCursorSceneController: Failed to start UDP receiver: {e.Message}");

                if (udpClient != null)
                {
                    try { udpClient.Close(); } catch { }
                    udpClient = null;
                }
            }
        }
    }

    void ReceiveData()
    {
        IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);

        while (isRunning)
        {
            try
            {
                if (udpClient != null && udpClient.Client != null)
                {
                    byte[] data = udpClient.Receive(ref remoteEndPoint);
                    string text = Encoding.UTF8.GetString(data);

                    string[] parts = text.Split(',');
                    if (parts.Length == 2)
                    {
                        if (float.TryParse(parts[0], out float x) &&
                            float.TryParse(parts[1], out float y))
                        {
                            wristX = x;
                            wristY = y;
                            hasData = true;
                            messagesReceived++;
                            lastMessage = text;
                        }
                    }
                }
                else
                {
                    break;
                }
            }
            catch (SocketException e)
            {
                if (e.SocketErrorCode != SocketError.TimedOut && isRunning)
                {
                    Debug.LogWarning($"WristCursorSceneController UDP Socket Error: {e.Message}");
                }
            }
            catch (System.ObjectDisposedException)
            {
                break;
            }
            catch (System.Exception e)
            {
                if (isRunning)
                {
                    Debug.LogWarning($"WristCursorSceneController UDP Error: {e.Message}");
                }
                Thread.Sleep(100);
            }
        }

        Debug.Log("WristCursorSceneController: UDP receiver thread stopped");
    }

    void Update()
    {
        if (isTransitioning) return;

        // キーボード入力チェック
        if (Input.GetKeyDown(backKey) || Input.GetKeyDown(alternativeBackKey))
        {
            StartCoroutine(TransitionToScene());
            return;
        }

        // カーソル位置の更新
        UpdateCursorPosition();

        // ボタンインタラクションの処理
        UpdateButtonInteraction();
    }

    void UpdateCursorPosition()
    {
        if (cursorObject == null || targetCanvas == null) return;

        if (hasData)
        {
            RectTransform canvasRect = targetCanvas.GetComponent<RectTransform>();
            float canvasWidth = canvasRect.rect.width;
            float canvasHeight = canvasRect.rect.height;

            // 正規化座標を取得
            float normalizedX = wristX;
            float normalizedY = wristY;

            // 反転オプション
            if (invertX) normalizedX = -normalizedX;
            if (invertY) normalizedY = -normalizedY;

            // スケール適用
            normalizedX *= movementScaleX;
            normalizedY *= movementScaleY;

            // クランプ
            normalizedX = Mathf.Clamp(normalizedX, -1f, 1f);
            normalizedY = Mathf.Clamp(normalizedY, -1f, 1f);

            // Canvas座標系に変換
            targetPosition.x = normalizedX * canvasWidth * 0.5f;
            targetPosition.y = normalizedY * canvasHeight * 0.5f;

            // スムージング
            currentPosition = Vector2.SmoothDamp(currentPosition, targetPosition, ref velocity, smoothTime);

            // カーソルの位置を更新
            cursorObject.anchoredPosition = currentPosition;
        }
    }

    void UpdateButtonInteraction()
    {
        if (backButton == null || cursorObject == null) return;

        bool isInside = IsPointInsideButton();

        if (isInside)
        {
            if (!isHovering)
            {
                isHovering = true;
                currentHoverTime = 0f;
                if (buttonImage != null)
                {
                    buttonImage.color = hoverColor;
                }
                Debug.Log("WristCursorSceneController: Hover started");
            }

            currentHoverTime += Time.deltaTime;

            if (progressBarImage != null && showProgressBar)
            {
                progressBarImage.fillAmount = currentHoverTime / hoverTimeRequired;
            }

            if (currentHoverTime >= hoverTimeRequired)
            {
                StartCoroutine(TransitionToScene());
            }
        }
        else
        {
            if (isHovering)
            {
                ResetHoverState();
                Debug.Log("WristCursorSceneController: Hover ended");
            }
        }
    }

    bool IsPointInsideButton()
    {
        if (backButton == null || cursorObject == null) return false;

        // Screen Space - Cameraの場合
        if (targetCanvas != null && targetCanvas.renderMode == RenderMode.ScreenSpaceCamera && canvasCamera != null)
        {
            Vector2 cursorScreenPos = RectTransformUtility.WorldToScreenPoint(canvasCamera, cursorObject.position);

            Vector2 localPoint;
            bool isInRect = RectTransformUtility.ScreenPointToLocalPointInRectangle(
                backButton,
                cursorScreenPos,
                canvasCamera,
                out localPoint
            );

            if (!isInRect) return false;

            Rect rect = backButton.rect;
            rect.xMin -= hitboxPadding;
            rect.xMax += hitboxPadding;
            rect.yMin -= hitboxPadding;
            rect.yMax += hitboxPadding;

            return rect.Contains(localPoint);
        }
        // Screen Space - Overlayの場合
        else if (targetCanvas != null && targetCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
        {
            Vector2 cursorScreenPos = cursorObject.position;
            Rect buttonRect = GetScreenRect(backButton);
            buttonRect.xMin -= hitboxPadding;
            buttonRect.xMax += hitboxPadding;
            buttonRect.yMin -= hitboxPadding;
            buttonRect.yMax += hitboxPadding;

            return buttonRect.Contains(cursorScreenPos);
        }
        else
        {
            return RectTransformUtility.RectangleContainsScreenPoint(backButton, cursorObject.position, canvasCamera);
        }
    }

    Rect GetScreenRect(RectTransform rectTransform)
    {
        Vector3[] corners = new Vector3[4];
        rectTransform.GetWorldCorners(corners);

        float xMin = corners[0].x;
        float xMax = corners[2].x;
        float yMin = corners[0].y;
        float yMax = corners[2].y;

        return new Rect(xMin, yMin, xMax - xMin, yMax - yMin);
    }

    void ResetHoverState()
    {
        isHovering = false;
        currentHoverTime = 0f;

        if (buttonImage != null)
        {
            buttonImage.color = normalColor;
        }

        if (progressBarImage != null && showProgressBar)
        {
            progressBarImage.fillAmount = 0f;
        }
    }

    IEnumerator TransitionToScene()
    {
        if (isTransitioning) yield break;

        isTransitioning = true;

        if (buttonImage != null)
        {
            buttonImage.color = selectedColor;
        }

        if (progressBarImage != null && showProgressBar)
        {
            progressBarImage.fillAmount = 1f;
        }

        Debug.Log($"WristCursorSceneController: Attempting to transition to scene '{targetSceneName}'");

        // Build Settingsに登録されているシーンをすべて確認
        Debug.Log("=== Scenes in Build Settings ===");
        int targetSceneIndex = -1;
        for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
        {
            string scenePath = SceneUtility.GetScenePathByBuildIndex(i);
            string sceneNameInBuild = System.IO.Path.GetFileNameWithoutExtension(scenePath);
            Debug.Log($"  [{i}] {sceneNameInBuild} (Path: {scenePath})");

            // 大文字小文字を無視して比較
            if (sceneNameInBuild.Equals(targetSceneName, System.StringComparison.OrdinalIgnoreCase))
            {
                targetSceneIndex = i;
            }
        }

        yield return new WaitForSeconds(0.3f);

        // シーン遷移を実行
        if (targetSceneIndex >= 0)
        {
            Debug.Log($"WristCursorSceneController: Loading scene by index {targetSceneIndex}");
            SceneManager.LoadScene(targetSceneIndex);
        }
        else
        {
            Debug.LogError($"WristCursorSceneController: Scene '{targetSceneName}' not found in build settings!");
            Debug.LogError($"Please check the scene name in the Inspector. Current value: '{targetSceneName}'");
            Debug.LogError("Make sure the scene is added to File > Build Settings > Scenes In Build");
            isTransitioning = false;
            ResetHoverState();
        }
    }

    [ContextMenu("Test Scene Transition")]
    public void TestTransition()
    {
        StartCoroutine(TransitionToScene());
    }

    //void OnGUI()
    //{
    //    if (!showDebugInfo) return;

    //    GUILayout.BeginArea(new Rect(10, 10, 400, 300));
    //    GUILayout.Label("=== Wrist Cursor Scene Controller ===");
    //    GUILayout.Label($"UDP Port: {udpPort}");
    //    GUILayout.Label($"UDP Running: {isRunning}");
    //    GUILayout.Label($"Has Data: {hasData}");
    //    GUILayout.Label($"Messages Received: {messagesReceived}");
    //    GUILayout.Label($"Last Message: {lastMessage}");
    //    GUILayout.Label($"Raw Wrist: ({wristX:F3}, {wristY:F3})");
    //    GUILayout.Label($"Cursor Position: ({currentPosition.x:F1}, {currentPosition.y:F1})");
    //    GUILayout.Label("---");
    //    GUILayout.Label($"Target Scene: {targetSceneName}");
    //    GUILayout.Label($"Is Hovering: {isHovering}");
    //    GUILayout.Label($"Hover Time: {currentHoverTime:F2} / {hoverTimeRequired:F2}");
    //    GUILayout.Label($"Progress: {(currentHoverTime / hoverTimeRequired * 100f):F1}%");
    //    GUILayout.Label($"Back Key: {backKey} or {alternativeBackKey}");
    //    GUILayout.EndArea();
    //}

    void StopUDPReceiverInternal()
    {
        isRunning = false;

        if (receiveThread != null && receiveThread.IsAlive)
        {
            bool joined = receiveThread.Join(1500);
            if (!joined)
            {
                Debug.LogWarning("WristCursorSceneController: UDP receiver thread did not stop in time");
            }
            receiveThread = null;
        }

        if (udpClient != null)
        {
            try
            {
                udpClient.Close();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"WristCursorSceneController: Error closing UDP client: {e.Message}");
            }
            finally
            {
                udpClient.Dispose();
                udpClient = null;
            }
        }

        Debug.Log("WristCursorSceneController: UDP Receiver stopped");
    }

    void OnDisable()
    {
        lock (udpLock)
        {
            StopUDPReceiverInternal();
        }
    }

    void OnApplicationQuit()
    {
        lock (udpLock)
        {
            StopUDPReceiverInternal();
        }
    }

    void OnDestroy()
    {
        lock (udpLock)
        {
            StopUDPReceiverInternal();
        }
    }
}