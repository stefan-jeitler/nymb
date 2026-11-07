using System.Buffers;
using System.Text;

namespace Nymb.Lexer;

internal sealed class NymbLexer
{
    private readonly string _input;

    public NymbLexer(string input)
    {
        ArgumentNullException.ThrowIfNull(input);

        _input = input;
    }

    public List<Token> Tokenize(NymbLexerConfiguration? configuration = null)
    {
        var config = configuration ?? NymbLexerConfiguration.Default;
        if (config.Delimiters.Any(x => x.Any(c => char.IsWhiteSpace(c) || char.IsControl(c))))
            throw new FormatException("Delimiters must not contain control characters.");

        return new TokenizationRun(_input, config).Tokenize();
    }
}

file class TokenizationRun(string input, NymbLexerConfiguration configuration)
{
    private int _charIndex;
    private int _column = 1;
    private int _line = 1;

    private SourcePosition CurrentPosition => new(_charIndex, _line, _column);

    public List<Token> Tokenize()
    {
        var tokens = new List<Token>();

        while (_charIndex < input.Length)
        {
            if (char.IsWhiteSpace(input[_charIndex]))
            {
                tokens.Add(ReadWhitespace());
                continue;
            }

            if (char.IsControl(input[_charIndex]))
            {
                var sourceSpan = new SourceSpan(CurrentPosition, CurrentPosition);
                throw new FormatException(FormatDiagnostic("Not allowed control character", sourceSpan));
            }

            var start = CurrentPosition;
            var character = input[_charIndex];

            switch (character)
            {
                case '(':
                    tokens.Add(ReadSingleCharacter(TokenType.OpenParenthesis));
                    break;
                case ')':
                    tokens.Add(ReadSingleCharacter(TokenType.CloseParenthesis));
                    break;
                case '\'':
                case '"':
                    tokens.Add(ReadStringLiteral());
                    break;
                default:
                    tokens.Add(IsDelimiterSequence()
                        ? ReadSingleCharacter(TokenType.Delimiter)
                        : ReadBareToken(start));
                    break;
            }
        }

        return tokens;
    }

    private bool IsDelimiterSequence()
    {
        foreach (var delimiter in configuration.Delimiters)
        {
            if (_charIndex + delimiter.Length > input.Length)
                return false;

            //var x = input.AsSpan(new Range(_charIndex, _charIndex + delimiter.Length));
            var x = input.Substring(_charIndex, delimiter.Length);

            if (x.Equals(delimiter, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private Token ReadWhitespace()
    {
        var start = CurrentPosition;
        var valueStart = _charIndex;

        while (_charIndex < input.Length && char.IsWhiteSpace(input[_charIndex]))
            Advance();

        var value = input[valueStart.._charIndex];
        return new Token(TokenType.Whitespace, value, value, new SourceSpan(start, CurrentPosition));
    }

    private Token ReadSingleCharacter(TokenType type)
    {
        var start = CurrentPosition;
        var value = input[_charIndex].ToString();
        Advance();

        return new Token(type, value, value, new SourceSpan(start, CurrentPosition));
    }

    private Token ReadStringLiteral()
    {
        var start = CurrentPosition;
        var quote = input[_charIndex];
        Advance();
        var value = new StringBuilder();

        while (_charIndex < input.Length)
        {
            var character = input[_charIndex];
            if (character == quote)
            {
                Advance();
                var span = new SourceSpan(start, CurrentPosition);
                var rawValue = input[span.Start.CharIndex..span.End.CharIndex];
                return new Token(TokenType.StringLiteral, value.ToString(), rawValue, span);
            }

            if (character != '\\')
            {
                var characterStart = _charIndex;
                Advance();
                value.Append(input.AsSpan(characterStart, _charIndex - characterStart));
                continue;
            }

            var escapeStart = CurrentPosition;
            Advance();

            if (_charIndex == input.Length)
            {
                var escapeSpan = new SourceSpan(escapeStart, CurrentPosition);
                throw new FormatException(FormatDiagnostic("Unterminated escape sequence", escapeSpan));
            }

            var escapedCharacter = input[_charIndex];
            Advance();
            var escapedValue = escapedCharacter switch
            {
                '"' => '"', // double quote
                '\'' => '\'', // single quote
                '\\' => '\\', // backslash
                't' => '\t', // horizontal tab
                'r' => '\r', // carriage return
                'n' => '\n', // line feed
                'a' => '\a', // alert (bell)
                'b' => '\b', // backspace
                'f' => '\f', // form feed
                'v' => '\v', // vertical tab
                '0' => '\0', // nul character (NUL)
                _ => throw new FormatException(FormatDiagnostic(
                    $"Invalid escape sequence '\\{escapedCharacter}'",
                    new SourceSpan(escapeStart, CurrentPosition)))
            };

            value.Append(escapedValue);
        }

        var errorSpan = new SourceSpan(start, CurrentPosition);
        throw new FormatException(FormatDiagnostic("Unterminated quoted string", errorSpan));
    }

    private Token ReadBareToken(SourcePosition start)
    {
        var valueStart = _charIndex;
        while (_charIndex < input.Length &&
               !char.IsWhiteSpace(input[_charIndex]) &&
               !IsDelimiterSequence() &&
               input[_charIndex] is not '(' and not ')' and not '\'' and not '"')
            Advance();

        var value = input[valueStart.._charIndex];
        return new Token(TokenType.BareToken, value, value, new SourceSpan(start, CurrentPosition));
    }



    private void Advance()
    {
        var inputPortion = input.AsSpan(_charIndex);

        bool ConsumeRune(ReadOnlySpan<char> inputSpan, out Rune current, out int count) =>
            Rune.DecodeFromUtf16(inputSpan, out current, out count) == OperationStatus.Done;

        bool IsSpecialCodePoint(Rune candidate) =>
            IsRegionalIndicator(candidate) ||
            IsFitzpatrickModifier(candidate) ||
            IsVariationSelector(candidate) ||
            IsEnclosingMark(candidate) ||
            IsDiacritic(candidate);

        var charsOfSingleVisualCharacter = 1;

        while (inputPortion.Length > 0 &&
               ConsumeRune(inputPortion, out var currentRune, out var charsConsumed) &&
               IsSpecialCodePoint(currentRune)
              )
        {
            charsOfSingleVisualCharacter += charsConsumed;

            if(Rune.IsControl(currentRune) || Rune.IsWhiteSpace(currentRune))
                break;

            inputPortion = inputPortion[charsConsumed..];
        }

        var currentChar = input[_charIndex];
        switch (currentChar)
        {
            case '\r':
            {
                var offset = _charIndex + 1 < input.Length && input[_charIndex + 1] == '\n' ? 2 : 1;
                _charIndex += offset;
                _line++;
                _column = 1;
                break;
            }
            case '\n':
                _charIndex++;
                _line++;
                _column = 1;
                break;
            default:
                _charIndex += charsOfSingleVisualCharacter;
                _column++;
                break;
        }

        // Country Flag
        bool IsRegionalIndicator(Rune candidate) => candidate.Value is >= 0x1F1E6 and <= 0x1F1FF;

        // Skin Tone
        bool IsFitzpatrickModifier(Rune candidate) => candidate.Value is >= 0x1F3FB and <= 0x1F3FF;

        bool IsVariationSelector(Rune candidate) => candidate.Value is 0xFE0F;

        // The range from 0x20DD to 0x20E4 covers circle, square, diamond, keycap, and triangle
        bool IsEnclosingMark(Rune candidate) => candidate.Value is >= 0x20DD and <= 0x20E4;
        bool IsDiacritic(Rune candidate) => candidate.Value is >= 0x0300 and <= 0x036F;
    }

    private string FormatDiagnostic(string message, SourceSpan span)
    {
        var (sourceExcerpt, markerColumn) = GetSourceExcerpt(span);
        var marker = new string(' ', markerColumn) + '↑';

        return $"""
                {message} at line {span.Start.Line}, column {span.Start.Column}.
                {sourceExcerpt}{Environment.NewLine}{marker}
                """;
    }

    private (string Text, int MarkerColumn) GetSourceExcerpt(SourceSpan span)
    {
        var lineStart = input.AsSpan(0, span.Start.CharIndex).LastIndexOfAny('\r', '\n') + 1;
        var lineEndOffset = input.AsSpan(span.Start.CharIndex).IndexOfAny('\r', '\n');
        var lineEnd = lineEndOffset < 0 ? input.Length : span.Start.CharIndex + lineEndOffset;

        const int contextBefore = 15;
        const int contextAfter = 15;

        var excerptStart = Math.Max(lineStart, span.Start.CharIndex - contextBefore);
        var excerptEnd = Math.Min(lineEnd, span.Start.CharIndex + contextAfter);
        var hasLeadingOmission = excerptStart > lineStart;
        var hasTrailingOmission = excerptEnd < lineEnd;

        var text =
            $"{(hasLeadingOmission ? "…" : "")}{input[excerptStart..excerptEnd]}{(hasTrailingOmission ? "…" : "")}";
        var markerColumn = (hasLeadingOmission ? 1 : 0) + span.Start.CharIndex - excerptStart;

        return (text, markerColumn);
    }
}
