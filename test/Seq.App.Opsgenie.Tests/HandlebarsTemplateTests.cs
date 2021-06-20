using System.Collections.Generic;
using Seq.App.Opsgenie.Tests.Support;
using Xunit;

namespace Seq.App.Opsgenie.Tests
{
    public class HandlebarsTemplateTests
    {
        [Fact]
        public void TemplateCanRenderEventProperties()
        {
            var evt = Some.LogEvent(include: new Dictionary<string, object>{ ["Name"] = "World" });
            var template = new HandlebarsTemplate(TestAppHost.Instance.Host, "Hello, {{Name}}!");
            var rendered = template.Render(evt);
            Assert.Equal("Hello, World!", rendered);
        }
    }
}