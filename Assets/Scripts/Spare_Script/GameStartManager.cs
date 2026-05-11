using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using TMPro;

public class GameStartManager : MonoBehaviour
{
    [Header("UI Elements")]
    public TextMeshProUGUI startText;  // "ゲームスタート！" を表示するUI Text
    public TextMeshProUGUI MenuText;
    public Image gameImage;  // ゲームスタート後に表示するImage

    [Header("Outline Settings")]
    [Range(0f, 1f)]
    public float outlineWidth = 0.2f;  // 縁取りの太さ（0～1）
    public Color outlineColor = Color.black;  // 縁取りの色

    [Header("Camera Settings")]
    public Camera mainCamera;       // ゲームで使うカメラ
    private MonoBehaviour[] cameraScripts; // カメラにアタッチされている制御スクリプト
    private Vector3 initialCamPos;         // カメラの固定用座標
    private Quaternion initialCamRot;      // カメラの固定用回転

    void Start()
    {
        if (mainCamera == null) mainCamera = Camera.main;

        // カメラの位置と回転を保存
        initialCamPos = mainCamera.transform.position;
        initialCamRot = mainCamera.transform.rotation;

        // カメラに付いているスクリプトを全部停止しておく（自由移動系や追従スクリプト）
        cameraScripts = mainCamera.GetComponents<MonoBehaviour>();
        foreach (var script in cameraScripts)
        {
            script.enabled = false;
        }

        // テキストに縁取りを適用
        ApplyOutline(startText);
        ApplyOutline(MenuText);

        // ゲーム開始時はImageを非表示にする
        if (gameImage != null)
        {
            gameImage.gameObject.SetActive(false);
        }

        // 「ゲームスタート！」を表示
        if (startText != null)
        {
            startText.gameObject.SetActive(true);
            startText.text = "ゲームスタート！";
        }

        if (MenuText != null)
        {
            MenuText.gameObject.SetActive(true);
            MenuText.text = "このもりからむしをみつけてつかまえよう！";
        }

        // コルーチンで4秒後に解除
        StartCoroutine(ReleaseStartSequence());
    }

    // TextMeshProに縁取りを適用するメソッド
    void ApplyOutline(TextMeshProUGUI textComponent)
    {
        if (textComponent != null)
        {
            // アウトラインを有効化
            textComponent.outlineWidth = outlineWidth;
            textComponent.outlineColor = outlineColor;
        }
    }

    System.Collections.IEnumerator ReleaseStartSequence()
    {
        // 4秒間待機
        yield return new WaitForSeconds(4f);

        // 「ゲームスタート！」を消す
        if (startText != null)
        {
            startText.gameObject.SetActive(false);
            MenuText.gameObject.SetActive(false);
        }

        // Imageを表示する
        if (gameImage != null)
        {
            gameImage.gameObject.SetActive(true);
        }

        // カメラのスクリプトを有効化して動けるようにする
        foreach (var script in cameraScripts)
        {
            script.enabled = true;
        }
    }

    void LateUpdate()
    {
        // スクリプト無効化中はカメラを固定
        if (cameraScripts != null)
        {
            foreach (var script in cameraScripts)
            {
                if (!script.enabled)
                {
                    mainCamera.transform.position = initialCamPos;
                    mainCamera.transform.rotation = initialCamRot;
                    break;
                }
            }
        }
    }
}