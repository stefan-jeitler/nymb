using System.Text;
using Nymb.Lexer;

namespace Nymb.Tests.Lexer;

public class NymbLexerTests
{
    [Fact]
    public void Empty_input_produces_no_tokens()
    {
        Assert.Empty(new NymbLexer("").Tokenize());
    }

    [Fact]
    public void Input_is_split_into_bare_tokens_whitespaces_delimiters_and_parentheses()
    {
        var lexerConfiguration = NymbLexerConfiguration.Default with { Delimiters = ["="] };
        var tokens = new NymbLexer("(foo = bar)\n").Tokenize(lexerConfiguration);

        Assert.Collection(
            tokens,
            token => AssertToken(token, TokenType.OpenParenthesis, "(", "("),
            token => AssertToken(token, TokenType.BareToken, "foo", "foo"),
            token => AssertToken(token, TokenType.Whitespace, " ", " "),
            token => AssertToken(token, TokenType.Delimiter, "=", "="),
            token => AssertToken(token, TokenType.Whitespace, " ", " "),
            token => AssertToken(token, TokenType.BareToken, "bar", "bar"),
            token => AssertToken(token, TokenType.CloseParenthesis, ")", ")"),
            token => AssertToken(token, TokenType.Whitespace, "\n", "\n"));
        return;

        void AssertToken(Token token, TokenType type, string value, string rawValue)
        {
            Assert.Equal(type, token.Type);
            Assert.Equal(value, token.Value);
            Assert.Equal(rawValue, token.RawValue);
        }
    }

    [Fact]
    public void Lexer_is_deterministic_by_default()
    {
        var lexer = new NymbLexer("some input");

        Assert.Equal(lexer.Tokenize(), lexer.Tokenize());
    }

    [Fact]
    public void Delimiters_do_not_need_whitespaces()
    {
        var configuration = NymbLexerConfiguration.Default with { Delimiters = ["="] };
        var lexer = new NymbLexer("y=s");

        var tokens = lexer.Tokenize(configuration);

        Assert.Equal(3, tokens.Count);
    }

    [Fact]
    public void Control_characters_are_not_allowed_as_delimiters()
    {
        var configuration = NymbLexerConfiguration.Default with { Delimiters = ["\0"] };
        var lexer =  new NymbLexer("some input");

        var exception = Assert.Throws<FormatException>(() => lexer.Tokenize(configuration));

        Assert.Equal("Delimiters must not contain control characters.", exception.Message);
    }
}
