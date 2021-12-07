using System.Threading.Tasks;

using TeamServer.Models;

namespace TeamServer.Modules
{
    public class CoreModule : Module
    {
        public override string Name => "Core";

        public override async Task Execute(DroneMetadata metadata, DroneTaskUpdate update)
        {
            var drone = Server.GetDrone(metadata.Guid);
            var task = drone?.GetTask(update.TaskGuid);
            
            task?.UpdateStatus((DroneTask.TaskStatus)update.Status);
            task?.UpdateResult(update.Result);

            // send message to hub
            switch (update.Status)
            {
                case DroneTaskUpdate.TaskStatus.Running:
                    await MessageHub.Clients.All.DroneTaskRunning(metadata, update);
                    break;
                
                case DroneTaskUpdate.TaskStatus.Complete:
                    await MessageHub.Clients.All.DroneTaskComplete(metadata, update);
                    break;
                
                case DroneTaskUpdate.TaskStatus.Cancelled:
                    await MessageHub.Clients.All.DroneTaskCancelled(metadata, update);
                    break;
                
                case DroneTaskUpdate.TaskStatus.Aborted:
                    await MessageHub.Clients.All.DroneTaskAborted(metadata, update);
                    break;
            }
        }
    }
}