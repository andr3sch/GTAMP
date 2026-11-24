namespace GTAMP.Shared
{
    /// <summary>
    /// Constantes globais do protocolo. Cliente e Servidor DEVEM usar o mesmo valor de
    /// <see cref="Version"/> e <see cref="ConnectionKey"/>, senao a conexao e recusada.
    /// </summary>
    public static class Protocol
    {
        /// <summary>Incrementar sempre que o formato de qualquer pacote mudar.</summary>
        public const int Version = 3;

        /// <summary>Chave de aplicacao (filtra conexoes de outros jogos/programas).</summary>
        public const string ConnectionKey = "GTAMP-1.0.463.1";

        public const int DefaultPort = 4499;
        public const int DefaultHttpPort = 4500;

        // ---- Canais LiteNetLib (NetManager.ChannelsCount deve ser igual nos dois lados) ----
        public const byte ChannelCount = 4;
        /// <summary>Canal 0: controle, entrada/saida de jogadores, chat, dano, RP (ReliableOrdered).</summary>
        public const byte ChannelReliable = 0;
        /// <summary>Canal 1: estados de peds (Sequenced - descarta pacotes antigos).</summary>
        public const byte ChannelState = 1;
        /// <summary>Canal 2: estados de veiculos (Sequenced, separado para nao competir com peds).</summary>
        public const byte ChannelVehicle = 2;
        /// <summary>Canal 3: voz por proximidade/radio (Unreliable, baixa latencia).</summary>
        public const byte ChannelVoice = 3;

        /// <summary>Limites de voz (16 kHz wideband, 16-bit mono comprimido em G.711 u-law = 16 KB/s).</summary>
        public const int VoiceSampleRate = 16000;
        public const int MaxVoicePayloadBytes = 2048;
        public const float DefaultVoiceRange = 25f;
        public const float WhisperVoiceRange = 6f;
        public const float ShoutVoiceRange = 45f;
        public const float DefaultChatProximityRange = 30f;
        public const int MaxMenuItems = 24;

        /// <summary>Valor de assento quando o jogador esta a pe. -1 = motorista, 0..N = passageiros.</summary>
        public const sbyte SeatNone = -2;
        public const sbyte SeatDriver = -1;

        /// <summary>Limite de veiculos sincronizados simultaneamente por jogador.</summary>
        public const int MaxVehiclesPerPlayer = 8;
        public const int MaxDamagePerHit = 400;

        // ---- Limites ----
        public const int MinNicknameLength = 3;
        public const int MaxNicknameLength = 24;
        public const int MaxChatLength = 128;
        public const int MaxShortString = 64;
        public const int MaxLongString = 256;

        /// <summary>Id usado como remetente em mensagens do proprio servidor.</summary>
        public const int ServerPlayerId = -1;

        // ---- Limites do mapa (anti-lixo/anti-cheat basico) ----
        public const float WorldLimitXY = 10000f;
        public const float WorldMinZ = -1000f;
        public const float WorldMaxZ = 3000f;
    }
}
