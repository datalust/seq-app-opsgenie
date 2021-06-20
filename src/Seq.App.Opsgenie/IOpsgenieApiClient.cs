using System.Net.Http;
using System.Threading.Tasks;

namespace Seq.App.Opsgenie
{
    interface IOpsgenieApiClient
    {
        Task<HttpResponseMessage> CreateAsync(OpsgenieAlert alert);
    }
}