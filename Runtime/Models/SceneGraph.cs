using System;
using System.Collections.Generic;
using UnityEngine;

namespace Estuary.Models
{
    /// <summary>
    /// Represents the spatial scene graph from the world model.
    /// Contains entities, relationships, and surfaces detected in the environment.
    /// </summary>
    [Serializable]
    public class SceneGraph
    {
        [SerializeField] private List<SceneEntity> entities = new List<SceneEntity>();
        [SerializeField] private List<SceneRelationship> relationships = new List<SceneRelationship>();
        [SerializeField] private List<SceneSurface> surfaces = new List<SceneSurface>();
        [SerializeField] private string summary;
        [SerializeField] private string locationType;
        [SerializeField] private string userActivity;
        [SerializeField] private List<string> recentEvents = new List<string>();
        [SerializeField] private int entityCount;
        [SerializeField] private string timestamp;

        /// <summary>
        /// List of detected entities (objects) in the scene.
        /// </summary>
        public List<SceneEntity> Entities => entities;

        /// <summary>
        /// List of spatial relationships between entities.
        /// </summary>
        public List<SceneRelationship> Relationships => relationships;

        /// <summary>
        /// List of detected surfaces (tables, floors, walls, etc.).
        /// </summary>
        public List<SceneSurface> Surfaces => surfaces;

        /// <summary>
        /// Natural language summary of the scene.
        /// </summary>
        public string Summary => summary;

        /// <summary>
        /// Type of location (e.g., "indoor", "outdoor", "office", "kitchen").
        /// </summary>
        public string LocationType => locationType;

        /// <summary>
        /// Detected user activity (e.g., "working", "eating", "relaxing").
        /// </summary>
        public string UserActivity => userActivity;

        /// <summary>
        /// List of recent events detected in the scene.
        /// </summary>
        public List<string> RecentEvents => recentEvents;

        /// <summary>
        /// Number of entities in the scene.
        /// </summary>
        public int EntityCount => entityCount;

        /// <summary>
        /// ISO timestamp of when the scene was captured.
        /// </summary>
        public string Timestamp => timestamp;

        /// <summary>
        /// Create SceneGraph from JSON string.
        /// </summary>
        public static SceneGraph FromJson(string json)
        {
            return JsonUtility.FromJson<SceneGraph>(json);
        }

        /// <summary>
        /// Find an entity by its track ID.
        /// </summary>
        public SceneEntity GetEntityByTrackId(int trackId)
        {
            return entities.Find(e => e.TrackId == trackId);
        }

        /// <summary>
        /// Find entities by class name (e.g., "person", "cup", "laptop").
        /// </summary>
        public List<SceneEntity> GetEntitiesByClass(string className)
        {
            return entities.FindAll(e => 
                string.Equals(e.ClassName, className, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Get entities within a certain distance from the user.
        /// </summary>
        public List<SceneEntity> GetEntitiesWithinDistance(float maxDistance)
        {
            return entities.FindAll(e => e.DistanceFromUser <= maxDistance);
        }
    }

    /// <summary>
    /// Represents a detected entity (object) in the scene.
    /// </summary>
    [Serializable]
    public class SceneEntity
    {
        [SerializeField] private string id;
        [SerializeField] private int trackId;
        [SerializeField] private string className;
        [SerializeField] private string label;
        [SerializeField] private float[] position;
        [SerializeField] private float[] bbox2d;
        [SerializeField] private string surface;
        [SerializeField] private float distanceFromUser;
        [SerializeField] private string state;
        [SerializeField] private float confidence;
        [SerializeField] private string firstSeen;
        [SerializeField] private string lastSeen;
        [SerializeField] private int frameCount;

        /// <summary>
        /// Unique identifier for this entity.
        /// </summary>
        public string Id => id;

        /// <summary>
        /// Tracking ID for temporal consistency.
        /// </summary>
        public int TrackId => trackId;

        /// <summary>
        /// Object class (e.g., "person", "cup", "laptop").
        /// </summary>
        public string ClassName => className;

        /// <summary>
        /// Descriptive label (may include VLM enrichment).
        /// </summary>
        public string Label => label;

        /// <summary>
        /// 3D position [x, y, z] in world coordinates.
        /// </summary>
        public Vector3 Position => position != null && position.Length >= 3 
            ? new Vector3(position[0], position[1], position[2]) 
            : Vector3.zero;

        /// <summary>
        /// 2D bounding box [x, y, width, height] in pixel coordinates.
        /// </summary>
        public Rect BoundingBox => bbox2d != null && bbox2d.Length >= 4
            ? new Rect(bbox2d[0], bbox2d[1], bbox2d[2], bbox2d[3])
            : Rect.zero;

        /// <summary>
        /// ID of the surface this entity is on (if any).
        /// </summary>
        public string Surface => surface;

        /// <summary>
        /// Distance from the user/camera in meters.
        /// </summary>
        public float DistanceFromUser => distanceFromUser;

        /// <summary>
        /// Current state (e.g., "stationary", "moving", "interacted").
        /// </summary>
        public string State => state;

        /// <summary>
        /// Detection confidence (0-1).
        /// </summary>
        public float Confidence => confidence;

        /// <summary>
        /// ISO timestamp when first detected.
        /// </summary>
        public string FirstSeen => firstSeen;

        /// <summary>
        /// ISO timestamp of last detection.
        /// </summary>
        public string LastSeen => lastSeen;

        /// <summary>
        /// Number of frames this entity has been tracked.
        /// </summary>
        public int FrameCount => frameCount;
    }

    /// <summary>
    /// Represents a spatial relationship between entities.
    /// </summary>
    [Serializable]
    public class SceneRelationship
    {
        [SerializeField] private string subjectId;
        [SerializeField] private string predicate;
        [SerializeField] private string objectId;
        [SerializeField] private float confidence;

        /// <summary>
        /// ID of the subject entity.
        /// </summary>
        public string SubjectId => subjectId;

        /// <summary>
        /// Relationship predicate (e.g., "on", "next_to", "in_front_of", "holding").
        /// </summary>
        public string Predicate => predicate;

        /// <summary>
        /// ID of the object entity or surface.
        /// </summary>
        public string ObjectId => objectId;

        /// <summary>
        /// Relationship confidence (0-1).
        /// </summary>
        public float Confidence => confidence;

        public override string ToString()
        {
            return $"{SubjectId} {Predicate} {ObjectId}";
        }
    }

    /// <summary>
    /// Represents a detected surface (table, floor, wall, etc.).
    /// </summary>
    [Serializable]
    public class SceneSurface
    {
        [SerializeField] private string id;
        [SerializeField] private string surfaceType;
        [SerializeField] private string label;
        [SerializeField] private float[] center;
        [SerializeField] private float[] normal;
        [SerializeField] private float[] extent;
        [SerializeField] private float confidence;

        /// <summary>
        /// Unique identifier for this surface.
        /// </summary>
        public string Id => id;

        /// <summary>
        /// Surface type (e.g., "table", "floor", "wall", "shelf").
        /// </summary>
        public string SurfaceType => surfaceType;

        /// <summary>
        /// Descriptive label.
        /// </summary>
        public string Label => label;

        /// <summary>
        /// Center point of the surface in world coordinates.
        /// </summary>
        public Vector3 Center => center != null && center.Length >= 3
            ? new Vector3(center[0], center[1], center[2])
            : Vector3.zero;

        /// <summary>
        /// Surface normal vector.
        /// </summary>
        public Vector3 Normal => normal != null && normal.Length >= 3
            ? new Vector3(normal[0], normal[1], normal[2])
            : Vector3.up;

        /// <summary>
        /// Surface extent [width, height] in meters.
        /// </summary>
        public Vector2 Extent => extent != null && extent.Length >= 2
            ? new Vector2(extent[0], extent[1])
            : Vector2.one;

        /// <summary>
        /// Detection confidence (0-1).
        /// </summary>
        public float Confidence => confidence;
    }

    /// <summary>
    /// Scene graph update event data.
    /// </summary>
    [Serializable]
    public class SceneGraphUpdate
    {
        [SerializeField] private string sessionId;
        [SerializeField] private SceneGraph sceneGraph;
        [SerializeField] private string timestamp;

        /// <summary>
        /// Session ID this update belongs to.
        /// </summary>
        public string SessionId => sessionId;

        /// <summary>
        /// Updated scene graph.
        /// </summary>
        public SceneGraph SceneGraph => sceneGraph;

        /// <summary>
        /// ISO timestamp of the update.
        /// </summary>
        public string Timestamp => timestamp;

        /// <summary>
        /// Create SceneGraphUpdate from JSON string.
        /// </summary>
        public static SceneGraphUpdate FromJson(string json)
        {
            return JsonUtility.FromJson<SceneGraphUpdate>(json);
        }
    }

    /// <summary>
    /// Room identification event data.
    /// </summary>
    [Serializable]
    public class RoomIdentified
    {
        [SerializeField] private string sessionId;
        [SerializeField] private string status;
        [SerializeField] private string roomId;
        [SerializeField] private string roomName;

        /// <summary>
        /// Session ID this event belongs to.
        /// </summary>
        public string SessionId => sessionId;

        /// <summary>
        /// Status of room identification ("matched", "unknown", "candidates").
        /// </summary>
        public string Status => status;

        /// <summary>
        /// Identified room ID (if matched).
        /// </summary>
        public string RoomId => roomId;

        /// <summary>
        /// Identified room name (if matched).
        /// </summary>
        public string RoomName => roomName;

        /// <summary>
        /// Create RoomIdentified from JSON string.
        /// </summary>
        public static RoomIdentified FromJson(string json)
        {
            return JsonUtility.FromJson<RoomIdentified>(json);
        }
    }
}
