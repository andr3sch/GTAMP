using System;
using LiteNetLib.Utils;

namespace GTAMP.Shared
{
    /// <summary>
    /// Vetor 3D leve e independente do GTA (o Servidor nao referencia ScriptHookVDotNet).
    /// No Mod Cliente converteremos para/de GTA.Math.Vector3.
    /// </summary>
    public struct NetVector3
    {
        public float X;
        public float Y;
        public float Z;

        public NetVector3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static readonly NetVector3 Zero = new NetVector3(0f, 0f, 0f);

        /// <summary>True se nenhum componente for NaN/Infinito.</summary>
        public bool IsFinite()
        {
            return !(float.IsNaN(X) || float.IsInfinity(X) ||
                     float.IsNaN(Y) || float.IsInfinity(Y) ||
                     float.IsNaN(Z) || float.IsInfinity(Z));
        }

        public float LengthSquared()
        {
            return X * X + Y * Y + Z * Z;
        }

        public static float DistanceSquared(NetVector3 a, NetVector3 b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
            return dx * dx + dy * dy + dz * dz;
        }

        public override string ToString()
        {
            return string.Format("({0:F2}, {1:F2}, {2:F2})", X, Y, Z);
        }
    }

    /// <summary>Quaternion leve (rotacao de veiculos).</summary>
    public struct NetQuaternion
    {
        public float X;
        public float Y;
        public float Z;
        public float W;

        public NetQuaternion(float x, float y, float z, float w)
        {
            X = x; Y = y; Z = z; W = w;
        }

        public static readonly NetQuaternion Identity = new NetQuaternion(0f, 0f, 0f, 1f);

        public bool IsFinite()
        {
            return !(float.IsNaN(X) || float.IsInfinity(X) || float.IsNaN(Y) || float.IsInfinity(Y) ||
                     float.IsNaN(Z) || float.IsInfinity(Z) || float.IsNaN(W) || float.IsInfinity(W));
        }
    }

    /// <summary>Extensoes de serializacao para NetDataWriter/NetDataReader.</summary>
    public static class NetSerializationExtensions
    {
        public static void PutQuaternion(this NetDataWriter w, NetQuaternion q)
        {
            w.Put(q.X);
            w.Put(q.Y);
            w.Put(q.Z);
            w.Put(q.W);
        }

        public static NetQuaternion GetQuaternion(this NetDataReader r)
        {
            float x = r.GetFloat();
            float y = r.GetFloat();
            float z = r.GetFloat();
            float w = r.GetFloat();
            return new NetQuaternion(x, y, z, w);
        }
        public static void PutVector3(this NetDataWriter w, NetVector3 v)
        {
            w.Put(v.X);
            w.Put(v.Y);
            w.Put(v.Z);
        }

        public static NetVector3 GetVector3(this NetDataReader r)
        {
            float x = r.GetFloat();
            float y = r.GetFloat();
            float z = r.GetFloat();
            return new NetVector3(x, y, z);
        }

        /// <summary>Garante que ha bytes suficientes para leitura (evita excecoes com pacotes truncados).</summary>
        public static void Require(this NetDataReader r, int bytes)
        {
            if (r.AvailableBytes < bytes)
                throw new FormatException("Pacote truncado: esperados " + bytes + " bytes, disponiveis " + r.AvailableBytes);
        }
    }
}
