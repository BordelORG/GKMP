using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Steamworks;

namespace CoopQA
{
    /// <summary>
    /// Replaces the Steamworks.NET calls used by the mod and the game with a fake backend (broker.py).
    /// Every patch is a Harmony prefix that skips the original; a missing target fails the run loudly.
    /// Callbacks are captured at Register and raised from Pump() on the main thread.
    /// </summary>
    internal static class SteamMock
    {
        // ---- identity ----
        public static ulong MyId;
        public static string MyName;
        public static readonly Dictionary<ulong, string> Names = new Dictionary<ulong, string>();
        public static readonly Dictionary<ulong, Dictionary<string, string>> RichPresence = new Dictionary<ulong, Dictionary<string, string>>();
        public static readonly Dictionary<string, string> MyRichPresence = new Dictionary<string, string>();

        // ---- lobbies (mirror of broker state) ----
        internal sealed class Lobby
        {
            public ulong Owner;
            public List<ulong> Members = new List<ulong>();
            public Dictionary<string, string> Data = new Dictionary<string, string>();
            public Dictionary<ulong, Dictionary<string, string>> MemberData = new Dictionary<ulong, Dictionary<string, string>>();
            public int Limit;
        }
        public static readonly Dictionary<ulong, Lobby> Lobbies = new Dictionary<ulong, Lobby>();
        private static List<ulong> lastLobbyList = new List<ulong>();

        // ---- P2P ----
        private const int MaxChannels = 16;
        private static readonly Queue<KeyValuePair<ulong, byte[]>>[] inbox =
            Enumerable.Range(0, MaxChannels).Select(_ => new Queue<KeyValuePair<ulong, byte[]>>()).ToArray();
        private static readonly HashSet<ulong> acceptedPeers = new HashSet<ulong>();
        private static readonly Dictionary<ulong, List<KeyValuePair<int, byte[]>>> heldFromStrangers =
            new Dictionary<ulong, List<KeyValuePair<int, byte[]>>>();
        public static long PacketsSent, PacketsReceived, BytesSent, BytesReceived;

        // ---- callbacks ----
        private static readonly Dictionary<Type, List<KeyValuePair<object, Delegate>>> callbacks =
            new Dictionary<Type, List<KeyValuePair<object, Delegate>>>();
        private static readonly Dictionary<ulong, KeyValuePair<object, Delegate>> callResults =
            new Dictionary<ulong, KeyValuePair<object, Delegate>>();
        private static ulong nextCall = 0x7A00000000000001UL;

        private static BrokerClient broker;
        private static Harmony harmony;

        public static void Install(Harmony h, BrokerClient b, ulong myId, string myName)
        {
            harmony = h; broker = b; MyId = myId; MyName = myName; Names[myId] = myName;

            P(typeof(SteamManager), "get_Initialized", nameof(Initialized_Prefix));
            P(typeof(SteamAPI), nameof(SteamAPI.RunCallbacks), nameof(Skip));
            P(typeof(SteamUserStats), nameof(SteamUserStats.SetAchievement), nameof(FalseResult));
            P(typeof(SteamUserStats), nameof(SteamUserStats.StoreStats), nameof(FalseResult));
            P(typeof(SteamUserStats), nameof(SteamUserStats.GetAchievement), nameof(GetAchievement_Prefix));
            P(typeof(SteamFriends), nameof(SteamFriends.ActivateGameOverlayToWebPage), nameof(Skip));

            P(typeof(SteamUser), nameof(SteamUser.GetSteamID), nameof(GetSteamID_Prefix));

            P(typeof(SteamFriends), nameof(SteamFriends.GetPersonaName), nameof(GetPersonaName_Prefix));
            P(typeof(SteamFriends), nameof(SteamFriends.GetFriendPersonaName), nameof(GetFriendPersonaName_Prefix));
            P(typeof(SteamFriends), nameof(SteamFriends.GetFriendPersonaState), nameof(GetFriendPersonaState_Prefix));
            P(typeof(SteamFriends), nameof(SteamFriends.GetFriendCount), nameof(GetFriendCount_Prefix));
            P(typeof(SteamFriends), nameof(SteamFriends.GetFriendByIndex), nameof(GetFriendByIndex_Prefix));
            P(typeof(SteamFriends), nameof(SteamFriends.GetFriendRichPresence), nameof(GetFriendRichPresence_Prefix));
            P(typeof(SteamFriends), nameof(SteamFriends.RequestFriendRichPresence), nameof(Skip));
            P(typeof(SteamFriends), nameof(SteamFriends.SetRichPresence), nameof(SetRichPresence_Prefix));
            P(typeof(SteamFriends), nameof(SteamFriends.ClearRichPresence), nameof(ClearRichPresence_Prefix));
            P(typeof(SteamFriends), nameof(SteamFriends.RequestUserInformation), nameof(FalseResult));
            P(typeof(SteamFriends), nameof(SteamFriends.GetMediumFriendAvatar), nameof(ZeroInt));
            P(typeof(SteamUtils), nameof(SteamUtils.GetImageSize), nameof(GetImageSize_Prefix));
            P(typeof(SteamUtils), nameof(SteamUtils.GetImageRGBA), nameof(FalseResult));

            P(typeof(SteamMatchmaking), nameof(SteamMatchmaking.CreateLobby), nameof(CreateLobby_Prefix));
            P(typeof(SteamMatchmaking), nameof(SteamMatchmaking.JoinLobby), nameof(JoinLobby_Prefix));
            P(typeof(SteamMatchmaking), nameof(SteamMatchmaking.LeaveLobby), nameof(LeaveLobby_Prefix));
            P(typeof(SteamMatchmaking), nameof(SteamMatchmaking.InviteUserToLobby), nameof(InviteUserToLobby_Prefix));
            P(typeof(SteamMatchmaking), nameof(SteamMatchmaking.GetLobbyData), nameof(GetLobbyData_Prefix));
            P(typeof(SteamMatchmaking), nameof(SteamMatchmaking.SetLobbyData), nameof(SetLobbyData_Prefix));
            P(typeof(SteamMatchmaking), nameof(SteamMatchmaking.GetLobbyMemberData), nameof(GetLobbyMemberData_Prefix));
            P(typeof(SteamMatchmaking), nameof(SteamMatchmaking.SetLobbyMemberData), nameof(SetLobbyMemberData_Prefix));
            P(typeof(SteamMatchmaking), nameof(SteamMatchmaking.GetNumLobbyMembers), nameof(GetNumLobbyMembers_Prefix));
            P(typeof(SteamMatchmaking), nameof(SteamMatchmaking.GetLobbyMemberByIndex), nameof(GetLobbyMemberByIndex_Prefix));
            P(typeof(SteamMatchmaking), nameof(SteamMatchmaking.GetLobbyOwner), nameof(GetLobbyOwner_Prefix));
            P(typeof(SteamMatchmaking), nameof(SteamMatchmaking.GetLobbyMemberLimit), nameof(GetLobbyMemberLimit_Prefix));
            P(typeof(SteamMatchmaking), nameof(SteamMatchmaking.RequestLobbyData), nameof(RequestLobbyData_Prefix));
            P(typeof(SteamMatchmaking), nameof(SteamMatchmaking.RequestLobbyList), nameof(RequestLobbyList_Prefix));
            P(typeof(SteamMatchmaking), nameof(SteamMatchmaking.GetLobbyByIndex), nameof(GetLobbyByIndex_Prefix));
            foreach (var f in new[] { "AddRequestLobbyListDistanceFilter", "AddRequestLobbyListFilterSlotsAvailable",
                                      "AddRequestLobbyListResultCountFilter", "AddRequestLobbyListStringFilter" })
                P(typeof(SteamMatchmaking), f, nameof(Skip));

            P(typeof(SteamNetworking), nameof(SteamNetworking.SendP2PPacket), nameof(SendP2PPacket_Prefix));
            P(typeof(SteamNetworking), nameof(SteamNetworking.IsP2PPacketAvailable), nameof(IsP2PPacketAvailable_Prefix));
            P(typeof(SteamNetworking), nameof(SteamNetworking.ReadP2PPacket), nameof(ReadP2PPacket_Prefix));
            P(typeof(SteamNetworking), nameof(SteamNetworking.AcceptP2PSessionWithUser), nameof(AcceptP2P_Prefix));
            P(typeof(SteamNetworking), nameof(SteamNetworking.CloseP2PSessionWithUser), nameof(CloseP2P_Prefix));
            P(typeof(SteamNetworking), nameof(SteamNetworking.GetP2PSessionState), nameof(GetP2PSessionState_Prefix));

            // Callback<T> / CallResult<T> registration for every type the mod uses.
            foreach (var t in new[] { typeof(LobbyCreated_t), typeof(LobbyEnter_t), typeof(LobbyDataUpdate_t),
                                      typeof(LobbyChatUpdate_t), typeof(P2PSessionRequest_t), typeof(GameLobbyJoinRequested_t),
                                      typeof(GameRichPresenceJoinRequested_t), typeof(FriendRichPresenceUpdate_t),
                                      typeof(LobbyMatchList_t) })
            {
                var cb = typeof(Callback<>).MakeGenericType(t);
                P(cb, "Register", nameof(CallbackRegister_Prefix));
                P(cb, "Unregister", nameof(CallbackUnregister_Prefix));
            }
            var cr = typeof(CallResult<>).MakeGenericType(typeof(LobbyMatchList_t));
            P(cr, "Set", nameof(CallResultSet_Prefix));
            P(cr, "Cancel", nameof(Skip));
            Plugin.Log.LogInfo($"[CoopQA] Steam mock installed as {MyName} ({MyId})");
        }

        private static void P(Type type, string method, string prefix)
        {
            var target = AccessTools.Method(type, method)
                         ?? throw new MissingMethodException(type.FullName, method);
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(SteamMock).GetMethod(prefix, BindingFlags.Static | BindingFlags.NonPublic)));
        }

        // =========================================================== generic helpers
        private static bool Skip() => false;
        private static bool FalseResult(ref bool __result) { __result = false; return false; }
        private static bool ZeroInt(ref int __result) { __result = 0; return false; }
        private static bool Initialized_Prefix(ref bool __result) { __result = true; return false; }
        private static bool GetAchievement_Prefix(ref bool pbAchieved, ref bool __result) { pbAchieved = false; __result = false; return false; }
        private static bool GetImageSize_Prefix(ref uint pnWidth, ref uint pnHeight, ref bool __result) { pnWidth = pnHeight = 0; __result = false; return false; }

        private static SteamAPICall_t NewCall() => new SteamAPICall_t(nextCall++);

        // =========================================================== identity / friends
        private static bool GetSteamID_Prefix(ref CSteamID __result) { __result = new CSteamID(MyId); return false; }
        private static bool GetPersonaName_Prefix(ref string __result) { __result = MyName; return false; }
        private static bool GetFriendPersonaName_Prefix(CSteamID steamIDFriend, ref string __result)
        {
            __result = Names.TryGetValue(steamIDFriend.m_SteamID, out var n) ? n : "[unknown]";
            return false;
        }
        private static bool GetFriendPersonaState_Prefix(CSteamID steamIDFriend, ref EPersonaState __result)
        {
            __result = Names.ContainsKey(steamIDFriend.m_SteamID) ? EPersonaState.k_EPersonaStateOnline : EPersonaState.k_EPersonaStateOffline;
            return false;
        }
        private static List<ulong> Friends() => Names.Keys.Where(k => k != MyId).OrderBy(k => k).ToList();
        private static bool GetFriendCount_Prefix(ref int __result) { __result = Friends().Count; return false; }
        private static bool GetFriendByIndex_Prefix(int iFriend, ref CSteamID __result)
        {
            var f = Friends();
            __result = iFriend >= 0 && iFriend < f.Count ? new CSteamID(f[iFriend]) : CSteamID.Nil;
            return false;
        }
        private static bool GetFriendRichPresence_Prefix(CSteamID steamIDFriend, string pchKey, ref string __result)
        {
            var map = steamIDFriend.m_SteamID == MyId ? MyRichPresence
                      : RichPresence.TryGetValue(steamIDFriend.m_SteamID, out var m) ? m : null;
            __result = map != null && map.TryGetValue(pchKey, out var v) ? v : "";
            return false;
        }
        private static bool SetRichPresence_Prefix(string pchKey, string pchValue, ref bool __result)
        {
            if (string.IsNullOrEmpty(pchValue)) MyRichPresence.Remove(pchKey); else MyRichPresence[pchKey] = pchValue;
            broker.Send(new { op = "rp_set", k = pchKey, v = pchValue ?? "" });
            __result = true; return false;
        }
        private static bool ClearRichPresence_Prefix()
        {
            MyRichPresence.Clear();
            broker.Send(new { op = "rp_set", clear = true });
            return false;
        }

        // =========================================================== matchmaking
        private static bool CreateLobby_Prefix(ELobbyType eLobbyType, int cMaxMembers, ref SteamAPICall_t __result)
        {
            __result = NewCall();
            broker.Send(new { op = "create_lobby", type = (int)eLobbyType, max = cMaxMembers, call = __result.m_SteamAPICall });
            return false;
        }
        private static bool JoinLobby_Prefix(CSteamID steamIDLobby, ref SteamAPICall_t __result)
        {
            __result = NewCall();
            broker.Send(new { op = "join", lobby = steamIDLobby.m_SteamID, call = __result.m_SteamAPICall });
            return false;
        }
        private static bool LeaveLobby_Prefix(CSteamID steamIDLobby)
        {
            Lobbies.Remove(steamIDLobby.m_SteamID);
            broker.Send(new { op = "leave", lobby = steamIDLobby.m_SteamID });
            return false;
        }
        private static bool InviteUserToLobby_Prefix(CSteamID steamIDLobby, CSteamID steamIDInvitee, ref bool __result)
        {
            broker.Send(new { op = "invite", lobby = steamIDLobby.m_SteamID, to = steamIDInvitee.m_SteamID });
            __result = true; return false;
        }
        private static bool GetLobbyData_Prefix(CSteamID steamIDLobby, string pchKey, ref string __result)
        {
            __result = Lobbies.TryGetValue(steamIDLobby.m_SteamID, out var l) && l.Data.TryGetValue(pchKey, out var v) ? v : "";
            return false;
        }
        private static bool SetLobbyData_Prefix(CSteamID steamIDLobby, string pchKey, string pchValue, ref bool __result)
        {
            if (!Lobbies.TryGetValue(steamIDLobby.m_SteamID, out var l) || l.Owner != MyId) { __result = false; return false; }
            l.Data[pchKey] = pchValue ?? "";
            broker.Send(new { op = "set_data", lobby = steamIDLobby.m_SteamID, k = pchKey, v = pchValue ?? "" });
            __result = true; return false;
        }
        private static bool GetLobbyMemberData_Prefix(CSteamID steamIDLobby, CSteamID steamIDUser, string pchKey, ref string __result)
        {
            __result = Lobbies.TryGetValue(steamIDLobby.m_SteamID, out var l) &&
                       l.MemberData.TryGetValue(steamIDUser.m_SteamID, out var md) && md.TryGetValue(pchKey, out var v) ? v : "";
            return false;
        }
        private static bool SetLobbyMemberData_Prefix(CSteamID steamIDLobby, string pchKey, string pchValue)
        {
            if (Lobbies.TryGetValue(steamIDLobby.m_SteamID, out var l))
            {
                if (!l.MemberData.TryGetValue(MyId, out var md)) l.MemberData[MyId] = md = new Dictionary<string, string>();
                md[pchKey] = pchValue ?? "";
            }
            broker.Send(new { op = "set_mdata", lobby = steamIDLobby.m_SteamID, k = pchKey, v = pchValue ?? "" });
            return false;
        }
        private static bool GetNumLobbyMembers_Prefix(CSteamID steamIDLobby, ref int __result)
        {
            __result = Lobbies.TryGetValue(steamIDLobby.m_SteamID, out var l) ? l.Members.Count : 0; return false;
        }
        private static bool GetLobbyMemberByIndex_Prefix(CSteamID steamIDLobby, int iMember, ref CSteamID __result)
        {
            __result = Lobbies.TryGetValue(steamIDLobby.m_SteamID, out var l) && iMember >= 0 && iMember < l.Members.Count
                ? new CSteamID(l.Members[iMember]) : CSteamID.Nil;
            return false;
        }
        private static bool GetLobbyOwner_Prefix(CSteamID steamIDLobby, ref CSteamID __result)
        {
            __result = Lobbies.TryGetValue(steamIDLobby.m_SteamID, out var l) ? new CSteamID(l.Owner) : CSteamID.Nil; return false;
        }
        private static bool GetLobbyMemberLimit_Prefix(CSteamID steamIDLobby, ref int __result)
        {
            __result = Lobbies.TryGetValue(steamIDLobby.m_SteamID, out var l) ? l.Limit : 0; return false;
        }
        private static bool RequestLobbyData_Prefix(CSteamID steamIDLobby, ref bool __result)
        {
            broker.Send(new { op = "request_data", lobby = steamIDLobby.m_SteamID });
            __result = true; return false;
        }
        private static bool RequestLobbyList_Prefix(ref SteamAPICall_t __result)
        {
            __result = NewCall();
            broker.Send(new { op = "list_lobbies", call = __result.m_SteamAPICall });
            return false;
        }
        private static bool GetLobbyByIndex_Prefix(int iLobby, ref CSteamID __result)
        {
            __result = iLobby >= 0 && iLobby < lastLobbyList.Count ? new CSteamID(lastLobbyList[iLobby]) : CSteamID.Nil; return false;
        }

        // =========================================================== P2P
        private static bool SendP2PPacket_Prefix(CSteamID steamIDRemote, byte[] pubData, uint cubData, EP2PSend eP2PSendType, int nChannel, ref bool __result)
        {
            bool reliable = eP2PSendType == EP2PSend.k_EP2PSendReliable || eP2PSendType == EP2PSend.k_EP2PSendReliableWithBuffering;
            // Steam rules: unreliable over 1200 bytes and anything over 1 MB fail.
            if ((!reliable && cubData > 1200) || cubData > 1024 * 1024 || pubData == null || cubData > pubData.Length ||
                nChannel < 0 || nChannel >= MaxChannels)
            {
                __result = false; return false;
            }
            acceptedPeers.Add(steamIDRemote.m_SteamID); // sending implicitly accepts the session
            var payload = new byte[cubData];
            Buffer.BlockCopy(pubData, 0, payload, 0, (int)cubData);
            broker.Send(new { op = "p2p", to = steamIDRemote.m_SteamID, ch = nChannel, rel = reliable, data = Convert.ToBase64String(payload) });
            PacketsSent++; BytesSent += cubData;
            __result = true; return false;
        }
        private static bool IsP2PPacketAvailable_Prefix(ref uint pcubMsgSize, int nChannel, ref bool __result)
        {
            if (nChannel >= 0 && nChannel < MaxChannels && inbox[nChannel].Count > 0)
            {
                pcubMsgSize = (uint)inbox[nChannel].Peek().Value.Length; __result = true;
            }
            else { pcubMsgSize = 0; __result = false; }
            return false;
        }
        private static bool ReadP2PPacket_Prefix(byte[] pubDest, uint cubDest, ref uint pcubMsgSize, ref CSteamID psteamIDRemote, int nChannel, ref bool __result)
        {
            if (nChannel < 0 || nChannel >= MaxChannels || inbox[nChannel].Count == 0) { pcubMsgSize = 0; __result = false; return false; }
            var pkt = inbox[nChannel].Dequeue();
            int n = (int)Math.Min(cubDest, (uint)pkt.Value.Length);
            Buffer.BlockCopy(pkt.Value, 0, pubDest, 0, n);
            pcubMsgSize = (uint)n;
            psteamIDRemote = new CSteamID(pkt.Key);
            __result = true; return false;
        }
        private static bool AcceptP2P_Prefix(CSteamID steamIDRemote, ref bool __result)
        {
            ulong id = steamIDRemote.m_SteamID;
            acceptedPeers.Add(id);
            if (heldFromStrangers.TryGetValue(id, out var held))
            {
                foreach (var p in held) inbox[p.Key].Enqueue(new KeyValuePair<ulong, byte[]>(id, p.Value));
                heldFromStrangers.Remove(id);
            }
            __result = true; return false;
        }
        private static bool CloseP2P_Prefix(CSteamID steamIDRemote, ref bool __result)
        {
            acceptedPeers.Remove(steamIDRemote.m_SteamID); __result = true; return false;
        }
        private static bool GetP2PSessionState_Prefix(CSteamID steamIDRemote, ref P2PSessionState_t pConnectionState, ref bool __result)
        {
            bool known = Names.ContainsKey(steamIDRemote.m_SteamID);
            pConnectionState = new P2PSessionState_t { m_bConnectionActive = (byte)(known && acceptedPeers.Contains(steamIDRemote.m_SteamID) ? 1 : 0), m_bUsingRelay = 0 };
            __result = known; return false;
        }

        // =========================================================== callbacks
        private static bool CallbackRegister_Prefix(object __instance, object[] __args)
        {
            var t = __instance.GetType().GetGenericArguments()[0];
            if (!callbacks.TryGetValue(t, out var list)) callbacks[t] = list = new List<KeyValuePair<object, Delegate>>();
            list.RemoveAll(e => ReferenceEquals(e.Key, __instance));
            list.Add(new KeyValuePair<object, Delegate>(__instance, (Delegate)__args[0]));
            return false;
        }
        private static bool CallbackUnregister_Prefix(object __instance)
        {
            var t = __instance.GetType().GetGenericArguments()[0];
            if (callbacks.TryGetValue(t, out var list)) list.RemoveAll(e => ReferenceEquals(e.Key, __instance));
            return false;
        }
        private static bool CallResultSet_Prefix(object __instance, object[] __args)
        {
            var handle = (SteamAPICall_t)__args[0];
            var func = (Delegate)__args[1] ?? (Delegate)AccessTools.Field(__instance.GetType(), "m_Func").GetValue(__instance);
            AccessTools.Field(__instance.GetType(), "m_hAPICall").SetValue(__instance, handle);
            callResults[handle.m_SteamAPICall] = new KeyValuePair<object, Delegate>(__instance, func);
            return false;
        }

        private static void Fire<T>(T value)
        {
            if (!callbacks.TryGetValue(typeof(T), out var list) || list.Count == 0)
            {
                Plugin.Log.LogInfo($"[CoopQA] no listener for {typeof(T).Name}");
                return;
            }
            foreach (var e in list.ToArray())
            {
                try { ((Callback<T>.DispatchDelegate)e.Value)(value); }
                catch (Exception ex) { Plugin.Log.LogError($"[CoopQA] {typeof(T).Name} handler threw: {ex}"); }
            }
        }

        public static int CallbackCount(Type t) => callbacks.TryGetValue(t, out var l) ? l.Count : 0;

        // =========================================================== broker messages -> Steam state/callbacks
        public static void Pump()
        {
            int budget = 4000;
            while (budget-- > 0 && broker.Inbox.TryDequeue(out var m))
            {
                try { Handle(m); }
                catch (Exception e) { Plugin.Log.LogError($"[CoopQA] handling {m["op"]} failed: {e}"); }
            }
        }

        private static void Handle(JObject m)
        {
            switch ((string)m["op"])
            {
                case "peers":
                    foreach (var p in (JArray)m["peers"]) Names[(ulong)p["id"]] = (string)p["name"];
                    break;
                case "lobby_state":
                {
                    ulong id = (ulong)m["lobby"];
                    if (!Lobbies.TryGetValue(id, out var l)) Lobbies[id] = l = new Lobby();
                    l.Owner = (ulong)m["owner"];
                    l.Limit = (int)m["limit"];
                    l.Members = ((JArray)m["members"]).Select(x => (ulong)x).ToList();
                    l.Data = ((JObject)m["data"]).Properties().ToDictionary(p => p.Name, p => (string)p.Value);
                    l.MemberData = ((JObject)m["mdata"]).Properties().ToDictionary(
                        p => ulong.Parse(p.Name), p => ((JObject)p.Value).Properties().ToDictionary(q => q.Name, q => (string)q.Value));
                    break;
                }
                case "lobby_created":
                    Fire(new LobbyCreated_t { m_eResult = (EResult)(int)m["result"], m_ulSteamIDLobby = (ulong)m["lobby"] });
                    break;
                case "entered":
                    Fire(new LobbyEnter_t { m_ulSteamIDLobby = (ulong)m["lobby"], m_rgfChatPermissions = 0, m_bLocked = false,
                                            m_EChatRoomEnterResponse = (uint)(int)m["response"] });
                    break;
                case "data_update":
                    Fire(new LobbyDataUpdate_t { m_ulSteamIDLobby = (ulong)m["lobby"], m_ulSteamIDMember = (ulong)m["member"], m_bSuccess = (byte)(int)m["success"] });
                    break;
                case "chat_update":
                    Fire(new LobbyChatUpdate_t { m_ulSteamIDLobby = (ulong)m["lobby"], m_ulSteamIDUserChanged = (ulong)m["user"],
                                                 m_ulSteamIDMakingChange = (ulong)m["by"], m_rgfChatMemberStateChange = (uint)(int)m["change"] });
                    break;
                case "invited":
                    Bot.LastInviteLobby = (ulong)m["lobby"];
                    Fire(new GameLobbyJoinRequested_t { m_steamIDLobby = new CSteamID((ulong)m["lobby"]), m_steamIDFriend = new CSteamID((ulong)m["from"]) });
                    break;
                case "rp":
                {
                    ulong user = (ulong)m["user"];
                    RichPresence[user] = ((JObject)m["values"]).Properties().ToDictionary(p => p.Name, p => (string)p.Value);
                    if (user != MyId)
                        Fire(new FriendRichPresenceUpdate_t { m_steamIDFriend = new CSteamID(user), m_nAppID = new AppId_t(599140) });
                    break;
                }
                case "lobby_list":
                {
                    lastLobbyList = new List<ulong>();
                    foreach (var st in (JArray)m["lobbies"])
                    {
                        Handle((JObject)st);
                        lastLobbyList.Add((ulong)st["lobby"]);
                    }
                    ulong call = (ulong)m["call"];
                    if (callResults.TryGetValue(call, out var cr))
                    {
                        callResults.Remove(call);
                        ((CallResult<LobbyMatchList_t>.APIDispatchDelegate)cr.Value)(new LobbyMatchList_t { m_nLobbiesMatching = (uint)lastLobbyList.Count }, false);
                    }
                    break;
                }
                case "p2p":
                {
                    ulong from = (ulong)m["from"];
                    int ch = (int)m["ch"];
                    byte[] data = Convert.FromBase64String((string)m["data"]);
                    PacketsReceived++; BytesReceived += data.Length;
                    if (acceptedPeers.Contains(from))
                        inbox[ch].Enqueue(new KeyValuePair<ulong, byte[]>(from, data));
                    else
                    {
                        bool first = !heldFromStrangers.ContainsKey(from);
                        if (first) heldFromStrangers[from] = new List<KeyValuePair<int, byte[]>>();
                        heldFromStrangers[from].Add(new KeyValuePair<int, byte[]>(ch, data));
                        if (first) Fire(new P2PSessionRequest_t { m_steamIDRemote = new CSteamID(from) });
                    }
                    break;
                }
                case "qa":
                    Bot.OnSideChannel((int)m["from"], (string)m["k"], (string)m["v"]);
                    break;
            }
        }
    }
}
