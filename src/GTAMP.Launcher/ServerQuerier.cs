using System.Diagnostics;
using System.Net;
using GTAMP.Shared;
using GTAMP.Shared.Packets;
using LiteNetLib;
using LiteNetLib.Utils;

namespace GTAMP.Launcher;

/// <summary>
/// Consulta um servidor dedicado via pacote UDP nao conectado (ServerInfoRequest -> ServerInfoResponse)
/// para obter jogadores online, nome e ping real sem precisar abrir conexao completa.
/// </summary>
public static class ServerQuerier
{
    public static Task<ServerQueryResult> QueryAsync(string host, int port, int timeoutMs = 1200)
    {
        return Task.Run(() =>
        {
            var result = new ServerQueryResult { Online = false };
            var listener = new EventBasedNetListener();
            var net = new NetManager(listener)
            {
                AutoRecycle = true,
                UnconnectedMessagesEnabled = true,
                ChannelsCount = Protocol.ChannelCount
            };

            var sw = new Stopwatch();
            bool received = false;

            listener.NetworkReceiveUnconnectedEvent += (ep, reader, type) =>
            {
                try
                {
                    if (reader.AvailableBytes < 1 || (PacketType)reader.GetByte() != PacketType.ServerInfoResponse)
                        return;

                    var resp = new ServerInfoResponsePacket();
                    resp.Deserialize(reader);
                    result.Online = true;
                    result.ServerName = resp.ServerName;
                    result.Motd = resp.Motd;
                    result.Players = resp.Players;
                    result.MaxPlayers = resp.MaxPlayers;
                    result.HasPassword = resp.HasPassword;
                    result.GameMode = resp.GameMode ?? "";
                    string mpUrl = resp.ModpackUrl ?? "";
                    if (mpUrl.StartsWith("auto:", StringComparison.OrdinalIgnoreCase))
                        mpUrl = $"http://{host}:{mpUrl.Substring(5)}";
                    result.ModpackUrl = mpUrl;
                    result.PingMs = (int)sw.ElapsedMilliseconds;
                    received = true;
                }
                catch { }
            };

            try
            {
                IPEndPoint target = NetUtils.MakeEndPoint(host, port);
                net.Start();
                sw.Start();
                var writer = PacketWriter.Write(new NetDataWriter(), new ServerInfoRequestPacket());
                net.SendUnconnectedMessage(writer, target);

                while (!received && sw.ElapsedMilliseconds < timeoutMs)
                {
                    net.PollEvents();
                    Thread.Sleep(10);
                }
            }
            catch { }
            finally
            {
                net.Stop();
            }

            return result;
        });
    }
}
