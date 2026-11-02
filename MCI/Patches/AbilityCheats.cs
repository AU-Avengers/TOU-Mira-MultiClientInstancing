using HarmonyLib;
using MiraAPI.Hud;

namespace MCI.Patches;

[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.FixedUpdate))]
public static class AbilityCheats
{
    public static bool NoCooldowns { get; set; }
    public static bool UnlimitedUses { get; set; }

    public static void Prefix(PlayerControl __instance)
    {
        if (!MCIPlugin.Enabled || !__instance.AmOwner || (!NoCooldowns && !UnlimitedUses))
            return;

        if (NoCooldowns && __instance.killTimer > 0)
            __instance.SetKillTimer(0);

        foreach (var button in CustomButtonManager.Buttons)
        {
            if (NoCooldowns && !button.EffectActive && button.Timer > 0)
                button.SetTimer(0);

            if (UnlimitedUses && button.LimitedUses && button.Button)
            {
                var full = Math.Max(button.MaxUses, 1);
                if (button.UsesLeft < full)
                    button.SetUses(full);
            }
        }
    }
}
