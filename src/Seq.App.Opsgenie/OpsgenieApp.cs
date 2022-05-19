using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Seq.App.Opsgenie.Api;
using Seq.App.Opsgenie.Classes;
using Seq.App.Opsgenie.Client;
using Seq.Apps;
using Seq.Apps.LogEvents;

// ReSharper disable StructuredMessageTemplateProblem

// ReSharper disable MemberCanBePrivate.Global, UnusedType.Global, UnusedAutoPropertyAccessor.Global

namespace Seq.App.Opsgenie
{
    [SeqApp("Opsgenie Alerting", Description = "Send Opsgenie alerts using the HTTP API.")]
    public class OpsgenieApp : SeqApp, IDisposable, ISubscribeToAsync<LogEventData>
    {
        readonly List<Responder> _defaultResponders = new List<Responder>();

        readonly Dictionary<string, Priority> _priorities =
            new Dictionary<string, Priority>(StringComparer.OrdinalIgnoreCase);

        readonly List<Responder> _responders = new List<Responder>();
        IDisposable _disposeClient;
        HandlebarsTemplate _generateMessage, _generateDescription;
        string _includeTagProperty;
        bool _includeTags;
        bool _isPriorityMapping;
        bool _isResponderMapping;
        DateTime _lastAlert;
        Priority _priority = Priority.P3, _defaultPriority = Priority.P3;
        string _priorityProperty = "@Level";
        string _responderProperty;
        string[] _tags;

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
            InputType = SettingInputType.LongText,
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
            var tags = ComputeTags(evt, _includeTags, _includeTagProperty, _tags);
            var responders = ComputeResponders(evt);
            var notification = Guid.NewGuid().ToString("n");

            var alert = new OpsgenieAlert(
                message,
                evt.Id,
                description,
                priority.ToString(),
                responders,
                new Dictionary<string, string>
                {
                    {"Seq Host Name", Host.InstanceName},
                    {"Seq Host URL", Host.BaseUri},
                    {"Seq App Instance", App.Title},
                    {"Seq App Instance Id", App.Id},
                    {"Seq ID", evt.Id},
                    {
                        "Seq URL",
                        !string.IsNullOrEmpty(evt.Id) && !string.IsNullOrEmpty(Host.BaseUri)
                            ? string.Concat(Host.BaseUri, "#/events?filter=@Id%20%3D%20'", evt.Id,
                                "'&amp;show=expanded")
                            : ""
                    },
                    {"Seq Timestamp UTC", evt.TimestampUtc.ToString("O")},
                    {"Seq Event Type", evt.EventType.ToString()},
                    {"Seq Notification Id", notification}
                },
                Host.BaseUri,
                tags);

            var log = Log.ForContext("Notification", notification);
            var result = new OpsGenieResult();

            try
            {
                // Log our intent to alert OpsGenie with details that could be re-fired to another app if needed
                log.ForContext("Alert", alert, destructureObjects: true)
                    .Debug("Sending alert to OpsGenie API");

                result = await ApiClient.CreateAsync(alert);

                //Log the result with details that could be re-fired to another app if needed
                log
                    .ForContext("Alert", alert, destructureObjects: true)
                    .ForContext("Result", result, destructureObjects: true)
                    .Debug("OpsGenie API responded with {Response}: {ResultCode}/{Reason}", result.Response.Result,
                        result.HttpResponse.StatusCode, result.HttpResponse.ReasonPhrase);
                _lastAlert = DateTime.UtcNow;
            }

            catch (Exception ex)
            {
                //Log an error which could be fired to another app (eg. alert via email of an OpsGenie alert failure, or raise a Jira) and include details of the alert
                log
                    .ForContext("Alert", alert, destructureObjects: true)
                    .ForContext("Result", result, destructureObjects: true)
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
                Enum.TryParse(DefaultPriority, ignoreCase: true, out Priority defaultPriority))
                _defaultPriority = defaultPriority;

            switch (string.IsNullOrEmpty(EventPriority))
            {
                case false when EventPriority.Contains("="):
                {
                    if (TryParsePriorityMappings(EventPriority, out var mappings))
                    {
                        _isPriorityMapping = true;
                        foreach (var mapping in mappings)
                            _priorities.Add(mapping.Key, mapping.Value);
                        Log.ForContext("Priority", _priorities, destructureObjects: true)
                            .Debug("Priority Mappings: {Priority}");
                    }
                    else
                    {
                        Log.ForContext("Priority", _defaultPriority).Debug(
                            "Cannot parse priority type in Priority configuration '{EventPriority}' - cannot add these priority mappings; will use default of '{Priority}'",
                            EventPriority);
                        _priority = _defaultPriority;
                    }

                    break;
                }
                case false when Enum.TryParse(EventPriority, ignoreCase: true, out Priority singlePriority):
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
                foreach (var responder in SplitAndTrim(splitOn: ',', Responders))
                    if (responder.Contains("="))
                    {
                        var r = SplitAndTrim(splitOn: '=', responder).ToArray();
                        if (Enum.TryParse(r[1], ignoreCase: true, out ResponderType responderType))
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

                Log.ForContext("Responders", _responders, destructureObjects: true)
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
                    from r in SplitAndTrim(splitOn: ',', DefaultResponders)
                    from p in _responders
                    where r.Equals(p.Name, StringComparison.OrdinalIgnoreCase) ||
                          r.Equals(p.Username, StringComparison.OrdinalIgnoreCase)
                    select p);

                Log.ForContext("DefaultResponders", _defaultResponders, destructureObjects: true)
                    .Debug("Default Responders: {DefaultResponders}");
            }

            _tags = SplitAndTrim(splitOn: ',', Tags ?? "").ToArray();

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

        public List<Responder> ComputeResponders(Event<LogEventData> evt)
        {
            if (!_isResponderMapping)
                return _responders;

            var result = new List<Responder>();

            //Match the Responder property if configured
            if (!TryGetPropertyValueCI(evt.Data.Properties, _responderProperty, out var responderValue)) return result;
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
                        from r in SplitAndTrim(splitOn: ',', responder)
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

            return result.Count.Equals(obj: 0) ? _defaultResponders : result;
        }

        internal static string[] ComputeTags(Event<LogEventData> evt, bool includeTags, string includeTagProperty,
            string[] tagValues)
        {
            if (!includeTags ||
                !TryGetPropertyValueCI(evt.Data.Properties, includeTagProperty, out var tagArrValue))
                return tagValues;

            string[] tagArr;
            if (tagArrValue is string[] value)
                tagArr = value;
            else if (tagArrValue is string arrValue)
                tagArr = SplitAndTrim(splitOn: ',', arrValue).ToArray();
            else
                return tagValues;

            var result = new HashSet<string>(tagValues, StringComparer.OrdinalIgnoreCase);
            foreach (var p in tagArr)
            {
                if (!(p is string tags))
                    continue;

                result.UnionWith(SplitAndTrim(splitOn: ',', tags));
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

            if (TryGetPropertyValueCI(evt.Data.Properties, _priorityProperty, out var priorityProperty) &&
                priorityProperty is string priorityValue &&
                _priorities.TryGetValue(priorityValue, out var matchedPriority))
                return matchedPriority;

            return _defaultPriority;
        }

        internal static bool TryGetPropertyValueCI(IReadOnlyDictionary<string, object> properties, string propertyName,
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
            var pairs = SplitAndTrim(splitOn: ',', encodedMappings);
            foreach (var pair in pairs)
            {
                var kv = SplitAndTrim(splitOn: '=', pair).ToArray();
                if (kv.Length != 2 || !Enum.TryParse(kv[1], ignoreCase: true, out Priority value)) return false;

                mappings.Add(kv[0], value);
            }

            return true;
        }

        static IEnumerable<string> SplitAndTrim(char splitOn, string setting)
        {
            return setting.Split(new[] {splitOn}, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim());
        }
    }
}