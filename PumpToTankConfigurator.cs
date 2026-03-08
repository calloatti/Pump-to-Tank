using Bindito.Core;
using Timberborn.SingletonSystem;

namespace Calloatti.TankToPump
{
  [Context("Game")]
  public class PumpToTankConfigurator : Configurator
  {
    protected override void Configure()
    {
      Bind<PumpToTankManager>().AsSingleton();
    }
  }
}