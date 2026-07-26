using System.Threading;
using System.Threading.Tasks;

namespace NebulaRaid.Resources
{
    public interface IResourceLoader<T> where T : class
    {
        Task<T> LoadAsync(string key, CancellationToken cancellationToken);
    }
}

