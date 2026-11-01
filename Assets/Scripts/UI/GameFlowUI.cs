using UnityEngine;
using UnityEngine.UI;
using TMPro;
using TagGame.Gameplay;

namespace TagGame.UI
{
    /// <summary>
    /// Wires the Start / Pause / Game Over screens to TagGameManager. Built
    /// automatically by Assets/Editor/TagGamePracticeSceneSetup.cs — see that
    /// script for the actual Canvas/panel/button hierarchy it creates.
    /// </summary>
    public class GameFlowUI : MonoBehaviour
    {
        [SerializeField] private TagGameManager gameManager;

        [Header("Start Screen")]
        [SerializeField] private GameObject startPanel;
        [SerializeField] private Button startButton;

        [Header("Pause Screen")]
        [SerializeField] private GameObject pausePanel;
        [SerializeField] private Button resumeButton;
        [SerializeField] private Button restartFromPauseButton;

        [Header("Game Over Screen")]
        [SerializeField] private GameObject gameOverPanel;
        [SerializeField] private TMP_Text gameOverText;
        [SerializeField] private Button playAgainButton;

        public GameObject StartPanel => startPanel;
        public Button StartButton => startButton;

        private void Awake()
        {
            startButton.onClick.AddListener(HandleStartClicked);
            resumeButton.onClick.AddListener(HandleResumeClicked);
            restartFromPauseButton.onClick.AddListener(HandleRestartClicked);
            playAgainButton.onClick.AddListener(HandleRestartClicked);
            AddMenuButton();

            // Known starting state regardless of what was left active in the editor.
            startPanel.SetActive(true);
            pausePanel.SetActive(false);
            gameOverPanel.SetActive(false);
        }

        private void OnEnable()
        {
            gameManager.OnPauseChanged += HandlePauseChanged;
            gameManager.OnRoundEnded += HandleRoundEnded;
        }

        private void OnDisable()
        {
            gameManager.OnPauseChanged -= HandlePauseChanged;
            gameManager.OnRoundEnded -= HandleRoundEnded;
        }

        private void HandleStartClicked()
        {
            startPanel.SetActive(false);
            gameManager.BeginGame();
        }

        private void HandlePauseChanged(bool isPaused)
        {
            pausePanel.SetActive(isPaused);
        }

        /// <summary>Adds a "Menu" button to the pause screen, under Restart, that goes back to the start screen.</summary>
        private void AddMenuButton()
        {
            var src = restartFromPauseButton;
            var go = Instantiate(src.gameObject, src.transform.parent);
            go.name = "MenuButton";
            var rt = (RectTransform)go.transform;
            var srcRt = (RectTransform)src.transform;
            var resumeRt = (RectTransform)resumeButton.transform;
            Vector2 step = srcRt.anchoredPosition - resumeRt.anchoredPosition;
            if (step.sqrMagnitude < 1f) step = new Vector2(0f, -(srcRt.sizeDelta.y + 20f));
            rt.anchoredPosition = srcRt.anchoredPosition + step;
            var label = go.GetComponentInChildren<TMP_Text>();
            if (label != null) label.text = "Menu";
            var btn = go.GetComponent<Button>();
            btn.onClick = new Button.ButtonClickedEvent();
            btn.onClick.AddListener(HandleMenuClicked);
        }

        private void HandleMenuClicked()
        {
            pausePanel.SetActive(false);
            gameOverPanel.SetActive(false);
            gameManager.QuitToMenu();
            startPanel.SetActive(true);
        }

        private void HandleResumeClicked()
        {
            gameManager.TogglePause();
        }

        private void HandleRestartClicked()
        {
            pausePanel.SetActive(false);
            gameOverPanel.SetActive(false);
            gameManager.RestartRound();
        }

        private void HandleRoundEnded(bool survived)
        {
            gameOverText.text = gameManager.ResultSummary;
            gameOverPanel.SetActive(true);
        }
    }
}
