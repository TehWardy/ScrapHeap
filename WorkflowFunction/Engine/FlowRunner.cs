using Core.Objects;
using Core.Objects.Dtos.Workflow;
using Core.Objects.Entities.Workflow;
using Microsoft.AspNetCore.SignalR.Client;
using Newtonsoft.Json;
using System;
using System.Threading.Tasks;

namespace Workflow
{
    public delegate Task LogEvent(WorkflowLogLevel level, string message);

    public class FlowRunner
    {
        HubConnection con;

        public async Task Run(WorkflowRequest msg)
        {
            await ConnectToHub(msg);

            try
            {
                await Log(WorkflowLogLevel.Info, "Request received by workflow, processing ...", msg.InstanceId);
                await Log(WorkflowLogLevel.Debug, msg.ToJson(ObjectExtensions.JSONSettings, Formatting.Indented), msg.InstanceId);

                // construct the flow instance
                var instance = msg.ResumeFrom == null
                    ? await FlowHelper.Get(msg.FlowId, msg.Api, msg.AuthToken, (WorkflowLogLevel level, string message) => Log(level, message, msg.InstanceId))
                    : await FlowHelper.GetInstance(msg.InstanceId, msg.Api, msg.AuthToken, (WorkflowLogLevel level, string message) => Log(level, message, msg.InstanceId));

                // execute the flow 
                await ExecuteRequest(msg, instance);
            }
            catch (Exception ex)
            {
                await Log(WorkflowLogLevel.Fatal, $"Failed to process request, abandoning execution\n{ex.Message}\n{ex.StackTrace}", msg.InstanceId);
            }
            finally
            {
                await Log(WorkflowLogLevel.Info, "Done!", msg.InstanceId);
            }
        }

        async Task ConnectToHub(WorkflowRequest msg)
        {
            try
            {
                // connect to the signalr hub on the source API
                con = new HubConnectionBuilder().WithUrl(msg.Api + "Hubs/Workflow").Build();
                con.On<Exception>("error", (ex) => Console.WriteLine(ex.Message + "\n" + ex.StackTrace));
                await con.StartAsync();
                await Log(WorkflowLogLevel.Info, $"Workflow Instance {msg.InstanceId} Connected.", msg.InstanceId);
            }
            catch (Exception ex)
            {
                await con.DisposeAsync();
                con = null;
                await Log(WorkflowLogLevel.Error, "Failed to connect to the hub because: " + ex.Message, msg.InstanceId);
            }
        }

        async Task ExecuteRequest(WorkflowRequest msg, FlowInstance instance)
        {
            var result = await (Task<FlowInstanceData>)instance
                .GetType()
                .GetMethod("Execute")
                .MakeGenericMethod(msg.GetType().GenericTypeArguments)
                .Invoke(instance, new[] { msg });

            await FlowHelper.SaveResult(result, msg.Api, msg.AuthToken);
        }

        public async Task Log(WorkflowLogLevel level, string message, Guid instanceId)
        {
            if (message.Length > 4000)
                message = $"{message.Substring(0, 1900)} ... {message.Length - 1900} characters cut due to excessive length.";

            Console.WriteLine($"{level}:: {message}");

            try
            {
                if (con != null) await con.InvokeAsync("ConsoleSend", level.ToString().ToLower(), message, instanceId.ToString());
            }
            catch(Exception ex)
            {
                await con.DisposeAsync();
                con = null;
                await Log(WorkflowLogLevel.Error, ex.Message, instanceId);
                await Log(level, message, instanceId); 
            }

            Console.ForegroundColor = ConsoleColor.White;
        }
    }
}