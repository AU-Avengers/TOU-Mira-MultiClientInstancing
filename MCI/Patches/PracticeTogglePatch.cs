using HarmonyLib;
using TownOfUs.Modules;

namespace MCI.Patches;

[HarmonyPatch(typeof(FreeplayButtonsVisibility), nameof(FreeplayButtonsVisibility.Toggle))]
public static class PracticeTogglePatch
{
    public static bool Prefix()
    {
        // Disabled because of the F9 conflict
        return false;
    }
}
