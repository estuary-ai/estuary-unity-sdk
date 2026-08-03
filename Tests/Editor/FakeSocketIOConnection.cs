using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Estuary.Tests
{
    /// <summary>
    /// Recording fake for wire-emission tests. Captures every EmitAsync call so
    /// tests can assert event names and typed payload objects.
    /// </summary>
    internal class FakeSocketIOConnection : ISocketIOConnection
    {
        internal class EmittedEvent
        {
            public string Name;
            public object Data;
        }

        public readonly List<EmittedEvent> Emitted = new List<EmittedEvent>();

#pragma warning disable 67  // interface events, never raised by the fake
        public event Action OnConnected;
        public event Action<string> OnDisconnected;
        public event Action<string> OnError;
#pragma warning restore 67

        public Task ConnectAsync(string url, string ns, object auth)
        {
            Emitted.Add(new EmittedEvent { Name = "__connect", Data = auth });
            return Task.CompletedTask;
        }

        public Task DisconnectAsync() => Task.CompletedTask;

        public Task EmitAsync(string eventName, object data)
        {
            Emitted.Add(new EmittedEvent { Name = eventName, Data = data });
            return Task.CompletedTask;
        }

        public void On(string eventName, Action<string> handler) { }

        public void Dispose() { }
    }
}
