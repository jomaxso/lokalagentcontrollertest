using System.Runtime.Versioning;
using Local.Lib;
using Local.Services;

namespace Local.Commands;

[SupportedOSPlatform("windows")]
public sealed class SaveEditorInputCommand(string input) : CatiaCommand<CatiaComSaveReceipt>
{
    protected override CatiaComSaveReceipt Execute(ICatiaCommandContext context)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            throw new ArgumentException("Der Frontend-Input darf nicht leer sein.", nameof(input));
        }

        var (openedEditorFileName, openedEditorFilePath) = context.GetOpenedEditorFile();
        if (string.IsNullOrWhiteSpace(openedEditorFileName) || string.IsNullOrWhiteSpace(openedEditorFilePath))
        {
            throw new InvalidOperationException("Es wurde noch keine Editor-Datei geoeffnet.");
        }

        var savedAtUtc = DateTimeOffset.UtcNow;

        using var stream = new FileStream(openedEditorFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
        stream.SetLength(0);

        using var writer = new StreamWriter(stream);
        writer.WriteLine($"Protocol={context.ProtocolName}");
        writer.WriteLine($"SavedAtUtc={savedAtUtc:O}");
        writer.WriteLine($"StaThreadId={context.StaThreadId}");
        writer.WriteLine($"EditorFile={openedEditorFileName}");
        writer.WriteLine();
        writer.Write(input);
        writer.Flush();
        stream.Flush(flushToDisk: true);

        return new CatiaComSaveReceipt(
            context.ProtocolName,
            openedEditorFileName,
            openedEditorFilePath,
            input.Length,
            savedAtUtc,
            context.StaThreadId,
            "Die COM/CATIA-Simulation hat den Frontend-Input in die geoeffnete Editor-Datei geschrieben.");
    }
}
