using System.Runtime.InteropServices;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UIManager : MonoBehaviour
{
    public TMP_InputField nicknameInput;
    public Button loginButton;
    public Button enterRoomButton;
    public TextMeshProUGUI statusText;

    void Start()
    {
        // 버튼 클릭 이벤트에 함수 연결
        loginButton.onClick.AddListener(OnLoginButtonClicked);
        enterRoomButton.onClick.AddListener(OnEnterRoomButtonClicked);

        // NetworkManager의 이벤트에 UI 업데이트 함수 구독
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

    // 구독 해제 (메모리 누수 방지)
    void OnDestroy()
    {
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.OnStatusTextChanged -= UpdateStatusText;
        }
    }
}
