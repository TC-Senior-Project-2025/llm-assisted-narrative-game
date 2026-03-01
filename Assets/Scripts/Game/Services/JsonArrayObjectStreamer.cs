using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Game.Services
{
    public static class JsonArrayObjectStreamer
    {
        /// <summary>
        /// Incrementally extracts each top-level object inside a JSON array: [ {..}, {..}, ... ]
        /// from an incoming text stream (chunks).
        ///
        /// Works even if chunks split mid-token. Handles strings/escapes so braces inside strings are ignored.
        /// </summary>
        public static async IAsyncEnumerable<string> ExtractObjectsFromArray(
            IAsyncEnumerable<string> chunks,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            // State machine for streaming parse
            bool seenArrayStart = false;
            bool inString = false;
            bool escape = false;

            int depth = 0;             // object depth (counts { })
            bool collecting = false;   // are we currently collecting an object?
            var obj = new StringBuilder(4 * 1024);

            await foreach (var chunk in chunks.WithCancellation(ct).ConfigureAwait(false))
            {
                if (ct.IsCancellationRequested) yield break;
                if (string.IsNullOrEmpty(chunk)) continue;

                for (int i = 0; i < chunk.Length; i++)
                {
                    char c = chunk[i];

                    // Before we start, skip whitespace until '['
                    if (!seenArrayStart)
                    {
                        if (char.IsWhiteSpace(c)) continue;
                        if (c == '[') { seenArrayStart = true; continue; }

                        // Error handling: if we see other characters before '[', warn
                        UnityEngine.Debug.LogWarning($"Expected '[' but got '{c}' at index {i} in chunk: {chunk}");
                        continue;
                    }

                    // If we're not collecting an object yet, we look for '{' or end ']'
                    if (!collecting)
                    {
                        if (char.IsWhiteSpace(c) || c == ',') continue;
                        if (c == ']') yield break;      // done
                        if (c == '{')
                        {
                            collecting = true;
                            depth = 1;
                            inString = false;
                            escape = false;
                            obj.Clear();
                            obj.Append(c);
                            continue;
                        }

                        // Ignore anything else (or throw if you want strictness)
                        continue;
                    }

                    // Collecting an object: append char and update JSON string/brace state
                    obj.Append(c);

                    if (inString)
                    {
                        if (escape) { escape = false; continue; }
                        if (c == '\\') { escape = true; continue; }
                        if (c == '"') { inString = false; continue; }
                        continue;
                    }
                    else
                    {
                        if (c == '"') { inString = true; continue; }
                        if (c == '{') { depth++; continue; }
                        if (c == '}')
                        {
                            depth--;
                            if (depth == 0)
                            {
                                // Completed one full object
                                collecting = false;
                                yield return obj.ToString();
                                obj.Clear();
                            }
                            continue;
                        }
                    }
                }
            }
        }
    }
}