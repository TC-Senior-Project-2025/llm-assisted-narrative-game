using System.Threading.Tasks;
using NativeWebSocket;
using Newtonsoft.Json;
using UnityEngine;

namespace Extensions
{
    public static class NativeWebSocketExtensions
    {
        private class WsMessage
        {
            public string Topic;
        }
        
        private class WsMessage<T> : WsMessage
        {
            public T Payload;
        }
        
        public static async Task SendTopic(this WebSocket webSocket, string topic)
        {
            var message = new WsMessage() { Topic = topic };
            var text = JsonConvert.SerializeObject(message);
            
            Debug.Log($"Sending topic \"{topic}\" with no payload");
            await webSocket.SendText(text);
        }
        
        public static async Task SendTopic<T>(this WebSocket webSocket, string topic, T payload)
        {
            var message = new WsMessage<T>
            {
                Topic = topic,
                Payload = payload
            };
            
            var text = JsonConvert.SerializeObject(message);
            
            Debug.Log($"Sending topic \"{topic}\": {text}");
            await webSocket.SendText(text);
        }
    }
}