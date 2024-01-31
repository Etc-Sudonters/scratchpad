using System.Runtime.CompilerServices;

using var cts = new CancellationTokenSource();
using var http = new HttpClient();
var caller = new Caller
{
    Client = http,
    Uri = new Uri(args[0]),
};

await LongPoller.Poll(caller.Call, cts.Token)
    .DelayBetween(TimeSpan.FromSeconds(3))
    .Take(10)
    .SelectAwaitWithCancellation(static async (msg, ct) =>
    {
        using var _ = msg;
        using var content = msg.Content;
        return await msg.Content.ReadAsStringAsync(ct);
    }).ForEachAsync(str => Console.WriteLine(str));

Console.WriteLine("no more calls");


file static class LongPoller
{
    public delegate ValueTask<T> Produce<T>(CancellationToken ct);

    public static IAsyncEnumerable<T> Poll<T>(this Produce<T> produce, CancellationToken ct)
    {
        LongPoller<T> poller = new(produce);
        return poller.Poll(ct);
    }
}

file class LongPoller<T>(LongPoller.Produce<T> produce)
{
    public async IAsyncEnumerable<T> Poll([EnumeratorCancellation] CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var item = await produce(ct);
            yield return item;
        }
    }
}


file static class AsyncEnumerableExtensions
{
    public static IAsyncEnumerable<T> DelayBetween<T>(this IAsyncEnumerable<T> enumer, TimeSpan delay) =>
        enumer.SelectAwaitWithCancellation(async (t, token) =>
        {
            await Task.Delay(delay, token);
            return t;
        });
}

 file class Caller
{
    public required HttpClient Client { get; init; }
    public required Uri Uri { get; init; }

    public async ValueTask<HttpResponseMessage> Call(CancellationToken ct) =>
        await Client.SendAsync(new HttpRequestMessage
        {
            Content = new StringContent("ping"),
            Method = HttpMethod.Post,
            RequestUri = Uri,
        }, ct);
}
