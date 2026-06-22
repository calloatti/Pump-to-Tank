using System.Collections.Generic;
using System.Linq;
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
  public class PumpFromTankManager : IPostLoadableSingleton, IUnloadableSingleton
  {
    public static Dictionary<WaterMover, Inventory> ActivePairs = new Dictionary<WaterMover, Inventory>();
    public static Dictionary<WaterMover, FractionalAccumulator> Accumulators = new Dictionary<WaterMover, FractionalAccumulator>();

    private readonly IBlockService _blockService;
    private readonly EventBus _eventBus;
    private readonly EntityComponentRegistry _entityRegistry;

    public PumpFromTankManager(IBlockService blockService, EventBus eventBus, EntityComponentRegistry entityRegistry)
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
      foreach (var b in _entityRegistry.GetEnabled<Building>())
      {
        var p = b.GetComponent<WaterMover>();
        if (p != null) TryPairPump(p);
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
      var p = e.BlockObject.GetComponent<WaterMover>();
      if (p != null) { TryPairPump(p); return; }

      var s = e.BlockObject.GetComponent<Stockpile>();
      if (s != null && s.WhitelistedGoodType == "Liquid")
      {
        foreach (var b in _entityRegistry.GetEnabled<Building>())
        {
          var pump = b.GetComponent<WaterMover>();
          if (pump != null && !ActivePairs.ContainsKey(pump)) TryPairPump(pump);
        }
      }
    }

    [OnEvent]
    public void OnExitedFinishedState(ExitedFinishedStateEvent e)
    {
      var p = e.BlockObject.GetComponent<WaterMover>();
      if (p != null) { ActivePairs.Remove(p); Accumulators.Remove(p); return; }

      // Null safety added here as discussed
      var keys = ActivePairs
        .Where(kvp => kvp.Key != null && kvp.Value != null && kvp.Value.GameObject == e.BlockObject.GameObject)
        .Select(kvp => kvp.Key).ToList();

      foreach (var k in keys) { ActivePairs.Remove(k); Accumulators.Remove(k); }
    }

    private void TryPairPump(WaterMover pump)
    {
      var intake = pump.GetComponent<WaterInputCoordinates>();
      if (intake == null) return;

      var targetPos = intake.Coordinates;
      var target = _blockService.GetBottomObjectAt(targetPos);

      if (target != null && target.IsFinished)
      {
        var s = target.GetComponent<Stockpile>();
        if (s != null && s.WhitelistedGoodType == "Liquid")
        {
          var inventories = new List<Inventory>();
          target.GetComponents(inventories);

          var fluidInv = inventories.FirstOrDefault(i => i.Takes("Water") || i.Takes("Badwater"));

          // ADDED: The IsValidIntakeZone check prevents pairing on corners!
          if (fluidInv != null && IsValidIntakeZone(target, targetPos))
          {
            ActivePairs[pump] = fluidInv;
            Accumulators[pump] = new FractionalAccumulator();
          }
        }
      }
    }

    // ADDED: The missing corner-check method
    private bool IsValidIntakeZone(BlockObject tank, Vector3Int pos)
    {
      var coords = tank.PositionedBlocks.GetOccupiedCoordinates().ToList();
      int minX = coords.Min(c => c.x), maxX = coords.Max(c => c.x);
      int minY = coords.Min(c => c.y), maxY = coords.Max(c => c.y);

      // Ignore corners for 3x3 or larger tanks for visual consistency
      if ((maxX - minX + 1) >= 3 && (maxY - minY + 1) >= 3)
      {
        return !((pos.x == minX || pos.x == maxX) && (pos.y == minY || pos.y == maxY));
      }
      return true;
    }
  }
}