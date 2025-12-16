using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;

public class NetworkManager : MonoBehaviour
{
    public static NetworkManager Instance { get; private set; }

    private TcpClient _client;
    private NetworkStream _stream;
    private byte[] _receiveBuffer = new byte[4096];

    private bool _isDisconnectRequested = false;

    private struct QueuedPacket
    {
        public PacketType type;
        public byte[] data;
    }

    // 메인 스레드에서 처리할 패킷들을 담아두는 스레드 안전 큐
    private ConcurrentQueue<QueuedPacket> _packetQueue = new ConcurrentQueue<QueuedPacket>();

    // 서버 IP, 포트. Inspector에서 설정 가능
    public string serverIp = "127.0.0.1";
    public int serverPort = 12345;

    // UI 업데이트를 위한 델리게이트와 이벤트
    public Action<string> OnStatusTextChanged;
    public Action OnGameStart;
    public Action<string> OnNewQuestion;
    public Action<PktS2CRoundResultNotify> OnRoundResult;
    public Action<PktS2CGameOverNotify> OnGameOver;
    public Action<PktS2CUserEnterNotify> OnUserEnteredRoom;
    public Action<PktS2CEnterRoomRes> OnEnterRoomResponse;
    public Action<PktS2CLoginRes> OnLoginResponse;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public async void ConnectToServer()
    {
        if (_client != null && _client.Connected) return;

        try
        {
            _client = new TcpClient();
            OnStatusTextChanged?.Invoke("서버에 접속 중...");
            await _client.ConnectAsync(serverIp, serverPort);
            _stream = _client.GetStream();
            OnStatusTextChanged?.Invoke("서버 접속 성공!");

            // 접속 성공 후, 비동기 수신 루프 시작
            Task.Run(ReceiveLoop);
        }
        catch (Exception e)
        {
            OnStatusTextChanged?.Invoke($"서버 접속 실패: {e.Message}");
            _client = null;
        }
    }

    // [핵심] 비동기 수신 루프 (백그라운드 스레드에서 실행됨)
    private async Task ReceiveLoop()
    {
        var receiveBuffer = new byte[4096];
        var memoryStream = new MemoryStream();
        int headerSize = Marshal.SizeOf<PacketHeader>();

        while (_client != null && _client.Connected)
        {
            try
            {
                int bytesRead = await _stream.ReadAsync(receiveBuffer, 0, receiveBuffer.Length);
                Debug.Log($"[ReceiveLoop] _stream.ReadAsync completed. Bytes read: {bytesRead}");
                if (bytesRead <= 0)
                {
                    Debug.Log("서버로부터 연결이 끊겼습니다.");
                    _isDisconnectRequested = true;
                    break;
                }

                // 받은 데이터를 메모리 스트림의 끝에 추가합니다.
                memoryStream.Write(receiveBuffer, 0, bytesRead);

                // 스트림에 완전한 패킷이 하나 이상 있는지 확인하고 처리합니다.
                while (true)
                {
                    if (memoryStream.Length < 4) // 헤더 크기
                        break;

                    byte[] buffer = memoryStream.GetBuffer();

                    // 서버가 보낸 Big Endian 바이트를 직접 조합
                    short packetSize = (short)((buffer[0] << 8) | buffer[1]);

                    // 타입도 동일하게 처리합니다.
                    PacketType packetType = (PacketType)((buffer[2] << 8) | buffer[3]);

                    // --- 디버깅 로그 ---
                    Debug.Log($"[PacketLoop] Parsed Header -> Size: {packetSize}, Type: {packetType}");

                    if (packetSize <= 0 || packetSize > 4096)
                    {
                        Debug.LogError($"[PacketLoop] Invalid packet size: {packetSize}. Disconnecting.");
                        _isDisconnectRequested = true;
                        return;
                    }

                    if (memoryStream.Length < packetSize)
                    {
                        Debug.Log($"[PacketLoop] Stream length ({memoryStream.Length}) is smaller than required packet size ({packetSize}). Breaking for more data.");
                        break;
                    }

                    // --- 패킷 처리 ---
                    byte[] packetData = new byte[packetSize];
                    Array.Copy(buffer, 0, packetData, 0, packetSize);
                    _packetQueue.Enqueue(new QueuedPacket { type = packetType, data = packetData });
                    Debug.Log($"[ReceiveLoop] Enqueued Packet! Type: {packetType}, Size: {packetSize}");

                    int remainingSize = (int)memoryStream.Length - packetSize;
                    if (remainingSize > 0)
                    {
                        byte[] remainingData = new byte[remainingSize];
                        Array.Copy(buffer, packetSize, remainingData, 0, remainingSize);
                        memoryStream = new MemoryStream();
                        memoryStream.Write(remainingData, 0, remainingData.Length);
                    }
                    else
                    {
                        memoryStream = new MemoryStream();
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Receive error: {e.Message}\n{e.StackTrace}");
                _isDisconnectRequested = true;
                break;
            }
        }
    }

    // 메인 스레드에서 실행되는 Update 함수
    void Update()
    {
        if (_isDisconnectRequested)
        {
            Disconnect();
            _isDisconnectRequested = false; // 처리 후 플래그 리셋
            return; // 다른 처리를 하지 않고 종료
        }

        if (_packetQueue.Count > 0)
        {
            Debug.Log($"[Update] Found {_packetQueue.Count} packets in queue.");
        }
        // 큐에 쌓인 패킷들을 하나씩 처리
        while (_packetQueue.TryDequeue(out QueuedPacket packet))
        {
            Debug.Log($"[Update] Dequeued packet. Type: {packet.type}");
            ProcessPacket(packet.type, packet.data);
        }
    }

    // 패킷 종류에 따라 분기 처리 (메인 스레드에서 실행됨)
    private void ProcessPacket(PacketType type, byte[] data)
    {
        Debug.Log($"[ProcessPacket] Processing packet type: {type}");
        switch (type)
        {
            case PacketType.S2C_LOGIN_RES:
                PktS2CLoginRes loginRes = BytesToStruct<PktS2CLoginRes>(data);
                OnLoginResponse?.Invoke(loginRes);
                break;

            case PacketType.S2C_ENTER_ROOM_RES:
                PktS2CEnterRoomRes enterRes = BytesToStruct<PktS2CEnterRoomRes>(data);
                // if (enterRes.success)
                // {
                //     string msg = $"방 입장 성공! 현재 인원: {enterRes.playerCount}\n";
                //     for (int i = 0; i < enterRes.players.Length; ++i)
                //     {
                //         if (enterRes.players[i].slotIndex >= 0 && !string.IsNullOrEmpty(enterRes.players[i].nickname))
                //         {
                //             msg += $"[{enterRes.players[i].slotIndex}] {enterRes.players[i].nickname}\n";
                //         }
                //     }
                //     OnStatusTextChanged?.Invoke(msg);
                // }
                // else
                // {
                //     OnStatusTextChanged?.Invoke("방 입장 실패 (꽉 찼거나 오류 발생)");
                // }
                OnEnterRoomResponse?.Invoke(enterRes);
                break;

            case PacketType.S2C_USER_ENTER_NOTIFY:
                Debug.Log("S2C_USER_ENTER_NOTIFY 처리 시작!");
                PktS2CUserEnterNotify enterNotify = BytesToStruct<PktS2CUserEnterNotify>(data);
                Debug.Log($"[{enterNotify.userSlotIndex}] {enterNotify.nickname} 님이 방에 입장했습니다.");
                OnUserEnteredRoom?.Invoke(enterNotify); // UI 매니저에 알림
                break;

            case PacketType.S2C_GAME_START_NOTIFY:
                Debug.Log("서버로부터 게임 시작 신호 수신!");
                OnGameStart?.Invoke(); // 게임 시작 이벤트를 UI에 알림
                break;
            case PacketType.S2C_NEW_QUESTION_NOTIFY:
                PktS2CNewQuestionNotify questionPkt = BytesToStruct<PktS2CNewQuestionNotify>(data);
                Debug.Log($"새 문제 수신: {questionPkt.question}");
                OnNewQuestion?.Invoke(questionPkt.question); // 새 문제 이벤트를 UI에 알림
                break;
            case PacketType.S2C_ROUND_RESULT_NOTIFY:
                PktS2CRoundResultNotify resultPkt = BytesToStruct<PktS2CRoundResultNotify>(data);
                OnRoundResult?.Invoke(resultPkt); // 라운드 결과 이벤트를 UI에 알림
                break;
            case PacketType.S2C_GAME_OVER_NOTIFY:
                PktS2CGameOverNotify overPkt = BytesToStruct<PktS2CGameOverNotify>(data);
                OnGameOver?.Invoke(overPkt); // 게임 종료 이벤트를 UI에 알림
                break;

        }
    }


    public static T BytesToStruct<T>(byte[] buffer, int offset = 0) where T : struct
    {
        T structure = default(T);
        int size = Marshal.SizeOf(typeof(T));
        IntPtr ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.Copy(buffer, offset, ptr, size);
            structure = (T)Marshal.PtrToStructure(ptr, typeof(T));
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
        return structure;
    }

    public static byte[] StructToBytes<T>(T structure) where T : struct
    {
        int size = Marshal.SizeOf(typeof(T));
        byte[] buffer = new byte[size];
        IntPtr ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(structure, ptr, true);
            Marshal.Copy(ptr, buffer, 0, size);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
        return buffer;
    }

    public void Send<T>(T packet) where T : struct
    {
        if (_client == null || !_client.Connected) return;
        try
        {
            byte[] data = StructToBytes(packet);
            // a. 먼저 현재 리틀 엔디안 값을 읽어옵니다.
            short size = BitConverter.ToInt16(data, 0);
            short type = BitConverter.ToInt16(data, 2);

            Debug.Log($"[전송 전] Original size: {size}, Original type: {type}");

            // 3. BitConverter를 사용하지 않고, 수동으로 Big Endian 바이트를 data 배열에 직접 씁니다.
            //    (size >> 8)은 상위 8비트를, (byte)size는 하위 8비트를 의미합니다.
            data[0] = (byte)(size >> 8);
            data[1] = (byte)size;
            data[2] = (byte)(type >> 8);
            data[3] = (byte)type;

            // 로그를 찍어서 확인하려면, 변환된 값을 다시 읽어봐야 합니다.
            short convertedSize = (short)((data[0] << 8) | data[1]);
            Debug.Log($"[전송 후] Converted size: {convertedSize}"); // 이 값은 이제 8이 나와야 합니다.
            // ----------------------------------------------------

            // 디버깅 로그 (이제 이 로그에서 Converted size가 제대로 보여야 합니다)
            _stream.Write(data, 0, data.Length);
        }
        catch (Exception e)
        {
            Debug.LogError($"Send error: {e.Message}");
        }
    }

    private void Disconnect()
    {
        if (_client != null)
        {
            _client.Close();
            _client = null;
            OnStatusTextChanged?.Invoke("서버와 연결이 끊겼습니다.");
        }
    }

    void OnApplicationQuit()
    {
        Disconnect();
    }
}
