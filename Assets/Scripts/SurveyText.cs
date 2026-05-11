using UnityEngine;
using TMPro;

public class TextDisplayManager : MonoBehaviour
{
    [Header("UI References")]
    public TextMeshProUGUI textDisplay;

    [Header("Text Content - 4 Types")]
    public string[] textMessages = new string[4]
    {
        "最初のメッセージ",
        "2番目のメッセージ",
        "3番目のメッセージ",
        "4番目のメッセージ"
    };

    private int currentIndex = 0;

    void Start()
    {
        // 4つ全てのテキストを表示
        ShowAllTexts();
    }

    // 4つ全てのテキストを箇条書きで表示
    public void ShowAllTexts()
    {
        string allText = "";
        for (int i = 0; i < textMessages.Length; i++)
        {
            allText += textMessages[i];
            if (i < textMessages.Length - 1)
            {
                allText += "\n"; // 改行
            }
        }
        textDisplay.text = allText;
    }

    // 指定したインデックスのテキストを表示(単独表示用)
    public void ShowText(int index)
    {
        if (index >= 0 && index < textMessages.Length)
        {
            currentIndex = index;
            textDisplay.text = textMessages[index];
        }
    }

    // 次のテキストを表示(循環)
    public void ShowNextText()
    {
        currentIndex = (currentIndex + 1) % textMessages.Length;
        ShowText(currentIndex);
    }

    // 前のテキストを表示(循環)
    public void ShowPreviousText()
    {
        currentIndex--;
        if (currentIndex < 0)
        {
            currentIndex = textMessages.Length - 1;
        }
        ShowText(currentIndex);
    }

    // 特定のテキスト内容を更新
    public void UpdateTextMessage(int index, string newText)
    {
        if (index >= 0 && index < textMessages.Length)
        {
            textMessages[index] = newText;
            if (currentIndex == index)
            {
                textDisplay.text = newText;
            }
        }
    }

    // 現在のインデックスを取得
    public int GetCurrentIndex()
    {
        return currentIndex;
    }
}