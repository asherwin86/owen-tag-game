using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using TMPro;
using TagGame.Gameplay;
using TagGame.Services;
using TagGame.UI;

namespace TagGame.EditorTools
{
    /// <summary>
    /// One-click builder for the practice-mode scene (player + AI taggers,
    /// no networking). Run it from the Editor menu, then press Play.
    /// This is an Editor-only tool (lives under Assets/Editor) and is not
    /// included in game builds.
    /// </summary>
    public static class TagGamePracticeSceneSetup
    {
        [MenuItem("Tag Game/Setup Practice Scene")]
        public static void SetupScene()
        {
            CleanUpPreviousSetup();

            // 1. Bootstrap — UGS init, Remote Config, Leaderboard submission.
            //    Safe to have even if UGS isn't linked yet; calls just no-op/log.
            var bootstrap = new GameObject("Bootstrap");
            bootstrap.AddComponent<UGSBootstrap>();
            bootstrap.AddComponent<RemoteConfigManager>();
            bootstrap.AddComponent<LeaderboardManager>();

            // 2. Ground
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(3f, 1f, 3f);
            var grassMat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Grass.mat");
            if (grassMat != null) ground.GetComponent<Renderer>().sharedMaterial = grassMat;

            // 3. Player
            var player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            player.name = "Player";
            player.transform.position = new Vector3(0f, 1f, 0f);
            Object.DestroyImmediate(player.GetComponent<CapsuleCollider>()); // CharacterController replaces it
            ApplyColor(player, Color.cyan);
            player.AddComponent<PlayerController>(); // [RequireComponent] auto-adds CharacterController

            // 3b. Camera follow — without this the scene's static default camera
            // never tracks the player, so both player and taggers walk off-screen
            // within a few seconds and the game looks "frozen" even though it isn't.
            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                var follow = mainCamera.GetComponent<FollowCamera>();
                if (follow == null)
                {
                    follow = mainCamera.gameObject.AddComponent<FollowCamera>();
                }
                follow.SetTarget(player.transform);
            }
            else
            {
                Debug.LogWarning("[TagGame] No Main Camera found in the scene — add one and a FollowCamera component manually so the view tracks the player.");
            }

            // 4. TaggerAI prefab
            var taggerGo = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            taggerGo.name = "TaggerAI";
            ApplyColor(taggerGo, Color.red);
            // No physical collider: TaggerAI checks distance to the player directly rather
            // than colliding with it. A solid collider here is what caused multiple taggers
            // piling on the player's CharacterController to spike frame time badly.
            Object.DestroyImmediate(taggerGo.GetComponent<CapsuleCollider>());
            taggerGo.AddComponent<TaggerAI>();

            if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
            {
                AssetDatabase.CreateFolder("Assets", "Prefabs");
            }
            const string prefabPath = "Assets/Prefabs/TaggerAI.prefab";
            GameObject taggerPrefab = PrefabUtility.SaveAsPrefabAsset(taggerGo, prefabPath);
            Object.DestroyImmediate(taggerGo);

            // 5. Spawn points, spread around the plane
            var spawnParent = new GameObject("TaggerSpawnPoints");
            Vector3[] positions =
            {
                new Vector3(6f, 1f, 6f),
                new Vector3(-6f, 1f, 6f),
                new Vector3(6f, 1f, -6f),
                new Vector3(-6f, 1f, -6f),
            };
            var spawnPoints = new Transform[positions.Length];
            for (int i = 0; i < positions.Length; i++)
            {
                var sp = new GameObject($"TaggerSpawn{i + 1}");
                sp.transform.position = positions[i];
                sp.transform.SetParent(spawnParent.transform);
                spawnPoints[i] = sp.transform;
            }

            // 6. Game Manager, wired up via SerializedObject (fields are private [SerializeField])
            var managerGo = new GameObject("GameManager");
            var manager = managerGo.AddComponent<TagGameManager>();

            var so = new SerializedObject(manager);
            so.FindProperty("player").objectReferenceValue = player.GetComponent<PlayerController>();
            so.FindProperty("taggerPrefab").objectReferenceValue = taggerPrefab.GetComponent<TaggerAI>();

            var spawnPointsProp = so.FindProperty("taggerSpawnPoints");
            spawnPointsProp.arraySize = spawnPoints.Length;
            for (int i = 0; i < spawnPoints.Length; i++)
            {
                spawnPointsProp.GetArrayElementAtIndex(i).objectReferenceValue = spawnPoints[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            // 7. Start / Pause / Game Over screens.
            EnsureEventSystem();
            BuildGameFlowUI(manager);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Selection.activeGameObject = managerGo;

            Debug.Log("[TagGame] Practice scene set up: Bootstrap, Ground, Player, TaggerAI prefab (4 spawn points), GameManager, GameUI (Start/Pause/Game Over). " +
                      "Save the scene (Ctrl+S) and press Play to test.");
        }

        /// <summary>
        /// Removes any GameObjects a previous run of this menu item left in the
        /// scene, by the exact names this script creates. Makes the command safe
        /// to run more than once instead of piling up duplicate players/taggers/
        /// managers each time (which is what caused the earlier freeze).
        /// </summary>
        private static void CleanUpPreviousSetup()
        {
            // GameObject.Find only returns the first match, which would leave
            // earlier duplicates behind — so walk every root object instead and
            // remove every match by name (including stray "TaggerAI(Clone)"
            // instances a previous Play session may have left in the scene).
            var scene = EditorSceneManager.GetActiveScene();
            var namesToRemove = new System.Collections.Generic.HashSet<string>
            {
                "Bootstrap", "Ground", "Player", "TaggerSpawnPoints", "GameManager", "GameUI", "EventSystem"
            };

            foreach (var root in scene.GetRootGameObjects())
            {
                if (namesToRemove.Contains(root.name) || root.name.StartsWith("TaggerAI"))
                {
                    Object.DestroyImmediate(root);
                }
            }
        }

        private static void ApplyColor(GameObject go, Color color)
        {
            var renderer = go.GetComponent<Renderer>();
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader) { color = color };
            renderer.sharedMaterial = mat;
        }

        /// <summary>
        /// UI needs an EventSystem to route clicks at all. Uses the new Input
        /// System's UI module (InputSystemUIInputModule) rather than the legacy
        /// StandaloneInputModule, since this project's Active Input Handling is
        /// set to "Input System Package (New)" only — the legacy module reads
        /// through the old UnityEngine.Input class, which is disabled here.
        /// </summary>
        private static void EnsureEventSystem()
        {
            if (Object.FindFirstObjectByType<EventSystem>() != null)
            {
                return;
            }

            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<InputSystemUIInputModule>();
        }

        /// <summary>
        /// Builds the Start / Pause / Game Over screens and wires them to the
        /// round controller via GameFlowUI. Kept deliberately plain (flat-color
        /// panels and buttons) — this is a functional scaffold, not final art.
        /// </summary>
        private static void BuildGameFlowUI(TagGameManager manager)
        {
            var canvasGo = new GameObject("GameUI");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            canvasGo.AddComponent<GraphicRaycaster>();

            GameObject startPanel = CreatePanel(canvasGo.transform, "StartPanel", new Color(0f, 0f, 0f, 0.75f));
            CreateText(startPanel.transform, "Title", "TAG GAME", 64, new Vector2(0f, 80f), new Vector2(800f, 100f));
            Button startButton = CreateButton(startPanel.transform, "StartButton", "Start", new Vector2(0f, -40f), new Vector2(260f, 80f));

            GameObject pausePanel = CreatePanel(canvasGo.transform, "PausePanel", new Color(0f, 0f, 0f, 0.75f));
            CreateText(pausePanel.transform, "Title", "PAUSED", 56, new Vector2(0f, 120f), new Vector2(800f, 100f));
            Button resumeButton = CreateButton(pausePanel.transform, "ResumeButton", "Resume", new Vector2(0f, 10f), new Vector2(260f, 80f));
            Button restartFromPauseButton = CreateButton(pausePanel.transform, "RestartButton", "Restart", new Vector2(0f, -90f), new Vector2(260f, 80f));
            pausePanel.SetActive(false);

            GameObject gameOverPanel = CreatePanel(canvasGo.transform, "GameOverPanel", new Color(0f, 0f, 0f, 0.85f));
            TMP_Text gameOverText = CreateText(gameOverPanel.transform, "ResultText", "GAME OVER", 56, new Vector2(0f, 80f), new Vector2(900f, 140f));
            Button playAgainButton = CreateButton(gameOverPanel.transform, "PlayAgainButton", "Play Again", new Vector2(0f, -60f), new Vector2(300f, 80f));
            gameOverPanel.SetActive(false);

            var flow = canvasGo.AddComponent<GameFlowUI>();
            var flowSo = new SerializedObject(flow);
            flowSo.FindProperty("gameManager").objectReferenceValue = manager;
            flowSo.FindProperty("startPanel").objectReferenceValue = startPanel;
            flowSo.FindProperty("startButton").objectReferenceValue = startButton;
            flowSo.FindProperty("pausePanel").objectReferenceValue = pausePanel;
            flowSo.FindProperty("resumeButton").objectReferenceValue = resumeButton;
            flowSo.FindProperty("restartFromPauseButton").objectReferenceValue = restartFromPauseButton;
            flowSo.FindProperty("gameOverPanel").objectReferenceValue = gameOverPanel;
            flowSo.FindProperty("gameOverText").objectReferenceValue = gameOverText;
            flowSo.FindProperty("playAgainButton").objectReferenceValue = playAgainButton;
            flowSo.ApplyModifiedPropertiesWithoutUndo();
        }

        private static GameObject CreatePanel(Transform parent, string name, Color backgroundColor)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var image = go.AddComponent<Image>();
            image.color = backgroundColor;
            image.raycastTarget = true; // blocks clicks to whatever is behind it while shown

            return go;
        }

        private static TMP_Text CreateText(Transform parent, string name, string text, int fontSize, Vector2 anchoredPos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPos;

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            tmp.raycastTarget = false;

            return tmp;
        }

        private static Button CreateButton(Transform parent, string name, string label, Vector2 anchoredPos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPos;

            var image = go.AddComponent<Image>();
            image.color = new Color(0.85f, 0.85f, 0.85f, 1f);

            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.highlightedColor = new Color(1f, 1f, 1f, 1f);
            colors.pressedColor = new Color(0.6f, 0.6f, 0.6f, 1f);
            button.colors = colors;

            CreateLabel(go.transform, label);

            return button;
        }

        private static void CreateLabel(Transform parent, string text)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = 32;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.black;
            tmp.raycastTarget = false;
        }
    }
}
