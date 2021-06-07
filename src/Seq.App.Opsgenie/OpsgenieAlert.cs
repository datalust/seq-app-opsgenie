using System.Collections.Generic;

// Properties on this type are serialized into the JSON payload sent to OpsGenie.
// ReSharper disable MemberCanBePrivate.Global, UnusedAutoPropertyAccessor.Global

namespace Seq.App.Opsgenie
{
    class OpsgenieAlert
    {
        public string Message { get; }
        public string Alias { get; }
        public string Description { get; }
        public string Priority { get; }
        public List<Responders> Responders { get; }
        public string Source { get; }
        public string[] Tags { get; }

        public OpsgenieAlert(
            string message,
            string alias,
            string description,
            string priority,
            List<Responders> responders,
            string source,
            string[] tags)
        {
            Message = message;
            Alias = alias;
            Description = description;
            Priority = priority;
            Responders = responders;
            Source = source;
            Tags = tags;
        }
    }    
}