using System.Collections.Frozen;
using System.Text;

namespace Nymb.Lexer;

internal sealed record NymbLexerConfiguration
{
    public static readonly NymbLexerConfiguration Default = new();

    public FrozenSet<string> Delimiters { get; init; } = FrozenSet<string>.Empty;
}
