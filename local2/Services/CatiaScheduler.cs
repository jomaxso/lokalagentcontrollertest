using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Local.Lib;

namespace Local.Services;

[SupportedOSPlatform("windows")]
public class CatiaScheduler : IDisposable
{
    private readonly BlockingCollection<CatiaCommand> _internalQueue = new();
    private readonly Thread _staThread;
    private readonly CancellationTokenSource _cts = new();
    private INFITF.Application? _catiaInstance;

    public CatiaScheduler()
    {
        _staThread = new Thread(ExecuteLoop);
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
        foreach (var command in _internalQueue.GetConsumingEnumerable(_cts.Token))
        {
            try
            {
                EnsureValidCatiaConnection();
                command.Run(_catiaInstance!);
            }
            catch (Exception ex)
            {
                command.Run(null!); // Weckt wartendes TCS mit einer Exception auf

                if (ex is COMException comEx && comEx.ErrorCode == unchecked((int)0x800706BA))
                {
                    _catiaInstance = null; // Instanz verwerfen, da geschlossen
                }
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

    public void Dispose()
    {
        _internalQueue.CompleteAdding();
        _cts.Cancel();
        _staThread.Join();
        _internalQueue.Dispose();
        _cts.Dispose();
    }
}