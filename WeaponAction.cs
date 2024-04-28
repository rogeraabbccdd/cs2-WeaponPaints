﻿using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;

namespace WeaponPaints
{
	public partial class WeaponPaints
	{
		private static void GiveKnifeToPlayer(CCSPlayerController? player)
		{
			if (!_config.Additional.KnifeEnabled || player == null || !player.IsValid) return;

			if (PlayerHasKnife(player)) return;

			//string knifeToGive = (CsTeam)player.TeamNum == CsTeam.Terrorist ? "weapon_knife_t" : "weapon_knife";
			player.GiveNamedItem(CsItem.Knife);
		}

		private static bool PlayerHasKnife(CCSPlayerController? player)
		{
			if (!_config.Additional.KnifeEnabled) return false;

			if (player == null || !player.IsValid || !player.PlayerPawn.IsValid)
			{
				return false;
			}

			if (player.PlayerPawn.Value == null || player.PlayerPawn.Value.WeaponServices == null || player.PlayerPawn.Value.ItemServices == null)
				return false;

			var weapons = player.PlayerPawn.Value.WeaponServices?.MyWeapons;
			if (weapons == null) return false;
			foreach (var weapon in weapons)
			{
				if (!weapon.IsValid || weapon.Value == null || !weapon.Value.IsValid) continue;
				if (weapon.Value.DesignerName.Contains("knife") || weapon.Value.DesignerName.Contains("bayonet"))
				{
					return true;
				}
			}
			return false;
		}

		private void RefreshWeapons(CCSPlayerController? player)
		{
			if (!g_bCommandsAllowed) return;
			if (player == null || !player.IsValid || player.PlayerPawn?.Value == null || (LifeState_t)player.LifeState != LifeState_t.LIFE_ALIVE)
				return;
			if (player.PlayerPawn.Value.WeaponServices == null || player.PlayerPawn.Value.ItemServices == null)
				return;

			var weapons = player.PlayerPawn.Value.WeaponServices.MyWeapons;

			if (weapons.Count == 0)
				return;
			if (player.Team is CsTeam.None or CsTeam.Spectator)
				return;

			int playerTeam = player.TeamNum;

			Dictionary<string, List<(int, int)>> weaponsWithAmmo = [];

			foreach (var weapon in weapons)
			{
				if (!weapon.IsValid || weapon.Value == null ||
					!weapon.Value.IsValid || !weapon.Value.DesignerName.Contains("weapon_"))
					continue;

				CCSWeaponBaseGun gun = weapon.Value.As<CCSWeaponBaseGun>();

				if (weapon.Value.Entity == null) continue;
				if (!weapon.Value.OwnerEntity.IsValid) continue;
				if (gun.Entity == null) continue;
				if (!gun.IsValid) continue;
				if (!gun.VisibleinPVS) continue;

				try
				{
					CCSWeaponBaseVData? weaponData = weapon.Value.As<CCSWeaponBase>().VData;

					if (weaponData == null) continue;

					if (weaponData.GearSlot == gear_slot_t.GEAR_SLOT_RIFLE || weaponData.GearSlot == gear_slot_t.GEAR_SLOT_PISTOL)
					{
						if (!WeaponDefindex.TryGetValue(weapon.Value.AttributeManager.Item.ItemDefinitionIndex, out var weaponByDefindex))
							continue;

						int clip1 = weapon.Value.Clip1;
						int reservedAmmo = weapon.Value.ReserveAmmo[0];

						if (!weaponsWithAmmo.TryGetValue(weaponByDefindex, out var value))
						{
							value = [];
							weaponsWithAmmo.Add(weaponByDefindex, value);
						}

						value.Add((clip1, reservedAmmo));

						if (gun.VData == null) return;

						weapon.Value.Remove();
					}
				}
				catch (Exception ex)
				{
					Logger.LogWarning(ex.Message);
					continue;
				}
			}

			try
			{
				player.ExecuteClientCommand("slot 3");
				player.ExecuteClientCommand("slot 3");

				var weapon = player.PlayerPawn.Value.WeaponServices.ActiveWeapon;
				if (!weapon.IsValid || weapon.Value == null) return;
				CCSWeaponBaseVData? weaponData = weapon.Value.As<CCSWeaponBase>().VData;

				if (weapon.Value.DesignerName.Contains("knife") || weaponData?.GearSlot == gear_slot_t.GEAR_SLOT_KNIFE)
				{
					CCSWeaponBaseGun gun;

					AddTimer(0.3f, () =>
					{
						if (player.TeamNum != playerTeam) return;

						player.ExecuteClientCommand("slot 3");
						gun = weapon.Value.As<CCSWeaponBaseGun>();
						player.DropActiveWeapon();

						AddTimer(0.7f, () =>
						{
							if (player.TeamNum != playerTeam) return;

							if (!gun.IsValid || gun.State != CSWeaponState_t.WEAPON_NOT_CARRIED) return;

							gun.Remove();
						});

						GiveKnifeToPlayer(player);
					});
				}
			}
			catch (Exception ex)
			{
				Logger.LogWarning($"Cannot remove knife: {ex.Message}");
			}

			AddTimer(0.6f, () =>
					{
						if (!g_bCommandsAllowed) return;

						foreach (var entry in weaponsWithAmmo)
						{
							foreach (var ammo in entry.Value)
							{
								var newWeapon = new CBasePlayerWeapon(player.GiveNamedItem(entry.Key));
								Server.NextFrame(() =>
						{
							try
							{
								newWeapon.Clip1 = ammo.Item1;
								newWeapon.ReserveAmmo[0] = ammo.Item2;
							}
							catch (Exception ex)
							{
								Logger.LogWarning("Error setting weapon properties: " + ex.Message);
							}
						});
							}
						}
					}, TimerFlags.STOP_ON_MAPCHANGE);
		}

		private static void RefreshGloves(CCSPlayerController player)
		{
			if (!Utility.IsPlayerValid(player) || (LifeState_t)player.LifeState != LifeState_t.LIFE_ALIVE) return;

			CCSPlayerPawn? pawn = player.PlayerPawn.Value;
			if (pawn == null || !pawn.IsValid || pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE)
				return;

			var model = pawn.CBodyComponent?.SceneNode?.GetSkeletonInstance()?.ModelState.ModelName ?? string.Empty;
			if (!string.IsNullOrEmpty(model))
			{
				pawn.SetModel("characters/models/tm_jumpsuit/tm_jumpsuit_varianta.vmdl");
				pawn.SetModel(model);
			}

			Instance.AddTimer(0.06f, () =>
			{
				CEconItemView item = pawn.EconGloves;
				try
				{
					if (!player.IsValid)
						return;

					if (!player.PawnIsAlive)
						return;

					if (!g_playersGlove.TryGetValue(player.Slot, out var gloveInfo) || gloveInfo == 0) return;

					WeaponInfo weaponInfo = gPlayerWeaponsInfo[player.Slot][gloveInfo];

					item.ItemDefinitionIndex = gloveInfo;
					item.ItemIDLow = 16384 & 0xFFFFFFFF;
					item.ItemIDHigh = 16384;

					CAttributeListSetOrAddAttributeValueByName.Invoke(item.NetworkedDynamicAttributes.Handle, "set item texture prefab", weaponInfo.Paint);
					CAttributeListSetOrAddAttributeValueByName.Invoke(item.NetworkedDynamicAttributes.Handle, "set item texture seed", weaponInfo.Seed);
					CAttributeListSetOrAddAttributeValueByName.Invoke(item.NetworkedDynamicAttributes.Handle, "set item texture wear", weaponInfo.Wear);

					item.Initialized = true;

					CBaseModelEntitySetBodygroup.Invoke(pawn, "default_gloves", 1);
				}
				catch (Exception) { }
			}, TimerFlags.STOP_ON_MAPCHANGE);
		}

		private static int GetRandomPaint(int defindex)
		{
			if (skinsList.Count == 0)
				return 0;

			Random rnd = new Random();

			// Filter weapons by the provided defindex
			var filteredWeapons = skinsList.Where(w => w["weapon_defindex"]?.ToString() == defindex.ToString()).ToList();

			if (filteredWeapons.Count == 0)
				return 0;

			var randomWeapon = filteredWeapons[rnd.Next(filteredWeapons.Count)];

			return int.TryParse(randomWeapon["paint"]?.ToString(), out var paintValue) ? paintValue : 0;
		}

		private static void SubclassChange(CBasePlayerWeapon weapon, ushort itemD)
		{
			var subclassChangeFunc = VirtualFunction.Create<nint, string, int>(
				GameData.GetSignature("ChangeSubclass")
			);

			subclassChangeFunc(weapon.Handle, itemD.ToString());
		}

		private static void UpdateWeaponMeshGroupMask(CBaseEntity weapon, bool isLegacy = false)
		{
			if (weapon.CBodyComponent?.SceneNode == null) return;
			var skeleton = weapon.CBodyComponent.SceneNode.GetSkeletonInstance();
			var value = (ulong)(isLegacy ? 2 : 1);

			if (skeleton.ModelState.MeshGroupMask != value)
			{
				skeleton.ModelState.MeshGroupMask = value;
			}
		}

		private static void UpdatePlayerWeaponMeshGroupMask(CCSPlayerController player, CBasePlayerWeapon weapon, bool isLegacy)
		{
			UpdateWeaponMeshGroupMask(weapon, isLegacy);

			var viewModel = GetPlayerViewModel(player);
			if (viewModel == null || viewModel.Weapon.Value == null ||
			    viewModel.Weapon.Value.Index != weapon.Index) return;
			UpdateWeaponMeshGroupMask(viewModel, isLegacy);
			Utilities.SetStateChanged(viewModel, "CBaseEntity", "m_CBodyComponent");
		}

		private static void GivePlayerAgent(CCSPlayerController player)
		{
			if (!g_playersAgent.TryGetValue(player.Slot, out var value)) return;

			var model = player.TeamNum == 3 ? value.CT : value.T;
			if (string.IsNullOrEmpty(model)) return;

			if (player.PlayerPawn.Value == null)
				return;

			try
			{
				Server.NextFrame(() =>
				{
					player.PlayerPawn.Value.SetModel(
						$"characters/models/{model}.vmdl"
					);
				});
			}
			catch (Exception)
			{
			}
		}

		private static void GivePlayerMusicKit(CCSPlayerController player)
		{
			if (!g_playersMusic.TryGetValue(player.Slot, out var value)) return;
			if (player.InventoryServices == null) return;

			Console.WriteLine(value);

			player.InventoryServices.MusicID = value;
		}

		private static CCSPlayerController? GetPlayerFromItemServices(CCSPlayer_ItemServices itemServices)
		{
			var pawn = itemServices.Pawn.Value;
			if (!pawn.IsValid || !pawn.Controller.IsValid || pawn.Controller.Value == null) return null;
			var player = new CCSPlayerController(pawn.Controller.Value.Handle);
			return !Utility.IsPlayerValid(player) ? null : player;
		}

		private static unsafe CBaseViewModel? GetPlayerViewModel(CCSPlayerController player)
		{
			if (player.PlayerPawn.Value == null || player.PlayerPawn.Value.ViewModelServices == null) return null;
			CCSPlayer_ViewModelServices viewModelServices = new(player.PlayerPawn.Value.ViewModelServices!.Handle);
			var ptr = viewModelServices.Handle + Schema.GetSchemaOffset("CCSPlayer_ViewModelServices", "m_hViewModel");
			var references = MemoryMarshal.CreateSpan(ref ptr, 3);
			var viewModel = (CHandle<CBaseViewModel>)Activator.CreateInstance(typeof(CHandle<CBaseViewModel>), references[0])!;
			return viewModel.Value == null ? null : viewModel.Value;
		}

		public static unsafe T[] GetFixedArray<T>(nint pointer, string @class, string member, int length) where T : CHandle<CBaseViewModel>
		{
			var ptr = pointer + Schema.GetSchemaOffset(@class, member);
			var references = MemoryMarshal.CreateSpan(ref ptr, length);
			var values = new T[length];

			for (var i = 0; i < length; i++)
			{
				values[i] = (T)Activator.CreateInstance(typeof(T), references[i])!;
			}

			return values;
		}
	}
}