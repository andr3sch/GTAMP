using System;
using GTA;
using GTA.Math;
using GTAMP.Shared;
using GTAMP.Shared.Packets;

namespace GTAMP.Client
{
    /// <summary>
    /// Representa um veiculo sincronizado na rede (controlado por outro jogador ou pelo local).
    /// Faz interpolacao suave de posicao/quaternion e aplica luzes, sirene, buzina e direcao.
    /// </summary>
    internal sealed class RemoteVehicle
    {
        public int NetId { get; private set; }
        public int OwnerPlayerId { get; private set; }
        public Vehicle Entity { get; private set; }
        public uint ModelHash { get; private set; }
        public long LastUpdateMs { get; private set; }

        private Vector3 _targetPos;
        private Vector3 _targetVel;
        private NetQuaternion _targetRot = NetQuaternion.Identity;
        private float _steeringAngle;
        private float _engineHealth = 1000f;
        private byte _primaryColor;
        private byte _secondaryColor;
        private VehicleStateFlags _flags;
        private bool _hasState;
        private bool _colorsApplied;

        public RemoteVehicle(int netId, int ownerPlayerId, Vehicle existingEntity)
        {
            NetId = netId;
            OwnerPlayerId = ownerPlayerId;
            Entity = existingEntity;
            if (existingEntity != null && existingEntity.Exists())
            {
                ModelHash = unchecked((uint)existingEntity.Model.Hash);
                _targetPos = existingEntity.Position;
            }
            LastUpdateMs = Environment.TickCount;
        }

        public void ApplyState(VehicleStatePacket s)
        {
            OwnerPlayerId = s.OwnerPlayerId;
            ModelHash = s.ModelHash;
            _targetPos = Natives.ToGta(s.Position);
            _targetVel = Natives.ToGta(s.Velocity);
            _targetRot = s.Rotation;
            _steeringAngle = s.SteeringAngle;
            _engineHealth = s.EngineHealth;

            if (_primaryColor != s.PrimaryColor || _secondaryColor != s.SecondaryColor)
            {
                _primaryColor = s.PrimaryColor;
                _secondaryColor = s.SecondaryColor;
                _colorsApplied = false;
            }

            _flags = s.Flags;
            _hasState = true;
            LastUpdateMs = Environment.TickCount;
        }

        public void SetOwner(int ownerPlayerId)
        {
            OwnerPlayerId = ownerPlayerId;
            LastUpdateMs = Environment.TickCount;
        }

        /// <summary>
        /// Atualiza o veiculo no jogo quando ele pertence a OUTRO jogador.
        /// Se o jogador local for o dono (motorista), nao sobrescrevemos a fisica local.
        /// </summary>
        public void Update(int localPlayerId, float dt)
        {
            if (!_hasState || OwnerPlayerId == localPlayerId)
                return;

            // Recria o veiculo se nao existir ou se o modelo mudou
            if (Entity == null || !Entity.Exists() || unchecked((uint)Entity.Model.Hash) != ModelHash)
            {
                SpawnVehicle();
                if (Entity == null || !Entity.Exists())
                    return;
            }

            // Extrapolacao leve + interpolacao suave
            Vector3 currentPos = Entity.Position;
            Vector3 predictedPos = _targetPos + _targetVel * Math.Min(dt, 0.1f);
            float dist = currentPos.DistanceTo(predictedPos);

            if (dist > 12f)
            {
                Natives.SetEntityCoordsNoOffset(Entity, predictedPos);
            }
            else
            {
                float alpha = Clamp01(dt * 12f);
                Vector3 nextPos = Vector3.Lerp(currentPos, predictedPos, alpha);
                Natives.SetEntityCoordsNoOffset(Entity, nextPos);
            }

            // Interpolacao de quaternion (NLerp)
            NetQuaternion curRot = Natives.GetEntityQuaternion(Entity);
            NetQuaternion nextRot = Nlerp(curRot, _targetRot, Clamp01(dt * 14f));
            Natives.SetEntityQuaternion(Entity, nextRot);
            Natives.SetEntityVelocity(Entity, _targetVel);

            // Direcao, cores, luzes, sirene e buzina
            Entity.SteeringAngle = _steeringAngle;
            Entity.EngineHealth = _engineHealth;
            Entity.EngineRunning = (_flags & VehicleStateFlags.EngineOn) != 0;
            Entity.LightsOn = (_flags & VehicleStateFlags.LightsOn) != 0;
            Entity.HighBeamsOn = (_flags & VehicleStateFlags.HighBeams) != 0;
            Entity.SirenActive = (_flags & VehicleStateFlags.SirenOn) != 0;
            Entity.HandbrakeOn = (_flags & VehicleStateFlags.Handbrake) != 0;
            Natives.SetVehicleBrakeLights(Entity, (_flags & VehicleStateFlags.BrakeLights) != 0);

            if ((_flags & VehicleStateFlags.Horn) != 0)
                Natives.StartVehicleHorn(Entity, 120);

            if (!_colorsApplied)
            {
                Natives.SetVehicleColors(Entity, _primaryColor, _secondaryColor);
                _colorsApplied = true;
            }
        }

        private void SpawnVehicle()
        {
            CleanupEntity();

            var model = new Model(unchecked((int)ModelHash));
            if (!model.IsInCdImage || !model.IsVehicle)
                return;

            if (!model.IsLoaded)
            {
                model.Request();
                return; // tenta novamente no proximo tick sem travar o jogo
            }

            Entity = World.CreateVehicle(model, _targetPos);
            model.MarkAsNoLongerNeeded();

            if (Entity != null && Entity.Exists())
            {
                Natives.SetEntityQuaternion(Entity, _targetRot);
                Natives.SetVehicleColors(Entity, _primaryColor, _secondaryColor);
                _colorsApplied = true;
            }
        }

        public void Cleanup()
        {
            CleanupEntity();
        }

        private void CleanupEntity()
        {
            if (Entity != null)
            {
                try
                {
                    if (Entity.Exists())
                        Entity.Delete();
                }
                catch { }
                Entity = null;
            }
        }

        private static float Clamp01(float v)
        {
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }

        private static NetQuaternion Nlerp(NetQuaternion a, NetQuaternion b, float t)
        {
            float dot = a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;
            if (dot < 0f)
            {
                b.X = -b.X; b.Y = -b.Y; b.Z = -b.Z; b.W = -b.W;
            }

            float x = a.X + (b.X - a.X) * t;
            float y = a.Y + (b.Y - a.Y) * t;
            float z = a.Z + (b.Z - a.Z) * t;
            float w = a.W + (b.W - a.W) * t;

            float len = (float)Math.Sqrt(x * x + y * y + z * z + w * w);
            if (len < 0.0001f) return NetQuaternion.Identity;
            float inv = 1f / len;
            return new NetQuaternion(x * inv, y * inv, z * inv, w * inv);
        }
    }
}
