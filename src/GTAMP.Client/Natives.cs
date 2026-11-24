using System;
using GTA;
using GTA.Math;
using GTA.Native;
using GTAMP.Shared;

namespace GTAMP.Client
{
    /// <summary>
    /// Wrappers de Natives do GTA V verificadas na Build 1.0.463.1 / 1.0.505.2 (2015).
    /// Nenhuma native de DLC posterior a 2015 ou de SHVDN v3 e usada aqui.
    /// </summary>
    internal static class Natives
    {
        // ---- Conversoes entre NetVector3 e GTA.Math.Vector3 ----
        public static NetVector3 ToNet(Vector3 v)
        {
            return new NetVector3(v.X, v.Y, v.Z);
        }

        public static Vector3 ToGta(NetVector3 v)
        {
            return new Vector3(v.X, v.Y, v.Z);
        }

        // ---- Entidade / Quaternions / Movimento ----

        // GET_ENTITY_QUATERNION (2015 base)
        public static NetQuaternion GetEntityQuaternion(Entity entity)
        {
            var x = new OutputArgument();
            var y = new OutputArgument();
            var z = new OutputArgument();
            var w = new OutputArgument();
            Function.Call((Hash)0x7B3703D2D32DFA18UL, entity.Handle, x, y, z, w);
            return new NetQuaternion(x.GetResult<float>(), y.GetResult<float>(), z.GetResult<float>(), w.GetResult<float>());
        }

        // SET_ENTITY_QUATERNION (2015 base)
        public static void SetEntityQuaternion(Entity entity, NetQuaternion q)
        {
            Function.Call((Hash)0x77B21BE7AC540F07UL, entity.Handle, q.X, q.Y, q.Z, q.W);
        }

        // SET_ENTITY_COORDS_NO_OFFSET (2015 base)
        public static void SetEntityCoordsNoOffset(Entity entity, Vector3 pos)
        {
            Function.Call((Hash)0x239A3351AC1DA385UL, entity.Handle, pos.X, pos.Y, pos.Z, false, false, false);
        }

        // SET_ENTITY_VELOCITY (2015 base)
        public static void SetEntityVelocity(Entity entity, Vector3 vel)
        {
            Function.Call((Hash)0x1C99BB7B6E96D16FUL, entity.Handle, vel.X, vel.Y, vel.Z);
        }

        // CLEAR_ENTITY_LAST_DAMAGE_ENTITY (2015 base)
        public static void ClearEntityLastDamageEntity(Entity entity)
        {
            Function.Call((Hash)0xA72CD9CA74A5ECBAUL, entity.Handle);
        }

        // HAS_ENTITY_BEEN_DAMAGED_BY_ENTITY (2015 base)
        public static bool HasEntityBeenDamagedByEntity(Entity target, Entity attacker)
        {
            return Function.Call<bool>((Hash)0xC86D67D52A707CF8UL, target.Handle, attacker.Handle, true);
        }

        // ---- Ped: configuracao, tarefas, armas, estados ----

        // SET_BLOCKING_OF_NON_TEMPORARY_EVENTS (2015 base)
        public static void SetBlockingOfNonTemporaryEvents(Ped ped, bool toggle)
        {
            Function.Call((Hash)0x9F8AA94D6D97DBF4UL, ped.Handle, toggle);
        }

        // SET_PED_FLEE_ATTRIBUTES (2015 base)
        public static void SetPedFleeAttributes(Ped ped, int attr, bool value)
        {
            Function.Call((Hash)0x70A2D1137C8ED7C9UL, ped.Handle, attr, value);
        }

        // SET_PED_COMBAT_ATTRIBUTES (2015 base)
        public static void SetPedCombatAttributes(Ped ped, int attr, bool value)
        {
            Function.Call((Hash)0x9F7794730795E019UL, ped.Handle, attr, value);
        }

        // SET_PED_CAN_BE_TARGETTED (2015 base)
        public static void SetPedCanBeTargetted(Ped ped, bool toggle)
        {
            Function.Call((Hash)0x63F58F7C80513AADUL, ped.Handle, toggle);
        }

        // SET_PED_DROPS_WEAPONS_WHEN_DEAD (2015 base)
        public static void SetPedDropsWeaponsWhenDead(Ped ped, bool toggle)
        {
            Function.Call((Hash)0x476AE72C1D19D1A8UL, ped.Handle, toggle);
        }

        // TASK_GO_STRAIGHT_TO_COORD (2015 base)
        public static void TaskGoStraightToCoord(Ped ped, Vector3 pos, float speed, int timeoutMs, float targetHeading, float distanceToSlide)
        {
            Function.Call((Hash)0xD76B57B44F1E6F8BUL, ped.Handle, pos.X, pos.Y, pos.Z, speed, timeoutMs, targetHeading, distanceToSlide);
        }

        // TASK_AIM_GUN_AT_COORD (2015 base)
        public static void TaskAimGunAtCoord(Ped ped, Vector3 target, int durationMs)
        {
            Function.Call((Hash)0x6671F3EEC681BDA1UL, ped.Handle, target.X, target.Y, target.Z, durationMs, false, false);
        }

        // TASK_SHOOT_AT_COORD (2015 base)
        public static void TaskShootAtCoord(Ped ped, Vector3 target, int durationMs)
        {
            // FiringPattern FullAuto = 0xC6EE6B4C
            Function.Call((Hash)0x46A6CC01E0826106UL, ped.Handle, target.X, target.Y, target.Z, durationMs, unchecked((int)0xC6EE6B4CU));
        }

        // TASK_JUMP (2015 base)
        public static void TaskJump(Ped ped)
        {
            Function.Call((Hash)0x0AE4086104E067B1UL, ped.Handle, false);
        }

        // CLEAR_PED_TASKS (2015 base)
        public static void ClearPedTasks(Ped ped)
        {
            Function.Call((Hash)0xE1EF3C1216AFF2CDUL, ped.Handle);
        }

        // CLEAR_PED_TASKS_IMMEDIATELY (2015 base)
        public static void ClearPedTasksImmediately(Ped ped)
        {
            Function.Call((Hash)0xAAA34F8A7CB32098UL, ped.Handle);
        }

        // SET_PED_INTO_VEHICLE (2015 base)
        public static void SetPedIntoVehicle(Ped ped, Vehicle veh, int seatIndex)
        {
            Function.Call((Hash)0xF75B0D629E1C063DUL, ped.Handle, veh.Handle, seatIndex);
        }

        // TASK_LEAVE_VEHICLE (2015 base)
        public static void TaskLeaveVehicle(Ped ped, Vehicle veh, int flags)
        {
            Function.Call((Hash)0xD3DBCE61A490BE02UL, ped.Handle, veh.Handle, flags);
        }

        // GIVE_WEAPON_TO_PED (2015 base)
        public static void GiveWeaponToPed(Ped ped, uint weaponHash, int ammo, bool isHidden, bool equipNow)
        {
            Function.Call((Hash)0xBF0FD6E56C964FCBUL, ped.Handle, unchecked((int)weaponHash), ammo, isHidden, equipNow);
        }

        // SET_CURRENT_PED_WEAPON (2015 base)
        public static void SetCurrentPedWeapon(Ped ped, uint weaponHash, bool forceInHand)
        {
            Function.Call((Hash)0xADF692B254977C0CUL, ped.Handle, unchecked((int)weaponHash), forceInHand);
        }

        // SET_PED_INFINITE_AMMO_CLIP (2015 base)
        public static void SetPedInfiniteAmmoClip(Ped ped, bool toggle)
        {
            Function.Call((Hash)0x183DADC6AA953186UL, ped.Handle, toggle);
        }

        // GET_SELECTED_PED_WEAPON (2015 base)
        public static uint GetSelectedPedWeapon(Ped ped)
        {
            return unchecked((uint)Function.Call<int>((Hash)0x0A6DB4965674D243UL, ped.Handle));
        }

        // GET_PED_LAST_WEAPON_IMPACT_COORD (2015 base)
        public static bool GetPedLastWeaponImpactCoord(Ped ped, out Vector3 impact)
        {
            var arg = new OutputArgument();
            bool hit = Function.Call<bool>((Hash)0x6C4D0409BA1A2BC2UL, ped.Handle, arg);
            impact = hit ? arg.GetResult<Vector3>() : Vector3.Zero;
            return hit;
        }

        // SHOOT_SINGLE_BULLET_BETWEEN_COORDS (2015 base)
        public static void ShootSingleBulletBetweenCoords(Vector3 from, Vector3 to, int damage, uint weaponHash, Ped ownerPed, float speed)
        {
            Function.Call((Hash)0x867654CBC7606F2CUL,
                from.X, from.Y, from.Z,
                to.X, to.Y, to.Z,
                damage, true, unchecked((int)weaponHash),
                ownerPed != null ? ownerPed.Handle : 0,
                true, false, speed);
        }

        // Estados do Ped (2015 base)
        public static bool IsPedInCover(Ped ped) { return Function.Call<bool>((Hash)0x60DFD0691A170B88UL, ped.Handle, false); }
        public static bool IsPedReloading(Ped ped) { return Function.Call<bool>((Hash)0x24B100C68C645951UL, ped.Handle); }
        public static bool IsPedClimbing(Ped ped) { return Function.Call<bool>((Hash)0x53E8CB4F48BFE623UL, ped.Handle); }
        public static bool IsPedSwimming(Ped ped) { return Function.Call<bool>((Hash)0x9DE327631295B4C2UL, ped.Handle); }
        public static bool IsPedDucking(Ped ped) { return Function.Call<bool>((Hash)0xD125AE748725C6BCUL, ped.Handle); }

        // ---- Veiculos (2015 base) ----

        // GET_PED_IN_VEHICLE_SEAT (2015 base)
        public static int GetPedHandleInVehicleSeat(Vehicle veh, int seatIndex)
        {
            return Function.Call<int>((Hash)0xBB40DD2270B65366UL, veh.Handle, seatIndex);
        }

        // GET_VEHICLE_MAX_NUMBER_OF_PASSENGERS (2015 base)
        public static int GetVehicleMaxPassengers(Vehicle veh)
        {
            return Function.Call<int>((Hash)0xA7C4F2C6E744A550UL, veh.Handle);
        }

        public static sbyte GetPedSeatIndex(Ped ped, Vehicle veh)
        {
            if (ped == null || veh == null || !veh.Exists()) return Protocol.SeatNone;
            int maxPass = GetVehicleMaxPassengers(veh);
            for (int seat = -1; seat < maxPass; seat++)
            {
                if (GetPedHandleInVehicleSeat(veh, seat) == ped.Handle)
                    return (sbyte)seat;
            }
            return Protocol.SeatNone;
        }

        // GET_VEHICLE_COLOURS / SET_VEHICLE_COLOURS (2015 base)
        public static void GetVehicleColors(Vehicle veh, out byte primary, out byte secondary)
        {
            var p = new OutputArgument();
            var s = new OutputArgument();
            Function.Call((Hash)0xA19435F193E081ACUL, veh.Handle, p, s);
            primary = (byte)p.GetResult<int>();
            secondary = (byte)s.GetResult<int>();
        }

        public static void SetVehicleColors(Vehicle veh, byte primary, byte secondary)
        {
            Function.Call((Hash)0x4F1D4BE3A7F24601UL, veh.Handle, (int)primary, (int)secondary);
        }

        // IS_HORN_ACTIVE / START_VEHICLE_HORN (2015 base)
        public static bool IsHornActive(Vehicle veh)
        {
            return Function.Call<bool>((Hash)0x9D6BFC12B05C6121UL, veh.Handle);
        }

        public static void StartVehicleHorn(Vehicle veh, int durationMs)
        {
            Function.Call((Hash)0x9C8C6504B5B63D2CUL, veh.Handle, durationMs, 0, false);
        }

        // SET_VEHICLE_BRAKE_LIGHTS (2015 base)
        public static void SetVehicleBrakeLights(Vehicle veh, bool toggle)
        {
            Function.Call((Hash)0x92B35082E0B42F66UL, veh.Handle, toggle);
        }

        // ---- Camera / Mira (2015 base) ----

        public static Vector3 GetGameplayCamCoord()
        {
            return Function.Call<Vector3>((Hash)0x14D6F5678D8F1B37UL);
        }

        public static Vector3 GetGameplayCamRot()
        {
            return Function.Call<Vector3>((Hash)0x837765A25378F0BBUL, 2);
        }

        public static Vector3 GetAimWorldPosition(float distance)
        {
            Vector3 camPos = GetGameplayCamCoord();
            Vector3 camRot = GetGameplayCamRot();
            double rx = camRot.X * Math.PI / 180.0;
            double rz = camRot.Z * Math.PI / 180.0;
            double cosX = Math.Cos(rx);
            Vector3 dir = new Vector3(
                (float)(-Math.Sin(rz) * cosX),
                (float)(Math.Cos(rz) * cosX),
                (float)Math.Sin(rx));
            return camPos + dir * distance;
        }

        // ---- HUD / Desenho 2D e 3D -> 2D (2015 base) ----

        // _WORLD3D_TO_SCREEN2D (2015 base)
        public static bool World3dToScreen2d(Vector3 worldPos, out float screenX, out float screenY)
        {
            var x = new OutputArgument();
            var y = new OutputArgument();
            bool ok = Function.Call<bool>((Hash)0x34E82F05DF2974F5UL, worldPos.X, worldPos.Y, worldPos.Z, x, y);
            screenX = ok ? x.GetResult<float>() : 0f;
            screenY = ok ? y.GetResult<float>() : 0f;
            return ok;
        }

        public static void DrawRect(float x, float y, float w, float h, int r, int g, int b, int a)
        {
            Function.Call((Hash)0x3A618A217E5154F0UL, x, y, w, h, r, g, b, a);
        }

        public static void DrawText2D(string text, float x, float y, float scale, int r, int g, int b, int a, bool center, bool outline)
        {
            Function.Call((Hash)0x66E0276CC5F6B9DAUL, 0); // SET_TEXT_FONT (Chalet London)
            Function.Call((Hash)0x07C837F9A01C34C9UL, scale, scale); // SET_TEXT_SCALE
            Function.Call((Hash)0xBE6B23FFA53FB442UL, r, g, b, a); // SET_TEXT_COLOUR
            Function.Call((Hash)0xC02F4DBFB51D988BUL, center); // SET_TEXT_CENTRE
            if (outline)
                Function.Call((Hash)0x2513DFB0FB8400FEUL); // SET_TEXT_OUTLINE
            Function.Call((Hash)0x25FBB336DF1804CBUL, "STRING"); // BEGIN_TEXT_COMMAND_DISPLAY_TEXT
            Function.Call((Hash)0x6C188BE134E074AAUL, text ?? string.Empty); // ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME
            Function.Call((Hash)0xCD015E5BB0D96A57UL, x, y); // END_TEXT_COMMAND_DISPLAY_TEXT
        }

        // DISABLE_ALL_CONTROL_ACTIONS (2015 base) - impede andar/atirar enquanto digita no chat
        public static void DisableAllControlActions()
        {
            Function.Call((Hash)0x5F4B6931816E599BUL, 0);
        }

        // DISABLE_CONTROL_ACTION (2015 base) - impede abrir celular/pause/menu padrao ao navegar no menu GTAMP
        public static void DisableControlAction(int padIndex, int control)
        {
            Function.Call((Hash)0xFE99B66D079CF6BCUL, padIndex, control, true);
        }

        public static void DisableMenuConflictControls()
        {
            // 27 = Phone (Seta Cima), 172..177 = Phone/Frontend Up/Down/Left/Right/Select/Cancel
            // 199/200 = Pause/Esc, 244 = Interaction Menu (M), 19 = CharacterWheel
            int[] controls = { 19, 27, 172, 173, 174, 175, 176, 177, 199, 200, 244 };
            for (int i = 0; i < controls.Length; i++)
            {
                DisableControlAction(0, controls[i]);
                DisableControlAction(2, controls[i]);
            }
        }

        // ---- Mundo / RP (2015 base) ----

        // NETWORK_OVERRIDE_CLOCK_TIME (2015 base)
        public static void SetClockTime(int hour, int minute, int second)
        {
            Function.Call((Hash)0xE679E3E06E363892UL, hour, minute, second);
        }

        // SET_WEATHER_TYPE_NOW_PERSIST (2015 base)
        public static void SetWeatherNowPersist(string weather)
        {
            Function.Call((Hash)0xED712CA327900C8AUL, weather ?? "CLEAR");
        }

        // DRAW_MARKER (2015 base) - tipo 1 = cilindro no chao
        public static void DrawCylinderMarker(Vector3 pos, float radius, int r, int g, int b, int a)
        {
            Function.Call((Hash)0x28477EC23D892089UL,
                1, pos.X, pos.Y, pos.Z - 1.0f,
                0f, 0f, 0f,
                0f, 0f, 0f,
                radius * 1.5f, radius * 1.5f, 1.0f,
                r, g, b, a,
                false, false, 2, false, (string)null, (string)null, false);
        }

        // SET_PLAYER_MODEL (2015 base)
        public static void SetPlayerModel(Player player, uint modelHash)
        {
            Function.Call((Hash)0x00A1CADD00108836UL, player.Handle, unchecked((int)modelHash));
        }

        // SET_MISSION_FLAG (2015 base) - bloqueia autosave, quick-save do celular e missoes da historia durante o MP
        public static void SetMissionFlag(bool toggle)
        {
            Function.Call((Hash)0xC4301E5121A0ED73UL, toggle);
        }

        // REQUEST_IPL / REMOVE_IPL (2015 base) - abre interiores de RP em saves 100% concluidos
        public static void RequestIpl(string iplName)
        {
            Function.Call((Hash)0x41B4893843BBDB74UL, iplName);
        }

        public static void RemoveIpl(string iplName)
        {
            Function.Call((Hash)0xEE6C5AD3ECE0A82DUL, iplName);
        }

        // ---- Animacoes / Emotes (2015 base) ----

        // REQUEST_ANIM_DICT
        public static void RequestAnimDict(string dict)
        {
            Function.Call((Hash)0xD3BD40951412FEF6UL, dict);
        }

        // HAS_ANIM_DICT_LOADED
        public static bool HasAnimDictLoaded(string dict)
        {
            return Function.Call<bool>((Hash)0xD031A9162D01088CUL, dict);
        }

        // TASK_PLAY_ANIM
        public static void TaskPlayAnim(Ped ped, string dict, string name, float blendIn, float blendOut, int duration, int flags, float playbackRate)
        {
            Function.Call((Hash)0xEA47FE3719165B94UL, ped.Handle, dict, name, blendIn, blendOut, duration, flags, playbackRate, false, false, false);
        }
    }
}
