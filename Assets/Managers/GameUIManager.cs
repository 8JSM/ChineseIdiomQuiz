using System.Collections;
using System.Runtime.InteropServices;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class GameUIManager : MonoBehaviour
{
    // LobbyUIManager가 제어할 패널 참조
    [Header("Panel References")]
    public GameObject lobbyPanel;
    public GameObject gamePanel;

    [Header("Game UI Elements")]
    public TextMeshProUGUI questionText;
    public TMP_InputField answerInput; // TextMeshPro의 InputField
    public Button submitButton;
    public TextMeshProUGUI[] playerScoreTexts; // 4명의 플레이어 이름과 점수
    public TextMeshProUGUI notificationText; // "O O O 정답!" 등을 표시

    [Header("Game Over UI")]
    public GameObject gameOverPanel;
    public TextMeshProUGUI winnerText;

    // 이 스크립트가 활성화될 때(Awake)와 비활성화될 때(OnDestroy) 이벤트를 구독/해제합니다.
    void Awake()
    {
        // NetworkManager의 게임 관련 방송(이벤트)을 구독 신청
        NetworkManager.Instance.OnNewQuestion += HandleNewQuestion;
        NetworkManager.Instance.OnRoundResult += HandleRoundResult;
        NetworkManager.Instance.OnGameOver += HandleGameOver;

        // 정답 제출 버튼에 함수 연결
        submitButton.onClick.AddListener(OnSubmitAnswer);
    }

    // 게임 시작 시 LobbyUIManager가 gamePanel을 활성화하면 이 함수가 자동 호출됩니다.
    void OnEnable()
    {
        // 게임 시작 시 UI 초기화
        gameOverPanel.SetActive(false);
        notificationText.gameObject.SetActive(false);
        questionText.text = "잠시 후 문제가 출제됩니다...";
        ClearScores(); // 점수판 초기화
        SetInputActive(false);
    }

    // NetworkManager가 "새 문제!" 방송을 하면 호출될 함수
    void HandleNewQuestion(string question)
    {
        questionText.text = question;
        answerInput.text = "";
        SetInputActive(true);
        answerInput.ActivateInputField(); // 입력창에 바로 포커스
    }

    // NetworkManager가 "라운드 결과!" 방송을 하면 호출될 함수
    void HandleRoundResult(PktS2CRoundResultNotify result)
    {
        SetInputActive(false); // 정답을 맞췄으므로 입력 비활성화

        // 점수판 업데이트
        UpdateScores(result.players);

        // 결과 알림
        string winnerNickname = "알 수 없음";
        // 서버가 보내준 winnerSlotIndex를 사용해 플레이어 배열에서 닉네임 찾기
        foreach (var player in result.players)
        {
            if (player.slotIndex == result.winnerSlotIndex)
            {
                winnerNickname = player.nickname;
                break;
            }
        }

        StartCoroutine(ShowNotification($"정답: {result.answer}\n({winnerNickname}님 1점 획득!)", 3f));
    }

    // NetworkManager가 "게임 종료!" 방송을 하면 호출될 함수
    void HandleGameOver(PktS2CGameOverNotify result)
    {
        SetInputActive(false);
        gamePanel.SetActive(false); // 메인 게임 화면 끄기
        gameOverPanel.SetActive(true);
        winnerText.text = $"최종 우승자\n{result.winnerNickname}";

        // 5초 뒤 로비로 돌아가기
        StartCoroutine(ReturnToLobby(5f));
    }

    // 정답 제출 버튼 클릭 시
    void OnSubmitAnswer()
    {
        if (string.IsNullOrEmpty(answerInput.text)) return;

        PktC2SSubmitAnswerReq packet = new PktC2SSubmitAnswerReq();


        packet.answer = answerInput.text;

        NetworkManager.Instance.Send(packet);

        SetInputActive(false); // 제출 후 입력창 비활성화 (중복 제출 방지)
    }

    // --- Helper Functions ---

    void SetInputActive(bool active)
    {
        answerInput.interactable = active;
        submitButton.interactable = active;
    }

    void ClearScores()
    {
        // LobbyUIManager의 playerLobbyTexts를 참조하여 초기 이름 설정 가능
        for (int i = 0; i < playerScoreTexts.Length; i++)
        {
            playerScoreTexts[i].text = $"Player {i + 1}: 0"; // 기본값
        }
    }

    void UpdateScores(PlayerInfo[] players)
    {
        foreach (var scoreText in playerScoreTexts)
        {
            scoreText.gameObject.SetActive(false);
        }

        // 2. 서버로부터 받은 플레이어 정보로 UI를 업데이트합니다.
        foreach (var player in players)
        {
            // 3. 닉네임이 비어있거나 NULL이면 쓰레기 데이터일 가능성이 높으므로 건너뜁니다.
            if (string.IsNullOrEmpty(player.nickname))
                continue;

            // 4. (가장 중요) slotIndex가 playerScoreTexts 배열의 유효한 범위 내에 있는지 확인합니다.
            if (player.slotIndex >= 0 && player.slotIndex < playerScoreTexts.Length)
            {
                playerScoreTexts[player.slotIndex].text = $"{player.nickname}: {player.score}";
                playerScoreTexts[player.slotIndex].gameObject.SetActive(true);
            }
        }
    }

    IEnumerator ShowNotification(string message, float duration)
    {
        notificationText.text = message;
        notificationText.gameObject.SetActive(true);
        yield return new WaitForSeconds(duration);
        notificationText.gameObject.SetActive(false);
    }

    IEnumerator ReturnToLobby(float delay)
    {
        yield return new WaitForSeconds(delay);
        gameOverPanel.SetActive(false);
        lobbyPanel.SetActive(true); // 로비 화면을 다시 켬
    }

    void OnDestroy()
    {
        if (NetworkManager.Instance != null)
        {
            // 구독했던 모든 이벤트 해제
            NetworkManager.Instance.OnNewQuestion -= HandleNewQuestion;
            NetworkManager.Instance.OnRoundResult -= HandleRoundResult;
            NetworkManager.Instance.OnGameOver -= HandleGameOver;
        }
    }
}
