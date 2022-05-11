﻿using ProtoBuf;

namespace TeamServer.Models;

[ProtoContract]
public class DroneTask
{
    [ProtoMember(1)]
    public string TaskId { get; set; }
    
    [ProtoMember(2)]
    public string DroneFunction { get; set; }
    
    [ProtoMember(3)]
    public string[] Parameters { get; set; }
    
    [ProtoMember(4)]
    public byte[] Artefact { get; set; }
}