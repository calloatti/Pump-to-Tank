using Bindito.Core;

namespace Calloatti.TankToPump
{
  [Context("Game")]
  public class ModConfigurator : Configurator
  {
    protected override void Configure()
    {
      Bind<PumpToTankManager>().AsSingleton();
      Bind<PumpFromTankManager>().AsSingleton();
    }
  }
}