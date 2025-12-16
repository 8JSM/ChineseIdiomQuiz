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
        try
        {
            using (MemoryStream ms = new MemoryStream())
            {
                while (_client != null && _client.Connected)
                {
                    int bytesRead = await _stream.ReadAsync(_receiveBuffer, 0, _receiveBuffer.Length);
                    if (bytesRead <= 0) break; // 연결 끊김

                    long previousPosition = ms.Position;
                    ms.Seek(0, SeekOrigin.End); // 스트림의 끝으로 이동
                    ms.Write(_receiveBuffer, 0, bytesRead); // 받은 데이터를 메모리 스트림에 누적
                    ms.Position = previousPosition; // 원래 위치로 복원

                    // 완전한 패킷이 만들어졌는지 계속 확인
                    while (true)
                    {
                        ms.Position = 0; // 스트림 포인터를 처음으로
                        if (ms.Length < Marshal.SizeOf<PacketHeader>()) break; // 헤더 크기보다 작으면 break

                        // 헤더만 먼저 읽어서 패킷 전체 크기 확인
                        byte[] headerBytes = new byte[Marshal.SizeOf<PacketHeader>()];
                        ms.Read(headerBytes, 0, headerBytes.Length);
                        PacketHeader header = BytesToStruct<PacketHeader>(headerBytes);

                        short packetSize = System.Net.IPAddress.NetworkToHostOrder(header.size);
                        PacketType packetType = (PacketType)System.Net.IPAddress.NetworkToHostOrder((short)header.type);

                        Debug.Log($"[ReceiveLoop-Check] Buffer Length: {ms.Length}, Header Size: {packetSize} (Raw: {header.size})");
                        if (packetSize < 0 || packetSize > 4096) // 0보다 작거나 비정상적으로 큰 패킷은 무시
                        {
                            Debug.LogError($"Invalid packet size received: {packetSize}. Disconnecting.");
                            _isDisconnectRequested = true;
                            return; // ReceiveLoop 종료
                        }
                        if (ms.Length < header.size)
                        {
                            ms.Position = ms.Length;
                            break;
                        } // 패킷이 아직 다 안 왔으면 break

                        // 패킷 하나가 완전히 도착함
                        ms.Position = 0;
                        byte[] packetBytes = new byte[packetSize];
                        ms.Read(packetBytes, 0, packetBytes.Length);

                        // 큐에 넣어서 메인 스레드가 처리하도록 함
                        _packetQueue.Enqueue(new QueuedPacket { type = packetType, data = packetBytes });
                        Debug.Log($"[ReceiveLoop] Enqueued Packet! Type: {packetType}, Queue Count: {_packetQueue.Count}");

                        // 처리한 패킷만큼 메모리 스트림에서 제거
                        long remainingLength = ms.Length - packetSize;
                        if (remainingLength > 0)
                        {
                            byte[] remainingBytes = new byte[remainingLength];
                            ms.Read(remainingBytes, 0, remainingBytes.Length);
                            ms.SetLength(0);
                            ms.Write(remainingBytes, 0, remainingBytes.Length);
                        }
                        else
                        {
                            ms.SetLength(0); // 남은 데이터가 없으면 완전히 비움
                        }

                    }
                }
            }
        }
        catch (Exception e)
        {
            if (e is ObjectDisposedException)
            {
                Debug.Log("Stream was closed, likely due to disconnection.");
            }
            else
            {
                Debug.LogError($"Receive error: {e.Message}\n{e.StackTrace}");
            }
        }
        finally
        {
            if (_client != null && _client.Connected)
            {
                _isDisconnectRequested = true;
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
            ProcessPacket(packet.type, packet.data);
        }
    }

    // 패킷 종류에 따라 분기 처리 (메인 스레드에서 실행됨)
    private void ProcessPacket(PacketType type, byte[] data)
    {
        switch (type)
        {
            case PacketType.S2C_LOGIN_RES:
                PktS2CLoginRes loginRes = BytesToStruct<PktS2CLoginRes>(data);
                if (loginRes.success)
                    OnStatusTextChanged?.Invoke("로그인 성공!");
                else
                    OnStatusTextChanged?.Invoke("로그인 실패!");
                break;

            case PacketType.S2C_ENTER_ROOM_RES:
                PktS2CEnterRoomRes enterRes = BytesToStruct<PktS2CEnterRoomRes>(data);
                if (enterRes.success)
                {
                    string msg = $"방 입장 성공! 현재 인원: {enterRes.playerCount}\n";
                    for (int i = 0; i < enterRes.players.Length; ++i)
                    {
                        if (enterRes.players[i].slotIndex >= 0 && !string.IsNullOrEmpty(enterRes.players[i].nickname))
                        {
                            msg += $"[{enterRes.players[i].slotIndex}] {enterRes.players[i].nickname}\n";
                        }
                    }
                    OnStatusTextChanged?.Invoke(msg);
                }
                else
                {
                    OnStatusTextChanged?.Invoke("방 입장 실패 (꽉 찼거나 오류 발생)");
                }
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


    // [핵심] 구조체를 byte[]로 변환하는 범용 함수
    public static byte[] StructToBytes<T>(T obj) where T : struct
    {
        int size = Marshal.SizeOf(obj);
        byte[] arr = new byte[size];
        IntPtr ptr = Marshal.AllocHGlobal(size);
        Marshal.StructureToPtr(obj, ptr, true);
        Marshal.Copy(ptr, arr, 0, size);
        Marshal.FreeHGlobal(ptr);
        return arr;
    }

    // [핵심] byte[]를 구조체로 변환하는 범용 함수
    public static T BytesToStruct<T>(byte[] buffer) where T : struct
    {
        int size = Marshal.SizeOf(typeof(T));
        if (size > buffer.Length)
            throw new Exception("Buffer smaller than struct size");
        IntPtr ptr = Marshal.AllocHGlobal(size);
        Marshal.Copy(buffer, 0, ptr, size);
        T obj = (T)Marshal.PtrToStructure(ptr, typeof(T));
        Marshal.FreeHGlobal(ptr);
        return obj;
    }

    public void Send<T>(T packet) where T : struct
    {
        if (_client == null || !_client.Connected) return;
        byte[] bytes = StructToBytes(packet);
        _stream.WriteAsync(bytes, 0, bytes.Length);
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
