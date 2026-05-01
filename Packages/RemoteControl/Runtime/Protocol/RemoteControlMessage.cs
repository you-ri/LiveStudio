using System;
using UnityEngine;

namespace Lilium.RemoteControl.Protocol
{
    [Serializable]
    public class RemoteControlMessage
    {
        public string type;
        public string data;
        public long timestamp;
        
        public RemoteControlMessage(string type, string data = null)
        {
            this.type = type;
            this.data = data;
            this.timestamp = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        }
    }
    
    [Serializable]
    public class PingMessage : RemoteControlMessage
    {
        public PingMessage() : base("ping") { }
    }
    
    [Serializable]
    public class PongMessage : RemoteControlMessage
    {
        public long originalTimestamp;
        
        public PongMessage(long originalTimestamp) : base("pong")
        {
            this.originalTimestamp = originalTimestamp;
        }
    }
    
    [Serializable]
    public class StatusMessage : RemoteControlMessage
    {
        public StatusData status;
        
        public StatusMessage(StatusData status) : base("status")
        {
            this.status = status;
        }
    }
    
    [Serializable]
    public class StatusData
    {
        public bool isConnected;
        public int connectionCount;
        public float fps;
        public string version;
    }
    
    [Serializable]
    public class CalibrationMessage : RemoteControlMessage
    {
        public CalibrationData calibration;
        
        public CalibrationMessage(CalibrationData calibration) : base("calibration")
        {
            this.calibration = calibration;
        }
    }
    
    [Serializable]
    public class CalibrationData
    {
        public string status;
        public bool isCalibrating;
        public float progress;
    }
    
    [Serializable]
    public class CommandMessage : RemoteControlMessage
    {
        public string command;
        public string[] parameters;
        
        public CommandMessage(string command, params string[] parameters) : base("command")
        {
            this.command = command;
            this.parameters = parameters;
        }
    }
    
    [Serializable]
    public class ErrorMessage : RemoteControlMessage
    {
        public string error;
        public string details;
        
        public ErrorMessage(string error, string details = null) : base("error")
        {
            this.error = error;
            this.details = details;
        }
    }
}