namespace Local.Lib;

public abstract class CatiaCommand
{
    public virtual bool RequiresCatiaConnection => false;

    internal abstract void Run(ICatiaCommandContext context);

    internal abstract void Fail(Exception exception);
}

public abstract class CatiaCommand<TResult> : CatiaCommand
{
    private readonly TaskCompletionSource<TResult> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<TResult> Completion => _completion.Task;

    internal override void Run(ICatiaCommandContext context)
    {
        try
        {
            _completion.TrySetResult(Execute(context));
        }
        catch (Exception exception)
        {
            _completion.TrySetException(exception);
        }
    }

    internal override void Fail(Exception exception)
    {
        _completion.TrySetException(exception);
    }

    protected abstract TResult Execute(ICatiaCommandContext context);
}

public interface ICatiaCommandContext
{
    string ProtocolName { get; }

    string EditorRootPath { get; }

    int StaThreadId { get; }

    INFITF.Application? Catia { get; }

    (string? EditorFileName, string? EditorFilePath) GetOpenedEditorFile();

    void SetOpenedEditorFile(string editorFileName, string editorFilePath);
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