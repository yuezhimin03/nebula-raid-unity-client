using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace NebulaRaid.HotUpdate
{
    /// <summary>Small strict JSON parser used only for the bounded update manifest.</summary>
    internal sealed class StrictJsonParser
    {
        private const int MaxDepth = 16;
        private const int MaxStringLength = 16_384;
        private readonly string _text;
        private int _cursor;

        private StrictJsonParser(string text)
        {
            _text = text;
        }

        public static object? Parse(string text)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            StrictJsonParser parser = new StrictJsonParser(text);
            object? value = parser.ReadValue(0);
            parser.SkipWhitespace();
            if (parser._cursor != text.Length)
            {
                throw parser.Error("Unexpected trailing JSON content.");
            }

            return value;
        }

        private object? ReadValue(int depth)
        {
            if (depth > MaxDepth)
            {
                throw Error("JSON nesting limit exceeded.");
            }

            SkipWhitespace();
            if (_cursor >= _text.Length)
            {
                throw Error("Unexpected end of JSON.");
            }

            char current = _text[_cursor];
            if (current == '{')
            {
                return ReadObject(depth + 1);
            }

            if (current == '[')
            {
                return ReadArray(depth + 1);
            }

            if (current == '"')
            {
                return ReadString();
            }

            if (current == '-' || (current >= '0' && current <= '9'))
            {
                return ReadInteger();
            }

            if (TryReadLiteral("true"))
            {
                return true;
            }

            if (TryReadLiteral("false"))
            {
                return false;
            }

            if (TryReadLiteral("null"))
            {
                return null;
            }

            throw Error("Unexpected JSON token.");
        }

        private Dictionary<string, object?> ReadObject(int depth)
        {
            Expect('{');
            Dictionary<string, object?> value =
                new Dictionary<string, object?>(StringComparer.Ordinal);
            SkipWhitespace();
            if (TryConsume('}'))
            {
                return value;
            }

            while (true)
            {
                SkipWhitespace();
                if (_cursor >= _text.Length || _text[_cursor] != '"')
                {
                    throw Error("Object key must be a JSON string.");
                }

                string key = ReadString();
                if (value.ContainsKey(key))
                {
                    throw Error("Duplicate object key: " + key + ".");
                }

                SkipWhitespace();
                Expect(':');
                value.Add(key, ReadValue(depth));
                SkipWhitespace();
                if (TryConsume('}'))
                {
                    return value;
                }

                Expect(',');
            }
        }

        private List<object?> ReadArray(int depth)
        {
            Expect('[');
            List<object?> value = new List<object?>();
            SkipWhitespace();
            if (TryConsume(']'))
            {
                return value;
            }

            while (true)
            {
                value.Add(ReadValue(depth));
                SkipWhitespace();
                if (TryConsume(']'))
                {
                    return value;
                }

                Expect(',');
            }
        }

        private string ReadString()
        {
            Expect('"');
            StringBuilder builder = new StringBuilder();
            while (_cursor < _text.Length)
            {
                char current = _text[_cursor++];
                if (current == '"')
                {
                    return builder.ToString();
                }

                if (current < 0x20)
                {
                    throw Error("Unescaped control character in string.");
                }

                if (current != '\\')
                {
                    AppendChecked(builder, current);
                    continue;
                }

                if (_cursor >= _text.Length)
                {
                    throw Error("Incomplete string escape.");
                }

                char escaped = _text[_cursor++];
                switch (escaped)
                {
                    case '"':
                    case '\\':
                    case '/':
                        AppendChecked(builder, escaped);
                        break;
                    case 'b':
                        AppendChecked(builder, '\b');
                        break;
                    case 'f':
                        AppendChecked(builder, '\f');
                        break;
                    case 'n':
                        AppendChecked(builder, '\n');
                        break;
                    case 'r':
                        AppendChecked(builder, '\r');
                        break;
                    case 't':
                        AppendChecked(builder, '\t');
                        break;
                    case 'u':
                        ReadUnicodeEscape(builder);
                        break;
                    default:
                        throw Error("Unsupported string escape.");
                }
            }

            throw Error("Unterminated string.");
        }

        private void ReadUnicodeEscape(StringBuilder builder)
        {
            int codeUnit = ReadHexCodeUnit();
            char first = (char)codeUnit;
            if (char.IsHighSurrogate(first))
            {
                if (_cursor + 1 >= _text.Length
                    || _text[_cursor] != '\\'
                    || _text[_cursor + 1] != 'u')
                {
                    throw Error("High surrogate must be followed by a low surrogate.");
                }

                _cursor += 2;
                char second = (char)ReadHexCodeUnit();
                if (!char.IsLowSurrogate(second))
                {
                    throw Error("Invalid low surrogate.");
                }

                AppendChecked(builder, first);
                AppendChecked(builder, second);
                return;
            }

            if (char.IsLowSurrogate(first))
            {
                throw Error("Unexpected low surrogate.");
            }

            AppendChecked(builder, first);
        }

        private int ReadHexCodeUnit()
        {
            if (_cursor + 4 > _text.Length)
            {
                throw Error("Incomplete unicode escape.");
            }

            int value = 0;
            for (int i = 0; i < 4; i++)
            {
                char digit = _text[_cursor++];
                value <<= 4;
                if (digit >= '0' && digit <= '9')
                {
                    value += digit - '0';
                }
                else if (digit >= 'a' && digit <= 'f')
                {
                    value += digit - 'a' + 10;
                }
                else if (digit >= 'A' && digit <= 'F')
                {
                    value += digit - 'A' + 10;
                }
                else
                {
                    throw Error("Invalid unicode escape.");
                }
            }

            return value;
        }

        private long ReadInteger()
        {
            int start = _cursor;
            if (_text[_cursor] == '-')
            {
                _cursor++;
            }

            if (_cursor >= _text.Length)
            {
                throw Error("Incomplete number.");
            }

            if (_text[_cursor] == '0')
            {
                _cursor++;
                if (_cursor < _text.Length && char.IsDigit(_text[_cursor]))
                {
                    throw Error("Leading zero is not allowed.");
                }
            }
            else
            {
                if (_text[_cursor] < '1' || _text[_cursor] > '9')
                {
                    throw Error("Invalid number.");
                }

                while (_cursor < _text.Length
                    && _text[_cursor] >= '0'
                    && _text[_cursor] <= '9')
                {
                    _cursor++;
                }
            }

            if (_cursor < _text.Length
                && (_text[_cursor] == '.' || _text[_cursor] == 'e' || _text[_cursor] == 'E'))
            {
                throw Error("Manifest numbers must be integers.");
            }

            string token = _text.Substring(start, _cursor - start);
            if (!long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value))
            {
                throw Error("Integer is outside the supported range.");
            }

            return value;
        }

        private bool TryReadLiteral(string literal)
        {
            if (_cursor + literal.Length > _text.Length
                || string.CompareOrdinal(_text, _cursor, literal, 0, literal.Length) != 0)
            {
                return false;
            }

            _cursor += literal.Length;
            return true;
        }

        private void SkipWhitespace()
        {
            while (_cursor < _text.Length)
            {
                char value = _text[_cursor];
                if (value != ' ' && value != '\t' && value != '\r' && value != '\n')
                {
                    return;
                }

                _cursor++;
            }
        }

        private bool TryConsume(char value)
        {
            if (_cursor < _text.Length && _text[_cursor] == value)
            {
                _cursor++;
                return true;
            }

            return false;
        }

        private void Expect(char value)
        {
            if (!TryConsume(value))
            {
                throw Error("Expected '" + value + "'.");
            }
        }

        private static void AppendChecked(StringBuilder builder, char value)
        {
            if (builder.Length >= MaxStringLength)
            {
                throw new FormatException("JSON string length limit exceeded.");
            }

            builder.Append(value);
        }

        private FormatException Error(string message)
        {
            return new FormatException(message + " Offset: " + _cursor + ".");
        }
    }
}

