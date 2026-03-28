using HarmonyLib;
using Timberborn.ModManagerScene;
using UnityEngine;

namespace Calloatti.TankToPump
{
  public class ModStarter : IModStarter
  {
    public static readonly string ModId = "calloatti.pumptotank";

    public void StartMod(IModEnvironment modEnvironment)
    {
      Harmony harmony = new Harmony(ModId);
      harmony.PatchAll();
      Debug.Log($"[{ModId}] Harmony patches applied successfully.");
    }
  }
}