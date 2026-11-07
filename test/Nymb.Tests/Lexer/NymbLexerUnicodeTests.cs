using Nymb.Lexer;

namespace Nymb.Tests.Lexer;

public class NymbLexerUnicodeTests
{
    [Fact]
    public void Smiley_emoji_is_a_bare_token()
    {
        var lexer = new NymbLexer("😀");
        var singleEmojiToken = Assert.Single(lexer.Tokenize());

        Assert.Equal(TokenType.BareToken, singleEmojiToken.Type);
        Assert.Equal("😀", singleEmojiToken.Value);
        Assert.Equal("😀", singleEmojiToken.RawValue);

    }
}

