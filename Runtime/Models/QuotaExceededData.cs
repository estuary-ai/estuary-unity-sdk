using System;
using UnityEngine;

namespace Estuary.Models
{
    /// <summary>
    /// Data received when the API key owner has exceeded their monthly interaction quota.
    /// </summary>
    [Serializable]
    public class QuotaExceededData
    {
        [SerializeField] private string error;
        [SerializeField] private string message;
        [SerializeField] private int current;
        [SerializeField] private int limit;
        [SerializeField] private int remaining;
        [SerializeField] private string tier;

        /// <summary>
        /// Error code (always "quota_exceeded").
        /// </summary>
        public string Error => error;

        /// <summary>
        /// Human-readable error message.
        /// </summary>
        public string Message => message;

        /// <summary>
        /// Current number of interactions used this month.
        /// </summary>
        public int Current => current;

        /// <summary>
        /// Maximum number of interactions allowed per month.
        /// </summary>
        public int Limit => limit;

        /// <summary>
        /// Remaining interactions this month.
        /// </summary>
        public int Remaining => remaining;

        /// <summary>
        /// User's subscription tier (e.g., "free", "pro", "enterprise").
        /// </summary>
        public string Tier => tier;

        public QuotaExceededData() { }

        /// <summary>
        /// Create QuotaExceededData from JSON string.
        /// </summary>
        public static QuotaExceededData FromJson(string json)
        {
            return JsonUtility.FromJson<QuotaExceededData>(json);
        }

        public override string ToString()
        {
            return $"QuotaExceededData(Current={Current}/{Limit}, Tier={Tier}, Message={Message})";
        }
    }
}
