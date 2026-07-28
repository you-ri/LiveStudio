using NUnit.Framework;
using Lilium.RemoteControl.RestApi;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// 内側ディスパッチ(<c>DispatchEndpoints</c>)の一致判定 <c>MatchPattern</c> と、
    /// 旧 if/else 連鎖の評価順を宣言表で再現するためのルート順序不変条件を検証する。
    /// HTTP ハンドラを直接叩く自動テストが無いため、ルーティングの中核ロジックを
    /// ここで単体検証する。
    /// </summary>
    [TestFixture]
    public class HandlerRoutingTests
    {
        // MatchPattern は protected static、RouteMatch は protected enum。
        // 派生クラスからのみアクセスできるため、enum をシグネチャに出さない
        // 公開ラッパで露出する。
        private sealed class Probe : BaseRemoteControlApiHandler
        {
            public Probe() : base(null) { }
            public override void Cleanup() { }

            public static bool Exact(string path, string pattern)
                => MatchPattern(path, pattern, RouteMatch.Exact);
            public static bool Prefix(string path, string pattern)
                => MatchPattern(path, pattern, RouteMatch.Prefix);
            public static bool Wildcard(string path, string pattern)
                => MatchPattern(path, pattern, RouteMatch.Wildcard);
        }

        [Test]
        public void Exact_IsCaseInsensitiveEquality()
        {
            Assert.IsTrue(Probe.Exact("/live/objects", "/live/objects"));
            Assert.IsTrue(Probe.Exact("/LIVE/Objects", "/live/objects"));
            Assert.IsFalse(Probe.Exact("/live/object", "/live/objects"));
            Assert.IsFalse(Probe.Exact("/live/objects/1", "/live/objects"));
        }

        [Test]
        public void Prefix_IsCaseInsensitivePrefix()
        {
            Assert.IsTrue(Probe.Prefix("/live/object/123", "/live/object/"));
            Assert.IsTrue(Probe.Prefix("/LIVE/OBJECT/123", "/live/object/"));
            Assert.IsFalse(Probe.Prefix("/live/objects", "/live/object/"));
        }

        [Test]
        public void Wildcard_AsteriskSpansSlashes()
        {
            Assert.IsTrue(Probe.Wildcard("/live/object/123/foo", "/live/object/*/*"));
            Assert.IsTrue(Probe.Wildcard("/LIVE/object/123/foo", "/live/object/*/*"));
        }

        // --- ルート順序不変条件 (旧 if/else 連鎖の評価順を表で再現できる根拠) ---

        [Test]
        public void Get_ObjectsExact_DoesNotCollideWithObjectWildcardOrPrefix()
        {
            // "/live/objects" は Exact 専用。object/*/* にも object/ Prefix にも一致しない。
            Assert.IsTrue(Probe.Exact("/live/objects", "/live/objects"));
            Assert.IsFalse(Probe.Wildcard("/live/objects", "/live/object/*/*"));
            Assert.IsFalse(Probe.Prefix("/live/objects", "/live/object/"));
        }

        [Test]
        public void Get_SingleObject_FallsToPrefixNotPropertyWildcard()
        {
            // プロパティ無し /live/object/{id} は object/*/* に不一致 → Prefix で GetObject。
            Assert.IsFalse(Probe.Wildcard("/live/object/123", "/live/object/*/*"));
            Assert.IsTrue(Probe.Prefix("/live/object/123", "/live/object/"));
            // プロパティ付きは Wildcard 一致 → GetProperty。
            Assert.IsTrue(Probe.Wildcard("/live/object/123/foo", "/live/object/*/*"));
        }

        [Test]
        public void Post_ResetMustBeEvaluatedBeforeAdd()
        {
            // /reset 付きは reset ルートに一致し、かつ add の object/*/* にも一致するため
            // テーブル順で reset を先に置く必要がある(本テストはその前提を固定)。
            Assert.IsTrue(Probe.Wildcard("/live/object/1/prop/reset", "/live/object/*/*/reset"));
            Assert.IsTrue(Probe.Wildcard("/live/object/1/prop/reset", "/live/object/*/*"));
            // /reset 無しは reset ルートに不一致 → add へ。
            Assert.IsFalse(Probe.Wildcard("/live/object/1/prop", "/live/object/*/*/reset"));
            Assert.IsTrue(Probe.Wildcard("/live/object/1/prop", "/live/object/*/*"));
        }

        [Test]
        public void Put_ParentMustBeEvaluatedBeforeSetProperty()
        {
            // @parent は専用ルートと汎用 object/*/* の両方に一致するため
            // テーブル順で @parent を先に置く必要がある。
            Assert.IsTrue(Probe.Wildcard("/live/object/1/@parent", "/live/object/*/@parent"));
            Assert.IsTrue(Probe.Wildcard("/live/object/1/@parent", "/live/object/*/*"));
        }

        [Test]
        public void Scene_ExportImport_AreExactAndDistinct()
        {
            Assert.IsTrue(Probe.Exact("/live/export", "/live/export"));
            Assert.IsTrue(Probe.Exact("/live/import", "/live/import"));
            Assert.IsFalse(Probe.Exact("/live/export", "/live/import"));
        }
    }
}
