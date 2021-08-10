using System;
using System.Collections;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Seq.Apps;

namespace Seq.App.Opsgenie
{
    class OpsgenieApiClient : IOpsgenieApiClient, IDisposable
    {
        const string OpsgenieCreateAlertUrl = "https://api.opsgenie.com/v2/alerts";
        readonly HttpClient _httpClient = new HttpClient();
        readonly Encoding _utf8Encoding = new UTF8Encoding(false);
        static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters =
            {
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
            }
        };

        public static string Serialize(IEnumerable list)
        {
            return JsonSerializer.Serialize(list, SerializerOptions);
        }

        public static string Serialize(OpsgenieAlert alert)
        {
            return JsonSerializer.Serialize(alert, SerializerOptions);
        }

        public OpsgenieApiClient(string apiKey)
        {
            if (apiKey == null) throw new ArgumentNullException(nameof(apiKey));
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("GenieKey", apiKey);
        }

        public async Task<HttpResponseMessage> CreateAsync(OpsgenieAlert alert)
        {
            if (alert == null) throw new ArgumentNullException(nameof(alert));

            var content = new StringContent(
                JsonSerializer.Serialize(alert, SerializerOptions),
                _utf8Encoding,
                "application/json");

            var response = await _httpClient.PostAsync(OpsgenieCreateAlertUrl, content);

            if (!response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync();
                var fragment = responseBody.Substring(0, Math.Min(1024, responseBody.Length));                
                throw new SeqAppException(
                    $"Opsgenie alert creation failed ({response.StatusCode}/{response.ReasonPhrase}): {fragment}");
            }

            return response;
        }

        public void Dispose()
        {
            _httpClient.Dispose();
        }
    }
}