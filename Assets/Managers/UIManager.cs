using System.Runtime.InteropServices;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UIManager : MonoBehaviour
{
     [Header("UI Panels")]
    public GameObject lobbyPanel; // 로비 UI 전체를 담는 부모 오브젝트
    public GameObject gamePanel;  // 게임 UI 전체를 담는 부모 오브젝트 (GameUIManager가 제어)

    [Header("Lobby UI")]
    public TMP_InputField nicknameInput;
    public Button loginButton;
    public Button enterRoomButton;
    public TextMeshProUGUI statusText;

    public TextMeshProUGUI[] playerLobbyTexts;

    void Start()
    {
        // 버튼 클릭 이벤트에 함수 연결
        loginButton.onClick.AddListener(OnLoginButtonClicked);
        enterRoomButton.onClick.AddListener(OnEnterRoomButtonClicked);

        // NetworkManager의 이벤트에 UI 업데이트 함수 구독
        NetworkManager.Instance.OnLoginResponse += HandleLoginResponse;
        NetworkManager.Instance.OnEnterRoomResponse += HandleEnterRoomResponse;
        NetworkManager.Instance.OnUserEnteredRoom += HandleUserEntered;
        NetworkManager.Instance.OnGameStart += HandleGameStart;
        NetworkManager.Instance.OnStatusTextChanged += UpdateStatusText;

        // 처음엔 서버 접속부터
        NetworkManager.Instance.ConnectToServer();
    }

    void OnLoginButtonClicked()
    {
        if (string.IsNullOrEmpty(nicknameInput.text))
        {
            UpdateStatusText("닉네임을 입력하세요.");
            return;
        }

        // 로그인 패킷 생성
        PktC2SLoginReq packet = new PktC2SLoginReq();
        packet.header.size = (short)System.Net.IPAddress.HostToNetworkOrder((short)Marshal.SizeOf<PktC2SLoginReq>());
        packet.header.type = (PacketType)System.Net.IPAddress.HostToNetworkOrder((short)PacketType.C2S_LOGIN_REQ);
        packet.nickname = nicknameInput.text;

        // NetworkManager를 통해 전송
        NetworkManager.Instance.Send(packet);
    }

    void OnEnterRoomButtonClicked()
    {
        // 0번 방 입장 요청 패킷 생성
        PktC2SEnterRoomReq packet = new PktC2SEnterRoomReq();
        packet.header.size = (short)System.Net.IPAddress.HostToNetworkOrder((short)Marshal.SizeOf<PktC2SEnterRoomReq>());
        packet.header.type = (PacketType)System.Net.IPAddress.HostToNetworkOrder((short)PacketType.C2S_ENTER_ROOM_REQ);
        packet.roomIndex = 0;

        NetworkManager.Instance.Send(packet);
    }

    // NetworkManager가 호출해줄 UI 업데이트 함수
    public void UpdateStatusText(string message)
    {
        statusText.text = message;
    }

    private void HandleLoginResponse(PktS2CLoginRes res)
    {
        statusText.text = res.success ? "로그인 성공!" : "로그인 실패!";
    }

    private void HandleEnterRoomResponse(PktS2CEnterRoomRes res)
    {
        if (res.success)
        {
            statusText.text = "방 입장 성공! 다른 플레이어를 기다립니다...";
            // 기존 플레이어 정보로 UI 업데이트
            for (int i = 0; i < 4; i++) playerLobbyTexts[i].text = ""; // 초기화
            for (int i = 0; i < res.playerCount; i++)
            {
                var player = res.players[i];
                if (!string.IsNullOrEmpty(player.nickname))
                {
                    playerLobbyTexts[player.slotIndex].text = player.nickname;
                }
            }
        }
        else
        {
            statusText.text = "방 입장 실패!";
        }
    }

     private void HandleUserEntered(PktS2CUserEnterNotify data)
    {
        playerLobbyTexts[data.userSlotIndex].text = data.nickname;
        statusText.text = $"{data.nickname} 님이 입장했습니다.";
    }

    private void HandleGameStart()
    {
        // 게임이 시작되면 로비 패널은 비활성화하고 게임 패널을 활성화
        lobbyPanel.SetActive(false);
        gamePanel.SetActive(true);
    }

    // 구독 해제 (메모리 누수 방지)
    void OnDestroy()
    {
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.OnStatusTextChanged -= UpdateStatusText;

            NetworkManager.Instance.OnLoginResponse -= HandleLoginResponse;
            NetworkManager.Instance.OnEnterRoomResponse -= HandleEnterRoomResponse;
            NetworkManager.Instance.OnUserEnteredRoom -= HandleUserEntered;
            NetworkManager.Instance.OnGameStart -= HandleGameStart;
        }
    }
}
