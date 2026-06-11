using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using Timberborn.BlockSystem;
using Timberborn.Goods;
using Timberborn.InventorySystem;
using Timberborn.Particles;
using Timberborn.Stockpiles;
using Timberborn.WaterBuildings;
using Timberborn.WaterBuildingsUI;
using UnityEngine;

namespace Calloatti.TankToPump
{
  [HarmonyPatch]
  public static class IntegratedPumpPatches
  {
    private const float VolToGood = 5.0f;
    private const float WorldUnitsPerGood = 0.2f;
    private static readonly FieldInfo WaterAddedEventField = AccessTools.Field(typeof(WaterOutput), "WaterAdded");
    private static readonly FieldInfo ParticlesRunnerField = AccessTools.Field(typeof(WaterMoverParticleController), "_particlesRunner");

    private static string GetConfiguredFluid(WaterMover pump)
    {
      if (pump.CleanWaterMovement && !pump.ContaminatedWaterMovement) return "Water";
      if (!pump.CleanWaterMovement && pump.ContaminatedWaterMovement) return "Badwater";
      return null;
    }

    [HarmonyPatch(typeof(WaterMover), "IsWaterFlowPossible")]
    [HarmonyPostfix]
    public static void IsWaterFlowPossible_Postfix(WaterMover __instance, ref bool __result)
    {
      bool isPumpToTank = PumpToTankManager.ActivePairs.TryGetValue(__instance, out var toTank);
      bool isPumpFromTank = PumpFromTankManager.ActivePairs.TryGetValue(__instance, out var fromTank);

      if (!isPumpToTank && !isPumpFromTank) return;

      string configuredFluid = GetConfiguredFluid(__instance);

      if (configuredFluid == null)
      {
        __result = false;
        return;
      }

      if (isPumpToTank && !toTank.Takes(configuredFluid))
      {
        __result = false;
        return;
      }

      if (isPumpFromTank && !fromTank.Takes(configuredFluid))
      {
        __result = false;
        return;
      }

      if (isPumpToTank && isPumpFromTank)
      {
        bool hasFluid = fromTank.UnreservedAmountInStock(configuredFluid) > 0;
        if (!hasFluid && PumpFromTankManager.Accumulators.TryGetValue(__instance, out var acc))
        {
          if (acc.FluidFractions.TryGetValue(configuredFluid, out float f) && f > 0.001f) hasFluid = true;
        }
        __result = hasFluid;
      }
      else if (isPumpToTank)
      {
        __result = true;
      }
      else if (isPumpFromTank)
      {
        bool hasFluid = fromTank.UnreservedAmountInStock(configuredFluid) > 0;
        if (!hasFluid && PumpFromTankManager.Accumulators.TryGetValue(__instance, out var acc))
        {
          if (acc.FluidFractions.TryGetValue(configuredFluid, out float f) && f > 0.001f) hasFluid = true;
        }
        __result = hasFluid;
      }
    }

    [HarmonyPatch(typeof(WaterMover), "MoveWater")]
    [HarmonyPrefix]
    public static bool MoveWater_Prefix(WaterMover __instance, float waterAmount)
    {
      bool isPumpToTank = PumpToTankManager.ActivePairs.TryGetValue(__instance, out var toTank);
      bool isPumpFromTank = PumpFromTankManager.ActivePairs.TryGetValue(__instance, out var fromTank);

      if (!isPumpToTank && !isPumpFromTank) return true;

      string configuredFluid = GetConfiguredFluid(__instance);
      if (configuredFluid == null) return false;

      if (isPumpToTank && isPumpFromTank)
      {
        if (!toTank.Takes(configuredFluid) || !fromTank.Takes(configuredFluid)) return false;

        var output = __instance.GetComponent<WaterOutput>();
        var accFrom = PumpFromTankManager.Accumulators[__instance];
        var accTo = PumpToTankManager.Accumulators[__instance];

        if (!accFrom.FluidFractions.ContainsKey(configuredFluid)) accFrom.FluidFractions[configuredFluid] = 0f;

        while (accFrom.FluidFractions[configuredFluid] < waterAmount)
        {
          if (fromTank.UnreservedAmountInStock(configuredFluid) >= 1)
          {
            fromTank.TakeExisting(new GoodAmount(configuredFluid, 1));
            accFrom.FluidFractions[configuredFluid] += WorldUnitsPerGood;
          }
          else break;
        }

        float accVol = Mathf.Max(0f, (1f / VolToGood) - (accTo.FluidFractions.TryGetValue(configuredFluid, out float f) ? f / VolToGood : 0f));
        float availableSpace = (toTank.UnreservedCapacity(configuredFluid) / VolToGood) + accVol;

        float amountToMove = Mathf.Max(Mathf.Min(waterAmount, accFrom.FluidFractions[configuredFluid], availableSpace), 0f);

        if (amountToMove > 0f)
        {
          accFrom.FluidFractions[configuredFluid] -= amountToMove;

          if (!accTo.FluidFractions.ContainsKey(configuredFluid)) accTo.FluidFractions[configuredFluid] = 0f;
          accTo.FluidFractions[configuredFluid] += amountToMove * VolToGood;

          ProcessInventory(toTank, accTo, configuredFluid);

          if (configuredFluid == "Water") TriggerVisualizer(output, amountToMove, 0f);
          else TriggerVisualizer(output, 0f, amountToMove);
        }
        return false;
      }

      if (isPumpToTank)
      {
        if (!toTank.Takes(configuredFluid)) return false;

        var input = __instance.GetComponent<WaterInput>();
        var output = __instance.GetComponent<WaterOutput>();
        var acc = PumpToTankManager.Accumulators[__instance];

        float cleanScaler = GetCleanMovementScaler(__instance, input);
        float num = waterAmount * cleanScaler;
        float num2 = waterAmount - num;

        float num3 = 0f;
        float num4 = 0f;

        if (configuredFluid == "Water")
        {
          float accVol = Mathf.Max(0f, (1f / VolToGood) - (acc.FluidFractions.TryGetValue("Water", out float fw) ? fw / VolToGood : 0f));
          float cleanAvailableSpace = (toTank.UnreservedCapacity("Water") / VolToGood) + accVol;
          num3 = Mathf.Max(Mathf.Min(num, input.DemandCleanWaterAmount(num), cleanAvailableSpace), 0f);
          if (num3 > 0f) input.RemoveCleanWater(num3);
        }
        else if (configuredFluid == "Badwater")
        {
          float accVol = Mathf.Max(0f, (1f / VolToGood) - (acc.FluidFractions.TryGetValue("Badwater", out float fb) ? fb / VolToGood : 0f));
          float badAvailableSpace = (toTank.UnreservedCapacity("Badwater") / VolToGood) + accVol;
          num4 = Mathf.Max(Mathf.Min(num2, input.DemandContaminatedWaterAmount(num2), badAvailableSpace), 0f);
          if (num4 > 0f) input.RemoveContaminatedWater(num4);
        }

        if (num3 > 0f || num4 > 0f) TriggerVisualizer(output, num3, num4);

        if (num3 > 0f)
        {
          if (!acc.FluidFractions.ContainsKey("Water")) acc.FluidFractions["Water"] = 0f;
          acc.FluidFractions["Water"] += num3 * VolToGood;
          ProcessInventory(toTank, acc, "Water");
        }
        if (num4 > 0f)
        {
          if (!acc.FluidFractions.ContainsKey("Badwater")) acc.FluidFractions["Badwater"] = 0f;
          acc.FluidFractions["Badwater"] += num4 * VolToGood;
          ProcessInventory(toTank, acc, "Badwater");
        }

        return false;
      }

      if (isPumpFromTank)
      {
        if (!fromTank.Takes(configuredFluid)) return false;

        var output = __instance.GetComponent<WaterOutput>();
        var acc = PumpFromTankManager.Accumulators[__instance];

        if (!acc.FluidFractions.ContainsKey(configuredFluid)) acc.FluidFractions[configuredFluid] = 0f;

        while (acc.FluidFractions[configuredFluid] < waterAmount)
        {
          if (fromTank.UnreservedAmountInStock(configuredFluid) >= 1)
          {
            fromTank.TakeExisting(new GoodAmount(configuredFluid, 1));
            acc.FluidFractions[configuredFluid] += WorldUnitsPerGood;
          }
          else break;
        }

        float amountToPump = Mathf.Min(waterAmount, acc.FluidFractions[configuredFluid]);

        if (amountToPump > 0f)
        {
          if (configuredFluid == "Water") output.AddWater(amountToPump, 0f);
          else output.AddWater(0f, amountToPump);

          acc.FluidFractions[configuredFluid] -= amountToPump;
        }

        return false;
      }

      return true;
    }

    [HarmonyPatch(typeof(WaterMoverParticleController), "Tick")]
    [HarmonyPostfix]
    public static void ParticleController_Tick_Postfix(WaterMoverParticleController __instance)
    {
      var mover = __instance.GetComponent<WaterMover>();
      if (PumpToTankManager.ActivePairs.TryGetValue(mover, out var tank))
      {
        string configuredFluid = GetConfiguredFluid(mover);

        if (configuredFluid == null || !tank.Takes(configuredFluid))
        {
          var runner = ParticlesRunnerField.GetValue(__instance) as ParticlesRunner;
          runner?.Stop();
          return;
        }

        bool hasCapacity = tank.UnreservedCapacity(configuredFluid) > 0;

        if (!hasCapacity)
        {
          var runner = ParticlesRunnerField.GetValue(__instance) as ParticlesRunner;
          runner?.Stop();
        }
      }
    }

    [HarmonyPatch(typeof(WaterInputPipeCoordinates), "IsTileOccupied")]
    [HarmonyPostfix]
    public static void IsTileOccupied_Postfix(WaterInputPipeCoordinates __instance, Vector3Int coordinates, ref bool __result, IBlockService ____blockService)
    {
      if (!__result) return;

      var mover = __instance.GetComponent<WaterMover>();
      if (mover == null) return;

      if (coordinates.z < 0) return;

      IEnumerable<BlockObject> objects = ____blockService.GetObjectsAt(coordinates);

      foreach (var obj in objects)
      {
        if (obj.GameObject == __instance.GameObject) continue;

        var s = obj.GetComponent<Stockpile>();
        if (s != null && s.WhitelistedGoodType == "Liquid")
        {
          if (coordinates.z >= obj.CoordinatesAtBaseZ.z)
          {
            __result = false;
            return;
          }
        }
      }
    }

    [HarmonyPatch(typeof(WaterInputPipe), "InitializeEntity")]
    [HarmonyPostfix]
    public static void WaterInputPipe_Initialize_Postfix(WaterInputPipe __instance)
    {
      Traverse.Create(__instance).Method("UpdatePipe").GetValue();
    }

    [HarmonyPatch(typeof(WaterInput), "RemoveCleanWater")]
    [HarmonyPrefix]
    public static bool RemoveCleanWater_Prefix(WaterInput __instance)
    {
      var mover = __instance.GetComponent<WaterMover>();
      if (mover == null) return true;
      return !PumpFromTankManager.ActivePairs.ContainsKey(mover);
    }

    [HarmonyPatch(typeof(WaterInput), "RemoveContaminatedWater")]
    [HarmonyPrefix]
    public static bool RemoveContaminatedWater_Prefix(WaterInput __instance)
    {
      var mover = __instance.GetComponent<WaterMover>();
      if (mover == null) return true;
      return !PumpFromTankManager.ActivePairs.ContainsKey(mover);
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
      int toGive = Mathf.Min(Mathf.FloorToInt(acc.FluidFractions[id] + 0.001f), tank.UnreservedCapacity(id));
      if (toGive > 0)
      {
        tank.GiveExisting(new GoodAmount(id, toGive));
        acc.FluidFractions[id] -= (float)toGive;
      }
    }

    private static void TriggerVisualizer(WaterOutput output, float cleanMoved, float badMoved)
    {
      if (WaterAddedEventField == null) return;
      var eventDelegate = (EventHandler<WaterAddition>)WaterAddedEventField.GetValue(output);
      eventDelegate?.Invoke(output, new WaterAddition(cleanMoved, badMoved));
    }

    [HarmonyPatch(typeof(WaterOutputParticleLength), "UpdateLifetime")]
    [HarmonyPrefix]
    public static bool UpdateLifetime_Prefix(WaterOutputParticleLength __instance, ref ParticleSystem.MainModule ____particlesMainModule)
    {
      var mover = __instance.GetComponent<WaterMover>();
      if (mover != null && PumpToTankManager.CustomParticleLengths.TryGetValue(mover, out float customLifetime))
      {
        // 1.1 PHYSICS FIX: Calculate lifetime precisely using the speed multiplier
        ____particlesMainModule.startLifetime = customLifetime * (1f / ____particlesMainModule.startSpeedMultiplier);
        return false;
      }
      return true;
    }
  }
}