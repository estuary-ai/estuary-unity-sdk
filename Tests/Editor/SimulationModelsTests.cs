using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Estuary.Models;

namespace Estuary.Tests
{
    /// <summary>
    /// Wire-contract tests for the Simulation (v1) Newtonsoft models
    /// (Runtime/Models/SimulationModels.cs). These assert the camelCase
    /// JsonProperty mapping matches the gateway's to_dict() serializers and
    /// that request bodies serialize the way EstuarySimulationApi sends them
    /// (NullValueHandling.Ignore, so omitted optionals fall back to server
    /// defaults). Pure C# + Newtonsoft — no UnityEngine — so they also run
    /// headless under Mono.
    /// </summary>
    [TestFixture]
    public class SimulationModelsTests
    {
        // Serializer settings that mirror EstuarySimulationApi request encoding.
        private static JsonSerializerSettings IgnoreNulls =>
            new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore };

        // -----------------------------------------------------------------
        // Response deserialization (server -> SDK)
        // -----------------------------------------------------------------

        [Test]
        public void SimulationWorld_DeserializesCamelCaseAndNestedCollections()
        {
            const string json = @"{
                ""id"": ""w1"",
                ""userId"": ""u1"",
                ""name"": ""Atlantis"",
                ""description"": ""a sunken city"",
                ""simulationConfig"": { ""tick_rate_seconds"": 60, ""active_hours"": [9, 17] },
                ""seedLore"": [""the tide remembers"", ""salt keeps secrets""],
                ""status"": ""draft"",
                ""createdAt"": ""2026-07-19T00:00:00Z"",
                ""updatedAt"": ""2026-07-19T01:00:00Z"",
                ""characters"": [
                    { ""worldId"": ""w1"", ""agentId"": ""a1"", ""roleInWorld"": ""prophet"", ""motive"": ""warn the city"" }
                ],
                ""relationships"": [
                    { ""id"": ""r1"", ""worldId"": ""w1"", ""sourceAgentId"": ""a1"", ""targetAgentId"": ""a2"",
                      ""relationshipType"": ""rival"", ""attributes"": { ""trust"": 0.2, ""fear"": 0.8 },
                      ""description"": ""old grudge"" }
                ]
            }";

            var world = JsonConvert.DeserializeObject<SimulationWorld>(json);

            Assert.AreEqual("w1", world.Id);
            Assert.AreEqual("u1", world.UserId);
            Assert.AreEqual("Atlantis", world.Name);
            Assert.AreEqual("draft", world.Status);
            Assert.AreEqual(2, world.SeedLore.Count);
            Assert.AreEqual("the tide remembers", world.SeedLore[0]);
            // JObject config preserved verbatim.
            Assert.AreEqual(60, (int)world.SimulationConfig["tick_rate_seconds"]);
            // Nested character carries the v1.7 seed motive.
            Assert.AreEqual(1, world.Characters.Count);
            Assert.AreEqual("prophet", world.Characters[0].RoleInWorld);
            Assert.AreEqual("warn the city", world.Characters[0].Motive);
            // Nested relationship with float attributes.
            Assert.AreEqual(1, world.Relationships.Count);
            Assert.AreEqual("rival", world.Relationships[0].RelationshipType);
            Assert.AreEqual(0.8f, world.Relationships[0].Attributes["fear"]);
        }

        [Test]
        public void SimulationWorld_ListOmitsNestedCollections_LeavesThemNull()
        {
            // create/list/update responses do not populate characters/relationships.
            const string json = @"{ ""id"": ""w1"", ""name"": ""x"", ""status"": ""active"" }";
            var world = JsonConvert.DeserializeObject<SimulationWorld>(json);
            Assert.IsNull(world.Characters);
            Assert.IsNull(world.Relationships);
        }

        [Test]
        public void SimulationInstance_DeserializesCharacterMotivesMapAndWorldViewTimestamp()
        {
            const string json = @"{
                ""id"": ""i1"",
                ""worldId"": ""w1"",
                ""userId"": ""u1"",
                ""playerId"": ""p1"",
                ""status"": ""active"",
                ""simulationState"": { ""cycle"": 3 },
                ""worldViewUpdatedAt"": ""2026-07-19T02:00:00Z"",
                ""characterMotives"": { ""a1"": ""protect the heir"", ""a2"": ""seize the throne"" },
                ""createdAt"": ""2026-07-19T00:00:00Z"",
                ""updatedAt"": ""2026-07-19T02:00:00Z""
            }";

            var inst = JsonConvert.DeserializeObject<SimulationInstance>(json);

            Assert.AreEqual("i1", inst.Id);
            Assert.AreEqual("p1", inst.PlayerId);
            Assert.AreEqual("active", inst.Status);
            Assert.AreEqual("2026-07-19T02:00:00Z", inst.WorldViewUpdatedAt);
            Assert.AreEqual(3, (int)inst.SimulationState["cycle"]);
            Assert.AreEqual(2, inst.CharacterMotives.Count);
            Assert.AreEqual("protect the heir", inst.CharacterMotives["a1"]);
            Assert.AreEqual("seize the throne", inst.CharacterMotives["a2"]);
            // recentEvents/relationships only populated by GetInstance.
            Assert.IsNull(inst.RecentEvents);
        }

        [Test]
        public void SimulationInstance_NullWorldViewUpdatedAt_BeforeFirstConversation()
        {
            const string json = @"{ ""id"": ""i1"", ""status"": ""paused"", ""worldViewUpdatedAt"": null }";
            var inst = JsonConvert.DeserializeObject<SimulationInstance>(json);
            Assert.IsNull(inst.WorldViewUpdatedAt);
            Assert.AreEqual("paused", inst.Status);
        }

        [Test]
        public void SimulationEvent_DeserializesAllFieldsIncludingTriggerMode()
        {
            const string json = @"{
                ""id"": ""e1"",
                ""instanceId"": ""i1"",
                ""sourceAgentId"": ""a1"",
                ""targetAgentId"": ""a2"",
                ""eventType"": ""scheduled_message"",
                ""intent"": ""apologize"",
                ""priority"": ""high"",
                ""scheduledAt"": ""2026-07-20T00:00:00Z"",
                ""status"": ""pending"",
                ""resultConversationId"": null,
                ""extra"": { ""participant_ids"": [""a1"", ""a2""] },
                ""retryCount"": 2,
                ""lastError"": ""timeout"",
                ""createdAt"": ""2026-07-19T00:00:00Z"",
                ""mode"": ""immediate""
            }";

            var ev = JsonConvert.DeserializeObject<SimulationEvent>(json);

            Assert.AreEqual("e1", ev.Id);
            Assert.AreEqual("a2", ev.TargetAgentId);
            Assert.AreEqual("scheduled_message", ev.EventType);
            Assert.AreEqual("high", ev.Priority);
            Assert.AreEqual("pending", ev.Status);
            Assert.IsNull(ev.ResultConversationId);
            Assert.AreEqual(2, ev.RetryCount);
            Assert.AreEqual("timeout", ev.LastError);
            Assert.AreEqual("immediate", ev.Mode);
            Assert.IsNotNull(ev.Extra["participant_ids"]);
        }

        [Test]
        public void SimulationWorldMemoriesCleared_DeserializesCount()
        {
            const string json = @"{ ""worldId"": ""w1"", ""memoriesDeleted"": 42 }";
            var cleared = JsonConvert.DeserializeObject<SimulationWorldMemoriesCleared>(json);
            Assert.AreEqual("w1", cleared.WorldId);
            Assert.AreEqual(42, cleared.MemoriesDeleted);
        }

        [Test]
        public void SimulationRelationship_SupportsBothSeedAndEvolvedShapes()
        {
            // Seed (world-level): worldId + description set, instanceId null.
            var seed = JsonConvert.DeserializeObject<SimulationRelationship>(
                @"{ ""id"": ""r1"", ""worldId"": ""w1"", ""sourceAgentId"": ""a1"",
                    ""targetAgentId"": ""a2"", ""relationshipType"": ""ally"",
                    ""description"": ""sworn"" }");
            Assert.AreEqual("w1", seed.WorldId);
            Assert.IsNull(seed.InstanceId);
            Assert.AreEqual("sworn", seed.Description);

            // Evolved (instance-level): instanceId + updatedAt set.
            var evolved = JsonConvert.DeserializeObject<SimulationRelationship>(
                @"{ ""id"": ""r2"", ""instanceId"": ""i1"", ""sourceAgentId"": ""a1"",
                    ""targetAgentId"": ""a2"", ""relationshipType"": ""rival"",
                    ""attributes"": { ""tension"": 0.9 }, ""updatedAt"": ""2026-07-19T00:00:00Z"" }");
            Assert.AreEqual("i1", evolved.InstanceId);
            Assert.IsNull(evolved.WorldId);
            Assert.AreEqual(0.9f, evolved.Attributes["tension"]);
        }

        [Test]
        public void SimulationConversation_DeserializesGroupTranscript()
        {
            const string json = @"{
                ""conversationId"": ""c1"",
                ""sourceCharacterId"": ""a1"",
                ""targetCharacterId"": null,
                ""participantIds"": [""a1"", ""a2"", ""a3""],
                ""messages"": [
                    { ""id"": 1, ""conversationId"": ""c1"", ""role"": ""assistant"",
                      ""content"": ""hello"", ""timestamp"": ""2026-07-19T00:00:00Z"" },
                    { ""id"": 2, ""conversationId"": ""c1"", ""role"": ""assistant"",
                      ""content"": ""what news?"", ""timestamp"": ""2026-07-19T00:00:01Z"",
                      ""imageUrl"": ""http://x/y.png"", ""imageDescription"": ""a map"" }
                ]
            }";

            var convo = JsonConvert.DeserializeObject<SimulationConversation>(json);
            Assert.IsNull(convo.TargetCharacterId);
            Assert.AreEqual(3, convo.ParticipantIds.Count);
            Assert.AreEqual(2, convo.Messages.Count);
            Assert.AreEqual(1, convo.Messages[0].Id);
            Assert.AreEqual("a map", convo.Messages[1].ImageDescription);
        }

        [Test]
        public void SimulationStreamMessage_MapsLongTimestampAndIntTurn()
        {
            const string json = @"{ ""id"": ""m1"", ""agentId"": ""a1"", ""agentName"": ""Nyx"",
                ""text"": ""the sea rises"", ""turn"": 4, ""timestamp"": 1721433600000 }";
            var msg = JsonConvert.DeserializeObject<SimulationStreamMessage>(json);
            Assert.AreEqual("Nyx", msg.AgentName);
            Assert.AreEqual(4, msg.Turn);
            Assert.AreEqual(1721433600000L, msg.Timestamp);
        }

        [Test]
        public void SimulationToolCall_PreservesArgumentsJObject()
        {
            const string json = @"{ ""agentName"": ""Nyx"", ""toolName"": ""schedule_message"",
                ""arguments"": { ""target"": ""a2"", ""intent"": ""warn"", ""delay_hours"": 3 } }";
            var call = JsonConvert.DeserializeObject<SimulationToolCall>(json);
            Assert.AreEqual("schedule_message", call.ToolName);
            Assert.AreEqual("a2", (string)call.Arguments["target"]);
            Assert.AreEqual(3, (int)call.Arguments["delay_hours"]);
        }

        [Test]
        public void PaginatedLists_Deserialize()
        {
            var worlds = JsonConvert.DeserializeObject<SimulationWorldList>(
                @"{ ""worlds"": [{ ""id"": ""w1"" }], ""total"": 1, ""limit"": 20, ""offset"": 0 }");
            Assert.AreEqual(1, worlds.Total);
            Assert.AreEqual(1, worlds.Worlds.Count);

            var events = JsonConvert.DeserializeObject<SimulationEventList>(
                @"{ ""events"": [], ""total"": 0, ""limit"": 50, ""offset"": 10 }");
            Assert.AreEqual(0, events.Events.Count);
            Assert.AreEqual(50, events.Limit);
            Assert.AreEqual(10, events.Offset);
        }

        [Test]
        public void SimulationLoreEntry_Deserializes()
        {
            var lore = JsonConvert.DeserializeObject<SimulationLoreEntry>(
                @"{ ""id"": ""l1"", ""instanceId"": ""i1"", ""content"": ""the flood came"",
                    ""eventSource"": ""simulation"", ""visibility"": ""public"",
                    ""createdAt"": ""2026-07-19T00:00:00Z"" }");
            Assert.AreEqual("the flood came", lore.Content);
            Assert.AreEqual("simulation", lore.EventSource);
        }

        [Test]
        public void SimulationWorldView_MarkdownNullBeforeFirstConversation()
        {
            var view = JsonConvert.DeserializeObject<SimulationWorldView>(
                @"{ ""instanceId"": ""i1"", ""markdown"": null, ""updatedAt"": null }");
            Assert.AreEqual("i1", view.InstanceId);
            Assert.IsNull(view.Markdown);
        }

        // -----------------------------------------------------------------
        // Request serialization (SDK -> server), NullValueHandling.Ignore
        // -----------------------------------------------------------------

        [Test]
        public void WorldCreateRequest_OmitsNullOptionalFields()
        {
            var req = new SimulationWorldCreateRequest { Name = "Atlantis" };
            string json = JsonConvert.SerializeObject(req, IgnoreNulls);
            var parsed = JObject.Parse(json);

            Assert.AreEqual("Atlantis", (string)parsed["name"]);
            // description/simulationConfig/seedLore/characters/relationships were null -> omitted.
            Assert.IsFalse(parsed.ContainsKey("description"));
            Assert.IsFalse(parsed.ContainsKey("simulationConfig"));
            Assert.IsFalse(parsed.ContainsKey("seedLore"));
            Assert.IsFalse(parsed.ContainsKey("characters"));
            Assert.IsFalse(parsed.ContainsKey("relationships"));
        }

        [Test]
        public void CharacterSpec_Constructor_SetsFieldsAndSerializesCamelCase()
        {
            var spec = new SimulationCharacterSpec("a1", "prophet", "warn the city");
            Assert.AreEqual("a1", spec.CharacterId);
            Assert.AreEqual("prophet", spec.RoleInWorld);
            Assert.AreEqual("warn the city", spec.Motive);

            var parsed = JObject.Parse(JsonConvert.SerializeObject(spec, IgnoreNulls));
            Assert.AreEqual("a1", (string)parsed["characterId"]);
            Assert.AreEqual("prophet", (string)parsed["roleInWorld"]);
            Assert.AreEqual("warn the city", (string)parsed["motive"]);
        }

        [Test]
        public void CharacterSpec_MinimalConstructor_OmitsNullMotiveAndRole()
        {
            var spec = new SimulationCharacterSpec("a1");
            var parsed = JObject.Parse(JsonConvert.SerializeObject(spec, IgnoreNulls));
            Assert.AreEqual("a1", (string)parsed["characterId"]);
            Assert.IsFalse(parsed.ContainsKey("roleInWorld"));
            Assert.IsFalse(parsed.ContainsKey("motive"));
        }

        [Test]
        public void ConversationTrigger_PairWithoutScheduledAt_OmitsSchedule()
        {
            var trigger = new SimulationConversationTrigger
            {
                SourceCharacterId = "a1",
                TargetCharacterId = "a2",
                Intent = "reconcile",
            };
            var parsed = JObject.Parse(JsonConvert.SerializeObject(trigger, IgnoreNulls));
            Assert.AreEqual("a1", (string)parsed["sourceCharacterId"]);
            Assert.AreEqual("a2", (string)parsed["targetCharacterId"]);
            Assert.AreEqual("reconcile", (string)parsed["intent"]);
            // Immediate run: no scheduledAt, no participantIds, no priority.
            Assert.IsFalse(parsed.ContainsKey("scheduledAt"));
            Assert.IsFalse(parsed.ContainsKey("participantIds"));
            Assert.IsFalse(parsed.ContainsKey("priority"));
        }

        [Test]
        public void ConversationTrigger_ScheduledAt_SerializesRoundTrips()
        {
            var when = new DateTime(2026, 7, 20, 12, 0, 0, DateTimeKind.Utc);
            var trigger = new SimulationConversationTrigger
            {
                SourceCharacterId = "a1",
                TargetCharacterId = "a2",
                Intent = "warn",
                ScheduledAt = when,
                Priority = "urgent",
            };
            string json = JsonConvert.SerializeObject(trigger, IgnoreNulls);
            var parsed = JObject.Parse(json);
            Assert.IsTrue(parsed.ContainsKey("scheduledAt"));
            Assert.AreEqual("urgent", (string)parsed["priority"]);

            // Round-trips back to the same UTC instant.
            var back = JsonConvert.DeserializeObject<SimulationConversationTrigger>(json);
            Assert.IsTrue(back.ScheduledAt.HasValue);
            Assert.AreEqual(when.ToUniversalTime(), back.ScheduledAt.Value.ToUniversalTime());
        }

        [Test]
        public void ConversationTrigger_GroupParticipants_Serialize()
        {
            var trigger = new SimulationConversationTrigger
            {
                ParticipantIds = new List<string> { "a1", "a2", "a3" },
                Intent = "town hall",
            };
            var parsed = JObject.Parse(JsonConvert.SerializeObject(trigger, IgnoreNulls));
            Assert.AreEqual(3, ((JArray)parsed["participantIds"]).Count);
            Assert.IsFalse(parsed.ContainsKey("sourceCharacterId"));
        }

        [Test]
        public void WorldUpdateRequest_SendsOnlyProvidedFields()
        {
            var req = new SimulationWorldUpdateRequest { Status = "archived" };
            var parsed = JObject.Parse(JsonConvert.SerializeObject(req, IgnoreNulls));
            Assert.AreEqual("archived", (string)parsed["status"]);
            Assert.IsFalse(parsed.ContainsKey("name"));
            Assert.IsFalse(parsed.ContainsKey("seedLore"));
        }

        [Test]
        public void RelationshipSpec_SerializesAttributesAndAgents()
        {
            var spec = new SimulationRelationshipSpec
            {
                SourceCharacterId = "a1",
                TargetCharacterId = "a2",
                RelationshipType = "mentor",
                Attributes = new Dictionary<string, float> { { "respect", 0.7f } },
                Description = "teacher and student",
            };
            var parsed = JObject.Parse(JsonConvert.SerializeObject(spec, IgnoreNulls));
            Assert.AreEqual("mentor", (string)parsed["relationshipType"]);
            Assert.AreEqual(0.7f, (float)parsed["attributes"]["respect"]);
        }

        [Test]
        public void FullRoundTrip_WorldWithCharacters_Survives()
        {
            var original = new SimulationWorldCreateRequest
            {
                Name = "Atlantis",
                Description = "sunken",
                SeedLore = new List<string> { "the tide remembers" },
                Characters = new List<SimulationCharacterSpec>
                {
                    new SimulationCharacterSpec("a1", "prophet", "warn"),
                    new SimulationCharacterSpec("a2"),
                },
            };
            string json = JsonConvert.SerializeObject(original, IgnoreNulls);
            var back = JsonConvert.DeserializeObject<SimulationWorldCreateRequest>(json);

            Assert.AreEqual("Atlantis", back.Name);
            Assert.AreEqual(1, back.SeedLore.Count);
            Assert.AreEqual(2, back.Characters.Count);
            Assert.AreEqual("warn", back.Characters[0].Motive);
            Assert.IsNull(back.Characters[1].Motive);
        }
    }
}
