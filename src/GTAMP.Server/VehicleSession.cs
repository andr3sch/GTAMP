using GTAMP.Shared.Packets;

namespace GTAMP.Server;

/// <summary>Estado de um veiculo sincronizado no servidor.</summary>
public sealed class VehicleSession
{
    public VehicleSession(int netId, int ownerPlayerId)
    {
        NetId = netId;
        OwnerPlayerId = ownerPlayerId;
        State.VehicleNetId = netId;
        State.OwnerPlayerId = ownerPlayerId;
    }

    public int NetId { get; }
    public int OwnerPlayerId { get; set; }
    public VehicleStatePacket State { get; } = new();
    public bool Dirty { get; set; }
    public long LastUpdateAt { get; set; }
}
