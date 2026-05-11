using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using TMPro;

public class GestureReceiver : MonoBehaviour
{
    private UdpClient udpClient;
    private IPEndPoint remoteEndPoint;

    [SerializeField] private TextMeshProUGUI messageText;
    private float messageDisplayTime = 2.0f;
    private float messageTimer = 0f;

    void Start()
    {
        udpClient = new UdpClient(9998);
        remoteEndPoint = new IPEndPoint(IPAddress.Any, 9998);

        //if (messageText != null)
        //    messageText.text = "準備完了"; // ← 起動時に確認用の文字を表示
    }

    void Update()
    {
        if (udpClient.Available > 0)
        {
            byte[] data = udpClient.Receive(ref remoteEndPoint);
            string message = Encoding.UTF8.GetString(data);

            if (message == "GESTURE_DETECTED")
            {
                Debug.Log("Gesture detected!");
                OnGestureDetected();
            }
        }

        if (messageTimer > 0)
        {
            messageTimer -= Time.deltaTime;
            if (messageTimer <= 0 && messageText != null)
            {
                messageText.text = "";
            }
        }
    }

    private void OnGestureDetected()
    {
        if (messageText != null)
        {
            messageText.text = "虫取り成功！";
            messageTimer = messageDisplayTime;
        }
    }
}
