using System.Collections.Generic;
using System.Threading.Tasks;
using Seq.App.Opsgenie.Tests.Support;
using Seq.Apps.LogEvents;
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

        [Fact]
        public void PriorityMappingsCanBeParsed()
        {
            const string input = "Test=P1, Another= p2";
            Assert.True(OpsgenieApp.TryParsePriorityMappings(input, out var mappings));
            Assert.Equal(2, mappings.Count);
            Assert.Equal(Priority.P1, mappings["test"]);
            Assert.Equal(Priority.P2, mappings["another"]);
        }

        [Fact]
        public void TryGetPropertyValueCIMatchesCaseInsensitivePropertyNames()
        {
            var expected = new { };
            var properties = new Dictionary<string, object>
            {
                ["A"] = expected,
                ["b"] = new { }
            };
            
            Assert.True(OpsgenieApp.TryGetPropertyValueCi(properties, "A", out var actual));
            Assert.Equal(expected, actual);

            Assert.True(OpsgenieApp.TryGetPropertyValueCi(properties, "a", out actual));
            Assert.Equal(expected, actual);
            
            Assert.False(OpsgenieApp.TryGetPropertyValueCi(properties, "C", out _));
        }

        [Theory]
        [InlineData(LogEventLevel.Debug, Priority.P1)]
        [InlineData(LogEventLevel.Warning, Priority.P3)]
        [InlineData(LogEventLevel.Error, Priority.P2)]
        public void WhenLevelMappingIsSpecifiedPriorityIsComputed(LogEventLevel level, Priority expectedPriority)
        {
            using var app = new OpsgenieApp
            {
                ApiClient = new TestOpsgenieApiClient(),
                DefaultPriority = Priority.P2.ToString(),
                PriorityProperty = "@Level",
                EventPriority = "Warning=P3,Debug=P1"
            };
            
            app.Attach(TestAppHost.Instance);
            
            var evt = Some.LogEvent(level: level);
            var priority = app.ComputePriority(evt);

            Assert.Equal(expectedPriority, priority);
        }
        
        [Theory]
        [InlineData("A", Priority.P1)]
        [InlineData("B", Priority.P3)]
        [InlineData("C", Priority.P2)]
        [InlineData(null, Priority.P2)]
        [InlineData(5, Priority.P2)]
        public void WhenValueMappingIsSpecifiedPriorityIsComputed(object value, Priority expectedPriority)
        {
            using var app = new OpsgenieApp
            {
                ApiClient = new TestOpsgenieApiClient(),
                DefaultPriority = Priority.P2.ToString(),
                PriorityProperty = "Test",
                EventPriority = "B=P3,A=P1"
            };
            
            app.Attach(TestAppHost.Instance);
            
            var evt = Some.LogEvent(include: new Dictionary<string, object>{ ["Test"] = value});
            var priority = app.ComputePriority(evt);

            Assert.Equal(expectedPriority, priority);
        }
        
        [Fact]
        public void WhenMappedPropertyIsMissingPriorityIsDefault()
        {
            using var app = new OpsgenieApp
            {
                ApiClient = new TestOpsgenieApiClient(),
                DefaultPriority = Priority.P2.ToString(),
                PriorityProperty = "Test",
                EventPriority = "B=P3,A=P1"
            };
            
            app.Attach(TestAppHost.Instance);
            
            var evt = Some.LogEvent();
            var priority = app.ComputePriority(evt);

            Assert.Equal(Priority.P2, priority);
        }
    }
}
