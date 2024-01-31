using System.Runtime.CompilerServices;

using var cts = new CancellationTokenSource();
using var http = new HttpClient();
var uri = new Uri(args[0]);

/* Probably better to build pipes like this rather than do any kind of
 * decoration of Call
 *
 * 50/50 on if the delegate should be a QOL override for some IPoll { blah blah
 * blah } or the primary implementation. Carrying state is hard but there's
 * nothing stopping LongPoller.Poll(someObj.Method, ct); And having the
 * interface means needing to allocate an object just to call the function I
 * could have just passed!
 */
var poller = LongPoller.Poll(Call, cts.Token)
    /*
     * 50/50 on this as well, it's probably more useful as a throttle set by
     * the creator to prevent it from being overwhelmed by unnecessary
     * requests. It could instead be Poll<T>(LongPoller<T>, TimeSpan,
     * CancellationToken) but as a primary interface it's a little _too_
     * constrained. Externalizing the delay means potentially interesting
     * things like `IThrottle` to adjust how fast messages are allowed to reach
     * the consumer -- not that it'll necessarily make them arrive that
     * quickly.
     *
     * Setting it as part of enumerable also means consumers don't need to
     * worry about remembering to cover it at every continue point and
     * accidentally create a much hotter loop than intended -- not that I've
     * done that.
     *
     * On the other hand, consumers would want to punt any kind of delay as
     * close to their `continue;` rather than idle around _before_ doing work.
     * And depending on where the delay gets put in the pipeline it might end
     * up holding on to a much heavier object than strictly necessary. 
     *
     * Compare:
     * Delay(...).SelectAwait(Read) vs SelectAwait(Read).Delay(...) 
     * Task<string> Read(HttpResponseMessage, CancellationToken);
     *
     * In the first case, the HttpResponseMessage sits around in our Delay
     * method for the entire delay, in the second we've processed and disposed
     * the response message already and now are just holding onto a string.
     *
     * It also means that the item is that much older. We didn't wait thirty
     * seconds _and then_ produce a value; we produced a value and then held it
     * up for 30 seconds which is an eternity.
     *
     * So 50/50. Letting the producer throttle means consumers don't need to
     * worry about implementing it, but at the expense of staler date and
     * possibly holding up expensive resources -- both possibly completely
     * unnecessarily! The producer might not know what a success looks like to
     * fast track us and if it did, why do we have this contraption?
     *
     * Letting consumers manage the throttle means it can be placed more
     * optimally at the risk that a consumer might not be aware of this
     * invisible contract and creates a run-away situation -- like if you've
     * ever cleaned your engine bay and didn't notice you hooked your throttle
     * cable and then started the motor right into the red zone. You start
     * looking twice but you still learned the hard way once.
     *
     * But the more optimal placement of the delay means that a consumer
     * doesn't need to be idle for longer than necessary.
     *
     * The more I think about potential fixes, the more complicated everything
     * becomes but maybe I'm just imagining narrowly right now.
     */
    .Delay(TimeSpan.FromSeconds(30))
    .SelectAwaitWithCancellation(Read)
    .Take(10);

/* There is a `ForEachAsync` method that essentially does this for us but it
 * has the same issues as the synchronous `ForEach` method that (I?)List has --
 * there's no way to break the loop early other than throwing an exception. In
 * Python you could be shifty and lob a StopIterationException at the problem
 * but even though that's supported it's really :|
 *
 * But if we want to process every message we can pull through the pipe, then
 * chucking this on at the end and throwing the whole thing into an
 * IHostedService might not be so bad. Pair with a ChannelReader<T> to process
 * in process events.
 */
await foreach (var str in poller)
{
     Console.WriteLine(str);
}


Console.WriteLine("no more calls");

static async ValueTask<string> Read(HttpResponseMessage msg, CancellationToken ct)
    {
        using var _ = msg;
        using var content = msg.Content;
        return await msg.Content.ReadAsStringAsync(ct);
    }

async ValueTask<HttpResponseMessage> Call(CancellationToken ct) =>
    // ReSharper disable once AccessToDisposedClosure
    await http.SendAsync(new HttpRequestMessage
    {
        Content = new StringContent("ping"),
        Method = HttpMethod.Post,
        RequestUri = uri,
    }, ct);

/* Polling is far from the only usage of this kind of on demand value
 * production, paginated results or a database cursor could be packaged into a
 * class and turned into an async enumerable in a similar fashion.
 */
file class LongPoller<T>(LongPoller.Produce<T> produce)
{
    public async IAsyncEnumerable<T> Poll([EnumeratorCancellation] CancellationToken ct)
    {
        // gets the job done on the happy path!
        // needs error handling at least
        while (!ct.IsCancellationRequested)
        {
            var item = await produce(ct);
            yield return item;
        }
    }
}


file static class LongPoller
{
    public delegate ValueTask<T> Produce<T>(CancellationToken ct);

    public static IAsyncEnumerable<T> Poll<T>(this Produce<T> produce, CancellationToken ct)
    {
        LongPoller<T> poller = new(produce);
        return poller.Poll(ct);
    }

    // not really related to long polling, but convenient to already have a static class around
    public static IAsyncEnumerable<T> Delay<T>(this IAsyncEnumerable<T> enumer, TimeSpan delay) =>
        enumer.SelectAwaitWithCancellation(async (t, token) =>
        {
            await Task.Delay(delay, token);
            return t;
        });
}

