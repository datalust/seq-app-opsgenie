namespace Seq.App.Opsgenie
{
    class OpsgenieAlert
    {
        public string Message { get; }
        public string Alias { get; }
        public string Description { get; }
        public string Source { get; }
        public string[] Tags { get; }

        public OpsgenieAlert(
            string message, 
            string alias, 
            string description,
            string source,
            string[] tags)
        {
            Message = message;
            Alias = alias;
            Description = description;
            Source = source;
            Tags = tags;
        }
    }
}