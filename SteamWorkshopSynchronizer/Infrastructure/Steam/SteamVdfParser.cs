using System.Text;

namespace SteamWorkshopSynchronizer.Infrastructure.Steam;

public sealed class SteamVdfObject
{
    private readonly Dictionary<string, SteamVdfValue> values = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, SteamVdfValue> Values => values;

    internal void Set(string key, SteamVdfValue value) => values[key] = value;

    public bool TryGetValue(string key, out SteamVdfValue value) => values.TryGetValue(key, out value!);

    public string? GetString(string key) => TryGetValue(key, out var value) ? value.Scalar : null;

    public SteamVdfObject? GetObject(string key) => TryGetValue(key, out var value) ? value.Object : null;
}

public sealed record SteamVdfValue(string? Scalar, SteamVdfObject? Object)
{
    public static SteamVdfValue FromScalar(string value) => new(value, null);

    public static SteamVdfValue FromObject(SteamVdfObject value) => new(null, value);
}

public sealed class SteamVdfParser
{
    public SteamVdfObject Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var tokenizer = new Tokenizer(content);
        return ParseObject(tokenizer, expectOpeningBrace: false);
    }

    private static SteamVdfObject ParseObject(Tokenizer tokenizer, bool expectOpeningBrace, bool requireClosingBrace = false)
    {
        if (expectOpeningBrace && tokenizer.Read() != Token.OpenBrace)
        {
            throw new FormatException("VDF 对象缺少开始大括号");
        }

        var result = new SteamVdfObject();
        while (true)
        {
            var key = tokenizer.Read();
            if (key == Token.End)
            {
                if (expectOpeningBrace || requireClosingBrace)
                {
                    throw new FormatException("VDF 对象缺少结束大括号");
                }

                return result;
            }

            if (key == Token.CloseBrace)
            {
                if (!expectOpeningBrace && !requireClosingBrace)
                {
                    throw new FormatException("VDF 出现意外的结束大括号");
                }

                return result;
            }

            if (key.Kind != TokenKind.String)
            {
                throw new FormatException("VDF 键必须是字符串");
            }

            var value = tokenizer.Read();
            if (value == Token.OpenBrace)
            {
                // The opening brace has already been consumed as the value token.
                // The nested parser only needs to consume and validate its closing brace.
                result.Set(key.Value!, SteamVdfValue.FromObject(ParseObject(tokenizer, expectOpeningBrace: false, requireClosingBrace: true)));
                continue;
            }

            if (value.Kind != TokenKind.String)
            {
                throw new FormatException($"VDF 键 {key.Value} 缺少字符串值");
            }

            result.Set(key.Value!, SteamVdfValue.FromScalar(value.Value!));
        }
    }

    private enum TokenKind
    {
        String,
        OpenBrace,
        CloseBrace,
        End
    }

    private sealed record Token(TokenKind Kind, string? Value)
    {
        public static readonly Token OpenBrace = new(TokenKind.OpenBrace, null);
        public static readonly Token CloseBrace = new(TokenKind.CloseBrace, null);
        public static readonly Token End = new(TokenKind.End, null);

        public static implicit operator TokenKind(Token token) => token.Kind;
    }

    private sealed class Tokenizer
    {
        private readonly string content;
        private int position;

        public Tokenizer(string content) => this.content = content;

        public Token Read()
        {
            SkipWhitespaceAndComments();
            if (position >= content.Length)
            {
                return Token.End;
            }

            var current = content[position++];
            if (current == '{')
            {
                return Token.OpenBrace;
            }

            if (current == '}')
            {
                return Token.CloseBrace;
            }

            if (current == '"')
            {
                return new(TokenKind.String, ReadQuotedString());
            }

            var start = position - 1;
            while (position < content.Length && !char.IsWhiteSpace(content[position]) && content[position] != '{' && content[position] != '}')
            {
                position++;
            }

            return new(TokenKind.String, content[start..position]);
        }

        private void SkipWhitespaceAndComments()
        {
            while (position < content.Length)
            {
                while (position < content.Length && char.IsWhiteSpace(content[position]))
                {
                    position++;
                }

                if (position + 1 >= content.Length || content[position] != '/' || content[position + 1] != '/')
                {
                    return;
                }

                position += 2;
                while (position < content.Length && content[position] != '\n')
                {
                    position++;
                }
            }
        }

        private string ReadQuotedString()
        {
            var builder = new StringBuilder();
            while (position < content.Length)
            {
                var current = content[position++];
                if (current == '"')
                {
                    return builder.ToString();
                }

                if (current == '\\' && position < content.Length)
                {
                    var escaped = content[position++];
                    builder.Append(escaped switch
                    {
                        'n' => '\n',
                        'r' => '\r',
                        't' => '\t',
                        _ => escaped
                    });
                    continue;
                }

                builder.Append(current);
            }

            throw new FormatException("VDF 字符串缺少结束引号");
        }
    }
}
