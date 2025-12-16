using System.Collections.Generic;
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
    public TextMeshProUGUI playerListText;

    public TextMeshProUGUI[] playerLobbyTexts;
    private string[] _playerSlotNicknames = new string[4];

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
        
        
        packet.header.size = (short)Marshal.SizeOf(packet);
        packet.header.type = PacketType.C2S_LOGIN_REQ;

        Debug.Log($"[전송 전] Converted size: {packet.header.size}, Converted type: {(short)packet.header.type}");
        packet.nickname = nicknameInput.text;

        // NetworkManager를 통해 전송
        NetworkManager.Instance.Send(packet);
    }

    void OnEnterRoomButtonClicked()
    {
        // 0번 방 입장 요청 패킷 생성
        PktC2SEnterRoomReq packet = new PktC2SEnterRoomReq();
        

        
        packet.header.size = (short)Marshal.SizeOf(packet);
        packet.header.type = PacketType.C2S_ENTER_ROOM_REQ;

        Debug.Log($"[전송 전] Converted size: {packet.header.size}, Converted type: {(short)packet.header.type}");
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
        Debug.Log("HandleLoginResponse 함수가 호출되었습니다.");
        statusText.text = res.success ? "로그인 성공!" : "로그인 실패!";
    }

    private void HandleEnterRoomResponse(PktS2CEnterRoomRes res)
    {
        Debug.Log("HandleEnterRoomResponse 함수가 호출되었습니다.");
        if (res.success)
        {
            statusText.text = "방 입장 성공! 다른 플레이어를 기다립니다...";

            // 데이터 모델을 먼저 초기화하고 서버가 준 정보로 채웁니다.
            System.Array.Clear(_playerSlotNicknames, 0, _playerSlotNicknames.Length);
            for (int i = 0; i < res.playerCount; i++)
            {
                var player = res.players[i];
                if (!string.IsNullOrEmpty(player.nickname))
                {
                    _playerSlotNicknames[player.slotIndex] = player.nickname;
                }
            }

            // UI 업데이트 함수를 '한 번만' 호출합니다.
            UpdateAllPlayerUI();
        }
        else
        {
            statusText.text = "방 입장 실패!";
        }
    }

    private void HandleUserEntered(PktS2CUserEnterNotify data)
    {
        Debug.Log($"HandleUserEntered 함수가 호출되었습니다: Nickname={data.nickname}, Slot={data.userSlotIndex}");
        playerLobbyTexts[data.userSlotIndex].text = data.nickname;
        statusText.text = $"{data.nickname} 님이 입장했습니다.";
        UpdateAllPlayerUI();
    }

    private void UpdateAllPlayerUI()
    {
        int playerCount = 0;
        string listStr = ""; // playerListText에 들어갈 문자열

        for (int i = 0; i < _playerSlotNicknames.Length; i++)
        {
            if (!string.IsNullOrEmpty(_playerSlotNicknames[i]))
            {
                // 데이터가 있으면 닉네임 표시
                playerLobbyTexts[i].text = _playerSlotNicknames[i];
                listStr += $"[{i}] {_playerSlotNicknames[i]}\n";
                playerCount++;
            }
            else
            {
                // 데이터가 없으면 빈 슬롯으로 표시
                playerLobbyTexts[i].text = "(빈 슬롯)";
            }
        }

        playerListText.text = $"현재 인원: {playerCount}\n" + listStr;
    }

    private void HandleGameStart()
    {
        Debug.Log("HandleGameStart 함수가 호출되었습니다! 게임 패널을 활성화합니다.");
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
