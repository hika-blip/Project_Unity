using UnityEngine;

public class ZoneObjectManager : MonoBehaviour
{
    [Header("Zone対応オブジェクト")]
    [Tooltip("Zone 1〜7 に対応する3Dオブジェクトを順番に入れてください")]
    public GameObject[] zoneObjects; // size = 7

    void Start()
    {
        int selectedZone = SelectionData.SelectedZone;
        Debug.Log($"Observation Scene: 選択されたゾーン = {selectedZone}");

        // すべて非表示にする
        foreach (GameObject obj in zoneObjects)
        {
            if (obj != null)
                obj.SetActive(false);
        }

        // 選ばれたゾーンだけ表示
        if (selectedZone >= 1 && selectedZone <= zoneObjects.Length)
        {
            if (zoneObjects[selectedZone - 1] != null)
            {
                zoneObjects[selectedZone - 1].SetActive(true);
                Debug.Log($"Zone {selectedZone} のオブジェクトを表示しました");
            }
        }
        else
        {
            Debug.LogWarning("選択されたゾーン番号が無効です");
        }
    }
}
