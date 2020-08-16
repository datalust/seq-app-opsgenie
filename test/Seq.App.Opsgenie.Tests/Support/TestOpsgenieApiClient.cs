using System.Collections.Generic;
using System.Threading.Tasks;

namespace Seq.App.Opsgenie.Tests.Support
{
    class TestOpsgenieApiClient : IOpsgenieApiClient
    {
        public List<OpsgenieAlert> CreatedAlerts { get; } = new List<OpsgenieAlert>();


        public Task CreateAsync(OpsgenieAlert alert)
        {
            CreatedAlerts.Add(alert);
            return Task.CompletedTask;
        }
    }
}
