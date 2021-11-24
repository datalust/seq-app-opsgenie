using System.Collections.Generic;

// Properties on this type are serialized into the JSON payload sent to OpsGenie.
// ReSharper disable MemberCanBePrivate.Global, UnusedAutoPropertyAccessor.Global

namespace Seq.App.Opsgenie.Classes
{
    class OpsgenieAlert
    {
        public OpsgenieAlert(
            string message,
            string alias,
            string description,
            string priority,
            List<Responder> responders,
            Dictionary<string, string> details,
            string source,
            string[] tags)
        {
            Message = message;
            Alias = alias;
            Description = description;
            Priority = priority;
            Responders = responders;
            Details = details;
            Source = source;
            Tags = tags;
        }

        public string Message { get; }
        public string Alias { get; }
        public string Description { get; }
        public string Priority { get; }
        public List<Responder> Responders { get; }
        public Dictionary<string, string> Details { get; }
        public string Source { get; }
        public string[] Tags { get; }
    }
}