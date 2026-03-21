using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Timberborn.BlockSystem;
using Timberborn.Buildings;
using Timberborn.EntitySystem;
using Timberborn.InventorySystem;
using Timberborn.SingletonSystem;
using Timberborn.WaterBuildings;
using Timberborn.Stockpiles;
using UnityEngine;

namespace Calloatti.TankToPump
{
  public class FractionalAccumulator
  {
    public Dictionary<string, float> FluidFractions = new Dictionary<string, float>();
  }

  public class PumpToTankManager : IPostLoadableSingleton, IUnloadableSingleton
  {
    public static Dictionary<WaterMover, Inventory> ActivePairs = new Dictionary<WaterMover, Inventory>();
    public static Dictionary<WaterMover, FractionalAccumulator> Accumulators = new Dictionary<WaterMover, FractionalAccumulator>();

    private readonly IBlockService _blockService;
    private readonly EventBus _eventBus;
    private readonly EntityComponentRegistry _entityRegistry;
    private static readonly FieldInfo TransformedCoordsField = AccessTools.Field(typeof(WaterOutput), "_waterCoordinatesTransformed");

    public PumpToTankManager(IBlockService blockService, EventBus eventBus, EntityComponentRegistry entityRegistry)
    {
      _blockService = blockService;
      _eventBus = eventBus;
      _entityRegistry = entityRegistry;
    }

    public void PostLoad()
    {
      ActivePairs.Clear();
      Accumulators.Clear();

      _eventBus.Register(this);
      foreach (var building in _entityRegistry.GetEnabled<Building>())
      {
        var pump = building.GetComponent<WaterMover>();
        if (pump != null) TryPairPump(pump);
      }
    }

    public void Unload()
    {
      _eventBus.Unregister(this);
      ActivePairs.Clear();
      Accumulators.Clear();
    }

    [OnEvent]
    public void OnEnteredFinishedState(EnteredFinishedStateEvent e)
    {
      if (e.BlockObject == null) return;

      var pump = e.BlockObject.GetComponent<WaterMover>();
      if (pump != null) { TryPairPump(pump); return; }

      var stockpile = e.BlockObject.GetComponent<Stockpile>();
      if (stockpile == null || stockpile.WhitelistedGoodType != "Liquid") return;

      var occupiedCoords = e.BlockObject.PositionedBlocks.GetOccupiedCoordinates().ToList();
      foreach (var coord in occupiedCoords)
      {
        if (!IsValidDropZone(e.BlockObject, coord)) continue;

        for (int z = coord.z + 1; z < _blockService.Size.z; z++)
        {
          var targetPos = new Vector3Int(coord.x, coord.y, z);
          var potentialPumpObject = _blockService.GetBottomObjectAt(targetPos);

          if (potentialPumpObject == null) continue;
          if (!potentialPumpObject.IsFinished) break;

          var foundPump = potentialPumpObject.GetComponent<WaterMover>();
          if (foundPump != null && !ActivePairs.ContainsKey(foundPump))
          {
            var output = foundPump.GetComponent<WaterOutput>();
            if (output != null)
            {
              // Fixed InvalidCastException by properly casting to Vector3Int
              Vector3Int nozzlePos = (Vector3Int)TransformedCoordsField.GetValue(output);
              if (nozzlePos.x == coord.x && nozzlePos.y == coord.y)
              {
                TryPairPump(foundPump);
                if (ActivePairs.ContainsKey(foundPump)) return;
              }
            }
          }
          break;
        }
      }
    }

    [OnEvent]
    public void OnExitedFinishedState(ExitedFinishedStateEvent e)
    {
      if (e.BlockObject == null) return;

      var pump = e.BlockObject.GetComponent<WaterMover>();
      if (pump != null)
      {
        ActivePairs.Remove(pump);
        Accumulators.Remove(pump);
        return;
      }

      var pumpsToUnpair = ActivePairs
        .Where(kvp => kvp.Key != null && kvp.Value != null && kvp.Value.GameObject == e.BlockObject.GameObject)
        .Select(kvp => kvp.Key).ToList();

      foreach (var p in pumpsToUnpair)
      {
        ActivePairs.Remove(p);
        Accumulators.Remove(p);
      }
    }

    private void TryPairPump(WaterMover pump)
    {
      var block = pump.GetComponent<BlockObject>();
      if (block == null || !block.IsFinished) return;

      var output = pump.GetComponent<WaterOutput>();
      if (output == null) return;

      Vector3Int nozzlePos = (Vector3Int)TransformedCoordsField.GetValue(output);

      List<Inventory> invs = new List<Inventory>();
      for (int z = nozzlePos.z - 1; z >= 0; z--)
      {
        var target = _blockService.GetBottomObjectAt(new Vector3Int(nozzlePos.x, nozzlePos.y, z));
        if (target == null || target == block) continue;
        if (!target.IsFinished) break;

        var stockpile = target.GetComponent<Stockpile>();
        if (stockpile != null && stockpile.WhitelistedGoodType == "Liquid")
        {
          invs.Clear();
          target.GetComponents<Inventory>(invs);
          Inventory fluidInv = invs.FirstOrDefault(i => i.Takes("Water") || i.Takes("Badwater"));

          if (fluidInv != null && IsValidDropZone(target, new Vector3Int(nozzlePos.x, nozzlePos.y, z)))
          {
            ActivePairs[pump] = fluidInv;
            Accumulators[pump] = new FractionalAccumulator();
            break;
          }
        }
        break;
      }
    }

    private bool IsValidDropZone(BlockObject tank, Vector3Int drop)
    {
      var coords = tank.PositionedBlocks.GetOccupiedCoordinates().ToList();
      int minX = coords.Min(c => c.x), maxX = coords.Max(c => c.x);
      int minY = coords.Min(c => c.y), maxY = coords.Max(c => c.y);
      if ((maxX - minX + 1) >= 3 && (maxY - minY + 1) >= 3)
        return !((drop.x == minX || drop.x == maxX) && (drop.y == minY || drop.y == maxY));
      return true;
    }
  }
}