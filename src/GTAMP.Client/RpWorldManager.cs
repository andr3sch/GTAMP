using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTAMP.Shared;
using GTAMP.Shared.Packets;

namespace GTAMP.Client
{
    /// <summary>
    /// Gerencia elementos de Roleplay no cliente:
    /// - Sincronizacao de hora e clima
    /// - Markers 3D e Blips de empregos/garagens/lojas + interacao [E]
    /// - HUD de RP (emprego, dinheiro em maos, banco, indicador de microfone)
    /// - Execucao de ServerActionPacket (TP, skin, arma, veiculo, cura, notificacao)
    /// </summary>
    internal sealed class RpWorldManager
    {
        private sealed class ActiveMarker
        {
            public RpMarkerPacket Data;
            public Blip Blip;
        }

        private readonly Dictionary<int, ActiveMarker> _markers = new Dictionary<int, ActiveMarker>();
        private readonly List<Vehicle> _spawnedVehicles = new List<Vehicle>();
        private bool _hasHud;
        private long _cash;
        private long _bank;
        private string _jobName = "";

        public int ActiveMarkerId { get; private set; }

        public void ApplyWorldSync(WorldSyncPacket p)
        {
            Natives.SetClockTime(p.Hour, p.Minute, 0);
            if (!string.IsNullOrEmpty(p.Weather))
                Natives.SetWeatherNowPersist(p.Weather);
        }

        /// <summary>
        /// Carrega interiores abertos de RP/MP (concessionaria do Simeon, Hospital Pillbox, etc.)
        /// para que o GTAMP funcione perfeitamente em saves 100% concluidos do Modo Historia.
        /// </summary>
        public void ApplyMultiplayerWorldIpls()
        {
            try
            {
                // Concessionaria Premium Deluxe Motorsport (Simeon) - aberta mesmo apos zerar a historia
                Natives.RemoveIpl("fakeint");
                Natives.RequestIpl("shr_int");

                // Hospital Pillbox Hill aberto
                Natives.RemoveIpl("RC12B_Default");
                Natives.RemoveIpl("RC12B_Fixed");
                Natives.RemoveIpl("RC12B_Destroyed");
                Natives.RequestIpl("RC12B_HospitalInterior");

                // Joalheria Vangelico aberta
                Natives.RemoveIpl("jewel2fake");
                Natives.RemoveIpl("bh1_16_refurb");
                Natives.RequestIpl("post_hiest_unload");
            }
            catch { }
        }

        public void ApplyHud(RpHudPacket p)
        {
            _cash = p.Cash;
            _bank = p.Bank;
            _jobName = p.JobName ?? "";
            _hasHud = true;
        }

        public void ApplyMarker(RpMarkerPacket p)
        {
            ActiveMarker existing;
            if (_markers.TryGetValue(p.MarkerId, out existing))
            {
                if (existing.Blip != null && existing.Blip.Exists())
                    existing.Blip.Remove();
                _markers.Remove(p.MarkerId);
            }

            if (p.Remove)
                return;

            Vector3 pos = Natives.ToGta(p.Position);
            Blip blip = World.CreateBlip(pos);
            if (blip != null && blip.Exists())
            {
                blip.Sprite = (BlipSprite)p.BlipSprite;
                blip.Color = (BlipColor)p.BlipColor;
                blip.Name = p.Label;
                blip.IsShortRange = true;
            }

            _markers[p.MarkerId] = new ActiveMarker { Data = p, Blip = blip };
        }

        public void ExecuteServerAction(ServerActionPacket p)
        {
            Ped ped = Game.Player.Character;
            if (ped == null || !ped.Exists())
                return;

            switch (p.Action)
            {
                case ServerActionType.Teleport:
                    {
                        Vector3 dest = Natives.ToGta(p.Position);
                        if (ped.IsInVehicle() && ped.CurrentVehicle != null && ped.CurrentVehicle.Exists())
                            ped.CurrentVehicle.Position = dest;
                        else
                            ped.Position = dest;
                        if (p.FloatParam != 0f)
                            ped.Heading = p.FloatParam;
                        break;
                    }

                case ServerActionType.SetModel:
                    {
                        var model = new Model(unchecked((int)p.UintParam));
                        if (!model.IsInCdImage || !model.IsPed)
                        {
                            UI.Notify("~r~Modelo de ped invalido:~w~ " + p.TextParam);
                            return;
                        }
                        if (model.Request(1500))
                        {
                            Natives.SetPlayerModel(Game.Player, p.UintParam);
                            model.MarkAsNoLongerNeeded();
                        }
                        break;
                    }

                case ServerActionType.GiveWeapon:
                    {
                        int ammo = p.IntParam > 0 ? p.IntParam : 250;
                        Natives.GiveWeaponToPed(ped, p.UintParam, ammo, false, true);
                        break;
                    }

                case ServerActionType.SpawnVehicle:
                    {
                        var model = new Model(unchecked((int)p.UintParam));
                        if (!model.IsInCdImage || !model.IsVehicle)
                        {
                            UI.Notify("~r~Modelo de veiculo invalido:~w~ " + p.TextParam);
                            return;
                        }
                        if (model.Request(2000))
                        {
                            Vector3 spawnPos = (p.Position.X != 0f || p.Position.Y != 0f)
                                ? Natives.ToGta(p.Position)
                                : ped.Position + ped.ForwardVector * 4.5f;
                            float heading = p.FloatParam != 0f ? p.FloatParam : ped.Heading;
                            Vehicle veh = World.CreateVehicle(model, spawnPos, heading);
                            model.MarkAsNoLongerNeeded();
                            if (veh != null && veh.Exists())
                            {
                                _spawnedVehicles.Add(veh);
                                Natives.SetPedIntoVehicle(ped, veh, -1);
                            }
                        }
                        break;
                    }

                case ServerActionType.Notify:
                    if (!string.IsNullOrEmpty(p.TextParam))
                        UI.Notify(p.TextParam);
                    break;

                case ServerActionType.Heal:
                    ped.Health = p.IntParam > 0 ? p.IntParam : 200;
                    break;
            }
        }

        public void UpdateAndDraw(Vector3 localPos, bool isTalking, bool isConnected, VoiceRangeMode voiceMode, int radioChannel)
        {
            ActiveMarkerId = 0;

            foreach (var kv in _markers)
            {
                RpMarkerPacket m = kv.Value.Data;
                Vector3 pos = Natives.ToGta(m.Position);
                float dist = localPos.DistanceTo(pos);

                if (dist < 60f)
                {
                    Natives.DrawCylinderMarker(pos, m.Radius, m.R, m.G, m.B, 150);
                }

                if (dist <= m.Radius)
                {
                    ActiveMarkerId = m.MarkerId;
                    Natives.DrawRect(0.5f, 0.86f, 0.34f, 0.042f, 0, 0, 0, 160);
                    Natives.DrawText2D(m.Label, 0.5f, 0.847f, 0.36f, 255, 255, 255, 245, true, true);
                }
            }

            if (isConnected && _hasHud)
            {
                // Painel discreto no canto superior direito
                Natives.DrawRect(0.905f, 0.068f, 0.17f, 0.095f, 0, 0, 0, 130);
                Natives.DrawText2D("Emprego: " + _jobName, 0.828f, 0.028f, 0.30f, 140, 215, 255, 240, false, true);
                Natives.DrawText2D("Maos: $" + _cash, 0.828f, 0.053f, 0.30f, 120, 240, 140, 240, false, true);
                Natives.DrawText2D("Banco: $" + _bank, 0.828f, 0.078f, 0.30f, 255, 225, 110, 240, false, true);
            }

            if (isConnected)
            {
                string modeLabel = voiceMode == VoiceRangeMode.Whisper ? "Sussurrar" :
                                   voiceMode == VoiceRangeMode.Shout ? "Gritar" :
                                   voiceMode == VoiceRangeMode.Radio ? ("Radio " + radioChannel + " MHz") : "Normal";

                int bgR = isTalking ? (voiceMode == VoiceRangeMode.Radio ? 25 : 20) : 15;
                int bgG = isTalking ? (voiceMode == VoiceRangeMode.Radio ? 110 : 145) : 18;
                int bgB = isTalking ? (voiceMode == VoiceRangeMode.Radio ? 200 : 65) : 26;
                int bgA = isTalking ? 210 : 140;

                Natives.DrawRect(0.905f, 0.134f, 0.17f, 0.030f, bgR, bgG, bgB, bgA);
                string txt = (isTalking ? "FALANDO: " : "Voz: ") + modeLabel;
                Natives.DrawText2D(txt, 0.905f, 0.123f, 0.26f, 255, 255, 255, 245, true, true);
            }
        }

        public void Cleanup()
        {
            foreach (var kv in _markers)
            {
                try
                {
                    if (kv.Value.Blip != null && kv.Value.Blip.Exists())
                        kv.Value.Blip.Remove();
                }
                catch { }
            }
            _markers.Clear();
            for (int i = 0; i < _spawnedVehicles.Count; i++)
            {
                try
                {
                    Vehicle v = _spawnedVehicles[i];
                    if (v != null && v.Exists())
                        v.Delete();
                }
                catch { }
            }
            _spawnedVehicles.Clear();
            _hasHud = false;
            ActiveMarkerId = 0;
        }
    }
}
