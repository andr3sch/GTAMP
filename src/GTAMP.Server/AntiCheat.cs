using GTAMP.Shared;
using GTAMP.Shared.Packets;

namespace GTAMP.Server;

/// <summary>
/// Validacoes basicas de anti-cheat no lado do servidor:
/// - Deteccao de teleporte nao autorizado
/// - Deteccao de spam de dano PvP
/// - Deteccao de vida/colete acima do limite
/// </summary>
public sealed class AntiCheat
{
    private readonly ServerConfig _config;
    private readonly Action<PlayerSession, string> _onKick;

    public AntiCheat(ServerConfig config, Action<PlayerSession, string> onKick)
    {
        _config = config;
        _onKick = onKick;
    }

    public bool ValidatePlayerState(PlayerSession session, PlayerStatePacket state, long nowMs)
    {
        if (!_config.EnableAntiCheat)
            return true;

        // Vida/colete absurdos (GTA V max padrao = 200 HP, 100 Armor; margem ate 500)
        if (state.Health > 500 || state.Armor > 250)
        {
            RegisterStrike(session, $"HP/Armor suspeito (HP={state.Health}, Armor={state.Armor})");
        }

        // Checagem de teleporte (somente apos ter posicao anterior e fora de TP autorizado pelo servidor)
        if (session.LastValidPosAt > 0 && nowMs - session.LastServerTeleportAt > 3500)
        {
            long dtMs = nowMs - session.LastValidPosAt;
            if (dtMs >= 50 && dtMs <= 3000)
            {
                float distSq = NetVector3.DistanceSquared(session.LastValidPos, state.Position);
                // Limite generoso: 220 m/s (~790 km/h) + 60m de folga para lag/respawn
                float maxAllowed = 60f + (dtMs / 1000f) * 220f;
                if (distSq > maxAllowed * maxAllowed)
                {
                    float dist = (float)Math.Sqrt(distSq);
                    RegisterStrike(session, $"possivel teleporte ({dist:F0}m em {dtMs}ms)");
                }
            }
        }

        session.LastValidPos = state.Position;
        session.LastValidPosAt = nowMs;
        return true;
    }

    public bool ValidateDamage(PlayerSession attacker, long nowMs)
    {
        if (!_config.EnableAntiCheat)
            return true;

        if (nowMs - attacker.DamageWindowStart >= 1000)
        {
            attacker.DamageWindowStart = nowMs;
            attacker.DamageWindowCount = 0;
        }

        attacker.DamageWindowCount++;
        if (attacker.DamageWindowCount > 15)
        {
            RegisterStrike(attacker, "spam de pacotes de dano");
            return false;
        }

        return true;
    }

    private void RegisterStrike(PlayerSession session, string reason)
    {
        session.AntiCheatStrikes++;
        Logger.Warn($"[AntiCheat] {session} strike #{session.AntiCheatStrikes}: {reason}");

        if (_config.AutoKickCheaters && session.AntiCheatStrikes >= 5)
        {
            _onKick(session, $"Anti-Cheat: {reason}");
        }
    }
}
