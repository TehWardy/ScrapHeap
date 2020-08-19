using Core.Objects;
using Core.Objects.Dtos.Workflow;
using Core.Objects.Entities;
using Core.Objects.Workflow.Activities;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace Workflow
{
    /// <summary>
    /// Construct one of these to execute a task structure.
    /// The execution context is passed between tasks as they execute.
    /// It also acts as a utility class for storing process related data across tasks in a single execution
    /// </summary>
    public sealed class WorkflowContext : IWorkflowContext
    {
        public string ExecutionState { get; set; }

        public Guid InstanceId { get; set; }

        public Flow Flow { get; set; }

        public IDictionary<string, object> Variables { get; set; }

        public ICollection<WorkflowLogEntry> ExecutionLog { get; set; }

        [JsonIgnore]
        public IScriptRunner Script => Instance.Script;

        [JsonIgnore]
        internal FlowInstance Instance { get; set; }

        public WorkflowContext() => Variables = new Dictionary<string, object>()
            {
                {   // Default script imports, override in flow to change this 
                    "Imports", new[] {
                        "Core.B2B.Objects",
                        "Core.B2B.Objects.Dtos",
                        "Core.B2B.Objects.Entities",
                        "Core.B2B.Objects.Workflow.Activities",
                        "Core.Connectivity.Workflow",
                        "Core.Objects",
                        "Core.Objects.Dtos.Workflow",
                        "Core.Objects.Entities",
                        "Core.Objects.Entities.CMS",
                        "Core.Objects.Entities.DMS",
                        "Core.Objects.Entities.Planning",
                        "Core.Objects.Workflow.Activities",
                        "Newtonsoft.Json",
                        "Newtonsoft.Json.Linq",
                        "System",
                        "System.Collections.Generic",
                        "System.Linq",
                        "System.Xml.Linq",
                        "Rebex.Net"
                    }
                }
            };

        internal WorkflowContext(Flow flow, FlowInstance instance) : this()
        {
            Flow = flow;
            ExecutionLog = new List<WorkflowLogEntry>();
            InstanceId = instance.Id;
            Instance = instance;
        }

        public async Task Execute<T>(T data, string apiRoot, string authToken = null, FormSubmissionActivity<T> resumePoint = null)
        {
            Log(WorkflowLogLevel.Info, "Execution started");
            Variables.Add("AppId", Instance.AppId);
            Variables.Add("Data", data); 
            Variables.Add("Api", apiRoot);
            Variables.Add("AuthToken", authToken);
            Variables.Add("InstanceId", InstanceId.ToString());

            var api = new HttpClient(new HttpClientHandler() { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate })
                .WithAuthToken(authToken)
                .WithBaseUri(apiRoot);

            using (api)
            {
                var userDetails = await api.GetAsync<User>("Core/User/Me()");
                Instance.Caller = userDetails.Id;
                Variables.Add("UserId", userDetails.Id);
                Variables.Add("UserName", userDetails.DisplayName);
                Variables.Add("UserEmail", userDetails.Email);
            }

            try
            {
                if (resumePoint == null)
                {
                    var start = (Start)Flow.Activities.First(a => a.GetType() == typeof(Start));
                    if (authToken != null)
                        start.AuthToken = authToken;

                    start.Data = data;
                    await start.ExecuteInternal(this);
                }
                else
                {
                    if (authToken != null)
                        resumePoint.AuthToken = authToken;

                    resumePoint.Data = data;
                    await resumePoint.ExecuteInternal(this);
                }

                if (Flow.Activities.All(a => a.State == ActivityState.Complete))
                {
                    Log(WorkflowLogLevel.Info, "Execution Complete.");
                    ExecutionState = "Complete";
                }
                else
                {
                    Log(WorkflowLogLevel.Info, "Execution awaiting further input.");
                    ExecutionState = "Suspended";
                }
            }
            catch (Exception ex)
            {
                // log the failure
                Log(WorkflowLogLevel.Error, "Execution failed.");
                Log(WorkflowLogLevel.Error, ex.Message + "\n" + ex.StackTrace);

                var e = ex.InnerException;
                while (e != null)
                {
                    Log(WorkflowLogLevel.Error, ex.Message + "\n" + ex.StackTrace);
                    e = ex.InnerException;
                }

                ExecutionState = "Failed";
            }
        }

        public void Log(WorkflowLogLevel level, string message)
        {
            // add to the local log for later saving to the db later
            ExecutionLog.Add(new WorkflowLogEntry(level, message));
            Instance?.Log(level, message);
        }
    }
}