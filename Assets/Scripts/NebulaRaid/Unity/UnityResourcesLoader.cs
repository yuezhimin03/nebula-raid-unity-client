using System;
using System.Threading;
using System.Threading.Tasks;
using NebulaRaid.Resources;
using UnityEngine;

namespace NebulaRaid.Unity
{
    public sealed class UnityResourcesLoader<T> : IResourceLoader<T> where T : UnityEngine.Object
    {
        public Task<T> LoadAsync(string key, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ResourceRequest request = Resources.LoadAsync<T>(key);
            TaskCompletionSource<T> completion =
                new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            request.completed += _ =>
            {
                T asset = request.asset as T;
                if (asset == null)
                {
                    completion.TrySetException(
                        new InvalidOperationException("Unity resource not found: " + key));
                }
                else
                {
                    completion.TrySetResult(asset);
                }
            };
            return completion.Task;
        }
    }
}
