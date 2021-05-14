﻿using System.Collections.Generic;

namespace Seq.App.Opsgenie
{
    class Responders
    {
        public string Name { get; set; }

        public string Type { get; set; }

    }

    class OpsgenieAlertWithResponders
    {
        public string Message { get; }
        public string Alias { get; }
        public string Description { get; }
        public string Priority { get; }
        public List<Responders> Responders { get; }
        public string Source { get; }
        public string[] Tags { get; }

        public OpsgenieAlertWithResponders(
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

    class OpsgenieAlert
    {
        public string Message { get; }
        public string Alias { get; }
        public string Description { get; }
        public string Priority { get; }
        public string Source { get; }
        public string[] Tags { get; }

        public OpsgenieAlert(
            string message,
            string alias,
            string description,
            string priority,
            string source,
            string[] tags)
        {
            Message = message;
            Alias = alias;
            Description = description;
            Priority = priority;
            Source = source;
            Tags = tags;
        }
    }
}