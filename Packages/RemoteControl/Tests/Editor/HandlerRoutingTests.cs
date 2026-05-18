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
            Assert.IsTrue(Probe.Exact("/exposed/objects", "/exposed/objects"));
            Assert.IsTrue(Probe.Exact("/EXPOSED/Objects", "/exposed/objects"));
            Assert.IsFalse(Probe.Exact("/exposed/object", "/exposed/objects"));
            Assert.IsFalse(Probe.Exact("/exposed/objects/1", "/exposed/objects"));
        }

        [Test]
        public void Prefix_IsCaseInsensitivePrefix()
        {
            Assert.IsTrue(Probe.Prefix("/exposed/object/123", "/exposed/object/"));
            Assert.IsTrue(Probe.Prefix("/EXPOSED/OBJECT/123", "/exposed/object/"));
            Assert.IsFalse(Probe.Prefix("/exposed/objects", "/exposed/object/"));
        }

        [Test]
        public void Wildcard_AsteriskSpansSlashes()
        {
            Assert.IsTrue(Probe.Wildcard("/exposed/object/123/foo", "/exposed/object/*/*"));
            Assert.IsTrue(Probe.Wildcard("/EXPOSED/object/123/foo", "/exposed/object/*/*"));
        }

        // --- ルート順序不変条件 (旧 if/else 連鎖の評価順を表で再現できる根拠) ---

        [Test]
        public void Get_ObjectsExact_DoesNotCollideWithObjectWildcardOrPrefix()
        {
            // "/exposed/objects" は Exact 専用。object/*/* にも object/ Prefix にも一致しない。
            Assert.IsTrue(Probe.Exact("/exposed/objects", "/exposed/objects"));
            Assert.IsFalse(Probe.Wildcard("/exposed/objects", "/exposed/object/*/*"));
            Assert.IsFalse(Probe.Prefix("/exposed/objects", "/exposed/object/"));
        }

        [Test]
        public void Get_SingleObject_FallsToPrefixNotPropertyWildcard()
        {
            // プロパティ無し /exposed/object/{id} は object/*/* に不一致 → Prefix で GetObject。
            Assert.IsFalse(Probe.Wildcard("/exposed/object/123", "/exposed/object/*/*"));
            Assert.IsTrue(Probe.Prefix("/exposed/object/123", "/exposed/object/"));
            // プロパティ付きは Wildcard 一致 → GetProperty。
            Assert.IsTrue(Probe.Wildcard("/exposed/object/123/foo", "/exposed/object/*/*"));
        }

        [Test]
        public void Post_ResetMustBeEvaluatedBeforeAdd()
        {
            // /reset 付きは reset ルートに一致し、かつ add の object/*/* にも一致するため
            // テーブル順で reset を先に置く必要がある(本テストはその前提を固定)。
            Assert.IsTrue(Probe.Wildcard("/exposed/object/1/prop/reset", "/exposed/object/*/*/reset"));
            Assert.IsTrue(Probe.Wildcard("/exposed/object/1/prop/reset", "/exposed/object/*/*"));
            // /reset 無しは reset ルートに不一致 → add へ。
            Assert.IsFalse(Probe.Wildcard("/exposed/object/1/prop", "/exposed/object/*/*/reset"));
            Assert.IsTrue(Probe.Wildcard("/exposed/object/1/prop", "/exposed/object/*/*"));
        }

        [Test]
        public void Put_ParentMustBeEvaluatedBeforeSetProperty()
        {
            // @parent は専用ルートと汎用 object/*/* の両方に一致するため
            // テーブル順で @parent を先に置く必要がある。
            Assert.IsTrue(Probe.Wildcard("/exposed/object/1/@parent", "/exposed/object/*/@parent"));
            Assert.IsTrue(Probe.Wildcard("/exposed/object/1/@parent", "/exposed/object/*/*"));
        }

        [Test]
        public void Scene_ExportImport_AreExactAndDistinct()
        {
            Assert.IsTrue(Probe.Exact("/exposed/export", "/exposed/export"));
            Assert.IsTrue(Probe.Exact("/exposed/import", "/exposed/import"));
            Assert.IsFalse(Probe.Exact("/exposed/export", "/exposed/import"));
        }
    }
}
