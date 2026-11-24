using System;
using System.Collections.Generic;
using LiteNetLib.Utils;

namespace GTAMP.Shared.Packets
{
    /// <summary>Modo de alcance da voz por proximidade ou transmissao via radio.</summary>
    public enum VoiceRangeMode : byte
    {
        Whisper = 0, // ~6m
        Normal = 1,  // ServerConfig.VoiceRange (padrao 25m)
        Shout = 2,   // ~45m
        Radio = 3    // Frequencia de radio (alcance global no canal)
    }
    /// <summary>
    /// Tipos de acao que o servidor pode ordenar ao cliente (base para servidores de Roleplay).
    /// </summary>
    public enum ServerActionType : byte
    {
        Teleport = 1,       // Position + FloatParam (heading)
        SetModel = 2,       // UintParam (ped model hash)
        GiveWeapon = 3,     // UintParam (weapon hash) + IntParam (ammo)
        SpawnVehicle = 4,   // UintParam (vehicle hash) + Position + FloatParam (heading)
        Notify = 5,         // TextParam (notificacao GTA)
        Heal = 6            // IntParam (health, default 200)
    }

    // =====================================================================
    // S -> C : sincronizacao global de hora e clima
    // =====================================================================
    public sealed class WorldSyncPacket : INetPacket
    {
        public PacketType Type { get { return PacketType.WorldSync; } }

        public byte Hour;
        public byte Minute;
        public string Weather = "CLEAR";

        public void Serialize(NetDataWriter w)
        {
            w.Put(Hour);
            w.Put(Minute);
            w.Put(Weather ?? "CLEAR", 32);
        }

        public void Deserialize(NetDataReader r)
        {
            r.Require(2);
            Hour = r.GetByte();
            Minute = r.GetByte();
            Weather = r.GetString(32);
        }
    }

    // =====================================================================
    // S -> C : acao direta do servidor sobre o jogador local (TP, skin, arma, carro, cura)
    // =====================================================================
    public sealed class ServerActionPacket : INetPacket
    {
        public PacketType Type { get { return PacketType.ServerAction; } }

        public ServerActionType Action;
        public NetVector3 Position;
        public float FloatParam;
        public uint UintParam;
        public int IntParam;
        public string TextParam = string.Empty;

        public void Serialize(NetDataWriter w)
        {
            w.Put((byte)Action);
            w.PutVector3(Position);
            w.Put(FloatParam);
            w.Put(UintParam);
            w.Put(IntParam);
            w.Put(TextParam ?? string.Empty, Protocol.MaxLongString);
        }

        public void Deserialize(NetDataReader r)
        {
            r.Require(1 + 12 + 4 + 4 + 4);
            Action = (ServerActionType)r.GetByte();
            Position = r.GetVector3();
            FloatParam = r.GetFloat();
            UintParam = r.GetUInt();
            IntParam = r.GetInt();
            TextParam = r.GetString(Protocol.MaxLongString);
        }
    }

    // =====================================================================
    // S -> C : atualiza o HUD de RP do jogador (dinheiro, banco, emprego)
    // =====================================================================
    public sealed class RpHudPacket : INetPacket
    {
        public PacketType Type { get { return PacketType.RpHud; } }

        public long Cash;
        public long Bank;
        public string JobName = "Desempregado";

        public void Serialize(NetDataWriter w)
        {
            w.Put(Cash);
            w.Put(Bank);
            w.Put(JobName ?? string.Empty, Protocol.MaxShortString);
        }

        public void Deserialize(NetDataReader r)
        {
            r.Require(16);
            Cash = r.GetLong();
            Bank = r.GetLong();
            JobName = r.GetString(Protocol.MaxShortString);
        }
    }

    // =====================================================================
    // S -> C : cria ou atualiza um ponto de interacao RP (marker 3D + blip no mapa)
    // =====================================================================
    public sealed class RpMarkerPacket : INetPacket
    {
        public PacketType Type { get { return PacketType.RpMarker; } }

        public int MarkerId;
        public bool Remove;
        public NetVector3 Position;
        public float Radius = 1.8f;
        public byte R = 0;
        public byte G = 180;
        public byte B = 255;
        public int BlipSprite = 1;
        public int BlipColor = 3;
        public string Label = string.Empty;

        public void Serialize(NetDataWriter w)
        {
            w.Put(MarkerId);
            w.Put(Remove);
            w.PutVector3(Position);
            w.Put(Radius);
            w.Put(R);
            w.Put(G);
            w.Put(B);
            w.Put(BlipSprite);
            w.Put(BlipColor);
            w.Put(Label ?? string.Empty, Protocol.MaxShortString);
        }

        public void Deserialize(NetDataReader r)
        {
            r.Require(4 + 1 + 12 + 4 + 3 + 4 + 4);
            MarkerId = r.GetInt();
            Remove = r.GetBool();
            Position = r.GetVector3();
            Radius = r.GetFloat();
            R = r.GetByte();
            G = r.GetByte();
            B = r.GetByte();
            BlipSprite = r.GetInt();
            BlipColor = r.GetInt();
            Label = r.GetString(Protocol.MaxShortString);
        }
    }

    // =====================================================================
    // C -> S : jogador pressionou E dentro de um marker RP
    // =====================================================================
    public sealed class RpInteractPacket : INetPacket
    {
        public PacketType Type { get { return PacketType.RpInteract; } }

        public int MarkerId;

        public void Serialize(NetDataWriter w)
        {
            w.Put(MarkerId);
        }

        public void Deserialize(NetDataReader r)
        {
            r.Require(4);
            MarkerId = r.GetInt();
        }
    }

    // =====================================================================
    // C -> S -> C : frame de voz por proximidade/radio (canal Voice, Unreliable)
    // Audio comprimido em G.711 u-law (1 byte por amostra a 16 kHz).
    // =====================================================================
    public sealed class VoiceDataPacket : INetPacket
    {
        public PacketType Type { get { return PacketType.VoiceData; } }

        public int SpeakerPlayerId;     // sobrescrito pelo servidor
        public float Distance;          // preenchido pelo servidor para atenuacao de volume
        public VoiceRangeMode Mode = VoiceRangeMode.Normal;
        public int RadioChannel;        // 0 = proximidade, >0 = frequencia de radio
        public NetVector3 SpeakerPos;   // posicao 3D do falante (para stereo panning)
        public byte[] AudioData = new byte[0];

        public void Serialize(NetDataWriter w)
        {
            w.Put(SpeakerPlayerId);
            w.Put(Distance);
            w.Put((byte)Mode);
            w.Put(RadioChannel);
            w.PutVector3(SpeakerPos);
            int len = AudioData != null ? Math.Min(AudioData.Length, Protocol.MaxVoicePayloadBytes) : 0;
            w.Put((ushort)len);
            if (len > 0)
                w.Put(AudioData, 0, len);
        }

        public void Deserialize(NetDataReader r)
        {
            r.Require(4 + 4 + 1 + 4 + 12 + 2);
            SpeakerPlayerId = r.GetInt();
            Distance = r.GetFloat();
            Mode = (VoiceRangeMode)r.GetByte();
            RadioChannel = r.GetInt();
            SpeakerPos = r.GetVector3();
            int len = r.GetUShort();
            if (len > Protocol.MaxVoicePayloadBytes)
                throw new FormatException("Pacote de voz excede tamanho maximo.");
            r.Require(len);
            AudioData = new byte[len];
            if (len > 0)
                r.GetBytes(AudioData, len);
        }
    }

    // =====================================================================
    // C -> S / S -> C : altera modo de voz (Sussurrar/Normal/Gritar/Radio) e frequencia
    // =====================================================================
    public sealed class VoiceModePacket : INetPacket
    {
        public PacketType Type { get { return PacketType.VoiceMode; } }

        public VoiceRangeMode Mode = VoiceRangeMode.Normal;
        public int RadioChannel; // 0 = desligado

        public void Serialize(NetDataWriter w)
        {
            w.Put((byte)Mode);
            w.Put(RadioChannel);
        }

        public void Deserialize(NetDataReader r)
        {
            r.Require(5);
            Mode = (VoiceRangeMode)r.GetByte();
            RadioChannel = r.GetInt();
        }
    }

    // =====================================================================
    // S -> C : menu interativo estilo NativeUI controlado pelo servidor/resources
    // =====================================================================
    public sealed class MenuItemData
    {
        public string Id = string.Empty;
        public string Label = string.Empty;
        public string RightLabel = string.Empty;
    }

    public sealed class PlayerMenuPacket : INetPacket
    {
        public PacketType Type { get { return PacketType.PlayerMenu; } }

        public string MenuId = string.Empty;
        public string Title = string.Empty;
        public string Subtitle = string.Empty;
        public bool Close;
        public List<MenuItemData> Items = new List<MenuItemData>();

        public void Serialize(NetDataWriter w)
        {
            w.Put(MenuId ?? string.Empty, Protocol.MaxShortString);
            w.Put(Title ?? string.Empty, Protocol.MaxShortString);
            w.Put(Subtitle ?? string.Empty, Protocol.MaxShortString);
            w.Put(Close);

            int count = Items != null ? Math.Min(Items.Count, Protocol.MaxMenuItems) : 0;
            w.Put((byte)count);
            for (int i = 0; i < count; i++)
            {
                MenuItemData item = Items[i];
                w.Put(item.Id ?? string.Empty, Protocol.MaxShortString);
                w.Put(item.Label ?? string.Empty, Protocol.MaxShortString);
                w.Put(item.RightLabel ?? string.Empty, 32);
            }
        }

        public void Deserialize(NetDataReader r)
        {
            MenuId = r.GetString(Protocol.MaxShortString);
            Title = r.GetString(Protocol.MaxShortString);
            Subtitle = r.GetString(Protocol.MaxShortString);
            r.Require(2);
            Close = r.GetBool();
            int count = r.GetByte();
            if (count > Protocol.MaxMenuItems)
                throw new FormatException("Menu excede limite de itens.");
            Items = new List<MenuItemData>(count);
            for (int i = 0; i < count; i++)
            {
                Items.Add(new MenuItemData
                {
                    Id = r.GetString(Protocol.MaxShortString),
                    Label = r.GetString(Protocol.MaxShortString),
                    RightLabel = r.GetString(32)
                });
            }
        }
    }

    // =====================================================================
    // C -> S : item escolhido pelo jogador no menu interativo
    // =====================================================================
    public sealed class MenuSelectPacket : INetPacket
    {
        public PacketType Type { get { return PacketType.MenuSelect; } }

        public string MenuId = string.Empty;
        public string ItemId = string.Empty;

        public void Serialize(NetDataWriter w)
        {
            w.Put(MenuId ?? string.Empty, Protocol.MaxShortString);
            w.Put(ItemId ?? string.Empty, Protocol.MaxShortString);
        }

        public void Deserialize(NetDataReader r)
        {
            MenuId = r.GetString(Protocol.MaxShortString);
            ItemId = r.GetString(Protocol.MaxShortString);
        }
    }

    // =====================================================================
    // C -> S -> C : animacao / emote sincronizado entre jogadores
    // =====================================================================
    public sealed class PedAnimationPacket : INetPacket
    {
        public PacketType Type { get { return PacketType.PedAnimation; } }

        public int PlayerId;
        public string Dict = string.Empty;
        public string Name = string.Empty;
        public int Flags = 1; // 1 = loop, 49 = upper body + loop + movable
        public bool Stop;

        public void Serialize(NetDataWriter w)
        {
            w.Put(PlayerId);
            w.Put(Dict ?? string.Empty, Protocol.MaxShortString);
            w.Put(Name ?? string.Empty, Protocol.MaxShortString);
            w.Put(Flags);
            w.Put(Stop);
        }

        public void Deserialize(NetDataReader r)
        {
            r.Require(4);
            PlayerId = r.GetInt();
            Dict = r.GetString(Protocol.MaxShortString);
            Name = r.GetString(Protocol.MaxShortString);
            r.Require(5);
            Flags = r.GetInt();
            Stop = r.GetBool();
        }
    }
}
