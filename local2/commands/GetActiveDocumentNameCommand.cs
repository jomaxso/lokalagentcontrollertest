using Local.Lib;

namespace Local.Commands;

public class GetActiveDocumentNameCommand : CatiaCommand
{
    protected override string Execute(INFITF.Application catia)
    {
        var doc = catia.ActiveDocument;
        return doc != null ? doc.Name : "Kein Dokument geöffnet";
    }
}