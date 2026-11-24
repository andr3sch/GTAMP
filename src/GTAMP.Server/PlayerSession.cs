using GTAMP.Shared;
using GTAMP.Shared.Packets;
using LiteNetLib;

namespace GTAMP.Server;

/// <summary>Estado de um jogador conectado no servidor.</summary>
public sealed class PlayerSession
{
    public PlayerSession(NetPeer peer, string nickname)
    {
        Peer = peer;
        Nickname = nickname;
        AcceptedAt = Environment.TickCount64;
    }

    public NetPeer Peer { get; }
    public int Id => Peer.Id;
    public string Nickname { get; }
    public long AcceptedAt { get; }

    /// <summary>True apos PeerConnectedEvent (handshake UDP concluido).</summary>
    public bool IsJoined { get; set; }

    /// <summary>Ultimo estado valido recebido do cliente.</summary>
    public PlayerStatePacket State { get; } = new();
    public bool HasState { get; set; }

    /// <summary>Ha um estado novo ainda nao retransmitido no proximo tick.</summary>
    public bool StateDirty { get; set; }
    public long LastStateAt { get; set; }

    public long LastChatAt { get; set; }
    public int InvalidPackets { get; set; }

    // ---- Voz e Radio ----
    public VoiceRangeMode VoiceMode { get; set; } = VoiceRangeMode.Normal;
    public int RadioChannel { get; set; }

    // ---- Anti-Cheat ----
    public int AntiCheatStrikes { get; set; }
    public long LastServerTeleportAt { get; set; }
    public NetVector3 LastValidPos { get; set; }
    public long LastValidPosAt { get; set; }
    public long DamageWindowStart { get; set; }
    public int DamageWindowCount { get; set; }

    // ---- Anti-flood (janela de 1 segundo) ----
    private long _rateWindowStart;
    private int _rateWindowCount;

    /// <summary>Registra um pacote de estado; retorna false se o limite por segundo foi excedido.</summary>
    public bool TryConsumeStateBudget(long now, int maxPerSecond)
    {
        if (now - _rateWindowStart >= 1000)
        {
            _rateWindowStart = now;
            _rateWindowCount = 0;
        }
        return ++_rateWindowCount <= maxPerSecond;
    }

    public override string ToString() => $"#{Id} {Nickname} ({Peer.EndPoint})";
}
