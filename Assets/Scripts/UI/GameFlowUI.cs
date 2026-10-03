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
            gameOverText.text = survived ? "Time's up — you survived!" : "You got tagged. You lose!";
            gameOverPanel.SetActive(true);
        }
    }
}
