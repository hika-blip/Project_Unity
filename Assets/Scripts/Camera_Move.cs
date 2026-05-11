using UnityEngine;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

public class Camera_Move : MonoBehaviour
{
    UdpClient client;
    Thread receiveThread;
    bool isRunning = false;

    [HideInInspector]
    public Quaternion baseRotation;

    // 受信データ
    private volatile float noseX = 0f;
    private volatile float noseY = 0f;
    private volatile float noseZ = 0.5f;
    private volatile bool hasReceivedData = false;

    [Header("キャリブレーション")]
    [Tooltip("CalibrationManagerへの参照")]
    public CalibrationManager calibrationManager;

    [Tooltip("キャリブレーションを使用するか")]
    public bool useCalibration = true;

    [Header("回転角変換スケール")]
    public float horizontalAngleRange = 45f;
    public float verticalAngleRange = 30f;
    public float cameraHeight = 1f;

    // スムージング   
    private float currentNoseX = 0f, currentNoseY = 0f, currentNoseZ = 0.5f;
    private float velX = 0f, velY = 0f, velZ = 0f;
    [Header("滑らかさパラメータ")]
    public float smoothTime = 0.12f;

    // デバッグ情報
    private string lastReceivedData = "No data received";
    private int totalMessagesReceived = 0;
    private float lastReceiveTime = 0f;

    void Start()
    {
        // CalibrationManagerを自動検索
        if (calibrationManager == null)
        {
            calibrationManager = FindObjectOfType<CalibrationManager>();
            if (calibrationManager == null)
            {
                Debug.LogWarning("Camera_Move: CalibrationManager not found.");
                //useCalibration = false;
                enabled = false;
                return;
            }
        }

        //StartUDPListener();
        baseRotation = transform.rotation;
    }

    //void StartUDPListener()
    //{
    //    try
    //    {
    //        if (client != null)
    //        {
    //            client.Close();
    //        }

    //        client = new UdpClient(9999);
    //        client.Client.ReceiveTimeout = 1000;

    //        isRunning = true;
    //        receiveThread = new Thread(ReceiveData);
    //        receiveThread.IsBackground = true;
    //        receiveThread.Start();

    //        Debug.Log("Camera_Move: UDP Listener started successfully on port 9999");
    //    }
    //    catch (System.Exception e)
    //    {
    //        Debug.LogError($"Camera_Move: Failed to start UDP listener: {e.Message}");
    //    }
    //}

    void ReceiveData()
    {
        IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 9999);

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
            catch (SocketException)
            {
                continue;
            }
            catch (System.Exception e)
            {
                if (isRunning)
                {
                    Debug.LogWarning($"Camera_Move: UDP Receive Error: {e.Message}");
                }
                Thread.Sleep(100);
            }
        }
    }

    //void Update()
    //{
    //    if (!hasReceivedData)
    //    {
    //        lastReceiveTime = Time.time;
    //    }

    //    // キャリブレーションオフセットを適用
    //    Vector3 rawPosition = new Vector3(noseX, noseY, noseZ);
    //    Vector3 calibratedPosition = rawPosition;

    //    if (useCalibration && calibrationManager != null && calibrationManager.IsCalibrated)
    //    {
    //        Vector3 offset = calibrationManager.CalibrationOffset;
    //        calibratedPosition = rawPosition - offset;
    //    }

    //    // スムージング
    //    currentNoseX = Mathf.SmoothDamp(currentNoseX, calibratedPosition.x, ref velX, smoothTime);
    //    currentNoseY = Mathf.SmoothDamp(currentNoseY, calibratedPosition.y, ref velY, smoothTime);
    //    currentNoseZ = Mathf.SmoothDamp(currentNoseZ, calibratedPosition.z, ref velZ, smoothTime);

    //    // カメラ方向制御
    //    float angleY = 2.5f * Mathf.Clamp(currentNoseX * horizontalAngleRange, -horizontalAngleRange, horizontalAngleRange);
    //    float angleX = 2.5f * Mathf.Clamp(currentNoseY * verticalAngleRange, -verticalAngleRange, verticalAngleRange);

    //    Quaternion headRotation = Quaternion.Euler(-angleX, angleY, 0f);
    //    transform.rotation = baseRotation * headRotation;
    //}

    void Update()
    {
        Vector3 pos = useCalibration
            ? calibrationManager.GetCalibratedPosition()
            : calibrationManager.GetRawPosition();

        // スムージング
        currentNoseX = Mathf.SmoothDamp(currentNoseX, pos.x, ref velX, smoothTime);
        currentNoseY = Mathf.SmoothDamp(currentNoseY, pos.y, ref velY, smoothTime);
        currentNoseZ = Mathf.SmoothDamp(currentNoseZ, pos.z, ref velZ, smoothTime);

        // カメラ回転
        float angleY = Mathf.Clamp(currentNoseX * horizontalAngleRange, -horizontalAngleRange, horizontalAngleRange);
        float angleX = Mathf.Clamp(currentNoseY * verticalAngleRange, -verticalAngleRange, verticalAngleRange);

        Quaternion headRotation = Quaternion.Euler(-angleX, angleY, 0f);
        transform.rotation = baseRotation * headRotation;
    }


    public float GetCurrentNoseZ()
    {
        return currentNoseZ;
    }

    //void OnGUI()
    //{
    //    GUILayout.BeginArea(new Rect(10, 10, 450, 250));

    //    GUILayout.Label("=== Camera Move (Calibrated) ===");
    //    GUILayout.Label($"UDP Status: {(isRunning ? "Running" : "Stopped")}");
    //    GUILayout.Label($"Messages Received: {totalMessagesReceived}");
    //    GUILayout.Label($"Last Data: {lastReceivedData}");
    //    GUILayout.Label($"Time Since Last: {Time.time - lastReceiveTime:F1}s");

    //    GUILayout.Label("--- Calibration ---");
    //    if (useCalibration && calibrationManager != null)
    //    {
    //        GUILayout.Label($"Calibration: {(calibrationManager.IsCalibrated ? "Active" : "Not Calibrated")}");
    //        if (calibrationManager.IsCalibrated)
    //        {
    //            Vector3 offset = calibrationManager.CalibrationOffset;
    //            GUILayout.Label($"Offset: ({offset.x:F3}, {offset.y:F3}, {offset.z:F3})");
    //        }
    //    }
    //    else
    //    {
    //        GUILayout.Label("Calibration: Disabled");
    //    }

    //    GUILayout.Label($"Raw Position: ({noseX:F3}, {noseY:F3}, {noseZ:F3})");
    //    GUILayout.Label($"Calibrated Position: ({currentNoseX:F3}, {currentNoseY:F3}, {currentNoseZ:F3})");

    //    GUILayout.EndArea();
    //}

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

        if (client != null)
        {
            client.Close();
        }
        Debug.Log("Camera_Move: UDP Listener stopped");
    }

    void OnDestroy()
    {
        OnApplicationQuit();
    }
}