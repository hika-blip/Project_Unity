using UnityEngine;

public class CanvasController : MonoBehaviour
{
    private CanvasGroup canvasGroup;

    void Start()
    {
        // 同じGameObjectにアタッチされた CanvasGroup を取得
        canvasGroup = GetComponent<CanvasGroup>();
    }

    // 完全に透明にする
    public void HideCanvas()
    {
        canvasGroup.alpha = 0f;   // 透明
        canvasGroup.interactable = false; // ボタンなど操作不可にする場合
        canvasGroup.blocksRaycasts = false; // クリック判定を無効化
    }

    // 完全に表示する
    public void ShowCanvas()
    {
        canvasGroup.alpha = 1f;   // 不透明
        canvasGroup.interactable = true;
        canvasGroup.blocksRaycasts = true;
    }

    // 半透明にする
    public void SemiTransparent()
    {
        canvasGroup.alpha = 0.5f; // 半透明
        canvasGroup.interactable = true; // 操作可能にしたいならtrue
        canvasGroup.blocksRaycasts = true;
    }
}
