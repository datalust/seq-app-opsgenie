using System;
using System.Net.Http;
// ReSharper disable UnusedAutoPropertyAccessor.Global

// ReSharper disable NotAccessedField.Global

namespace Seq.App.Opsgenie.Classes
{
    public class OpsGenieResult
    {
        public OpsGenieResult()
        {
            Ok = false;
            StatusCode = -1;
            HttpResponse = null;
            Response = null;
            Error = null;
            ResponseBody = string.Empty;
        }

        public bool Ok { get; set; }
        public int StatusCode { get; set; }
        public HttpResponseMessage HttpResponse { get; set; }
        public OpsGenieResponse Response { get; set; }
        public Exception Error { get; set; }
        public string ResponseBody { get; set; }
    }
}