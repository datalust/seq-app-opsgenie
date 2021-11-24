// ReSharper disable UnusedMember.Global

namespace Seq.App.Opsgenie.Classes
{
    public class OpsGenieResponse
    {
        public OpsGenieResponse()
        {
            Result = string.Empty;
            RequestId = string.Empty;
            Took = -1;
        }

        public OpsGenieData Data { get; set; }
        public string Result { get; set; }
        public string RequestId { get; set; }
        public decimal Took { get; set; }
    }
}