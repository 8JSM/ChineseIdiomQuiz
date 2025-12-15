using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Unicode)]
public struct PlayerInfo
{
    public int slotIndex;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
    public string nickname;
}

public enum PacketType : short
{

    C2S_LOGIN_REQ = 101,
    S2C_LOGIN_RES = 102,
    C2S_ENTER_ROOM_REQ = 201,
    S2C_ENTER_ROOM_RES = 202,
    S2C_USER_ENTER_NOTIFY = 203,
    S2C_USER_LEAVE_NOTIFY = 204,
    S2C_GAME_START_NOTIFY = 301,
    S2C_NEW_QUESTION_NOTIFY = 302,
    C2S_SUBMIT_ANSWER_REQ = 303,
    S2C_ROUND_RESULT_NOTIFY = 304,
    S2C_GAME_OVER_NOTIFY = 305,

}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct PacketHeader
{
    public short size;
    public PacketType type;
}

[StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Unicode)]
public struct PktC2SLoginReq
{
    public PacketHeader header;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
    public string nickname;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct PktS2CLoginRes
{
    public PacketHeader header;
    [MarshalAs(UnmanagedType.I1)] // C++의 bool은 C#에서 1바이트로 마샬링
    public bool success;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct PktC2SEnterRoomReq
{
    public PacketHeader header;
    public int roomIndex;
}

[StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Unicode)]
public struct PktS2CEnterRoomRes
{
    public PacketHeader header;
    [MarshalAs(UnmanagedType.I1)]
    public bool success;
    public int playerCount;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public PlayerInfo[] players;
}

[StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Unicode)]
public struct PktS2CUserEnterNotify
{
    public PacketHeader header;
    public int userSlotIndex;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
    public string nickname;
}
