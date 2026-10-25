using Microsoft.Extensions.Caching.Hybrid;

namespace FindThatBook.Eval.Hosting;

/// <summary>
/// A HybridCache that never stores anything: every GetOrCreate runs the factory. Swapped in for stability runs, so
/// repeated identical calls really reach the LLM / Open Library. Observed cache hits are still reported per call, so if
/// an adapter caches somewhere else the stability report shows it instead of silently measuring the cache.
/// </summary>
public sealed class PassThroughHybridCache : HybridCache
{
    public override ValueTask<T> GetOrCreateAsync<TState, T>(string key, TState state, Func<TState, CancellationToken, ValueTask<T>> factory,
        HybridCacheEntryOptions? options = null, IEnumerable<string>? tags = null, CancellationToken cancellationToken = default)
        => factory(state, cancellationToken);

    public override ValueTask SetAsync<T>(string key, T value, HybridCacheEntryOptions? options = null, IEnumerable<string>? tags = null,
        CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    public override ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    public override ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
}
