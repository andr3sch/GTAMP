using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTAMP.Shared;
using GTAMP.Shared.Packets;

namespace GTAMP.Client
{
    /// <summary>
    /// Representa um jogador remoto no mundo do GTA V (SHVDN 2.x).
    /// Controla o Ped, Blip no mapa, nametag 3D, interpolacao, animacoes, armas e assento em veiculos.
    /// </summary>
    internal sealed class RemotePlayer
    {
        private const uint FallbackPedHash = 0x705E61F2U; // mp_m_freemode_01
        private const uint BaseGameFallbackPedHash = 0x0D7114C9U; // player_zero (sempre existe no GTA V Lighter)

        public int PlayerId { get; private set; }
        public string Nickname { get; private set; }
        public Ped Ped { get; private set; }
        public Blip Blip { get; private set; }
        public long LastUpdateMs { get; private set; }

        private Vector3 _targetPos;
        private Vector3 _targetVel;
        private float _targetHeading;
        private uint _modelHash = FallbackPedHash;
        private short _health = 200;
        private short _armor;
        private PedStateFlags _flags;
        private uint _weaponHash;
        private uint _equippedWeapon;
        private Vector3 _aimPos;
        private int _vehicleNetId;
        private sbyte _seatIndex = Protocol.SeatNone;

        private bool _hasState;
        private int _lastTaskTimeMs;
        private bool _wasJumping;
        private bool _wasShooting;
        private int _lastVoiceMs;
        private bool _lastVoiceRadio;
        private string _pendingAnimDict;
        private string _pendingAnimName;
        private int _pendingAnimFlags;

        public RemotePlayer(int playerId, string nickname)
        {
            PlayerId = playerId;
            Nickname = string.IsNullOrEmpty(nickname) ? ("Player#" + playerId) : nickname;
            LastUpdateMs = Environment.TickCount;
        }

        public void SetNickname(string nickname)
        {
            if (string.IsNullOrWhiteSpace(nickname))
                return;

            Nickname = nickname.Trim();
            if (Blip != null && Blip.Exists())
            {
                try { Blip.Name = Nickname; } catch { }
            }
        }

        public void ApplyState(PlayerStatePacket s)
        {
            _targetPos = Natives.ToGta(s.Position);
            _targetVel = Natives.ToGta(s.Velocity);
            _targetHeading = s.Heading;
            _modelHash = s.ModelHash != 0 ? s.ModelHash : FallbackPedHash;
            _health = s.Health;
            _armor = s.Armor;
            _flags = s.Flags;
            _weaponHash = s.WeaponHash;
            _aimPos = Natives.ToGta(s.AimPosition);
            _vehicleNetId = s.VehicleNetId;
            _seatIndex = s.SeatIndex;

            _hasState = true;
            LastUpdateMs = Environment.TickCount;
        }

        public void Update(float dt, Dictionary<int, RemoteVehicle> vehicles, Vector3 localPlayerPos)
        {
            if (!_hasState)
                return;

            // Recria o ped se nao existir ou se o modelo mudou
            if (Ped == null || !Ped.Exists() || unchecked((uint)Ped.Model.Hash) != _modelHash)
            {
                SpawnPed();
                if (Ped == null || !Ped.Exists())
                    return;
            }

            // Sincroniza arma equipada
            SyncWeapon();

            // Sincroniza vida basica (nao deixa IA matar o ped remoto antes do pacote de morte)
            if ((_flags & PedStateFlags.Dead) != 0)
            {
                if (!Ped.IsDead)
                    Ped.Kill();
            }
            else
            {
                if (Ped.IsDead)
                {
                    // Respawn do ped remoto
                    SpawnPed();
                    if (Ped == null || !Ped.Exists()) return;
                }
                int targetHp = Math.Max(105, (int)_health);
                if (Ped.Health != targetHp)
                    Ped.Health = targetHp;
                Ped.Armor = Math.Max(0, Math.Min(100, (int)_armor));
            }

            // Verifica se o jogador remoto esta em um veiculo
            bool inSyncedVehicle = false;
            if (_vehicleNetId > 0 && _seatIndex >= Protocol.SeatDriver)
            {
                RemoteVehicle rv;
                if (vehicles.TryGetValue(_vehicleNetId, out rv) && rv.Entity != null && rv.Entity.Exists())
                {
                    inSyncedVehicle = true;
                    if (!Ped.IsInVehicle(rv.Entity) || Natives.GetPedSeatIndex(Ped, rv.Entity) != _seatIndex)
                    {
                        Natives.SetPedIntoVehicle(Ped, rv.Entity, _seatIndex);
                    }
                }
            }

            if (!inSyncedVehicle)
            {
                if (Ped.IsInVehicle())
                {
                    Natives.ClearPedTasksImmediately(Ped);
                }

                UpdateOnFootMovement(dt);
                UpdateOnFootTasks();
            }

            // Aplica animacao pendente quando o dicionario terminar de carregar
            if (!string.IsNullOrEmpty(_pendingAnimDict) && Natives.HasAnimDictLoaded(_pendingAnimDict))
            {
                Natives.TaskPlayAnim(Ped, _pendingAnimDict, _pendingAnimName, 4f, -4f, -1, _pendingAnimFlags, 0f);
                _pendingAnimDict = null;
                _pendingAnimName = null;
            }

            // Desenha nametag 3D acima da cabeca se estiver proximo
            DrawNametag(localPlayerPos);
        }

        public void NotifyVoiceActivity(bool isRadio)
        {
            _lastVoiceMs = Environment.TickCount;
            _lastVoiceRadio = isRadio;
        }

        public void ApplyAnimation(PedAnimationPacket pkt)
        {
            if (Ped == null || !Ped.Exists())
                return;

            if (pkt.Stop || string.IsNullOrEmpty(pkt.Dict))
            {
                _pendingAnimDict = null;
                _pendingAnimName = null;
                Natives.ClearPedTasks(Ped);
                return;
            }

            Natives.RequestAnimDict(pkt.Dict);
            if (Natives.HasAnimDictLoaded(pkt.Dict))
            {
                Natives.TaskPlayAnim(Ped, pkt.Dict, pkt.Name, 4f, -4f, -1, pkt.Flags, 0f);
                _pendingAnimDict = null;
            }
            else
            {
                _pendingAnimDict = pkt.Dict;
                _pendingAnimName = pkt.Name;
                _pendingAnimFlags = pkt.Flags;
            }
        }

        private void UpdateOnFootMovement(float dt)
        {
            Vector3 currentPos = Ped.Position;
            Vector3 predictedPos = _targetPos + _targetVel * Math.Min(dt, 0.1f);
            float dist = currentPos.DistanceTo(predictedPos);

            if (dist > 8f)
            {
                // Teleporta apenas se estiver muito defasado
                Natives.SetEntityCoordsNoOffset(Ped, predictedPos);
            }
            else if (dist > 0.08f)
            {
                float alpha = Clamp01(dt * 10f);
                Vector3 nextPos = Vector3.Lerp(currentPos, predictedPos, alpha);
                Natives.SetEntityCoordsNoOffset(Ped, nextPos);
            }

            // Interpolacao suave de rotacao (menor arco)
            float curH = Ped.Heading;
            float diff = DeltaAngle(curH, _targetHeading);
            Ped.Heading = curH + diff * Clamp01(dt * 12f);

            if ((_flags & PedStateFlags.Dead) == 0 && (_flags & PedStateFlags.Ragdoll) == 0)
            {
                Natives.SetEntityVelocity(Ped, _targetVel);
            }
        }

        private void UpdateOnFootTasks()
        {
            int now = Environment.TickCount;
            bool jumping = (_flags & PedStateFlags.Jumping) != 0;
            bool shooting = (_flags & PedStateFlags.Shooting) != 0;
            bool aiming = (_flags & PedStateFlags.Aiming) != 0;

            if (jumping && !_wasJumping)
            {
                Natives.TaskJump(Ped);
                _lastTaskTimeMs = now;
            }
            _wasJumping = jumping;

            if (shooting)
            {
                if (!_wasShooting || now - _lastTaskTimeMs > 250)
                {
                    Natives.TaskShootAtCoord(Ped, _aimPos, 350);
                    _lastTaskTimeMs = now;
                }
                _wasShooting = true;
                return;
            }
            _wasShooting = false;

            if (aiming)
            {
                if (now - _lastTaskTimeMs > 250)
                {
                    Natives.TaskAimGunAtCoord(Ped, _aimPos, 400);
                    _lastTaskTimeMs = now;
                }
                return;
            }

            // Animacao de caminhada / corrida
            if (now - _lastTaskTimeMs > 220)
            {
                float moveSpeed = 0f;
                if ((_flags & PedStateFlags.Sprinting) != 0) moveSpeed = 3.0f;
                else if ((_flags & PedStateFlags.Running) != 0) moveSpeed = 2.0f;
                else if ((_flags & PedStateFlags.Walking) != 0) moveSpeed = 1.0f;

                if (moveSpeed > 0f && _targetVel.LengthSquared() > 0.1f)
                {
                    Vector3 goal = _targetPos + _targetVel * 0.5f;
                    Natives.TaskGoStraightToCoord(Ped, goal, moveSpeed, 500, _targetHeading, 0.5f);
                }
                else if (Ped.Position.DistanceTo(_targetPos) < 0.4f)
                {
                    Natives.ClearPedTasks(Ped);
                }

                _lastTaskTimeMs = now;
            }
        }

        private void SyncWeapon()
        {
            if (_weaponHash == 0 || _weaponHash == _equippedWeapon)
                return;

            Natives.GiveWeaponToPed(Ped, _weaponHash, 9999, false, true);
            Natives.SetCurrentPedWeapon(Ped, _weaponHash, true);
            Natives.SetPedInfiniteAmmoClip(Ped, true);
            _equippedWeapon = _weaponHash;
        }

        public void FireVisualShot(WeaponShotPacket shot)
        {
            if (Ped == null || !Ped.Exists())
                return;

            Vector3 from = Natives.ToGta(shot.Origin);
            Vector3 to = Natives.ToGta(shot.Target);
            // Dano 0 no projetil visual: o dano real PvP e aplicado via PlayerDamagePacket
            Natives.ShootSingleBulletBetweenCoords(from, to, 0, shot.WeaponHash, Ped, 200f);
        }

        private void DrawNametag(Vector3 localPlayerPos)
        {
            float dist = localPlayerPos.DistanceTo(Ped.Position);
            if (dist > 60f)
                return;

            Vector3 headPos = Ped.Position + new Vector3(0f, 0f, 1.05f);
            float sx, sy;
            if (!Natives.World3dToScreen2d(headPos, out sx, out sy))
                return;

            float scale = Math.Max(0.24f, Math.Min(0.38f, 0.42f - dist * 0.003f));
            bool isTalking = _lastVoiceMs != 0 && unchecked(Environment.TickCount - _lastVoiceMs) < 450;
            if (isTalking)
            {
                string tag = _lastVoiceRadio ? "[RADIO] " + Nickname : "[VOZ] " + Nickname;
                int r = _lastVoiceRadio ? 90 : 90;
                int g = _lastVoiceRadio ? 190 : 255;
                int b = _lastVoiceRadio ? 255 : 120;
                Natives.DrawText2D(tag, sx, sy - 0.028f, scale, r, g, b, 245, true, true);
            }
            else
            {
                Natives.DrawText2D(Nickname, sx, sy - 0.025f, scale, 255, 255, 255, 230, true, true);
            }
        }

        private void SpawnPed()
        {
            Cleanup();

            var model = new Model(unchecked((int)_modelHash));
            if (!model.IsInCdImage || !model.IsPed)
            {
                _modelHash = FallbackPedHash;
                model = new Model(unchecked((int)FallbackPedHash));
                if (!model.IsInCdImage || !model.IsPed)
                {
                    _modelHash = BaseGameFallbackPedHash;
                    model = new Model(unchecked((int)BaseGameFallbackPedHash));
                }
            }

            if (!model.IsLoaded)
            {
                model.Request();
                return;
            }

            Ped = World.CreatePed(model, _targetPos, _targetHeading);
            model.MarkAsNoLongerNeeded();

            if (Ped == null || !Ped.Exists())
                return;

            // Desativa reacoes de IA para o ped obedecer apenas a rede
            Natives.SetBlockingOfNonTemporaryEvents(Ped, true);
            Natives.SetPedFleeAttributes(Ped, 0, false);
            Natives.SetPedCombatAttributes(Ped, 17, true);
            Natives.SetPedDropsWeaponsWhenDead(Ped, false);
            Natives.SetPedCanBeTargetted(Ped, true);
            Ped.CanRagdoll = false;
            Ped.IsInvincible = false;

            _equippedWeapon = 0;

            Blip = Ped.AddBlip();
            if (Blip != null && Blip.Exists())
            {
                Blip.Sprite = BlipSprite.Standard;
                Blip.Color = BlipColor.Blue;
                Blip.Scale = 0.8f;
                Blip.Name = Nickname;
                Blip.IsShortRange = false;
            }
        }

        public void Cleanup()
        {
            if (Blip != null)
            {
                try { if (Blip.Exists()) Blip.Remove(); } catch { }
                Blip = null;
            }
            if (Ped != null)
            {
                try { if (Ped.Exists()) Ped.Delete(); } catch { }
                Ped = null;
            }
        }

        private static float Clamp01(float v)
        {
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }

        private static float DeltaAngle(float current, float target)
        {
            float delta = ((target - current + 540f) % 360f) - 180f;
            return delta;
        }
    }
}
