using UnityEngine;
using System;
using System.Collections.Concurrent;

namespace Infrastructure
{
    public sealed class MainThreadDispatcher : MonoBehaviour
    {
        private static readonly ConcurrentQueue<Action> _q = new();

        public static void Post(Action a) => _q.Enqueue(a);

        void Update()
        {
            while (_q.TryDequeue(out var a))
                a();
        }
    }
}