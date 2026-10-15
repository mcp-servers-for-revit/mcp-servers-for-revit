using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace revit_mcp_plugin.Core
{
    /// <summary>
    /// Splits a TCP byte stream into JSON messages, one per line.
    /// MCP servers released before newline framing send a single JSON object with no line break,
    /// so a buffered object that parses on its own also counts as a message.
    /// </summary>
    public class MessageFramer
    {
        private readonly Decoder _decoder = new UTF8Encoding(false).GetDecoder();
        private readonly StringBuilder _pending = new StringBuilder();
        private readonly int _maxMessageLength;

        public MessageFramer(int maxMessageLength)
        {
            _maxMessageLength = maxMessageLength;
        }

        /// <summary>
        /// Adds bytes read from the socket and returns every message they complete.
        /// </summary>
        /// <exception cref="InvalidDataException">A message is longer than the size limit.</exception>
        public List<string> Append(byte[] buffer, int count)
        {
            var messages = new List<string>();

            // The decoder keeps partial UTF-8 sequences between reads, so characters split across packets survive.
            char[] chars = new char[_decoder.GetCharCount(buffer, 0, count)];
            int charCount = _decoder.GetChars(buffer, 0, count, chars, 0);

            for (int i = 0; i < charCount; i++)
            {
                if (chars[i] == '\n')
                {
                    string line = _pending.ToString().Trim();
                    _pending.Clear();
                    if (line.Length > 0)
                    {
                        messages.Add(line);
                    }
                }
                else
                {
                    _pending.Append(chars[i]);
                }
            }

            if (_pending.Length > _maxMessageLength)
            {
                _pending.Clear();
                throw new InvalidDataException($"Message exceeds the {_maxMessageLength} character limit.");
            }

            if (TryTakeUnterminatedObject(out string legacyMessage))
            {
                messages.Add(legacyMessage);
            }

            return messages;
        }

        private bool TryTakeUnterminatedObject(out string message)
        {
            message = null;

            // Check the ends before building a string, so large messages arriving in pieces stay cheap.
            int last = _pending.Length - 1;
            while (last >= 0 && char.IsWhiteSpace(_pending[last]))
            {
                last--;
            }
            if (last < 0 || _pending[last] != '}')
            {
                return false;
            }

            string text = _pending.ToString().Trim();
            if (text[0] != '{')
            {
                return false;
            }

            try
            {
                JToken.Parse(text);
            }
            catch (JsonException)
            {
                // Not complete yet; wait for more data.
                return false;
            }

            _pending.Clear();
            message = text;
            return true;
        }
    }
}
