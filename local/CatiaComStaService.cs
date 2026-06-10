using System.Threading.Channels;

sealed class CatiaComStaService(ILogger<CatiaComStaService> logger) : IHostedService, IAsyncDisposable
{
    private const string ProtocolName = "COM (CATIA Simulation)";

    private readonly Channel<IStaCommand> commandChannel = Channel.CreateBounded<IStaCommand>(
        new BoundedChannelOptions(32)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });

    private readonly CancellationTokenSource shutdownSource = new();
    private readonly TaskCompletionSource startupCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Thread? staThread;
    private int staThreadId;
    private string? editorRootPath;
    private string? openedEditorFileName;
    private string? openedEditorFilePath;
    private bool isRunning;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Der COM/CATIA-STA-Dienst ist nur unter Windows verfuegbar.");
        }

        if (staThread is not null)
        {
            return Task.CompletedTask;
        }

        editorRootPath = ResolveEditorRootPath();
        staThread = new Thread(RunStaLoop)
        {
            IsBackground = true,
            Name = "CatiaComStaThread"
        };
        staThread.SetApartmentState(ApartmentState.STA);
        staThread.Start();

        return startupCompletion.Task.WaitAsync(cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        commandChannel.Writer.TryComplete();
        shutdownSource.Cancel();

        if (staThread is null)
        {
            return;
        }

        var joined = await Task.Run(() => staThread.Join(TimeSpan.FromSeconds(5)), cancellationToken);

        if (!joined)
        {
            logger.LogWarning("Der COM/CATIA-STA-Thread wurde nicht innerhalb des Shutdown-Timeouts beendet.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        commandChannel.Writer.TryComplete();
        shutdownSource.Cancel();

        if (staThread is not null && staThread.IsAlive)
        {
            await Task.Run(() => staThread.Join(TimeSpan.FromSeconds(5)));
        }

        shutdownSource.Dispose();
    }

    public CatiaComServiceStatus GetStatus()
    {
        return new CatiaComServiceStatus(
            ProtocolName,
            GetEditorRootPath(),
            isRunning,
            staThreadId == 0 ? null : staThreadId,
            openedEditorFileName,
            openedEditorFilePath);
    }

    public async Task<CatiaComOpenReceipt> OpenEditorFileAsync(string editorFileName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(editorFileName))
        {
            throw new ArgumentException("Ein Editor-Dateiname ist erforderlich.", nameof(editorFileName));
        }

        var command = new OpenEditorFileCommand(
            SanitizeEditorFileName(editorFileName),
            new TaskCompletionSource<CatiaComOpenReceipt>(TaskCreationOptions.RunContinuationsAsynchronously));

        await commandChannel.Writer.WriteAsync(command, cancellationToken);
        return await command.Completion.Task.WaitAsync(cancellationToken);
    }

    public async Task<CatiaComSaveReceipt> SaveOpenedEditorInputAsync(string input, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            throw new ArgumentException("Der Frontend-Input darf nicht leer sein.", nameof(input));
        }

        var command = new SaveOpenedEditorInputCommand(
            input,
            new TaskCompletionSource<CatiaComSaveReceipt>(TaskCreationOptions.RunContinuationsAsynchronously));

        await commandChannel.Writer.WriteAsync(command, cancellationToken);
        return await command.Completion.Task.WaitAsync(cancellationToken);
    }

    private void RunStaLoop()
    {
        try
        {
            isRunning = true;
            staThreadId = Environment.CurrentManagedThreadId;
            Directory.CreateDirectory(GetEditorRootPath());
            startupCompletion.TrySetResult();

            while (commandChannel.Reader.WaitToReadAsync(shutdownSource.Token).AsTask().GetAwaiter().GetResult())
            {
                while (commandChannel.Reader.TryRead(out var command))
                {
                    command.Execute(this);
                }
            }
        }
        catch (OperationCanceledException)
        {
            startupCompletion.TrySetResult();
        }
        catch (Exception ex)
        {
            startupCompletion.TrySetException(ex);
            FailPendingCommands(ex);
            logger.LogError(ex, "Der COM/CATIA-STA-Thread ist unerwartet beendet worden.");
        }
        finally
        {
            isRunning = false;
        }
    }

    private CatiaComOpenReceipt ExecuteOpenEditorFile(string editorFileName)
    {
        var openedAtUtc = DateTimeOffset.UtcNow;
        var editorFilePath = Path.Combine(GetEditorRootPath(), editorFileName);

        using var stream = new FileStream(editorFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);

        openedEditorFileName = editorFileName;
        openedEditorFilePath = editorFilePath;

        logger.LogInformation(
            "COM/CATIA simulation opened editor file {EditorFilePath} on STA thread {ThreadId}",
            editorFilePath,
            staThreadId);

        return new CatiaComOpenReceipt(
            ProtocolName,
            editorFileName,
            editorFilePath,
            stream.Length,
            openedAtUtc,
            staThreadId,
            "Die COM/CATIA-Simulation hat die Editor-Datei auf dem STA-Thread geoeffnet.");
    }

    private CatiaComSaveReceipt ExecuteSaveOpenedEditorInput(string input)
    {
        if (string.IsNullOrWhiteSpace(openedEditorFileName) || string.IsNullOrWhiteSpace(openedEditorFilePath))
        {
            throw new InvalidOperationException("Es wurde noch keine Editor-Datei geoeffnet.");
        }

        var savedAtUtc = DateTimeOffset.UtcNow;

        using var stream = new FileStream(openedEditorFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
        stream.SetLength(0);

        using var writer = new StreamWriter(stream);
        writer.WriteLine($"Protocol={ProtocolName}");
        writer.WriteLine($"SavedAtUtc={savedAtUtc:O}");
        writer.WriteLine($"StaThreadId={staThreadId}");
        writer.WriteLine($"EditorFile={openedEditorFileName}");
        writer.WriteLine();
        writer.Write(input);
        writer.Flush();
        stream.Flush(flushToDisk: true);

        logger.LogInformation(
            "COM/CATIA simulation saved frontend input to {EditorFilePath} on STA thread {ThreadId}",
            openedEditorFilePath,
            staThreadId);

        return new CatiaComSaveReceipt(
            ProtocolName,
            openedEditorFileName,
            openedEditorFilePath,
            input.Length,
            savedAtUtc,
            staThreadId,
            "Die COM/CATIA-Simulation hat den Frontend-Input in die geoeffnete Editor-Datei geschrieben.");
    }

    private void FailPendingCommands(Exception exception)
    {
        while (commandChannel.Reader.TryRead(out var command))
        {
            command.Fail(exception);
        }
    }

    private string GetEditorRootPath()
    {
        editorRootPath ??= ResolveEditorRootPath();
        return editorRootPath;
    }

    private static string ResolveEditorRootPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "lokalagentcontrollertest",
            "editor-com-catia");
    }

    private static string SanitizeEditorFileName(string editorFileName)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var sanitized = new string(
            editorFileName
                .Trim()
                .Select(character => invalidCharacters.Contains(character) ? '_' : character)
                .ToArray());

        if (string.IsNullOrWhiteSpace(sanitized))
        {
            sanitized = "catia-editor-input";
        }

        if (!Path.HasExtension(sanitized))
        {
            sanitized += ".CATPart";
        }

        return sanitized;
    }

    private interface IStaCommand
    {
        void Execute(CatiaComStaService service);
        void Fail(Exception exception);
    }

    private sealed record OpenEditorFileCommand(
        string EditorFileName,
        TaskCompletionSource<CatiaComOpenReceipt> Completion) : IStaCommand
    {
        public void Execute(CatiaComStaService service)
        {
            Completion.TrySetResult(service.ExecuteOpenEditorFile(EditorFileName));
        }

        public void Fail(Exception exception)
        {
            Completion.TrySetException(exception);
        }
    }

    private sealed record SaveOpenedEditorInputCommand(
        string Input,
        TaskCompletionSource<CatiaComSaveReceipt> Completion) : IStaCommand
    {
        public void Execute(CatiaComStaService service)
        {
            Completion.TrySetResult(service.ExecuteSaveOpenedEditorInput(Input));
        }

        public void Fail(Exception exception)
        {
            Completion.TrySetException(exception);
        }
    }
}

sealed record CatiaComServiceStatus(
    string ProtocolName,
    string EditorRootPath,
    bool IsRunning,
    int? StaThreadId,
    string? OpenedEditorFileName,
    string? OpenedEditorFilePath);

sealed record CatiaComOpenReceipt(
    string ProtocolName,
    string EditorFileName,
    string EditorFilePath,
    long ExistingLength,
    DateTimeOffset OpenedAtUtc,
    int StaThreadId,
    string Acknowledgement);

sealed record CatiaComSaveReceipt(
    string ProtocolName,
    string EditorFileName,
    string EditorFilePath,
    int CharacterCount,
    DateTimeOffset SavedAtUtc,
    int StaThreadId,
    string Acknowledgement);
