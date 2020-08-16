using System.Threading.Tasks;
using Seq.App.Opsgenie.Tests.Support;
using Xunit;

namespace Seq.App.Opsgenie.Tests
{
    public class OpsgenieAppTests
    {
        [Fact]
        public async Task AppTriggersAlerts()
        {
            var apiClient = new TestOpsgenieApiClient();
            
            using var app = new OpsgenieApp
            {
                AlertMessage = "Test",
                Tags = "First, Second, Third",
                ApiClient = apiClient
            };
            
            app.Attach(TestAppHost.Instance);

            var evt = Some.LogEvent();            
            await app.OnAsync(evt);

            var alert = Assert.Single(apiClient.CreatedAlerts);
            Assert.Equal("Test", alert!.Message);
            Assert.Equal(evt.Id, alert.Alias);
            Assert.Equal(new[] { "First", "Second", "Third" }, alert.Tags);
        }
    }
}