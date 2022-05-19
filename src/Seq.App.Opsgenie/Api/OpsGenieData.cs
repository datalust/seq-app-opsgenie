using System;

// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global
// ReSharper disable UnusedAutoPropertyAccessor.Global

// ReSharper disable ClassNeverInstantiated.Global

namespace Seq.App.Opsgenie.Api
{
    public class OpsGenieData
    {
        public OpsGenieData()
        {
            Success = false;
            Action = string.Empty;
            ProcessedAt = DateTime.Now;
            IntegrationId = string.Empty;
            IsSuccess = false;
            Status = string.Empty;
            AlertId = string.Empty;
            Alias = string.Empty;
        }

        public bool Success { get; set; }
        public string Action { get; set; }
        public DateTime ProcessedAt { get; set; }
        public string IntegrationId { get; set; }
        public bool IsSuccess { get; set; }
        public string Status { get; set; }
        public string AlertId { get; set; }
        public string Alias { get; set; }
    }
}