using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Local.Lib;

namespace Local.Services;

[SupportedOSPlatform("windows")]
public sealed class CatiaScheduler : IDisposable, ICatiaCommandContext
{
    private const string ProtocolName = "COM (CATIA Simulation)";
    private readonly BlockingCollection<CatiaCommand> _internalQueue = new();
    private readonly Thread _staThread;
    private readonly CancellationTokenSource _cts = new();
    private readonly Lock _stateGate = new();
    private readonly string _editorRootPath;
    private INFITF.Application? _catiaInstance;
    private bool _isRunning;
    private int _staThreadId;
    private string? _openedEditorFileName;
    private string? _openedEditorFilePath;

    public CatiaScheduler()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Der CATIA-Scheduler ist nur unter Windows verfuegbar.");
        }

        _editorRootPath = ResolveEditorRootPath();
        _staThread = new Thread(ExecuteLoop);
        _staThread.IsBackground = true;
        _staThread.SetApartmentState(ApartmentState.STA);
        _staThread.Name = "CATIA_STA_CoreEngine";
        _staThread.Start();
    }

    public void Enqueue(CatiaCommand command)
    {
        _internalQueue.Add(command);
    }

    private void ExecuteLoop()
    {
        try
        {
            Directory.CreateDirectory(_editorRootPath);

            lock (_stateGate)
            {
                _isRunning = true;
                _staThreadId = Environment.CurrentManagedThreadId;
            }

            foreach (var command in _internalQueue.GetConsumingEnumerable(_cts.Token))
            {
                try
                {
                    if (command.RequiresCatiaConnection)
                    {
                        EnsureValidCatiaConnection();
                    }

                    command.Run(this);
                }
                catch (Exception ex)
                {
                    command.Fail(ex);

                    if (ex is COMException comEx && comEx.ErrorCode == unchecked((int)0x800706BA))
                    {
                        _catiaInstance = null;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            lock (_stateGate)
            {
                _isRunning = false;
            }
        }
    }

    private void EnsureValidCatiaConnection()
    {
        var processes = Process.GetProcessesByName("CNEXT");
        if (processes.Length == 0)
        {
            _catiaInstance = null;
            throw new CatiaException("CATIA_NOT_RUNNING", "CATIA ist nicht geöffnet. Bitte starten Sie CATIA.");
        }
        if (processes.Length > 1)
        {
            _catiaInstance = null;
            throw new CatiaException("MULTIPLE_INSTANCES", "Es laufen mehrere Instanzen von CATIA. Bitte schließen Sie alle bis auf eine.");
        }

        if (_catiaInstance != null)
        {
            try { var test = _catiaInstance.Name; return; }
            catch (COMException) { _catiaInstance = null; }
        }

        _catiaInstance = new INFITF.Application() // (INFITF.Application)Marshal.GetActiveObject("CATIA.Application");
        {
                Name = "CATIA V5"
        };
    }

    public CatiaComServiceStatus GetStatus()
    {
        lock (_stateGate)
        {
            return new CatiaComServiceStatus(
                ProtocolName,
                _editorRootPath,
                _isRunning,
                _staThreadId == 0 ? null : _staThreadId,
                _openedEditorFileName,
                _openedEditorFilePath);
        }
    }

    string ICatiaCommandContext.ProtocolName => ProtocolName;

    string ICatiaCommandContext.EditorRootPath => _editorRootPath;

    int ICatiaCommandContext.StaThreadId
    {
        get
        {
            lock (_stateGate)
            {
                return _staThreadId;
            }
        }
    }

    INFITF.Application? ICatiaCommandContext.Catia => _catiaInstance;

    (string? EditorFileName, string? EditorFilePath) ICatiaCommandContext.GetOpenedEditorFile()
    {
        lock (_stateGate)
        {
            return (_openedEditorFileName, _openedEditorFilePath);
        }
    }

    void ICatiaCommandContext.SetOpenedEditorFile(string editorFileName, string editorFilePath)
    {
        lock (_stateGate)
        {
            _openedEditorFileName = editorFileName;
            _openedEditorFilePath = editorFilePath;
        }
    }

    public void Dispose()
    {
        _internalQueue.CompleteAdding();
        _cts.Cancel();
        _staThread.Join(TimeSpan.FromSeconds(5));
        _internalQueue.Dispose();
        _cts.Dispose();
    }

    private static string ResolveEditorRootPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "lokalagentcontrollertest",
            "editor-com-catia");
    }
}

public sealed record CatiaComServiceStatus(
    string ProtocolName,
    string EditorRootPath,
    bool IsRunning,
    int? StaThreadId,
    string? OpenedEditorFileName,
    string? OpenedEditorFilePath);

public sealed record CatiaComOpenReceipt(
    string ProtocolName,
    string EditorFileName,
    string EditorFilePath,
    long ExistingLength,
    DateTimeOffset OpenedAtUtc,
    int StaThreadId,
    string Acknowledgement);

public sealed record CatiaComSaveReceipt(
    string ProtocolName,
    string EditorFileName,
    string EditorFilePath,
    int CharacterCount,
    DateTimeOffset SavedAtUtc,
    int StaThreadId,
    string Acknowledgement);