using AmongUs.Data;
using Il2CppInterop.Runtime.Attributes;
using InnerNet;
using MCI.Embedded.ReactorImGui;
using MCI.Patches;
using MiraAPI.Hud;
using MiraAPI.Modifiers;
using MiraAPI.Modifiers.Types;
using MiraAPI.Networking;
using MiraAPI.Utilities;
using TownOfUs.Modules;
using TownOfUs.Modules.Anims;
using TownOfUs.Networking;
using TownOfUs.Utilities;
using UnityEngine;

namespace MCI.Components;
public class Debugger : MonoBehaviour
{
    [HideFromIl2Cpp]
    public DragWindow Window { get; }
    public bool WindowEnabled { get; set; } = true;
    public Debugger(System.IntPtr ptrAlt) : base(ptrAlt)
    {
        Window = new(new(20, 20, 0, 0), "MCI Debugger", () =>
        {
            GUILayout.Label($"{DataManager.Player.customization.Name} - Press F1 To Hide");

            var mouse = Input.mousePosition;
            GUILayout.Label($"Mouse Position\nx: {mouse.x:00.00} y: {mouse.y:00.00} z: {mouse.z:00.00}");

            if (PlayerControl.LocalPlayer)
            {
                GUILayout.Label($"{PlayerControl.LocalPlayer.CurrentOutfit.PlayerName}");
                var position = PlayerControl.LocalPlayer.gameObject.transform.position;
                GUILayout.Label($"Player Position\nx: {position.x:00.00} y: {position.y:00.00} z: {position.z:00.00}");

                if (!PlayerControl.LocalPlayer.Data.IsDead && MCIPlugin.Enabled)
                {
                    PlayerControl.LocalPlayer.Collider.enabled = GUILayout.Toggle(PlayerControl.LocalPlayer.Collider.enabled, "Enable Player Collider");
                }
            }

            if (!MCIPlugin.Enabled || !PlayerControl.LocalPlayer)
            {
                GUILayout.Label("Debugger features only work on localhosted lobbies");
                return;
            }

            if (AmongUsClient.Instance == null || GameManager.Instance == null ||
                !(AmongUsClient.Instance.GameState is InnerNetClient.GameStates.Joined
                    or InnerNetClient.GameStates.Started ||
                (GameManager.Instance.GameHasStarted &&
                 AmongUsClient.Instance.GameState != InnerNetClient.GameStates.Ended)))
            {
                return;
            }

            if (GUILayout.Button($"Spawn Bot ({InstanceControl.Clients.Count}/15)"))
            {
                Keyboard_Joystick.CreatePlayer();
            }
            GUILayout.Label("Hold LftShft when pressing to bypass player limit.");

            if (GUILayout.Button("Remove Last Bot"))
                InstanceControl.RemovePlayer((byte)InstanceControl.Clients.Count);

            if (GUILayout.Button("Remove All Bots"))
                InstanceControl.RemoveAllPlayers();

            if (GUILayout.Button("Next Player"))
                Keyboard_Joystick.Switch(true);

            if (GUILayout.Button("Previous Player"))
                Keyboard_Joystick.Switch(false);

            if (GUILayout.Button("End Game"))
                GameManager.Instance.RpcEndGame(GameOverReason.ImpostorsBySabotage, false);

            if (GUILayout.Button("Turn Impostor"))
            {
                PlayerControl.LocalPlayer.Data.Role.TeamType = RoleTeamTypes.Impostor;
                if (!PlayerControl.LocalPlayer.Data.IsDead)
                {
                    RoleManager.Instance.SetRole(PlayerControl.LocalPlayer, AmongUs.GameOptions.RoleTypes.Impostor);
                    DestroyableSingleton<HudManager>.Instance.KillButton.gameObject.SetActive(true);
                    PlayerControl.LocalPlayer.SetKillTimer(GameOptionsManager.Instance.currentNormalGameOptions.KillCooldown);
                }
                else
                {
                    RoleManager.Instance.SetRole(PlayerControl.LocalPlayer, AmongUs.GameOptions.RoleTypes.ImpostorGhost);
                }
            }

            if (GUILayout.Button("Turn Crewmate"))
            {
                PlayerControl.LocalPlayer.Data.Role.TeamType = RoleTeamTypes.Crewmate;
                if (!PlayerControl.LocalPlayer.Data.IsDead)
                    RoleManager.Instance.SetRole(PlayerControl.LocalPlayer, AmongUs.GameOptions.RoleTypes.Crewmate);
                else
                    RoleManager.Instance.SetRole(PlayerControl.LocalPlayer, AmongUs.GameOptions.RoleTypes.CrewmateGhost);
            }

            if (GUILayout.Button("Complete Tasks"))
                foreach (var task in PlayerControl.LocalPlayer.myTasks)
                {
                    PlayerControl.LocalPlayer.RpcCompleteTask(task.Id);
                }

            if (GUILayout.Button("Complete Everyone's Tasks"))
                foreach (var player in PlayerControl.AllPlayerControls)
                {
                    foreach (var task in player.myTasks)
                    {
                        player.RpcCompleteTask(task.Id);
                    }
                }

            if (GUILayout.Button("Redo Intro Sequence"))
            {
                HudManager.Instance.StartCoroutine(HudManager.Instance.CoFadeFullScreen(Color.clear, Color.black));
                HudManager.Instance.StartCoroutine(HudManager.Instance.CoShowIntro());
            }

            if (!MeetingHud.Instance && GUILayout.Button("Start Meeting"))
            {
                PlayerControl.LocalPlayer.RemainingEmergencies++;
                PlayerControl.LocalPlayer.CmdReportDeadBody(null);
            }

            if (GUILayout.Button("End Meeting") && MeetingHud.Instance)
                MeetingHud.Instance.RpcClose();

            if (GUILayout.Button("Kill Self"))
                PlayerControl.LocalPlayer.RpcCustomMurder(PlayerControl.LocalPlayer, showKillAnim: false);

            if (GUILayout.Button("Kill All"))
            {
                PlayerControl.LocalPlayer.RpcSpecialMultiMurder(Helpers.GetAlivePlayers(), true, showKillAnim: false);
            }

            if (GUILayout.Button("Revive Self"))
            {
                var inMeetingOrExile = ExileController.Instance != null || MeetingHud.Instance != null;
                    if (!PlayerControl.LocalPlayer.HasDied())
                    {
                        return;
                    }

                    var roleWhenAlive = PlayerControl.LocalPlayer.GetRoleWhenAlive();

                    var body = FindObjectsOfType<DeadBody>()
                        .FirstOrDefault(b => b.ParentId == PlayerControl.LocalPlayer.PlayerId);
                    var position = new Vector2(PlayerControl.LocalPlayer.transform.localPosition.x, PlayerControl.LocalPlayer.transform.localPosition.y);

                    if (body != null)
                    {
                        position = new Vector2(body.transform.localPosition.x,
                            body.transform.localPosition.y + 0.3636f);
                        Destroy(body.gameObject);
                    }

                    GameHistory.ClearMurder(PlayerControl.LocalPlayer);

                    PlayerControl.LocalPlayer.Revive();

                    if (!inMeetingOrExile)
                    {
                        PlayerControl.LocalPlayer.transform.position = position;
                        PlayerControl.LocalPlayer.NetTransform.RpcSnapTo(position);

                        if (PlayerControl.LocalPlayer.MyPhysics.body != null)
                        {
                            PlayerControl.LocalPlayer.MyPhysics.body.position = position;
                            Physics2D.SyncTransforms();
                        }
                    }

                    if (ModCompatibility.IsSubmerged())
                    {
                        ModCompatibility.ChangeFloor(PlayerControl.LocalPlayer.transform.position.y > -7);
                    }

                    PlayerControl.LocalPlayer.ChangeRole((ushort)roleWhenAlive.Role, recordRole: false);

                    if (PlayerControl.LocalPlayer.Data.Role is IAnimated animatedRole)
                    {
                        animatedRole.IsVisible = true;
                        animatedRole.SetVisible();
                    }

                    foreach (var button in CustomButtonManager.Buttons.Where(x => x.Enabled(PlayerControl.LocalPlayer.Data.Role))
                                 .OfType<IAnimated>())
                    {
                        button.IsVisible = true;
                        button.SetVisible();
                    }

                    foreach (var modifier in PlayerControl.LocalPlayer.GetModifiers<GameModifier>().Where(x => x is IAnimated))
                    {
                        if (modifier is IAnimated animatedMod)
                        {
                            animatedMod.IsVisible = true;
                            animatedMod.SetVisible();
                        }
                    }
            }

            if (GUILayout.Button("Revive All"))
            {
                var inMeetingOrExile = ExileController.Instance != null || MeetingHud.Instance != null;
                foreach (var player in PlayerControl.AllPlayerControls)
                {
                    if (!player.HasDied())
                    {
                        continue;
                    }

                    var roleWhenAlive = player.GetRoleWhenAlive();

                    var body = FindObjectsOfType<DeadBody>()
                        .FirstOrDefault(b => b.ParentId == player.PlayerId);
                    var position = new Vector2(player.transform.localPosition.x, player.transform.localPosition.y);

                    if (body != null)
                    {
                        position = new Vector2(body.transform.localPosition.x,
                            body.transform.localPosition.y + 0.3636f);
                        Destroy(body.gameObject);
                    }

                    GameHistory.ClearMurder(player);

                    player.Revive();

                    if (!inMeetingOrExile)
                    {
                        player.transform.position = position;
                        player.NetTransform.RpcSnapTo(position);

                        if (player.MyPhysics?.body != null)
                        {
                            player.MyPhysics.body.position = position;
                            Physics2D.SyncTransforms();
                        }
                    }

                    if (ModCompatibility.IsSubmerged() && PlayerControl.LocalPlayer != null &&
                        PlayerControl.LocalPlayer.PlayerId == player.PlayerId)
                    {
                        ModCompatibility.ChangeFloor(player.transform.position.y > -7);
                    }

                    player.ChangeRole((ushort)roleWhenAlive.Role, recordRole: false);

                    if (player.Data.Role is IAnimated animatedRole)
                    {
                        animatedRole.IsVisible = true;
                        animatedRole.SetVisible();
                    }

                    foreach (var button in CustomButtonManager.Buttons.Where(x => x.Enabled(player.Data.Role))
                                 .OfType<IAnimated>())
                    {
                        button.IsVisible = true;
                        button.SetVisible();
                    }

                    foreach (var modifier in player.GetModifiers<GameModifier>().Where(x => x is IAnimated))
                    {
                        if (modifier is IAnimated animatedMod)
                        {
                            animatedMod.IsVisible = true;
                            animatedMod.SetVisible();
                        }
                    }
                }
            }

            if (GUILayout.Button("Hide Player Names"))
            {
                foreach (var player in PlayerControl.AllPlayerControls)
                {
                    player.cosmetics.nameTextContainer.gameObject.SetActive(false);
                }
            }
            if (GUILayout.Button("Show Player Names"))
            {
                foreach (var player in PlayerControl.AllPlayerControls)
                {
                    player.cosmetics.nameTextContainer.gameObject.SetActive(true);
                }
            }
            if (GUILayout.Button("Toggle Shadows") && HudManager.InstanceExists)
            {
                HudManager.Instance.ShadowQuad.gameObject.SetActive(!HudManager.Instance.ShadowQuad.gameObject.active);
            }
        });
    }

    public void OnGUI()
    {
        if (WindowEnabled) Window.OnGUI();
    }

    public void Toggle()
    {
        WindowEnabled = !WindowEnabled;
    }

    public void Update()
    {
        if (Input.GetKeyDown(KeyCode.F1))
            Toggle();
    }
}
