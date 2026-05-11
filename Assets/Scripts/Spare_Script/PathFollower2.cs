using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

public class PathFollower2 : MonoBehaviour
{
    [Header("パス設定")]
    [Tooltip("カメラが通過する座標リスト")]
    public Vector3[] pathPoints = new Vector3[]
    {
        new Vector3(0, 1.6f, 0),
        new Vector3(10, 1.6f, 10),
        new Vector3(20, 1.6f, 15),
        new Vector3(30, 1.6f, 10)
    };

    [Header("移動設定")]
    public float minSpeed = 0.5f;
    public float maxSpeed = 4.0f;

    [Header("曲線設定")]
    [Range(10, 200)]
    public int curveResolution = 50;
    [Range(0.01f, 1.0f)]
    public float rotationSmoothness = 0.1f;
    [Range(0.5f, 5.0f)]
    public float lookAheadDistance = 2.0f;

    [Header("追従対象")]
    public Transform followTarget;

    [Header("ゲーム終了設定")]
    public TextMeshProUGUI gameOverText;
    [Range(0f, 1f)]
    public float outlineWidth = 0.2f;
    public Color outlineColor = Color.black;
    public string nextSceneName = "Observation_selector";
    public float messageDisplayTime = 2f;

    [Header("デバッグ表示")]
    public bool showPath = true;
    public Color pathColor = Color.green;
    public bool showDebugInfo = true;

    [Header("セッション記録")]
    public bool recordPathData = true;

    [Header("チュートリアル制御")]
    public bool pauseMovement = false; // trueの時は移動を停止

    // UDP受信用（ポート9990）
    private UdpClient udpClient;
    private Thread receiveThread;
    private bool isRunning = false;
    private volatile float receivedZ = 0.5f;
    private float currentMoveSpeed = 2.0f;
    private Camera_Move cameraMove;

    private Vector3[] curvePoints;
    private float currentDistance = 0f;
    private float totalPathLength = 0f;
    private bool isGameOver = false;

    private Quaternion targetRotation;
    private Quaternion currentRotation;

    // セッション記録用
    private float pathStartTime;
    private float totalDistanceTraveled = 0f;
    private float maxSpeedReached = 0f;
    private float minSpeedReached = 999f;
    private Vector3 lastPosition;

    void Start()
    {
        pathStartTime = Time.time;

        if (followTarget == null)
        {
            followTarget = Camera.main.transform;
        }

        lastPosition = followTarget.position;

        if (gameOverText != null)
        {
            gameOverText.gameObject.SetActive(false);
        }

        cameraMove = followTarget.GetComponent<Camera_Move>();
        if (cameraMove == null)
        {
            Debug.LogWarning("PathFollower: Camera_Move component not found on followTarget!");
        }

        GenerateSmoothCurve();

        // 初期のパス方向を設定
        if (curvePoints != null && curvePoints.Length > 1)
        {
            Vector3 initialForward = (curvePoints[1] - curvePoints[0]).normalized;
            if (initialForward != Vector3.zero)
            {
                Quaternion initialRotation = Quaternion.LookRotation(initialForward);
                currentRotation = initialRotation;
                targetRotation = initialRotation;
                if (cameraMove != null)
                {
                    cameraMove.baseRotation = initialRotation;
                }
            }
        }

        // セッション記録
        if (recordPathData && UserSessionManager.Instance != null)
        {
            UserSessionManager.Instance.RecordCustomEvent(
                "PathFollowerStart",
                $"TotalPathLength={totalPathLength:F2},MinSpeed={minSpeed},MaxSpeed={maxSpeed}"
            );
        }

        StartUDPReceiver();
    }

    void StartUDPReceiver()
    {
        try
        {
            udpClient = new UdpClient(9990);
            udpClient.Client.ReceiveTimeout = 1000;

            isRunning = true;
            receiveThread = new Thread(ReceiveData);
            receiveThread.IsBackground = true;
            receiveThread.Start();

            Debug.Log("PathFollower: UDP Receiver started on port 9990");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"PathFollower: Failed to start UDP receiver: {e.Message}");
        }
    }

    void ReceiveData()
    {
        IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 9990);

        while (isRunning)
        {
            try
            {
                byte[] data = udpClient.Receive(ref remoteEndPoint);
                string text = Encoding.UTF8.GetString(data);

                if (float.TryParse(text, out float z))
                {
                    receivedZ = z;
                }
            }
            catch (SocketException)
            {
                continue;
            }
            catch (System.Exception e)
            {
                if (isRunning)
                {
                    Debug.LogWarning($"PathFollower UDP Receive Error: {e.Message}");
                }
                Thread.Sleep(100);
            }
        }
    }

    void Update()
    {
        // チュートリアル中は移動を停止
        if (pauseMovement)
            return;

        if (curvePoints == null || curvePoints.Length < 2)
            return;

        if (isGameOver)
            return;

        // 速度計算
        currentMoveSpeed = Mathf.Lerp(minSpeed, maxSpeed, receivedZ);

        // 統計記録
        if (currentMoveSpeed > maxSpeedReached)
            maxSpeedReached = currentMoveSpeed;
        if (currentMoveSpeed < minSpeedReached)
            minSpeedReached = currentMoveSpeed;

        // パスに沿って移動
        float previousDistance = currentDistance;
        currentDistance += currentMoveSpeed * Time.deltaTime;

        // 移動距離を記録
        totalDistanceTraveled += currentMoveSpeed * Time.deltaTime;

        // パスの終わりに到達したらゲーム終了
        if (currentDistance >= totalPathLength)
        {
            StartGameOver();
            return;
        }

        // 現在位置を取得
        Vector3 newPosition = GetPositionAtDistance(currentDistance);

        // 先読み位置での進行方向を取得
        float lookAheadDist = Mathf.Min(currentDistance + lookAheadDistance, totalPathLength);
        Vector3 lookAheadForward = GetForwardAtDistance(lookAheadDist);

        // ターゲットの位置を更新
        followTarget.position = newPosition;
        lastPosition = newPosition;

        // 目標の回転を計算
        if (lookAheadForward != Vector3.zero)
        {
            targetRotation = Quaternion.LookRotation(lookAheadForward);
        }

        // 現在の回転から目標の回転へ滑らかに補間
        currentRotation = Quaternion.Slerp(currentRotation, targetRotation, rotationSmoothness);

        // Camera_MoveスクリプトのbaseRotationを更新
        if (cameraMove != null)
        {
            cameraMove.baseRotation = currentRotation;
        }

        // 進行度を定期的に記録（10%ごと）
        if (recordPathData && UserSessionManager.Instance != null)
        {
            float progress = (currentDistance / totalPathLength) * 100f;
            int progressMilestone = Mathf.FloorToInt(progress / 10f) * 10;

            // 前回のフレームでのマイルストーンと比較
            float previousProgress = (previousDistance / totalPathLength) * 100f;
            int previousMilestone = Mathf.FloorToInt(previousProgress / 10f) * 10;

            if (progressMilestone > previousMilestone && progressMilestone > 0)
            {
                UserSessionManager.Instance.RecordCustomEvent(
                    "PathProgress",
                    $"Progress={progressMilestone}%,Speed={currentMoveSpeed:F2},Distance={currentDistance:F2}"
                );
            }
        }
    }

    void StartGameOver()
    {
        isGameOver = true;

        // 最終統計を記録
        if (recordPathData && UserSessionManager.Instance != null)
        {
            float travelTime = Time.time - pathStartTime;
            float averageSpeed = totalDistanceTraveled / travelTime;

            UserSessionManager.Instance.RecordCustomEvent(
                "PathCompleted",
                $"TravelTime={travelTime:F2}s,AvgSpeed={averageSpeed:F2},MaxSpeed={maxSpeedReached:F2},MinSpeed={minSpeedReached:F2},TotalDistance={totalDistanceTraveled:F2}"
            );
        }

        StartCoroutine(GameOverSequence());
    }

    System.Collections.IEnumerator GameOverSequence()
    {
        ApplyOutline(gameOverText);

        if (gameOverText != null)
        {
            gameOverText.gameObject.SetActive(true);
            gameOverText.text = "ゲームおわり！";
        }

        yield return new WaitForSeconds(messageDisplayTime);

        LoadNextScene();
    }

    void ApplyOutline(TextMeshProUGUI textComponent)
    {
        if (textComponent != null)
        {
            textComponent.outlineWidth = outlineWidth;
            textComponent.outlineColor = outlineColor;
        }
    }

    void LoadNextScene()
    {
        // シーン遷移を記録
        if (recordPathData && UserSessionManager.Instance != null)
        {
            UserSessionManager.Instance.RecordSceneTransition("Forest", nextSceneName);

            // Forestシーン終了を記録
            float totalTime = Time.time - pathStartTime;
            UserSessionManager.Instance.RecordCustomEvent("ForestSceneComplete", $"TotalTime={totalTime:F2}s");

            // ここでセッションを終了（次のシーンは別ユーザーの可能性があるため）
            Debug.Log("PathFollower: Forestシーン完了、セッション終了");
            UserSessionManager.Instance.EndCurrentSession();
        }

        SceneManager.LoadScene(nextSceneName);
    }

    void OnGUI()
    {
        if (!showDebugInfo) return;

        GUILayout.BeginArea(new Rect(10, 250, 400, 300));
        GUILayout.Label("=== Path Follower Debug ===");

        if (UserSessionManager.Instance != null && UserSessionManager.Instance.IsUserActive)
        {
            GUILayout.Label($"User: {UserSessionManager.Instance.CurrentUserID}");
        }

        GUILayout.Label($"Current Distance: {currentDistance:F2} / {totalPathLength:F2}");
        GUILayout.Label($"Progress: {(currentDistance / totalPathLength * 100f):F1}%");
        GUILayout.Label($"Received Z (Port 9990): {receivedZ:F3}");
        GUILayout.Label($"Current Speed: {currentMoveSpeed:F2} m/s");
        GUILayout.Label($"Max Speed Reached: {maxSpeedReached:F2} m/s");
        GUILayout.Label($"Min Speed Reached: {minSpeedReached:F2} m/s");
        GUILayout.Label($"Total Distance Traveled: {totalDistanceTraveled:F2}m");
        GUILayout.Label($"Travel Time: {(Time.time - pathStartTime):F1}s");

        if (cameraMove != null)
        {
            GUILayout.Label($"Target Rotation: {targetRotation.eulerAngles}");
            GUILayout.Label($"Current Rotation: {currentRotation.eulerAngles}");
        }

        GUILayout.EndArea();
    }

    void GenerateSmoothCurve()
    {
        if (pathPoints.Length < 2)
        {
            Debug.LogWarning("パスポイントが2つ以上必要です");
            return;
        }

        curvePoints = new Vector3[curveResolution * (pathPoints.Length - 1)];
        int index = 0;
        totalPathLength = 0f;

        for (int i = 0; i < pathPoints.Length - 1; i++)
        {
            Vector3 p0 = i > 0 ? pathPoints[i - 1] : pathPoints[i];
            Vector3 p1 = pathPoints[i];
            Vector3 p2 = pathPoints[i + 1];
            Vector3 p3 = i + 2 < pathPoints.Length ? pathPoints[i + 2] : pathPoints[i + 1];

            Vector3 previousPoint = p1;

            for (int j = 0; j < curveResolution; j++)
            {
                float t = j / (float)curveResolution;
                Vector3 point = CalculateCatmullRom(p0, p1, p2, p3, t);
                curvePoints[index++] = point;

                if (j > 0)
                {
                    totalPathLength += Vector3.Distance(previousPoint, point);
                }
                previousPoint = point;
            }
        }

        Debug.Log($"パス生成完了: 総距離 {totalPathLength:F2}m, ポイント数 {curvePoints.Length}");
    }

    Vector3 CalculateCatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;

        return 0.5f * (
            (2f * p1) +
            (-p0 + p2) * t +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
            (-p0 + 3f * p1 - 3f * p2 + p3) * t3
        );
    }

    Vector3 GetPositionAtDistance(float distance)
    {
        float accumulatedDistance = 0f;

        for (int i = 0; i < curvePoints.Length - 1; i++)
        {
            float segmentLength = Vector3.Distance(curvePoints[i], curvePoints[i + 1]);

            if (accumulatedDistance + segmentLength >= distance)
            {
                float t = (distance - accumulatedDistance) / segmentLength;
                return Vector3.Lerp(curvePoints[i], curvePoints[i + 1], t);
            }

            accumulatedDistance += segmentLength;
        }

        return curvePoints[curvePoints.Length - 1];
    }

    Vector3 GetForwardAtDistance(float distance)
    {
        float accumulatedDistance = 0f;

        for (int i = 0; i < curvePoints.Length - 1; i++)
        {
            float segmentLength = Vector3.Distance(curvePoints[i], curvePoints[i + 1]);

            if (accumulatedDistance + segmentLength >= distance)
            {
                return (curvePoints[i + 1] - curvePoints[i]).normalized;
            }

            accumulatedDistance += segmentLength;
        }

        return Vector3.forward;
    }

    void OnDrawGizmos()
    {
        if (!showPath || pathPoints == null || pathPoints.Length < 2)
            return;

        Gizmos.color = Color.red;
        foreach (Vector3 point in pathPoints)
        {
            Gizmos.DrawSphere(point, 0.3f);
        }

        if (curvePoints != null && curvePoints.Length > 1)
        {
            Gizmos.color = pathColor;
            for (int i = 0; i < curvePoints.Length - 1; i++)
            {
                Gizmos.DrawLine(curvePoints[i], curvePoints[i + 1]);
            }
        }
        else
        {
            Gizmos.color = Color.yellow;
            for (int i = 0; i < pathPoints.Length - 1; i++)
            {
                Gizmos.DrawLine(pathPoints[i], pathPoints[i + 1]);
            }
        }
    }

    void OnApplicationQuit()
    {
        isRunning = false;

        if (receiveThread != null && receiveThread.IsAlive)
        {
            receiveThread.Join(1000);
            if (receiveThread.IsAlive)
            {
                receiveThread.Abort();
            }
        }

        if (udpClient != null)
        {
            udpClient.Close();
        }
        Debug.Log("PathFollower: UDP Receiver stopped");
    }

    void OnDestroy()
    {
        OnApplicationQuit();
    }
}