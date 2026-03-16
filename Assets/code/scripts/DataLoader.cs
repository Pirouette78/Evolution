using UnityEngine;
using System.Collections.Generic;
using System.IO;

[System.Serializable]
public class TechDataListWrapper {
    public List<TechData> technologies;
}

public class DataLoader : MonoBehaviour {
    public static DataLoader Instance { get; private set; }

    public Dictionary<string, TechData> TechDatabase { get; private set; } = new Dictionary<string, TechData>();

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(this.gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(this.gameObject);
        LoadTechnologies();
    }

    public void LoadTechnologies() {
        string filePath = Path.Combine(Application.streamingAssetsPath, "Data", "TechTree.json");
        
        if (File.Exists(filePath)) {
            string json = File.ReadAllText(filePath);
            TechDataListWrapper loadedData = JsonUtility.FromJson<TechDataListWrapper>(json);
            
            TechDatabase.Clear();
            if (loadedData != null && loadedData.technologies != null) {
                foreach (var tech in loadedData.technologies) {
                    TechDatabase[tech.id] = tech;
                }
            }
            
            Debug.Log($"Loaded {TechDatabase.Count} technologies from {filePath}");
        } else {
            Debug.LogWarning($"TechTree file not found at {filePath}");
        }
    }
    
    public TechData GetTech(string id) {
        if(TechDatabase.TryGetValue(id, out var tech)) {
            return tech;
        }
        Debug.LogError($"Tech {id} not found in database!");
        return default;
    }
}
