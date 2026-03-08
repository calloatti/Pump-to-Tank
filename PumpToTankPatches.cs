using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using Timberborn.Goods;
using Timberborn.InventorySystem;
using Timberborn.WaterBuildings;
using UnityEngine;

namespace Calloatti.TankToPump
{
  [HarmonyPatch]
  public static class PumpToTankPatches
  {
    private const float VolToGood = 5.0f;
    private static readonly FieldInfo WaterAddedEventField = AccessTools.Field(typeof(WaterOutput), "WaterAdded");

    [HarmonyPatch(typeof(WaterMover), "IsWaterFlowPossible")]
    [HarmonyPostfix]
    public static void IsWaterFlowPossible_Postfix(WaterMover __instance, ref bool __result)
    {
      // If paired to a tank, we ALWAYS return true to keep the power draw constant
      if (PumpToTankManager.ActivePairs.ContainsKey(__instance))
      {
        __result = true;
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

      // Strict Mode Definitions
      bool pumpCleanOnly = __instance.CleanWaterMovement && !__instance.ContaminatedWaterMovement;
      bool pumpBadOnly = !__instance.CleanWaterMovement && __instance.ContaminatedWaterMovement;

      // 1. WATER LOGIC
      if (pumpCleanOnly && tank.Takes("Water"))
      {
        // Flush internal badwater to keep pipe blue
        if (input.ContaminatedWaterAmount > 0) input.RemoveContaminatedWater(input.ContaminatedWaterAmount);

        // Only transfer if tank has space AND river has water
        if (tank.UnreservedCapacity("Water") > 0 && input.CleanWaterAmount > 0.0001f)
          Transfer(input, output, acc, tank, "Water", waterAmount, true);
        else
          TriggerVisualizer(output, 0f, 0f); // Keep column empty but gears turning
      }
      // 2. BADWATER LOGIC
      else if (pumpBadOnly && tank.Takes("Badwater"))
      {
        // Flush internal clean water to keep pipe red
        if (input.CleanWaterAmount > 0) input.RemoveCleanWater(input.CleanWaterAmount);

        // Only transfer if tank has space AND river has badwater
        if (tank.UnreservedCapacity("Badwater") > 0 && input.ContaminatedWaterAmount > 0.0001f)
          Transfer(input, output, acc, tank, "Badwater", waterAmount, false);
        else
          TriggerVisualizer(output, 0f, 0f); // Keep column empty but gears turning
      }
      else
      {
        // Mismatch or Unfiltered: Pump runs (consumes power) but moves nothing
        TriggerVisualizer(output, 0f, 0f);
      }

      return false; // Always skip vanilla spill
    }

    private static void Transfer(WaterInput input, WaterOutput output, FractionalAccumulator acc, Inventory tank, string id, float work, bool isClean)
    {
      float available = isClean ? input.CleanWaterAmount : input.ContaminatedWaterAmount;
      float toMove = Mathf.Min(work, available);

      if (toMove <= 0) return;

      if (!acc.FluidFractions.ContainsKey(id)) acc.FluidFractions[id] = 0f;
      acc.FluidFractions[id] += toMove * VolToGood;

      if (isClean)
      {
        input.RemoveCleanWater(toMove);
        TriggerVisualizer(output, toMove, 0f);
      }
      else
      {
        input.RemoveContaminatedWater(toMove);
        TriggerVisualizer(output, 0f, toMove);
      }

      int actual = Mathf.Min(Mathf.FloorToInt(acc.FluidFractions[id]), tank.UnreservedCapacity(id));
      if (actual > 0)
      {
        tank.Give(new GoodAmount(id, actual));
        acc.FluidFractions[id] -= actual;

        string otherId = (id == "Water") ? "Badwater" : "Water";
        if (acc.FluidFractions.ContainsKey(otherId)) acc.FluidFractions[otherId] = 0f;
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