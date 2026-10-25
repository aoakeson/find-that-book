namespace FindThatBook.Api.Tests.Support;

/// <summary>One host per test class with the pipeline faked. Tests in a class run one at a time, so Reset() is enough.</summary>
public sealed class FakeSearchApi : IAsyncLifetime
{
    public FakeBookSearch Search { get; } = new();
    public ApiFactory Api { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;

    public ValueTask InitializeAsync()
    {
        Api = new ApiFactory { Search = Search };
        Client = Api.Client();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await Api.DisposeAsync();

    /// <summary>Forget earlier calls and go back to answering "none".</summary>
    public FakeSearchApi Reset()
    {
        Search.Calls.Clear();
        Api.Logs.Entries.Clear();
        Search.Handler = (q, _, _) => Task.FromResult(Outcomes.None(q));
        return this;
    }
}
