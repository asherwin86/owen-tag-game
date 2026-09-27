using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using TagGame.Gameplay;
using TagGame.Services;

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

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Selection.activeGameObject = managerGo;

            Debug.Log("[TagGame] Practice scene set up: Bootstrap, Ground, Player, TaggerAI prefab (4 spawn points), GameManager. " +
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
                "Bootstrap", "Ground", "Player", "TaggerSpawnPoints", "GameManager"
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
    }
}
