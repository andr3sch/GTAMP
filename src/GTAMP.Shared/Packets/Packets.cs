using System;
using LiteNetLib.Utils;

namespace GTAMP.Shared.Packets
{
    /// <summary>
    /// Primeiro byte de todo pacote conectado/desconectado. NUNCA reordene valores existentes:
    /// apenas acrescente novos (e incremente Protocol.Version).
    /// </summary>
    public enum PacketType : byte
    {
        // ---- Controle (canal reliable) ----
        ServerWelcome = 1,   // S -> C : dados iniciais apos conectar
        PlayerJoined = 2,    // S -> C : jogador entrou
        PlayerLeft = 3,      // S -> C : jogador saiu

        // ---- Sincronizacao (canal state / vehicle) ----
        PlayerState = 10,    // C -> S -> C : estado do ped (a pe ou em veiculo)
        VehicleState = 11,   // C -> S -> C : estado de veiculo (canal vehicle)
        VehicleRemoved = 12, // C -> S -> C : veiculo removido/abandonado (canal reliable)

        // ---- Combate (canal reliable) ----
        WeaponShot = 13,     // C -> S -> C : disparo de arma
        PlayerDamage = 14,   // C -> S -> C : dano aplicado em outro jogador

        // ---- Interacao ----
        ChatMessage = 20,    // C -> S -> C

        // ---- Roleplay e Mundo ----
        WorldSync = 21,      // S -> C : hora e clima sincronizados
        ServerAction = 22,   // S -> C : teleporte, skin, arma, veiculo, cura, notificacao
        RpHud = 23,          // S -> C : dinheiro, banco, emprego
        RpMarker = 24,       // S -> C : marker 3D + blip de emprego/loja/garagem
        RpInteract = 25,     // C -> S : jogador pressionou E no marker
        VoiceData = 26,      // C -> S -> C : voz por proximidade/radio (canal Voice)
        VoiceMode = 27,      // C -> S : altera alcance de voz / frequencia de radio
        PlayerMenu = 28,     // S -> C : menu interativo estilo NativeUI controlado pelo servidor
        MenuSelect = 29,     // C -> S : opcao selecionada no menu

        // ---- Mensagens nao conectadas (Launcher / server browser) ----
        ServerInfoRequest = 30,  // Launcher -> S
        ServerInfoResponse = 31, // S -> Launcher

        // ---- Animacoes / Emotes ----
        PedAnimation = 32    // C -> S -> C : animacao/emote sincronizado
    }

    /// <summary>Contrato basico de pacote.</summary>
    public interface INetPacket
    {
        PacketType Type { get; }
        void Serialize(NetDataWriter w);
        void Deserialize(NetDataReader r);
    }

    public static class PacketWriter
    {
        /// <summary>Limpa o writer e grava [tipo][conteudo].</summary>
        public static NetDataWriter Write(NetDataWriter w, INetPacket packet)
        {
            w.Reset();
            w.Put((byte)packet.Type);
            packet.Serialize(w);
            return w;
        }
    }

    /// <summary>
    /// Bitmask de estado do ped. Usado pelo cliente remoto para escolher animacoes/tarefas.
    /// </summary>
    [Flags]
    public enum PedStateFlags : ushort
    {
        None = 0,
        Walking = 1 << 0,
        Running = 1 << 1,
        Sprinting = 1 << 2,
        Jumping = 1 << 3,
        Aiming = 1 << 4,
        Shooting = 1 << 5,
        Reloading = 1 << 6,
        InCover = 1 << 7,
        Ragdoll = 1 << 8,
        Dead = 1 << 9,
        Climbing = 1 << 10,
        Swimming = 1 << 11,
        InVehicle = 1 << 12,
        Ducking = 1 << 13
    }

    /// <summary>Bitmask de estado de veiculo.</summary>
    [Flags]
    public enum VehicleStateFlags : ushort
    {
        None = 0,
        EngineOn = 1 << 0,
        LightsOn = 1 << 1,
        HighBeams = 1 << 2,
        SirenOn = 1 << 3,
        BrakeLights = 1 << 4,
        Horn = 1 << 5,
        Handbrake = 1 << 6,
        RoofOpen = 1 << 7
    }

    // =====================================================================
    // Dados enviados no handshake (NetManager.Connect(host, port, writer))
    // Nao possui PacketType: e lido direto de ConnectionRequest.Data.
    // =====================================================================
    public sealed class ConnectRequestData
    {
        public string ConnectionKey = Protocol.ConnectionKey;
        public int ProtocolVersion = Protocol.Version;
        public string Nickname = string.Empty;
        public string Password = string.Empty;

        public void Serialize(NetDataWriter w)
        {
            w.Put(ConnectionKey ?? string.Empty, Protocol.MaxShortString);
            w.Put(ProtocolVersion);
            w.Put(Nickname ?? string.Empty, Protocol.MaxNicknameLength);
            w.Put(Password ?? string.Empty, Protocol.MaxShortString);
        }

        public static ConnectRequestData Deserialize(NetDataReader r)
        {
            var d = new ConnectRequestData();
            d.ConnectionKey = r.GetString(Protocol.MaxShortString);
            r.Require(4);
            d.ProtocolVersion = r.GetInt();
            d.Nickname = r.GetString(Protocol.MaxNicknameLength);
            d.Password = r.GetString(Protocol.MaxShortString);
            return d;
        }
    }

    // =====================================================================
    // S -> C : enviado imediatamente apos a conexao ser aceita
    // =====================================================================
    public sealed class ServerWelcomePacket : INetPacket
    {
        public PacketType Type { get { return PacketType.ServerWelcome; } }

        public int LocalPlayerId;
        public string ServerName = string.Empty;
        public string Motd = string.Empty;
        public byte TickRate;
        public int MaxPlayers;
        public float VoiceRange = Protocol.DefaultVoiceRange;

        public void Serialize(NetDataWriter w)
        {
            w.Put(LocalPlayerId);
            w.Put(ServerName ?? string.Empty, Protocol.MaxShortString);
            w.Put(Motd ?? string.Empty, Protocol.MaxLongString);
            w.Put(TickRate);
            w.Put(MaxPlayers);
            w.Put(VoiceRange);
        }

        public void Deserialize(NetDataReader r)
        {
            r.Require(4);
            LocalPlayerId = r.GetInt();
            ServerName = r.GetString(Protocol.MaxShortString);
            Motd = r.GetString(Protocol.MaxLongString);
            r.Require(9);
            TickRate = r.GetByte();
            MaxPlayers = r.GetInt();
            VoiceRange = r.GetFloat();
        }
    }

    // =====================================================================
    // S -> C : um jogador entrou (tambem enviado para cada jogador ja online
    //          quando alguem acaba de conectar)
    // =====================================================================
    public sealed class PlayerJoinedPacket : INetPacket
    {
        public PacketType Type { get { return PacketType.PlayerJoined; } }

        public int PlayerId;
        public string Nickname = string.Empty;

        public void Serialize(NetDataWriter w)
        {
            w.Put(PlayerId);
            w.Put(Nickname ?? string.Empty, Protocol.MaxNicknameLength);
        }

        public void Deserialize(NetDataReader r)
        {
            r.Require(4);
            PlayerId = r.GetInt();
            Nickname = r.GetString(Protocol.MaxNicknameLength);
        }
    }

    // =====================================================================
    // S -> C : um jogador saiu
    // =====================================================================
    public sealed class PlayerLeftPacket : INetPacket
    {
        public PacketType Type { get { return PacketType.PlayerLeft; } }

        public int PlayerId;
        public string Reason = string.Empty;

        public void Serialize(NetDataWriter w)
        {
            w.Put(PlayerId);
            w.Put(Reason ?? string.Empty, Protocol.MaxShortString);
        }

        public void Deserialize(NetDataReader r)
        {
            r.Require(4);
            PlayerId = r.GetInt();
            Reason = r.GetString(Protocol.MaxShortString);
        }
    }

    // =====================================================================
    // C -> S -> C : estado do ped (alta frequencia, canal Sequenced)
    // Tamanho fixo: 63 bytes + 1 byte de tipo.
    // O servidor SOBRESCREVE PlayerId com o id real do remetente (anti-spoof).
    // =====================================================================
    public sealed class PlayerStatePacket : INetPacket
    {
        public const int PayloadSize = 4 + 12 + 12 + 4 + 4 + 2 + 2 + 2 + 4 + 12 + 4 + 1;

        public PacketType Type { get { return PacketType.PlayerState; } }

        public int PlayerId;
        public NetVector3 Position;
        public NetVector3 Velocity;     // usado para extrapolacao/interpolacao suave
        public float Heading;           // 0..360 (Entity.Heading do SHVDN 2)
        public uint ModelHash;          // hash do modelo do ped (Model.Hash)
        public short Health;
        public short Armor;
        public PedStateFlags Flags;
        public uint WeaponHash;         // arma equipada
        public NetVector3 AimPosition;  // ponto mirado no mundo
        public int VehicleNetId;        // 0 = a pe; > 0 = id de rede do veiculo
        public sbyte SeatIndex;         // Protocol.SeatNone (-2) = a pe, -1 = motorista, 0+ = passageiro

        public void Serialize(NetDataWriter w)
        {
            w.Put(PlayerId);
            w.PutVector3(Position);
            w.PutVector3(Velocity);
            w.Put(Heading);
            w.Put(ModelHash);
            w.Put(Health);
            w.Put(Armor);
            w.Put((ushort)Flags);
            w.Put(WeaponHash);
            w.PutVector3(AimPosition);
            w.Put(VehicleNetId);
            w.Put(SeatIndex);
        }

        public void Deserialize(NetDataReader r)
        {
            r.Require(PayloadSize);
            PlayerId = r.GetInt();
            Position = r.GetVector3();
            Velocity = r.GetVector3();
            Heading = r.GetFloat();
            ModelHash = r.GetUInt();
            Health = r.GetShort();
            Armor = r.GetShort();
            Flags = (PedStateFlags)r.GetUShort();
            WeaponHash = r.GetUInt();
            AimPosition = r.GetVector3();
            VehicleNetId = r.GetInt();
            SeatIndex = r.GetSByte();
        }

        public void CopyFrom(PlayerStatePacket o)
        {
            PlayerId = o.PlayerId;
            Position = o.Position;
            Velocity = o.Velocity;
            Heading = o.Heading;
            ModelHash = o.ModelHash;
            Health = o.Health;
            Armor = o.Armor;
            Flags = o.Flags;
            WeaponHash = o.WeaponHash;
            AimPosition = o.AimPosition;
            VehicleNetId = o.VehicleNetId;
            SeatIndex = o.SeatIndex;
        }
    }

    // =====================================================================
    // C -> S -> C : estado de veiculo (canal Vehicle, Sequenced)
    // =====================================================================
    public sealed class VehicleStatePacket : INetPacket
    {
        public const int PayloadSize = 4 + 4 + 4 + 12 + 16 + 12 + 4 + 4 + 1 + 1 + 2;

        public PacketType Type { get { return PacketType.VehicleState; } }

        public int VehicleNetId;
        public int OwnerPlayerId;       // sobrescrito pelo servidor
        public uint ModelHash;
        public NetVector3 Position;
        public NetQuaternion Rotation;
        public NetVector3 Velocity;
        public float SteeringAngle;
        public float EngineHealth;
        public byte PrimaryColor;
        public byte SecondaryColor;
        public VehicleStateFlags Flags;

        public void Serialize(NetDataWriter w)
        {
            w.Put(VehicleNetId);
            w.Put(OwnerPlayerId);
            w.Put(ModelHash);
            w.PutVector3(Position);
            w.PutQuaternion(Rotation);
            w.PutVector3(Velocity);
            w.Put(SteeringAngle);
            w.Put(EngineHealth);
            w.Put(PrimaryColor);
            w.Put(SecondaryColor);
            w.Put((ushort)Flags);
        }

        public void Deserialize(NetDataReader r)
        {
            r.Require(PayloadSize);
            VehicleNetId = r.GetInt();
            OwnerPlayerId = r.GetInt();
            ModelHash = r.GetUInt();
            Position = r.GetVector3();
            Rotation = r.GetQuaternion();
            Velocity = r.GetVector3();
            SteeringAngle = r.GetFloat();
            EngineHealth = r.GetFloat();
            PrimaryColor = r.GetByte();
            SecondaryColor = r.GetByte();
            Flags = (VehicleStateFlags)r.GetUShort();
        }

        public void CopyFrom(VehicleStatePacket o)
        {
            VehicleNetId = o.VehicleNetId;
            OwnerPlayerId = o.OwnerPlayerId;
            ModelHash = o.ModelHash;
            Position = o.Position;
            Rotation = o.Rotation;
            Velocity = o.Velocity;
            SteeringAngle = o.SteeringAngle;
            EngineHealth = o.EngineHealth;
            PrimaryColor = o.PrimaryColor;
            SecondaryColor = o.SecondaryColor;
            Flags = o.Flags;
        }
    }

    // =====================================================================
    // C -> S -> C : veiculo removido/destruido/abandonado (canal Reliable)
    // =====================================================================
    public sealed class VehicleRemovedPacket : INetPacket
    {
        public PacketType Type { get { return PacketType.VehicleRemoved; } }

        public int VehicleNetId;

        public void Serialize(NetDataWriter w)
        {
            w.Put(VehicleNetId);
        }

        public void Deserialize(NetDataReader r)
        {
            r.Require(4);
            VehicleNetId = r.GetInt();
        }
    }

    // =====================================================================
    // C -> S -> C : disparo de arma (efeito visual + projetil nos remotos)
    // =====================================================================
    public sealed class WeaponShotPacket : INetPacket
    {
        public const int PayloadSize = 4 + 4 + 12 + 12;

        public PacketType Type { get { return PacketType.WeaponShot; } }

        public int ShooterPlayerId;     // sobrescrito pelo servidor
        public uint WeaponHash;
        public NetVector3 Origin;
        public NetVector3 Target;

        public void Serialize(NetDataWriter w)
        {
            w.Put(ShooterPlayerId);
            w.Put(WeaponHash);
            w.PutVector3(Origin);
            w.PutVector3(Target);
        }

        public void Deserialize(NetDataReader r)
        {
            r.Require(PayloadSize);
            ShooterPlayerId = r.GetInt();
            WeaponHash = r.GetUInt();
            Origin = r.GetVector3();
            Target = r.GetVector3();
        }
    }

    // =====================================================================
    // C -> S -> C : dano aplicado em outro jogador
    // =====================================================================
    public sealed class PlayerDamagePacket : INetPacket
    {
        public const int PayloadSize = 4 + 4 + 4 + 2 + 1;

        public PacketType Type { get { return PacketType.PlayerDamage; } }

        public int AttackerPlayerId;    // sobrescrito pelo servidor
        public int TargetPlayerId;
        public uint WeaponHash;
        public short Damage;
        public bool IsHeadshot;

        public void Serialize(NetDataWriter w)
        {
            w.Put(AttackerPlayerId);
            w.Put(TargetPlayerId);
            w.Put(WeaponHash);
            w.Put(Damage);
            w.Put(IsHeadshot);
        }

        public void Deserialize(NetDataReader r)
        {
            r.Require(PayloadSize);
            AttackerPlayerId = r.GetInt();
            TargetPlayerId = r.GetInt();
            WeaponHash = r.GetUInt();
            Damage = r.GetShort();
            IsHeadshot = r.GetBool();
        }
    }

    // =====================================================================
    // Chat. C -> S: apenas Text e usado. S -> C: SenderId/SenderName preenchidos.
    // SenderId == Protocol.ServerPlayerId => mensagem do sistema.
    // =====================================================================
    public sealed class ChatMessagePacket : INetPacket
    {
        public PacketType Type { get { return PacketType.ChatMessage; } }

        public int SenderId;
        public string SenderName = string.Empty;
        public string Text = string.Empty;

        public void Serialize(NetDataWriter w)
        {
            w.Put(SenderId);
            w.Put(SenderName ?? string.Empty, Protocol.MaxNicknameLength);
            w.Put(Text ?? string.Empty, Protocol.MaxChatLength);
        }

        public void Deserialize(NetDataReader r)
        {
            r.Require(4);
            SenderId = r.GetInt();
            SenderName = r.GetString(Protocol.MaxNicknameLength);
            Text = r.GetString(Protocol.MaxChatLength);
        }
    }

    // =====================================================================
    // Mensagens NAO conectadas (UDP "solto") - usadas pelo Launcher para
    // consultar nome/jogadores/ping sem precisar entrar no servidor.
    // =====================================================================
    public sealed class ServerInfoRequestPacket : INetPacket
    {
        public PacketType Type { get { return PacketType.ServerInfoRequest; } }

        public string ConnectionKey = Protocol.ConnectionKey;

        public void Serialize(NetDataWriter w)
        {
            w.Put(ConnectionKey ?? string.Empty, Protocol.MaxShortString);
        }

        public void Deserialize(NetDataReader r)
        {
            ConnectionKey = r.GetString(Protocol.MaxShortString);
        }
    }

    public sealed class ServerInfoResponsePacket : INetPacket
    {
        public PacketType Type { get { return PacketType.ServerInfoResponse; } }

        public string ServerName = string.Empty;
        public string Motd = string.Empty;
        public int Players;
        public int MaxPlayers;
        public int ProtocolVersion;
        public bool HasPassword;
        public string GameMode = "Roleplay";
        public string ModpackUrl = string.Empty;

        public void Serialize(NetDataWriter w)
        {
            w.Put(ServerName ?? string.Empty, Protocol.MaxShortString);
            w.Put(Motd ?? string.Empty, Protocol.MaxLongString);
            w.Put(Players);
            w.Put(MaxPlayers);
            w.Put(ProtocolVersion);
            w.Put(HasPassword);
            w.Put(GameMode ?? string.Empty, Protocol.MaxShortString);
            w.Put(ModpackUrl ?? string.Empty, Protocol.MaxLongString);
        }

        public void Deserialize(NetDataReader r)
        {
            ServerName = r.GetString(Protocol.MaxShortString);
            Motd = r.GetString(Protocol.MaxLongString);
            r.Require(13);
            Players = r.GetInt();
            MaxPlayers = r.GetInt();
            ProtocolVersion = r.GetInt();
            HasPassword = r.GetBool();
            if (r.AvailableBytes > 0)
                GameMode = r.GetString(Protocol.MaxShortString);
            if (r.AvailableBytes > 0)
                ModpackUrl = r.GetString(Protocol.MaxLongString);
        }
    }
}
