using System.Threading.Tasks;

namespace Seq.App.Opsgenie
{
    interface IOpsgenieApiClient
    {
        Task CreateAsync(OpsgenieAlert alert);
    }
}