using System.Collections.Generic;
using System.Threading.Tasks;
using Seq.App.Opsgenie.Api;
using Seq.App.Opsgenie.Classes;
using Seq.App.Opsgenie.Client;

namespace Seq.App.Opsgenie.Tests.Support
{
    class TestOpsgenieApiClient : IOpsgenieApiClient
    {
        public List<OpsgenieAlert> CreatedAlerts { get; } = new List<OpsgenieAlert>();


        public Task<OpsGenieResult> CreateAsync(OpsgenieAlert alert)
        {
            CreatedAlerts.Add(alert);
            return (Task<OpsGenieResult>) Task.CompletedTask;
        }
    }
}