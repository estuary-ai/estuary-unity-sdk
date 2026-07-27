using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Estuary.Models
{
    /// <summary>
    /// Typed in-world action pushed by the server (client_action event,
    /// contract v1.9). Delivered via native LLM function calling — replaces
    /// the legacy inline XML &lt;action .../&gt; tags that rode inside
    /// bot_response text. Fire-on-arrival: trigger the action as soon as the
    /// event arrives (actions are not synchronized to TTS playback).
    /// </summary>
    public class ClientActionEvent
    {
        private string name;
        private Dictionary<string, string> arguments;
        private string messageId;
        private int chunkIndex;
        private string timestamp;

        /// <summary>
        /// The action name exactly as declared on the character (NOT the
        /// sanitized function-calling name).
        /// </summary>
        public string Name => name;

        /// <summary>
        /// Action arguments, validated server-side against the character's
        /// declared parameters. Values are stringified (numbers with invariant
        /// culture, booleans as lowercase "true"/"false") to match the legacy
        /// XML-attribute form consumed by <see cref="AgentAction"/>'s typed
        /// getters. Empty for parameterless actions; never null.
        /// </summary>
        public IReadOnlyDictionary<string, string> Arguments => arguments;

        /// <summary>
        /// Correlates the action with the same turn's bot_response / bot_voice
        /// stream.
        /// </summary>
        public string MessageId => messageId;

        /// <summary>
        /// Sentence counter at the moment the action was emitted (optional
        /// anchoring to the text stream; not required for correct behavior).
        /// </summary>
        public int ChunkIndex => chunkIndex;

        /// <summary>ISO 8601 server emit time.</summary>
        public string Timestamp => timestamp;

        public ClientActionEvent()
        {
            // Case-insensitive lookup, matching the legacy tag parser's
            // attribute dictionary (see ActionParser.ParseAttributes).
            arguments = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Create ClientActionEvent from JSON string.
        /// </summary>
        public static ClientActionEvent FromJson(string json)
        {
            // JsonUtility cannot deserialize the dynamic-keyed arguments map,
            // so this model parses with Newtonsoft (already a Runtime
            // dependency — see AgentResponse / SimulationModels).
            var wire = JsonConvert.DeserializeObject<ClientActionJson>(json);
            var evt = new ClientActionEvent();
            evt.name = wire.name;
            evt.messageId = wire.message_id;
            evt.chunkIndex = wire.chunk_index;
            evt.timestamp = wire.timestamp;

            if (wire.arguments != null)
            {
                foreach (var kvp in wire.arguments)
                {
                    evt.arguments[kvp.Key] = StringifyArgument(kvp.Value);
                }
            }

            return evt;
        }

        /// <summary>
        /// Convert to the existing <see cref="AgentAction"/> model so the typed
        /// event feeds the same action callbacks as the legacy tag path.
        /// </summary>
        public AgentAction ToAgentAction()
        {
            return new AgentAction(name,
                new Dictionary<string, string>(arguments, System.StringComparer.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Stringify a wire argument value (string | number | boolean) to its
        /// legacy XML-attribute form: numbers via invariant culture, booleans
        /// as lowercase "true"/"false".
        /// </summary>
        private static string StringifyArgument(JToken value)
        {
            if (value == null)
            {
                return "";
            }

            switch (value.Type)
            {
                case JTokenType.Boolean:
                    return (bool)value ? "true" : "false";
                case JTokenType.Integer:
                    return ((long)value).ToString(CultureInfo.InvariantCulture);
                case JTokenType.Float:
                    return ((double)value).ToString(CultureInfo.InvariantCulture);
                case JTokenType.String:
                    return (string)value;
                case JTokenType.Null:
                    return "";
                default:
                    // The server coerces arguments to string/number/boolean —
                    // anything else is a defensive fallback.
                    return value.ToString();
            }
        }

        public override string ToString()
        {
            return $"ClientActionEvent(Name=\"{name}\", Arguments={arguments.Count}, MessageId={messageId}, ChunkIndex={chunkIndex})";
        }

        // Internal class for JSON deserialization with snake_case wire keys.
        // Arguments stay as raw JTokens so numbers/booleans can be stringified
        // deliberately instead of via Newtonsoft's default conversions.
        private class ClientActionJson
        {
            public string name;
            public Dictionary<string, JToken> arguments;
            public string message_id;
            public int chunk_index;
            public string timestamp;
        }
    }
}
