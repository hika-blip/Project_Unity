using UnityEngine;
using UnityEngine.SceneManagement;

public class ReturnToTitle : MonoBehaviour
{
    public float waitTime = 5f; // Titleシーンに戻るまでの待ち時間（秒）

    void Start()
    {
        // 指定秒数後にTitleシーンへ遷移
        Invoke("GoToTitleScene", waitTime);
    }

    void GoToTitleScene()
    {
        // Build Settingsで設定されたインデックス番号「0」のシーンへ移動
        SceneManager.LoadScene(0);
    }
}
