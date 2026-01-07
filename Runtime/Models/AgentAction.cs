using System;
using System.Collections.Generic;
using UnityEngine;

namespace Estuary.Models
{
    /// <summary>
    /// Represents an action parsed from AI character responses.
    /// Actions are embedded in responses using XML-style tags: &lt;action name="sit" /&gt;
    /// </summary>
    [Serializable]
    public class AgentAction
    {
        [SerializeField] private string name;
        [SerializeField] private Dictionary<string, string> parameters;

        /// <summary>
        /// The name of the action (e.g., "sit", "wave", "follow").
        /// </summary>
        public string Name => name;

        /// <summary>
        /// Additional parameters for the action (e.g., target="player", speed="fast").
        /// </summary>
        public IReadOnlyDictionary<string, string> Parameters => parameters;

        /// <summary>
        /// Create a new AgentAction.
        /// </summary>
        public AgentAction()
        {
            parameters = new Dictionary<string, string>();
        }

        /// <summary>
        /// Create a new AgentAction with a name.
        /// </summary>
        /// <param name="name">The action name</param>
        public AgentAction(string name)
        {
            this.name = name;
            this.parameters = new Dictionary<string, string>();
        }

        /// <summary>
        /// Create a new AgentAction with a name and parameters.
        /// </summary>
        /// <param name="name">The action name</param>
        /// <param name="parameters">The action parameters</param>
        public AgentAction(string name, Dictionary<string, string> parameters)
        {
            this.name = name;
            this.parameters = parameters ?? new Dictionary<string, string>();
        }

        /// <summary>
        /// Check if the action has a specific parameter.
        /// </summary>
        /// <param name="paramName">The parameter name to check</param>
        /// <returns>True if the parameter exists</returns>
        public bool HasParameter(string paramName)
        {
            return parameters != null && parameters.ContainsKey(paramName);
        }

        /// <summary>
        /// Get a parameter value as a string.
        /// </summary>
        /// <param name="paramName">The parameter name</param>
        /// <param name="defaultValue">Default value if parameter doesn't exist</param>
        /// <returns>The parameter value or default</returns>
        public string GetParameter(string paramName, string defaultValue = null)
        {
            if (parameters != null && parameters.TryGetValue(paramName, out var value))
            {
                return value;
            }
            return defaultValue;
        }

        /// <summary>
        /// Get a parameter value as an integer.
        /// </summary>
        /// <param name="paramName">The parameter name</param>
        /// <param name="defaultValue">Default value if parameter doesn't exist or can't be parsed</param>
        /// <returns>The parameter value or default</returns>
        public int GetParameterInt(string paramName, int defaultValue = 0)
        {
            var value = GetParameter(paramName);
            if (value != null && int.TryParse(value, out var result))
            {
                return result;
            }
            return defaultValue;
        }

        /// <summary>
        /// Get a parameter value as a float.
        /// </summary>
        /// <param name="paramName">The parameter name</param>
        /// <param name="defaultValue">Default value if parameter doesn't exist or can't be parsed</param>
        /// <returns>The parameter value or default</returns>
        public float GetParameterFloat(string paramName, float defaultValue = 0f)
        {
            var value = GetParameter(paramName);
            if (value != null && float.TryParse(value, out var result))
            {
                return result;
            }
            return defaultValue;
        }

        /// <summary>
        /// Get a parameter value as a boolean.
        /// </summary>
        /// <param name="paramName">The parameter name</param>
        /// <param name="defaultValue">Default value if parameter doesn't exist or can't be parsed</param>
        /// <returns>The parameter value or default</returns>
        public bool GetParameterBool(string paramName, bool defaultValue = false)
        {
            var value = GetParameter(paramName);
            if (value != null)
            {
                if (bool.TryParse(value, out var result))
                {
                    return result;
                }
                // Also accept "1", "0", "yes", "no"
                var lower = value.ToLowerInvariant();
                if (lower == "1" || lower == "yes" || lower == "true")
                {
                    return true;
                }
                if (lower == "0" || lower == "no" || lower == "false")
                {
                    return false;
                }
            }
            return defaultValue;
        }

        public override string ToString()
        {
            var paramStr = parameters != null && parameters.Count > 0
                ? string.Join(", ", System.Linq.Enumerable.Select(parameters, kvp => $"{kvp.Key}=\"{kvp.Value}\""))
                : "none";
            return $"AgentAction(Name=\"{name}\", Parameters=[{paramStr}])";
        }
    }
}



