using Core.Objects;
using Core.Objects.Dtos.Workflow;
using Core.Objects.Entities.Workflow;
using Core.Objects.Workflow.Activities;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Workflow
{
    public sealed class FlowInstance
    {
        public Guid Id { get; set; }

        public int AppId { get; }

        public string Caller { get; set; }

        public string Name { get; }
        public string FlowDefinition { get; }
        internal Flow Flow { get; private set; }
        public WorkflowContext Context { get; private set; }


        public DateTimeOffset Start { get; set; }
        public DateTimeOffset End { get; set; }

        public event LogEvent OnLog;
        internal IScriptRunner Script { get; private set; }

        private class AssemblyComparer : IEqualityComparer<Assembly>
        {
            public bool Equals(Assembly x, Assembly y) => x.FullName == y.FullName;
            public int GetHashCode(Assembly obj) => base.GetHashCode();
        }

        private FlowInstance(LogEvent log) => Script = new ScriptRunner(log);

        /// <summary>
        /// Constructs a flow instance from a name and a flow definition string
        /// </summary>
        /// <param name="name"></param>
        /// <param name="definition"></param>
        public FlowInstance(FlowDefinition def, LogEvent log) : this(log)
        {
            OnLog += log;
            Name = def.Name;
            FlowDefinition = def.ToJson();
            Flow = def.GetFlow();
            AppId = def.AppId;
            Stitch();
        }

        /// <summary>
        /// Constructs a FlowInstance from raw data from a db
        /// </summary>
        /// <param name="instanceData"></param>
        public FlowInstance(FlowInstanceData instanceData, LogEvent log) : this(log)
        {
            OnLog += log;
            Id = instanceData.Id;
            Name = instanceData.Name;
            Start = instanceData.Start;
            Context = JsonConvert.DeserializeObject<WorkflowContext>(instanceData.ContextJson, ObjectExtensions.JSONSettings);
            FlowDefinition = Context.Flow.ToJson();
            Flow = Context.Flow;
            Stitch();
        }

        public async Task Log(WorkflowLogLevel level, string message)
        {
            try
            {
                await OnLog?.Invoke(level, message);
            }
            catch (Exception ex)
            {
                OnLog = null;
                Context.Log(WorkflowLogLevel.Warning, "Realtime processing has failed:\n" + ex.Message + "\nLog streaming has been disabled.");
            }
        }

        public async Task<FlowInstanceData> Execute<T>(WorkflowRequest<T> request)
        {
            Start = DateTimeOffset.UtcNow;
            Id = request.InstanceId;
            Context = new WorkflowContext(Flow, this);

            if (request.ResumeFrom == null)
                await Context.Execute(request.Data, request.Api, request.AuthToken);
            else
                await Resume(request);

            return Complete();
        }

        private Task Resume<T>(WorkflowRequest<T> request)
        {
            var resumePoint = (FormSubmissionActivity<T>)Flow.Activities.FirstOrDefault(a => a.Ref == request.ResumeFrom);
            resumePoint.Data = request.Data;
            return Context.Execute(request.Data, request.Api, request.AuthToken, resumePoint);
        }

        private FlowInstanceData Complete()
        {
            var flowDef = JsonConvert.DeserializeObject<FlowDefinition>(FlowDefinition, ObjectExtensions.JSONSettings);
            return new FlowInstanceData
            {
                Id = Id,
                Name = Name,
                Caller = Caller,
                FlowDefinitionId = flowDef.Id,
                ContextJson = Context.ToJson(),
                State = Context.ExecutionState,
                Start = Start,
                End = DateTimeOffset.UtcNow
            };
        }

        private async void Stitch()
        {
            foreach (var activity in Flow.Activities)
            {
                try
                {
                    var links = Flow.Links.Where(l => l.Destination == activity.Ref).Select(l => l.Source).ToArray();
                    activity.Previous = Flow.Activities.Where(a => links.Contains(a.Ref)).ToArray();
                }
                catch (Exception ex)
                {
                    await Log(WorkflowLogLevel.Error, $"Problem in previous activity selection for activity {activity.Ref}:\n{ex.Message}\n{ex.StackTrace}");
                }
            }

            foreach (var activity in Flow.Activities)
            {
                try
                {
                    activity.Next = Flow.Activities.Where(a => a.Previous?.Contains(activity) ?? false).ToArray();
                }
                catch (Exception ex)
                {
                    await Log(WorkflowLogLevel.Error, $"Problem in next activity selection for activity {activity.Ref}:\n{ex.Message}\n{ex.StackTrace}");
                }

                try
                {
                    activity.AssignCode = BuildAssign(activity, Flow);
                }
                catch (Exception ex)
                {
                    await Log(WorkflowLogLevel.Error, $"Problem in one or more links for activity {activity.Ref}:\n{ex.Message}\n{ex.StackTrace}");
                }
            }
        }

        private string BuildAssign(Activity activity, Flow flow)
        {
            var assigns = activity.Previous.Select(source =>
            {
                var link = flow.Links.First(l => l.Source == source.Ref && l.Destination == activity.Ref);
                var sourceType = source.GetType().GetCSharpTypeName();
                var destType = activity.GetType().GetCSharpTypeName();
                return string.IsNullOrEmpty(link.Expression?.Trim())
                    ? null
                    : $"//LINK:: {source.Ref} => {activity.Ref}\n" + link.Expression
                        .Replace("destination.", $"(({destType})activity).")
                        .Replace("source.", $"flow.GetActivity<{sourceType}>(\"{source.Ref}\").");
            })
                .Where(i => i != null)
                .ToArray();

            var body = $"\t{string.Join(";\n\t", assigns)}";

            // the complete block as a single "Action<Activity>" that can be called
            if (assigns.Any()) return $"(activity, variables, flow) => {{\n{body}\n}}";
            return null;
        }
    }
}