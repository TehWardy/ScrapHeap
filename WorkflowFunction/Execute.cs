using Core.Objects;
using Core.Objects.Dtos.Workflow;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Newtonsoft.Json;
using System.Net.Http;
using System.Threading.Tasks;

namespace Workflow
{
    public static class Execute
    {
        [FunctionName("Execute")]
        public static async Task Run([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "Execute")]HttpRequestMessage req)
        {
            var json = await req.Content.ReadAsStringAsync();
            var request = (json.Contains("$type")
                ? JsonConvert.DeserializeObject(json, ObjectExtensions.JSONSettings)
                : JsonConvert.DeserializeObject<WorkflowRequest<dynamic>>(json)) as WorkflowRequest;

           await new FlowRunner().Run(request);
        }
    }
}