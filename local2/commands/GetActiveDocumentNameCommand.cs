using Local.Lib;

namespace Local.Commands;

public class GetActiveDocumentNameCommand : CatiaCommand<string>
{
    public override bool RequiresCatiaConnection => true;

    protected override string Execute(ICatiaCommandContext context)
    {
        var catia = context.Catia ?? throw new InvalidOperationException("CATIA ist nicht verbunden.");
        var doc = catia.ActiveDocument;
        return doc != null ? doc.Name : "Kein Dokument geöffnet";
    }
}