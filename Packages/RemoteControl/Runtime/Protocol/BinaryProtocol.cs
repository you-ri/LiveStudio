using System;
using System.Text;
using UnityEngine;

namespace Lilium.RemoteControl.Protocol
{
    public static class BinaryProtocol
    {
        public enum MessageType : byte
        {
            Text = 0,
            MotionCapture = 1,
            Image = 2,
            Audio = 3,
            Custom = 255
        }
        
        public struct ProtocolMessage
        {
            public MessageType MessageType;
            public byte[] Payload;
        }
        
        public struct ParsedProtocolMessage
        {
            public MessageType MessageType;
            public byte[] Payload;
            public string TextData;
        }
        
        /// <summary>
        /// Encode text data (JSON) in binary protocol format
        /// Protocol: [4 bytes: payload size (little endian)] [1 byte: message type] [payload]
        /// </summary>
        public static byte[] EncodeTextMessage(string textData)
        {
            var payloadBytes = Encoding.UTF8.GetBytes(textData);
            return EncodeMessage(MessageType.Text, payloadBytes);
        }
        
        /// <summary>
        /// Encode binary data in binary protocol format
        /// </summary>
        public static byte[] EncodeBinaryMessage(byte[] binaryData)
        {
            return EncodeMessage(MessageType.MotionCapture, binaryData);
        }
        
        /// <summary>
        /// Encode image data in binary protocol format
        /// </summary>
        public static byte[] EncodeImageMessage(byte[] imageData)
        {
            return EncodeMessage(MessageType.Image, imageData);
        }
        
        /// <summary>
        /// Encode message with specified type in binary protocol format
        /// </summary>
        private static byte[] EncodeMessage(MessageType messageType, byte[] payload)
        {
            var payloadSize = payload.Length;
            var totalSize = 4 + 1 + payloadSize; // 4 bytes (size) + 1 byte (type) + payload
            
            var buffer = new byte[totalSize];
            
            // First 4 bytes: payload size (little endian)
            BitConverter.GetBytes(payloadSize).CopyTo(buffer, 0);
            
            // Next 1 byte: message type
            buffer[4] = (byte)messageType;
            
            // Remaining data: payload
            Array.Copy(payload, 0, buffer, 5, payloadSize);
            
            return buffer;
        }
        
        /// <summary>
        /// Decode binary protocol format data
        /// </summary>
        public static ParsedProtocolMessage? DecodeMessage(byte[] data)
        {
            if (data == null || data.Length < 5)
            {
                Debug.LogError("[RemoteControl] Invalid message: too short");
                return null;
            }
            
            // First 4 bytes: payload size (little endian)
            var payloadSize = BitConverter.ToInt32(data, 0);
            
            if (payloadSize < 0 || payloadSize > data.Length - 5)
            {
                Debug.LogError($"[RemoteControl] Invalid payload size: {payloadSize}");
                return null;
            }
            
            // Next 1 byte: message type
            var messageType = (MessageType)data[4];
            
            // Extract payload
            var payload = new byte[payloadSize];
            Array.Copy(data, 5, payload, 0, payloadSize);
            
            var result = new ParsedProtocolMessage
            {
                MessageType = messageType,
                Payload = payload,
                TextData = null
            };
            
            // Decode text if message type is text
            if (messageType == MessageType.Text)
            {
                try
                {
                    result.TextData = Encoding.UTF8.GetString(payload);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[RemoteControl] Failed to decode text: {ex.Message}");
                }
            }
            
            return result;
        }
        
        /// <summary>
        /// Check if data is valid binary protocol message
        /// </summary>
        public static bool IsValidMessage(byte[] data)
        {
            if (data == null || data.Length < 5)
                return false;
            
            var payloadSize = BitConverter.ToInt32(data, 0);
            return payloadSize >= 0 && payloadSize == data.Length - 5;
        }
        
        /// <summary>
        /// Get message type from binary data without full decoding
        /// </summary>
        public static MessageType? GetMessageType(byte[] data)
        {
            if (data == null || data.Length < 5)
                return null;
            
            return (MessageType)data[4];
        }
    }
}