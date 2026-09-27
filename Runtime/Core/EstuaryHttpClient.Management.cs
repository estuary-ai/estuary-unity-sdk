using System;
using System.Collections;
using Estuary.Models;
using Newtonsoft.Json.Linq;

namespace Estuary
{
    public partial class EstuaryHttpClient
    {
        /// <summary>List/search a page of characters (v1). GetAgents preserves its older unpaginated contract.</summary>
        public IEnumerator ListCharacters(Action<CharacterList> onSuccess, Action<string> onError, int limit = 20, int offset = 0, string search = null)
            => Send("GET", "/api/v1/characters" + Query("limit", limit, "offset", offset, "search", search), null, onSuccess, onError);

        /// <summary>Get one accessible character, including model and provider configuration.</summary>
        public IEnumerator GetCharacter(string characterId, Action<AgentResponse> onSuccess, Action<string> onError)
            => Send("GET", CharacterPath(characterId), null, onSuccess, onError);

        /// <summary>Create a character using the v1 persona/llm/tts/stt/vad/actions JSON schema.</summary>
        public IEnumerator CreateCharacter(JObject body, Action<AgentResponse> onSuccess, Action<string> onError)
            => Send("POST", "/api/v1/characters", body, onSuccess, onError);

        /// <summary>Update only the fields supplied in the v1 write schema. Explicit JSON nulls are preserved.</summary>
        public IEnumerator UpdateCharacter(string characterId, JObject body, Action<AgentResponse> onSuccess, Action<string> onError)
            => Send("PUT", CharacterPath(characterId), body, onSuccess, onError);

        /// <summary>Test a proposed character API tool using the backend's validation and request normalization.</summary>
        public IEnumerator TestApiEndpoint(string characterId, JObject endpoint, JObject arguments, Action<JObject> onSuccess, Action<string> onError)
            => Send("POST", CharacterPath(characterId) + "/api-endpoints/test", new { endpoint, arguments }, onSuccess, onError);

        /// <summary>Move a character to an organization or personal account. Server enforces membership and edit access.</summary>
        public IEnumerator TransferCharacter(string characterId, string destination, Action<AgentResponse> onSuccess,
            Action<string> onError, string orgId = null, string recipientUserId = null)
            => Send("POST", CharacterPath(characterId) + "/transfer", new { destination, orgId, recipientUserId }, onSuccess, onError);

        /// <summary>Read a private seed motive and, optionally, its live player-specific override. Owner-only.</summary>
        public IEnumerator GetCharacterMotive(string characterId, Action<CharacterMotive> onSuccess, Action<string> onError, string playerId = null)
            => Send("GET", CharacterPath(characterId) + "/motive" + Query("playerId", playerId), null, onSuccess, onError);

        /// <summary>Override the private motive for one character/player relationship.</summary>
        public IEnumerator SetCharacterMotive(string characterId, string playerId, string motive, Action<CharacterMotive> onSuccess, Action<string> onError)
            => Send("PATCH", CharacterPath(characterId) + "/motive" + Query("playerId", playerId), new { motive }, onSuccess, onError);

        /// <summary>Reset a relationship's private motive to the character seed.</summary>
        public IEnumerator ResetCharacterMotive(string characterId, string playerId, Action onSuccess, Action<string> onError)
            => SendNoContent("DELETE", CharacterPath(characterId) + "/motive" + Query("playerId", playerId), null, onSuccess, onError);

        /// <summary>Replace a character avatar with JPEG, PNG or WebP bytes.</summary>
        public IEnumerator UploadAvatar(string characterId, byte[] image, string mimeType, Action<AgentResponse> onSuccess, Action<string> onError)
            => Send("PUT", CharacterPath(characterId) + "/avatar", null, onSuccess, onError, 60,
                FileForm("image", image, "avatar" + ImageExtension(mimeType), mimeType));

        /// <summary>Replace a character's model with a binary glTF file. Does not consume generation quota.</summary>
        public IEnumerator UploadModel(string characterId, byte[] glb, Action<AgentResponse> onSuccess, Action<string> onError)
            => Send("PUT", CharacterPath(characterId) + "/model-file", null, onSuccess, onError, 120,
                FileForm("model", glb, "model.glb", "model/gltf-binary"));

        /// <summary>List a character's conversations, with optional player search and message-count filter.</summary>
        public IEnumerator ListConversations(string characterId, Action<ConversationList> onSuccess, Action<string> onError,
            int limit = 50, int offset = 0, string sortBy = "last_activity", string sortOrder = "desc", string playerSearch = null, int? minMessages = null)
            => Send("GET", CharacterPath(characterId) + "/players" + Query("limit", limit, "offset", offset,
                "sortBy", sortBy, "sortOrder", sortOrder, "playerSearch", playerSearch, "minMessages", minMessages), null, onSuccess, onError);

        /// <summary>Get aggregate conversation counts and activity dates.</summary>
        public IEnumerator GetConversationStats(string characterId, Action<ConversationStats> onSuccess, Action<string> onError)
            => Send("GET", CharacterPath(characterId) + "/players/stats", null, onSuccess, onError);

        /// <summary>Get one character/player conversation (404 if it does not exist).</summary>
        public IEnumerator GetConversation(string characterId, string playerId, Action<ConversationRecord> onSuccess, Action<string> onError)
            => Send("GET", PairPath(characterId, playerId), null, onSuccess, onError);

        /// <summary>Page through messages. Pages are newest-first; each page is chronological.</summary>
        public IEnumerator GetMessages(string characterId, string playerId, Action<ConversationMessages> onSuccess, Action<string> onError,
            int limit = 100, int page = 1)
            => Send("GET", PairPath(characterId, playerId) + "/messages" + Query("limit", limit, "page", page), null, onSuccess, onError);

        /// <summary>Permanently delete a conversation, its messages and its memories (204).</summary>
        public IEnumerator DeleteConversation(string characterId, string playerId, Action onSuccess, Action<string> onError)
            => SendNoContent("DELETE", PairPath(characterId, playerId), null, onSuccess, onError);

        /// <summary>List memories for a character/player relationship.</summary>
        public IEnumerator GetMemories(string characterId, string playerId, Action<MemoryList> onSuccess, Action<string> onError,
            int limit = 50, int offset = 0, string memoryType = null, string status = "active")
            => Send("GET", PairPath(characterId, playerId) + "/memories" + Query("limit", limit, "offset", offset,
                "memoryType", memoryType, "status", status), null, onSuccess, onError);

        /// <summary>Group memories by day, week or month within an optional date range.</summary>
        public IEnumerator GetMemoryTimeline(string characterId, string playerId, Action<MemoryTimeline> onSuccess, Action<string> onError,
            string groupBy = "day", string startDate = null, string endDate = null)
            => Send("GET", PairPath(characterId, playerId) + "/memories/timeline" + Query("groupBy", groupBy,
                "startDate", startDate, "endDate", endDate), null, onSuccess, onError);

        /// <summary>Get memory counts by type, status and layer.</summary>
        public IEnumerator GetMemoryStats(string characterId, string playerId, Action<MemoryStats> onSuccess, Action<string> onError)
            => Send("GET", PairPath(characterId, playerId) + "/memories/stats", null, onSuccess, onError);

        /// <summary>Get stable player facts.</summary>
        public IEnumerator GetCoreFacts(string characterId, string playerId, Action<CoreFactsResponse> onSuccess, Action<string> onError)
            => Send("GET", PairPath(characterId, playerId) + "/memories/core-facts", null, onSuccess, onError);

        /// <summary>Get the heterogeneous memory graph; unknown node/edge keys are retained.</summary>
        public IEnumerator GetMemoryGraph(string characterId, string playerId, Action<MemoryGraph> onSuccess, Action<string> onError,
            bool includeEntities = false, bool includeCharacterMemories = false)
            => Send("GET", PairPath(characterId, playerId) + "/memories/graph" + Query("includeEntities", includeEntities,
                "includeCharacterMemories", includeCharacterMemories), null, onSuccess, onError);

        /// <summary>Semantic search uses POST with a JSON body and requires only memories:read.</summary>
        public IEnumerator SearchMemories(string characterId, string playerId, string query, Action<MemorySearchResult> onSuccess, Action<string> onError, int limit = 10)
            => Send("POST", PairPath(characterId, playerId) + "/memories/search", new { query, limit }, onSuccess, onError);

        /// <summary>Create a manual memory, subject to the server's manual-memory quota.</summary>
        public IEnumerator CreateMemory(string characterId, string playerId, MemoryWriteRequest body, Action<MemoryData> onSuccess, Action<string> onError)
            => Send("POST", PairPath(characterId, playerId) + "/memories", body, onSuccess, onError);

        /// <summary>Patch only supplied memory fields.</summary>
        public IEnumerator UpdateMemory(string characterId, string playerId, string memoryId, MemoryWriteRequest body, Action<MemoryData> onSuccess, Action<string> onError)
            => Send("PATCH", PairPath(characterId, playerId) + "/memories/" + Esc(memoryId), body, onSuccess, onError);

        /// <summary>Soft-delete a memory, or permanently remove it with hard=true (204).</summary>
        public IEnumerator DeleteMemory(string characterId, string playerId, string memoryId, Action onSuccess, Action<string> onError, bool hard = false)
            => SendNoContent("DELETE", PairPath(characterId, playerId) + "/memories/" + Esc(memoryId) + Query("hard", hard), null, onSuccess, onError);

        /// <summary>Delete all memories for this pair. The caller must explicitly pass confirm=true.</summary>
        public IEnumerator DeleteAllMemories(string characterId, string playerId, bool confirm, Action<DeletedMemories> onSuccess, Action<string> onError)
            => Send("DELETE", PairPath(characterId, playerId) + "/memories" + Query("confirm", confirm), null, onSuccess, onError);

        /// <summary>Create an expiring or permanent share. Uses Bearer authentication on this route.</summary>
        public IEnumerator CreateShare(ShareCreateRequest body, Action<ShareCreated> onSuccess, Action<string> onError)
            => Send("POST", "/api/v1/share", body, onSuccess, onError, bearerKey: true);

        /// <summary>Redeem a permanent share without forwarding caller credentials. Use SessionToken as the socket API key.</summary>
        public IEnumerator OpenShare(string id, Action<ShareSession> onSuccess, Action<string> onError)
            => Send("POST", "/api/v1/share/" + Esc(id) + "/open", null, onSuccess, onError, anonymous: true);

        /// <summary>Redeem an expiring share without forwarding caller credentials.</summary>
        public IEnumerator ExchangeShare(string token, Action<ShareSession> onSuccess, Action<string> onError, string recipientId = null)
            => Send("POST", "/api/v1/share/" + Esc(token) + "/exchange", new { recipientId }, onSuccess, onError, anonymous: true);

        /// <summary>List owned shares. Requires a Firebase TokenProvider, per the share API.</summary>
        public IEnumerator ListShares(string characterId, Action<ShareList> onSuccess, Action<string> onError)
            => Send("GET", "/api/v1/share/character/" + Esc(characterId), null, onSuccess, onError);

        /// <summary>Revoke a share and end its active sessions. Requires Firebase authentication.</summary>
        public IEnumerator RevokeShare(string id, Action<ShareRevoked> onSuccess, Action<string> onError)
            => Send("DELETE", "/api/v1/share/" + Esc(id), null, onSuccess, onError);

        /// <summary>Revoke shares created with an API key. Requires Firebase authentication.</summary>
        public IEnumerator RevokeSharesByApiKey(string apiKeyId, Action<SharesRevoked> onSuccess, Action<string> onError)
            => Send("DELETE", "/api/v1/share/by-api-key/" + Esc(apiKeyId), null, onSuccess, onError);

        /// <summary>Run one text-only HTTP turn, preserving history for the character/player pair.</summary>
        public IEnumerator Turn(string characterId, CharacterTurnRequest body, Action<CharacterTurnResponse> onSuccess, Action<string> onError)
            => Send("POST", CharacterPath(characterId) + "/turn", body, onSuccess, onError,
                (body.TimeoutSeconds ?? 45) + 15);
    }
}
