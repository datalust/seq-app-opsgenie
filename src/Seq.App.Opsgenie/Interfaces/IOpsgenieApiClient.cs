using System.Threading.Tasks;
using Seq.App.Opsgenie.Classes;

namespace Seq.App.Opsgenie.Interfaces
{
    interface IOpsgenieApiClient
    {
        Task<OpsGenieResult> CreateAsync(OpsgenieAlert alert);
    }
}