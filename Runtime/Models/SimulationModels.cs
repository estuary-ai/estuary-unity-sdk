using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Estuary.Models
{
    // =====================================================================
    // Simulation (v1) wire models — REST API — Simulation (v1) in
    // SDK_CONTRACT.md. camelCase JSON matching the gateway's to_dict()
    // serializers; grouped in one file like AgentResponse.cs because they
    // form a single cohesive wire surface.
    // =====================================================================

    /// <summary>
    /// A simulation world: a group of the developer's characters plus seed
    /// relationships and lore. Matches World.to_dict().
    /// </summary>
    public class SimulationWorld
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("userId")] public string UserId;
        [JsonProperty("name")] public string Name;
        [JsonProperty("description")] public string Description;

        /// <summary>tick_rate_seconds, max_events_per_cycle, compute_budget_daily, active_hours.</summary>
        [JsonProperty("simulationConfig")] public JObject SimulationConfig;

        [JsonProperty("seedLore")] public List<string> SeedLore;

        /// <summary>"draft" | "active" | "archived". Worlds start draft; the first instance auto-activates them.</summary>
        [JsonProperty("status")] public string Status;

        [JsonProperty("createdAt")] public string CreatedAt;
        [JsonProperty("updatedAt")] public string UpdatedAt;

        /// <summary>Only populated by GetWorld (null on create/list/update responses).</summary>
        [JsonProperty("characters")] public List<SimulationWorldCharacter> Characters;

        /// <summary>Only populated by GetWorld (null on create/list/update responses).</summary>
        [JsonProperty("relationships")] public List<SimulationRelationship> Relationships;
    }

    /// <summary>A character's membership in a world. Matches WorldAgent.to_dict().</summary>
    public class SimulationWorldCharacter
    {
        [JsonProperty("worldId")] public string WorldId;
        [JsonProperty("agentId")] public string AgentId;
        [JsonProperty("roleInWorld")] public string RoleInWorld;
    }

    /// <summary>
    /// Result of ClearWorldMemories (DELETE /worlds/{id}/memories): how many
    /// simulation-created memories were deleted across the world's instances.
    /// </summary>
    public class SimulationWorldMemoriesCleared
    {
        [JsonProperty("worldId")] public string WorldId;
        [JsonProperty("memoriesDeleted")] public int MemoriesDeleted;
    }

    /// <summary>
    /// A relationship between two characters. Covers both seed (world-level:
    /// WorldId + Description set) and evolved (instance-level: InstanceId +
    /// UpdatedAt set) relationships — the unused fields are null.
    /// </summary>
    public class SimulationRelationship
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("worldId")] public string WorldId;
        [JsonProperty("instanceId")] public string InstanceId;
        [JsonProperty("sourceAgentId")] public string SourceAgentId;
        [JsonProperty("targetAgentId")] public string TargetAgentId;
        [JsonProperty("relationshipType")] public string RelationshipType;
        [JsonProperty("attributes")] public Dictionary<string, float> Attributes;
        [JsonProperty("description")] public string Description;
        [JsonProperty("updatedAt")] public string UpdatedAt;
    }

    /// <summary>
    /// A per-player fork of a world where state evolves independently.
    /// Matches WorldInstance.to_dict(). The world-view document itself is NOT
    /// carried here — fetch it via GetWorldView; only WorldViewUpdatedAt is.
    /// </summary>
    public class SimulationInstance
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("worldId")] public string WorldId;
        [JsonProperty("userId")] public string UserId;
        [JsonProperty("playerId")] public string PlayerId;

        /// <summary>"active" | "paused". Instances are created paused; active ones generate autonomous (billable) events.</summary>
        [JsonProperty("status")] public string Status;

        [JsonProperty("simulationState")] public JObject SimulationState;

        /// <summary>ISO timestamp of the last world-view rewrite; null until the first conversation completes.</summary>
        [JsonProperty("worldViewUpdatedAt")] public string WorldViewUpdatedAt;

        [JsonProperty("createdAt")] public string CreatedAt;
        [JsonProperty("updatedAt")] public string UpdatedAt;

        /// <summary>Evolved relationships. Only populated by GetInstance.</summary>
        [JsonProperty("relationships")] public List<SimulationRelationship> Relationships;

        /// <summary>Last 10 events, newest first. Only populated by GetInstance.</summary>
        [JsonProperty("recentEvents")] public List<SimulationEvent> RecentEvents;
    }

    /// <summary>
    /// A (scheduled or completed) conversation event in an instance.
    /// Matches InstanceEvent.to_dict(); trigger responses additionally carry Mode.
    /// </summary>
    public class SimulationEvent
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("instanceId")] public string InstanceId;
        [JsonProperty("sourceAgentId")] public string SourceAgentId;

        /// <summary>Null for group conversations (participants are in Extra.participant_ids).</summary>
        [JsonProperty("targetAgentId")] public string TargetAgentId;

        /// <summary>"scheduled_message" for pair, "group_conversation" for group triggers.</summary>
        [JsonProperty("eventType")] public string EventType;

        [JsonProperty("intent")] public string Intent;

        /// <summary>"low" | "normal" | "high" | "urgent".</summary>
        [JsonProperty("priority")] public string Priority;

        [JsonProperty("scheduledAt")] public string ScheduledAt;

        /// <summary>"pending" | "processing" | "completed" | "failed" | "cancelled".</summary>
        [JsonProperty("status")] public string Status;

        /// <summary>Set once completed — fetch the transcript via GetConversation.</summary>
        [JsonProperty("resultConversationId")] public string ResultConversationId;

        [JsonProperty("extra")] public JObject Extra;
        [JsonProperty("retryCount")] public int RetryCount;
        [JsonProperty("lastError")] public string LastError;
        [JsonProperty("createdAt")] public string CreatedAt;

        /// <summary>"immediate" | "scheduled". Only present on the trigger response.</summary>
        [JsonProperty("mode")] public string Mode;
    }

    /// <summary>One entry of an instance's lore log. Matches InstanceLoreLog.to_dict().</summary>
    public class SimulationLoreEntry
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("instanceId")] public string InstanceId;
        [JsonProperty("content")] public string Content;

        /// <summary>"simulation" | "user_interaction" | "seed".</summary>
        [JsonProperty("eventSource")] public string EventSource;

        [JsonProperty("sourceConversationId")] public string SourceConversationId;
        [JsonProperty("visibility")] public string Visibility;
        [JsonProperty("createdAt")] public string CreatedAt;
    }

    /// <summary>
    /// The instance's living world-view document (markdown chronicle of what
    /// is going on in the world). Markdown is null until the first
    /// conversation completes.
    /// </summary>
    public class SimulationWorldView
    {
        [JsonProperty("instanceId")] public string InstanceId;
        [JsonProperty("markdown")] public string Markdown;
        [JsonProperty("updatedAt")] public string UpdatedAt;
    }

    /// <summary>Transcript of a completed simulation conversation.</summary>
    public class SimulationConversation
    {
        [JsonProperty("conversationId")] public string ConversationId;
        [JsonProperty("sourceCharacterId")] public string SourceCharacterId;
        [JsonProperty("targetCharacterId")] public string TargetCharacterId;

        /// <summary>Set for group conversations, null for pairs.</summary>
        [JsonProperty("participantIds")] public List<string> ParticipantIds;

        [JsonProperty("messages")] public List<SimulationConversationMessage> Messages;
    }

    /// <summary>One transcript message. Matches PlayerMessage.to_dict().</summary>
    public class SimulationConversationMessage
    {
        [JsonProperty("id")] public int Id;
        [JsonProperty("conversationId")] public string ConversationId;
        [JsonProperty("role")] public string Role;
        [JsonProperty("content")] public string Content;
        [JsonProperty("timestamp")] public string Timestamp;
        [JsonProperty("imageUrl")] public string ImageUrl;
        [JsonProperty("imageDescription")] public string ImageDescription;
    }

    // ---------------------------------------------------------------------
    // Paginated list responses
    // ---------------------------------------------------------------------

    public class SimulationWorldList
    {
        [JsonProperty("worlds")] public List<SimulationWorld> Worlds;
        [JsonProperty("total")] public int Total;
        [JsonProperty("limit")] public int Limit;
        [JsonProperty("offset")] public int Offset;
    }

    public class SimulationInstanceList
    {
        [JsonProperty("instances")] public List<SimulationInstance> Instances;
        [JsonProperty("total")] public int Total;
        [JsonProperty("limit")] public int Limit;
        [JsonProperty("offset")] public int Offset;
    }

    public class SimulationEventList
    {
        [JsonProperty("events")] public List<SimulationEvent> Events;
        [JsonProperty("total")] public int Total;
        [JsonProperty("limit")] public int Limit;
        [JsonProperty("offset")] public int Offset;
    }

    public class SimulationLoreList
    {
        [JsonProperty("lore")] public List<SimulationLoreEntry> Lore;
        [JsonProperty("total")] public int Total;
        [JsonProperty("limit")] public int Limit;
        [JsonProperty("offset")] public int Offset;
    }

    // ---------------------------------------------------------------------
    // Request bodies. The server rejects unknown fields (extra="forbid"),
    // and EstuarySimulationApi serializes these with NullValueHandling.Ignore
    // so omitted optionals fall back to server defaults.
    // ---------------------------------------------------------------------

    /// <summary>Body for CreateWorld.</summary>
    public class SimulationWorldCreateRequest
    {
        [JsonProperty("name")] public string Name;
        [JsonProperty("description")] public string Description;
        [JsonProperty("simulationConfig")] public JObject SimulationConfig;
        [JsonProperty("seedLore")] public List<string> SeedLore;
        [JsonProperty("characters")] public List<SimulationCharacterSpec> Characters;
        [JsonProperty("relationships")] public List<SimulationRelationshipSpec> Relationships;
    }

    /// <summary>Body for UpdateWorld. Only non-null fields are sent (simulationConfig merges, seedLore replaces).</summary>
    public class SimulationWorldUpdateRequest
    {
        [JsonProperty("name")] public string Name;
        [JsonProperty("description")] public string Description;

        /// <summary>"draft" | "active" | "archived".</summary>
        [JsonProperty("status")] public string Status;

        [JsonProperty("simulationConfig")] public JObject SimulationConfig;
        [JsonProperty("seedLore")] public List<string> SeedLore;
    }

    /// <summary>A character to add to a world.</summary>
    public class SimulationCharacterSpec
    {
        [JsonProperty("characterId")] public string CharacterId;
        [JsonProperty("roleInWorld")] public string RoleInWorld;

        public SimulationCharacterSpec() { }

        public SimulationCharacterSpec(string characterId, string roleInWorld = null)
        {
            CharacterId = characterId;
            RoleInWorld = roleInWorld;
        }
    }

    /// <summary>A seed relationship between two world characters (both must be members).</summary>
    public class SimulationRelationshipSpec
    {
        [JsonProperty("sourceCharacterId")] public string SourceCharacterId;
        [JsonProperty("targetCharacterId")] public string TargetCharacterId;
        [JsonProperty("relationshipType")] public string RelationshipType;
        [JsonProperty("attributes")] public Dictionary<string, float> Attributes;
        [JsonProperty("description")] public string Description;
    }

    /// <summary>
    /// Body for TriggerConversation. Set SourceCharacterId + TargetCharacterId
    /// for a pair, or ParticipantIds (2–6) for a group — not both. Without
    /// ScheduledAt the conversation runs immediately; with a future
    /// ScheduledAt (UTC, ≤7 days) it is queued for the engine.
    /// </summary>
    public class SimulationConversationTrigger
    {
        [JsonProperty("sourceCharacterId")] public string SourceCharacterId;
        [JsonProperty("targetCharacterId")] public string TargetCharacterId;
        [JsonProperty("participantIds")] public List<string> ParticipantIds;

        /// <summary>What the conversation should be about. Required.</summary>
        [JsonProperty("intent")] public string Intent;

        /// <summary>Use UTC (DateTime.UtcNow-based); serialized as ISO 8601.</summary>
        [JsonProperty("scheduledAt")] public DateTime? ScheduledAt;

        /// <summary>"low" | "normal" | "high" | "urgent". Null → server default "normal".</summary>
        [JsonProperty("priority")] public string Priority;
    }

    // ---------------------------------------------------------------------
    // /sim-v1 streaming payloads (same shapes as /demo-sim)
    // ---------------------------------------------------------------------

    /// <summary>simulation_message — one character line, streamed as it is generated.</summary>
    public class SimulationStreamMessage
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("agentId")] public string AgentId;
        [JsonProperty("agentName")] public string AgentName;
        [JsonProperty("text")] public string Text;
        [JsonProperty("turn")] public int Turn;

        /// <summary>Milliseconds since epoch.</summary>
        [JsonProperty("timestamp")] public long Timestamp;
    }

    /// <summary>simulation_tool_call — a character used a tool (schedule_message / update_relationship / remember).</summary>
    public class SimulationToolCall
    {
        [JsonProperty("agentName")] public string AgentName;
        [JsonProperty("toolName")] public string ToolName;
        [JsonProperty("arguments")] public JObject Arguments;
    }
}
