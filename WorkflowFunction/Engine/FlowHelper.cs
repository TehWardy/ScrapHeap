using Core.Objects;
using Core.Objects.Entities.Workflow;
using Newtonsoft.Json;
using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace Workflow
{
    internal static class FlowHelper
    {
        public static async Task<FlowInstance> Get(Guid flowId, string apiRoot, string auth, LogEvent log)
        {
            using var api = Api(apiRoot);
            if (auth != null) api.WithAuthToken(auth);

            var def = JsonConvert.DeserializeObject<FlowDefinition>(await api.GetStringAsync($"Core/FlowDefinition({flowId})"), ObjectExtensions.JSONSettings);
            return new FlowInstance(def, log);
        }

        public static async Task<FlowInstance> GetInstance(Guid instanceId, string apiRoot, string auth, LogEvent log)
        {
            using var api = Api(apiRoot);
            if (auth != null) api.WithAuthToken(auth);

            var def = JsonConvert.DeserializeObject<FlowInstanceData>(await api.GetStringAsync($"Core/FlowInstanceData({instanceId})"), ObjectExtensions.JSONSettings);
            return new FlowInstance(def, log);
        }

        public static async Task SaveResult(FlowInstanceData result, string apiRoot, string auth)
        {
            using var api = Api(apiRoot);
            if (auth != null)  api.WithAuthToken(auth);

            var r = await api.PutAsJsonAsync($"Core/FlowInstanceData({result.Id})", result);
            r.EnsureSuccessStatusCode();
        }

        private static HttpClient Api(string apiBase)
            => new HttpClient(new HttpClientHandler() { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate }) { BaseAddress = new Uri(apiBase) };

    }
}