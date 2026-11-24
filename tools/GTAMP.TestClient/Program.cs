using GTAMP.Shared;
using GTAMP.Shared.Packets;
using LiteNetLib;
using LiteNetLib.Utils;

// Uso: GTAMP.TestClient.exe [host] [porta] [apelido]
// Abra 2+ instancias para ver os estados sendo retransmitidos entre elas.
string host = args.Length > 0 ? args[0] : "127.0.0.1";
int port = args.Length > 1 ? int.Parse(args[1]) : Protocol.DefaultPort;
string nick = args.Length > 2 ? args[2] : "Bot" + Random.Shared.Next(100, 999);

var listener = new EventBasedNetListener();
var client = new NetManager(listener)
{
    AutoRecycle = true,
    ChannelsCount = Protocol.ChannelCount,
    UnconnectedMessagesEnabled = true
};

int localId = -1;
var names = new Dictionary<int, string>();
long receivedStates = 0;

listener.PeerConnectedEvent += peer => Console.WriteLine($"[+] Conectado a {peer.EndPoint}");

listener.PeerDisconnectedEvent += (peer, info) =>
{
    string reason = info.AdditionalData != null && info.AdditionalData.AvailableBytes > 0
        ? info.AdditionalData.GetString(Protocol.MaxLongString)
        : info.Reason.ToString();
    Console.WriteLine($"[-] Desconectado: {reason}");
};

listener.NetworkReceiveEvent += (peer, reader, method) =>
{
    var type = (PacketType)reader.GetByte();
    switch (type)
    {
        case PacketType.ServerWelcome:
            var w = new ServerWelcomePacket(); w.Deserialize(reader);
            localId = w.LocalPlayerId;
            Console.WriteLine($"[i] Bem-vindo a '{w.ServerName}' (id #{localId}, tick {w.TickRate} Hz). MOTD: {w.Motd}");
            break;
        case PacketType.PlayerJoined:
            var j = new PlayerJoinedPacket(); j.Deserialize(reader);
            names[j.PlayerId] = j.Nickname;
            Console.WriteLine($"[i] Jogador entrou: #{j.PlayerId} {j.Nickname}");
            break;
        case PacketType.PlayerLeft:
            var l = new PlayerLeftPacket(); l.Deserialize(reader);
            names.Remove(l.PlayerId);
            Console.WriteLine($"[i] Jogador saiu: #{l.PlayerId} ({l.Reason})");
            break;
        case PacketType.PlayerState:
            var s = new PlayerStatePacket(); s.Deserialize(reader);
            if (++receivedStates % 40 == 0)
                Console.WriteLine($"    estado de #{s.PlayerId} {names.GetValueOrDefault(s.PlayerId, "?")}: pos {s.Position} hdg {s.Heading:F0}");
            break;
        case PacketType.ChatMessage:
            var c = new ChatMessagePacket(); c.Deserialize(reader);
            Console.WriteLine($"[chat] {c.SenderName}: {c.Text}");
            break;
    }
};

client.Start();

// Consulta nao conectada (igual ao que o Launcher fara na Etapa 5)
listener.NetworkReceiveUnconnectedEvent += (ep, reader, type) =>
{
    if ((PacketType)reader.GetByte() != PacketType.ServerInfoResponse) return;
    var info = new ServerInfoResponsePacket(); info.Deserialize(reader);
    Console.WriteLine($"[info] {info.ServerName} | {info.Players}/{info.MaxPlayers} | senha: {info.HasPassword} | v{info.ProtocolVersion}");
};
var infoWriter = PacketWriter.Write(new NetDataWriter(), new ServerInfoRequestPacket());
client.SendUnconnectedMessage(infoWriter, NetUtils.MakeEndPoint(host, port));

var connectData = new NetDataWriter();
new ConnectRequestData { Nickname = nick }.Serialize(connectData);
client.Connect(host, port, connectData);

Console.WriteLine($"Conectando a {host}:{port} como '{nick}'. Digite texto + Enter para chat, 'q' para sair.");

var state = new PlayerStatePacket
{
    ModelHash = 0x705E61F2,          // mp_m_freemode_01
    Health = 200,
    Flags = PedStateFlags.Running,
    VehicleNetId = 0,
    SeatIndex = Protocol.SeatNone
};
var stateWriter = new NetDataWriter();
var chatWriter = new NetDataWriter();
var center = new NetVector3(-1037f, -2737f, 20f); // aeroporto de Los Santos
double t = 0;
bool running = true;

new Thread(() =>
{
    while (running)
    {
        string line = Console.ReadLine();
        if (line == null || line == "q") { running = false; break; }
        var peer = client.FirstPeer;
        if (peer == null) continue;
        PacketWriter.Write(chatWriter, new ChatMessagePacket { Text = line });
        lock (client) peer.Send(chatWriter, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
    }
}) { IsBackground = true }.Start();

while (running)
{
    lock (client) client.PollEvents();

    var peer = client.FirstPeer;
    if (peer != null && peer.ConnectionState == ConnectionState.Connected)
    {
        // Anda em circulo de raio 10 m a ~5 m/s
        t += 0.05 * 0.5;
        state.Position = new NetVector3(center.X + (float)Math.Cos(t) * 10f, center.Y + (float)Math.Sin(t) * 10f, center.Z);
        state.Velocity = new NetVector3((float)-Math.Sin(t) * 5f, (float)Math.Cos(t) * 5f, 0f);
        state.Heading = (float)((t * 180.0 / Math.PI + 180.0) % 360.0);
        PacketWriter.Write(stateWriter, state);
        lock (client) peer.Send(stateWriter, Protocol.ChannelState, DeliveryMethod.Sequenced);
    }

    Thread.Sleep(50); // 20 Hz
}

client.Stop();
