using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Seq.Apps;
using Seq.Apps.LogEvents;

// ReSharper disable MemberCanBePrivate.Global, UnusedType.Global, UnusedAutoPropertyAccessor.Global

namespace Seq.App.Opsgenie
{
    [SeqApp("Opsgenie Alerting", Description = "Send Opsgenie alerts using the HTTP API.")]
    public class OpsgenieApp : SeqApp, IDisposable, ISubscribeToAsync<LogEventData>
    {
        private readonly List<Responder> _defaultResponders = new List<Responder>();

        private readonly Dictionary<string, Priority> _priorities =
            new Dictionary<string, Priority>(StringComparer.OrdinalIgnoreCase);

        private readonly List<Responder> _responders = new List<Responder>();
        private IDisposable _disposeClient;
        private HandlebarsTemplate _generateMessage, _generateDescription;
        private string _includeTagProperty;
        private bool _includeTags;
        private bool _isPriorityMapping;
        private bool _isResponderMapping;
        private Priority _priority = Priority.P3, _defaultPriority = Priority.P3;
        private string _priorityProperty = "@Level";
        private string _responderProperty;
        private string[] _tags;

        // Permits substitution for testing.
        internal IOpsgenieApiClient ApiClient { get; set; }

        [SeqAppSetting(
            DisplayName = "API key",
            HelpText = "The Opsgenie API key to use.",
            InputType = SettingInputType.Password)]
        public string ApiKey { get; set; }

        [SeqAppSetting(
            IsOptional = true,
            DisplayName = "Alert message",
            HelpText =
                "The message associated with the alert, specified with Handlebars syntax. If blank, the message " +
                "from the incoming event or notification will be used.")]
        public string AlertMessage { get; set; }

        [SeqAppSetting(
            IsOptional = true,
            DisplayName = "Alert description",
            HelpText =
                "The description associated with the alert, specified with Handlebars syntax. If blank, a default" +
                " description will be used.")]
        public string AlertDescription { get; set; }

        [SeqAppSetting(
            IsOptional = true,
            DisplayName = "Priority Property",
            HelpText =
                "Optional property to read for alert priority (default @Level); properties can be mapped to OpsGenie priorities using the Alert Priority or Property Mapping field.")]
        public string PriorityProperty { get; set; }

        [SeqAppSetting(
            IsOptional = true,
            DisplayName = "Alert Priority or Property Mapping",
            HelpText =
                "Priority for the alert - P1, P2, P3, P4, P5 - or 'Priority Property' mapping using Property=Mapping format - Highest=P1,Error=P2,Critical=P3.")]
        public string EventPriority { get; set; }

        [SeqAppSetting(
            IsOptional = true,
            DisplayName = "Default Priority",
            HelpText =
                "If using Priority Property Mapping - Default Priority for alerts not matching the mapping - P1, P2, P3, P4, P5. Defaults to P3.")]
        public string DefaultPriority { get; set; }

        [SeqAppSetting(
            IsOptional = true,
            DisplayName = "Responder Property",
            HelpText =
                "Optional property to read for responder; properties can be mapped to responder type using the Responders or Property Mapping field.")]
        public string ResponderProperty { get; set; }

        //Defaults to team if only name is specified, but we also optionally accept name=type to allow user, escalation, and schedule to be specified
        [SeqAppSetting(
            IsOptional = true,
            DisplayName = "Responders or Property Mapping",
            HelpText =
                "Responders for the alert - team name, or name=[team,user,escalation,schedule] - comma-delimited for multiple responder or property mapping.")]
        public string Responders { get; set; }

        [SeqAppSetting(
            IsOptional = true,
            DisplayName = "Default Responders",
            HelpText =
                "If using Responder Property Mapping - Default Responder names for alerts not matching the mapping. Comma-delimited for multiple responders.")]
        public string DefaultResponders { get; set; }

        //Static list of tags
        [SeqAppSetting(
            IsOptional = true,
            DisplayName = "Alert tags",
            HelpText = "Tags for the alert, separated by commas.")]
        public string Tags { get; set; }

        //Optionally allow dynamic tags from an event property
        [SeqAppSetting(
            IsOptional = true,
            DisplayName = "Include event tags",
            HelpText =
                "Include tags from from an event property - comma-delimited or array accepted. Will append to existing tags.")]
        public bool AddEventTags { get; set; }

        //The property containing tags that can be added dynamically during runtime
        [SeqAppSetting(
            IsOptional = true,
            DisplayName = "Event tag property",
            HelpText =
                "The property that contains tags to include from events- defaults to 'Tags', only used if Include Event tags is enabled.")]
        public string AddEventProperty { get; set; }

        public void Dispose()
        {
            _disposeClient?.Dispose();
        }

        public async Task OnAsync(Event<LogEventData> evt)
        {
            if (evt == null) throw new ArgumentNullException(nameof(evt));

            var message = _generateMessage.Render(evt);
            var description = _generateDescription.Render(evt);
            var priority = ComputePriority(evt);
            var tags = ComputeTags(evt);
            var responders = ComputeResponders(evt);

            var alert = new OpsgenieAlert(
                message,
                evt.Id,
                description,
                priority.ToString(),
                responders,
                Host.BaseUri,
                tags);

            var log = Log.ForContext("Notification", Guid.NewGuid().ToString("n"));
            try
            {
                // Log our intent to alert OpsGenie with details that could be re-fired to another app if needed
                log.ForContext("Alert", alert, true)
                    .Debug("Sending alert to OpsGenie API");

                var result = await ApiClient.CreateAsync(alert);

                //Log the result with details that could be re-fired to another app if needed
                log.Debug("OpsGenie API responded {Result}/{Reason}", result.StatusCode, result.ReasonPhrase);
            }

            catch (Exception ex)
            {
                //Log an error which could be fired to another app (eg. alert via email of an OpsGenie alert failure, or raise a Jira) and include details of the alert
                log
                    .ForContext("Alert", alert, true)
                    .Error(ex, "OpsGenie alert creation failed");
            }
        }

        protected override void OnAttached()
        {
            _generateMessage = new HandlebarsTemplate(Host,
                !string.IsNullOrWhiteSpace(AlertMessage) ? AlertMessage : "{{$Message}}");
            _generateDescription = new HandlebarsTemplate(Host,
                !string.IsNullOrWhiteSpace(AlertDescription)
                    ? AlertDescription
                    : $"Generated by Seq running at {Host.BaseUri}.");

            if (!string.IsNullOrEmpty(PriorityProperty))
                _priorityProperty = PriorityProperty;

            if (!string.IsNullOrEmpty(DefaultPriority) &&
                Enum.TryParse(DefaultPriority, true, out Priority defaultPriority)) _defaultPriority = defaultPriority;

            switch (string.IsNullOrEmpty(EventPriority))
            {
                case false when EventPriority.Contains("="):
                {
                    if (TryParsePriorityMappings(EventPriority, out var mappings))
                    {
                        _isPriorityMapping = true;
                        foreach (var mapping in mappings)
                            _priorities.Add(mapping.Key, mapping.Value);
                        Log.ForContext("Priority", _priorities, true).Debug("Priority Mappings: {Priority}");
                    }
                    else
                    {
                        Log.ForContext("Priority", _defaultPriority).Debug(
                            "Cannot parse priority type in Priority configuration '{EventPriority}' - cannot add these priority mappings. Will use default of '{Priority}'",
                            EventPriority);
                        _priority = _defaultPriority;
                    }

                    break;
                }
                case false when Enum.TryParse(EventPriority, true, out Priority singlePriority):
                    _priority = singlePriority;
                    Log.ForContext("Priority", _priority).Debug("Priority: {Priority}");
                    break;
                default:
                    Log.ForContext("Priority", _defaultPriority).Debug(
                        "Priority configuration '{EventPriority}' not matched - will use default of '{Priority}'",
                        EventPriority);
                    _priority = _defaultPriority;
                    break;
            }

            //Only map responders to a property if a valid responder mapping and default responders exist
            if (!string.IsNullOrEmpty(ResponderProperty) && !string.IsNullOrEmpty(Responders) &&
                !string.IsNullOrEmpty(DefaultResponders))
            {
                _responderProperty = ResponderProperty;
                _isResponderMapping = true;
                Log.ForContext("ResponderProperty", _responderProperty)
                    .Debug("Map Responder Property: {ResponderProperty}");
            }
            else if (!string.IsNullOrEmpty(ResponderProperty) || !string.IsNullOrEmpty(DefaultResponders))
            {
                Log.Debug(
                    "Responder Property Mapping not performed, Responder Property set: {ResponderProperty}, Responders set: {Responders}, Default Responders set: {DefaultResponders}",
                    !string.IsNullOrEmpty(ResponderProperty), !string.IsNullOrEmpty(Responders),
                    !string.IsNullOrEmpty(DefaultResponders));
            }

            if (!string.IsNullOrEmpty(Responders))
            {
                foreach (var responder in Responders.Split(new[] {','}, StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.Trim()))
                    if (responder.Contains("="))
                    {
                        var r = responder.Split(new[] {'='}, StringSplitOptions.RemoveEmptyEntries)
                            .Select(t => t.Trim()).ToArray();
                        if (Enum.TryParse(r[1], true, out ResponderType responderType))
                            _responders.Add(responderType == ResponderType.User
                                ? new Responder {Username = r[0], Type = responderType}
                                : new Responder {Name = r[0], Type = responderType});
                        else
                            Log.Debug(
                                "Cannot parse responder type in Responder configuration '{Responder}' - cannot add this responder",
                                responder);
                    }
                    else
                    {
                        //Unmatched Name=Type defaults to Team
                        _responders.Add(new Responder {Name = responder, Type = ResponderType.Team});
                    }

                Log.ForContext("Responders", _responders, true)
                    .Debug(_isResponderMapping ? "Responder Mappings: {Responders}" : "Responders: {Responders}");
            }
            else
            {
                Log.Debug("No Responders specified, responders will not be passed to OpsGenie");
            }

            // Assign default responders where they can be matched
            if (_isResponderMapping && !string.IsNullOrEmpty(DefaultResponders))
            {
                _defaultResponders.AddRange(
                    from r in DefaultResponders.Split(new[] {','}, StringSplitOptions.RemoveEmptyEntries)
                        .Select(t => t.Trim()).ToArray()
                    from p in _responders
                    where r.Equals(p.Name, StringComparison.OrdinalIgnoreCase) ||
                          r.Equals(p.Username, StringComparison.OrdinalIgnoreCase)
                    select p);

                Log.ForContext("DefaultResponders", _defaultResponders, true)
                    .Debug("Default Responders: {DefaultResponders}");
            }

            _tags = (Tags ?? "")
                .Split(new[] {','}, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .ToArray();

            _includeTags = AddEventTags;
            _includeTagProperty = "Tags";
            if (!string.IsNullOrEmpty(AddEventProperty)) _includeTagProperty = AddEventProperty;
            Log.ForContext("Tags", _tags).ForContext("IncludeTags", _includeTags)
                .ForContext("IncludeEventTags", _includeTagProperty)
                .Debug("Tags: {Tags}, IncludeTags: {IncludeTags}, Include Event Tags: {IncludeEventTags}");

            if (ApiClient != null) return;
            var client = new OpsgenieApiClient(ApiKey);
            ApiClient = client;
            _disposeClient = client;
        }

        private List<Responder> ComputeResponders(Event<LogEventData> evt)
        {
            if (!_isResponderMapping)
                return _responders;

            var result = new List<Responder>();

            //Match the Responder property if configured
            if (!TryGetPropertyValueCi(evt.Data.Properties, _responderProperty, out var responderValue)) return result;
            switch (responderValue)
            {
                case object[] responderArr:
                    result.AddRange(responderArr.Select(r =>
                            _responders.FirstOrDefault(p =>
                                ((string) r).Equals(p.Name, StringComparison.OrdinalIgnoreCase) ||
                                ((string) r).Equals(p.Username, StringComparison.OrdinalIgnoreCase)))
                        .Where(matched => matched != null));
                    break;
                case string responder when responder.Contains(","):
                    result.AddRange(
                        from r in responder.Split(new[] {','}, StringSplitOptions.RemoveEmptyEntries)
                            .Select(t => t.Trim()).ToArray()
                        from p in _responders
                        where r.Equals(p.Name, StringComparison.OrdinalIgnoreCase) ||
                              r.Equals(p.Username, StringComparison.OrdinalIgnoreCase)
                        select p);
                    break;
                case string responder:
                {
                    var matched = _responders.FirstOrDefault(p =>
                        responder.Equals(p.Name, StringComparison.OrdinalIgnoreCase) ||
                        responder.Equals(p.Username, StringComparison.OrdinalIgnoreCase));
                    if (matched != null) result.Add(matched);

                    break;
                }
            }

            return result.Count.Equals(0) ? _defaultResponders : result;
        }

        private string[] ComputeTags(Event<LogEventData> evt)
        {
            if (!_includeTags ||
                !TryGetPropertyValueCi(evt.Data.Properties, _includeTagProperty, out var tagArrValue) ||
                !(tagArrValue is object[] tagArr))
                return _tags;

            var result = new HashSet<string>(_tags, StringComparer.OrdinalIgnoreCase);
            foreach (var p in tagArr)
            {
                if (!(p is string tags))
                    continue;

                result.UnionWith(tags.Split(new[] {','}, StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()));
            }

            return result.ToArray();
        }

        internal Priority ComputePriority(Event<LogEventData> evt)
        {
            if (!_isPriorityMapping)
                return _priority;

            if (_priorityProperty.Equals("@Level", StringComparison.OrdinalIgnoreCase) &&
                _priorities.TryGetValue(evt.Data.Level.ToString(), out var matched))
                return matched;

            if (TryGetPropertyValueCi(evt.Data.Properties, _priorityProperty, out var priorityProperty) &&
                priorityProperty is string priorityValue &&
                _priorities.TryGetValue(priorityValue, out var matchedPriority))
                return matchedPriority;

            return _defaultPriority;
        }

        internal static bool TryGetPropertyValueCi(IReadOnlyDictionary<string, object> properties, string propertyName,
            out object propertyValue)
        {
            var pair = properties.FirstOrDefault(p => p.Key.Equals(propertyName, StringComparison.OrdinalIgnoreCase));
            if (pair.Key == null)
            {
                propertyValue = null;
                return false;
            }

            propertyValue = pair.Value;
            return true;
        }

        internal static bool TryParsePriorityMappings(string encodedMappings, out Dictionary<string, Priority> mappings)
        {
            if (encodedMappings == null) throw new ArgumentNullException(nameof(encodedMappings));
            mappings = new Dictionary<string, Priority>(StringComparer.OrdinalIgnoreCase);
            var pairs = encodedMappings.Split(new[] {','}, StringSplitOptions.RemoveEmptyEntries);
            foreach (var pair in pairs)
            {
                var kv = pair.Split(new[] {'='}, StringSplitOptions.RemoveEmptyEntries);
                if (kv.Length != 2 || !Enum.TryParse(kv[1], true, out Priority value)) return false;

                mappings.Add(kv[0].Trim(), value);
            }

            return true;
        }
    }
}