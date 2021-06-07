using System;
using System.Linq;
using System.Threading.Tasks;
using Seq.Apps;
using Seq.Apps.LogEvents;
using System.Collections.Generic;

// ReSharper disable MemberCanBePrivate.Global, UnusedType.Global, UnusedAutoPropertyAccessor.Global

namespace Seq.App.Opsgenie
{
    [SeqApp("Opsgenie Alerting", Description = "Send Opsgenie alerts using the HTTP API.")]
    public class OpsgenieApp : SeqApp, IDisposable, ISubscribeToAsync<LogEventData>
    {
        IDisposable _disposeClient;
        HandlebarsTemplate _generateMessage, _generateDescription;
        string _priorityProperty = "@Level";
        bool _isPriorityMapping;
        Priority _priority = Priority.P3, _defaultPriority = Priority.P3;
        readonly Dictionary<string, Priority> _priorities = new Dictionary<string, Priority>(StringComparer.OrdinalIgnoreCase);
        string _responderProperty;
        bool _isResponderMapping;
        readonly List<Responder> _responders = new List<Responder>();
        string[] _tags;
        bool _includeTags;
        string _includeTagProperty;

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
            HelpText = "The message associated with the alert, specified with Handlebars syntax. If blank, the message " +
                       "from the incoming event or notification will be used.")]
        public string AlertMessage { get; set; }

        [SeqAppSetting(
            IsOptional = true,
            DisplayName = "Alert description",
            HelpText = "The description associated with the alert, specified with Handlebars syntax. If blank, a default" +
                       " description will be used.")]
        public string AlertDescription { get; set; }

        [SeqAppSetting(
            IsOptional = true,
            DisplayName = "Priority Property",
            HelpText = "Optional property to read for alert priority (default @Level); properties can be mapped to OpsGenie priorities using the Alert Priority or Property Mapping field.")]
        public string PriorityProperty { get; set; }

        [SeqAppSetting(
            IsOptional = true,
            DisplayName = "Alert Priority or Property Mapping",
            HelpText = "Priority for the alert - P1, P2, P3, P4, P5 - or 'Priority Property' mapping using Property=Mapping format - Highest=P1,Error=P2,Critical=P3.")]
        public string EventPriority { get; set; }

        [SeqAppSetting(
            IsOptional = true,
            DisplayName = "Default Priority",
            HelpText = "If using Priority Property Mapping - Default Priority for alerts not matching the mapping - P1, P2, P3, P4, P5. Defaults to P3.")]
        public string DefaultPriority { get; set; }

        [SeqAppSetting(
            IsOptional = true,
            DisplayName = "Responder Property",
            HelpText = "Optional property to read for responder; properties can be mapped to responder type using the Responders or Property Mapping field.")]
        public string ResponderProperty { get; set; }

        //Defaults to team if only name is specified, but we also optionally accept name=type to allow user, escalation, and schedule to be specified
        [SeqAppSetting(
            IsOptional = true,
            DisplayName = "Responders",
            HelpText = "Responders for the alert - team name, or name=[team,user,escalation,schedule] - comma-delimited for multiple responder or property mapping.")]
        public string Responders { get; set; }

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
            HelpText = "Include tags from from an event property - comma-delimited or array accepted. Will append to existing tags.")]
        public bool AddEventTags { get; set; }

        //The property containing tags that can be added dynamically during runtime
        [SeqAppSetting(
            IsOptional = true,
            DisplayName = "Event tag property",
            HelpText = "The property that contains tags to include from events- defaults to 'Tags', only used if Include Event tags is enabled.")]
        public string AddEventProperty { get; set; }

        protected override void OnAttached()
        {
            _generateMessage = new HandlebarsTemplate(Host, !string.IsNullOrWhiteSpace(AlertMessage) ? AlertMessage : "{{$Message}}");
            _generateDescription = new HandlebarsTemplate(Host, !string.IsNullOrWhiteSpace(AlertDescription) ? AlertDescription : $"Generated by Seq running at {Host.BaseUri}.");

            if (!string.IsNullOrEmpty(PriorityProperty))
                _priorityProperty = PriorityProperty;

            if (!string.IsNullOrEmpty(DefaultPriority) && Enum.TryParse(DefaultPriority, true, out Priority defaultPriority))
            {
                _defaultPriority = defaultPriority;
            }

            if (!string.IsNullOrEmpty(EventPriority) && EventPriority.Contains("="))
            {
                if (TryParsePriorityMappings(EventPriority, out var mappings))
                {
                    _isPriorityMapping = true;
                    foreach (var mapping in mappings)
                        _priorities.Add(mapping.Key, mapping.Value);
                }
                else
                {
                    Log.Debug("Cannot parse priority type in Priority configuration '{Priority}' - cannot add these priority mappings. Will use default of '{DefaultPriority}'", EventPriority, _defaultPriority);
                    _priority = _defaultPriority;
                }
            }
            else
            {
                if (!string.IsNullOrEmpty(EventPriority) && Enum.TryParse(EventPriority, true, out Priority singlePriority))
                {
                    _priority = singlePriority;
                }
                else
                {
                    Log.Debug("Priority configuration '{EventPriority}' not matched - will use default of '{Priority}'", EventPriority, _defaultPriority);
                    _priority = _defaultPriority;
                }
            }
            
            if (!string.IsNullOrEmpty(ResponderProperty))
            {
                _responderProperty = ResponderProperty;
                _isResponderMapping = true;
            }
            
            if (!string.IsNullOrEmpty(Responders))
            {
                foreach (var responder in Responders.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()))
                {
                    if (responder.Contains("="))
                    {
                        var r = responder.Split(new[] { '=' }, StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()).ToArray();
                        if (Enum.TryParse(r[1], true, out ResponderType responderType))
                        {
                            _responders.Add(new Responder { Name = r[0], Type = responderType });
                        }
                        else
                        {
                            Log.Debug("Cannot parse responder type in Responder configuration '{Responder}' - cannot add this responder", responder);
                        }
                    }
                    else
                    {
                        _responders.Add(new Responder { Name = responder, Type = ResponderType.Team });
                    }
                }
            }

            _tags = (Tags ?? "")
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .ToArray();

            _includeTags = AddEventTags;
            _includeTagProperty = "Tags";
            if (!string.IsNullOrEmpty(AddEventProperty))
            {
                _includeTagProperty = AddEventProperty;
            }

            if (ApiClient == null)
            {
                var client = new OpsgenieApiClient(ApiKey);
                ApiClient = client;
                _disposeClient = client;
            }
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
                log.ForContext("Alert", alert, destructureObjects: true)
                    .Debug("Sending alert to OpsGenie API");

                var result = await ApiClient.CreateAsync(alert);

                //Log the result with details that could be re-fired to another app if needed
                log.Debug("OpsGenie API responded {Result}/{Reason}", result.StatusCode, result.ReasonPhrase);
            }

            catch (Exception ex)
            {
                //Log an error which could be fired to another app (eg. alert via email of an OpsGenie alert failure, or raise a Jira) and include details of the alert
                log
                    .ForContext("Alert", alert, destructureObjects: true)
                    .Error(ex, "OpsGenie alert creation failed");
            }
        }
        
        public void Dispose()
        {
            _disposeClient?.Dispose();
        }
        
        List<Responder> ComputeResponders(Event<LogEventData> evt)
        {
            if (!_isResponderMapping)
                return _responders;

            var result = new List<Responder>();
            
            //Match the Responder property if configured
            if (TryGetPropertyValueCI(evt.Data.Properties, _responderProperty, out var responderValue) && responderValue is string responder)
            {
                var matched = _responders.FirstOrDefault(p => responder.Equals(p.Name, StringComparison.OrdinalIgnoreCase));
                if (matched != null)
                {
                    result.Add(matched);
                }
            }

            return result;
        }

        string[] ComputeTags(Event<LogEventData> evt)
        {
            if (!_includeTags || !TryGetPropertyValueCI(evt.Data.Properties, _includeTagProperty, out var tagArrValue) ||
                !(tagArrValue is object[] tagArr))
                return _tags;

            var result = new HashSet<string>(_tags, StringComparer.OrdinalIgnoreCase);
            foreach (var p in tagArr)
            {
                if (!(p is string tags))
                    continue;
                
                result.UnionWith(tags.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()));
            }

            return result.ToArray();
        }

        internal Priority ComputePriority(Event<LogEventData> evt)
        {
            if (!_isPriorityMapping)
                return _priority;
            
            if (_priorityProperty.Equals("@Level", StringComparison.OrdinalIgnoreCase) &&
                _priorities.TryGetValue(evt.Data.Level.ToString(), out var matched))
            {
                return matched;
            }
            
            if (TryGetPropertyValueCI(evt.Data.Properties, _priorityProperty, out var priorityProperty) &&
                priorityProperty is string priorityValue &&
                _priorities.TryGetValue(priorityValue, out var matchedPriority))
            {
                return matchedPriority;
            }

            return _defaultPriority;
        }

        internal static bool TryGetPropertyValueCI(IReadOnlyDictionary<string,object> properties, string propertyName, out object propertyValue)
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
                if (kv.Length != 2 || !Enum.TryParse(kv[1], true, out Priority value))
                {
                    return false;
                }
                
                mappings.Add(kv[0].Trim(), value);
            }

            return true;
        }
    }
}
