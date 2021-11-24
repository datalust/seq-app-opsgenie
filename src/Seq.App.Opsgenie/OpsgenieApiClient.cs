using System;
using System.Collections;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Seq.App.Opsgenie.Classes;
using Seq.App.Opsgenie.Interfaces;

namespace Seq.App.Opsgenie
{
    class OpsgenieApiClient : IOpsgenieApiClient, IDisposable
    {
        const string OpsgenieCreateAlertUrl = "https://api.opsgenie.com/v2/alerts";

        static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters =
            {
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
            }
        };

        readonly HttpClient _httpClient = new HttpClient();
        readonly Encoding _utf8Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        public OpsgenieApiClient(string apiKey)
        {
            if (apiKey == null) throw new ArgumentNullException(nameof(apiKey));
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("GenieKey", apiKey);
        }

        public void Dispose()
        {
            _httpClient.Dispose();
        }

        public async Task<OpsGenieResult> CreateAsync(OpsgenieAlert alert)
        {
            if (alert == null) throw new ArgumentNullException(nameof(alert));

            var content = new StringContent(
                JsonSerializer.Serialize(alert, SerializerOptions),
                _utf8Encoding,
                "application/json");

            var response = await _httpClient.PostAsync(OpsgenieCreateAlertUrl, content);
            var responseBody = await response.Content.ReadAsStringAsync();
            var result = new OpsGenieResult
            {
                StatusCode = (int) response.StatusCode,
                Ok = response.IsSuccessStatusCode,
                HttpResponse = response,
                ResponseBody = responseBody
            };

            try
            {
                var opsGenieResponse =
                    JsonSerializer.Deserialize<OpsGenieResponse>(responseBody,
                        new JsonSerializerOptions {PropertyNameCaseInsensitive = true});

                result.Response = opsGenieResponse;
            }
            catch (Exception ex)
            {
                result.Ok = false;
                result.Error = ex;
            }


            return result;
        }

        public static string Serialize(IEnumerable list)
        {
            return JsonSerializer.Serialize(list, SerializerOptions);
        }

        public static string Serialize(OpsgenieAlert alert)
        {
            return JsonSerializer.Serialize(alert, SerializerOptions);
        }
    }
}