using System.Collections.Generic;
using UnityEngine;
using System.IO;

[System.Serializable]
public struct LocalizedString {
    public string key;
    public string value;
}

[System.Serializable]
public class LocalizationData {
    public string language;
    public List<LocalizedString> strings;
}

public class LocalizationManager : MonoBehaviour {
    public static LocalizationManager Instance { get; private set; }

    private Dictionary<string, string> localizedText;
    private string currentLanguage = "fr";

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(this.gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(this.gameObject);
        localizedText = new Dictionary<string, string>();
        LoadLanguage(currentLanguage);
    }

    public void LoadLanguage(string langName) {
        string filePath = Path.Combine(Application.streamingAssetsPath, "Languages", $"{langName}.json");
        
        if (File.Exists(filePath)) {
            string dataAsJson = File.ReadAllText(filePath);
            LocalizationData loadedData = JsonUtility.FromJson<LocalizationData>(dataAsJson);
            
            localizedText.Clear();
            currentLanguage = loadedData.language;

            if (loadedData != null && loadedData.strings != null) {
                foreach (var str in loadedData.strings) {
                    localizedText[str.key] = str.value;
                }
            }
            
            Debug.Log($"Loaded Language: {currentLanguage} with {localizedText.Count} entries.");
        } else {
            Debug.LogError($"Cannot find language file at: {filePath}");
        }
    }

    public string GetText(string key) {
        if (localizedText.TryGetValue(key, out string value)) {
            return value;
        }
        return $"MISSING_{key}";
    }
}
