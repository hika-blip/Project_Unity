using UnityEngine;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

public class HeadTracking_2 : MonoBehaviour
{
    // UDP通信用
    private UdpClient client;
    private Thread receiveThread;
    private bool isRunning = false;

    [Header("UDP Settings")]
    [Tooltip("UDP受信ポート番号")]
    public int udpPort = 9999;

    // 基準位置・回転
    [HideInInspector]
    public Quaternion baseRotation;
    private Vector3 basePosition;

    // 受信データ
    private volatile float noseX = 0f;
    private volatile float noseY = 0f;
    private volatile float noseZ = 0.5f;
    private volatile bool hasReceivedData = false;

    [Header("注視点設定")]
    [Tooltip("カメラが注視する対象物（立方体など）")]
    public Transform lookAtTarget;

    [Tooltip("注視点のオフセット")]
    public Vector3 lookAtOffset = Vector3.zero;

    [Header("極座標パラメータ")]
    [Tooltip("カメラから注視点までの基準距離")]
    public float baseDistance = 7f;

    [Tooltip("水平方向の角度範囲（度）")]
    public float horizontalAngleRange = 45f;

    [Tooltip("垂直方向の角度範囲（度）")]
    public float verticalAngleRange = 30f;

    [Tooltip("基準となるZ距離")]
    public float referenceDistance = 0.5f;

    [Tooltip("Y座標の倍率")]
    public float yMultiplier = 1.5f;

    [Header("拡大率パラメータ")]
    [Tooltip("拡大率の基準値（FOV調整用）")]
    public float baseFOV = 60f;

    [Tooltip("拡大率の最小値")]
    public float minZoomFactor = 0.5f;

    [Tooltip("拡大率の最大値")]
    public float maxZoomFactor = 2.0f;

    [Header("スムージングパラメータ")]
    [Tooltip("角度のスムージング時間")]
    public float angleSmoothTime = 0.12f;

    [Tooltip("距離のスムージング時間")]
    public float distanceSmoothTime = 0.15f;

    [Tooltip("FOVのスムージング時間")]
    public float fovSmoothTime = 0.15f;

    // スムージング用変数
    private float currentNoseX = 0f;
    private float currentNoseY = 0f;
    private float currentNoseZ = 0.5f;
    private float velX = 0f;
    private float velY = 0f;
    private float velZ = 0f;

    private float currentHorizontalAngle = 0f;
    private float currentVerticalAngle = 0f;
    private float currentDistance;
    private float horizontalAngleVelocity = 0f;
    private float verticalAngleVelocity = 0f;
    private float distanceVelocity = 0f;

    // カメラコンポーネント
    private Camera cam;
    private float currentFOV;
    private float fovVelocity = 0f;

    // デバッグ情報
    private string lastReceivedData = "No data received";
    private int totalMessagesReceived = 0;
    private float lastReceiveTime = 0f;
    private float currentZoomFactor = 1f;

    [Header("デバッグ")]
    public bool showDebugGUI = true;
    public bool showDebugGizmos = true;

    void Start()
    {
        StartUDPListener();
        // baseRotationをIdentity（無回転）に設定
        //baseRotation = Quaternion.identity;
        baseRotation = Quaternion.Euler(0, 140, 0);
        basePosition = transform.position;
        currentDistance = baseDistance;

        // カメラコンポーネントを取得
        cam = GetComponent<Camera>();
        if (cam == null)
        {
            cam = gameObject.AddComponent<Camera>();
        }
        currentFOV = baseFOV;
        cam.fieldOfView = currentFOV;

        // lookAtTargetが設定されていない場合は警告
        if (lookAtTarget == null)
        {
            Debug.LogWarning("HeadTracking_Polar: lookAtTarget is not set. Please assign a target object in the Inspector.");
        }
    }

    void StartUDPListener()
    {
        try
        {
            // 既存の接続があれば先に閉じる
            if (client != null)
            {
                Debug.Log("HeadTracking_Polar: Closing existing UDP client before reinitializing");
                StopUDPListener();
                Thread.Sleep(100);
            }

            // UdpClientを作成し、ReuseAddressオプションを設定
            client = new UdpClient();
            client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            client.Client.Bind(new IPEndPoint(IPAddress.Any, udpPort));
            client.Client.ReceiveTimeout = 1000;

            isRunning = true;
            receiveThread = new Thread(ReceiveData);
            receiveThread.IsBackground = true;
            receiveThread.Start();

            Debug.Log($"HeadTracking_Polar: UDP Listener started successfully on port {udpPort}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"HeadTracking_Polar: Failed to start UDP listener: {e.Message}");
        }
    }

    void ReceiveData()
    {
        IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);

        while (isRunning)
        {
            try
            {
                byte[] data = client.Receive(ref remoteEndPoint);
                string text = Encoding.UTF8.GetString(data);

                string[] parts = text.Split(',');
                if (parts.Length == 3)
                {
                    if (float.TryParse(parts[0], out float x) &&
                        float.TryParse(parts[1], out float y) &&
                        float.TryParse(parts[2], out float z))
                    {
                        noseX = x;
                        noseY = y;
                        noseZ = z;
                        hasReceivedData = true;
                        lastReceivedData = text;
                        totalMessagesReceived++;
                    }
                }
            }
            catch (SocketException e)
            {
                if (e.SocketErrorCode != SocketError.TimedOut && isRunning)
                {
                    Debug.LogWarning($"HeadTracking_Polar: UDP Socket Error: {e.Message}");
                }
            }
            catch (System.Exception e)
            {
                if (isRunning)
                {
                    Debug.LogWarning($"HeadTracking_Polar: UDP Receive Error: {e.Message}");
                }
                Thread.Sleep(100);
            }
        }

        Debug.Log("HeadTracking_Polar: UDP listener thread stopped");
    }

    void Update()
    {
        if (lookAtTarget == null)
        {
            return;
        }

        if (hasReceivedData)
        {
            lastReceiveTime = Time.time;
        }

        // 受信データをそのまま使用（キャリブレーションなし）
        Vector3 rawPosition = new Vector3(noseX, noseY, noseZ);

        // 受信データのスムージング
        currentNoseX = Mathf.SmoothDamp(currentNoseX, rawPosition.x, ref velX, angleSmoothTime);
        currentNoseY = Mathf.SmoothDamp(currentNoseY, rawPosition.y, ref velY, angleSmoothTime);
        currentNoseZ = Mathf.SmoothDamp(currentNoseZ, rawPosition.z, ref velZ, distanceSmoothTime);

        // Y座標に倍率を適用
        float adjustedY = currentNoseY * yMultiplier;

        // 頭部の位置を正規化
        float normalizedX = Mathf.Clamp(currentNoseX, -1f, 1f);
        float normalizedY = Mathf.Clamp(adjustedY, -1f, 1f);

        // 極座標での角度を計算
        float targetHorizontalAngle = normalizedX * horizontalAngleRange;
        float targetVerticalAngle = -normalizedY * verticalAngleRange;

        // スムーズに角度を更新
        currentHorizontalAngle = Mathf.SmoothDampAngle(currentHorizontalAngle, targetHorizontalAngle, ref horizontalAngleVelocity, angleSmoothTime);
        currentVerticalAngle = Mathf.SmoothDampAngle(currentVerticalAngle, targetVerticalAngle, ref verticalAngleVelocity, angleSmoothTime);

        // 注視点の位置を取得
        Vector3 lookAtPoint = lookAtTarget.position + lookAtOffset;

        // 極座標からカメラ位置を計算
        float horizontalRad = currentHorizontalAngle * Mathf.Deg2Rad;
        float verticalRad = currentVerticalAngle * Mathf.Deg2Rad;

        Vector3 offset_vec = new Vector3(
            baseDistance * Mathf.Cos(verticalRad) * Mathf.Sin(horizontalRad),
            baseDistance * Mathf.Sin(verticalRad),
            baseDistance * Mathf.Cos(verticalRad) * Mathf.Cos(horizontalRad)
        );

        // カメラの位置を設定
        transform.position = lookAtPoint + baseRotation * offset_vec;

        // カメラを注視点に向ける
        transform.LookAt(lookAtPoint);

        // 拡大率の計算
        float smoothX = currentNoseX;
        float smoothY = adjustedY;
        float z = Mathf.Max(currentNoseZ, 0.01f);

        float numerator = Mathf.Sqrt(smoothX * smoothX + smoothY * smoothY + z * z);
        currentZoomFactor = numerator / z;

        currentZoomFactor = Mathf.Clamp(currentZoomFactor, minZoomFactor, maxZoomFactor);

        // FOVを拡大率に基づいて調整
        float targetFOV = baseFOV / currentZoomFactor;
        currentFOV = Mathf.SmoothDamp(currentFOV, targetFOV, ref fovVelocity, fovSmoothTime);

        if (cam != null)
        {
            cam.fieldOfView = currentFOV;
        }
    }

    public float GetCurrentNoseZ()
    {
        return currentNoseZ;
    }

    public Vector3 GetCurrentHeadPosition()
    {
        return new Vector3(currentNoseX, currentNoseY, currentNoseZ);
    }

    public Vector3 GetPolarCoordinates()
    {
        return new Vector3(currentHorizontalAngle, currentVerticalAngle, baseDistance);
    }

    public float GetCurrentZoomFactor()
    {
        return currentZoomFactor;
    }

    void OnGUI()
    {
        if (!showDebugGUI) return;

        GUILayout.BeginArea(new Rect(10, 10, 500, 350));
        GUILayout.Label("=== Head Tracking Polar (No Calibration) ===");
        GUILayout.Label($"UDP Status: {(isRunning ? "Running" : "Stopped")}");
        GUILayout.Label($"Messages Received: {totalMessagesReceived}");
        GUILayout.Label($"Last Data: {lastReceivedData}");
        GUILayout.Label($"Time Since Last: {Time.time - lastReceiveTime:F1}s");

        GUILayout.Label("--- Position Data ---");
        GUILayout.Label($"Raw Position: ({noseX:F3}, {noseY:F3}, {noseZ:F3})");
        GUILayout.Label($"Smoothed Position: ({currentNoseX:F3}, {currentNoseY:F3}, {currentNoseZ:F3})");
        GUILayout.Label($"Adjusted Y: {currentNoseY * yMultiplier:F3}");
        GUILayout.Label($"Horizontal Angle: {currentHorizontalAngle:F1}°");
        GUILayout.Label($"Vertical Angle: {currentVerticalAngle:F1}°");
        GUILayout.Label($"Distance: {baseDistance:F2}m");
        GUILayout.Label($"Zoom Factor: {currentZoomFactor:F3}");
        GUILayout.Label($"Current FOV: {currentFOV:F1}°");
        GUILayout.Label($"Look At Target: {(lookAtTarget != null ? lookAtTarget.name : "Not Set")}");
        GUILayout.EndArea();
    }

    void OnDrawGizmos()
    {
        if (!showDebugGizmos || !Application.isPlaying || lookAtTarget == null) return;

        Vector3 lookAtPoint = lookAtTarget.position + lookAtOffset;

        // 注視点を表示
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(lookAtPoint, 0.1f);

        // カメラから注視点への線
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(transform.position, lookAtPoint);

        // カメラの位置
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, 0.05f);

        // 球面軌道を表示（水平方向）
        Gizmos.color = new Color(0f, 1f, 1f, 0.5f);
        int segments = 64;
        Vector3 prevPoint = Vector3.zero;
        for (int i = 0; i <= segments; i++)
        {
            float angle = Mathf.Lerp(-horizontalAngleRange, horizontalAngleRange, i / (float)segments) * Mathf.Deg2Rad;

            Vector3 point = lookAtPoint + baseRotation * new Vector3(
                baseDistance * Mathf.Sin(angle),
                0f,
                baseDistance * Mathf.Cos(angle)
            );

            if (i > 0)
            {
                Gizmos.DrawLine(prevPoint, point);
            }
            prevPoint = point;
        }

        // 垂直方向の軌道を表示
        Gizmos.color = new Color(1f, 0f, 1f, 0.5f);
        prevPoint = Vector3.zero;
        for (int i = 0; i <= segments; i++)
        {
            float angle = Mathf.Lerp(-verticalAngleRange, verticalAngleRange, i / (float)segments) * Mathf.Deg2Rad;
            Vector3 point = lookAtPoint + baseRotation * new Vector3(
                0f,
                baseDistance * Mathf.Sin(angle),
                baseDistance * Mathf.Cos(angle)
            );

            if (i > 0)
            {
                Gizmos.DrawLine(prevPoint, point);
            }
            prevPoint = point;
        }

        // 球面の外周を表示
        Gizmos.color = new Color(0.5f, 0.5f, 1f, 0.3f);
        Gizmos.DrawWireSphere(lookAtPoint, baseDistance);
    }

    void StopUDPListener()
    {
        isRunning = false;

        if (receiveThread != null && receiveThread.IsAlive)
        {
            receiveThread.Join(1000);
            if (receiveThread.IsAlive)
            {
                Debug.LogWarning("HeadTracking_Polar: UDP receiver thread did not stop in time");
            }
        }

        if (client != null)
        {
            try
            {
                client.Close();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"HeadTracking_Polar: Error closing UDP client: {e.Message}");
            }
            finally
            {
                client = null;
            }
        }

        Debug.Log("HeadTracking_Polar: UDP Listener stopped");
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