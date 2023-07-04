using System.Threading.Tasks;
using Seq.App.Opsgenie.Api;

namespace Seq.App.Opsgenie.Client
{
    interface IOpsgenieApiClient
    {
        Task<OpsGenieResult> CreateAsync(OpsgenieAlert alert);
    }
}