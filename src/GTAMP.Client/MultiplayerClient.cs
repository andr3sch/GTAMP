using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Windows.Forms;
using GTA;
using GTA.Math;
using GTA.Native;
using GTAMP.Shared;
using GTAMP.Shared.Packets;
using LiteNetLib;
using LiteNetLib.Utils;

namespace GTAMP.Client
{
    /// <summary>
    /// Script principal do Mod Multiplayer para GTA V (Build 1.0.463.1 / 1.0.505.2, SHVDN v2.x).
    /// - F9 (configuravel): conectar / desconectar
    /// - T  (configuravel): abrir chat
    /// </summary>
    public sealed class MultiplayerClient : Script
    {
        private readonly ClientConfig _config;
        private readonly string _configPath;
        private readonly EventBasedNetListener _listener = new EventBasedNetListener();
        private readonly NetManager _net;
        private readonly ChatOverlay _chat = new ChatOverlay();
        private readonly NativeMenuOverlay _menu = new NativeMenuOverlay();
        private readonly RpWorldManager _rpWorld = new RpWorldManager();
        private readonly VoiceManager _voice;
        private readonly ConcurrentQueue<byte[]> _outgoingVoice = new ConcurrentQueue<byte[]>();

        private VoiceRangeMode _voiceMode = VoiceRangeMode.Normal;
        private int _radioChannel;
        private string _pendingLocalAnimDict;
        private string _pendingLocalAnimName;
        private int _pendingLocalAnimFlags;

        private readonly Dictionary<int, RemotePlayer> _players = new Dictionary<int, RemotePlayer>();
        private readonly Dictionary<int, RemoteVehicle> _vehicles = new Dictionary<int, RemoteVehicle>();

        // Mapeia handle local de veiculo -> VehicleNetId (evita duplicar veiculos ja sincronizados)
        private readonly Dictionary<int, int> _localVehToNetId = new Dictionary<int, int>();

        private readonly NetDataWriter _writer = new NetDataWriter();
        private readonly PlayerStatePacket _outState = new PlayerStatePacket();
        private readonly VehicleStatePacket _outVehicle = new VehicleStatePacket();

        private int _localPlayerId = -1;
        private int _sendIntervalMs = 50; // ajustado pelo TickRate do servidor no ServerWelcome
        private int _lastSendMs;
        private int _lastShotSendMs;
        private int _nextLocalVehSeq = 1;
        private int _currentOwnedVehicleNetId;
        private bool _autoConnectPending;
        private int _startTickMs;

        // ---- Isolamento do Save do Modo Historia ----
        private bool _hasStorySnapshot;
        private Vector3 _storyPos;
        private float _storyHeading;
        private uint _storyModelHash;
        private int _storyHealth;
        private int _storyArmor;
        private int _storyMoney;

        public MultiplayerClient()
        {
            _configPath = ClientConfig.GetDefaultPath();
            _config = ClientConfig.LoadOrCreate(_configPath);

            _net = new NetManager(_listener)
            {
                AutoRecycle = true,
                ChannelsCount = Protocol.ChannelCount
            };

            _listener.PeerConnectedEvent += OnPeerConnected;
            _listener.PeerDisconnectedEvent += OnPeerDisconnected;
            _listener.NetworkReceiveEvent += OnNetworkReceive;

            _voice = new VoiceManager(data =>
            {
                if (_outgoingVoice.Count < 8)
                    _outgoingVoice.Enqueue(data);
            });
            _voice.Configure(_config);

            _autoConnectPending = _config.AutoConnect;
            _startTickMs = Environment.TickCount;

            Tick += OnTick;
            KeyDown += OnKeyDown;
            KeyUp += OnKeyUp;
            Aborted += OnAborted;

            UI.Notify("~b~GTAMP~w~ carregado. ~y~" + _config.ConnectKey + "~w~ conectar | ~y~" + _config.ChatKey + "~w~ chat | ~y~" + _config.VoiceKey + "~w~ voz | ~y~" + _config.MenuKey + "~w~ menu.");
        }

        private bool IsConnected
        {
            get { return _net.IsRunning && _net.FirstPeer != null && _net.FirstPeer.ConnectionState == ConnectionState.Connected; }
        }

        // =====================================================================
        // Ciclo principal (thread do jogo)
        // =====================================================================

        private void OnTick(object sender, EventArgs e)
        {
            // Aguarda 3 segundos apos o carregamento do jogo antes do auto-connect
            if (_autoConnectPending && unchecked(Environment.TickCount - _startTickMs) > 3000)
            {
                _autoConnectPending = false;
                Connect();
            }

            if (_net.IsRunning)
                _net.PollEvents();

            Ped localPed = Game.Player.Character;
            if (localPed == null || !localPed.Exists())
                return;

            float dt = Math.Max(0.001f, Math.Min(0.1f, Game.LastFrameTime));

            if (IsConnected)
            {
                // Mantem o save do modo historia bloqueado (sem autosave/quick-save) enquanto no servidor
                Natives.SetMissionFlag(true);

                int now = Environment.TickCount;
                if (unchecked(now - _lastSendMs) >= _sendIntervalMs)
                {
                    _lastSendMs = now;
                    SendLocalState(localPed);
                }

                CheckLocalCombat(localPed);
                UpdateRemoteEntities(dt, localPed.Position);
                FlushOutgoingVoice();

                if (!string.IsNullOrEmpty(_pendingLocalAnimDict) && Natives.HasAnimDictLoaded(_pendingLocalAnimDict))
                {
                    Natives.TaskPlayAnim(localPed, _pendingLocalAnimDict, _pendingLocalAnimName, 4f, -4f, -1, _pendingLocalAnimFlags, 0f);
                    _pendingLocalAnimDict = null;
                    _pendingLocalAnimName = null;
                }
            }

            _rpWorld.UpdateAndDraw(localPed.Position, _voice.IsTransmitting, IsConnected, _voiceMode, _radioChannel);
            _menu.Draw();
            _chat.Draw();
        }

        private void FlushOutgoingVoice()
        {
            byte[] frame;
            while (_outgoingVoice.TryDequeue(out frame))
            {
                SendPacket(new VoiceDataPacket
                {
                    SpeakerPlayerId = _localPlayerId,
                    AudioData = frame
                }, Protocol.ChannelVoice, DeliveryMethod.Unreliable);
            }
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (_chat.IsTyping)
            {
                string msg = _chat.HandleKeyDown(e);
                if (msg != null && IsConnected)
                {
                    SendPacket(new ChatMessagePacket { Text = msg }, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
                }
                return;
            }

            if (_menu.IsOpen)
            {
                if (e.KeyCode == _config.MenuKey)
                {
                    _menu.Close();
                    return;
                }
                MenuSelectPacket sel = _menu.HandleKeyDown(e);
                if (sel != null && IsConnected)
                {
                    SendPacket(sel, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
                }
                return;
            }

            if (e.KeyCode == _config.ConnectKey)
            {
                if (_net.IsRunning)
                    Disconnect("Desconectado pelo jogador.");
                else
                    Connect();
            }
            else if (e.KeyCode == _config.ChatKey)
            {
                if (!IsConnected) { UI.Notify("~y~GTAMP:~w~ Desconectado. Aperte ~g~" + _config.ConnectKey + "~w~ para conectar."); return; }
                _voice.StopTalking();
                _chat.OpenInput();
            }
            else if (e.KeyCode == _config.VoiceKey && IsConnected)
            {
                _voice.StartTalking();
            }
            else if (e.KeyCode == _config.VoiceModeKey)
            {
                if (!IsConnected) { UI.Notify("~y~GTAMP:~w~ Desconectado. Aperte ~g~" + _config.ConnectKey + "~w~ para conectar."); return; }
                CycleVoiceMode();
            }
            else if (e.KeyCode == _config.MenuKey)
            {
                if (!IsConnected) { UI.Notify("~y~GTAMP:~w~ Desconectado. Aperte ~g~" + _config.ConnectKey + "~w~ para conectar."); return; }
                SendPacket(new ChatMessagePacket { Text = "/menu" }, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
            }
            else if (e.KeyCode == _config.InteractKey && IsConnected && _rpWorld.ActiveMarkerId > 0)
            {
                SendPacket(new RpInteractPacket { MarkerId = _rpWorld.ActiveMarkerId },
                    Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
            }
        }

        private void CycleVoiceMode()
        {
            if (_voiceMode == VoiceRangeMode.Whisper) _voiceMode = VoiceRangeMode.Normal;
            else if (_voiceMode == VoiceRangeMode.Normal) _voiceMode = VoiceRangeMode.Shout;
            else if (_voiceMode == VoiceRangeMode.Shout && _radioChannel > 0) _voiceMode = VoiceRangeMode.Radio;
            else _voiceMode = VoiceRangeMode.Whisper;

            SendPacket(new VoiceModePacket { Mode = _voiceMode, RadioChannel = _radioChannel },
                Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);

            string name = _voiceMode == VoiceRangeMode.Whisper ? "Sussurrar (6m)" :
                          _voiceMode == VoiceRangeMode.Shout ? "Gritar (45m)" :
                          _voiceMode == VoiceRangeMode.Radio ? ("Radio " + _radioChannel + " MHz") : "Normal";
            UI.Notify("~b~Voz:~w~ " + name);
        }

        private void OnKeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == _config.VoiceKey)
                _voice.StopTalking();
        }

        private void OnAborted(object sender, EventArgs e)
        {
            _voice.Dispose();
            Disconnect(null);
            CleanupAllEntities();
        }

        // =====================================================================
        // Conexao / Desconexao
        // =====================================================================

        private void Connect()
        {
            // Recarrega mp_config.ini caso o Launcher ou o usuario tenha alterado
            ClientConfig fresh = ClientConfig.LoadOrCreate(_configPath);
            _config.ServerIp = fresh.ServerIp;
            _config.ServerPort = fresh.ServerPort;
            _config.ServerPassword = fresh.ServerPassword;
            _config.Nickname = fresh.Nickname;
            _config.VoiceActivation = fresh.VoiceActivation;
            _config.VadThreshold = fresh.VadThreshold;
            _config.MicDeviceIndex = fresh.MicDeviceIndex;
            _config.OutputDeviceIndex = fresh.OutputDeviceIndex;
            _voice.Configure(_config);

            if (_net.IsRunning)
                _net.Stop();

            _net.Start();

            var connectData = new NetDataWriter();
            new ConnectRequestData
            {
                ConnectionKey = Protocol.ConnectionKey,
                ProtocolVersion = Protocol.Version,
                Nickname = _config.Nickname,
                Password = _config.ServerPassword
            }.Serialize(connectData);

            _net.Connect(_config.ServerIp, _config.ServerPort, connectData);
            _chat.AddMessage("GTAMP", "Conectando a " + _config.ServerIp + ":" + _config.ServerPort + " como " + _config.Nickname + "...", true);
        }

        private void Disconnect(string notifyText)
        {
            if (_net.IsRunning)
                _net.Stop();

            _localPlayerId = -1;
            _currentOwnedVehicleNetId = 0;
            CleanupAllEntities();

            if (!string.IsNullOrEmpty(notifyText))
                _chat.AddMessage("GTAMP", notifyText, true);
        }

        private void OnPeerConnected(NetPeer peer)
        {
            SaveStoryModeSnapshot();
            Natives.SetMissionFlag(true);
            _rpWorld.ApplyMultiplayerWorldIpls();
            _chat.AddMessage("GTAMP", "Conectado ao servidor! (Save do Modo Historia protegido)", true);
        }

        private void SaveStoryModeSnapshot()
        {
            if (_hasStorySnapshot)
                return;

            Ped ped = Game.Player.Character;
            if (ped == null || !ped.Exists())
                return;

            _storyPos = ped.Position;
            _storyHeading = ped.Heading;
            _storyModelHash = unchecked((uint)ped.Model.Hash);
            _storyHealth = ped.Health;
            _storyArmor = ped.Armor;
            _storyMoney = Game.Player.Money;
            _hasStorySnapshot = true;
        }

        private void RestoreStoryModeSnapshot()
        {
            Natives.SetMissionFlag(false);
            if (!_hasStorySnapshot)
                return;

            _hasStorySnapshot = false;
            try
            {
                Ped ped = Game.Player.Character;
                if (ped != null && ped.Exists())
                {
                    if (_storyModelHash != 0 && unchecked((uint)ped.Model.Hash) != _storyModelHash)
                    {
                        var model = new Model(unchecked((int)_storyModelHash));
                        if (model.IsInCdImage && model.IsPed && model.Request(1500))
                        {
                            Natives.SetPlayerModel(Game.Player, _storyModelHash);
                            model.MarkAsNoLongerNeeded();
                            ped = Game.Player.Character;
                        }
                    }

                    if (ped != null && ped.Exists())
                    {
                        ped.Position = _storyPos;
                        ped.Heading = _storyHeading;
                        ped.Health = Math.Max(105, _storyHealth);
                        ped.Armor = Math.Max(0, _storyArmor);
                    }
                }
                Game.Player.Money = _storyMoney;
            }
            catch { }
        }

        private void OnPeerDisconnected(NetPeer peer, DisconnectInfo info)
        {
            string reason = info.Reason.ToString();
            if (info.AdditionalData != null && info.AdditionalData.AvailableBytes > 0)
            {
                try { reason = info.AdditionalData.GetString(Protocol.MaxLongString); }
                catch { }
            }

            _localPlayerId = -1;
            _currentOwnedVehicleNetId = 0;
            CleanupAllEntities();
            if (_net.IsRunning)
                _net.Stop();

            _chat.AddMessage("GTAMP", "Desconectado: " + reason, true);
            UI.Notify("~r~GTAMP:~w~ " + reason);
        }

        // =====================================================================
        // Recebimento de pacotes
        // =====================================================================

        private void OnNetworkReceive(NetPeer peer, NetPacketReader reader, DeliveryMethod deliveryMethod)
        {
            if (reader.AvailableBytes < 1)
                return;

            var type = (PacketType)reader.GetByte();
            try
            {
                switch (type)
                {
                    case PacketType.ServerWelcome:
                        {
                            var p = new ServerWelcomePacket();
                            p.Deserialize(reader);
                            _localPlayerId = p.LocalPlayerId;
                            if (p.TickRate >= 10 && p.TickRate <= 60)
                                _sendIntervalMs = 1000 / p.TickRate;
                            _voice.SetServerVoiceRange(p.VoiceRange);
                            _chat.AddMessage("Servidor", p.ServerName + " - " + p.Motd, true);
                            UI.Notify("~g~Conectado:~w~ " + p.ServerName);
                            break;
                        }

                    case PacketType.PlayerJoined:
                        {
                            var p = new PlayerJoinedPacket();
                            p.Deserialize(reader);
                            if (p.PlayerId != _localPlayerId)
                            {
                                RemotePlayer existing;
                                if (_players.TryGetValue(p.PlayerId, out existing))
                                    existing.SetNickname(p.Nickname);
                                else
                                    _players[p.PlayerId] = new RemotePlayer(p.PlayerId, p.Nickname);
                            }
                            break;
                        }

                    case PacketType.PlayerLeft:
                        {
                            var p = new PlayerLeftPacket();
                            p.Deserialize(reader);
                            _voice.RemoveSpeaker(p.PlayerId);
                            RemotePlayer rp;
                            if (_players.TryGetValue(p.PlayerId, out rp))
                            {
                                rp.Cleanup();
                                _players.Remove(p.PlayerId);
                            }
                            break;
                        }

                    case PacketType.PlayerState:
                        {
                            var p = new PlayerStatePacket();
                            p.Deserialize(reader);
                            if (p.PlayerId == _localPlayerId) break;
                            RemotePlayer rp;
                            if (!_players.TryGetValue(p.PlayerId, out rp))
                            {
                                rp = new RemotePlayer(p.PlayerId, "Player#" + p.PlayerId);
                                _players[p.PlayerId] = rp;
                            }
                            rp.ApplyState(p);
                            break;
                        }

                    case PacketType.VehicleState:
                        {
                            var p = new VehicleStatePacket();
                            p.Deserialize(reader);
                            RemoteVehicle rv;
                            if (!_vehicles.TryGetValue(p.VehicleNetId, out rv))
                            {
                                rv = new RemoteVehicle(p.VehicleNetId, p.OwnerPlayerId, null);
                                _vehicles[p.VehicleNetId] = rv;
                            }
                            rv.ApplyState(p);
                            break;
                        }

                    case PacketType.VehicleRemoved:
                        {
                            var p = new VehicleRemovedPacket();
                            p.Deserialize(reader);
                            RemoveSyncedVehicle(p.VehicleNetId);
                            break;
                        }

                    case PacketType.WeaponShot:
                        {
                            var p = new WeaponShotPacket();
                            p.Deserialize(reader);
                            RemotePlayer shooter;
                            if (_players.TryGetValue(p.ShooterPlayerId, out shooter))
                                shooter.FireVisualShot(p);
                            break;
                        }

                    case PacketType.PlayerDamage:
                        {
                            var p = new PlayerDamagePacket();
                            p.Deserialize(reader);
                            Ped localPed = Game.Player.Character;
                            if (localPed != null && localPed.Exists() && !localPed.IsDead)
                            {
                                int dmg = Math.Max(1, Math.Min(Protocol.MaxDamagePerHit, (int)p.Damage));
                                localPed.ApplyDamage(dmg);
                            }
                            break;
                        }

                    case PacketType.ChatMessage:
                        {
                            var p = new ChatMessagePacket();
                            p.Deserialize(reader);
                            bool isSys = p.SenderId == Protocol.ServerPlayerId;
                            _chat.AddMessage(p.SenderName, p.Text, isSys);
                            break;
                        }

                    case PacketType.WorldSync:
                        {
                            var p = new WorldSyncPacket();
                            p.Deserialize(reader);
                            _rpWorld.ApplyWorldSync(p);
                            break;
                        }

                    case PacketType.ServerAction:
                        {
                            var p = new ServerActionPacket();
                            p.Deserialize(reader);
                            _rpWorld.ExecuteServerAction(p);
                            break;
                        }

                    case PacketType.RpHud:
                        {
                            var p = new RpHudPacket();
                            p.Deserialize(reader);
                            _rpWorld.ApplyHud(p);
                            break;
                        }

                    case PacketType.RpMarker:
                        {
                            var p = new RpMarkerPacket();
                            p.Deserialize(reader);
                            _rpWorld.ApplyMarker(p);
                            break;
                        }

                    case PacketType.VoiceData:
                        {
                            var p = new VoiceDataPacket();
                            p.Deserialize(reader);
                            if (p.SpeakerPlayerId != _localPlayerId)
                            {
                                _voice.PlayRemoteAudio(p, Natives.GetGameplayCamCoord(), Natives.GetGameplayCamRot());
                                RemotePlayer speaker;
                                if (_players.TryGetValue(p.SpeakerPlayerId, out speaker))
                                    speaker.NotifyVoiceActivity(p.RadioChannel > 0);
                            }
                            break;
                        }

                    case PacketType.VoiceMode:
                        {
                            var p = new VoiceModePacket();
                            p.Deserialize(reader);
                            _voiceMode = p.Mode;
                            _radioChannel = p.RadioChannel;
                            break;
                        }

                    case PacketType.PlayerMenu:
                        {
                            var p = new PlayerMenuPacket();
                            p.Deserialize(reader);
                            _menu.ApplyPacket(p);
                            break;
                        }

                    case PacketType.PedAnimation:
                        {
                            var p = new PedAnimationPacket();
                            p.Deserialize(reader);
                            if (p.PlayerId == _localPlayerId)
                            {
                                Ped lp = Game.Player.Character;
                                if (lp != null && lp.Exists())
                                {
                                    if (p.Stop || string.IsNullOrEmpty(p.Dict))
                                    {
                                        _pendingLocalAnimDict = null;
                                        Natives.ClearPedTasks(lp);
                                    }
                                    else
                                    {
                                        Natives.RequestAnimDict(p.Dict);
                                        if (Natives.HasAnimDictLoaded(p.Dict))
                                            Natives.TaskPlayAnim(lp, p.Dict, p.Name, 4f, -4f, -1, p.Flags, 0f);
                                        else
                                        {
                                            _pendingLocalAnimDict = p.Dict;
                                            _pendingLocalAnimName = p.Name;
                                            _pendingLocalAnimFlags = p.Flags;
                                        }
                                    }
                                }
                            }
                            else
                            {
                                RemotePlayer rp;
                                if (_players.TryGetValue(p.PlayerId, out rp))
                                    rp.ApplyAnimation(p);
                            }
                            break;
                        }
                }
            }
            catch
            {
                // Ignora pacote malformado
            }
        }

        // =====================================================================
        // Envio do estado local (Ped + Veiculo)
        // =====================================================================

        private void SendLocalState(Ped ped)
        {
            if (_localPlayerId < 0)
                return;

            int vehNetId = 0;
            sbyte seatIndex = Protocol.SeatNone;

            if (ped.IsInVehicle())
            {
                Vehicle veh = ped.CurrentVehicle;
                if (veh != null && veh.Exists())
                {
                    seatIndex = Natives.GetPedSeatIndex(ped, veh);
                    vehNetId = GetOrAssignVehicleNetId(veh);

                    // Apenas o motorista envia VehicleState (autoridade de fisica do carro)
                    if (seatIndex == Protocol.SeatDriver && vehNetId > 0)
                    {
                        _currentOwnedVehicleNetId = vehNetId;
                        RemoteVehicle rv;
                        if (_vehicles.TryGetValue(vehNetId, out rv))
                            rv.SetOwner(_localPlayerId);
                        SendLocalVehicleState(veh, vehNetId);
                    }
                }
            }

            PedStateFlags flags = PedStateFlags.None;
            if (ped.IsWalking) flags |= PedStateFlags.Walking;
            if (ped.IsRunning) flags |= PedStateFlags.Running;
            if (ped.IsSprinting) flags |= PedStateFlags.Sprinting;
            if (ped.IsJumping) flags |= PedStateFlags.Jumping;
            if (Function.Call<bool>((Hash)0x2E397FD2ECD37C87UL, Game.Player.Handle)) flags |= PedStateFlags.Aiming; // IS_PLAYER_FREE_AIMING
            if (ped.IsShooting) flags |= PedStateFlags.Shooting;
            if (Natives.IsPedReloading(ped)) flags |= PedStateFlags.Reloading;
            if (Natives.IsPedInCover(ped)) flags |= PedStateFlags.InCover;
            if (ped.IsRagdoll) flags |= PedStateFlags.Ragdoll;
            if (ped.IsDead) flags |= PedStateFlags.Dead;
            if (Natives.IsPedClimbing(ped)) flags |= PedStateFlags.Climbing;
            if (Natives.IsPedSwimming(ped)) flags |= PedStateFlags.Swimming;
            if (vehNetId > 0) flags |= PedStateFlags.InVehicle;
            if (Natives.IsPedDucking(ped)) flags |= PedStateFlags.Ducking;

            _outState.PlayerId = _localPlayerId;
            _outState.Position = Natives.ToNet(ped.Position);
            _outState.Velocity = Natives.ToNet(ped.Velocity);
            _outState.Heading = ped.Heading;
            _outState.ModelHash = unchecked((uint)ped.Model.Hash);
            _outState.Health = (short)Math.Max(0, Math.Min(short.MaxValue, ped.Health));
            _outState.Armor = (short)Math.Max(0, Math.Min(short.MaxValue, ped.Armor));
            _outState.Flags = flags;
            _outState.WeaponHash = Natives.GetSelectedPedWeapon(ped);
            _outState.AimPosition = Natives.ToNet(Natives.GetAimWorldPosition(50f));
            _outState.VehicleNetId = vehNetId;
            _outState.SeatIndex = seatIndex;

            SendPacket(_outState, Protocol.ChannelState, DeliveryMethod.Sequenced);
        }

        private void SendLocalVehicleState(Vehicle veh, int netId)
        {
            byte primary, secondary;
            Natives.GetVehicleColors(veh, out primary, out secondary);

            VehicleStateFlags vflags = VehicleStateFlags.None;
            if (veh.EngineRunning) vflags |= VehicleStateFlags.EngineOn;
            if (veh.LightsOn) vflags |= VehicleStateFlags.LightsOn;
            if (veh.HighBeamsOn) vflags |= VehicleStateFlags.HighBeams;
            if (veh.SirenActive) vflags |= VehicleStateFlags.SirenOn;
            if (Game.IsControlPressed(0, GTA.Control.VehicleBrake)) vflags |= VehicleStateFlags.BrakeLights;
            if (Natives.IsHornActive(veh)) vflags |= VehicleStateFlags.Horn;
            if (Game.IsControlPressed(0, GTA.Control.VehicleHandbrake)) vflags |= VehicleStateFlags.Handbrake;

            _outVehicle.VehicleNetId = netId;
            _outVehicle.OwnerPlayerId = _localPlayerId;
            _outVehicle.ModelHash = unchecked((uint)veh.Model.Hash);
            _outVehicle.Position = Natives.ToNet(veh.Position);
            _outVehicle.Rotation = Natives.GetEntityQuaternion(veh);
            _outVehicle.Velocity = Natives.ToNet(veh.Velocity);
            _outVehicle.SteeringAngle = veh.SteeringAngle;
            _outVehicle.EngineHealth = veh.EngineHealth;
            _outVehicle.PrimaryColor = primary;
            _outVehicle.SecondaryColor = secondary;
            _outVehicle.Flags = vflags;

            SendPacket(_outVehicle, Protocol.ChannelVehicle, DeliveryMethod.Sequenced);
        }

        private int GetOrAssignVehicleNetId(Vehicle veh)
        {
            int handle = veh.Handle;
            int existingId;
            if (_localVehToNetId.TryGetValue(handle, out existingId))
                return existingId;

            // Verifica se ja e um veiculo remoto sincronizado
            foreach (var kv in _vehicles)
            {
                if (kv.Value.Entity != null && kv.Value.Entity.Exists() && kv.Value.Entity.Handle == handle)
                {
                    _localVehToNetId[handle] = kv.Key;
                    return kv.Key;
                }
            }

            // Gera um ID deterministico sem colisao entre jogadores: (PlayerId+1)*10000 + seq
            int netId = (_localPlayerId + 1) * 10000 + (_nextLocalVehSeq++);
            _localVehToNetId[handle] = netId;
            _vehicles[netId] = new RemoteVehicle(netId, _localPlayerId, veh);
            return netId;
        }

        // =====================================================================
        // Combate: deteccao de tiros e dano PvP
        // =====================================================================

        private void CheckLocalCombat(Ped localPed)
        {
            int now = Environment.TickCount;

            if (localPed.IsShooting && unchecked(now - _lastShotSendMs) >= 60)
            {
                _lastShotSendMs = now;
                Vector3 target;
                if (!Natives.GetPedLastWeaponImpactCoord(localPed, out target))
                    target = Natives.GetAimWorldPosition(80f);

                Vector3 origin = localPed.Position + new Vector3(0f, 0f, 0.65f);
                SendPacket(new WeaponShotPacket
                {
                    ShooterPlayerId = _localPlayerId,
                    WeaponHash = Natives.GetSelectedPedWeapon(localPed),
                    Origin = Natives.ToNet(origin),
                    Target = Natives.ToNet(target)
                }, Protocol.ChannelReliable, DeliveryMethod.Unreliable);
            }

            // Verifica se algum ped remoto sofreu dano causado pelo jogador local
            uint weaponHash = Natives.GetSelectedPedWeapon(localPed);
            foreach (var kv in _players)
            {
                RemotePlayer rp = kv.Value;
                if (rp.Ped == null || !rp.Ped.Exists())
                    continue;

                if (Natives.HasEntityBeenDamagedByEntity(rp.Ped, localPed))
                {
                    Natives.ClearEntityLastDamageEntity(rp.Ped);
                    SendPacket(new PlayerDamagePacket
                    {
                        AttackerPlayerId = _localPlayerId,
                        TargetPlayerId = rp.PlayerId,
                        WeaponHash = weaponHash,
                        Damage = 25,
                        IsHeadshot = false
                    }, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
                }
            }
        }

        // =====================================================================
        // Atualizacao e limpeza de entidades remotas
        // =====================================================================

        private void UpdateRemoteEntities(float dt, Vector3 localPos)
        {
            foreach (var kv in _vehicles)
                kv.Value.Update(_localPlayerId, dt);

            foreach (var kv in _players)
                kv.Value.Update(dt, _vehicles, localPos);
        }

        private void RemoveSyncedVehicle(int netId)
        {
            RemoteVehicle rv;
            if (!_vehicles.TryGetValue(netId, out rv))
                return;

            // Nao deleta o veiculo se o jogador local estiver dentro dele
            Ped localPed = Game.Player.Character;
            bool localInside = localPed != null && localPed.Exists() && localPed.IsInVehicle() &&
                               rv.Entity != null && rv.Entity.Exists() &&
                               localPed.CurrentVehicle.Handle == rv.Entity.Handle;

            if (!localInside)
                rv.Cleanup();

            _vehicles.Remove(netId);
        }

        private void CleanupAllEntities()
        {
            _voice.StopTalking();
            _menu.Close();
            _rpWorld.Cleanup();

            foreach (var kv in _players)
                kv.Value.Cleanup();
            _players.Clear();

            foreach (var kv in _vehicles)
            {
                // So deleta veiculos criados por outros jogadores
                if (kv.Value.OwnerPlayerId != _localPlayerId)
                    kv.Value.Cleanup();
            }
            _vehicles.Clear();
            _localVehToNetId.Clear();
            RestoreStoryModeSnapshot();
        }

        private void SendPacket(INetPacket packet, byte channel, DeliveryMethod method)
        {
            NetPeer peer = _net.FirstPeer;
            if (peer == null || peer.ConnectionState != ConnectionState.Connected)
                return;

            peer.Send(PacketWriter.Write(_writer, packet), channel, method);
        }
    }
}
