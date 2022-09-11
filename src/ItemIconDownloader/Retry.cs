namespace ItemIconDownloader;

public class Retry
{
    public static Task Do(
        Func<Task> action,
        TimeSpan retryInterval,
        int maxAttemptCount = 3)
    {
        return Do(async () =>
        {
            await action();
            return Task.FromResult<object?>(null);
        }, retryInterval, maxAttemptCount);
    }

    public static async Task<T> Do<T>(
        Func<Task<T>> action,
        TimeSpan retryInterval,
        int maxAttemptCount = 3)
    {
        var exceptions = new List<Exception>();

        for (var attempted = 0; attempted < maxAttemptCount; attempted++)
        {
            try
            {
                if (attempted > 0)
                {
                    await Task.Delay(retryInterval);
                }

                return await action();
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        }

        throw new AggregateException(exceptions);
    }
}