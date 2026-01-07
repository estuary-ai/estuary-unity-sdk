using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using Estuary.Models;

namespace Estuary.Utilities
{
    /// <summary>
    /// Utility class for parsing action tags from AI character responses.
    /// Actions are embedded using XML-style tags: &lt;action name="sit" /&gt; or &lt;action name="walk" target="player" /&gt;
    /// </summary>
    public static class ActionParser
    {
        // Regex pattern to match action tags
        // Matches: <action name="actionName" [param="value" ...] />
        // Also matches: <action name='actionName' [param='value' ...] />
        private static readonly Regex ActionPattern = new Regex(
            @"<action\s+([^>]*?)\s*/>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline
        );

        // Regex pattern to parse attributes from an action tag
        // Matches: name="value" or name='value'
        private static readonly Regex AttributePattern = new Regex(
            @"(\w+)\s*=\s*[""']([^""']*)[""']",
            RegexOptions.IgnoreCase
        );

        /// <summary>
        /// Parse all action tags from a text string.
        /// </summary>
        /// <param name="text">The text containing action tags</param>
        /// <returns>List of parsed AgentAction objects</returns>
        public static List<AgentAction> ParseActions(string text)
        {
            var actions = new List<AgentAction>();

            if (string.IsNullOrEmpty(text))
            {
                return actions;
            }

            var matches = ActionPattern.Matches(text);

            foreach (Match match in matches)
            {
                try
                {
                    var action = ParseActionFromMatch(match);
                    if (action != null && !string.IsNullOrEmpty(action.Name))
                    {
                        actions.Add(action);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[ActionParser] Failed to parse action: {e.Message}");
                }
            }

            return actions;
        }

        /// <summary>
        /// Parse a single action from a regex match.
        /// </summary>
        private static AgentAction ParseActionFromMatch(Match match)
        {
            var attributesString = match.Groups[1].Value;
            var attributes = ParseAttributes(attributesString);

            // The "name" attribute is required
            if (!attributes.TryGetValue("name", out var actionName))
            {
                Debug.LogWarning($"[ActionParser] Action tag missing 'name' attribute: {match.Value}");
                return null;
            }

            // Remove "name" from parameters since it's the action identifier
            attributes.Remove("name");

            return new AgentAction(actionName, attributes);
        }

        /// <summary>
        /// Parse attributes from an attributes string.
        /// </summary>
        private static Dictionary<string, string> ParseAttributes(string attributesString)
        {
            var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var matches = AttributePattern.Matches(attributesString);
            foreach (Match match in matches)
            {
                var key = match.Groups[1].Value;
                var value = match.Groups[2].Value;
                attributes[key] = value;
            }

            return attributes;
        }

        /// <summary>
        /// Remove all action tags from a text string, returning only the clean text.
        /// </summary>
        /// <param name="text">The text containing action tags</param>
        /// <returns>Text with all action tags removed</returns>
        public static string StripActions(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            // Remove action tags and clean up extra whitespace
            var result = ActionPattern.Replace(text, "");
            
            // Clean up multiple consecutive spaces
            result = Regex.Replace(result, @"\s{2,}", " ");
            
            // Trim leading/trailing whitespace
            return result.Trim();
        }

        /// <summary>
        /// Parse actions from text and return both the actions and the cleaned text.
        /// </summary>
        /// <param name="text">The text containing action tags</param>
        /// <returns>Tuple of (actions list, cleaned text)</returns>
        public static (List<AgentAction> actions, string cleanText) ParseAndStrip(string text)
        {
            var actions = ParseActions(text);
            var cleanText = StripActions(text);
            return (actions, cleanText);
        }

        /// <summary>
        /// Check if a text string contains any action tags.
        /// </summary>
        /// <param name="text">The text to check</param>
        /// <returns>True if the text contains action tags</returns>
        public static bool ContainsActions(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            return ActionPattern.IsMatch(text);
        }

        /// <summary>
        /// Count the number of action tags in a text string.
        /// </summary>
        /// <param name="text">The text to check</param>
        /// <returns>Number of action tags found</returns>
        public static int CountActions(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0;
            }

            return ActionPattern.Matches(text).Count;
        }
    }
}



