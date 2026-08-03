using System.Reflection;
using NUnit.Framework;

namespace Estuary.Tests
{
    /// <summary>
    /// Wire-emission tests for server-side push-to-talk (SDK_CONTRACT v1.11,
    /// SCRUM-220), driven through a fake socket injected via EstuaryClient's
    /// internal test seam. Serializing is JsonUtility's job (not headless-safe);
    /// these tests assert event names + typed payload objects instead.
    /// </summary>
    [TestFixture]
    public class PushToTalkEmissionTests
    {
        private EstuaryClient _client;
        private FakeSocketIOConnection _socket;

        [SetUp]
        public void SetUp()
        {
            _client = new EstuaryClient();
            _socket = new FakeSocketIOConnection();
            _client.AttachSocketForTest(_socket);
        }

        [Test]
        public void AttachSocketForTest_MarksClientConnected()
        {
            Assert.IsTrue(_client.IsConnected,
                "the seam must mark the client Connected so emit paths run");
        }
    }
}
