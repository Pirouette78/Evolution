using UnityEngine;
using UnityEngine.UIElements;
using Unity.Entities;

public class UIController : MonoBehaviour {
    
    private UIDocument uiDocument;
    private Label energyLabel;
    private Button pauseButton;

    private Label techNameLabel;
    private Label techCostLabel;
    private Button researchButton;

    private bool isPaused = false;
    private float previousTimeScale = 1f;

    private float currentP1Energy = 0f;
    private bool hasResearchedMembrane = false;
    private string targetTechId = "tech_membrane";
    private int techCostCache = 1500;

    private EntityManager entityManager;

    private void OnEnable() {
        uiDocument = GetComponent<UIDocument>();
        if (uiDocument == null) return;

        var root = uiDocument.rootVisualElement;
        
        energyLabel = root.Q<Label>("EnergyLabel");
        pauseButton = root.Q<Button>("PauseButton");

        techNameLabel = root.Q<Label>("TechNameLabel");
        techCostLabel = root.Q<Label>("TechCostLabel");
        researchButton = root.Q<Button>("ResearchButton");

        if (pauseButton != null) {
            pauseButton.clicked += TogglePause;
        }

        if (researchButton != null) {
            researchButton.clicked += OnResearchClicked;
        }

        entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;

        // Try getting initial tech data if available
        UpdateTechUI();
    }

    private void OnDisable() {
        if (pauseButton != null) {
            pauseButton.clicked -= TogglePause;
        }
        if (researchButton != null) {
            researchButton.clicked -= OnResearchClicked;
        }
    }

    private void TogglePause() {
        if (entityManager == default) return;
        
        var query = entityManager.CreateEntityQuery(typeof(GameTime));
        if (query.IsEmpty) {
            Debug.LogWarning("GameTime singleton not found!");
            return;
        }

        var gameTimeEntity = query.GetSingletonEntity();
        var gameTime = entityManager.GetComponentData<GameTime>(gameTimeEntity);

        isPaused = !isPaused;

        if (isPaused) {
            previousTimeScale = gameTime.TimeScale;
            gameTime.TimeScale = 0f;
            pauseButton.text = "Play";
            pauseButton.style.backgroundColor = new StyleColor(new Color(0.2f, 0.6f, 0.2f)); // Green when paused (ready to play)
        } else {
            gameTime.TimeScale = previousTimeScale > 0 ? previousTimeScale : 1f;
            pauseButton.text = "Pause";
            pauseButton.style.backgroundColor = new StyleColor(new Color(0.2f, 0.2f, 0.2f)); // Dark grey
        }

        entityManager.SetComponentData(gameTimeEntity, gameTime);
    }
    
    private void UpdateTechUI() {
        if (DataLoader.Instance != null && DataLoader.Instance.TechDatabase.Count > 0) {
            var tech = DataLoader.Instance.GetTech(targetTechId);
            if (tech.id != null) {
                techNameLabel.text = "Tech: " + LocalizationManager.Instance.GetText(tech.nameKey);
                techCostCache = tech.energyCost;
                techCostLabel.text = $"Coût : {techCostCache} Énergie";
            }
        }
    }

    private void OnResearchClicked() {
        if (hasResearchedMembrane) return;

        if (currentP1Energy >= techCostCache) {
            // Deduct from ECS
            DeductEnergyFromPlayer1(techCostCache);
            hasResearchedMembrane = true;
            
            researchButton.text = "Recherché !";
            researchButton.style.backgroundColor = new StyleColor(new Color(0.2f, 0.6f, 0.2f));
            researchButton.SetEnabled(false);
            techCostLabel.text = "Débloqué";
            
            // Dispatch event to ECS
            if (entityManager != default) {
                var evtEntity = entityManager.CreateEntity();
                entityManager.AddComponentData(evtEntity, new TechResearchedEvent { 
                    TechID = targetTechId
                });
            }

            Debug.Log($"Researched {targetTechId}!");
        } else {
            Debug.LogWarning("Not enough energy to research.");
        }
    }

    private void DeductEnergyFromPlayer1(float amountToDeduct) {
        if (entityManager == default) return;
        
        var query = entityManager.CreateEntityQuery(typeof(CellComponent));
        var cellEntities = query.ToEntityArray(Unity.Collections.Allocator.TempJob);
        
        float remainingToDeduct = amountToDeduct;

        foreach (var entity in cellEntities) {
            if (remainingToDeduct <= 0) break;

            var cell = entityManager.GetComponentData<CellComponent>(entity);
            if (cell.PlayerID == 0) {
                if (cell.Energy >= remainingToDeduct) {
                    cell.Energy -= remainingToDeduct;
                    remainingToDeduct = 0;
                } else {
                    remainingToDeduct -= cell.Energy;
                    cell.Energy = 0; // The cell will likely die soon from 0 energy in future mechanic, but for now we just drain it
                }
                entityManager.SetComponentData(entity, cell);
            }
        }
        
        cellEntities.Dispose();
    }
    
    private void Update() {
        // Optional: Update energy label based on total energy of Player 1 (PlayerID == 0)
        // For performance, this usually shouldn't run every frame like this in ECS,
        // but for a quick UI test it demonstrates ECS read access.
        if (energyLabel != null && entityManager != default) {
            var query = entityManager.CreateEntityQuery(typeof(CellComponent));
            var cellArray = query.ToComponentDataArray<CellComponent>(Unity.Collections.Allocator.TempJob);
            
            float totalEnergy = 0f;
            for (int i = 0; i < cellArray.Length; i++) {
                if (cellArray[i].PlayerID == 0) { // Player 1
                    totalEnergy += cellArray[i].Energy;
                }
            }
            cellArray.Dispose();

            currentP1Energy = totalEnergy;
            energyLabel.text = $"Énergie : {Mathf.FloorToInt(totalEnergy)}";

            if (researchButton != null && !hasResearchedMembrane) {
                researchButton.SetEnabled(currentP1Energy >= techCostCache);
            }
        }
    }
}
