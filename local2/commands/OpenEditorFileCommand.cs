using System.Diagnostics;
using System.Runtime.Versioning;
using Local.Lib;
using Local.Services;

namespace Local.Commands;

[SupportedOSPlatform("windows")]
public sealed class OpenEditorFileCommand(string editorFileName) : CatiaCommand<CatiaComOpenReceipt>
{
    protected override CatiaComOpenReceipt Execute(ICatiaCommandContext context)
    {
        if (string.IsNullOrWhiteSpace(editorFileName))
        {
            throw new ArgumentException("Ein Editor-Dateiname ist erforderlich.", nameof(editorFileName));
        }

        var sanitizedEditorFileName = SanitizeEditorFileName(editorFileName);
        var openedAtUtc = DateTimeOffset.UtcNow;
        var editorFilePath = Path.Combine(context.EditorRootPath, sanitizedEditorFileName);

        using var stream = new FileStream(editorFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);

        var editorProcess = Process.Start(new ProcessStartInfo
        {
            FileName = "notepad.exe",
            Arguments = $"\"{editorFilePath}\"",
            UseShellExecute = true
        });

        if (editorProcess is null)
        {
            throw new InvalidOperationException("Der Windows-Editor konnte nicht gestartet werden.");
        }

        context.SetOpenedEditorFile(sanitizedEditorFileName, editorFilePath);

        return new CatiaComOpenReceipt(
            context.ProtocolName,
            sanitizedEditorFileName,
            editorFilePath,
            stream.Length,
            openedAtUtc,
            context.StaThreadId,
            "Die COM/CATIA-Simulation hat die Editor-Datei auf dem STA-Thread geoeffnet und im Windows-Editor gestartet.");
    }

    private static string SanitizeEditorFileName(string fileName)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var sanitized = new string(
            fileName
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
}
