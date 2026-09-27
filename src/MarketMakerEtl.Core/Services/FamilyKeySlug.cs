using System.Text;

namespace MarketMakerEtl.Core.Services;

internal static class FamilyKeySlug
{
    public static string From(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            AppendChar(builder, char.ToLowerInvariant(ch));
        }

        var slug = builder.ToString().Trim('-');
        return slug.Length > 0 ? slug : "family";
    }

    private static void AppendChar(StringBuilder builder, char ch)
    {
        if (char.IsLetterOrDigit(ch))
        {
            builder.Append(ch);
        }
        else if (builder.Length > 0 && builder[^1] != '-')
        {
            builder.Append('-');
        }
    }
}
