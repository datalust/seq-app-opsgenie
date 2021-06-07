using System;
using System.Linq;
using System.Threading.Tasks;
using Seq.Apps;
using Seq.Apps.LogEvents;
using System.Text.RegularExpressions;
using System.Collections.Generic;

// ReSharper disable MemberCanBePrivate.Global, UnusedType.Global, UnusedAutoPropertyAccessor.Global

namespace Seq.App.Opsgenie
{
    [SeqApp("Opsgenie Alerting", Description = "Send Opsgenie alerts using the HTTP API.")]
    public class OpsgenieApp : SeqApp, IDisposable, ISubscribeToAsync<LogEventData>
    {
        IDisposable _disposeClient;
        HandlebarsTemplate _generateMessage, _generateDescription;
        string _priorityProperty;
        bool _isPriorityMapping = false;
        Priority _priority;
        Priority _defaultPriority;
        List<Priorities> _priorities;
        string _responderProperty;
        bool _isResponderMapping = false;
        List<Responders> _responders;
        string responder;
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
            HelpText = "Optional property to read for responder; properties can be mapped to responder type using the Responders or Property Mapping field")]
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
            _generateMessage = new HandlebarsTemplate(Host, !string.IsNullOrWhiteSpace(AlertMessage) ?
                AlertMessage :
                "{{$Message}}");

            _generateDescription = new HandlebarsTemplate(Host, !string.IsNullOrWhiteSpace(AlertDescription) ?
                AlertDescription :
                $"Generated by Seq running at {Host.BaseUri}.");

            _priorityProperty = "@Level";
            if (!string.IsNullOrEmpty(PriorityProperty))
                _priorityProperty = PriorityProperty;

            _priority = Priority.P3;
            _defaultPriority = Priority.P3;
            _priorities = new List<Priorities>();

            if (!string.IsNullOrEmpty(DefaultPriority) && Regex.IsMatch(DefaultPriority, "(^P[1-5]$)", RegexOptions.IgnoreCase))
            {
                var priorityType = Priority.P3;
                if (Enum.TryParse(DefaultPriority, true, out priorityType))
                {
                    _defaultPriority = priorityType;
                }
            }

            if (!string.IsNullOrEmpty(EventPriority) && EventPriority.Contains("="))
            {
                _isPriorityMapping = true;
                var priorityList = new List<Priorities>();
                var p = EventPriority.Split(new[] { '=' }, StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()).ToArray();
                var priorityType = Priority.P3;
                if (Enum.TryParse(p[1], true, out priorityType))
                {
                    priorityList.Add(new Priorities() { Name = p[0], Priority = priorityType });
                }
                else
                {
                    Log.Debug("Cannot parse priority type in Priority configuration '{Priority}' - cannot add these priority mappings. Will use default of '{DefaultPriority}'", 
                        EventPriority, _defaultPriority);
                    _priority = _defaultPriority;
                    _isPriorityMapping = false;
                }

                _priorities = priorityList;
            }
            else
            {
                if (!string.IsNullOrEmpty(EventPriority) && Regex.IsMatch(EventPriority, "(^P[1-5]$)", RegexOptions.IgnoreCase))
                {
                    var priorityType = Priority.P3;
                    if (Enum.TryParse(EventPriority, true, out priorityType))
                    {
                        _priority = priorityType;
                    }
                }
                else
                {
                    Log.Debug("Priority configuration '{EventPriority}' not matched - will use default of '{Priority}'", EventPriority, _defaultPriority);
                    _priority = _defaultPriority;
                }
            }

            _responderProperty = String.Empty;
            if (!string.IsNullOrEmpty(ResponderProperty))
            {
                _responderProperty = ResponderProperty;
                _isResponderMapping = true;
            }

            var responderList = new List<Responders>();
            if (!string.IsNullOrEmpty(Responders))
            {
                foreach (string responder in Responders.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()))
                {
                    if (responder.Contains("="))
                    {
                        var r = responder.Split(new[] { '=' }, StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()).ToArray();
                        var responderType = ResponderType.Team;
                        if (Enum.TryParse(r[1], true, out responderType))
                        {
                            responderList.Add(new Responders() { Name = r[0], Type = responderType });
                        }
                        else
                        {
                            Log.Debug("Cannot parse responder type in Responder configuration '{Responder}' - cannot add this responder", responder);
                        }
                    }
                    else
                    {
                        responderList.Add(new Responders() { Name = responder, Type = ResponderType.Team });
                    }
                }
            }

            _responders = responderList;
            responder = OpsgenieApiClient.Serialize(_responders);

            _tags = (Tags ?? "")
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .ToArray();

            _includeTags = AddEventTags;
            _includeTagProperty = "Tags";
            if (!string.IsNullOrEmpty(AddEventProperty))
                _includeTagProperty = AddEventProperty;

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

            List<string> tagList = _tags.ToList<string>();
            List<Responders> responderList = new List<Responders>();

            //Perform property matching if required
            if (_includeTags || _isPriorityMapping || _isResponderMapping)
            {
                bool matchTag = false;
                bool matchPriority = false;
                bool matchResponder = false;                

                if (_isPriorityMapping && _priorityProperty.Equals("@Level", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (Priorities p in _priorities)
                    {
                        if ((evt.Data.Level.ToString()).Equals(p.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            matchPriority = true;
                            _priority = p.Priority;
                            break;
                        }
                    }
                }

                //Case insensitive property name match
                foreach (KeyValuePair<string, object> property in evt.Data.Properties)
                {   
                    //Match the tag property if configured
                    if (_includeTags && !matchTag && property.Key.Equals(_includeTagProperty, StringComparison.OrdinalIgnoreCase))
                    {
                        matchTag = true;
                        var tag = evt.Data.Properties[_includeTagProperty];
                        if (tag.GetType().IsArray)
                        {
                            foreach (object p in (object[])tag)
                            {
                                if (!string.IsNullOrEmpty((string)p) && !tagList.Contains((string)p, StringComparer.OrdinalIgnoreCase))
                                {
                                    tagList.Add(((string)p).Trim());
                                }
                                else if (!string.IsNullOrEmpty((string)p) && !tagList.Contains((string)p, StringComparer.OrdinalIgnoreCase))
                                {
                                    tagList.AddRange(((string)tag).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()));
                                }
                            }                            
                        }

                        //Exit loop if the other match iterations are satisfied
                        if (matchTag &&
                            ((_isPriorityMapping && matchPriority && _isResponderMapping && matchResponder) ||
                            (!_isPriorityMapping && _isResponderMapping && matchResponder) ||
                            (_isPriorityMapping && matchPriority && !_isResponderMapping) ||
                            (!_isPriorityMapping && !_isResponderMapping)))
                        {
                            break;
                        }
                    }

                    //Match the Priority property if configured
                    if (_isPriorityMapping && !matchPriority && property.Key.Equals(_priorityProperty, StringComparison.OrdinalIgnoreCase))
                    {
                        var priority = evt.Data.Properties[_includeTagProperty];
                        foreach (Priorities p in _priorities)
                        {
                            if (((string)priority).Equals(p.Name, StringComparison.OrdinalIgnoreCase))
                            {
                                matchPriority = true;
                                _priority = p.Priority;
                                break;
                            }
                        }

                        //Exit loop if the other match iterations are satisfied
                        if (matchPriority &&
                            ((_includeTags && matchTag && _isResponderMapping && matchResponder) ||
                            (!_includeTags && _isResponderMapping && matchResponder) ||
                            (!_isResponderMapping && _includeTags && matchTag) ||
                            (!_includeTags && !_isPriorityMapping)))
                        {
                            break;
                        }

                    }

                    //Match the Responder property if configured
                    if (_isResponderMapping && !matchResponder && property.Key.Equals(_responderProperty, StringComparison.OrdinalIgnoreCase))
                    {
                        var responder = evt.Data.Properties[_responderProperty];
                        foreach (Responders p in _responders)
                        {
                            if (((string)responder).Equals(p.Name, StringComparison.OrdinalIgnoreCase))
                            {
                                matchResponder = true;
                                responderList.Add(p);
                                break;
                            }
                        }

                        //Exit loop if the other match iterations are satisfied
                        if (matchResponder &&
                            ((_isPriorityMapping && matchPriority && _includeTags && matchTag) ||
                            (!_isPriorityMapping && _includeTags && matchTag) ||
                            (!_includeTags && _isPriorityMapping && matchPriority) ||
                            (!_isPriorityMapping && !_includeTags)))
                        {
                            break;
                        }
                    }
                }

                if (_isPriorityMapping && !matchPriority)
                {
                    _priority = _defaultPriority;
                }
            }

            try
            {
                if (!_isResponderMapping)
                    responderList = _responders;


                //Log our intent to alert OpsGenie with details that could be re-fired to another app if needed
                Log.Debug("Send Alert to OpsGenie: Message {Message}, Description {Description}, Priority {Priority}, Responders {Responders}, Tags {Tags}", _generateMessage.Render(evt), _generateDescription.Render(evt),
                    _priority, responder, tagList.ToArray());

                //Logging the API call helps with debugging "why" an alert did not fire or was rejected by OpsGenie API
                Log.Debug("OpsGenie API call: {JsonCall}", OpsgenieApiClient.Serialize(new OpsgenieAlert(_generateMessage.Render(evt),
                        evt.Id,
                        _generateDescription.Render(evt),
                        _priority.ToString(),
                        responderList,
                        Host.BaseUri,                        
                        tagList.ToArray())));

                var result = await ApiClient.CreateAsync(new OpsgenieAlert(
                        _generateMessage.Render(evt),
                        evt.Id,
                        _generateDescription.Render(evt),
                        _priority.ToString(),
                        responderList,
                        Host.BaseUri,
                        tagList.ToArray()));

                //Log the result with details that could be re-fired to another app if needed
                Log.Debug("OpsGenie Result: Result {Result}/{Reason}, Message {Message}, Description {Description}, Priority {Priority}, Responders {Responders}, Tags {Tags}", result.StatusCode, result.ReasonPhrase, 
                    _generateMessage.Render(evt), _generateDescription.Render(evt), _priority, responder, tagList.ToArray());
            }

            catch (Exception ex)
            {
                //Log an error which could be fired to another app (eg. alert via email of an OpsGenie alert failure, or raise a Jira) and include details of the alert
                Log.Error(ex, "OpsGenie Exception: Result {Result}, Message {Message}, Description {Description}, Priority {Priority}, Responders {Responders}, Tags {Tags}", ex.Message, _generateMessage.Render(evt), _generateDescription.Render(evt),
                    _priority, responder, tagList.ToArray());
            }
        }

        public void Dispose()
        {
            _disposeClient?.Dispose();
        }
    }
}
