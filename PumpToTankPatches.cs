using HarmonyLib;
using System;
using System.Reflection;
using Timberborn.Goods;
using Timberborn.InventorySystem;
using Timberborn.WaterBuildings;
using Timberborn.Particles;
using UnityEngine;

namespace Calloatti.TankToPump
{
  [HarmonyPatch]
  public static class PumpToTankPatches
  {
    private const float VolToGood = 5.0f;
    private static readonly FieldInfo WaterAddedEventField = AccessTools.Field(typeof(WaterOutput), "WaterAdded");
    private static readonly FieldInfo ParticlesRunnerField = AccessTools.Field(typeof(WaterMoverParticleController), "_particlesRunner");

    [HarmonyPatch(typeof(WaterMover), "IsWaterFlowPossible")]
    [HarmonyPostfix]
    public static void IsWaterFlowPossible_Postfix(WaterMover __instance, ref bool __result)
    {
      // Per your rule: This stays TRUE so the pump always consumes power and turns the wheel
      if (PumpToTankManager.ActivePairs.ContainsKey(__instance))
      {
        __result = true;
      }
    }

    // NEW PATCH: Intercept the particles directly to stop the visual spray when the tank is full
    [HarmonyPatch(typeof(WaterMoverParticleController), "Tick")]
    [HarmonyPostfix]
    public static void ParticleController_Tick_Postfix(WaterMoverParticleController __instance)
    {
      var mover = __instance.GetComponent<WaterMover>();
      if (PumpToTankManager.ActivePairs.TryGetValue(mover, out var tank))
      {
        bool hasCapacity = false;
        if (mover.CleanWaterMovement && tank.Takes("Water") && tank.UnreservedCapacity("Water") > 0)
          hasCapacity = true;
        if (mover.ContaminatedWaterMovement && tank.Takes("Badwater") && tank.UnreservedCapacity("Badwater") > 0)
          hasCapacity = true;

        // If the tank is full, override the game's automatic "Play" command and force the particles to stop
        if (!hasCapacity)
        {
          var runner = ParticlesRunnerField.GetValue(__instance) as ParticlesRunner;
          runner?.Stop();
        }
      }
    }

    [HarmonyPatch(typeof(WaterMover), "MoveWater")]
    [HarmonyPrefix]
    public static bool MoveWater_Prefix(WaterMover __instance, float waterAmount)
    {
      if (!PumpToTankManager.ActivePairs.TryGetValue(__instance, out var tank)) return true;

      var input = __instance.GetComponent<WaterInput>();
      var output = __instance.GetComponent<WaterOutput>();
      var acc = PumpToTankManager.Accumulators[__instance];

      float cleanScaler = GetCleanMovementScaler(__instance, input);
      float num = waterAmount * cleanScaler;
      float num2 = waterAmount - num;

      float cleanAvailableSpace = 0f;
      float badAvailableSpace = 0f;

      if (tank.Takes("Water"))
      {
        float accVol = Mathf.Max(0f, (1f / VolToGood) - (acc.FluidFractions.TryGetValue("Water", out float fw) ? fw / VolToGood : 0f));
        cleanAvailableSpace = (tank.UnreservedCapacity("Water") / VolToGood) + accVol;
      }

      if (tank.Takes("Badwater"))
      {
        float accVol = Mathf.Max(0f, (1f / VolToGood) - (acc.FluidFractions.TryGetValue("Badwater", out float fb) ? fb / VolToGood : 0f));
        badAvailableSpace = (tank.UnreservedCapacity("Badwater") / VolToGood) + accVol;
      }

      float num3 = Mathf.Max(Mathf.Min(num, input.CleanWaterAmount, cleanAvailableSpace), 0f);
      float num4 = Mathf.Max(Mathf.Min(num2, input.ContaminatedWaterAmount, badAvailableSpace), 0f);

      // Stop physical river draining if tank is full
      if (num3 > 0f) input.RemoveCleanWater(num3);
      if (num4 > 0f) input.RemoveContaminatedWater(num4);

      // Trigger the water column underneath the pump (stops if 0)
      if (num3 > 0f || num4 > 0f)
      {
        TriggerVisualizer(output, num3, num4);
      }

      // Process Inventory Transfers
      if (num3 > 0f)
      {
        if (!acc.FluidFractions.ContainsKey("Water")) acc.FluidFractions["Water"] = 0f;
        acc.FluidFractions["Water"] += num3 * VolToGood;
        ProcessInventory(tank, acc, "Water");
      }
      if (num4 > 0f)
      {
        if (!acc.FluidFractions.ContainsKey("Badwater")) acc.FluidFractions["Badwater"] = 0f;
        acc.FluidFractions["Badwater"] += num4 * VolToGood;
        ProcessInventory(tank, acc, "Badwater");
      }

      return false;
    }

    private static float GetCleanMovementScaler(WaterMover instance, WaterInput input)
    {
      if (instance.CleanWaterMovement)
      {
        float contaminationPercentage = input.ContaminationPercentage;
        if (!instance.ContaminatedWaterMovement) return 1f;
        return 1f - contaminationPercentage;
      }
      return 0f;
    }

    private static void ProcessInventory(Inventory tank, FractionalAccumulator acc, string id)
    {
      int toGive = Mathf.Min(Mathf.FloorToInt(acc.FluidFractions[id]), tank.UnreservedCapacity(id));
      if (toGive > 0)
      {
        tank.Give(new GoodAmount(id, toGive));
        acc.FluidFractions[id] -= (float)toGive;
      }
    }

    private static void TriggerVisualizer(WaterOutput output, float cleanMoved, float badMoved)
    {
      if (WaterAddedEventField == null) return;
      var eventDelegate = (EventHandler<WaterAddition>)WaterAddedEventField.GetValue(output);
      eventDelegate?.Invoke(output, new WaterAddition(cleanMoved, badMoved));
    }
  }
}