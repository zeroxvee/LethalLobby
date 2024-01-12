using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System;
using UnityEngine;
using Steamworks.Data;
using TMPro;
using Object = UnityEngine.Object;
using Steamworks;
using System.Linq;
using System.Collections;
using System.Reflection;
using BepInEx.Bootstrap;
using System.Collections.Generic;
using System.Text;


namespace LethalLobby

{
    [BepInPlugin(modGUID, modName, modVersion)]
    public class LethalLobby : BaseUnityPlugin
    {
        private const string modGUID = "com.zeroxvee.LethalLobby";
        private const string modName = "Lobby Master";
        private const string modVersion = "0.1.0";

        private readonly Harmony harmony = new Harmony(modGUID);
        public static LethalLobby Instance;
        public static ManualLogSource ModLogger;
        public static Dictionary<string, BepInPlugin> LocalMods;
        public static ArrayList EssentialMods;

        public static void LogLoadedPlugins()
        {
            foreach (var kvp in Chainloader.PluginInfos)
            {
                string guid = kvp.Value.Metadata.GUID;
                string version = kvp.Value.Metadata.Version.ToString();
                Debug.Log($"Loaded plugin: {guid}, Version: {version}");
                LocalMods.Add(guid, kvp.Value.Metadata);
            }
        }

        void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }

            ModLogger = BepInEx.Logging.Logger.CreateLogSource(modName);
            ModLogger.LogInfo($"{modName}v{modVersion} loaded!");
            Logger.LogInfo($"{modName}v{modVersion} loaded!");

            Harmony val = new Harmony(modGUID);
            try
            {
                harmony.PatchAll();
            }
            catch (Exception ex)
            {
                ModLogger.LogError("Failed to patch: " + ex);
            }
            LogLoadedPlugins();
        }
    }

    public class ModCategoryList
    {
        // The list of mods that are required to be installed on the client to join the lobby
        // If the host has any of these mods installed, the client must have them installed as well
        private static string[] EssentialMods = new string[]
        {
            "me.swipez.melonloader.morecompany",
            "com.zeroxvee.LethalLobby",
            "com.bepis.r2api"
        };

        // The mods that are not necessary to be installed, but will improve the gameplay
        private static string[] SuggestedMods = new string[]
        {
            "MoreEmotes",
            "x753.Mimics",
            "twig.latecompany"
        };
    }

    public static class SteamMatchmaking_OnLobbyCreatedPatch
    {
        [HarmonyPatch(typeof(GameNetworkManager), "SteamMatchmaking_OnLobbyCreated")]
        [HarmonyPrefix]
        public static void Prefix(Result result, Lobby lobby, Dictionary<int, BepInPlugin> mods, GameNetworkManager __instance)
        {
            // Add your additional mod data here before the lobby is set up
            if (result == Result.OK)
            {
                for (int i = 0; i < mods.Count; i++)
                {
                    if (mods.TryGetValue(i, out BepInPlugin modInfo))
                    {
                        lobby.SetData($"mod{i}", $"{modInfo.GUID}-{modInfo.Name}-{modInfo.Version}");
                    }
                }
            }
        }
    }

    public static class LobbyDataIsJoinablePatch
    {
        [HarmonyPatch(typeof(GameNetworkManager), "LobbyDataIsJoinable")]
        [HarmonyPrefix]
        public static bool Prefix(Lobby lobby, ref bool __result, GameNetworkManager __instance)
        {
            // Check host-client game version compatibility
            string lobbyGameVersion = lobby.GetData("vers");
            if (lobbyGameVersion != __instance.gameVersionNum.ToString())
            {
                Debug.Log($"Lobby join denied! Attempted to join vers.{lobbyGameVersion} lobby id: {lobby.Id}");
                Object.FindObjectOfType<MenuManager>().SetLoadingScreen(isLoading: false, RoomEnter.Error, $"The server host is playing on version {lobbyGameVersion} while you are on version {__instance.gameVersionNum}.");
                __result = false; // exix the current patch method
                return false; // skip the execution of the original method
            }

            StringBuilder mismatchMessageBuilder = new StringBuilder();
            mismatchMessageBuilder.AppendLine("Can't join lobby, mod version mismatch!\n");

            bool hasMismatches = false;
            int modIndex = 0;

            // Use a while loop to iterate over the host's mods
            while (true)
            {
                string modKey = $"mod{modIndex}";
                string[] modInfo = lobby.GetData(modKey).Split('-');
                string hostModGUID = modInfo[0];
                string hostModName = modInfo[1];
                string hostModVersion = modInfo[2];

                // Stop the loop if there are no more mods
                if (string.IsNullOrEmpty(hostModVersion))
                    break;

                // Check if the client has the mod and if the version matches
                if (LethalLobby.LocalMods.TryGetValue(hostModGUID, out BepInPlugin clientModVersion) && clientModVersion.Version.ToString() != hostModVersion)
                {
                    // Append mismatch info to the StringBuilder
                    mismatchMessageBuilder.AppendLine($"{hostModName}: host - {hostModVersion}, you - {clientModVersion}\n");
                    hasMismatches = true;
                }
                else if (!LethalLobby.LocalMods.ContainsKey(modKey))
                {
                    // The client is missing a mod that the host has
                    mismatchMessageBuilder.AppendLine($"{hostModName}: host - {hostModVersion}, you - not installed\n");
                    hasMismatches = true;
                }

                modIndex++; // Increment to check the next mod
            }

            // If there were any mismatches, set the loading screen with the accumulated message
            if (hasMismatches)
            {
                string mismatchMessage = mismatchMessageBuilder.ToString();
                LethalLobby.ModLogger.LogError(mismatchMessage);

                // Set the loading screen with the combined mismatch message
                Object.FindObjectOfType<MenuManager>().SetLoadingScreen(isLoading: false, RoomEnter.Error, mismatchMessage);
                __result = false;
                return false; // Skip the execution of the original method
            }

            // letting the original method to run from here
            return true;
        }
    }

    [HarmonyPatch(typeof(SteamLobbyManager), "LoadServerList")]
    public static class LoadServerListPatch
    {
        public static bool Prefix(SteamLobbyManager __instance)
        {
            OverrideMethod(__instance);
            return false;
        }

        private static async void OverrideMethod(SteamLobbyManager __instance)
        {
            if (GameNetworkManager.Instance.waitingForLobbyDataRefresh)
            {
                return;
            }
            ReflectionUtils.SetFieldValue(__instance, "refreshServerListTimer", 0f);
            __instance.serverListBlankText.text = "Loading server list...";
            ReflectionUtils.GetFieldValue<Lobby[]>(__instance, "currentLobbyList");
            LobbySlot[] array = Object.FindObjectsOfType<LobbySlot>();
            for (int i = 0; i < array.Length; i++)
            {
                Object.Destroy((Object)(object)((Component)array[i]).gameObject);
            }
            LobbyQuery val;
            switch (__instance.sortByDistanceSetting)
            {
                case 0:
                    val = SteamMatchmaking.LobbyList;
                    val.FilterDistanceClose();
                    break;
                case 1:
                    val = SteamMatchmaking.LobbyList;
                    val.FilterDistanceFar();
                    break;
                case 2:
                    val = SteamMatchmaking.LobbyList;
                    val.FilterDistanceWorldwide();
                    break;
            }
            Debug.Log("Requested server list");
            GameNetworkManager.Instance.waitingForLobbyDataRefresh = true;
            Lobby[] currentLobbyList;
            switch (__instance.sortByDistanceSetting)
            {
                case 0:
                    val = SteamMatchmaking.LobbyList;
                    val = val.FilterDistanceClose();
                    val = val.WithSlotsAvailable(1);
                    val = val.WithKeyValue("vers", GameNetworkManager.Instance.gameVersionNum.ToString());
                    currentLobbyList = await val.RequestAsync();
                    break;
                case 1:
                    val = SteamMatchmaking.LobbyList;
                    val = val.FilterDistanceFar();
                    val = val.WithSlotsAvailable(1);
                    val = val.WithKeyValue("vers", GameNetworkManager.Instance.gameVersionNum.ToString());
                    currentLobbyList = await val.RequestAsync();
                    break;
                default:
                    val = SteamMatchmaking.LobbyList;
                    val = val.FilterDistanceWorldwide();
                    val = val.WithSlotsAvailable(1);
                    val = val.WithKeyValue("vers", GameNetworkManager.Instance.gameVersionNum.ToString());
                    currentLobbyList = await val.RequestAsync();
                    break;
            }
            GameNetworkManager.Instance.waitingForLobbyDataRefresh = false;
            if (currentLobbyList != null)
            {
                Debug.Log("Got lobby list!");
                ReflectionUtils.InvokeMethod(__instance, "DebugLogServerList", null);
                if (currentLobbyList.Length == 0)
                {
                    __instance.serverListBlankText.text = "No available servers to join.";
                }
                else
                {
                    __instance.serverListBlankText.text = "";
                }
                ReflectionUtils.SetFieldValue(__instance, "lobbySlotPositionOffset", 0f);
                Debug.Log(currentLobbyList.Length);
                for (int j = 0; j < currentLobbyList.Length; j++)
                {
                    Debug.Log($"Lobby {j}: {currentLobbyList[j].GetData("name")}-{currentLobbyList[j].GetData("vers")}");
                    Friend[] array2 = SteamFriends.GetBlocked().ToArray();
                    if (array2 != null)
                    {
                        for (int k = 0; k < array2.Length; k++)
                        {
                            Debug.Log($"blocked user: {array2[k].Name}; id: {array2[k].Id}");
                            if (currentLobbyList[j].IsOwnedBy(array2[k].Id))
                            {
                                Debug.Log(("Hiding lobby by blocked user: " + array2[k].Name));
                            }
                        }
                    }
                    else
                    {
                        Debug.Log("Blocked users list is null");
                    }
                    GameObject gameObject = Object.Instantiate<GameObject>(__instance.LobbySlotPrefab, __instance.levelListContainer);
                    gameObject.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, 0f + ReflectionUtils.GetFieldValue<float>(__instance, "lobbySlotPositionOffset"));
                    ReflectionUtils.SetFieldValue(__instance, "lobbySlotPositionOffset", ReflectionUtils.GetFieldValue<float>(__instance, "lobbySlotPositionOffset") - 42f);
                    LobbySlot componentInChildren = gameObject.GetComponentInChildren<LobbySlot>();
                    componentInChildren.LobbyName.text = currentLobbyList[j].GetData("name");
                    componentInChildren.playerCount.text = $"{currentLobbyList[j].MemberCount} / {currentLobbyList[j].MaxMembers}";
                    componentInChildren.lobbyId = currentLobbyList[j].Id;
                    componentInChildren.thisLobby = currentLobbyList[j];
                    ReflectionUtils.SetFieldValue(__instance, "currentLobbyList", currentLobbyList);
                }
            }
            else
            {
                Debug.Log("Lobby list is null after request.");
                __instance.serverListBlankText.text = "No available servers to join.";
            }
        }
    }


    public class ReflectionUtils
    {
        public static void InvokeMethod(object obj, string methodName, object[] parameters)
        {
            Type type = obj.GetType();
            MethodInfo method = type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            method.Invoke(obj, parameters);
        }

        public static void InvokeMethod(object obj, Type forceType, string methodName, object[] parameters)
        {
            MethodInfo method = forceType.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            method.Invoke(obj, parameters);
        }

        public static void SetPropertyValue(object obj, string propertyName, object value)
        {
            Type type = obj.GetType();
            PropertyInfo property = type.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            property.SetValue(obj, value);
        }

        public static T InvokeMethod<T>(object obj, string methodName, object[] parameters)
        {
            Type type = obj.GetType();
            MethodInfo method = type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return (T)method.Invoke(obj, parameters);
        }

        public static T GetFieldValue<T>(object obj, string fieldName)
        {
            Type type = obj.GetType();
            FieldInfo field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return (T)field.GetValue(obj);
        }

        public static void SetFieldValue(object obj, string fieldName, object value)
        {
            Type type = obj.GetType();
            FieldInfo field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            field.SetValue(obj, value);
        }
    }
}
