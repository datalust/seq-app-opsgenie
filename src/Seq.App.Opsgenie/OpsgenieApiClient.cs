using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Seq.App.Opsgenie
{
    class OpsgenieApiClient : IOpsgenieApiClient, IDisposable
    {
        const string OpsgenieCreateAlertUrl = "https://api.opsgenie.com/v2/alerts";
        
        readonly HttpClient _httpClient = new HttpClient();
        readonly Encoding _utf8Encoding = new UTF8Encoding(false);
        readonly JsonSerializerOptions _serializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        public OpsgenieApiClient(string apiKey)
        {
            if (apiKey == null) throw new ArgumentNullException(nameof(apiKey));
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("GenieKey", apiKey);
        }

        public async Task CreateAsync(OpsgenieAlert alert)
        {
            if (alert == null) throw new ArgumentNullException(nameof(alert));
            
            var content = new StringContent(
                JsonSerializer.Serialize(alert, _serializerOptions),
                _utf8Encoding,
                "application/json");

            var response = await _httpClient.PostAsync(OpsgenieCreateAlertUrl, content);

            // Any exception here will propagate back to the host and be surfaced in the app's diagnostic output.
            response.EnsureSuccessStatusCode();
        }
        
        public void Dispose()
        {
            _httpClient.Dispose();
        }
    }
}