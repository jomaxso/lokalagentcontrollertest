namespace Local.Lib;

public abstract class CatiaCommand
{
    private readonly TaskCompletionSource<string> _tcs = new();
    public Task<string> Task => _tcs.Task;

    // Wird vom Scheduler auf dem STA-Thread aufgerufen
    public void Run(INFITF.Application catia)
    {
        try
        {
            string result = ExecuteInternal(catia);
            _tcs.TrySetResult(result);
        }
        catch (Exception ex)
        {
            _tcs.TrySetException(ex);
        }
    }

    public string ExecuteInternal(INFITF.Application catia)
    {
        return Execute(catia);
    }

    protected abstract string Execute(INFITF.Application catia);
}

public class CatiaException : Exception
{
    public string ErrorCode { get; }
    public CatiaException(string errorCode, string message) : base(message) => ErrorCode = errorCode;
}

public static class INFITF
{
    public class Application
    {
        public string Name { get; set; } = string.Empty;
        public Document? ActiveDocument { get; internal set; }
    }

    public class Document
    {
        public string Name { get; set; } = string.Empty;
    }
}