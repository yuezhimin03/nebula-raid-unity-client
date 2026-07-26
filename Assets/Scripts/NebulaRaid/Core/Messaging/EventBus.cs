using System;
using System.Collections.Generic;

namespace NebulaRaid.Messaging
{
    /// <summary>
    /// Synchronous, ordered event bus. Handlers run in subscription order on the
    /// publishing thread. Subscription changes during Publish affect the next publish.
    /// </summary>
    public sealed class EventBus
    {
        private readonly object _gate = new object();
        private readonly Dictionary<Type, List<Subscription>> _subscriptions =
            new Dictionary<Type, List<Subscription>>();

        public IDisposable Subscribe<T>(Action<T> handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            Subscription subscription = new Subscription(this, typeof(T), handler);
            lock (_gate)
            {
                if (!_subscriptions.TryGetValue(typeof(T), out List<Subscription>? handlers))
                {
                    handlers = new List<Subscription>();
                    _subscriptions.Add(typeof(T), handlers);
                }

                handlers.Add(subscription);
            }

            return subscription;
        }

        public void Publish<T>(T message)
        {
            Subscription[] snapshot;
            lock (_gate)
            {
                if (!_subscriptions.TryGetValue(typeof(T), out List<Subscription>? handlers)
                    || handlers.Count == 0)
                {
                    return;
                }

                snapshot = handlers.ToArray();
            }

            for (int i = 0; i < snapshot.Length; i++)
            {
                ((Action<T>)snapshot[i].Handler)(message);
            }
        }

        private void Remove(Subscription subscription)
        {
            lock (_gate)
            {
                if (!_subscriptions.TryGetValue(subscription.MessageType, out List<Subscription>? handlers))
                {
                    return;
                }

                handlers.Remove(subscription);
                if (handlers.Count == 0)
                {
                    _subscriptions.Remove(subscription.MessageType);
                }
            }
        }

        private sealed class Subscription : IDisposable
        {
            private readonly EventBus _owner;
            private bool _isDisposed;

            public Subscription(EventBus owner, Type messageType, Delegate handler)
            {
                _owner = owner;
                MessageType = messageType;
                Handler = handler;
            }

            public Type MessageType { get; }

            public Delegate Handler { get; }

            public void Dispose()
            {
                lock (_owner._gate)
                {
                    if (_isDisposed)
                    {
                        return;
                    }

                    _isDisposed = true;
                    _owner.Remove(this);
                }
            }
        }
    }
}
