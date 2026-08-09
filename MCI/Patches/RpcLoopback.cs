using AmongUs.InnerNet.GameDataMessages;
using HarmonyLib;
using Hazel;
using InnerNet;

namespace MCI.Patches;

/// <summary>
///     Makes client-targeted RPCs reach MCI's bots.
///     <para>
///         A targeted RPC is written as a message addressed to a client id and handed to the server, which relays it to
///         the socket owning that id. MCI's bots have a <see cref="ClientData" /> entry but nothing behind it, so every
///         such message is written to the wire and lost - which is why role assignments, per-player syncs and anything
///         else a mod sends to one player have never applied to a bot.
///     </para>
///     <para>
///         Every bot lives in this process and shares the same <see cref="InnerNetObject" /> graph as the real client, so
///         "delivering" one of these messages is just running its handler here. This intercepts messages aimed at a bot
///         and dispatches them locally instead of sending them.
///     </para>
///     <para>
///         Broadcast RPCs are deliberately left alone: the host already applies those locally before sending, so looping
///         them back would run every one of them twice.
///     </para>
/// </summary>
[HarmonyPatch(typeof(InnerNetClient))]
public static class RpcLoopback
{
    /// <summary>
    ///     Escape hatch. Set to false to restore the old behaviour of dropping targeted RPCs.
    /// </summary>
    public static bool Enabled { get; set; } = true;

    /// <summary>
    ///     The target client id of each in-flight writer, keyed by its il2cpp pointer.
    ///     <see cref="InnerNetClient.FinishRpcImmediately" /> only receives the writer, so the id has to be remembered
    ///     from the matching <see cref="InnerNetClient.StartRpcImmediately" /> call.
    /// </summary>
    private static readonly Dictionary<IntPtr, int> PendingTargets = new();

    internal static void Reset() => PendingTargets.Clear();

    [HarmonyPostfix]
    [HarmonyPatch(nameof(InnerNetClient.StartRpcImmediately))]
    public static void StartRpcImmediatelyPostfix(int targetClientId, MessageWriter __result)
    {
        if (targetClientId < 0 || __result == null)
            return;

        PendingTargets[__result.Pointer] = targetClientId;
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(InnerNetClient.FinishRpcImmediately))]
    public static bool FinishRpcImmediatelyPrefix(InnerNetClient __instance, MessageWriter msg)
    {
        if (msg == null || !PendingTargets.Remove(msg.Pointer, out var targetClientId))
            return true;

        if (!Enabled || !MCIPlugin.Enabled || !InstanceControl.Clients.ContainsKey(targetClientId))
            return true;

        // FinishRpcImmediately closes the rpc message and the envelope around it before sending; do the same so the
        // buffer is a complete, readable message, then dispatch it instead of sending it.
        msg.EndMessage();
        msg.EndMessage();

        Dispatch(__instance, msg, targetClientId);

        msg.Recycle();
        return false;
    }

    /// <summary>
    ///     The envelope tag <see cref="InnerNetClient.StartRpcImmediately" /> uses for a message addressed to one client.
    ///     The game writes these as raw numbers rather than through an enum.
    /// </summary>
    private const byte GameDataToFlag = 6;

    private static void Dispatch(InnerNetClient client, MessageWriter msg, int targetClientId)
    {
        // A writer's buffer opens with a send option header - 3 bytes when reliable, 1 otherwise - that is not part of
        // the message. ToByteArray(false) drops it and trims to the written length, so the result starts at the
        // envelope's own header and nothing has to assume the header's size.
        var reader = MessageReader.Get(msg.ToByteArray(false));

        try
        {
            var envelope = reader.ReadMessage();

            if (envelope.Tag != GameDataToFlag)
            {
                MCIPlugin.Logger.LogError($"Dropped an rpc aimed at bot {targetClientId}: expected envelope tag {GameDataToFlag}, got {envelope.Tag}.");
                return;
            }

            envelope.ReadInt32(); // game id
            envelope.ReadPackedInt32(); // target client id, already known

            while (envelope.Position < envelope.Length)
            {
                var rpc = envelope.ReadMessage();

                if (rpc.Tag != (byte)GameDataTypes.RpcFlag)
                    continue;

                var netId = rpc.ReadPackedUInt32();
                var callId = rpc.ReadByte();
                var target = client.FindObjectByNetId<InnerNetObject>(netId);

                if (target == null)
                {
                    MCIPlugin.Logger.LogWarning($"Dropped rpc {callId} for bot {targetClientId}: no object with net id {netId}.");
                    continue;
                }

                target.HandleRpc(callId, rpc);
            }
        }
        catch (Exception e)
        {
            MCIPlugin.Logger.LogError($"Failed to loop back an rpc aimed at bot {targetClientId}: {e}");
        }
        finally
        {
            reader.Recycle();
        }
    }
}
