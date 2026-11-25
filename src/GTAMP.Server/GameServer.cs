using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using GTAMP.Shared;
using GTAMP.Shared.Packets;
using LiteNetLib;
using LiteNetLib.Utils;

namespace GTAMP.Server;

/// <summary>
/// Nucleo do servidor dedicado.
///
/// Modelo de threads:
///  - Toda logica (eventos LiteNetLib, ticks, comandos) roda na THREAD PRINCIPAL dentro de Run().
///    LiteNetLib so dispara eventos dentro de PollEvents(), entao nao ha concorrencia nos dicionarios.
///  - A thread de console apenas enfileira comandos em _pendingCommands.
///
/// Fluxo de sincronizacao:
///  Cliente envia PlayerState (~20-30 Hz) -> servidor valida e guarda o ULTIMO estado ->
///  a cada tick (TickRate) o servidor retransmite os estados "sujos" para os demais jogadores.
///  Isso limita banda, absorve flood e mantem uma taxa de atualizacao previsivel.
/// </summary>
public sealed class GameServer : IServerApi
{
    private static readonly Regex NicknameRegex =
        new(@"^[\p{L}\p{N}_\-\.\[\] ]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly ServerConfig _config;
    private readonly RpDatabase _rpDb;
    private readonly AntiCheat _antiCheat;
    private readonly ResourceManager _resources;
    private readonly ResourceHttpServer _httpServer;
    private readonly EventBasedNetListener _listener = new();
    private readonly NetManager _net;

    /// <summary>Sessoes por PeerId (inclui aceitas que ainda nao concluiram o handshake).</summary>
    private readonly Dictionary<int, PlayerSession> _sessions = new();

    /// <summary>Veiculos sincronizados ativos por VehicleNetId.</summary>
    private readonly Dictionary<int, VehicleSession> _vehicles = new();

    private readonly ConcurrentQueue<string> _pendingCommands = new();

    // Objetos reutilizaveis (somente thread principal) para evitar alocacoes por pacote.
    private readonly NetDataWriter _writer = new();
    private readonly PlayerStatePacket _incomingState = new();
    private readonly VehicleStatePacket _incomingVehicle = new();
    private readonly VehicleRemovedPacket _incomingVehicleRemoved = new();
    private readonly WeaponShotPacket _incomingShot = new();
    private readonly PlayerDamagePacket _incomingDamage = new();
    private readonly ChatMessagePacket _incomingChat = new();
    private readonly RpInteractPacket _incomingInteract = new();
    private readonly VoiceDataPacket _incomingVoice = new();
    private readonly VoiceModePacket _incomingVoiceMode = new();
    private readonly MenuSelectPacket _incomingMenuSelect = new();
    private readonly PedAnimationPacket _incomingAnim = new();

    private byte _worldHour;
    private byte _worldMinute;
    private string _worldWeather;
    private long _nextWorldTickMs;
    private long _nextPaydayMs;

    private readonly Stopwatch _clock = new();
    private volatile bool _running;

    public GameServer(ServerConfig config)
    {
        _config = config;
        _rpDb = new RpDatabase(AppContext.BaseDirectory);
        _antiCheat = new AntiCheat(config, Kick);
        _resources = new ResourceManager(AppContext.BaseDirectory, this);
        _httpServer = new ResourceHttpServer(
            config,
            _resources.ModpackDir,
            BuildAdminStatusDto,
            BuildAdminPlayersDto,
            EnqueueCommand);
        _worldHour = (byte)(config.StartHour % 24);
        _worldMinute = (byte)(config.StartMinute % 60);
        _worldWeather = string.IsNullOrWhiteSpace(config.Weather) ? "EXTRASUNNY" : config.Weather.ToUpperInvariant();

        _net = new NetManager(_listener)
        {
            AutoRecycle = true,                     // readers reciclados automaticamente apos cada evento
            UpdateTime = 15,                        // ms entre iteracoes da thread interna da LiteNetLib
            DisconnectTimeout = config.TimeoutMs,
            UnconnectedMessagesEnabled = true,      // necessario para o Launcher consultar info do servidor
            ChannelsCount = Protocol.ChannelCount
        };

        _listener.ConnectionRequestEvent += OnConnectionRequest;
        _listener.PeerConnectedEvent += OnPeerConnected;
        _listener.PeerDisconnectedEvent += OnPeerDisconnected;
        _listener.NetworkReceiveEvent += OnNetworkReceive;
        _listener.NetworkReceiveUnconnectedEvent += OnNetworkReceiveUnconnected;
        _listener.NetworkErrorEvent += OnNetworkError;
    }

    private int JoinedCount => _sessions.Values.Count(s => s.IsJoined);

    // =====================================================================
    // Ciclo de vida
    // =====================================================================

    /// <summary>Bloqueia a thread atual executando o loop do servidor ate Stop() ser chamado.</summary>
    public void Run()
    {
        if (!_net.Start(_config.Port))
        {
            Logger.Error($"Nao foi possivel abrir a porta UDP {_config.Port}. Ela ja esta em uso?");
            return;
        }

        _running = true;
        _clock.Start();

        _resources.LoadAll();
        _httpServer.Start();

        Logger.Success($"Servidor '{_config.ServerName}' ouvindo em UDP {_config.Port} " +
                       $"(max {_config.MaxPlayers} jogadores, tick {_config.TickRate} Hz, protocolo v{Protocol.Version}).");
        Logger.Info("Digite 'help' para ver os comandos do console.");

        long tickInterval = 1000 / _config.TickRate;
        long nextTick = 0;
        long nextMaintenance = 0;
        _nextWorldTickMs = 30000;
        _nextPaydayMs = Math.Max(30, _config.PaydayIntervalSeconds) * 1000L;

        while (_running)
        {
            _net.PollEvents();          // dispara os eventos de rede NESTA thread
            ProcessConsoleCommands();

            long now = _clock.ElapsedMilliseconds;

            if (now >= nextTick)
            {
                BroadcastStates();
                nextTick = now + tickInterval;
            }

            if (now >= nextMaintenance)
            {
                RunMaintenance();
                nextMaintenance = now + 1000;
            }

            Thread.Sleep(1);
        }

        Shutdown();
    }

    public void Stop() => _running = false;

    public void EnqueueCommand(string line)
    {
        if (!string.IsNullOrWhiteSpace(line))
            _pendingCommands.Enqueue(line.Trim());
    }

    private void Shutdown()
    {
        Logger.Info("Desligando servidor...");
        _httpServer.Dispose();
        _resources.UnloadAll();
        foreach (var s in _sessions.Values.ToList())
            _net.DisconnectPeer(s.Peer, MakeReasonWriter("Servidor desligado."));

        // Da tempo para os pacotes de desconexao sairem.
        Thread.Sleep(250);
        _net.Stop();
        _sessions.Clear();
        _vehicles.Clear();
        Logger.Success("Servidor finalizado.");
    }

    // =====================================================================
    // Conexao / desconexao
    // =====================================================================

    private void OnConnectionRequest(ConnectionRequest request)
    {
        ConnectRequestData data;
        try
        {
            data = ConnectRequestData.Deserialize(request.Data);
        }
        catch
        {
            Reject(request, "Pacote de conexao invalido.");
            return;
        }

        if (data.ConnectionKey != Protocol.ConnectionKey)
        {
            // Nao e nosso cliente: rejeita sem detalhes.
            request.Reject();
            return;
        }

        if (data.ProtocolVersion != Protocol.Version)
        {
            Reject(request, $"Versao incompativel. Servidor: v{Protocol.Version}, cliente: v{data.ProtocolVersion}.");
            return;
        }

        if (_sessions.Count >= _config.MaxPlayers)
        {
            Reject(request, "Servidor lotado.");
            return;
        }

        if (_config.Password.Length > 0 && data.Password != _config.Password)
        {
            Reject(request, "Senha incorreta.");
            return;
        }

        string nick = Regex.Replace((data.Nickname ?? "").Trim(), @"\s+", " ");
        if (nick.Length < Protocol.MinNicknameLength || nick.Length > Protocol.MaxNicknameLength || !NicknameRegex.IsMatch(nick))
        {
            Reject(request, $"Apelido invalido. Use {Protocol.MinNicknameLength}-{Protocol.MaxNicknameLength} caracteres (letras, numeros, espacos, _ - . [ ]).");
            return;
        }

        if (_sessions.Values.Any(s => string.Equals(s.Nickname, nick, StringComparison.OrdinalIgnoreCase)))
        {
            Reject(request, "Este apelido ja esta em uso no servidor.");
            return;
        }

        NetPeer peer = request.Accept();
        if (peer == null)
            return;

        // Sessao criada ja no Accept para reservar o apelido/vaga; vira "joined" no PeerConnected.
        _sessions[peer.Id] = new PlayerSession(peer, nick);
        Logger.Info($"Conexao aceita: {nick} ({request.RemoteEndPoint}) -> id #{peer.Id}");
    }

    private void OnPeerConnected(NetPeer peer)
    {
        if (!_sessions.TryGetValue(peer.Id, out var session))
        {
            // Nao deveria acontecer: peer sem sessao (ex.: aceito antes de um restart logico).
            _net.DisconnectPeer(peer, MakeReasonWriter("Sessao invalida."));
            return;
        }

        session.IsJoined = true;

        // 1) Boas-vindas ao novo jogador
        Send(peer, new ServerWelcomePacket
        {
            LocalPlayerId = session.Id,
            ServerName = _config.ServerName,
            Motd = _config.Motd,
            TickRate = (byte)_config.TickRate,
            MaxPlayers = _config.MaxPlayers,
            VoiceRange = _config.VoiceRange
        }, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);

        // 2) Lista de jogadores que ja estao online (+ ultimo estado conhecido para spawn imediato)
        foreach (var other in _sessions.Values)
        {
            if (other == session || !other.IsJoined)
                continue;

            Send(peer, new PlayerJoinedPacket { PlayerId = other.Id, Nickname = other.Nickname },
                Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);

            if (other.HasState)
                Send(peer, other.State, Protocol.ChannelState, DeliveryMethod.Sequenced);
        }

        // 2b) Veiculos ativos no servidor
        foreach (var veh in _vehicles.Values)
        {
            Send(peer, veh.State, Protocol.ChannelVehicle, DeliveryMethod.Sequenced);
        }

        // 2c) Sincronizacao de mundo (hora/clima) e dados de RP (HUD + markers)
        Send(peer, new WorldSyncPacket { Hour = _worldHour, Minute = _worldMinute, Weather = _worldWeather },
            Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);

        if (_config.RoleplayMode)
        {
            var profile = _rpDb.GetOrCreate(session.Nickname);
            SendRpHud(session, profile);

            foreach (var m in _rpDb.Markers)
            {
                Send(peer, new RpMarkerPacket
                {
                    MarkerId = m.Id,
                    Remove = false,
                    Position = new NetVector3(m.X, m.Y, m.Z),
                    Radius = m.Radius,
                    R = m.R, G = m.G, B = m.B,
                    BlipSprite = m.BlipSprite,
                    BlipColor = m.BlipColor,
                    Label = m.Label
                }, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
            }

            if (profile.HasSavedPosition)
            {
                session.LastServerTeleportAt = _clock.ElapsedMilliseconds;
                Send(peer, new ServerActionPacket
                {
                    Action = ServerActionType.Teleport,
                    Position = new NetVector3(profile.LastX, profile.LastY, profile.LastZ)
                }, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
            }
        }

        // 2d) Markers de Resources customizados (scripts JSON)
        SendResourceMarkers(peer);
        _resources.NotifyPlayerJoin(session);

        // 3) Avisa os demais que alguem entrou
        BroadcastExcept(session, new PlayerJoinedPacket { PlayerId = session.Id, Nickname = session.Nickname },
            Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);

        BroadcastSystemChat($"{session.Nickname} entrou no servidor.");
        Logger.Success($"{session} entrou. Online: {JoinedCount}/{_config.MaxPlayers}");
        UpdateTitle();
    }

    private void OnPeerDisconnected(NetPeer peer, DisconnectInfo info)
    {
        if (!_sessions.Remove(peer.Id, out var session))
            return;

        string reason = DescribeDisconnect(info.Reason);

        if (session.IsJoined)
        {
            // Salva ultima posicao RP ao desconectar
            if (_config.RoleplayMode && session.HasState)
            {
                var profile = _rpDb.GetOrCreate(session.Nickname);
                profile.LastX = session.State.Position.X;
                profile.LastY = session.State.Position.Y;
                profile.LastZ = session.State.Position.Z;
                profile.HasSavedPosition = true;
                _rpDb.SavePlayers();
            }

            // Remove veiculos cujo dono era este jogador
            foreach (var vehId in _vehicles.Values.Where(v => v.OwnerPlayerId == session.Id).Select(v => v.NetId).ToList())
            {
                _vehicles.Remove(vehId);
                BroadcastExcept(session, new VehicleRemovedPacket { VehicleNetId = vehId },
                    Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
            }

            BroadcastExcept(session, new PlayerLeftPacket { PlayerId = session.Id, Reason = reason },
                Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
            BroadcastSystemChat($"{session.Nickname} saiu ({reason}).");
            _resources.NotifyPlayerLeave(session);
        }

        Logger.Info($"{session} desconectou: {reason}. Online: {JoinedCount}/{_config.MaxPlayers}");
        UpdateTitle();
    }

    private static string DescribeDisconnect(DisconnectReason reason) => reason switch
    {
        DisconnectReason.RemoteConnectionClose => "saiu do jogo",
        DisconnectReason.Timeout => "tempo esgotado",
        DisconnectReason.DisconnectPeerCalled => "expulso/desconectado pelo servidor",
        DisconnectReason.ConnectionFailed => "falha na conexao",
        _ => reason.ToString()
    };

    private void OnNetworkError(IPEndPoint endPoint, SocketError socketError)
    {
        Logger.Warn($"Erro de socket ({endPoint}): {socketError}");
    }

    // =====================================================================
    // Recebimento de pacotes conectados
    // =====================================================================

    private void OnNetworkReceive(NetPeer peer, NetPacketReader reader, DeliveryMethod deliveryMethod)
    {
        if (!_sessions.TryGetValue(peer.Id, out var session) || !session.IsJoined)
            return;

        if (reader.AvailableBytes < 1)
            return;

        var type = (PacketType)reader.GetByte();

        try
        {
            switch (type)
            {
                case PacketType.PlayerState:
                    HandlePlayerState(session, reader);
                    break;

                case PacketType.VehicleState:
                    HandleVehicleState(session, reader);
                    break;

                case PacketType.VehicleRemoved:
                    HandleVehicleRemoved(session, reader);
                    break;

                case PacketType.WeaponShot:
                    HandleWeaponShot(session, reader);
                    break;

                case PacketType.PlayerDamage:
                    HandlePlayerDamage(session, reader);
                    break;

                case PacketType.ChatMessage:
                    HandleChat(session, reader);
                    break;

                case PacketType.RpInteract:
                    HandleRpInteract(session, reader);
                    break;

                case PacketType.VoiceData:
                    HandleVoiceData(session, reader);
                    break;

                case PacketType.VoiceMode:
                    HandleVoiceMode(session, reader);
                    break;

                case PacketType.MenuSelect:
                    HandleMenuSelect(session, reader);
                    break;

                case PacketType.PedAnimation:
                    HandlePedAnimation(session, reader);
                    break;

                default:
                    RegisterInvalid(session, $"tipo de pacote desconhecido ({(byte)type})");
                    break;
            }
        }
        catch (Exception ex)
        {
            RegisterInvalid(session, $"falha ao ler {type}: {ex.Message}");
        }
    }

    private void HandlePlayerState(PlayerSession session, NetPacketReader reader)
    {
        long now = _clock.ElapsedMilliseconds;
        if (!session.TryConsumeStateBudget(now, _config.MaxStatePacketsPerSecond))
            return; // flood: descarta silenciosamente

        _incomingState.Deserialize(reader);

        if (!IsValidState(_incomingState, out string why))
        {
            RegisterInvalid(session, $"estado invalido ({why})");
            return;
        }

        _incomingState.PlayerId = session.Id;   // anti-spoof: nunca confie no id enviado pelo cliente
        _antiCheat.ValidatePlayerState(session, _incomingState, now);
        session.State.CopyFrom(_incomingState);
        session.HasState = true;
        session.StateDirty = true;
        session.LastStateAt = now;
    }

    private static bool IsValidState(PlayerStatePacket s, out string why)
    {
        if (!s.Position.IsFinite() || !s.Velocity.IsFinite() || !s.AimPosition.IsFinite() ||
            float.IsNaN(s.Heading) || float.IsInfinity(s.Heading))
        {
            why = "NaN/Infinity";
            return false;
        }

        if (Math.Abs(s.Position.X) > Protocol.WorldLimitXY || Math.Abs(s.Position.Y) > Protocol.WorldLimitXY ||
            s.Position.Z < Protocol.WorldMinZ || s.Position.Z > Protocol.WorldMaxZ)
        {
            why = "fora do mapa";
            return false;
        }

        // ~720 km/h: margem generosa para jatos (Hydra/Lazer) e quedas.
        if (s.Velocity.LengthSquared() > 200f * 200f)
        {
            why = "velocidade absurda";
            return false;
        }

        if (s.ModelHash == 0)
        {
            why = "modelo nulo";
            return false;
        }

        why = null;
        return true;
    }

    private void HandleVehicleState(PlayerSession session, NetPacketReader reader)
    {
        _incomingVehicle.Deserialize(reader);

        if (_incomingVehicle.VehicleNetId <= 0 || _incomingVehicle.ModelHash == 0 ||
            !_incomingVehicle.Position.IsFinite() || !_incomingVehicle.Velocity.IsFinite() ||
            !_incomingVehicle.Rotation.IsFinite() || float.IsNaN(_incomingVehicle.SteeringAngle) ||
            Math.Abs(_incomingVehicle.Position.X) > Protocol.WorldLimitXY ||
            Math.Abs(_incomingVehicle.Position.Y) > Protocol.WorldLimitXY ||
            _incomingVehicle.Position.Z < Protocol.WorldMinZ || _incomingVehicle.Position.Z > Protocol.WorldMaxZ)
        {
            RegisterInvalid(session, "estado de veiculo invalido");
            return;
        }

        long now = _clock.ElapsedMilliseconds;
        if (!_vehicles.TryGetValue(_incomingVehicle.VehicleNetId, out var veh))
        {
            // Limita quantos veiculos cada jogador pode registrar
            int owned = _vehicles.Values.Count(v => v.OwnerPlayerId == session.Id);
            if (owned >= Protocol.MaxVehiclesPerPlayer)
                return;

            veh = new VehicleSession(_incomingVehicle.VehicleNetId, session.Id);
            _vehicles[veh.NetId] = veh;
        }
        else if (veh.OwnerPlayerId != session.Id)
        {
            // Permite assumir o controle se o dono anterior nao atualiza ha >2s ou se este jogador e o motorista
            bool ownerStale = now - veh.LastUpdateAt > 2000;
            bool isDriver = session.HasState && session.State.VehicleNetId == veh.NetId && session.State.SeatIndex == Protocol.SeatDriver;
            if (!ownerStale && !isDriver)
                return;

            veh.OwnerPlayerId = session.Id;
        }

        _incomingVehicle.OwnerPlayerId = session.Id;
        veh.State.CopyFrom(_incomingVehicle);
        veh.Dirty = true;
        veh.LastUpdateAt = now;
    }

    private void HandleVehicleRemoved(PlayerSession session, NetPacketReader reader)
    {
        _incomingVehicleRemoved.Deserialize(reader);
        int netId = _incomingVehicleRemoved.VehicleNetId;
        if (_vehicles.TryGetValue(netId, out var veh) && veh.OwnerPlayerId == session.Id)
        {
            _vehicles.Remove(netId);
            BroadcastExcept(session, new VehicleRemovedPacket { VehicleNetId = netId },
                Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
        }
    }

    private void HandleWeaponShot(PlayerSession session, NetPacketReader reader)
    {
        _incomingShot.Deserialize(reader);
        if (!_incomingShot.Origin.IsFinite() || !_incomingShot.Target.IsFinite() || _incomingShot.WeaponHash == 0)
        {
            RegisterInvalid(session, "disparo invalido");
            return;
        }

        _incomingShot.ShooterPlayerId = session.Id;
        BroadcastExcept(session, _incomingShot, Protocol.ChannelReliable, DeliveryMethod.Unreliable);
    }

    private void HandlePlayerDamage(PlayerSession session, NetPacketReader reader)
    {
        _incomingDamage.Deserialize(reader);
        if (_incomingDamage.TargetPlayerId == session.Id)
            return;

        if (!_sessions.TryGetValue(_incomingDamage.TargetPlayerId, out var target) || !target.IsJoined)
            return;

        if (_incomingDamage.Damage <= 0 || _incomingDamage.Damage > Protocol.MaxDamagePerHit)
        {
            RegisterInvalid(session, "dano fora dos limites");
            return;
        }

        if (!_antiCheat.ValidateDamage(session, _clock.ElapsedMilliseconds))
            return;

        _incomingDamage.AttackerPlayerId = session.Id;
        Send(target.Peer, _incomingDamage, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
    }

    private void HandleChat(PlayerSession session, NetPacketReader reader)
    {
        _incomingChat.Deserialize(reader);

        long now = _clock.ElapsedMilliseconds;
        if (session.LastChatAt != 0 && now - session.LastChatAt < _config.ChatCooldownMs)
        {
            SendSystemChat(session, "Aguarde um pouco antes de enviar outra mensagem.");
            return;
        }
        session.LastChatAt = now;

        string text = SanitizeChat(_incomingChat.Text);
        if (text.Length == 0)
            return;

        if (text[0] == '/')
        {
            HandleChatCommand(session, text);
            return;
        }

        Logger.Chat($"{session.Nickname}: {text}");

        var packet = new ChatMessagePacket
        {
            SenderId = session.Id,
            SenderName = _config.RoleplayMode ? $"[Local] {session.Nickname}" : session.Nickname,
            Text = text
        };

        if (_config.RoleplayMode && _config.ChatProximityRange > 0f && session.HasState)
            BroadcastProximity(session, packet, _config.ChatProximityRange);
        else
            BroadcastAll(packet, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
    }

    /// <summary>Remove caracteres de controle e '~' (codigos de cor/formatacao do GTA, ex.: ~r~ ~h~).</summary>
    private static string SanitizeChat(string input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        var sb = new StringBuilder(input.Length);
        foreach (char c in input)
        {
            if (char.IsControl(c) || c == '~')
                continue;
            sb.Append(c);
        }

        string result = sb.ToString().Trim();
        return result.Length > Protocol.MaxChatLength ? result[..Protocol.MaxChatLength] : result;
    }

    private void HandleChatCommand(PlayerSession session, string text)
    {
        string[] parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        string cmd = parts[0].ToLowerInvariant();
        string arg = parts.Length > 1 ? parts[1].Trim() : "";

        // Primeiro tenta resources/plugins customizados (estilo FiveM)
        if (_resources.TryHandleCommand(session, cmd, arg))
            return;

        switch (cmd)
        {
            case "/help":
                SendSystemChat(session, "Geral: /players /ping /nick <novo> /ooc /me /do /g(ritar) /s(ussurrar) /menu");
                SendSystemChat(session, "Voz/Emote: /radio <freq|0> /e <dancar|sentar|maos|acenar|fumar|parar>");
                SendSystemChat(session, "RP: /emprego /pagar <id> <valor> /depositar <valor> /sacar <valor>");
                SendSystemChat(session, "Freeroam/Admin: /carro <nome> /skin <nome> /arma <nome> /tp <x> <y> <z>");
                break;

            case "/nick":
                {
                    string newNick = Regex.Replace((arg ?? "").Trim(), @"\s+", " ");
                    if (newNick.Length < Protocol.MinNicknameLength || newNick.Length > Protocol.MaxNicknameLength || !NicknameRegex.IsMatch(newNick))
                    {
                        SendSystemChat(session, $"Apelido invalido ({Protocol.MinNicknameLength}-{Protocol.MaxNicknameLength} caracteres).");
                        break;
                    }
                    if (_sessions.Values.Any(s => s != session && string.Equals(s.Nickname, newNick, StringComparison.OrdinalIgnoreCase)))
                    {
                        SendSystemChat(session, "Este apelido ja esta em uso.");
                        break;
                    }
                    string oldNick = session.Nickname;
                    session.Nickname = newNick;
                    BroadcastExcept(session, new PlayerJoinedPacket { PlayerId = session.Id, Nickname = newNick },
                        Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
                    BroadcastSystemChat($"{oldNick} mudou o apelido para {newNick}.");
                    break;
                }

            case "/menu":
                OpenMainInteractionMenu(session);
                break;

            case "/radio":
                {
                    if (!int.TryParse(arg, out int freq) || freq < 0 || freq > 9999)
                    {
                        SendSystemChat(session, $"Uso: /radio <frequencia 1-9999 ou 0 para desligar>. Atual: {(session.RadioChannel > 0 ? session.RadioChannel + " MHz" : "Desligado")}");
                        break;
                    }
                    session.RadioChannel = freq;
                    if (freq == 0 && session.VoiceMode == VoiceRangeMode.Radio)
                        session.VoiceMode = VoiceRangeMode.Normal;
                    else if (freq > 0)
                        session.VoiceMode = VoiceRangeMode.Radio;

                    Send(session.Peer, new VoiceModePacket { Mode = session.VoiceMode, RadioChannel = session.RadioChannel },
                        Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
                    SendActionNotify(session, freq > 0 ? $"~b~Radio:~w~ conectado na frequencia ~y~{freq} MHz" : "~y~Radio:~w~ desligado.");
                    break;
                }

            case "/e":
            case "/emote":
                HandleEmoteCommand(session, arg);
                break;

            case "/players":
                var names = _sessions.Values.Where(s => s.IsJoined).Select(s => $"{s.Nickname}(#{s.Id})");
                SendSystemChat(session, $"Online ({JoinedCount}/{_config.MaxPlayers}): {string.Join(", ", names)}");
                break;

            case "/ping":
                SendSystemChat(session, $"Seu ping: {session.Peer.Ping} ms");
                break;

            case "/ooc":
                if (arg.Length == 0) { SendSystemChat(session, "Uso: /ooc <mensagem>"); break; }
                BroadcastAll(new ChatMessagePacket { SenderId = session.Id, SenderName = $"[OOC] {session.Nickname}", Text = arg },
                    Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
                break;

            case "/me":
                if (arg.Length == 0) { SendSystemChat(session, "Uso: /me <acao>"); break; }
                BroadcastProximity(session, new ChatMessagePacket { SenderId = Protocol.ServerPlayerId, SenderName = "RP", Text = $"* {session.Nickname} {arg}" }, _config.ChatProximityRange);
                break;

            case "/do":
                if (arg.Length == 0) { SendSystemChat(session, "Uso: /do <ambiente/estado>"); break; }
                BroadcastProximity(session, new ChatMessagePacket { SenderId = Protocol.ServerPlayerId, SenderName = "RP", Text = $"** {arg} (({session.Nickname}))" }, _config.ChatProximityRange);
                break;

            case "/g":
            case "/gritar":
                if (arg.Length == 0) { SendSystemChat(session, "Uso: /g <mensagem>"); break; }
                BroadcastProximity(session, new ChatMessagePacket { SenderId = session.Id, SenderName = $"[Grita] {session.Nickname}", Text = arg.ToUpperInvariant() }, _config.ChatProximityRange * 2f);
                break;

            case "/s":
            case "/sussurrar":
                if (arg.Length == 0) { SendSystemChat(session, "Uso: /s <mensagem>"); break; }
                BroadcastProximity(session, new ChatMessagePacket { SenderId = session.Id, SenderName = $"[Sussurra] {session.Nickname}", Text = arg }, 8f);
                break;

            case "/emprego":
                {
                    var p = _rpDb.GetOrCreate(session.Nickname);
                    SendSystemChat(session, $"Emprego: {p.JobName} | Salario: ${RpDatabase.GetSalary(p.JobName)} | Dinheiro: ${p.Cash} | Banco: ${p.Bank}");
                    break;
                }

            case "/pagar":
                {
                    string[] a = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (a.Length < 2 || !int.TryParse(a[0], out int targetId) || !long.TryParse(a[1], out long amount) || amount <= 0)
                    { SendSystemChat(session, "Uso: /pagar <id> <valor>"); break; }
                    if (!_sessions.TryGetValue(targetId, out var target) || !target.IsJoined || target == session)
                    { SendSystemChat(session, "Jogador nao encontrado."); break; }

                    var pFrom = _rpDb.GetOrCreate(session.Nickname);
                    if (pFrom.Cash < amount) { SendSystemChat(session, "Dinheiro insuficiente em maos."); break; }

                    var pTo = _rpDb.GetOrCreate(target.Nickname);
                    pFrom.Cash -= amount;
                    pTo.Cash += amount;
                    _rpDb.SavePlayers();
                    SendRpHud(session, pFrom);
                    SendRpHud(target, pTo);
                    SendSystemChat(session, $"Voce pagou ${amount} para {target.Nickname}.");
                    SendSystemChat(target, $"Voce recebeu ${amount} de {session.Nickname}.");
                    break;
                }

            case "/depositar":
                {
                    if (!long.TryParse(arg, out long amount) || amount <= 0) { SendSystemChat(session, "Uso: /depositar <valor>"); break; }
                    var p = _rpDb.GetOrCreate(session.Nickname);
                    if (p.Cash < amount) { SendSystemChat(session, "Dinheiro insuficiente em maos."); break; }
                    p.Cash -= amount; p.Bank += amount;
                    _rpDb.SavePlayers();
                    SendRpHud(session, p);
                    SendSystemChat(session, $"Depositado ${amount} no banco.");
                    break;
                }

            case "/sacar":
                {
                    if (!long.TryParse(arg, out long amount) || amount <= 0) { SendSystemChat(session, "Uso: /sacar <valor>"); break; }
                    var p = _rpDb.GetOrCreate(session.Nickname);
                    if (p.Bank < amount) { SendSystemChat(session, "Saldo bancario insuficiente."); break; }
                    p.Bank -= amount; p.Cash += amount;
                    _rpDb.SavePlayers();
                    SendRpHud(session, p);
                    SendSystemChat(session, $"Sacado ${amount} do banco.");
                    break;
                }

            case "/carro":
                if (arg.Length == 0) { SendSystemChat(session, "Uso: /carro <modelo> (ex.: adder, sultan, police, bati)"); break; }
                Send(session.Peer, new ServerActionPacket
                {
                    Action = ServerActionType.SpawnVehicle,
                    UintParam = JenkinsHash(arg),
                    TextParam = arg
                }, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
                break;

            case "/skin":
                if (arg.Length == 0) { SendSystemChat(session, "Uso: /skin <modelo> (ex.: s_m_y_cop_01, mp_m_freemode_01)"); break; }
                Send(session.Peer, new ServerActionPacket
                {
                    Action = ServerActionType.SetModel,
                    UintParam = JenkinsHash(arg),
                    TextParam = arg
                }, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
                break;

            case "/arma":
                if (arg.Length == 0) { SendSystemChat(session, "Uso: /arma <nome> (ex.: weapon_pistol, weapon_carbinerifle)"); break; }
                Send(session.Peer, new ServerActionPacket
                {
                    Action = ServerActionType.GiveWeapon,
                    UintParam = JenkinsHash(arg.StartsWith("weapon_", StringComparison.OrdinalIgnoreCase) ? arg : "weapon_" + arg),
                    IntParam = 250
                }, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
                break;

            case "/tp":
                {
                    string[] a = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (a.Length < 3 || !float.TryParse(a[0], System.Globalization.CultureInfo.InvariantCulture, out float x) ||
                        !float.TryParse(a[1], System.Globalization.CultureInfo.InvariantCulture, out float y) ||
                        !float.TryParse(a[2], System.Globalization.CultureInfo.InvariantCulture, out float z))
                    { SendSystemChat(session, "Uso: /tp <x> <y> <z>"); break; }
                    Send(session.Peer, new ServerActionPacket
                    {
                        Action = ServerActionType.Teleport,
                        Position = new NetVector3(x, y, z)
                    }, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
                    session.LastServerTeleportAt = _clock.ElapsedMilliseconds;
                    break;
                }

            default:
                SendSystemChat(session, "Comando desconhecido. Use /help.");
                break;
        }
    }

    private void HandleRpInteract(PlayerSession session, NetPacketReader reader)
    {
        _incomingInteract.Deserialize(reader);

        // Primeiro verifica markers de resources/plugins customizados
        if (_resources.TryHandleMarkerInteract(session, _incomingInteract.MarkerId))
            return;

        if (!_config.RoleplayMode) return;

        var marker = _rpDb.Markers.FirstOrDefault(m => m.Id == _incomingInteract.MarkerId);
        if (marker == null) return;

        // Valida proximidade no servidor
        if (session.HasState)
        {
            float maxDist = marker.Radius + 4.0f;
            if (NetVector3.DistanceSquared(session.State.Position, new NetVector3(marker.X, marker.Y, marker.Z)) > maxDist * maxDist)
                return;
        }

        var profile = _rpDb.GetOrCreate(session.Nickname);

        switch (marker.Action.ToLowerInvariant())
        {
            case "agency":
                profile.JobName = RpDatabase.GetNextJob(profile.JobName);
                _rpDb.SavePlayers();
                SendRpHud(session, profile);
                SendActionNotify(session, $"~g~Novo emprego:~w~ {profile.JobName} (salario ${RpDatabase.GetSalary(profile.JobName)})");
                break;

            case "garage":
                string vehModel = profile.JobName == "Policial" ? "police" :
                                  profile.JobName == "SAMU" ? "ambulance" :
                                  profile.JobName == "Taxista" ? "taxi" :
                                  profile.JobName == "Mecanico" ? "towtruck" : "sultan";
                Send(session.Peer, new ServerActionPacket
                {
                    Action = ServerActionType.SpawnVehicle,
                    UintParam = JenkinsHash(vehModel),
                    TextParam = vehModel
                }, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
                SendActionNotify(session, $"~b~Garagem:~w~ veiculo ~y~{vehModel}~w~ retirado.");
                break;

            case "hospital":
                Send(session.Peer, new ServerActionPacket { Action = ServerActionType.Heal, IntParam = 200 },
                    Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
                SendActionNotify(session, "~g~Hospital:~w~ sua saude foi restaurada.");
                break;

            case "bank":
                if (profile.Cash > 0)
                {
                    long moved = profile.Cash;
                    profile.Bank += moved;
                    profile.Cash = 0;
                    _rpDb.SavePlayers();
                    SendRpHud(session, profile);
                    SendActionNotify(session, $"~g~Banco:~w~ depositado ${moved}. Saldo: ${profile.Bank}");
                }
                else
                {
                    SendActionNotify(session, $"~y~Banco:~w~ saldo atual ${profile.Bank}. Use /sacar <valor>.");
                }
                break;
        }
    }

    private void HandleVoiceData(PlayerSession session, NetPacketReader reader)
    {
        _incomingVoice.Deserialize(reader);
        if (_incomingVoice.AudioData == null || _incomingVoice.AudioData.Length == 0 || !session.HasState)
            return;

        VoiceRangeMode mode = session.VoiceMode;
        bool isRadio = mode == VoiceRangeMode.Radio && session.RadioChannel > 0;

        float range = mode switch
        {
            VoiceRangeMode.Whisper => Protocol.WhisperVoiceRange,
            VoiceRangeMode.Shout => Math.Max(_config.VoiceRange * 1.8f, Protocol.ShoutVoiceRange),
            _ => _config.VoiceRange
        };
        float rangeSq = range * range;

        _incomingVoice.SpeakerPlayerId = session.Id;
        _incomingVoice.Mode = mode;
        _incomingVoice.SpeakerPos = session.State.Position;

        foreach (var target in _sessions.Values)
        {
            if (target == session || !target.IsJoined || !target.HasState)
                continue;

            bool sameRadio = isRadio && target.RadioChannel == session.RadioChannel;
            float distSq = NetVector3.DistanceSquared(session.State.Position, target.State.Position);

            if (!sameRadio && distSq > rangeSq)
                continue;

            _incomingVoice.RadioChannel = sameRadio ? session.RadioChannel : 0;
            _incomingVoice.Distance = sameRadio ? 0f : (float)Math.Sqrt(distSq);
            target.Peer.Send(PacketWriter.Write(_writer, _incomingVoice), Protocol.ChannelVoice, DeliveryMethod.Unreliable);
        }
    }

    private void HandleVoiceMode(PlayerSession session, NetPacketReader reader)
    {
        _incomingVoiceMode.Deserialize(reader);
        session.VoiceMode = _incomingVoiceMode.Mode;
        if (_incomingVoiceMode.RadioChannel >= 0 && _incomingVoiceMode.RadioChannel <= 9999)
            session.RadioChannel = _incomingVoiceMode.RadioChannel;
    }

    private void HandleMenuSelect(PlayerSession session, NetPacketReader reader)
    {
        _incomingMenuSelect.Deserialize(reader);
        string menuId = _incomingMenuSelect.MenuId ?? "";
        string itemId = _incomingMenuSelect.ItemId ?? "";

        if (_resources.TryHandleMenuSelect(session, menuId, itemId))
            return;

        if (menuId == "gtamp_main")
        {
            switch (itemId)
            {
                case "emotes":
                    OpenEmotesMenu(session);
                    break;
                case "vestiario":
                    if (!_resources.TryOpenJsonMenu(session, "vestiario"))
                        SendActionNotify(session, "~y~Vestiario indisponivel.");
                    break;
                case "concessionaria":
                    if (!_resources.TryOpenJsonMenu(session, "concessionaria"))
                        SendActionNotify(session, "~y~Concessionaria indisponivel.");
                    break;
                case "radio_190":
                    session.RadioChannel = 190;
                    session.VoiceMode = VoiceRangeMode.Radio;
                    Send(session.Peer, new VoiceModePacket { Mode = session.VoiceMode, RadioChannel = 190 }, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
                    SendActionNotify(session, "~b~Radio:~w~ conectado em ~y~190 MHz");
                    CloseMenu(session);
                    break;
                case "radio_off":
                    session.RadioChannel = 0;
                    if (session.VoiceMode == VoiceRangeMode.Radio) session.VoiceMode = VoiceRangeMode.Normal;
                    Send(session.Peer, new VoiceModePacket { Mode = session.VoiceMode, RadioChannel = 0 }, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
                    SendActionNotify(session, "~y~Radio:~w~ desligado.");
                    CloseMenu(session);
                    break;
                case "heal":
                    Heal(session, 200);
                    SendActionNotify(session, "~g~Vida restaurada.");
                    CloseMenu(session);
                    break;
            }
        }
        else if (menuId == "gtamp_emotes")
        {
            HandleEmoteCommand(session, itemId);
            CloseMenu(session);
        }
    }

    private void HandlePedAnimation(PlayerSession session, NetPacketReader reader)
    {
        _incomingAnim.Deserialize(reader);
        _incomingAnim.PlayerId = session.Id;
        BroadcastExcept(session, _incomingAnim, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
    }

    private void OpenMainInteractionMenu(PlayerSession session)
    {
        OpenMenu(session, new PlayerMenuPacket
        {
            MenuId = "gtamp_main",
            Title = "GTAMP Menu",
            Subtitle = "Acoes Rapidas",
            Items = new List<MenuItemData>
            {
                new() { Id = "emotes", Label = "Emotes / Animacoes", RightLabel = ">" },
                new() { Id = "vestiario", Label = "Vestiario (Skins)", RightLabel = ">" },
                new() { Id = "concessionaria", Label = "Concessionaria", RightLabel = ">" },
                new() { Id = "radio_190", Label = "Entrar Radio 190 MHz", RightLabel = "RP" },
                new() { Id = "radio_off", Label = "Desligar Radio", RightLabel = "OFF" },
                new() { Id = "heal", Label = "Curar Personagem", RightLabel = "HP" }
            }
        });
    }

    private void OpenEmotesMenu(PlayerSession session)
    {
        OpenMenu(session, new PlayerMenuPacket
        {
            MenuId = "gtamp_emotes",
            Title = "Emotes",
            Subtitle = "Escolha uma animacao",
            Items = new List<MenuItemData>
            {
                new() { Id = "dancar", Label = "Dancar", RightLabel = "/e dancar" },
                new() { Id = "maos", Label = "Render-se (Maos pro alto)", RightLabel = "/e maos" },
                new() { Id = "acenar", Label = "Acenar", RightLabel = "/e acenar" },
                new() { Id = "baterpalma", Label = "Bater Palmas", RightLabel = "/e baterpalma" },
                new() { Id = "fumar", Label = "Fumar", RightLabel = "/e fumar" },
                new() { Id = "sentar", Label = "Sentar no Chao", RightLabel = "/e sentar" },
                new() { Id = "parar", Label = "Parar Animacao", RightLabel = "X" }
            }
        });
    }

    private void HandleEmoteCommand(PlayerSession session, string emote)
    {
        string key = (emote ?? "").Trim().ToLowerInvariant();
        if (key == "" || key == "parar" || key == "stop" || key == "c")
        {
            StopAnimation(session);
            return;
        }

        // 100% animacoes do jogo base 2013 (sem dependencia de DLCs posteriores / compativel com GTA V Lighter)
        (string dict, string name, int flags) anim = key switch
        {
            "dancar" => ("misschinese2_crystalmazemcs1_cs", "dance_loop_tao", 1),
            "maos" => ("random@mugging3", "handsup_standing_base", 49),
            "acenar" => ("friends@frj@ig_1", "wave_a", 49),
            "baterpalma" => ("amb@world_human_cheering@male_a", "base", 49),
            "fumar" => ("amb@world_human_smoking@male@male_a@enter", "enter", 49),
            "sentar" => ("amb@world_human_picnic@male@idle_a", "idle_a", 1),
            _ => (null, null, 0)
        };

        if (anim.dict == null)
        {
            SendSystemChat(session, "Emotes: dancar, maos, acenar, baterpalma, fumar, sentar, parar");
            return;
        }

        PlayAnimation(session, anim.dict, anim.name, anim.flags);
    }

    private void SendResourceMarkers(NetPeer peer)
    {
        foreach (var m in _resources.Markers)
        {
            Send(peer, new RpMarkerPacket
            {
                MarkerId = m.Id,
                Remove = false,
                Position = new NetVector3(m.X, m.Y, m.Z),
                Radius = m.Radius,
                R = m.R, G = m.G, B = m.B,
                BlipSprite = m.BlipSprite,
                BlipColor = m.BlipColor,
                Label = m.Label
            }, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
        }
    }

    private void BroadcastProximity(PlayerSession origin, ChatMessagePacket packet, float range)
    {
        float rangeSq = range * range;
        PacketWriter.Write(_writer, packet);
        foreach (var target in _sessions.Values)
        {
            if (!target.IsJoined) continue;
            if (target != origin && origin.HasState && target.HasState &&
                NetVector3.DistanceSquared(origin.State.Position, target.State.Position) > rangeSq)
                continue;
            target.Peer.Send(_writer, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
        }
    }

    private void SendRpHud(PlayerSession session, RpPlayerProfile profile)
    {
        Send(session.Peer, new RpHudPacket
        {
            Cash = profile.Cash,
            Bank = profile.Bank,
            JobName = profile.JobName
        }, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
    }

    private void SendActionNotify(PlayerSession session, string msg)
    {
        Send(session.Peer, new ServerActionPacket { Action = ServerActionType.Notify, TextParam = msg },
            Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
    }

    private static uint JenkinsHash(string key)
    {
        string lower = (key ?? "").Trim().ToLowerInvariant();
        uint hash = 0;
        for (int i = 0; i < lower.Length; i++)
        {
            hash += lower[i];
            hash += (hash << 10);
            hash ^= (hash >> 6);
        }
        hash += (hash << 3);
        hash ^= (hash >> 11);
        hash += (hash << 15);
        return hash;
    }

    private void RegisterInvalid(PlayerSession session, string detail)
    {
        session.InvalidPackets++;
        if (session.InvalidPackets <= 5)
            Logger.Warn($"{session}: {detail}");

        if (session.InvalidPackets >= _config.MaxInvalidPackets)
            Kick(session, "Muitos pacotes invalidos.");
    }

    // =====================================================================
    // Mensagens nao conectadas (consulta do Launcher)
    // =====================================================================

    private void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType)
    {
        try
        {
            if (reader.AvailableBytes < 1 || (PacketType)reader.GetByte() != PacketType.ServerInfoRequest)
                return;

            var req = new ServerInfoRequestPacket();
            req.Deserialize(reader);
            if (req.ConnectionKey != Protocol.ConnectionKey)
                return;

            string modpackUrl = _config.ModpackUrl;
            if (string.IsNullOrWhiteSpace(modpackUrl) && _httpServer.IsRunning && _httpServer.HasModpackFiles())
            {
                modpackUrl = !string.IsNullOrWhiteSpace(_config.PublicHost)
                    ? $"http://{_config.PublicHost}:{_config.HttpPort}/modpack.json"
                    : $"auto:{_config.HttpPort}/modpack.json";
            }

            var resp = new ServerInfoResponsePacket
            {
                ServerName = _config.ServerName,
                Motd = _config.Motd,
                Players = JoinedCount,
                MaxPlayers = _config.MaxPlayers,
                ProtocolVersion = Protocol.Version,
                HasPassword = _config.Password.Length > 0,
                GameMode = _config.RoleplayMode ? "Roleplay" : "Freeroam",
                ModpackUrl = modpackUrl
            };

            _net.SendUnconnectedMessage(PacketWriter.Write(_writer, resp), remoteEndPoint);
        }
        catch
        {
            // Pacote lixo vindo da internet: ignora.
        }
    }

    // =====================================================================
    // Tick: retransmissao de estados
    // =====================================================================

    private void BroadcastStates()
    {
        float maxDistSq = _config.StreamDistance > 0 ? _config.StreamDistance * _config.StreamDistance : 0f;

        foreach (var source in _sessions.Values)
        {
            if (!source.IsJoined || !source.StateDirty)
                continue;

            source.StateDirty = false;

            // Serializa UMA vez e envia para todos os alvos.
            PacketWriter.Write(_writer, source.State);

            foreach (var target in _sessions.Values)
            {
                if (target == source || !target.IsJoined)
                    continue;

                // Culling por distancia (opcional). O cliente remove peds sem atualizacao apos alguns segundos.
                if (maxDistSq > 0f && target.HasState &&
                    NetVector3.DistanceSquared(source.State.Position, target.State.Position) > maxDistSq)
                    continue;

                target.Peer.Send(_writer, Protocol.ChannelState, DeliveryMethod.Sequenced);
            }
        }

        // Retransmite estados de veiculos alterados
        foreach (var veh in _vehicles.Values)
        {
            if (!veh.Dirty)
                continue;

            veh.Dirty = false;
            PacketWriter.Write(_writer, veh.State);

            foreach (var target in _sessions.Values)
            {
                if (target.Id == veh.OwnerPlayerId || !target.IsJoined)
                    continue;

                if (maxDistSq > 0f && target.HasState &&
                    NetVector3.DistanceSquared(veh.State.Position, target.State.Position) > maxDistSq)
                    continue;

                target.Peer.Send(_writer, Protocol.ChannelVehicle, DeliveryMethod.Sequenced);
            }
        }
    }

    /// <summary>Executado 1x por segundo: limpa sessoes travadas, veiculos abandonados e atualiza o titulo.</summary>
    private void RunMaintenance()
    {
        long nowTicks = Environment.TickCount64;
        long nowClock = _clock.ElapsedMilliseconds;

        // Sessoes aceitas que nunca concluiram o handshake em 15s.
        foreach (var s in _sessions.Values.Where(s => !s.IsJoined && nowTicks - s.AcceptedAt > 15000).ToList())
        {
            Logger.Warn($"{s}: handshake nao concluido, removendo.");
            _sessions.Remove(s.Id);
            _net.DisconnectPeer(s.Peer);
        }

        // Veiculos sem atualizacao ha mais de 30s (abandonados/deletados sem aviso)
        foreach (var v in _vehicles.Values.Where(v => nowClock - v.LastUpdateAt > 30000).ToList())
        {
            _vehicles.Remove(v.NetId);
            BroadcastAll(new VehicleRemovedPacket { VehicleNetId = v.NetId },
                Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
        }

        // Sincronizacao periodica de hora/clima (a cada 30s avanca 15 min no jogo)
        if (nowClock >= _nextWorldTickMs)
        {
            _nextWorldTickMs = nowClock + 30000;
            int totalMin = _worldHour * 60 + _worldMinute + 15;
            _worldHour = (byte)((totalMin / 60) % 24);
            _worldMinute = (byte)(totalMin % 60);
            BroadcastAll(new WorldSyncPacket { Hour = _worldHour, Minute = _worldMinute, Weather = _worldWeather },
                Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
        }

        // Pagamento periodico de salario RP (Payday)
        if (_config.RoleplayMode && nowClock >= _nextPaydayMs)
        {
            _nextPaydayMs = nowClock + Math.Max(30, _config.PaydayIntervalSeconds) * 1000L;
            bool changed = false;
            foreach (var s in _sessions.Values.Where(x => x.IsJoined))
            {
                var p = _rpDb.GetOrCreate(s.Nickname);
                int salary = RpDatabase.GetSalary(p.JobName);
                p.Bank += salary;
                changed = true;
                SendRpHud(s, p);
                SendActionNotify(s, $"~g~Salario (~w~{p.JobName}~g~):~w~ +${salary} no banco.");
            }
            if (changed) _rpDb.SavePlayers();
        }

        UpdateTitle();
    }

    // =====================================================================
    // Envio (helpers)
    // =====================================================================

    private void Send(NetPeer peer, INetPacket packet, byte channel, DeliveryMethod method)
    {
        peer.Send(PacketWriter.Write(_writer, packet), channel, method);
    }

    private void BroadcastAll(INetPacket packet, byte channel, DeliveryMethod method)
    {
        PacketWriter.Write(_writer, packet);
        foreach (var s in _sessions.Values)
            if (s.IsJoined)
                s.Peer.Send(_writer, channel, method);
    }

    private void BroadcastExcept(PlayerSession except, INetPacket packet, byte channel, DeliveryMethod method)
    {
        PacketWriter.Write(_writer, packet);
        foreach (var s in _sessions.Values)
            if (s.IsJoined && s != except)
                s.Peer.Send(_writer, channel, method);
    }

    private void SendSystemChat(PlayerSession target, string text)
    {
        Send(target.Peer, new ChatMessagePacket
        {
            SenderId = Protocol.ServerPlayerId,
            SenderName = "Servidor",
            Text = text
        }, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
    }

    private void BroadcastSystemChat(string text)
    {
        BroadcastAll(new ChatMessagePacket
        {
            SenderId = Protocol.ServerPlayerId,
            SenderName = "Servidor",
            Text = text
        }, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
    }

    private static NetDataWriter MakeReasonWriter(string reason)
    {
        var w = new NetDataWriter();
        w.Put(reason ?? string.Empty, Protocol.MaxLongString);
        return w;
    }

    private static void Reject(ConnectionRequest request, string reason)
    {
        Logger.Warn($"Conexao recusada de {request.RemoteEndPoint}: {reason}");
        request.Reject(MakeReasonWriter(reason));
    }

    private void Kick(PlayerSession session, string reason)
    {
        Logger.Warn($"Expulsando {session}: {reason}");
        _net.DisconnectPeer(session.Peer, MakeReasonWriter("Expulso: " + reason));
    }

    private void UpdateTitle()
    {
        if (OperatingSystem.IsWindows())
        {
            try { Console.Title = $"GTAMP Server - {_config.ServerName} - {JoinedCount}/{_config.MaxPlayers} - UDP {_config.Port}"; }
            catch { /* console redirecionado */ }
        }
    }

    // =====================================================================
    // Comandos do console
    // =====================================================================

    private void ProcessConsoleCommands()
    {
        while (_pendingCommands.TryDequeue(out var line))
        {
            string[] parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            string cmd = parts[0].ToLowerInvariant();
            string arg = parts.Length > 1 ? parts[1].Trim() : "";

            switch (cmd)
            {
                case "help":
                    Logger.Info("Comandos: help | status | players | resources | reload | say <msg> | kick <id> [motivo] | time <h> <m> | weather <nome> | givecash <id> <valor> | stop");
                    break;

                case "resources":
                    Logger.Info("Resources ativos: " + (_resources.LoadedResourceNames.Count > 0 ? string.Join(", ", _resources.LoadedResourceNames) : "nenhum"));
                    break;

                case "reload":
                    _resources.LoadAll();
                    foreach (var s in _sessions.Values.Where(x => x.IsJoined))
                        SendResourceMarkers(s.Peer);
                    BroadcastSystemChat("Resources do servidor recarregados.");
                    break;

                case "time":
                    {
                        string[] t = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (t.Length < 2 || !byte.TryParse(t[0], out byte h) || !byte.TryParse(t[1], out byte m))
                        { Logger.Warn("Uso: time <hora 0-23> <minuto 0-59>"); break; }
                        _worldHour = (byte)(h % 24);
                        _worldMinute = (byte)(m % 60);
                        BroadcastAll(new WorldSyncPacket { Hour = _worldHour, Minute = _worldMinute, Weather = _worldWeather },
                            Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
                        Logger.Info($"Horario alterado para {_worldHour:D2}:{_worldMinute:D2}");
                        break;
                    }

                case "weather":
                    if (arg.Length == 0) { Logger.Warn("Uso: weather <EXTRASUNNY|CLEAR|CLOUDS|OVERCAST|RAIN|THUNDER|FOGGY>"); break; }
                    _worldWeather = arg.ToUpperInvariant();
                    BroadcastAll(new WorldSyncPacket { Hour = _worldHour, Minute = _worldMinute, Weather = _worldWeather },
                        Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
                    Logger.Info($"Clima alterado para {_worldWeather}");
                    break;

                case "givecash":
                    {
                        string[] gc = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (gc.Length < 2 || !int.TryParse(gc[0], out int id) || !long.TryParse(gc[1], out long val) || !_sessions.TryGetValue(id, out var tgt))
                        { Logger.Warn("Uso: givecash <id> <valor>"); break; }
                        var prof = _rpDb.GetOrCreate(tgt.Nickname);
                        prof.Cash += val;
                        _rpDb.SavePlayers();
                        SendRpHud(tgt, prof);
                        Logger.Info($"Adicionado ${val} para {tgt.Nickname}.");
                        break;
                    }

                case "status":
                    Logger.Info($"Online {JoinedCount}/{_config.MaxPlayers} | Uptime {TimeSpan.FromMilliseconds(_clock.ElapsedMilliseconds):hh\\:mm\\:ss} | " +
                                $"Tick {_config.TickRate} Hz | Porta UDP {_config.Port}");
                    break;

                case "players":
                    if (_sessions.Count == 0) { Logger.Info("Nenhum jogador online."); break; }
                    foreach (var s in _sessions.Values)
                    {
                        string pos = s.HasState ? s.State.Position.ToString() : "sem estado";
                        Logger.Info($"  #{s.Id,-3} {s.Nickname,-24} ping {s.Peer.Ping,4} ms  {pos}{(s.IsJoined ? "" : "  [handshake]")}");
                    }
                    break;

                case "say":
                    if (arg.Length == 0) { Logger.Warn("Uso: say <mensagem>"); break; }
                    BroadcastSystemChat(SanitizeChat(arg));
                    Logger.Chat($"[Servidor]: {arg}");
                    break;

                case "kick":
                    {
                        string[] kickArgs = arg.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                        if (kickArgs.Length == 0 || !int.TryParse(kickArgs[0], out int id) || !_sessions.TryGetValue(id, out var target))
                        {
                            Logger.Warn("Uso: kick <id> [motivo]  (veja os ids com 'players')");
                            break;
                        }
                        Kick(target, kickArgs.Length > 1 ? kickArgs[1] : "Expulso pelo administrador.");
                        break;
                    }

                case "stop":
                case "exit":
                case "quit":
                    Stop();
                    break;

                default:
                    Logger.Warn($"Comando desconhecido: {cmd}. Digite 'help'.");
                    break;
            }
        }
    }

    // =====================================================================
    // Implementacao de IServerApi (usada por Plugins DLL e Scripts JSON)
    // =====================================================================

    public void SendChat(PlayerSession player, string message) => SendSystemChat(player, message);
    public void BroadcastChat(string message) => BroadcastSystemChat(message);
    public void Notify(PlayerSession player, string gtaText) => SendActionNotify(player, gtaText);

    public void Teleport(PlayerSession player, float x, float y, float z, float heading = 0f)
    {
        player.LastServerTeleportAt = _clock.ElapsedMilliseconds;
        Send(player.Peer, new ServerActionPacket
        {
            Action = ServerActionType.Teleport,
            Position = new NetVector3(x, y, z),
            FloatParam = heading
        }, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
    }

    public void SpawnVehicle(PlayerSession player, string modelName)
    {
        Send(player.Peer, new ServerActionPacket
        {
            Action = ServerActionType.SpawnVehicle,
            UintParam = JenkinsHash(modelName),
            TextParam = modelName
        }, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
    }

    public void SetModel(PlayerSession player, string modelName)
    {
        Send(player.Peer, new ServerActionPacket
        {
            Action = ServerActionType.SetModel,
            UintParam = JenkinsHash(modelName),
            TextParam = modelName
        }, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
    }

    public void GiveWeapon(PlayerSession player, string weaponName, int ammo = 250)
    {
        string full = weaponName.StartsWith("weapon_", StringComparison.OrdinalIgnoreCase) ? weaponName : "weapon_" + weaponName;
        Send(player.Peer, new ServerActionPacket
        {
            Action = ServerActionType.GiveWeapon,
            UintParam = JenkinsHash(full),
            IntParam = ammo
        }, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
    }

    public void Heal(PlayerSession player, int health = 200)
    {
        Send(player.Peer, new ServerActionPacket
        {
            Action = ServerActionType.Heal,
            IntParam = health
        }, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
    }

    public void OpenMenu(PlayerSession player, PlayerMenuPacket menu)
    {
        Send(player.Peer, menu, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
    }

    public void CloseMenu(PlayerSession player)
    {
        Send(player.Peer, new PlayerMenuPacket { Close = true }, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
    }

    public void PlayAnimation(PlayerSession player, string dict, string name, int flags = 1)
    {
        var pkt = new PedAnimationPacket
        {
            PlayerId = player.Id,
            Dict = dict,
            Name = name,
            Flags = flags,
            Stop = false
        };
        BroadcastAll(pkt, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
    }

    public void StopAnimation(PlayerSession player)
    {
        var pkt = new PedAnimationPacket
        {
            PlayerId = player.Id,
            Stop = true
        };
        BroadcastAll(pkt, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
    }

    public bool TryChargeCash(PlayerSession player, long amount)
    {
        var prof = _rpDb.GetOrCreate(player.Nickname);
        if (prof.Cash < amount) return false;
        prof.Cash -= amount;
        _rpDb.SavePlayers();
        SendRpHud(player, prof);
        return true;
    }

    public void GiveCash(PlayerSession player, long amount)
    {
        var prof = _rpDb.GetOrCreate(player.Nickname);
        prof.Cash += amount;
        _rpDb.SavePlayers();
        SendRpHud(player, prof);
    }

    public RpPlayerProfile GetProfile(PlayerSession player) => _rpDb.GetOrCreate(player.Nickname);

    private object BuildAdminStatusDto() => new
    {
        serverName = _config.ServerName,
        players = JoinedCount,
        maxPlayers = _config.MaxPlayers,
        tickRate = _config.TickRate,
        weather = _worldWeather,
        time = $"{_worldHour:D2}:{_worldMinute:D2}",
        uptime = TimeSpan.FromMilliseconds(_clock.ElapsedMilliseconds).ToString(@"hh\:mm\:ss"),
        resources = _resources.LoadedResourceNames
    };

    private object BuildAdminPlayersDto() => _sessions.Values
        .Where(s => s.IsJoined)
        .Select(s => new
        {
            id = s.Id,
            nickname = s.Nickname,
            ping = s.Peer.Ping,
            voiceMode = s.VoiceMode.ToString(),
            radio = s.RadioChannel,
            pos = s.HasState ? s.State.Position.ToString() : "-"
        })
        .ToList();
}
