using System.Text.Json.Serialization;

namespace Seq.App.Opsgenie
{
    class Responder
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Username { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Name { get; set; }
        public ResponderType Type { get; set; }
    }
}
